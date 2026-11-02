using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.App;

public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private static readonly IBrush HighBatteryBrush = new SolidColorBrush(Color.Parse("#4DDC9A"));
    private static readonly IBrush MediumBatteryBrush = new SolidColorBrush(Color.Parse("#F8C95C"));
    private static readonly IBrush LowBatteryBrush = new SolidColorBrush(Color.Parse("#FF7186"));
    private static readonly IBrush UnknownBatteryBrush = new SolidColorBrush(Color.Parse("#8FA3C8"));

    private WiimoteState _state = new();
    private bool _isConnected = true;
    private string? _pinnedProfile;

    public DeviceViewModel(WiimoteConnection connection)
    {
        Connection = connection;
        DisplayName = connection.ProductName;
        BluetoothAddress = connection.BluetoothAddress ?? "Unknown address";
        DeviceKey = connection.DeviceKey;
    }

    public WiimoteConnection Connection { get; }
    public string DeviceKey { get; }
    public string DisplayName { get; }
    public string BluetoothAddress { get; }

    public WiimoteState State => _state;

    /// <summary>
    /// The angular velocity around the X axis from the Wii Motion Plus gyro, in degrees/sec.
    /// </summary>
    public short GyroX => _state.GyroX;

    /// <summary>
    /// The angular velocity around the Y axis from the Wii Motion Plus gyro, in degrees/sec.
    /// </summary>
    public short GyroY => _state.GyroY;

    /// <summary>
    /// The angular velocity around the Z axis from the Wii Motion Plus gyro, in degrees/sec.
    /// </summary>
    public short GyroZ => _state.GyroZ;

    /// <summary>
    /// The profile this remote is mapped with on its own, or null while it follows the active
    /// profile. The window keeps it in step with the picker and with the settings file.
    /// </summary>
    public string? PinnedProfile
    {
        get => _pinnedProfile;
        set
        {
            if (string.Equals(_pinnedProfile, value, StringComparison.Ordinal))
            {
                return;
            }

            _pinnedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileText));
        }
    }

    /// <summary>The line of the device list that says which profile the remote is mapped with.</summary>
    public string ProfileText => string.IsNullOrWhiteSpace(_pinnedProfile)
        ? "Follows the active profile"
        : $"Profile: {_pinnedProfile}";

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionState));
        }
    }

    public string ConnectionState => IsConnected ? "Connected" : "Disconnected";

    public int BatteryValue => Math.Clamp(_state.BatteryPercent, 0, 100);

    public string BatteryText => _state.BatteryPercent < 0 ? "Battery --" : $"{_state.BatteryPercent}%";

    public IBrush BatteryBrush => _state.BatteryPercent switch
    {
        < 0 => UnknownBatteryBrush,
        <= 20 => LowBatteryBrush,
        <= 45 => MediumBatteryBrush,
        _ => HighBatteryBrush
    };

    public string PressedButtonText => _state.PressedButtonText;
    public string AccelText => $"X {_state.AccelX:0.00}   Y {_state.AccelY:0.00}   Z {_state.AccelZ:0.00}";
    public string TiltText => $"{_state.TiltXDegrees:0}° / {_state.TiltYDegrees:0}°";

    /// <summary>
    /// The angular velocity around each axis from the Wii Motion Plus gyro, in degrees/sec.
    /// </summary>
    public string GyroText => $"X {_state.GyroX}°/sec   Y {_state.GyroY}°/sec   Z {_state.GyroZ}°/sec";

    /// <summary>The IR points of the last report, or "No points" while the camera sees none.</summary>
    public string IrText
    {
        get
        {
            var points = _state.IrPoints.Where(point => point.IsValid).ToArray();
            return points.Length == 0
                ? "No points"
                : string.Join("  ", points.Select(point => $"{point.X},{point.Y}"));
        }
    }

    public string ExtensionText => _state.ExtensionType switch
    {
        WiimoteExtensionType.Nunchuk => "Nunchuk connected",
        WiimoteExtensionType.ClassicController => "Classic Controller connected",
        WiimoteExtensionType.ClassicControllerPro => "Classic Controller Pro connected",
        WiimoteExtensionType.MotionPlus => "Motion Plus connected",
        WiimoteExtensionType.Unknown => "Unknown extension connected",
        _ => _state.ExtensionConnected ? "Extension connected" : "No extension"
    };

    public string LastSeenText => _state.LastReport.ToLocalTime().ToString("HH:mm:ss");

    public void Apply(WiimoteState state)
    {
        _state = state;
        RaiseStateProperties();
    }

    public void MarkDisconnected()
    {
        IsConnected = false;
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(BatteryValue));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(BatteryBrush));
        OnPropertyChanged(nameof(PressedButtonText));
        OnPropertyChanged(nameof(AccelText));
        OnPropertyChanged(nameof(TiltText));
        OnPropertyChanged(nameof(GyroText));
        OnPropertyChanged(nameof(IrText));
        OnPropertyChanged(nameof(ExtensionText));
        OnPropertyChanged(nameof(LastSeenText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
