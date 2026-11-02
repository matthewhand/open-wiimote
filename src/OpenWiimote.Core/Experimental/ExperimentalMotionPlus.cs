using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Experimental;

/// <summary>
/// Placeholder for a future verified sample source. It never opens a device,
/// activates an extension, or supplies simulated readings to the application.
/// </summary>
public static class ExperimentalMotionPlus
{
    public const string Status = "experimental";
    public const bool Available = false;
    public const string Description = "MotionPlus gyro support is experimental and unavailable. Tilt works without MotionPlus.";

    public static bool TryGetReading(out GyroReading reading)
    {
        reading = default;
        return false;
    }
}
