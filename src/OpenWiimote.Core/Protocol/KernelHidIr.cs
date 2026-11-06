using HidSharp;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Keeps the kernel's Wii Remote IR input device open.
/// </summary>
/// <remarks>
/// The Linux <c>hid-wiimote</c> driver powers the camera down until some program opens the
/// IR input device. Opening it is what makes the driver initialise the camera and switch
/// the remote into a report mode that includes the IR bytes. Those bytes then arrive on
/// the hidraw node. The events themselves are not read.
/// </remarks>
public sealed class KernelHidIr : IDisposable
{
    private FileStream? _stream;

    private KernelHidIr(FileStream stream, string eventPath)
    {
        _stream = stream;
        EventPath = eventPath;
    }

    /// <summary>The input device that is being held open, for example /dev/input/event18.</summary>
    public string EventPath { get; }

    public static bool IsIrName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.EndsWith(" IR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns an open handle, or null when no IR input device exists or it cannot be opened.
    /// </summary>
    public static KernelHidIr? TryOpen(HidDevice device)
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
                if (!File.Exists(nameFile) || !IsIrName(File.ReadAllText(nameFile).Trim()))
                {
                    continue;
                }

                foreach (var eventDirectory in Directory.EnumerateDirectories(inputDirectory, "event*"))
                {
                    var eventPath = Path.Combine("/dev/input", Path.GetFileName(eventDirectory));
                    var stream = new FileStream(eventPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return new KernelHidIr(stream, eventPath);
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
