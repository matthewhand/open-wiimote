using OpenWiimote.Core.Virtual;

namespace OpenWiimote.Platform.MacOS;

public sealed class MacOSGamepadProvider : IVirtualGamepadProvider
{
    private const string UnavailableMessage =
        "macOS virtual-gamepad output is not implemented in this build. " +
        "Signing the app or granting permissions does not enable this placeholder. " +
        "HID monitoring and controls require separate native verification; " +
        "controller emulation remains unavailable.";

    public string BackendName => "macOS virtual HID";

    public string BackendDescription => UnavailableMessage;

    public bool Probe(out string? error)
    {
        error = UnavailableMessage;
        return false;
    }

    public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
    {
        gamepad = null;
        error = UnavailableMessage;
        return false;
    }

    public void Dispose()
    {
    }
}
