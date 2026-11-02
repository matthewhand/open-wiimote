using System.Collections.Concurrent;
using System.Text.Json;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Experimental;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using OpenWiimote.Core.Protocol;
using OpenWiimote.Core.Settings;
using OpenWiimote.Core.Virtual;

namespace OpenWiimote.App.Headless;

public sealed class HeadlessWiimoteService : IAsyncDisposable
{
    private const int MaxSnapshotLogEntries = 200;

    private static readonly string[] TiltTargets =
    [
        "LeftStick (Steering + Pitch)",
        "LeftStickX",
        "LeftStickY",
        "RightStickX",
        "RightStickY",
        "LeftTrigger",
        "RightTrigger"
    ];

    private readonly object _gate = new();
    private readonly WiimoteWatcher _watcher;
    private readonly IVirtualGamepadProvider _gamepadProvider;
    private readonly SettingsStore _settingsStore;
    private readonly ProfileStore _profileStore;
    private readonly Dictionary<string, DeviceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ServerLogEntry> _logs = new();
    private readonly string _logFilePath;
    private AppSettings _settings;
    private MappingProfile? _activeProfile;
    private bool _emulationEnabled;
    private bool _rumbleEnabled;
    private string? _emulationError;
    private bool _gamepadAvailable;
    private string? _gamepadProbeError;
    private bool _started;
    private bool _disposed;
    private ServerProfileInfo[]? _profileInfos;
    private string? _profileInfosSignature;

    public HeadlessWiimoteService()
        : this(new WiimoteWatcher(), PlatformBackend.CreateGamepadProvider(),
            SettingsStore.CreateDefault(), ProfileStore.CreateDefault())
    {
    }

    internal HeadlessWiimoteService(WiimoteWatcher watcher, IVirtualGamepadProvider provider,
        SettingsStore settingsStore, ProfileStore profileStore)
    {
        _watcher = watcher;
        _gamepadProvider = provider;
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _settings = _settingsStore.Load();
        _logFilePath = Path.Combine(_settingsStore.LogsDirectory, $"headless_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _activeProfile = _profileStore.LoadProfile(_settings.PreferredProfile) ?? _profileStore.LoadProfile("Default");
        _emulationEnabled = _settings.AutoEnableEmulation;
        _gamepadAvailable = _gamepadProvider.Probe(out var probeError);
        _gamepadProbeError = probeError;
        _watcher.DeviceConnected += OnDeviceConnected;
        _watcher.DeviceDisconnected += OnDeviceDisconnected;
        _watcher.Log += OnWatcherLog;
        AddLog($"OpenWiimote headless service ready on {PlatformBackend.PlatformName}.");
        AddLog($"Virtual controller backend: {_gamepadProvider.BackendName}.");
        if (!_gamepadAvailable)
        {
            AddLog($"{_gamepadProvider.BackendName} probe failed: {_gamepadProbeError}", "Warning");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_emulationEnabled)
        {
            EnableEmulation();
        }

        await _watcher.StartAsync(_settings.ToWatcherOptions(), cancellationToken).ConfigureAwait(false);
        AddLog("Auto-connect watcher started.");
    }

    public ServerSnapshot GetSnapshot()
    {
        DeviceSession[] sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.ToArray();
        }

        var devices = sessions.Select(session =>
        {
            lock (session.Gate)
            {
                var state = session.Connection.Snapshot;
                return new ServerDevice(
                    session.Connection.DeviceKey,
                    session.Connection.ProductName,
                    session.Connection.BluetoothAddress,
                    state,
                    session.Gamepad is not null,
                    state.TiltXDegrees,
                    state.TiltYDegrees,
                    state.GyroX,
                    state.GyroY,
                    state.GyroZ,
                    session.Profile?.Name,
                    session.Connection.LedMask);
            }
        }).ToArray();

        var mappings = _activeProfile?.AllMappings.Select(ToServerMapping).ToArray() ?? [];
        var settings = CopySettings(_settings);
        var logs = _logs.ToArray();
        if (logs.Length > MaxSnapshotLogEntries)
        {
            logs = logs[^MaxSnapshotLogEntries..];
        }

        return new ServerSnapshot(
            PlatformBackend.PlatformName,
            PlatformBackend.BluetoothHint,
            _gamepadProvider.BackendName,
            _gamepadProvider.BackendDescription,
            _gamepadAvailable,
            _gamepadProbeError,
            _emulationEnabled,
            _emulationError,
            _rumbleEnabled,
            settings,
            _profileStore.GetProfileNames(),
            GetProfileInfos(),
            _activeProfile?.Name,
            _activeProfile?.Description,
            _profileStore.Directory,
            ServerTiltSettings.From(_activeProfile?.AccelMapping),
            mappings,
            devices,
            logs,
            _activeProfile?.ExtensionStick ?? new ExtensionStickMapping());
    }

    public async Task ScanAsync() => await _watcher.ScanOnceAsync().ConfigureAwait(false);

