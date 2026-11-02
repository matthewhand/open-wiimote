using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// Reads the six gyro bytes that a Wii Motion Plus keeps in its register block.
/// </summary>
public sealed class MotionPlusGyroTests
{
    [Fact]
    public void TryDecode_ReadsAStillRemoteAsStill()
    {
        // A remote that does not turn reads 0x1F7F on every axis, and every axis has its slow
        // mode flag set. The low bytes come first, the upper six bits in the last three bytes.
        byte[] block = [0x7F, 0x7F, 0x7F, 0xDF, 0x5F, 0x1F];

        Assert.True(MotionPlusGyro.TryDecode(block, out var reading));
        Assert.Equal(0, reading.Yaw);
        Assert.Equal(0, reading.Roll);
        Assert.Equal(0, reading.Pitch);
    }

    [Fact]
    public void TryDecode_ReadsTheRatesInDegreesPerSecond()
    {
        // 9302 is 90 degrees per second to the right (9302 - 8063 = 1239 counts, and 1239 /
        // 13.768 counts per degree per second is 90) and 7443 is 45 to the left.
        byte[] block = [0x56, 0x13, 0x7F, 0xE4, 0x5D, 0x1F];

        Assert.True(MotionPlusGyro.TryDecode(block, out var reading));
        Assert.Equal(90, reading.Yaw);
        Assert.Equal(-45, reading.Roll);
        Assert.Equal(0, reading.Pitch);
    }

    [Fact]
    public void TryDecode_ReadsTheFastModeWithItsWiderRange()
    {
        // Bit 6 of the third byte is clear, so the yaw axis is in fast mode: its 20 counts over
        // the zero point cover 6.6 degrees per second instead of 1.45.
        byte[] fast = [0x93, 0x7F, 0x7F, 0x9F, 0x5F, 0x1F];
        Assert.True(MotionPlusGyro.TryDecode(fast, out var reading));
        Assert.Equal(7, reading.Yaw);

        // Both modes stop at the same 6058 counts, which the fast mode calls 2000 degrees per
        // second and the slow mode 440.
        Assert.Equal(2000, MotionPlusGyro.ToDegreesPerSecond(14121, slowMode: false));
        Assert.Equal(440, MotionPlusGyro.ToDegreesPerSecond(14121, slowMode: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void TryDecode_RejectsBlocksThatAreTooShort(int length)
    {
        Assert.False(MotionPlusGyro.TryDecode(new byte[length], out _));
    }
}
