using System.Diagnostics;
using OpenWiimote.Core.Virtual;
using OpenWiimote.Platform.Linux;
using OpenWiimote.Platform.MacOS;
using OpenWiimote.Platform.Windows;

namespace OpenWiimote.App;

public static class PlatformBackend
{
    public static string PlatformName
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return "Windows";
            }

            if (OperatingSystem.IsLinux())
            {
                return "Linux";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "macOS";
            }

            return "Unknown platform";
        }
    }

    public static string BluetoothHint
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return "Choose Pair a Wiimote in Windows, press the red SYNC button and find your remote. " +
                       "The app handles its special PIN. After bonding, wake it with a single button press.";
            }

            if (OperatingSystem.IsLinux())
            {
                return "Plug the Wiimote into USB, or pair it with BlueZ (for example bluetoothctl). " +
                       "The remote is then exposed through hidraw.";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "Pair the Wiimote in System Settings > Bluetooth. " +
                       "Approve any Input Monitoring or Accessibility prompt.";
            }

            return "Pair the Wiimote using your operating system's Bluetooth settings.";
        }
    }

    public static IVirtualGamepadProvider CreateGamepadProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ViGEmGamepadProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return new UinputGamepadProvider();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSGamepadProvider();
        }

        return new UnsupportedGamepadProvider();
    }

    public static OpenWiimote.Core.Pointer.IPointerBackend CreatePointerBackend()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPointerBackend();
        if (OperatingSystem.IsLinux()) return new LinuxPointerBackend();
        return new NoPointerBackend();
    }
    private sealed class NoPointerBackend : OpenWiimote.Core.Pointer.IPointerBackend
    {
        public IReadOnlyList<OpenWiimote.Core.Pointer.PointerDisplay> GetDisplays() => [];
        public OpenWiimote.Core.Pointer.IPointerOutput Create(OpenWiimote.Core.Pointer.PointerDisplay display)
            => throw new PlatformNotSupportedException("Pointer output is unavailable on this platform.");
    }

    public static void OpenFolder(string path)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(path);
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(path);
            }

            Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private sealed class UnsupportedGamepadProvider : IVirtualGamepadProvider
    {
        public string BackendName => "Unavailable";

        public string BackendDescription => "No virtual controller backend is available on this platform.";

        public bool Probe(out string? error)
        {
            error = BackendDescription;
            return false;
        }

        public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
        {
            gamepad = null;
            error = BackendDescription;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
