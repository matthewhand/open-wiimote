using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class ReplayTests
{
    [Fact]
    public void Replay_DecodesAButtonAndAccelerometerReport()
    {
        // 0x80 is the zero point of an axis, 0x9A is one gravity of Z.
        var entries = WiimoteReportReplayer.Replay(["31 00 0C 80 80 9A"]);

        var entry = Assert.Single(entries);
        Assert.Null(entry.Error);
        Assert.NotNull(entry.Report);
        Assert.Equal(0x31, entry.Report.ReportId);
        Assert.True(entry.Report.HasButtons);
        Assert.Equal(WiimoteButton.A | WiimoteButton.B, entry.Report.Buttons);
        Assert.True(entry.Report.HasAccelerometer);
        Assert.Equal(1f, entry.Report.AccelZ, 2);

        var text = entry.Describe();
        Assert.Contains("id=0x31", text);
        Assert.Contains("buttons=", text);
        Assert.Contains("accel=(0.00, 0.00, 1.00)g", text);
    }

    [Fact]
    public void Replay_ShowsTheTiltOfARolledRemote()
    {
        var entry = Assert.Single(WiimoteReportReplayer.Replay(["31 00 00 8E 82 96"]));

        // The X axis carries the gravity of the roll, so the roll is no longer zero.
        var text = entry.Describe();
        Assert.Contains("tilt=(", text);
        Assert.DoesNotContain("tilt=(0deg, 0deg)", text);
    }

    [Fact]
    public void Replay_ReportsBatteryAndExtensionOfAStatusReport()
    {
        var entry = Assert.Single(WiimoteReportReplayer.Replay(["20 00 00 F2 00 00 9E"]));

        Assert.True(entry.Report!.HasBattery);
        var text = entry.Describe();
        Assert.Contains("battery=79%", text);
        Assert.Contains("extension=yes", text);
    }

    [Fact]
    public void Replay_SkipsCommentsAndBlankLines()
    {
        var entries = WiimoteReportReplayer.Replay(
        [
            "# a capture with a comment",
            string.Empty,
            "   ",
            "30 00 00"
        ]);

        var entry = Assert.Single(entries);
        Assert.Equal(1, entry.LineNumber);
        Assert.Equal(0x30, entry.Report!.ReportId);
    }

    [Fact]
    public void Replay_AcceptsColonAndCommaSeparatorsAndBytePrefixes()
    {
        var entries = WiimoteReportReplayer.Replay(["0x31,0x00:0x00 0x82 0x82 0x9A"]);

        var entry = Assert.Single(entries);
        Assert.Null(entry.Error);
        Assert.Equal(0x31, entry.Report!.ReportId);
    }

    [Fact]
    public void Replay_ReportsBadHexInsteadOfThrowing()
    {
        var entries = WiimoteReportReplayer.Replay(["31 00 zz 82"]);

        var entry = Assert.Single(entries);
        Assert.Null(entry.Report);
        Assert.NotNull(entry.Error);
        Assert.Contains("'zz' is not a hex byte", entry.Describe());
    }

    [Fact]
    public void Replay_DecodesNothingFromATruncatedReport()
    {
        var entry = Assert.Single(WiimoteReportReplayer.Replay(["31 00"]));

        Assert.Null(entry.Error);
        Assert.NotNull(entry.Report);
        Assert.False(entry.Report.HasButtons);
        Assert.False(entry.Report.HasAccelerometer);
        Assert.Equal("#1   id=0x31", entry.Describe());
    }

    [Fact]
    public void Replay_ReportsAnUnknownReportIdAsNoReport()
    {
        var entry = Assert.Single(WiimoteReportReplayer.Replay(["3F 00 00"]));

        Assert.Null(entry.Report);
        Assert.Null(entry.Error);
        Assert.Contains("no report", entry.Describe());
    }

    [Fact]
    public void ParseBytes_RejectsAThreeDigitToken()
    {
        Assert.Throws<FormatException>(() => WiimoteReportReplayer.ParseBytes("31 100"));

    }

    [Fact]
    public void Replay_KeepsTheLineNumbersOfTheCapture()
    {
        var entries = WiimoteReportReplayer.Replay(
        [
            "# header",
            "30 00 00",
            "# another comment",
            "20 00 00 18 00 00 D7"
        ]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].LineNumber);
        Assert.Equal(2, entries[1].LineNumber);
        Assert.Equal(0x30, entries[0].Report!.ReportId);
        Assert.Equal(0x20, entries[1].Report!.ReportId);
    }
}
