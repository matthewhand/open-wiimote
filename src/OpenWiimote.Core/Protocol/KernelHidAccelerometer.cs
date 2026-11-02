using HidSharp;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Keeps the kernel's Wii Remote accelerometer device open.
/// </summary>
/// <remarks>
/// <para>
/// When the Linux <c>hid-wiimote</c> driver is bound, it owns the remote's request channel:
/// user space output reports are rejected by the remote, and the driver itself only asks for
/// accelerometer reports while its accelerometer input device is open. The remote therefore
/// stays in the buttons-only report mode (0x30) and no accelerometer ever arrives.
/// </para>
/// <para>
/// Holding that input device open makes the driver switch the remote to the buttons plus
/// accelerometer mode, and the reports are then available on the hidraw node like every
/// other report. The events themselves are not read: opening the device is what matters.
/// </para>
/// </remarks>
public sealed class KernelHidAccelerometer : IDisposable
{
    private FileStream? _stream;

    private KernelHidAccelerometer(FileStream stream, string eventPath)
    {
        _stream = stream;
        EventPath = eventPath;
    }

    /// <summary>The input device that is being held open, for example /dev/input/event17.</summary>
    public string EventPath { get; }

    public static bool IsAccelerometerName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.Contains("Accelerometer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns an open handle, or null when the kernel driver is not in charge, no
    /// accelerometer device exists, or the device cannot be opened.
    /// </summary>
    public static KernelHidAccelerometer? TryOpen(HidDevice device)
    {
        if (!OperatingSystem.IsLinux() ||
            HidSysfs.TryGetHidrawNode(device.DevicePath) is not { } node)
        {
            return null;
        }

        var inputRoot = Path.Combine("/sys/class/hidraw", node, "device", "input");

        try
        {
            foreach (var inputDirectory in Directory.EnumerateDirectories(inputRoot, "input*"))
            {
                var nameFile = Path.Combine(inputDirectory, "name");
                if (!File.Exists(nameFile) || !IsAccelerometerName(File.ReadAllText(nameFile).Trim()))
                {
                    continue;
                }

                foreach (var eventDirectory in Directory.EnumerateDirectories(inputDirectory, "event*"))
                {
                    var eventPath = Path.Combine("/dev/input", Path.GetFileName(eventDirectory));
                    var stream = new FileStream(eventPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return new KernelHidAccelerometer(stream, eventPath);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
