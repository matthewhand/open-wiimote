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

            try
            {
                _client ??= new ViGEmClient();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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

            try
            {
                _client ??= new ViGEmClient();
                gamepad = new ViGEmVirtualGamepad(_client);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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
