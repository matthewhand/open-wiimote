namespace OpenWiimote.Core.Models;

/// <summary>
/// One virtual controller control and the Wiimote, Nunchuk and Classic Controller inputs
/// that press it. Every source is optional, so a control can be driven by any mix of them.
/// </summary>
public sealed class ControlMapping
{
    public string TargetName { get; set; } = string.Empty;
    public WiimoteButton? WiimoteButton { get; set; }
    public NunchukButton? NunchukButton { get; set; }
    public ClassicButton? ClassicButton { get; set; }
}
