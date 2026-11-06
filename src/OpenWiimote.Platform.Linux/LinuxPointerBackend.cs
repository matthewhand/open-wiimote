using System.Runtime.InteropServices;
using OpenWiimote.Core.Pointer;
namespace OpenWiimote.Platform.Linux;

// uinput maps normalized absolute coordinates onto the desktop. This backend supports
// one calibrated desktop surface; Windows additionally supports selecting each monitor.
public sealed class LinuxPointerBackend : IPointerBackend
{
    private static readonly object Gate = new();
    private static bool _threadsInitialized;
    public IReadOnlyList<PointerDisplay> GetDisplays()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return [];
        lock (Gate) return ReadDisplays();
    }
    private static IReadOnlyList<PointerDisplay> ReadDisplays()
    {
        nint display = 0;
        try
        {
            if (!_threadsInitialized) { if (XInitThreads() == 0) return []; _threadsInitialized = true; }
            display = XOpenDisplay(0);
            if (display == 0) return [];
            var screen = XDefaultScreen(display);
            return [new("linux-desktop", "Whole desktop", 0, 0, XDisplayWidth(display, screen), XDisplayHeight(display, screen))];
        }
        catch (DllNotFoundException) { return []; }
        finally { if (display != 0) XCloseDisplay(display); }
    }
    public IPointerOutput Create(PointerDisplay display) => new UinputPointer();
    [DllImport("libX11.so.6")] private static extern int XInitThreads();
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] private static extern int XDisplayWidth(nint display, int screen);
    [DllImport("libX11.so.6")] private static extern int XDisplayHeight(nint display, int screen);
}
