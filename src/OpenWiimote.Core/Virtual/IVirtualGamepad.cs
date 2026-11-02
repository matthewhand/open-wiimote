using OpenWiimote.Core.Mapping;

namespace OpenWiimote.Core.Virtual;

public interface IVirtualGamepad : IDisposable
{
    bool IsConnected { get; }
    void Submit(XboxGamepadState state);
    void SetBattery(int percent) { }
}
