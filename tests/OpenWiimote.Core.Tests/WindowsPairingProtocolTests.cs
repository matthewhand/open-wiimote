using OpenWiimote.Platform.Windows;
using Xunit;
namespace OpenWiimote.Core.Tests;
public sealed class WindowsPairingProtocolTests
{
    [Fact]
    public void RedSyncPinUsesSixBinaryHostAddressBytesInReverseDisplayOrder()
    {
        var pin=WindowsWiimotePairing.SyncPin(0x112233445566);
        Assert.Equal(new char[]{(char)0x66,(char)0x55,(char)0x44,(char)0x33,(char)0x22,(char)0x11},pin.ToCharArray());
        Assert.Equal(6,WindowsWiimotePairing.SyncPin(0x110033004400).Length);
        Assert.Equal(0,WindowsWiimotePairing.SyncPin(0x110033004400)[0]);
    }
    [Theory]
    [InlineData("not an address")]
    [InlineData("00:00:00:00:00:00")]
    [InlineData("12:34:56:78:90;ab")]
    public void InvalidPairingTargetsCannotReachTheNativeApis(string value)=>Assert.Throws<ArgumentException>(()=>WindowsWiimotePairing.ParseAddress(value));
    [Fact]
    public void OnlyKnownWiiRemoteNamesAreEligibleAndNativeStructsMatchWin32()
    {
        Assert.True(WindowsWiimotePairing.IsRemoteName("Nintendo RVL-CNT-01"));
        Assert.False(WindowsWiimotePairing.IsRemoteName("A keyboard"));
        Assert.False(WindowsWiimotePairing.IsRemoteName("Nintendo RVL-CNT-01; command"));
        Assert.Equal(560,WindowsWiimotePairing.StructureSizes.Device);
        Assert.Equal(520,WindowsWiimotePairing.StructureSizes.Radio);
        Assert.Equal(IntPtr.Size==8?40:32,WindowsWiimotePairing.StructureSizes.Search);
    }
}
