using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class WiimoteReportParserTests
{
    [Fact]
    public void Parse_StatusReport_UsesProtocolOffsets()
    {
        var report = WiimoteReportParser.Parse(Report(
            0x20, 0x00, 0x02, 0x18, 0x00, 0x00, 0xD7));

        Assert.NotNull(report);
        Assert.True(report.HasButtons);
        Assert.Equal(WiimoteButton.One, report.Buttons);
        Assert.True(report.HasStatusFlags);
        Assert.False(report.ExtensionConnected);
        Assert.Equal(1, report.LedState);
        Assert.True(report.HasBattery);
        Assert.Equal(0xD7, report.BatteryRaw);
        Assert.Equal(100, WiimoteReportParser.BatteryPercent(report.BatteryRaw));
    }

    [Fact]
    public void Parse_StatusReport_ReadsExtensionAndAllLeds()
    {
        var report = WiimoteReportParser.Parse(Report(
            0x20, 0x00, 0x00, 0xF2, 0x00, 0x00, 0x9E));

        Assert.NotNull(report);
        Assert.True(report.ExtensionConnected);
        Assert.Equal(15, report.LedState);
        Assert.Equal(79, WiimoteReportParser.BatteryPercent(report.BatteryRaw));
    }

    [Fact]
    public void Parse_StatusReport_RejectsTruncatedPayload()
    {
        var report = WiimoteReportParser.Parse(Exact(0x20, 0x00, 0x00, 0x18));

        Assert.NotNull(report);
        Assert.False(report.HasButtons);
        Assert.False(report.HasStatusFlags);
        Assert.False(report.HasBattery);
    }

    [Fact]
    public void CountsPerGravity_MatchesTheCalibrationOfARealRemote()
    {
        // The EEPROM of a real remote holds 0x80 as the zero point of an axis and 0x9A
        // as the value that one gravity produces on it. Both are the high eight bits of
        // the ten bit axis value, so one gravity is (0x9A - 0x80) * 4 = 104 counts and
        // an axis without acceleration reads 0x200.
        Assert.Equal(104f, WiimoteReportParser.CountsPerGravity);
        Assert.Equal(0x200, WiimoteReportParser.AccelerometerZero);
    }

    [Fact]
    public void Parse_CoreAccelerometerReport_DecodesTheTenBitAxisValues()
    {
        var report = WiimoteReportParser.Parse(Report(
            0x31, 0x00, 0x02, 0x76, 0x6A, 0x9B));

        Assert.NotNull(report);
        Assert.True(report.HasButtons);
        Assert.Equal(WiimoteButton.One, report.Buttons);
        Assert.True(report.HasAccelerometer);
        Assert.Equal(Gravity(-40), report.AccelX, 4);
        Assert.Equal(Gravity(-88), report.AccelY, 4);
        Assert.Equal(Gravity(108), report.AccelZ, 4);
    }

    [Fact]
    public void Parse_AccelerometerReport_ReportsOneGravityWhenTheRemoteLiesFlat()
    {
        // A remote that lies flat reads 0x9A on the axis that points up, which is the
        // value that a real remote stores in its EEPROM for one gravity.
        var report = WiimoteReportParser.Parse(Report(
            0x31, 0x00, 0x00, 0x80, 0x80, 0x9A));

        Assert.NotNull(report);
        Assert.True(report.HasAccelerometer);
        Assert.Equal(0f, report.AccelX, 4);
        Assert.Equal(0f, report.AccelY, 4);
        Assert.Equal(1f, report.AccelZ, 4);
    }

    [Fact]
    public void Parse_CoreAccelerometerReport_StripsAccelerometerBitsFromButtons()
    {
        // The first button byte carries the two low bits of X, the second one the low
        // bits of Y and Z. They must not be reported as buttons.
        var report = WiimoteReportParser.Parse(Report(
            0x31, 0x40, 0x42, 0x78, 0x6B, 0x9A));

        Assert.NotNull(report);
        Assert.Equal(WiimoteButton.One, report.Buttons);
        Assert.Equal(Gravity(-30), report.AccelX, 4);
        Assert.Equal(Gravity(-84), report.AccelY, 4);
        Assert.Equal(Gravity(106), report.AccelZ, 4);
    }

    [Fact]
    public void Parse_AccelerometerReport_KeepsHomeButton()
    {
        // Home lives in bit 7 of the second button byte and has to survive the
        // accelerometer bit masking.
        var report = WiimoteReportParser.Parse(Report(
            0x31, 0x00, 0x80, 0x80, 0x80, 0x80));

        Assert.NotNull(report);
        Assert.Equal(WiimoteButton.Home, report.Buttons);
        Assert.Equal(0f, report.AccelX, 4);
        Assert.Equal(0f, report.AccelY, 4);
        Assert.Equal(0f, report.AccelZ, 4);
    }

    [Fact]
    public void Parse_AccelerometerReport_UsesLowBitsOfTheCorrectButtonBytes()
    {
        // Bit 6 of the first button byte is the second low bit of X, bit 6 of the second
        // button byte is the low bit of Z.
        var report = WiimoteReportParser.Parse(Report(
            0x31, 0x40, 0x40, 0x80, 0x80, 0x80));

        Assert.NotNull(report);
        Assert.Equal(WiimoteButton.None, report.Buttons);
        Assert.Equal(Gravity(2), report.AccelX, 4);
        Assert.Equal(0f, report.AccelY, 4);
        Assert.Equal(Gravity(2), report.AccelZ, 4);
    }

    [Fact]
    public void Parse_CoreButtonsReport_PreservesAllButtonBits()
    {
        var report = WiimoteReportParser.Parse(Report(0x30, 0x08, 0x01));

        Assert.NotNull(report);
        Assert.Equal(WiimoteButton.Up | WiimoteButton.Two, report.Buttons);
        Assert.False(report.HasAccelerometer);
    }

    [Fact]
    public void Parse_CoreExtensionReport_CopiesExtensionData()
    {
        // DRM_KE is BB*2 EE*8, so the extension payload starts at offset 3.
        var report = WiimoteReportParser.Parse(Report(
            0x32, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08));

        Assert.NotNull(report);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 },
            report.ExtensionData);
    }

    [Fact]
    public void Parse_CoreExtensionReport_OmitsExtensionDataWhenTruncated()
    {
        var report = WiimoteReportParser.Parse(Exact(
            0x32, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06));

        Assert.NotNull(report);
        Assert.Null(report.ExtensionData);
    }

    [Fact]
    public void Parse_CoreAccelExtensionReport_StripsButtonsAndCopiesExtensionData()
    {
        // DRM_KAE is BB*2 AA*3 EE*16, so the extension payload starts at offset 6.
        var report = WiimoteReportParser.Parse(Report(
            0x35, 0x00, 0x04, 0x80, 0x80, 0x80,
            0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13,
            0x14, 0x15, 0x16, 0x17, 0x18, 0x19));

        Assert.NotNull(report);
        Assert.Equal(WiimoteButton.B, report.Buttons);
        Assert.Equal(
            new byte[] { 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19 },
            report.ExtensionData);
    }

    [Fact]
    public void Parse_UnknownOrShortReport_ReturnsNull()
    {
        Assert.Null(WiimoteReportParser.Parse(Exact(0x99, 0x00, 0x00)));
        Assert.Null(WiimoteReportParser.Parse(Exact(0x31)));
    }

    [Fact]
    public void Parse_DataReport_ReadsButtonsFromPayload()
    {
        // The 0x21 data report carries the button mask in the first two payload bytes.
        var report = WiimoteReportParser.Parse(Report(0x21, 0x00, 0x08));

        Assert.NotNull(report);
        Assert.Equal(WiimoteReportParser.DataReport, report.ReportId);
        Assert.True(report.HasButtons);
        Assert.Equal(WiimoteButton.A, report.Buttons);
    }

    [Fact]
    public void Parse_MemoryReply_ReadsOffsetSizeAndData()
    {
        // The reply to a read of the extension identifier at 0xA400FA. SE carries the size
        // minus one (5) in its upper nibble and the error code (0) in its lower one, and
        // OO OO echoes the low two bytes of the offset.
        var report = WiimoteReportParser.Parse(Report(
            0x21, 0x00, 0x00, 0x50, 0x00, 0xFA,
            0x00, 0x00, 0xA4, 0x20, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00));

        Assert.NotNull(report);
        Assert.True(report.HasMemoryReply);
        Assert.Equal(0x00FA, report.MemoryOffset);
        Assert.Equal(0, report.MemoryError);
        Assert.Equal(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x00, 0x00 }, report.MemoryData);
    }

    [Fact]
    public void Parse_MemoryReply_KeepsTheErrorCode()
    {
        // A read of unexisting memory answers with error code 8 in the lower nibble.
        var report = WiimoteReportParser.Parse(Exact(0x21, 0x00, 0x00, 0x18, 0x12, 0x34));

        Assert.NotNull(report);
        Assert.True(report.HasMemoryReply);
        Assert.Equal(0x08, report.MemoryError);
        Assert.Equal(0x1234, report.MemoryOffset);
        Assert.Empty(report.MemoryData!);
    }

    [Fact]
    public void Parse_ResultReport_ReadsTheAcknowledgedReportAndItsError()
    {
        var report = WiimoteReportParser.Parse(Report(0x22, 0x00, 0x02, 0x16, 0x03));

        Assert.NotNull(report);
        Assert.True(report.HasAck);
        Assert.Equal(0x16, report.AckReportId);
        Assert.Equal(0x03, report.AckError);
        Assert.Equal(WiimoteButton.One, report.Buttons);
    }

    [Fact]
    public void Parse_ReturnReport_ReadsButtonsFromPayload()
    {
        var report = WiimoteReportParser.Parse(Report(0x22, 0x00, 0x02, 0x15, 0x00));

        Assert.NotNull(report);
        Assert.True(report.HasButtons);
        Assert.Equal(WiimoteButton.One, report.Buttons);
    }

    [Fact]
    public void Parse_AccelAndIrReport_ReadsButtonsAndAccelerometer()
    {
        var report = WiimoteReportParser.Parse(Report(0x33, 0x00, 0x02, 0x76, 0x6A, 0x9B));

        Assert.NotNull(report);
        Assert.True(report.HasButtons);
        Assert.Equal(WiimoteButton.One, report.Buttons);
        Assert.True(report.HasAccelerometer);
        Assert.Equal(Gravity(-40), report.AccelX, 4);
        Assert.Equal(Gravity(-88), report.AccelY, 4);
        Assert.Equal(Gravity(108), report.AccelZ, 4);
    }

    [Fact]
    public void Parse_AccelIrReport_ReadsTheExtendedIrFormat()
    {
        var report = WiimoteReportParser.Parse(Report(
            0x33, 0x00, 0x00, 0x80, 0x80, 0x9A,
            0x12, 0x34, 0x15,
            0xFF, 0xFF, 0xF0,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00));

        Assert.NotNull(report);
        Assert.NotNull(report.IrPoints);

        // Three bytes per point, with the size in the low nibble of the third byte.
        Assert.True(report.IrPoints[0].IsValid);
        Assert.Equal(0x112, report.IrPoints[0].X);
        Assert.Equal(0x34, report.IrPoints[0].Y);
        Assert.Equal(5, report.IrPoints[0].Size);

        // A slot without an object reads as 0x3FF on both axes.
        Assert.False(report.IrPoints[1].IsValid);
    }

    [Fact]
    public void Parse_AccelIrExtensionReport_UsesTheBasicIrFormat()
    {
        // DRM_KAIE has ten IR bytes: two points per five bytes, with the high two bits of
        // every coordinate packed into the third byte of the pair.
        var payload = new byte[WiimoteProtocol.InputReportLength];
        payload[0] = 0x37;
        payload[6] = 0x12;
        payload[7] = 0x34;
        payload[8] = 0x10;

        var report = WiimoteReportParser.Parse(payload);

        Assert.NotNull(report);
        Assert.NotNull(report.IrPoints);
        Assert.True(report.IrPoints[0].IsValid);
        Assert.Equal(0x112, report.IrPoints[0].X);
        Assert.Equal(0x34, report.IrPoints[0].Y);
        Assert.Equal(0, report.IrPoints[0].Size);
    }

    [Fact]
    public void Parse_LongExtensionReport_CopiesExtensionData()
    {
        // DRM_KEE is BB*2 EE*19, so the extension payload starts at offset 3.
        var report = WiimoteReportParser.Parse(Report(
            0x34, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
            0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13));

        Assert.NotNull(report);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
                0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13 },
            report.ExtensionData);
    }

    [Fact]
    public void Parse_IrExtensionReport_CopiesExtensionDataFromCorrectOffset()
    {
        // DRM_KIE is BB*2 II*10 EE*9, so the extension payload starts at offset 13.
        var report = WiimoteReportParser.Parse(Report(
            0x36, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
            0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22, 0x33));

        Assert.NotNull(report);
        Assert.Equal(
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22, 0x33 },
            report.ExtensionData);
    }

    [Fact]
    public void Parse_AccelIrExtensionReport_CopiesExtensionDataFromCorrectOffset()
    {
        // DRM_KAIE is BB*2 AA*3 II*10 EE*6, so the extension payload starts at offset 16.
        var payload = new byte[WiimoteProtocol.InputReportLength];
        payload[0] = 0x37;

        // DRM_KAIE carries the IR points after the accelerometer: X and Y low bytes plus
        // the shared high byte that also holds the size of the point.
        payload[6] = 0x12;
        payload[7] = 0x34;
        payload[8] = 0x10;

        payload[16] = 0xAA;
        payload[17] = 0xBB;
        payload[18] = 0xCC;
        payload[19] = 0xDD;
        payload[20] = 0xEE;
        payload[21] = 0xFF;

        var report = WiimoteReportParser.Parse(payload);

        Assert.NotNull(report);
        Assert.True(report.HasAccelerometer);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, report.ExtensionData);

        Assert.NotNull(report.IrPoints);
        Assert.True(report.IrPoints[0].IsValid);
        Assert.Equal(0x112, report.IrPoints[0].X);
        Assert.Equal(0x34, report.IrPoints[0].Y);
    }

    [Fact]
    public void Parse_ReportIdIsNeverMatchedAtLaterOffset()
    {
        // A report whose id byte is not a known one must not be reinterpreted by scanning
        // later bytes for something that looks like a report id.
        var report = WiimoteReportParser.Parse(Report(0x99, 0x31, 0x00, 0x02, 0x76, 0x6A, 0x9B));

        Assert.Null(report);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 50)]
    [InlineData(200, 100)]
    [InlineData(0xD5, 100)]
    [InlineData(0xFF, 100)]
    [InlineData(300, 100)]
    [InlineData(-5, 0)]
    public void BatteryPercent_UsesIntegerScalingWithClamp(int raw, int expected)
    {
        Assert.Equal(expected, WiimoteReportParser.BatteryPercent(raw));
    }

    /// <summary>
    /// Turns a number of counts of the ten bit axis value into gravity units.
    /// </summary>
    private static float Gravity(int counts) => counts / 104f;

    private static byte[] Report(params byte[] payload)
    {
        var data = new byte[WiimoteProtocol.InputReportLength];
        payload.CopyTo(data, 0);
        return data;
    }

    private static byte[] Exact(params byte[] payload) => payload;
}
