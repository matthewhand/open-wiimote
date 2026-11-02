using System.Globalization;
using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Replays captured HID reports through <see cref="WiimoteReportParser"/> so a protocol
/// change can be checked against a recording instead of a remote on the desk.
/// </summary>
/// <remarks>
/// The input is one report per line: hex bytes separated by spaces, commas or colons.
/// Blank lines and lines starting with <c>#</c> are ignored, and every byte may carry a
/// <c>0x</c> prefix, so a line copied out of a log usually works unchanged.
/// </remarks>
public static class WiimoteReportReplayer
{
    private static readonly char[] Separators = [' ', '\t', ',', ':', ';', '-'];

    public static IReadOnlyList<ReplayedReport> Replay(IEnumerable<string> lines)
    {
        var entries = new List<ReplayedReport>();
        var number = 0;

        foreach (var rawLine in lines)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            number++;

            byte[] bytes;
            try
            {
                bytes = ParseBytes(line);
            }
            catch (FormatException ex)
            {
                entries.Add(new ReplayedReport(number, line, null, ex.Message));
                continue;
            }

            entries.Add(new ReplayedReport(number, line, WiimoteReportParser.Parse(bytes), null));
        }

        return entries;
    }

    /// <summary>Turns the hex text of one line into the bytes of a report.</summary>
    /// <exception cref="FormatException">A token is not a hex byte.</exception>
    public static byte[] ParseBytes(string line)
    {
        var tokens = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new FormatException("the line has no bytes");
        }

        var bytes = new byte[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
            }

            if (token.Length is 0 or > 2 ||
                !byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"'{tokens[index]}' is not a hex byte");
            }

            bytes[index] = value;
        }

        return bytes;
    }
}

/// <summary>One replayed line: the raw bytes, the decoded report and, on bad input, the reason.</summary>
public sealed record ReplayedReport(int LineNumber, string Source, WiimoteReport? Report, string? Error)
{
    public string Describe()
    {
        var prefix = $"#{LineNumber,-3}";

        if (Error is not null)
        {
            return $"{prefix} error: {Error}";
        }

        if (Report is null)
        {
            return $"{prefix} no report (unknown report id or too short)";
        }

        var parts = new List<string> { $"id=0x{Report.ReportId:X2}" };

        if (Report.HasButtons)
        {
            parts.Add($"buttons={Report.Buttons}");
        }

        if (Report.HasAccelerometer)
        {
            parts.Add($"accel=({Report.AccelX:0.00}, {Report.AccelY:0.00}, {Report.AccelZ:0.00})g");
            parts.Add($"tilt=({Roll:0.#}deg, {Pitch:0.#}deg)");
        }

        if (Report.HasBattery)
        {
            parts.Add($"battery={WiimoteReportParser.BatteryPercent(Report.BatteryRaw)}%");
        }

        if (Report.HasStatusFlags)
        {
            parts.Add(Report.ExtensionConnected ? "extension=yes" : "extension=no");
        }

        if (Report.HasMemoryReply)
        {
            parts.Add($"read=0x{Report.MemoryOffset:X4} error=0x{Report.MemoryError:X2}");

            if (Report.MemoryData is { Length: > 0 } memoryData)
            {
                parts.Add($"data={Convert.ToHexString(memoryData)}");
            }
        }

        if (Report.HasAck)
        {
            parts.Add($"ack=0x{Report.AckReportId:X2} error=0x{Report.AckError:X2}");
        }

        if (Report.ExtensionData is { Length: > 0 } extensionData)
        {
            parts.Add($"extension={Convert.ToHexString(extensionData)}");
        }

        return $"{prefix} {string.Join(" ", parts)}";
    }

    private double Roll => Math.Atan2(Report!.AccelX, Report.AccelZ) * (180.0 / Math.PI);

    private double Pitch => Math.Atan2(Report!.AccelY, Report.AccelZ) * (180.0 / Math.PI);
}
