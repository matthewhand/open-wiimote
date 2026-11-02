using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Experimental legacy decoder for synthetic gyro blocks. Its layout and calibration
/// assumptions are unverified; it is not used by the live connection or experimental stub.
/// Retained for research and compatibility with existing decoder tests.
/// </summary>
public static class MotionPlusGyro
{
    /// <summary>The number of bytes the Motion Plus keeps its three rates in.</summary>
    public const int BlockLength = 6;

    /// <summary>The value an axis reads while the remote does not turn.</summary>
    public const int ZeroPoint = 0x1F7F;

    /// <summary>The counts of a slow mode axis that make up one degree per second.</summary>
    public const double CountsPerDegreePerSecond = 13.768;

    /// <summary>
    /// How much further a fast mode reading reaches than a slow mode one, which is the ratio
    /// of the two full scale rates of the gyro.
    /// </summary>
    public const double FastModeScale = 2000.0 / 440.0;

    /// <summary>
    /// Decodes the six gyro bytes of a Motion Plus, in degrees per second.
    /// </summary>
    /// <returns>False when the block is shorter than the six bytes of a reading.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> block, out GyroReading reading)
    {
        if (block.Length < BlockLength)
        {
            reading = GyroReading.Still;
            return false;
        }

        reading = new GyroReading(
            Yaw: ToDegreesPerSecond(((block[3] & 0x3F) << 8) | block[0], slowMode: (block[3] & 0x40) != 0),
            Roll: ToDegreesPerSecond(((block[4] & 0x3F) << 8) | block[1], slowMode: (block[4] & 0x40) != 0),
            Pitch: ToDegreesPerSecond(((block[5] & 0x3F) << 8) | block[2], slowMode: (block[3] & 0x80) != 0));
        return true;
    }

    /// <summary>
    /// Converts one raw gyro value, zero point included, into degrees per second.
    /// </summary>
    /// <remarks>
    /// The result is clamped rather than wrapped, although the full scale of the gyro and the
    /// fourteen bits of a reading leave no room to reach the end of a short.
    /// </remarks>
    public static short ToDegreesPerSecond(int rawValue, bool slowMode)
    {
        var countsPerDegreePerSecond = slowMode
            ? CountsPerDegreePerSecond
            : CountsPerDegreePerSecond / FastModeScale;
        var degreesPerSecond = (rawValue - ZeroPoint) / countsPerDegreePerSecond;

        return (short)Math.Clamp(Math.Round(degreesPerSecond), short.MinValue, short.MaxValue);
    }
}
