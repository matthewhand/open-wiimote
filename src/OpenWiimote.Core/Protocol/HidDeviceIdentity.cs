using System.Text.RegularExpressions;
using HidSharp;

namespace OpenWiimote.Core.Protocol;

public static partial class HidDeviceIdentity
{
    /// <summary>
    /// True when the device is a Wii Remote. Bluetooth remotes report the adapter's ids
    /// through HidSharp, so the kernel's own HID id is used as a second opinion.
    /// </summary>
    public static bool IsWiimote(HidDevice device)
    {
        if (IsWiimoteIds(device.VendorID, device.ProductID))
        {
            return true;
        }

        var identity = HidSysfs.TryRead(device);
        return identity is not null && IsWiimoteIds(identity.VendorId, identity.ProductId);
    }

    /// <summary>The Nintendo identity of the device, or null when it is not a remote.</summary>
    public static HidSysfs.HidIdentity? GetWiimoteIdentity(HidDevice device)
    {
        var identity = HidSysfs.TryRead(device);
        return identity is not null && IsWiimoteIds(identity.VendorId, identity.ProductId) ? identity : null;
    }

    public static string GetKey(HidDevice device)
    {
        if (TryGetBluetoothAddress(device) is { } address)
        {
            return address;
        }

        try
        {
            var serial = device.GetSerialNumber();
            if (!string.IsNullOrWhiteSpace(serial))
            {
                return serial.Trim();
            }
        }
        catch
        {
        }

        return device.DevicePath ?? $"hid:{device.VendorID:X4}:{device.ProductID:X4}";
    }

    public static string GetDisplayName(HidDevice device)
    {
        // The kernel name comes first: a Bluetooth remote otherwise reports the name of
        // the Bluetooth adapter it is attached to.
        var identity = GetWiimoteIdentity(device);
        if (!string.IsNullOrWhiteSpace(identity?.Name))
        {
            return identity.Name.Trim();
        }

        try
        {
            var product = device.GetProductName();
            if (!string.IsNullOrWhiteSpace(product))
            {
                return product.Trim();
            }
        }
        catch
        {
        }

        var productId = identity?.ProductId ?? device.ProductID;
        return productId == WiimoteProtocol.WiimotePlusProductId
            ? "Nintendo RVL-CNT-01-TR"
            : "Nintendo RVL-CNT-01";
    }

    public static string? TryGetBluetoothAddress(HidDevice device)
    {
        try
        {
            var serial = device.GetSerialNumber();
            if (TryFormatBluetoothAddress(serial) is { } serialAddress)
            {
                return serialAddress;
            }
        }
        catch
        {
        }

        if (TryFormatBluetoothAddress(GetWiimoteIdentity(device)?.UniqueId) is { } uniqueAddress)
        {
            return uniqueAddress;
        }

        return TryGetBluetoothAddress(device.DevicePath);
    }

    public static string? TryGetBluetoothAddress(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return null;
        }

        var match = BluetoothAddressPattern().Match(devicePath);
        if (!match.Success)
        {
            return null;
        }

        return TryFormatBluetoothAddress(match.Groups[1].Value);
    }

    public static string? TryFormatBluetoothAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains(':') || trimmed.Contains('-'))
        {
            trimmed = trimmed.Replace(":", string.Empty).Replace("-", string.Empty);
        }

        if (trimmed.Length != 12 || !trimmed.All(Uri.IsHexDigit))
        {
            return null;
        }

        var upper = trimmed.ToUpperInvariant();
        return string.Join(":", Enumerable.Range(0, 6).Select(i => upper.Substring(i * 2, 2)));
    }

    private static bool IsWiimoteIds(int vendorId, int productId)
    {
        return vendorId == WiimoteProtocol.NintendoVendorId && WiimoteProtocol.IsSupportedProduct(productId);
    }

    [GeneratedRegex(@"(?:^|[^0-9A-Fa-f])([0-9A-Fa-f]{12})(?:[^0-9A-Fa-f]|$)")]
    private static partial Regex BluetoothAddressPattern();
}
