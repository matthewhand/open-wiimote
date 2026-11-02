using OpenWiimote.Core.Virtual;

namespace OpenWiimote.Platform.MacOS;

public sealed class MacOSGamepadProvider : IVirtualGamepadProvider
{
    private const string EntitlementMessage =
        "macOS virtual controllers require Apple's restricted " +
        "com.apple.developer.hid.virtual.device entitlement. " +
        "HID monitoring, diagnostics, LED and rumble controls work without it; " +
        "controller emulation stays disabled in unsigned builds.";

    public string BackendName => "macOS virtual HID";

    public string BackendDescription => EntitlementMessage;

    public bool Probe(out string? error)
    {
        error = EntitlementMessage;
        return false;
    }

    public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
    {
        gamepad = null;
        error = EntitlementMessage;
        return false;
    }

    public void Dispose()
    {
    }
}
