using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class HidDeviceIdentityTests
{
    [Theory]
    [InlineData("020000000001", "02:00:00:00:00:01")]
    [InlineData(" 020000000001 ", "02:00:00:00:00:01")]
    [InlineData("00805F9B34FB", "00:80:5F:9B:34:FB")]
    [InlineData("02:00:00:00:00:01", "02:00:00:00:00:01")]
    [InlineData("02-00-00-00-00-01", "02:00:00:00:00:01")]
    public void TryFormatBluetoothAddress_FormatsSerialAddress(string input, string expected)
    {
        Assert.Equal(expected, HidDeviceIdentity.TryFormatBluetoothAddress(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0017ab398e0")]
    [InlineData("0200000000015")]
    [InlineData("0017ab398e0z")]
    public void TryFormatBluetoothAddress_RejectsInvalidValues(string? input)
    {
        Assert.Null(HidDeviceIdentity.TryFormatBluetoothAddress(input));
    }
}
