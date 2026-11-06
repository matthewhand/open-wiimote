using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class HidSysfsTests
{
    private const string BluetoothUevent = """
        DRIVER=wiimote
        HID_ID=0005:0000057E:00000306
        HID_NAME=Nintendo RVL-CNT-01
        HID_PHYS=dc:56:7b:03:2b:ae
        HID_UNIQ=02:11:ab:33:cd:55
        MODALIAS=hid:b0005g0000v0000057Ep00000306
        """;

    [Fact]
    public void ParseUevent_ReadsBluetoothRemoteIdentity()
    {
        var identity = HidSysfs.ParseUevent(BluetoothUevent);

        Assert.NotNull(identity);
        Assert.Equal(0x057E, identity.VendorId);
        Assert.Equal(0x0306, identity.ProductId);
        Assert.Equal("Nintendo RVL-CNT-01", identity.Name);
        Assert.Equal("02:11:ab:33:cd:55", identity.UniqueId);
        Assert.Equal("hid:b0005g0000v0000057Ep00000306", identity.Modalias);
    }

    [Fact]
    public void ParseUevent_ReadsUsbRemoteIdentity()
    {
        var identity = HidSysfs.ParseUevent("HID_ID=0003:0000057E:00000330\nHID_NAME=Nintendo RVL-CNT-01-TR\n");

        Assert.NotNull(identity);
        Assert.Equal(0x057E, identity.VendorId);
        Assert.Equal(0x0330, identity.ProductId);
        Assert.Equal("Nintendo RVL-CNT-01-TR", identity.Name);
        Assert.Null(identity.UniqueId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DRIVER=wiimote\nHID_NAME=Nintendo RVL-CNT-01\n")]
    [InlineData("HID_ID=not-an-id\n")]
    [InlineData("HID_ID=0005:0000057E\n")]
    public void ParseUevent_RejectsIncompleteContents(string? contents)
    {
        Assert.Null(HidSysfs.ParseUevent(contents));
    }

    [Theory]
    [InlineData("0005:0000057E:00000306", 0x057E, 0x0306)]
    [InlineData("0003:0000057e:00000330", 0x057E, 0x0330)]
    public void TryParseHidId_ReadsVendorAndProduct(string value, int vendor, int product)
    {
        Assert.True(HidSysfs.TryParseHidId(value, out var vendorId, out var productId));
        Assert.Equal(vendor, vendorId);
        Assert.Equal(product, productId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0005:0000057E:")]
    [InlineData("0005:ZZZZ:00000306")]
    [InlineData("0005:0000057E:00000306:1")]
    [InlineData("0005:00000000:00000306")]
    public void TryParseHidId_RejectsInvalidValues(string? value)
    {
        Assert.False(HidSysfs.TryParseHidId(value, out _, out _));
    }

    [Theory]
    [InlineData("/sys/devices/pci0000:00/usb3/3-3/3-3:1.0/bluetooth/hci0/hci0:51/0005:057E:0306.0002/hidraw/hidraw0", "hidraw0")]
    [InlineData("/dev/hidraw7", "hidraw7")]
    [InlineData("/dev/hidraw7/", "hidraw7")]
    public void TryGetHidrawNode_ExtractsNodeName(string devicePath, string expected)
    {
        Assert.Equal(expected, HidSysfs.TryGetHidrawNode(devicePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/dev/usb/hiddev0")]
    [InlineData("hidraw")]
    public void TryGetHidrawNode_RejectsOtherPaths(string? devicePath)
    {
        Assert.Null(HidSysfs.TryGetHidrawNode(devicePath));
    }

    [Theory]
    [InlineData("0005:057E:0306.0003:blue:p0", 0)]
    [InlineData("0005:057E:0306.0003:blue:p3", 3)]
    [InlineData("0005:057e:0306.0003:BLUE:P2", 2)]
    public void TryParsePlayerIndex_ReadsTheLedIndex(string name, int expected)
    {
        Assert.Equal(expected, KernelHidLeds.TryParsePlayerIndex(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chromeos:white:power")]
    [InlineData("0005:057E:0306.0003:blue:p4")]
    [InlineData("0005:057E:0306.0003:blue:px")]
    public void TryParsePlayerIndex_RejectsOtherLeds(string? name)
    {
        Assert.Null(KernelHidLeds.TryParsePlayerIndex(name));
    }

    [Theory]
    [InlineData("Nintendo Wii Remote Accelerometer", true)]
    [InlineData("nintendo wii remote accelerometer", true)]
    [InlineData("Nintendo Wii Remote", false)]
    [InlineData("Nintendo Wii Remote IR", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAccelerometerName_MatchesOnlyTheAccelerometerDevice(string? name, bool expected)
    {
        Assert.Equal(expected, KernelHidAccelerometer.IsAccelerometerName(name));
    }
}
