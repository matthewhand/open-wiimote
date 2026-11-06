using Nefarius.ViGEm.Client;
using OpenWiimote.Core.Virtual;
using IVirtualGamepad = OpenWiimote.Core.Virtual.IVirtualGamepad;

namespace OpenWiimote.Platform.Windows;

public sealed class ViGEmGamepadProvider : IVirtualGamepadProvider
{
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private bool _disposed;

    public string BackendName => "ViGEmBus";

    public string BackendDescription =>
        "Creates an Xbox 360 controller through the ViGEmBus driver.";

    public bool Probe(out string? error)
    {
        error = null;

        lock (_gate)
        {
            if (_disposed)
            {
                error = "The emulation provider has been disposed.";
                return false;
            }

            if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            {
                error = "For virtual Xbox gamepads only: on Windows 11 ARM, use the x64 OpenWiimote build with the ARM64 ViGEmBus driver. Native ARM64 pointer mode works without that driver.";
                return false;
            }
            try
            {
                _client ??= new ViGEmClient();
                return true;
            }
            catch (Exception ex)
            {
                error = ex is Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException
                    ? "Install the official signed ViGEmBus driver, then re-enable emulation. Pointer mode works without it."
                    : "Windows virtual controller is unavailable: " + ex.Message;
                return false;
            }
        }
    }

    public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
    {
        gamepad = null;
        error = null;

        lock (_gate)
        {
            if (_disposed)
            {
                error = "The emulation provider has been disposed.";
                return false;
            }

            if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            {
                error = "For virtual Xbox gamepads only: on Windows 11 ARM, use the x64 OpenWiimote build with the ARM64 ViGEmBus driver. Native ARM64 pointer mode works without that driver.";
                return false;
            }
            try
            {
                _client ??= new ViGEmClient();
                gamepad = new ViGEmVirtualGamepad(_client);
                return true;
            }
            catch (Exception ex)
            {
                error = ex is Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException
                    ? "Install the official signed ViGEmBus driver, then re-enable emulation. Pointer mode works without it."
                    : "Windows virtual controller is unavailable: " + ex.Message;
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _client?.Dispose();
            }
            catch
            {
            }

            _client = null;
        }
    }
}
