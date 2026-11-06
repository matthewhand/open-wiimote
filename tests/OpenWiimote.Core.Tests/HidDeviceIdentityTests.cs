using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class HidDeviceIdentityTests
{
    [Theory]
    [InlineData("0211ab33cd55", "02:11:AB:33:CD:55")]
    [InlineData(" 0211AB33CD55 ", "02:11:AB:33:CD:55")]
    [InlineData("00805F9B34FB", "00:80:5F:9B:34:FB")]
    [InlineData("02:11:AB:33:CD:55", "02:11:AB:33:CD:55")]
    [InlineData("02-11-ab-33-cd-55", "02:11:AB:33:CD:55")]
    public void TryFormatBluetoothAddress_FormatsSerialAddress(string input, string expected)
    {
        Assert.Equal(expected, HidDeviceIdentity.TryFormatBluetoothAddress(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0211ab33cd5")]
    [InlineData("0211ab33cd555")]
    [InlineData("0211ab33cd5z")]
    public void TryFormatBluetoothAddress_RejectsInvalidValues(string? input)
    {
        Assert.Null(HidDeviceIdentity.TryFormatBluetoothAddress(input));
    }
}