    public async Task DisconnectAllAsync()
    {
        await _watcher.StopAsync().ConfigureAwait(false);
        await _watcher.StartAsync(_settings.ToWatcherOptions()).ConfigureAwait(false);
        AddLog("All Wiimotes disconnected. Watcher restarted.");
    }

    public void SetEmulation(bool enabled)
    {
        if (enabled)
        {
            EnableEmulation();
            return;
        }

        _emulationEnabled = false;
        _emulationError = null;
        foreach (var session in GetSessions())
        {
            IVirtualGamepad? gamepad;
            lock (session.Gate)
            {
                gamepad = session.Gamepad;
                session.Gamepad = null;
            }

            ReleaseGamepad(gamepad);
        }

        AddLog("Virtual controller emulation disabled.");
    }

    public void SetProfile(string name)
    {
        var profile = _profileStore.LoadProfile(name)
            ?? throw new ArgumentException($"Profile '{name}' was not found.", nameof(name));
        _activeProfile = profile;
        _profileInfos = null;
        _profileInfosSignature = null;
        _settings.PreferredProfile = profile.Name;
        SaveSettings();
        ApplyProfile(profile);
        AddLog($"Profile set to '{profile.Name}'.");
    }

    public void DeleteProfile(string name)
    {
        var profile = _profileStore.LoadProfile(name)
            ?? throw new ArgumentException($"Profile '{name}' was not found.", nameof(name));
        _profileStore.DeleteProfile(profile.Name);
        _settings.UpdateProfileReferences(profile.Name, null);
        SaveSettings();
        _profileInfos = null;
        _profileInfosSignature = null;
        if (string.Equals(_activeProfile?.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
            _activeProfile = _profileStore.LoadProfile("Default");
        if (_activeProfile is not null) ApplyProfile(_activeProfile);
        AddLog($"Profile '{name}' deleted.");
    }

    /// <summary>
    /// Points one remote at a profile of its own, or back at the active profile when the name
    /// is empty. The choice is part of the settings, so it survives a restart, and a remote
    /// that is already connected starts using it on its next report.
    /// </summary>
    public void AssignDeviceProfile(string deviceKey, string? profileName)
    {
        var key = (deviceKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            throw new ArgumentException("A device key is required.", nameof(deviceKey));
        }

        var map = _settings.DeviceProfiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(profileName))
        {
            map.Remove(key);
            SaveSettings();
            AddLog($"{ApplyDeviceProfile(key)} follows the active profile again.");
            return;
        }

        var profile = _profileStore.LoadProfile(profileName)
            ?? throw new ArgumentException($"Profile '{profileName}' was not found.", nameof(profileName));

        map[key] = profile.Name;
        SaveSettings();
        AddLog($"Profile '{profile.Name}' assigned to {ApplyDeviceProfile(key)}.");
    }

    /// <summary>
    /// Re-resolves the profile of the connected remote with this device key and returns the
    /// remote as the log describes it, or the key itself while no such remote is connected.
    /// </summary>
    private string ApplyDeviceProfile(string deviceKey)
    {
        var described = deviceKey;

        foreach (var session in GetSessions())
        {
            if (!string.Equals(session.Connection.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            described = session.Connection.ToString() ?? deviceKey;

            IVirtualGamepad? gamepad;
            MappingProfile? profile;
            lock (session.Gate)
            {
                session.Profile = ResolveProfileForDevice(session.Connection);
                profile = session.Profile;
                gamepad = session.Gamepad;
            }

            if (gamepad is not null && profile is not null)
            {
                Submit(session, session.Connection.Snapshot);
            }
        }

        return described;
    }

    /// <summary>
    /// Creates a profile and makes it the active one. Without a name the next free name
    /// based on "New profile" is used, and without <paramref name="copyOf"/> the profile
    /// starts from the default layout.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is taken or the source profile does not exist.
    /// </exception>
    public void CreateProfile(string? name, string? copyOf)
    {
        var profileName = string.IsNullOrWhiteSpace(name)
            ? _profileStore.NextAvailableProfileName("New profile")
            : name.Trim();

        _profileStore.CreateProfile(profileName, copyOf);
        _profileInfos = null;
        _profileInfosSignature = null;
        SetProfile(profileName);
        AddLog($"Profile '{profileName}' created.");
    }

    public void RenameProfile(string name, string newName)
    {
        if (!_profileStore.RenameProfile(name, newName))
        {
            throw new ArgumentException($"Profile '{name}' was not found.", nameof(name));
        }

        var trimmed = newName.Trim();

        // The active profile is a detached copy of the stored one, so keep its name in step.
        if (_activeProfile is not null &&
            string.Equals(_activeProfile.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfile.Name = trimmed;
        }

        _settings.UpdateProfileReferences(name, trimmed);
        SaveSettings();
        if (_activeProfile is not null) ApplyProfile(_activeProfile);

        _profileInfos = null;
        _profileInfosSignature = null;
        AddLog($"Profile '{name}' renamed to '{trimmed}'.");
    }

    /// <summary>
    /// Validates headless tilt tuning before changing or saving the active profile, then
    /// applies the same bounded values accepted by the UI and desktop editor.
    /// </summary>
    public void UpdateTiltSettings(TiltRequest request)
    {
        var profile = _activeProfile
                      ?? throw new ArgumentException("No profile is selected.", nameof(request));
        // Preserve the existing API defaults for older clients omitting these numbers.
        var maxTiltDegrees = request.MaxTiltDegrees == 0f ? 25f : request.MaxTiltDegrees;
        var sensitivity = request.Sensitivity == 0f ? 1f : request.Sensitivity;
        if (!float.IsFinite(maxTiltDegrees) ||
            maxTiltDegrees < 5f || maxTiltDegrees > 80f)
            throw new ArgumentException("Max tilt must be 5–80 degrees.", nameof(request));
        if (!float.IsFinite(sensitivity) ||
            sensitivity < 0.1f || sensitivity > 3f)
            throw new ArgumentException("Sensitivity must be 0.1–3.", nameof(request));
        if (!float.IsFinite(request.DeadZone) ||
            request.DeadZone < 0f || request.DeadZone > 0.5f)
            throw new ArgumentException("Dead zone must be 0–0.5.", nameof(request));

        // Stage edits on a detached mapping. The live profile must remain unchanged
        // if the profile store cannot write its temporary file or replace the original.
        var previousMapping = profile.AccelMapping;
        var previousUseAccelerometer = profile.UseAccelerometer;
        var previousModifiedAt = profile.ModifiedAt;
        var previousVersion = profile.Version;
        var mapping = previousMapping is null
            ? new AccelerometerMapping()
            : JsonSerializer.Deserialize<AccelerometerMapping>(JsonSerializer.Serialize(previousMapping))
              ?? throw new InvalidOperationException("Could not copy the active tilt settings.");
        mapping.TiltSteeringEnabled = request.Enabled;
        mapping.TargetControl = NormalizeTiltTarget(request.Target);
        mapping.TiltAxis = NormalizeTiltAxis(request.Axis);
        mapping.MaxTiltDegrees = maxTiltDegrees;
        mapping.Sensitivity = sensitivity;
        mapping.DeadZone = request.DeadZone;
        mapping.InvertSteering = request.InvertSteering;
        mapping.InvertPitch = request.InvertPitch;
        mapping.InvertAxis = request.InvertAxis;
        // Omitted by legacy tilt-only clients: leave the current gyro setting intact.
        if (request.GyroEnabled is { } gyroEnabled)
            mapping.GyroEnabled = gyroEnabled;
        profile.AccelMapping = mapping;
        profile.UseAccelerometer = mapping.TiltSteeringEnabled;

        try
        {
            _profileStore.SaveProfile(profile);
        }
        catch (Exception ex)
        {
            profile.AccelMapping = previousMapping!;
            profile.UseAccelerometer = previousUseAccelerometer;
            profile.ModifiedAt = previousModifiedAt;
            profile.Version = previousVersion;
            AddLog($"Could not save the profile: {ex.Message}", "Error");
            throw new InvalidOperationException($"Could not save profile '{profile.Name}': {ex.Message}", ex);
        }

        _profileInfos = null;
        _profileInfosSignature = null;
        ApplyProfile(profile);
        AddLog($"Tilt steering updated for profile '{profile.Name}'.");
    }

    public void ClearLogs()
    {
        while (_logs.TryDequeue(out _))
        {
        }
    }

    private static string NormalizeTiltTarget(string? target)
    {
        // Retain the old default for omitted selectors, but reject unrecognised names.
        if (string.IsNullOrWhiteSpace(target)) return TiltTargets[0];
        var normalized = target.Trim();
        return TiltTargets.FirstOrDefault(candidate =>
            string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown tilt target.", nameof(target));
    }

    private static string NormalizeTiltAxis(string? axis)
    {
        // Older clients that omit the selector still get X.
        return axis?.Trim().ToUpperInvariant() switch
        {
            null or "" or "X" => "X",
            "Y" => "Y",
            "Z" => "Z",
            _ => throw new ArgumentException("Unknown tilt axis.", nameof(axis))
        };
    }

    public void SetLedMask(int mask, string? deviceKey = null)
    {
        var led = mask & 0xF0;
        if (!string.IsNullOrWhiteSpace(deviceKey))
        {
            var session = GetSessions().FirstOrDefault(candidate =>
                string.Equals(candidate.Connection.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                return;
            }

            session.Connection.SetLedMask((byte)led);
            AddLog($"Player LEDs for {session.Connection.BluetoothAddress ?? session.Connection.DeviceKey} set to 0x{led:X2}.");
            return;
        }

        _settings.LedMask = led;
        foreach (var session in GetSessions())
        {
            session.Connection.SetLedMask((byte)_settings.LedMask);
        }

        SaveSettings();
        AddLog($"Player LED mask set to 0x{_settings.LedMask:X2}.");
    }

    public void SetRumble(bool enabled)
    {
        _rumbleEnabled = enabled;
        foreach (var session in GetSessions())
        {
            session.Connection.SetRumble(enabled);
        }

        AddLog(enabled ? "Rumble enabled." : "Rumble disabled.");
    }

    public void UpdateSettings(SettingsRequest request)
    {
        _settings.AutoConnect = request.AutoConnect;
        _settings.AutoEnableEmulation = request.AutoEnableEmulation;
        _settings.PollIntervalMs = request.PollIntervalMs;
        _settings.ReconnectDelayMs = request.ReconnectDelayMs;
        _settings.StaleTimeoutMs = request.StaleTimeoutMs;
        _settings.StatusRequestIntervalMs = request.StatusRequestIntervalMs;
        _settings.AvatarTheme = string.Equals(request.AvatarTheme, "screwdriver", StringComparison.OrdinalIgnoreCase)
            ? "screwdriver"
            : "wiimote";
        _settings.AvatarRainEnabled = request.AvatarRainEnabled;
        _settings.SparklineEnabled = request.SparklineEnabled;
        _settings.KeyboardShortcutsEnabled = request.KeyboardShortcutsEnabled;
        _settings.RumbleShakeEnabled = request.RumbleShakeEnabled;
        _settings.RumbleOnButtonPress = request.RumbleOnButtonPress;
        _settings.BatterySegmentsEnabled = request.BatterySegmentsEnabled;
        var irChanged = _settings.IrEnabled != request.IrEnabled;
        _settings.IrEnabled = request.IrEnabled;
        var normalized = _settings.ToWatcherOptions();
        _settings.PollIntervalMs = normalized.PollIntervalMs;
        _settings.ReconnectDelayMs = normalized.ReconnectDelayMs;
        _settings.StaleTimeoutMs = normalized.StaleTimeoutMs;
        _settings.StatusRequestIntervalMs = normalized.StatusRequestIntervalMs;
        _watcher.UpdateOptions(normalized);
        SaveSettings();

        if (irChanged)
        {
            foreach (var session in GetSessions())
            {
                session.Connection.SetIrEnabled(_settings.IrEnabled);
            }

            AddLog(_settings.IrEnabled ? "IR camera switched on." : "IR camera switched off.");
        }

        if (_settings.AutoEnableEmulation && !_emulationEnabled)
        {
            EnableEmulation();
        }

        AddLog("Settings saved.");
    }

    /// <summary>
    /// Updates a mapping in the active profile and applies it to connected remotes.
    /// </summary>
    public void UpdateMapping(MappingRequest request)
    {
        if (request is null)
        {
            throw new ArgumentException("A JSON mapping body is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            throw new ArgumentException("A target is required.", nameof(request));
        }

        var profile = _activeProfile
                      ?? throw new ArgumentException("No profile is selected.", nameof(request));

        _profileStore.UpdateMapping(profile.Name, request.Target, request.Button);
        // Reload to get the updated profile instance
        _activeProfile = _profileStore.LoadProfile(profile.Name) ?? profile;
        _profileInfos = null;
        _profileInfosSignature = null;
        ApplyProfile(_activeProfile);
        AddLog($"Mapping updated for profile '{_activeProfile.Name}'.");
    }

    public void UpdateExtensionMapping(ExtensionMappingRequest request)
    {
        if (request is null)
        {
            throw new ArgumentException("A JSON extension mapping body is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            throw new ArgumentException("A target is required.", nameof(request));
        }

        var profile = _activeProfile
                      ?? throw new ArgumentException("No profile is selected.", nameof(request));

        _profileStore.UpdateExtensionMapping(profile.Name, request.Target, request.NunchukButton, request.ClassicButton);
        _activeProfile = _profileStore.LoadProfile(profile.Name) ?? profile;
        _profileInfos = null;
        _profileInfosSignature = null;
        ApplyProfile(_activeProfile);
        AddLog($"Extension mapping updated for profile '{_activeProfile.Name}'.");
    }

    public void UpdateExtensionStick(ExtensionStickRequest request)
    {
        if (request is null)
        {
            throw new ArgumentException("A JSON extension stick body is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new ArgumentException("A source is required.", nameof(request));
        }

        // Validate every supplied tuning value before changing the target mapping. The
        // headless API must reject the same out-of-range values as its browser editor,
        // without silently clamping them or partially saving the request.
        if (request.DeadZone is { } deadZone &&
            (!float.IsFinite(deadZone) || deadZone < 0f || deadZone > 0.95f))
        {
            throw new ArgumentException("Stick dead zone must be between 0 and 0.95.", nameof(request));
        }

        if (request.Sensitivity is { } sensitivity &&
            (!float.IsFinite(sensitivity) || sensitivity < 0.1f || sensitivity > 3f))
        {
            throw new ArgumentException("Stick sensitivity must be between 0.1 and 3.", nameof(request));
        }

        var activeProfile = _activeProfile
            ?? throw new ArgumentException("No profile is selected.", nameof(request));
        // Work on a detached profile. A failed disk write must not leave the active
        // in-memory profile changed, and target plus tuning must be committed together.
        var profile = _profileStore.LoadProfile(activeProfile.Name)
            ?? throw new ArgumentException($"Profile '{activeProfile.Name}' was not found.", nameof(request));

        var normalizedTarget = string.IsNullOrWhiteSpace(request.Target)
            ? OpenWiimote.Core.Models.ExtensionStickMapping.UnmappedTarget
            : OpenWiimote.Core.Models.ExtensionStickMapping.Targets.FirstOrDefault(candidate =>
                string.Equals(candidate, request.Target.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Stick target '{request.Target}' is not valid.", nameof(request));

        var stick = profile.ExtensionStick ??= new OpenWiimote.Core.Models.ExtensionStickMapping();
        switch (request.Source.Trim().ToLowerInvariant())
        {
            case "nunchuk":
                stick.NunchukTarget = normalizedTarget;
                break;
            case "classicleft":
                stick.ClassicLeftTarget = normalizedTarget;
                break;
            case "classicright" or "classicr":
                stick.ClassicRightTarget = normalizedTarget;
                break;
            default:
                throw new ArgumentException($"Stick source '{request.Source}' is not valid.", nameof(request));
        }

        if (request.DeadZone is { } validatedDeadZone) stick.DeadZone = validatedDeadZone;
        if (request.Sensitivity is { } validatedSensitivity) stick.Sensitivity = validatedSensitivity;
        if (request.InvertY is { } invertY) stick.InvertY = invertY;

        try
        {
            // One write persists the target and all optional tuning fields atomically.
            _profileStore.SaveProfile(profile);
        }
        catch (Exception ex)
        {
            AddLog($"Could not save the profile: {ex.Message}", "Error");
            throw new InvalidOperationException($"Could not save profile '{profile.Name}': {ex.Message}", ex);
        }

        _activeProfile = profile;
        _profileInfos = null;
        _profileInfosSignature = null;
        ApplyProfile(profile);
        AddLog($"Extension stick mapping updated for profile '{profile.Name}'.");
    }

    public IReadOnlyList<string> GetProfileNames() => _profileStore.GetProfileNames();

    /// <summary>
    /// Per profile details for the dashboard profile cards. Cached until the profile
    /// table changes or a reload is requested, so the per poll snapshot does not hit disk.
    /// </summary>
    private ServerProfileInfo[] GetProfileInfos()
    {
        var names = _profileStore.GetProfileNames();
        var signature = string.Join("|", names) + "::" + (_activeProfile?.Name ?? "");
        if (_profileInfos is not null && signature == _profileInfosSignature)
        {
            return _profileInfos;
        }

        _profileInfosSignature = signature;
        _profileInfos = names.Select(ToProfileInfo).ToArray();
        return _profileInfos;
    }

    private static ServerMapping ToServerMapping(ControlMapping mapping) => new(
        mapping.TargetName,
        mapping.WiimoteButton?.ToString() ?? "Unmapped",
        mapping.NunchukButton?.ToString(),
        mapping.ClassicButton?.ToString());

    private ServerProfileInfo ToProfileInfo(string name)
    {
        var profile = _profileStore.LoadProfile(name);
        return new ServerProfileInfo(
            name,
            profile?.Description,
            profile?.UseAccelerometer ?? false,
            profile?.AccelMapping?.TargetControl ?? "",
            profile?.AccelMapping?.TiltAxis ?? "",
            profile?.IconEmoji ?? "🎮",
            profile?.AllMappings.Select(ToServerMapping).ToArray() ?? []);
    }

    /// <summary>
    /// Re-reads the active profile from disk, so hand edited profiles show up without a
    /// restart, and returns the refreshed snapshot to the caller.
    /// </summary>
    public ServerSnapshot ReloadProfiles()
    {
        var profile = _profileStore.LoadProfile(_activeProfile?.Name ?? _settings.PreferredProfile)
                      ?? _profileStore.LoadProfile(_settings.PreferredProfile)
                      ?? _profileStore.LoadProfile("Default");
        if (profile is not null)
        {
            _activeProfile = profile;
            _profileInfos = null;
            _profileInfosSignature = null;
            ApplyProfile(profile);
            AddLog($"Profile '{profile.Name}' reloaded from disk.");
        }

        return GetSnapshot();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.DeviceConnected -= OnDeviceConnected;
        _watcher.DeviceDisconnected -= OnDeviceDisconnected;
        _watcher.Log -= OnWatcherLog;
        foreach (var session in GetSessions())
        {
            session.Connection.SnapshotUpdated -= OnSnapshotUpdated;
            lock (session.Gate)
            {
                session.Retired = true;
                ReleaseGamepad(session.Gamepad);
                session.Gamepad = null;
            }
        }

        await _watcher.DisposeAsync().ConfigureAwait(false);
        _gamepadProvider.Dispose();
        SaveSettings();
    }

    private bool EnableEmulation()
    {
        _gamepadAvailable = _gamepadProvider.Probe(out var error);
        _gamepadProbeError = error;
        if (!_gamepadAvailable)
        {
            SetEmulation(false);
            _emulationError = error ?? $"{_gamepadProvider.BackendName} was not found.";
            AddLog($"Cannot enable emulation: {_emulationError}", "Error");
            return false;
        }

        _emulationEnabled = true;
        _emulationError = null;
        foreach (var session in GetSessions())
        {
            AttachGamepad(session);
        }

        SaveSettings();
        AddLog($"Virtual controller emulation enabled through {_gamepadProvider.BackendName}.");
        return true;
    }

    private void AttachGamepad(DeviceSession session)
    {
        lock (session.Gate)
        {
            if (session.Gamepad is not null)
            {
                return;
            }
        }

        if (!_gamepadProvider.TryCreate(out var gamepad, out var error) || gamepad is null)
        {
            _emulationError = error ?? "Unable to create virtual controller.";
            AddLog($"Unable to create virtual controller: {_emulationError}", "Error");
            return;
        }

        lock (session.Gate)
        {
            if (session.Retired || _disposed || !_emulationEnabled || session.Gamepad is not null)
            {
                ReleaseGamepad(gamepad);
                return;
            }
            session.Gamepad = gamepad;
            session.Profile = ResolveProfileForDevice(session.Connection);
            Submit(session, session.Connection.Snapshot);
            if (session.Gamepad is null) return;
        }

        AddLog($"Virtual controller ready for {session.Connection.ProductName}.", "Success");
    }

    private MappingProfile? ResolveProfileForDevice(WiimoteConnection connection)
    {
        if (_settings.DeviceProfiles != null)
        {
            if (_settings.DeviceProfiles.TryGetValue(connection.DeviceKey, out var profileByKey))
            {
                var byKey = _profileStore.LoadProfile(profileByKey);
                if (byKey != null) return byKey;
            }
            if (!string.IsNullOrWhiteSpace(connection.BluetoothAddress) &&
                _settings.DeviceProfiles.TryGetValue(connection.BluetoothAddress, out var profileByAddr))
            {
                var byAddr = _profileStore.LoadProfile(profileByAddr);
                if (byAddr != null) return byAddr;
            }
        }
        return _activeProfile;
    }

    internal void OnDeviceConnected(object? sender, WiimoteConnection connection)
    {
        var session = new DeviceSession(connection) { Profile = ResolveProfileForDevice(connection) };
        connection.SnapshotUpdated += OnSnapshotUpdated;

        DeviceSession? replaced;
        lock (_gate)
        {
            _sessions.Remove(connection.DeviceKey, out replaced);
            session.PlayerNumber = NextPlayerNumber(_sessions.Values);
            _sessions[connection.DeviceKey] = session;
        }

        if (replaced is not null)
        {
            // A duplicate connect event would otherwise leak the previous virtual controller.
            replaced.Connection.SnapshotUpdated -= OnSnapshotUpdated;
            lock (replaced.Gate)
            {
                replaced.Retired = true;
                ReleaseGamepad(replaced.Gamepad);
                replaced.Gamepad = null;
            }
        }

        connection.SetLedMask((byte)(WiimoteProtocol.Led1 << (session.PlayerNumber - 1)));
        connection.SetRumble(_rumbleEnabled);
        if (_emulationEnabled)
        {
            AttachGamepad(session);
        }

        AddLog($"Connected to {connection}.", "Success");
    }

    internal void OnDeviceDisconnected(object? sender, WiimoteConnection connection)
    {
        DeviceSession? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(connection.DeviceKey, out session) ||
                !ReferenceEquals(session.Connection, connection)) return;
            _sessions.Remove(connection.DeviceKey);
        }

        if (session is not null)
        {
            connection.SnapshotUpdated -= OnSnapshotUpdated;
            lock (session.Gate)
            {
                session.Retired = true;
                ReleaseGamepad(session.Gamepad);
                session.Gamepad = null;
            }

            AddLog($"Disconnected from {connection.ProductName}.", "Warning");
        }
    }

    private void OnWatcherLog(object? sender, string message) => AddLog(message);

    internal void OnSnapshotUpdated(object? sender, WiimoteState state)
    {
        if (sender is not WiimoteConnection connection)
        {
            return;
        }

        DeviceSession? session;
        lock (_gate)
        {
            _sessions.TryGetValue(connection.DeviceKey, out session);
        }

        if (session is null || !ReferenceEquals(session.Connection, connection))
        {
            return;
        }

        lock (session.Gate)
        {
            if (session.Retired) return;
            if (_settings.RumbleOnButtonPress && state.Buttons != session.LastButtons && state.Buttons != WiimoteButton.None)
                _ = PulseRumbleAsync(session);
            session.LastButtons = state.Buttons;
            if (session.Gamepad is not null && state.BatteryPercent >= 0 && state.BatteryPercent != session.LastBatteryPercent)
            {
                session.LastBatteryPercent = state.BatteryPercent;
                try { session.Gamepad.SetBattery(state.BatteryPercent); } catch { }
            }
            Submit(session, state);
        }
    }

    private async Task PulseRumbleAsync(DeviceSession session)
    {
        try
        {
            session.Connection.SetRumble(true);
            await Task.Delay(150).ConfigureAwait(false);
            lock (session.Gate)
            {
                if (!session.Retired && !_disposed) session.Connection.SetRumble(_rumbleEnabled);
            }
        }
        catch { }
    }

    private void ApplyProfile(MappingProfile profile)
    {
        foreach (var session in GetSessions())
        {
            IVirtualGamepad? gamepad;
            lock (session.Gate)
            {
                session.Profile = ResolveProfileForDevice(session.Connection);
                gamepad = session.Gamepad;
            }

            if (gamepad is not null)
            {
                Submit(session, session.Connection.Snapshot);
            }
        }
    }

    private DeviceSession[] GetSessions()
    {
        lock (_gate)
        {
            return _sessions.Values.ToArray();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            AddLog($"Could not save settings: {ex.Message}", "Warning");
        }
    }

    private void AddLog(string message, string level = "Info")
    {
        var entry = new ServerLogEntry(DateTimeOffset.Now, message, level);
        _logs.Enqueue(entry);
        while (_logs.Count > 500 && _logs.TryDequeue(out _))
        {
        }

        try
        {
            File.AppendAllText(_logFilePath, $"[{entry.Timestamp:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void Submit(DeviceSession session, WiimoteState state)
    {
        lock (session.Gate)
        {
            if (session.Retired || !_emulationEnabled || session.Gamepad is null || session.Profile is null) return;
            try
            {
                session.Gamepad.Submit(ProfileMapper.Map(session.Profile, state));
            }
            catch (Exception ex)
            {
                var failed = session.Gamepad;
                session.Gamepad = null;
                ReleaseGamepad(failed);
                _emulationError = "Controller output failed. Disable and re-enable emulation to retry.";
                AddLog($"{_emulationError} {ex.Message}", "Error");
            }
        }
    }

    private static void ReleaseGamepad(IVirtualGamepad? gamepad)
    {
        if (gamepad is null) return;
        try { gamepad.Submit(XboxGamepadState.Neutral); } catch { }
        try { gamepad.Dispose(); } catch { }
    }

    private static AppSettings CopySettings(AppSettings settings) => new()
    {
        AutoConnect = settings.AutoConnect,
        AutoEnableEmulation = settings.AutoEnableEmulation,
        PollIntervalMs = settings.PollIntervalMs,
        ReconnectDelayMs = settings.ReconnectDelayMs,
        StaleTimeoutMs = settings.StaleTimeoutMs,
        StatusRequestIntervalMs = settings.StatusRequestIntervalMs,
        LedMask = settings.LedMask,
        PreferredProfile = settings.PreferredProfile,
        DeviceProfiles = settings.DeviceProfiles != null
            ? new Dictionary<string, string>(settings.DeviceProfiles, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        AvatarTheme = settings.AvatarTheme,
        AvatarRainEnabled = settings.AvatarRainEnabled,
        SparklineEnabled = settings.SparklineEnabled,
        KeyboardShortcutsEnabled = settings.KeyboardShortcutsEnabled,
        RumbleShakeEnabled = settings.RumbleShakeEnabled,
        RumbleOnButtonPress = settings.RumbleOnButtonPress,
        BatterySegmentsEnabled = settings.BatterySegmentsEnabled,
        IrEnabled = settings.IrEnabled
    };

    private sealed class DeviceSession(WiimoteConnection connection)
    {
        public WiimoteConnection Connection { get; } = connection;
        public object Gate { get; } = new();
        public IVirtualGamepad? Gamepad { get; set; }
        public MappingProfile? Profile { get; set; }
        public WiimoteButton LastButtons { get; set; }
        public int LastBatteryPercent { get; set; } = -1;
        public int PlayerNumber { get; set; }
        public bool Retired { get; set; }
    }

    /// <summary>The lowest unused player slot, so the second remote lights LED 2.</summary>
    private static int NextPlayerNumber(IEnumerable<DeviceSession> sessions)
    {
        var used = sessions.Select(session => session.PlayerNumber).ToHashSet();
        for (var player = 1; player <= 4; player++)
        {
            if (!used.Contains(player))
            {
                return player;
            }
        }

        return 4;
    }
}

public sealed record ServerLogEntry(DateTimeOffset Timestamp, string Message, string Level);
public sealed record ServerProfileInfo(
    string Name,
    string? Description,
    bool TiltSteeringEnabled,
    string TiltTarget,
    string TiltAxis,
    string IconEmoji,
    IReadOnlyList<ServerMapping> Mappings);

public sealed record ServerMapping(
    string Target,
    string Button,
    string? Nunchuk = null,
    string? Classic = null);
public sealed record ServerDevice(
    string Key,
    string Name,
    string? BluetoothAddress,
    WiimoteState State,
    bool VirtualControllerAttached,
    double TiltXDegrees,
    double TiltYDegrees,
    short GyroX,
    short GyroY,
    short GyroZ,
    string? PreferredProfile = null,
    int LedMask = 0);
public sealed record ServerSnapshot(
    string Platform,
    string BluetoothHint,
    string GamepadBackend,
    string GamepadDescription,
    bool GamepadAvailable,
    string? GamepadError,
    bool EmulationEnabled,
    string? EmulationError,
    bool RumbleEnabled,
    AppSettings Settings,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<ServerProfileInfo> ProfileInfos,
    string? ActiveProfile,
    string? ProfileDescription,
    string ProfileDirectory,
    ServerTiltSettings Tilt,
    IReadOnlyList<ServerMapping> Mappings,
    IReadOnlyList<ServerDevice> Devices,
    IReadOnlyList<ServerLogEntry> Logs,
    ExtensionStickMapping ExtensionStick)
{
    public ExperimentalFeatureStatus MotionPlus { get; } = new(
        ExperimentalMotionPlus.Status, ExperimentalMotionPlus.Available, ExperimentalMotionPlus.Description);
}

public sealed record ExperimentalFeatureStatus(string Status, bool Available, string Description);

public sealed record ServerTiltSettings(
    bool Enabled,
    string Target,
    string Axis,
    float MaxTiltDegrees,
    float Sensitivity,
    float DeadZone,
    bool InvertSteering,
    bool InvertPitch,
    bool InvertAxis,
    bool GyroEnabled = false,
    string GyroAxis = "Z",
    float GyroDeadZone = 0f,
    float MaxGyroDegreesPerSecond = 200f,
    float GyroSensitivity = 1f,
    bool InvertGyro = false)
{
    public static ServerTiltSettings From(AccelerometerMapping? mapping)
    {
        mapping ??= new AccelerometerMapping();
        return new ServerTiltSettings(
            mapping.TiltSteeringEnabled,
            mapping.TargetControl,
            mapping.TiltAxis,
            mapping.MaxTiltDegrees,
            mapping.Sensitivity,
            mapping.DeadZone,
            mapping.InvertSteering,
            mapping.InvertPitch,
            mapping.InvertAxis,
            mapping.GyroEnabled,
            string.IsNullOrWhiteSpace(mapping.GyroAxis) ? "Z" : mapping.GyroAxis.Trim().ToUpperInvariant(),
            mapping.GyroDeadZone,
            mapping.MaxGyroDegreesPerSecond,
            mapping.GyroSensitivity,
            mapping.InvertGyro);
    }
}

public sealed record BooleanRequest(bool Enabled);
public sealed record IntegerRequest(int Value, string? DeviceKey = null);
public sealed record ProfileRequest(string Name, bool Delete = false, string? RenameTo = null);
public sealed record DeviceProfileRequest(string DeviceKey, string? Profile = null);

public sealed record CreateProfileRequest(string? Name = null, string? CopyOf = null);
public sealed record AccessRequest(string Token);
public sealed record TiltRequest(
    bool Enabled,
    string Target,
    string Axis,
    float MaxTiltDegrees,
    float Sensitivity,
    float DeadZone,
    bool InvertSteering,
    bool InvertPitch,
    bool InvertAxis,
    bool? GyroEnabled = null,
    string? GyroAxis = null,
    float GyroDeadZone = 0f,
    float MaxGyroDegreesPerSecond = 200f,
    float GyroSensitivity = 1f,
    bool InvertGyro = false);
public sealed record MappingRequest(string Target, string? Button);
public sealed record ExtensionMappingRequest(string Target, string? NunchukButton, string? ClassicButton);
public sealed record ExtensionStickRequest(string Source, string? Target, float? DeadZone = null, float? Sensitivity = null, bool? InvertY = null);
public sealed record SettingsRequest(
    bool AutoConnect,
    bool AutoEnableEmulation,
    int PollIntervalMs,
    int ReconnectDelayMs,
    int StaleTimeoutMs,
    int StatusRequestIntervalMs,
    string AvatarTheme,
    bool AvatarRainEnabled = true,
    bool SparklineEnabled = true,
    bool KeyboardShortcutsEnabled = true,
    bool RumbleShakeEnabled = true,
    bool RumbleOnButtonPress = false,
    bool BatterySegmentsEnabled = true,
    bool IrEnabled = false);
