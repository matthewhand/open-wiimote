using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class AccelerometerReaderTests
{
    [Fact]
    public void ReadAxes_flat_remote_returns_zero_x_zero_y_one_g()
    {
        // Flat remote: known configuration from existing test suite
        var reportBytes = new byte[] { 0x31, 0x00, 0x00, 0x80, 0x80, 0x9A };
        AccelerometerReader.ReadAxes(reportBytes, out var x, out var y, out var z);

        // Assert: X=0, Y=0, Z=1g when Wiimote held horizontally
        Assert.Equal(0f, x, 4);
        Assert.Equal(0f, y, 4);
        Assert.Equal(1f, z, 4);
    }

    [Fact]
    public void AdjustAxis_zero_point_returns_zero()
    {
        var result = AccelerometerReader.AdjustAxis(AccelerometerReader.AccelerometerZero);
        Assert.Equal(0f, result);
    }

    [Fact]
    public void AdjustAxis_one_gravity_positive_returns_one()
    {
        var result = AccelerometerReader.AdjustAxis(AccelerometerReader.AccelerometerZero + (int)AccelerometerReader.CountsPerGravity);
        Assert.Equal(1f, result);
    }

    [Fact]
    public void AdjustAxis_negative_one_gravity_returns_negative_one()
    {
        var result = AccelerometerReader.AdjustAxis(AccelerometerReader.AccelerometerZero - (int)AccelerometerReader.CountsPerGravity);
        Assert.Equal(-1f, result);
    }

    [Fact]
    public void Constants_are_properly_defined()
    {
        Assert.Equal(0x200, AccelerometerReader.AccelerometerZero);
        Assert.Equal(104f, AccelerometerReader.CountsPerGravity);
    }
}