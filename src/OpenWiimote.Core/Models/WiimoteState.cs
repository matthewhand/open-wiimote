namespace OpenWiimote.Core.Models;

/// <summary>
/// The decoded state of a Wiimote.
/// </summary>
/// <remarks>
/// The remote is expected to be held horizontally like a NES controller, with the buttons
/// facing up. The accelerometer then points with Z up, so Z carries the gravity and the
/// horizontal axes carry the part of the gravity that the tilt moves into them.
/// </remarks>
public sealed record WiimoteState
{
    public WiimoteButton Buttons { get; init; } = WiimoteButton.None;
    public int BatteryRaw { get; init; }
    public int BatteryPercent { get; init; } = -1;

    /// <summary>The acceleration on the X axis in gravity units.</summary>
    public float AccelX { get; init; }

    /// <summary>The acceleration on the Y axis in gravity units.</summary>
    public float AccelY { get; init; }

    /// <summary>The acceleration on the Z axis in gravity units.</summary>
    public float AccelZ { get; init; } = 1f;

    /// <summary>
    /// The angular velocity around the X axis in degrees per second, which the Motion Plus
    /// calls pitch: positive while the far end of the remote goes up. Only a remote with a
    /// Motion Plus reports a rate, and the other two stay zero without one.
    /// </summary>
    public short GyroX { get; init; }

    /// <summary>
    /// The angular velocity around the Y axis in degrees per second, which the Motion Plus
    /// calls roll: positive while the remote is rolled to the right.
    /// </summary>
    public short GyroY { get; init; }

    /// <summary>
    /// The angular velocity around the Z axis in degrees per second, which the Motion Plus
    /// calls yaw: positive while the remote is turned to the right.
    /// </summary>
    public short GyroZ { get; init; }

    public bool ExtensionConnected { get; init; }
    public WiimoteExtensionType ExtensionType { get; init; } = WiimoteExtensionType.None;
    public NunchukState? Nunchuk { get; init; }
    public ClassicControllerState? ClassicController { get; init; }
    public IrPoint[] IrPoints { get; init; } = Array.Empty<IrPoint>();
    /// <summary>Only an actual IR-bearing report advances these freshness markers.</summary>
    public DateTimeOffset? LastIrReport { get; init; }
    public long IrFrameNumber { get; init; }
    public DateTimeOffset LastReport { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The roll of the remote in degrees, positive when it is rolled to the right.</summary>
    public double TiltXDegrees => Math.Atan2(AccelX, AccelZ) * (180.0 / Math.PI);

    /// <summary>The pitch of the remote in degrees, positive when the far end goes up.</summary>
    public double TiltYDegrees => Math.Atan2(AccelY, AccelZ) * (180.0 / Math.PI);

    /// <summary>
    /// The angle between the face of the remote and the horizontal in degrees. It is
    /// zero while the remote lies flat and grows in every direction it is tilted.
    /// </summary>
    public double TiltDegrees => Math.Atan2(
        Math.Sqrt((AccelX * AccelX) + (AccelY * AccelY)),
        AccelZ) * (180.0 / Math.PI);

    public string PressedButtonText
    {
        get
        {
            var pressed = new List<string>(4);
            Add(pressed, WiimoteButton.A, "A");
            Add(pressed, WiimoteButton.B, "B");
            Add(pressed, WiimoteButton.One, "1");
            Add(pressed, WiimoteButton.Two, "2");
            Add(pressed, WiimoteButton.Plus, "+");
            Add(pressed, WiimoteButton.Minus, "-");
            Add(pressed, WiimoteButton.Home, "Home");
            Add(pressed, WiimoteButton.Up, "Up");
            Add(pressed, WiimoteButton.Down, "Down");
            Add(pressed, WiimoteButton.Left, "Left");
            Add(pressed, WiimoteButton.Right, "Right");
            return pressed.Count == 0 ? "None" : string.Join(", ", pressed);
        }
    }

    private void Add(List<string> target, WiimoteButton flag, string label)
    {
        if ((Buttons & flag) != 0)
        {
            target.Add(label);
        }
    }
}
