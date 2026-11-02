using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using OpenWiimote.Core.Protocol;
using OpenWiimote.Core.Settings;
using OpenWiimote.Core.Virtual;

namespace OpenWiimote.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8FA3C8"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#6C9CFF"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#4DDC9A"));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#FF7186"));

    private readonly SettingsStore _settingsStore;
    private readonly ProfileStore _profileStore;
    private readonly string _logFilePath;
    private readonly object _logFileGate = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logWriter = new();
    private System.Threading.Timer? _logTimer;
    private readonly WiimoteWatcher _watcher;
    private readonly IVirtualGamepadProvider _gamepadProvider;
    private readonly object _sessionsGate = new();
    private readonly Dictionary<string, DeviceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = new();
    private MappingProfile? _activeProfile;
    private DeviceViewModel? _selectedDevice;
    private string? _selectedProfile;
    private string? _pendingDeleteProfile;
    private string _statusText = "Starting";
    private IBrush _statusBrush = MutedBrush;
    private string? _emulationError;
    private bool _emulationEnabled;
    private bool _suppressUiEvents;
    private volatile bool _closing;
    private volatile bool _rumbleEnabled;

    public MainWindow()
        : this(new WiimoteWatcher(), PlatformBackend.CreateGamepadProvider(),
            SettingsStore.CreateDefault(), ProfileStore.CreateDefault())
    {
    }

    internal MainWindow(WiimoteWatcher watcher, IVirtualGamepadProvider provider,
        SettingsStore settingsStore, ProfileStore profileStore)
    {
        InitializeComponent();

        _watcher = watcher;
        _gamepadProvider = provider;
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _settings = _settingsStore.Load();
        _logFilePath = Path.Combine(
            _settingsStore.LogsDirectory,
            $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        _watcher.DeviceConnected += OnWatcherDeviceConnected;
        _watcher.DeviceDisconnected += OnWatcherDeviceDisconnected;
        _watcher.Log += OnWatcherLog;

        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DeviceCountText));
        AvatarThemes.Add(new AvatarThemeOption(Controls.WiimoteAvatar.WiimoteTheme, "Original Wiimote"));
        AvatarThemes.Add(new AvatarThemeOption(Controls.WiimoteAvatar.ScrewdriverTheme, "Screwdriver (third-party)"));

        ProfileComboBox.ItemsSource = Profiles;
        ThemeComboBox.ItemsSource = AvatarThemes;
        ProfileFolderText.Text = _profileStore.Directory;
        PlatformText.Text = PlatformBackend.PlatformName;
        BluetoothHintText.Text = PlatformBackend.BluetoothHint;
        GamepadBackendText.Text = $"{_gamepadProvider.BackendName}: {_gamepadProvider.BackendDescription}";
        EmptyDetails.IsVisible = true;
        DetailsPanel.IsVisible = false;

        DataContext = this;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Bound in code: the details panel carries the selected remote as its data
        // context, so the window's own profile list is not reachable from a binding
        // that sits inside that panel.
        DeviceProfileComboBox.ItemsSource = DeviceProfileOptions;
        _logTimer = new System.Threading.Timer(_ => DrainLogWriter(), null, 250, 250);
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<string> Profiles { get; } = [];
    public ObservableCollection<ProfileMappingRow> ProfileMappings { get; } = [];
    public ObservableCollection<AvatarThemeOption> AvatarThemes { get; } = [];

    /// <summary>The choices of the per-remote profile picker: the active profile, then the rest.</summary>
    public ObservableCollection<string> DeviceProfileOptions { get; } = [];

    /// <summary>The choice that means the remote has no profile of its own.</summary>
    private const string ActiveProfileOption = "Use the active profile";

    public string DeviceCountText => Devices.Count == 1 ? "1 connected" : $"{Devices.Count} connected";

    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
            var visible = value is not null;
            EmptyDetails.IsVisible = !visible;
            DetailsPanel.IsVisible = visible;
            Avatar.SetState(value?.State ?? new WiimoteState());
            ExtensionAvatarControl.SetState(value?.State ?? new WiimoteState());
            Avatar.SetLedMask((byte)_settings.LedMask);
            Avatar.SetBatteryPercent(value?.State.BatteryPercent ?? -1);
            UpdateDeviceProfileCombo(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        private set
        {
            if (ReferenceEquals(_statusBrush, value))
            {
                return;
            }

            _statusBrush = value;
            OnPropertyChanged();
        }
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _suppressUiEvents = true;
        AutoConnectCheckBox.IsChecked = _settings.AutoConnect;
        AutoEnableEmulationCheckBox.IsChecked = _settings.AutoEnableEmulation;
        IrEnabledCheckBox.IsChecked = _settings.IrEnabled;
        PollIntervalBox.Text = _settings.PollIntervalMs.ToString();
        ReconnectDelayBox.Text = _settings.ReconnectDelayMs.ToString();
        StaleTimeoutBox.Text = _settings.StaleTimeoutMs.ToString();
        StatusIntervalBox.Text = _settings.StatusRequestIntervalMs.ToString();

        var theme = AvatarThemes.FirstOrDefault(option =>
            string.Equals(option.Key, _settings.AvatarTheme, StringComparison.OrdinalIgnoreCase));
        ThemeComboBox.SelectedItem = theme ?? AvatarThemes[0];
        Avatar.SetTheme((theme ?? AvatarThemes[0]).Key);
        UpdateLedButtons();
        _suppressUiEvents = false;

        LoadProfiles();
        UpdateHeaderStatus();
        AddLog($"OpenWiimote ready on {PlatformBackend.PlatformName}.");
        AddLog($"Virtual controller backend: {_gamepadProvider.BackendName}.");

        if (_settings.AutoEnableEmulation)
        {
            SetEmulationToggle(true);
        }

        ProbeGamepadBackend();

        try
        {
            await _watcher.StartAsync(_settings.ToWatcherOptions());
            AddLog("Auto-connect watcher started.");
        }
        catch (Exception ex)
        {
            AddLog($"Unable to start watcher: {ex.Message}", "Error");
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _logTimer?.Dispose();
        _logTimer = null;
        DrainLogWriter();
        _watcher.DeviceConnected -= OnWatcherDeviceConnected;
        _watcher.DeviceDisconnected -= OnWatcherDeviceDisconnected;
        _watcher.Log -= OnWatcherLog;

        DeviceSession[] sessions;
        lock (_sessionsGate)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            if (session.SnapshotHandler is not null)
            {
                session.Connection.SnapshotUpdated -= session.SnapshotHandler;
            }

            IVirtualGamepad? gamepad;
            lock (session.Gate)
            {
                gamepad = session.Gamepad;
                session.Gamepad = null;
            }

            ReleaseGamepad(gamepad);
        }

        try
        {
            _watcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _gamepadProvider.Dispose();

        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
        }
    }

    private void OnWatcherDeviceConnected(object? sender, WiimoteConnection connection)
    {
        Dispatch(() => HandleDeviceConnected(connection));
    }

    private void OnWatcherDeviceDisconnected(object? sender, WiimoteConnection connection)
    {
        Dispatch(() => HandleDeviceDisconnected(connection));
    }

    private void OnWatcherLog(object? sender, string message)
    {
        var level = message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("could not", StringComparison.OrdinalIgnoreCase)
            ? "Warning"
            : "Info";
        AddLog(message, level);
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

    internal void HandleDeviceConnected(WiimoteConnection connection)
    {
        if (_closing)
        {
            return;
        }

        DeviceSession? replaced;
        lock (_sessionsGate) _sessions.TryGetValue(connection.DeviceKey, out replaced);
        if (replaced is not null)
        {
            if (ReferenceEquals(replaced.Connection, connection)) return;
            HandleDeviceDisconnected(replaced.Connection);
        }

        var viewModel = new DeviceViewModel(connection)
        {
            PinnedProfile = PinnedProfileName(connection.DeviceKey)
        };
        var session = new DeviceSession(connection, viewModel)
        {
            Profile = ResolveProfileForDevice(connection)
        };
        session.SnapshotHandler = (_, state) => HandleDeviceSnapshot(connection, state);
        connection.SnapshotUpdated += session.SnapshotHandler;

        lock (_sessionsGate)
        {
            _sessions[connection.DeviceKey] = session;
        }

        viewModel.Apply(connection.Snapshot);
        Devices.Insert(0, viewModel);
        SelectedDevice ??= viewModel;

        if (ReferenceEquals(SelectedDevice, viewModel))
        {
            UpdateAvatar(viewModel);
        }

        AddLog($"Connected to {connection}.", "Success");

        if (_emulationEnabled)
        {
            TryAttachGamepad(session, log: true);
        }

        if (RumbleToggle.IsChecked == true)
        {
            connection.SetRumble(true);
        }

        UpdateHeaderStatus();
        UpdateEmulationStatus();
        Avatar.SetLedMask((byte)_settings.LedMask);
    }

    internal void HandleDeviceDisconnected(WiimoteConnection connection)
    {
        DeviceSession? session;
        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(connection.DeviceKey, out session) ||
                !ReferenceEquals(session.Connection, connection)) return;
            _sessions.Remove(connection.DeviceKey);
        }

        if (session is null)
        {
            return;
        }

        if (session.SnapshotHandler is not null)
        {
            connection.SnapshotUpdated -= session.SnapshotHandler;
        }

        IVirtualGamepad? gamepad;
        lock (session.Gate)
        {
            session.Retired = true;
            gamepad = session.Gamepad;
            session.Gamepad = null;
        }

        ReleaseGamepad(gamepad);
        session.ViewModel.MarkDisconnected();
        Devices.Remove(session.ViewModel);

        if (ReferenceEquals(SelectedDevice, session.ViewModel))
        {
            SelectedDevice = Devices.FirstOrDefault();
        }

        AddLog($"Disconnected from {session.ViewModel.DisplayName}.", "Warning");
        UpdateHeaderStatus();
        UpdateEmulationStatus();
    }

    internal void HandleDeviceSnapshot(WiimoteConnection connection, WiimoteState state)
    {
        DeviceSession? session;
        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(connection.DeviceKey, out session) ||
                !ReferenceEquals(session.Connection, connection)) return;
        }
        OnSnapshot(session, state);
    }

    private void OnSnapshot(DeviceSession session, WiimoteState state)
    {
        lock (session.Gate)
        {
            if (session.Retired || _closing) return;
            if (_settings.RumbleOnButtonPress && state.Buttons != session.LastButtons && state.Buttons != WiimoteButton.None)
                _ = PulseRumbleAsync(session);
            session.LastButtons = state.Buttons;
            if (session.Gamepad is not null && state.BatteryPercent >= 0 && state.BatteryPercent != session.LastForwardedBattery)
            {
                session.LastForwardedBattery = state.BatteryPercent;
                try { session.Gamepad.SetBattery(state.BatteryPercent); } catch { }
            }
            Submit(session, state);
        }

        var updateUi = false;
        var logBattery = false;
        var now = DateTimeOffset.UtcNow;
        lock (session.Gate)
        {
            if (now - session.LastUiUpdate >= TimeSpan.FromMilliseconds(33))
            {
                session.LastUiUpdate = now;
                updateUi = true;
            }

            if (state.BatteryPercent >= 0 && state.BatteryPercent != session.LastLoggedBattery)
            {
                session.LastLoggedBattery = state.BatteryPercent;
                logBattery = true;
            }
        }

        if (updateUi)
        {
            Dispatch(() =>
            {
                if (_closing || session.Retired) return;
                session.ViewModel.Apply(state);
                if (ReferenceEquals(SelectedDevice, session.ViewModel))
                {
                    UpdateAvatar(session.ViewModel);
                }
            });
        }

        if (logBattery)
        {
            Dispatch(() => AddLog(
                $"Battery on {session.ViewModel.DisplayName}: {state.BatteryPercent}% (raw 0x{state.BatteryRaw:X2})."));
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
                if (!_closing && !session.Retired) session.Connection.SetRumble(_rumbleEnabled);
            }
        }
        catch { }
    }

    private void UpdateAvatar(DeviceViewModel viewModel)
    {
        Avatar.SetState(viewModel.State);
        ExtensionAvatarControl.SetState(viewModel.State);
        Avatar.SetLedMask((byte)_settings.LedMask);
        Avatar.SetBatteryPercent(viewModel.State.BatteryPercent);
    }

    private void ProbeGamepadBackend()
    {
        if (_gamepadProvider.Probe(out var error))
        {
            _emulationError = null;
            UpdateEmulationStatus();
            return;
        }

        _emulationError = error ?? $"{_gamepadProvider.BackendName} was not found.";
        SetEmulationStatus(
            $"{_gamepadProvider.BackendName} unavailable. {_emulationError}",
            DangerBrush);
        AddLog($"{_gamepadProvider.BackendName} probe failed: {_emulationError}", "Warning");
    }

    internal bool EnableEmulation()
    {
        if (!_gamepadProvider.Probe(out var error))
        {
            DisableEmulation();
            _emulationError = error ?? $"{_gamepadProvider.BackendName} was not found.";
            SetEmulationStatus(
                $"{_gamepadProvider.BackendName} unavailable. {_emulationError}",
                DangerBrush);
            AddLog($"Cannot enable emulation: {_emulationError}", "Error");
            return false;
        }

        _emulationEnabled = true;
        _emulationError = null;

        foreach (var session in GetSessions())
        {
            TryAttachGamepad(session, log: true);
        }

        UpdateEmulationStatus();
        AddLog($"Virtual controller emulation enabled through {_gamepadProvider.BackendName}.");
        return true;
    }

    private void DisableEmulation()
    {
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

        SetEmulationStatus("Off", MutedBrush);
        AddLog("Virtual controller emulation disabled.");
    }

    private bool TryAttachGamepad(DeviceSession session, bool log)
    {
        lock (session.Gate)
        {
            if (session.Gamepad is not null)
            {
                return true;
            }
        }

        if (!_gamepadProvider.TryCreate(out var gamepad, out var error) || gamepad is null)
        {
            AddLog($"Unable to create virtual controller: {error}", "Error");
            return false;
        }

        lock (session.Gate)
        {
            if (session.Retired || _closing || !_emulationEnabled || session.Gamepad is not null)
            {
                ReleaseGamepad(gamepad);
                return session.Gamepad is not null;
            }

            session.Gamepad = gamepad;
            session.Profile ??= _activeProfile;
        }

        var profile = session.Profile ?? _activeProfile;
        if (profile is not null)
        {
            Submit(session, session.Connection.Snapshot);
            lock (session.Gate) { if (session.Gamepad is null) return false; }
        }

        if (log)
        {
            AddLog($"Virtual controller ready for {session.ViewModel.DisplayName}.", "Success");
        }

        return true;
    }

    private static void ReleaseGamepad(IVirtualGamepad? gamepad)
    {
        if (gamepad is null) return;
        try { gamepad.Submit(XboxGamepadState.Neutral); } catch { }
        try { gamepad.Dispose(); } catch { }
    }

    private void Submit(DeviceSession session, WiimoteState state)
    {
        lock (session.Gate)
        {
            if (session.Retired || _closing || !_emulationEnabled || session.Gamepad is null || session.Profile is null) return;
            try { session.Gamepad.Submit(ProfileMapper.Map(session.Profile, state)); }
            catch (Exception ex)
            {
                var failed = session.Gamepad;
                session.Gamepad = null;
                ReleaseGamepad(failed);
                _emulationError = "Controller output failed. Disable and re-enable emulation to retry.";
                AddLog($"{_emulationError} {ex.Message}", "Error");
                Dispatch(UpdateEmulationStatus);
            }
        }
    }

    private DeviceSession[] GetSessions()
    {
        lock (_sessionsGate)
        {
            return _sessions.Values.ToArray();
        }
    }

    private void EmulationToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (EmulationToggle.IsChecked == true)
        {
            if (!EnableEmulation())
            {
                _suppressUiEvents = true;
                EmulationToggle.IsChecked = false;
                _suppressUiEvents = false;
            }
        }
        else
        {
            DisableEmulation();
        }
    }

    private void SetEmulationToggle(bool enabled)
    {
        _suppressUiEvents = true;
        EmulationToggle.IsChecked = enabled;
        _suppressUiEvents = false;

        if (enabled && !EnableEmulation())
        {
            _suppressUiEvents = true;
            EmulationToggle.IsChecked = false;
            _suppressUiEvents = false;
        }
        else if (!enabled)
        {
            DisableEmulation();
        }
    }

    private void UpdateEmulationStatus()
    {
        if (!_emulationEnabled)
        {
            SetEmulationStatus("Off", MutedBrush);
            return;
        }

        if (_emulationError is not null)
        {
            SetEmulationStatus($"{_gamepadProvider.BackendName} unavailable. {_emulationError}", DangerBrush);
            return;
        }

        var active = GetSessions().Count(session =>
        {
            lock (session.Gate)
            {
                return session.Gamepad is not null;
            }
        });

        if (active == 0)
        {
            SetEmulationStatus("Ready - waiting for a Wiimote.", AccentBrush);
        }
        else
        {
            SetEmulationStatus(
                active == 1 ? "Active - 1 virtual controller." : $"Active - {active} virtual controllers.",
                SuccessBrush);
        }
    }

    private void SetEmulationStatus(string text, IBrush brush)
    {
        EmulationStatusText.Text = text;
        EmulationStatusText.Foreground = brush;
    }

    private void LoadProfiles()
    {
        var names = _profileStore.GetProfileNames();
        ResetPendingProfileDelete();
        _suppressUiEvents = true;
        Profiles.Clear();
        foreach (var name in names)
        {
            Profiles.Add(name);
        }

        DeviceProfileOptions.Clear();
        DeviceProfileOptions.Add(ActiveProfileOption);
        foreach (var name in names)
        {
            DeviceProfileOptions.Add(name);
        }

        var preferred = Profiles.Contains(_settings.PreferredProfile, StringComparer.OrdinalIgnoreCase)
            ? _settings.PreferredProfile
            : Profiles.FirstOrDefault();

        ProfileComboBox.SelectedItem = preferred;
        _suppressUiEvents = false;

        _selectedProfile = preferred;
        _activeProfile = _profileStore.LoadProfile(preferred);
        UpdateProfileDetails();
        ApplyProfileToSessions();

        // A profile that was deleted or renamed takes the pin of every remote that used it
        // with it, so the list is refreshed as a whole.
        foreach (var device in Devices)
        {
            device.PinnedProfile = PinnedProfileName(device.DeviceKey);
        }

        UpdateDeviceProfileCombo(SelectedDevice);
    }

    private void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (ProfileComboBox.SelectedItem is not string name || string.IsNullOrWhiteSpace(name) || name == _selectedProfile)
        {
            return;
        }

        ResetPendingProfileDelete();
        _selectedProfile = name;
        _settings.PreferredProfile = name;
        _activeProfile = _profileStore.LoadProfile(name);
        _settingsStore.Save(_settings);
        UpdateProfileDetails();
        ApplyProfileToSessions();
        AddLog($"Profile set to '{name}'.");
    }

    private void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (ThemeComboBox.SelectedItem is not AvatarThemeOption option)
        {
            return;
        }

        _settings.AvatarTheme = option.Key;
        Avatar.SetTheme(option.Key);
        _settingsStore.Save(_settings);
    }

    private void DeviceProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || SelectedDevice is not { } viewModel || DeviceProfileComboBox.SelectedItem is not string choice)
        {
            return;
        }

        var key = viewModel.DeviceKey;
        if (choice == ActiveProfileOption)
        {
            _settings.DeviceProfiles.Remove(key);
        }
        else
        {
            _settings.DeviceProfiles[key] = choice;
        }

        _settingsStore.Save(_settings);
        viewModel.PinnedProfile = choice == ActiveProfileOption ? null : choice;
        ApplyDeviceProfile(key);

        AddLog(choice == ActiveProfileOption
            ? $"{viewModel.DisplayName} follows the active profile again."
            : $"Profile '{choice}' assigned to {viewModel.DisplayName}.");
    }

    /// <summary>
    /// The profile a remote is pinned to, or null when it follows the active profile. A name
    /// that is no longer on disk counts as no pin, which is what the remote then uses.
    /// </summary>
    private string? PinnedProfileName(string deviceKey)
    {
        if (_settings.DeviceProfiles is null ||
            !_settings.DeviceProfiles.TryGetValue(deviceKey, out var name) ||
            string.IsNullOrWhiteSpace(name) ||
            _profileStore.LoadProfile(name) is null)
        {
            return null;
        }

        return name;
    }

    /// <summary>Shows the profile the selected remote is pinned to, if one is still on disk.</summary>
    private void UpdateDeviceProfileCombo(DeviceViewModel? viewModel)
    {
        var assigned = viewModel is null ? null : PinnedProfileName(viewModel.DeviceKey);

        if (viewModel is not null)
        {
            viewModel.PinnedProfile = assigned;
        }

        _suppressUiEvents = true;
        DeviceProfileComboBox.SelectedItem = assigned ?? ActiveProfileOption;
        _suppressUiEvents = false;
    }

    /// <summary>Hands a new profile to one remote, leaving the other remotes alone.</summary>
    private void ApplyDeviceProfile(string deviceKey)
    {
        DeviceSession? session;
        lock (_sessionsGate)
        {
            _sessions.TryGetValue(deviceKey, out session);
        }

        if (session is null)
        {
            return;
        }

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

    private void ApplyProfileToSessions()
    {
        foreach (var session in GetSessions())
        {
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
    }

    private void UpdateProfileDetails()
    {
        var profile = _activeProfile;
        ProfileNameBox.Text = profile?.Name ?? string.Empty;
        ProfileSummaryText.Text = profile?.Description ?? "No profile selected.";
        ProfileMappings.Clear();

        if (profile is null)
        {
            return;
        }

        foreach (var mapping in profile.AllMappings)
        {
            ProfileMappings.Add(new ProfileMappingRow(
                mapping.TargetName,
                Describe(mapping.WiimoteButton),
                Describe(mapping.NunchukButton),
                Describe(mapping.ClassicButton)));
        }

        // Update combo boxes after items are added
        Dispatcher.UIThread.Post(() => SetMappingComboBoxValues(), DispatcherPriority.Background);

        LoadExtensionStickSettings(profile);
        LoadTiltSettings(profile);
    }

    private static string Describe<TEnum>(TEnum? value) where TEnum : struct, Enum
    {
        return value is null || EqualityComparer<TEnum>.Default.Equals(value.Value, default)
            ? "Unmapped"
            : value.Value.ToString();
    }

    private void SetMappingComboBoxValues()
    {
        if (_activeProfile is null) return;
        var mappings = _activeProfile.AllMappings
            .ToDictionary(m => m.TargetName, StringComparer.OrdinalIgnoreCase);

        // Walk the visual tree to find all mapping combo boxes
        FindAndSetMappingCombos(this, mappings);
    }

    private void FindAndSetMappingCombos(Control control, Dictionary<string, ControlMapping> mappings)
    {
        if (control is ComboBox combo &&
            combo.Tag is string target &&
            combo.Name is "MappingComboBox" or "NunchukMappingComboBox" or "ClassicMappingComboBox")
        {
            ApplyMappingSelection(combo, target, mappings);
            return;
        }

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control c)
                {
                    FindAndSetMappingCombos(c, mappings);
                }
            }
        }
        else if (control is ContentControl cc)
        {
            if (cc.Content is Control ccChild)
            {
                FindAndSetMappingCombos(ccChild, mappings);
            }
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            FindAndSetMappingCombos(decoratorChild, mappings);
        }
        else if (control is ItemsControl itemsControl)
        {
            foreach (var child in itemsControl.GetRealizedContainers())
            {
                if (child is Control container)
                {
                    FindAndSetMappingCombos(container, mappings);
                }
            }
        }
    }

    /// <summary>
    /// A mapping combo box is filled as soon as its row is created, because the rows of an
    /// items control are not part of the visual tree when the profile is applied.
    /// </summary>
    private void MappingComboBox_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo ||
            combo.Tag is not string target ||
            _activeProfile is null)
        {
            return;
        }

        var mappings = _activeProfile.AllMappings
            .ToDictionary(m => m.TargetName, StringComparer.OrdinalIgnoreCase);
        ApplyMappingSelection(combo, target, mappings);
    }

    private void ApplyMappingSelection(
        ComboBox combo,
        string target,
        Dictionary<string, ControlMapping> mappings)
    {
        mappings.TryGetValue(target, out var mapping);
        var button = combo.Name switch
        {
            "NunchukMappingComboBox" => Describe(mapping?.NunchukButton),
            "ClassicMappingComboBox" => Describe(mapping?.ClassicButton),
            _ => Describe(mapping?.WiimoteButton)
        };

        _suppressUiEvents = true;
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Content?.ToString(), button, StringComparison.OrdinalIgnoreCase));
        if (combo.SelectedItem == null)
        {
            combo.SelectedIndex = 0;
        }
        _suppressUiEvents = false;
    }

    private static string? SelectedContent(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
    }

    /// <summary>
    /// Saves the Nunchuk and Classic Controller buttons of a mapping. The combo boxes of
    /// one row are saved together, because a mapping holds one button of each family.
    /// </summary>
    private void ExtensionMappingComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        if (sender is not ComboBox comboBox) return;
        if (comboBox.Tag is not string target) return;
        if (_activeProfile is null) return;

        var nunchuk = FindSiblingComboBox(comboBox, "NunchukMappingComboBox");
        var classic = FindSiblingComboBox(comboBox, "ClassicMappingComboBox");

        try
        {
            _profileStore.UpdateExtensionMapping(
                _activeProfile.Name,
                target,
                nunchuk is null ? null : SelectedContent(nunchuk),
                classic is null ? null : SelectedContent(classic));
            _activeProfile = _profileStore.LoadProfile(_activeProfile.Name) ?? _activeProfile;
            ApplyProfileToSessions();
            AddLog($"Extension mapping for {target} updated.");
        }
        catch (Exception ex)
        {
            AddLog($"Could not update the extension mapping: {ex.Message}", "Error");
            Dispatcher.UIThread.Post(() => UpdateProfileDetails(), DispatcherPriority.Background);
        }
    }

    private static ComboBox? FindSiblingComboBox(Control child, string name)
    {
        if (child.Parent is not Panel parent)
        {
            return null;
        }

        return parent.Children.OfType<ComboBox>().FirstOrDefault(candidate => candidate.Name == name);
    }

    private void ExtensionStickComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        if (sender is not ComboBox comboBox) return;
        if (_activeProfile is null) return;

        var source = comboBox.Name switch
        {
            "NunchukStickComboBox" => "Nunchuk",
            "ClassicLeftStickComboBox" => "ClassicLeft",
            "ClassicRightStickComboBox" => "ClassicRight",
            _ => null
        };

        if (source is null) return;

        try
        {
            _profileStore.UpdateExtensionStick(_activeProfile.Name, source, SelectedContent(comboBox));
            _activeProfile = _profileStore.LoadProfile(_activeProfile.Name) ?? _activeProfile;
            ApplyProfileToSessions();
            AddLog($"Extension stick mapping for {source} updated.");
        }
        catch (Exception ex)
        {
            AddLog($"Could not update the extension stick mapping: {ex.Message}", "Error");
            Dispatcher.UIThread.Post(() => UpdateProfileDetails(), DispatcherPriority.Background);
        }
    }

    /// <summary>Fills the extension stick controls with the values of the profile.</summary>
    private void LoadExtensionStickSettings(MappingProfile profile)
    {
        var mapping = profile.ExtensionStick ?? new ExtensionStickMapping();
        var suppressed = _suppressUiEvents;
        _suppressUiEvents = true;
        SelectComboItem(NunchukStickComboBox, mapping.NunchukTarget);
        SelectComboItem(ClassicLeftStickComboBox, mapping.ClassicLeftTarget);
        SelectComboItem(ClassicRightStickComboBox, mapping.ClassicRightTarget);
        _suppressUiEvents = suppressed;
    }

    private static void SelectComboItem(ComboBox comboBox, string? value)
    {
        var item = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate =>
            string.Equals(candidate.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedItem = item ?? comboBox.Items.OfType<ComboBoxItem>().LastOrDefault();
    }

    private ComboBox? FindComboBox(Control control)
    {
        if (control is ComboBox cb && cb.Name == "MappingComboBox") return cb;
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control c)
                {
                    var result = FindComboBox(c);
                    if (result != null) return result;
                }
            }
        }
        if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            return FindComboBox(ccChild);
        }
        return null;
    }

    private void MappingComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        if (sender is not ComboBox comboBox) return;
        if (comboBox.Tag is not string target) return;
        if (_activeProfile is null) return;

        var button = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unmapped";
        if (string.Equals(button, "Unmapped", StringComparison.OrdinalIgnoreCase))
        {
            button = null;
        }

        try
        {
            _profileStore.UpdateMapping(_activeProfile.Name, target, button);
            _activeProfile = _profileStore.LoadProfile(_activeProfile.Name);
            ApplyProfileToSessions();
            AddLog($"Mapping for {target} updated.");
        }
        catch (Exception ex)
        {
            AddLog($"Could not update mapping: {ex.Message}", "Error");
            // Revert
            Dispatcher.UIThread.Post(() => UpdateProfileDetails(), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Fills the tilt controls with the values of the selected profile.
    /// </summary>
    private void LoadTiltSettings(MappingProfile profile)
    {
        var mapping = profile.AccelMapping ?? new AccelerometerMapping();

        var suppressed = _suppressUiEvents;
        _suppressUiEvents = true;
        TiltSteeringCheckBox.IsChecked = mapping.TiltSteeringEnabled;
        TiltTargetComboBox.SelectedItem = FindComboItem(TiltTargetComboBox, mapping.TargetControl);
        TiltAxisComboBox.SelectedItem = FindComboItem(TiltAxisComboBox, mapping.TiltAxis);
        MaxTiltBox.Value = (decimal)Math.Clamp(mapping.MaxTiltDegrees, 5f, 80f);
        SensitivityBox.Value = (decimal)Math.Clamp(mapping.Sensitivity <= 0 ? 1f : mapping.Sensitivity, 0.1f, 3f);
        DeadZoneBox.Value = (decimal)Math.Clamp(mapping.DeadZone, 0f, 0.5f);
        InvertSteeringCheckBox.IsChecked = mapping.InvertSteering;
        InvertPitchCheckBox.IsChecked = mapping.InvertPitch;
        InvertAxisCheckBox.IsChecked = mapping.InvertAxis;
        _suppressUiEvents = suppressed;
    }

    private static ComboBoxItem? FindComboItem(ComboBox comboBox, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private void TiltSteeringCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        ApplyTiltSettings(save: true);
    }

    private void SaveTiltSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplyTiltSettings(save: true);
    }

    /// <summary>
    /// Copies the values of the tilt controls into the selected profile, saves the profile
    /// and hands the updated profile to the connected remotes.
    /// </summary>
    private void ApplyTiltSettings(bool save)
    {
        if (_activeProfile is null)
        {
            AddLog("No profile selected, tilt settings were not applied.", "Warning");
            return;
        }

        var mapping = _activeProfile.AccelMapping ?? new AccelerometerMapping();
        mapping.TiltSteeringEnabled = TiltSteeringCheckBox.IsChecked == true;
        mapping.TargetControl = (TiltTargetComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                ?? "LeftStick (Steering + Pitch)";
        mapping.TiltAxis = (TiltAxisComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "X";
        mapping.MaxTiltDegrees = (float)(MaxTiltBox.Value ?? 25m);
        mapping.Sensitivity = (float)(SensitivityBox.Value ?? 1m);
        mapping.DeadZone = (float)(DeadZoneBox.Value ?? 0.05m);
        mapping.InvertSteering = InvertSteeringCheckBox.IsChecked == true;
        mapping.InvertPitch = InvertPitchCheckBox.IsChecked == true;
        mapping.InvertAxis = InvertAxisCheckBox.IsChecked == true;

        _activeProfile.AccelMapping = mapping;
        _activeProfile.UseAccelerometer = mapping.TiltSteeringEnabled;

        if (save)
        {
            try
            {
                _profileStore.SaveProfile(_activeProfile);
            }
            catch (Exception ex)
            {
                AddLog($"Could not save the profile: {ex.Message}", "Error");
                return;
            }
        }

        ApplyProfileToSessions();
        AddLog($"Tilt steering updated for profile '{_activeProfile.Name}'.");
    }

    private void ReloadProfilesButton_Click(object? sender, RoutedEventArgs e)
    {
        LoadProfiles();
        AddLog("Profiles reloaded from disk.");
    }

    /// <summary>
    /// Adds a copy of the selected profile under the next free "New profile" name and
    /// selects it, so the name box is ready for a rename.
    /// </summary>
    private void NewProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var name = _profileStore.NextAvailableProfileName("New profile");
            _profileStore.CreateProfile(name, _selectedProfile);
            _settings.PreferredProfile = name;
            _settingsStore.Save(_settings);
            LoadProfiles();
            AddLog($"Profile '{name}' created.");
        }
        catch (ArgumentException ex)
        {
            AddLog($"Could not create the profile: {ex.Message}", "Error");
        }
    }

    /// <summary>
    /// Renames the selected profile to the name typed in the box next to it. The profile
    /// keeps its mappings and tilt settings; only the file and the display name change.
    /// </summary>
    private void RenameProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        var current = _selectedProfile;
        if (string.IsNullOrWhiteSpace(current))
        {
            AddLog("Select a profile to rename.", "Warning");
            return;
        }

        var newName = ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            AddLog("Type the new profile name in the box first.", "Warning");
            return;
        }

        if (string.Equals(newName, current, StringComparison.Ordinal))
        {
            AddLog($"Profile '{current}' already has that name.", "Warning");
            return;
        }

        try
        {
            if (!_profileStore.RenameProfile(current, newName))
            {
                AddLog($"Profile '{current}' was not found.", "Warning");
                return;
            }
        }
        catch (ArgumentException ex)
        {
            AddLog($"Could not rename the profile: {ex.Message}", "Error");
            return;
        }

        _settings.UpdateProfileReferences(current, newName);
        _settingsStore.Save(_settings);

        LoadProfiles();
        AddLog($"Profile '{current}' renamed to '{newName}'.");
    }

    /// <summary>
    /// Deletes the selected profile. The first click arms the button and the second one
    /// deletes, so a profile is never removed by a single stray click.
    /// </summary>
    private void DeleteProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        var current = _selectedProfile;
        if (string.IsNullOrWhiteSpace(current))
        {
            AddLog("Select a profile to delete.", "Warning");
            return;
        }

        if (string.Equals(current, "Default", StringComparison.OrdinalIgnoreCase))
        {
            AddLog("The Default profile cannot be deleted.", "Warning");
            return;
        }

        if (!string.Equals(_pendingDeleteProfile, current, StringComparison.OrdinalIgnoreCase))
        {
            _pendingDeleteProfile = current;
            DeleteProfileButton.Content = "Confirm delete";
            AddLog($"Press Delete again to remove '{current}'.", "Warning");
            return;
        }

        if (!_profileStore.DeleteProfile(current))
        {
            ResetPendingProfileDelete();
            AddLog($"Profile '{current}' was not found.", "Warning");
            return;
        }

        ResetPendingProfileDelete();

        _settings.UpdateProfileReferences(current, null);
        _settingsStore.Save(_settings);

        LoadProfiles();
        AddLog($"Profile '{current}' deleted.");
    }

    private void ResetPendingProfileDelete()
    {
        _pendingDeleteProfile = null;
        DeleteProfileButton.Content = "Delete";
    }

    private void SaveSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveSettings(log: true);
    }

    private void AutoConnectCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
        SaveSettings(log: false);
        AddLog(_settings.AutoConnect ? "Auto-connect enabled." : "Auto-connect disabled.");
    }

    private void IrEnabledCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _settings.IrEnabled = IrEnabledCheckBox.IsChecked == true;
        SaveSettings(log: false);

        foreach (var session in GetSessions())
        {
            session.Connection.SetIrEnabled(_settings.IrEnabled);
        }

        AddLog(_settings.IrEnabled ? "IR camera switched on." : "IR camera switched off.");
    }

    private void AutoEnableEmulationCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _settings.AutoEnableEmulation = AutoEnableEmulationCheckBox.IsChecked == true;
        SaveSettings(log: false);

        if (_settings.AutoEnableEmulation && !_emulationEnabled)
        {
            SetEmulationToggle(true);
        }
    }

    private void SaveSettings(bool log)
    {
        _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
        _settings.AutoEnableEmulation = AutoEnableEmulationCheckBox.IsChecked == true;
        _settings.IrEnabled = IrEnabledCheckBox.IsChecked == true;
        _settings.PollIntervalMs = ParseInt(PollIntervalBox.Text, _settings.PollIntervalMs);
        _settings.ReconnectDelayMs = ParseInt(ReconnectDelayBox.Text, _settings.ReconnectDelayMs);
        _settings.StaleTimeoutMs = ParseInt(StaleTimeoutBox.Text, _settings.StaleTimeoutMs);
        _settings.StatusRequestIntervalMs = ParseInt(StatusIntervalBox.Text, _settings.StatusRequestIntervalMs);

        var normalized = _settings.ToWatcherOptions();
        _settings.PollIntervalMs = normalized.PollIntervalMs;
        _settings.ReconnectDelayMs = normalized.ReconnectDelayMs;
        _settings.StaleTimeoutMs = normalized.StaleTimeoutMs;
        _settings.StatusRequestIntervalMs = normalized.StatusRequestIntervalMs;
        _settings.LedMask = normalized.LedMask;

        PollIntervalBox.Text = _settings.PollIntervalMs.ToString();
        ReconnectDelayBox.Text = _settings.ReconnectDelayMs.ToString();
        StaleTimeoutBox.Text = _settings.StaleTimeoutMs.ToString();
        StatusIntervalBox.Text = _settings.StatusRequestIntervalMs.ToString();

        _watcher.UpdateOptions(normalized);
        _settingsStore.Save(_settings);

        if (log)
        {
            AddLog("Settings saved.");
        }
    }

    private static int ParseInt(string? text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private void UpdateLedButtons()
    {
        Led1Button.IsChecked = (_settings.LedMask & WiimoteProtocol.Led1) != 0;
        Led2Button.IsChecked = (_settings.LedMask & WiimoteProtocol.Led2) != 0;
        Led3Button.IsChecked = (_settings.LedMask & WiimoteProtocol.Led3) != 0;
        Led4Button.IsChecked = (_settings.LedMask & WiimoteProtocol.Led4) != 0;
    }

    private void LedButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string raw } || !byte.TryParse(raw, out var bit))
        {
            return;
        }

        _settings.LedMask = (_settings.LedMask ^ bit) & 0xF0;
        UpdateLedButtons();
        var mask = (byte)_settings.LedMask;

        foreach (var session in GetSessions())
        {
            session.Connection.SetLedMask(mask);
        }

        Avatar.SetLedMask(mask);
        SaveSettings(log: false);
        AddLog($"Player LED mask set to 0x{mask:X2}.");
    }

    private void RumbleToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        var enabled = RumbleToggle.IsChecked == true;
        _rumbleEnabled = enabled;
        foreach (var session in GetSessions())
        {
            session.Connection.SetRumble(enabled);
        }

        AddLog(enabled ? "Rumble enabled." : "Rumble disabled.");
    }

    private async void ScanNowButton_Click(object? sender, RoutedEventArgs e)
    {
        AddLog("Scanning for Wiimotes...");
        try
        {
            await _watcher.ScanOnceAsync();
            AddLog(_watcher.Connections.Count == 0 ? "Scan complete. No Wiimote found." : "Scan complete.");
        }
        catch (Exception ex)
        {
            AddLog($"Scan failed: {ex.Message}", "Error");
        }
    }

    private async void DisconnectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        AddLog("Disconnecting all Wiimotes...");
        try
        {
            await _watcher.StopAsync();
            await _watcher.StartAsync(_settings.ToWatcherOptions());
            AddLog("All Wiimotes disconnected. Watcher restarted.");
        }
        catch (Exception ex)
        {
            AddLog($"Disconnect failed: {ex.Message}", "Error");
        }
    }

    private void OpenProfilesFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        PlatformBackend.OpenFolder(_profileStore.Directory);
    }

    private void ClearLogButton_Click(object? sender, RoutedEventArgs e)
    {
        LogEntries.Clear();
    }

    private void AddLog(string message, string level = "Info")
    {
        if (_closing)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddLog(message, level));
            return;
        }

        var now = DateTime.Now;
        var entry = new LogEntry(now, message, level);
        LogEntries.Add(entry);

        // Disk writes happen on a background thread so bursts of watcher logs never
        // stall the UI thread.
        _logWriter.Enqueue($"[{now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");

        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(0);
        }

        ActivityList.ScrollIntoView(entry);
    }

    private void DrainLogWriter()
    {
        try
        {
            while (_logWriter.TryDequeue(out var line))
            {
                lock (_logFileGate)
                {
                    File.AppendAllText(_logFilePath, line);
                }
            }
        }
        catch
        {
        }
    }

    private void UpdateHeaderStatus()
    {
        var count = Devices.Count;
        StatusText = count switch
        {
            0 => "Waiting for a Wiimote",
            1 => "1 Wiimote connected",
            _ => $"{count} Wiimotes connected"
        };
        StatusBrush = count == 0 ? MutedBrush : SuccessBrush;
    }

    private void Dispatch(Action action)
    {
        if (_closing)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DeviceSession
    {
        public DeviceSession(WiimoteConnection connection, DeviceViewModel viewModel)
        {
            Connection = connection;
            ViewModel = viewModel;
        }

        public WiimoteConnection Connection { get; }
        public DeviceViewModel ViewModel { get; }
        public object Gate { get; } = new();
        public IVirtualGamepad? Gamepad { get; set; }
        public MappingProfile? Profile { get; set; }
        public EventHandler<WiimoteState>? SnapshotHandler { get; set; }
        public DateTimeOffset LastUiUpdate { get; set; }
        public int LastLoggedBattery { get; set; } = -1;
        public WiimoteButton LastButtons { get; set; }
        public int LastForwardedBattery { get; set; } = -1;
        public bool Retired { get; set; }
    }
}
