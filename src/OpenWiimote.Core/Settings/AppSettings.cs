using System.Text.Json;
using System.Text.Json.Serialization;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.Core.Settings;

public sealed class AppSettings
{
    // Preserve settings written by newer or locally extended builds during an upgrade.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public void UpdateProfileReferences(string oldName, string? newName)
    {
        DeviceProfiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(PreferredProfile, oldName, StringComparison.OrdinalIgnoreCase))
            PreferredProfile = newName ?? "Default";
        foreach (var key in DeviceProfiles.Where(pair =>
            string.Equals(pair.Value, oldName, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
        {
            if (newName is null) DeviceProfiles.Remove(key);
            else DeviceProfiles[key] = newName;
        }
    }

    public OpenWiimote.Core.Pointer.PointerPreferences Pointer { get; set; } = new();

    public bool AutoConnect { get; set; } = true;
    public int PollIntervalMs { get; set; } = 1000;
    public int ReconnectDelayMs { get; set; } = 3000;
    public int StaleTimeoutMs { get; set; } = 6000;
    public int StatusRequestIntervalMs { get; set; } = 1000;
    public int LedMask { get; set; } = WiimoteProtocol.Led1;
    public bool AutoEnableEmulation { get; set; }
    public string PreferredProfile { get; set; } = "Default";
    /// <summary>Profile to use for a specific device, keyed by the device key or Bluetooth address.</summary>
    public Dictionary<string, string> DeviceProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Remembered player lights, keyed by stable device identity; at most four slots.</summary>
    public Dictionary<string, int> PlayerSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string AvatarTheme { get; set; } = "wiimote";

    /// <summary>Matrix style digital rain behind the dashboard avatar.</summary>
    public bool AvatarRainEnabled { get; set; } = true;

    /// <summary>Live X/Y/Z accelerometer trace in the device detail card.</summary>
    public bool SparklineEnabled { get; set; } = true;

    /// <summary>1-4 toggle the player LEDs, R toggles rumble.</summary>
    public bool KeyboardShortcutsEnabled { get; set; } = true;

    /// <summary>The avatar LED sockets buzz while rumble runs.</summary>
    public bool RumbleShakeEnabled { get; set; } = true;

    /// <summary>Rumble briefly when any button is pressed.</summary>
    public bool RumbleOnButtonPress { get; set; }

    /// <summary>Battery segments on the avatar model.</summary>
    public bool BatterySegmentsEnabled { get; set; } = true;

    /// <summary>
    /// Switches the IR camera on, so the reports carry the four IR points. It costs battery
    /// and is only useful with a sensor bar or another infrared source in front of the remote.
    /// </summary>
    public bool IrEnabled { get; set; }

    public WiimoteWatcherOptions ToWatcherOptions()
    {
        return new WiimoteWatcherOptions
        {
            AutoConnect = AutoConnect,
            PollIntervalMs = PollIntervalMs,
            ReconnectDelayMs = ReconnectDelayMs,
            StaleTimeoutMs = StaleTimeoutMs,
            StatusRequestIntervalMs = StatusRequestIntervalMs,
            LedMask = (byte)LedMask,
            IrEnabled = IrEnabled
        }.Normalize();
    }
}
