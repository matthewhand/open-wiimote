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
        if (args.Length == 2 && args[0] == "--windows-scan")
        {
            try { File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(OpenWiimote.Platform.Windows.WindowsWiimotePairing.Scan())); }
            catch (Exception ex) { File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message })); Environment.ExitCode = 2; }
            return;
        }
        if (args.Length == 4 && args[0] == "--windows-pair")
        {
            try { File.WriteAllText(args[3], System.Text.Json.JsonSerializer.Serialize(OpenWiimote.Platform.Windows.WindowsWiimotePairing.Pair(args[1], args[2]))); }
            catch (Exception ex) { File.WriteAllText(args[3], System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message })); Environment.ExitCode = 2; }
            return;
        }

        var diagnosticIndex = Array.FindIndex(args, arg => arg == "--diagnostics");
        if (diagnosticIndex >= 0)
        {
            if (diagnosticIndex + 1 >= args.Length) { Environment.ExitCode = 2; return; }
            using var provider = PlatformBackend.CreateGamepadProvider();
            var gamepadAvailable = provider.Probe(out var gamepadError);
            var backend = PlatformBackend.CreatePointerBackend();
            var displays = backend.GetDisplays();
            bool? pointerOutput = null; string? pointerError = null;
            if (args.Contains("--check-pointer-output") && backend is OpenWiimote.Platform.Windows.WindowsPointerBackend windows)
            {
                try { pointerOutput = windows.CheckOutputAtCurrentCursor(); } catch (Exception ex) { pointerOutput = false; pointerError = ex.Message; }
            }
            bool? gamepadOutput = null; string? gamepadOutputError = null;
            if (args.Contains("--check-gamepad-output") && OperatingSystem.IsWindows())
                gamepadOutput = OpenWiimote.Platform.Windows.WindowsGamepadProbe.Check(out gamepadOutputError);
            var report = new
            {
                platform = PlatformBackend.PlatformName,
                osArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                sourceVersion = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
                gamepadAvailable, gamepadError, pointerAvailable = displays.Count > 0, displays, pointerOutput, pointerError,
                gamepadOutput, gamepadOutputError,
                physicalControllerAcceptance = "pending"
            };
            File.WriteAllText(args[diagnosticIndex + 1], System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

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

        var ownsDesktop = true;
        using var desktopMutex = OperatingSystem.IsWindows() ? new Mutex(true, "Local\\OpenWiimote.Desktop", out ownsDesktop) : null;
        if (!ownsDesktop) return;
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
