using System.Globalization;
using System.Text.RegularExpressions;
using HidSharp;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Reads the identification a Linux kernel HID device reports for itself.
///
/// A remote that is paired over Bluetooth hangs off the Bluetooth adapter, so the HID
/// device inherits the adapter's USB vendor and product ids: HidSharp reports a MediaTek
/// (0x0E8D) or Intel (0x8087) adapter instead of Nintendo (0x057E). The real ids are in
/// the <c>HID_ID</c> line of the device's sysfs <c>uevent</c> file, which is readable
/// without opening the hidraw node.
/// </summary>
public static partial class HidSysfs
{
    public sealed record HidIdentity(ushort VendorId, ushort ProductId, string? Name, string? UniqueId, string? Modalias);

    public static HidIdentity? TryRead(HidDevice device)
    {
        return TryRead(device.DevicePath);
    }

    public static HidIdentity? TryRead(string? devicePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        if (TryGetHidrawNode(devicePath) is not { } node)
        {
            return null;
        }

        try
        {
            var uevent = Path.Combine("/sys/class/hidraw", node, "device", "uevent");
            return File.Exists(uevent) ? ParseUevent(File.ReadAllText(uevent)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The battery percentage the kernel driver reports for the remote, or null when it
    /// exposes none.
    /// </summary>
    public static int? TryReadBatteryCapacity(string? devicePath)
    {
        if (!OperatingSystem.IsLinux() || TryGetHidrawNode(devicePath) is not { } node)
        {
            return null;
        }

        var root = Path.Combine("/sys/class/hidraw", node, "device", "power_supply");

        try
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var capacityFile = Path.Combine(directory, "capacity");
                if (!File.Exists(capacityFile))
                {
                    continue;
                }

                if (int.TryParse(File.ReadAllText(capacityFile).Trim(), out var capacity))
                {
                    return Math.Clamp(capacity, 0, 100);
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

    /// <summary>The name of the kernel driver bound to the HID device, for example "wiimote".</summary>
    public static string? TryGetDriverName(string? devicePath)
    {
        if (!OperatingSystem.IsLinux() || TryGetHidrawNode(devicePath) is not { } node)
        {
            return null;
        }

        try
        {
            var link = Path.Combine("/sys/class/hidraw", node, "device", "driver");
            if (!Directory.Exists(link))
            {
                return null;
            }

            return new DirectoryInfo(link).ResolveLinkTarget(returnFinalTarget: true)?.Name;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string? TryGetHidrawNode(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return null;
        }

        var match = HidrawNodePattern().Match(devicePath);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static HidIdentity? ParseUevent(string? contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        ushort vendorId = 0;
        ushort productId = 0;
        var hasId = false;
        string? name = null;
        string? uniqueId = null;
        string? modalias = null;

        foreach (var rawLine in contents.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            switch (line[..separator])
            {
                case "HID_ID":
                    hasId = TryParseHidId(value, out vendorId, out productId);
                    break;
                case "HID_NAME":
                    name = value.Length == 0 ? null : value;
                    break;
                case "HID_UNIQ":
                    uniqueId = value.Length == 0 ? null : value;
                    break;
                case "MODALIAS":
                    modalias = value.Length == 0 ? null : value;
                    break;
            }
        }

        return hasId ? new HidIdentity(vendorId, productId, name, uniqueId, modalias) : null;
    }

    /// <summary>Parses a <c>bus:vendor:product</c> HID id, each field eight hex digits.</summary>
    public static bool TryParseHidId(string? value, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;

        var parts = value?.Split(':');
        if (parts is not { Length: 3 })
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendor) ||
            !int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var product))
        {
            return false;
        }

        if (vendor is <= 0 or > 0xFFFF || product is <= 0 or > 0xFFFF)
        {
            return false;
        }

        vendorId = (ushort)vendor;
        productId = (ushort)product;
        return true;
    }

    [GeneratedRegex(@"(?:^|/)(hidraw\d+)(?:/|$)")]
    private static partial Regex HidrawNodePattern();
}
