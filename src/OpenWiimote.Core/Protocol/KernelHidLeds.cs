namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Drives the player LEDs of a remote that is owned by the Linux <c>hid-wiimote</c> driver.
/// </summary>
/// <remarks>
/// A kernel owned remote rejects the user space LED output report, but the driver publishes
/// every player LED as a LED class device named after the HID device, for example
/// <c>0005:057E:0306.0003:blue:p0</c>. Writing its brightness is the supported way to switch
/// a LED on or off.
/// </remarks>
public sealed class KernelHidLeds
{
    private readonly string?[] _brightnessPaths;

    internal KernelHidLeds(string?[] brightnessPaths)
    {
        _brightnessPaths = brightnessPaths;
    }

    /// <summary>The number of player LEDs that could be located.</summary>
    public int Count => _brightnessPaths.Count(path => path is not null);

    /// <summary>
    /// Locates the LED devices that belong to the HID device behind the given hidraw node,
    /// or returns null when none of them is available.
    /// </summary>
    public static KernelHidLeds? TryCreate(string? devicePath)
    {
        if (!OperatingSystem.IsLinux() || HidSysfs.TryGetHidrawNode(devicePath) is not { } node)
        {
            return null;
        }

        var brightnessPaths = new string?[4];

        try
        {
            // /sys/class/hidraw/<node>/device is a link to the HID device, and the LED
            // devices link back to it, so both sides have to be resolved before comparing.
            var hidDirectory = new DirectoryInfo(Path.Combine("/sys/class/hidraw", node, "device"))
                .ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            if (hidDirectory is null)
            {
                return null;
            }

            foreach (var ledDirectory in Directory.EnumerateDirectories("/sys/class/leds"))
            {
                var index = TryParsePlayerIndex(Path.GetFileName(ledDirectory));
                if (index is null)
                {
                    continue;
                }

                var deviceLink = Path.Combine(ledDirectory, "device");
                if (!Directory.Exists(deviceLink))
                {
                    continue;
                }

                var target = new DirectoryInfo(deviceLink).ResolveLinkTarget(returnFinalTarget: true);
                if (target is null ||
                    !string.Equals(Path.GetFullPath(target.FullName), hidDirectory, StringComparison.Ordinal))
                {
                    continue;
                }

                var brightness = Path.Combine(ledDirectory, "brightness");
                if (File.Exists(brightness))
                {
                    brightnessPaths[index.Value] = brightness;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return brightnessPaths.Any(path => path is not null) ? new KernelHidLeds(brightnessPaths) : null;
    }

    /// <summary>Parses the player index out of a name like "0005:057E:0306.0003:blue:p2".</summary>
    public static int? TryParsePlayerIndex(string? ledName)
    {
        if (string.IsNullOrWhiteSpace(ledName))
        {
            return null;
        }

        const string Marker = ":blue:p";

        var marker = ledName.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        return int.TryParse(ledName[(marker + Marker.Length)..], out var index) && index is >= 0 and <= 3
            ? index
            : null;
    }

    /// <summary>
    /// Applies a Wiimote LED mask (0x10 is player 1) to the kernel LED devices.
    /// Returns false when at least one LED could not be written.
    /// </summary>
    public bool SetMask(byte mask)
    {
        var complete = true;

        for (var index = 0; index < _brightnessPaths.Length; index++)
        {
            var path = _brightnessPaths[index];
            if (path is null)
            {
                complete = false;
                continue;
            }

            var on = (mask & (0x10 << index)) != 0;

            try
            {
                // hid-wiimote player LEDs normally have max_brightness=1. Do not
                // write 255 to a binary LED or assume every LED uses the same range.
                var maximumPath = Path.Combine(Path.GetDirectoryName(path)!, "max_brightness");
                var maximum = int.TryParse(File.ReadAllText(maximumPath).Trim(), out var value) && value > 0 ? value : 1;
                File.WriteAllText(path, on ? maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0");
            }
            catch (IOException)
            {
                complete = false;
            }
            catch (UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return complete;
    }
}
