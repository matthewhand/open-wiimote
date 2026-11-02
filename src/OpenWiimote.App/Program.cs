using Avalonia;
using Avalonia.Fonts.Inter;
using OpenWiimote.App.Headless;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.App;

internal static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase)))
        {
            await HeadlessServer.RunAsync(args);
            return;
        }

        var replayIndex = Array.FindIndex(
            args,
            arg => string.Equals(arg, "--replay", StringComparison.OrdinalIgnoreCase));
        if (replayIndex >= 0)
        {
            Environment.ExitCode = ReplayReports(replayIndex + 1 < args.Length ? args[replayIndex + 1] : "-");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Decodes a capture of raw reports and prints one line per report. <c>-</c> reads the
    /// capture from standard input, so a log can be piped in.
    /// </summary>
    private static int ReplayReports(string source)
    {
        IEnumerable<string> lines;
        if (source == "-")
        {
            lines = Console.In.ReadToEnd().Split('\n');
        }
        else
        {
            if (!File.Exists(source))
            {
                Console.Error.WriteLine($"Replay file '{source}' was not found.");
                return 1;
            }

            lines = File.ReadLines(source);
        }

        var entries = WiimoteReportReplayer.Replay(lines);
        if (entries.Count == 0)
        {
            Console.Error.WriteLine("No reports found in the capture.");
            return 1;
        }

        foreach (var entry in entries)
        {
            Console.WriteLine(entry.Describe());
        }

        return entries.Any(entry => entry.Error is not null) ? 1 : 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
