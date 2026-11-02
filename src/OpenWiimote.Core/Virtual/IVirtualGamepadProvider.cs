namespace OpenWiimote.Core.Virtual;

public interface IVirtualGamepadProvider : IDisposable
{
    string BackendName { get; }
    string BackendDescription { get; }

    bool Probe(out string? error);
    bool TryCreate(out IVirtualGamepad? gamepad, out string? error);
}
