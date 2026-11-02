namespace OpenWiimote.Core.Models;

/// <summary>
/// The angular velocity that the gyro of a Wii Motion Plus reports, in degrees per second.
/// </summary>
/// <remarks>
/// The three rates keep the names of the Motion Plus registers rather than the names of the
/// axes of <see cref="WiimoteState"/>, because the two frames of reference are not the same
/// one. The remote lies flat with Z up, so the yaw of the Motion Plus turns it about that
/// vertical Z axis, the pitch tilts the far end up and down about X, and the roll twists it
/// about its own long Y axis.
/// </remarks>
public readonly record struct GyroReading(short Yaw, short Roll, short Pitch)
{
    /// <summary>The reading of a remote that does not turn.</summary>
    public static GyroReading Still => default;
}
