namespace OpenWiimote.Core.Models;

public sealed record ClassicControllerState
{
    public float LeftStickX { get; init; }
    public float LeftStickY { get; init; }
    public float RightStickX { get; init; }
    public float RightStickY { get; init; }
    public float LeftTrigger { get; init; }
    public float RightTrigger { get; init; }
    public bool ButtonA { get; init; }
    public bool ButtonB { get; init; }
    public bool ButtonX { get; init; }
    public bool ButtonY { get; init; }
    public bool ButtonL { get; init; }
    public bool ButtonR { get; init; }
    public bool ButtonZL { get; init; }
    public bool ButtonZR { get; init; }
    public bool ButtonMinus { get; init; }
    public bool ButtonPlus { get; init; }
    public bool ButtonHome { get; init; }
    public bool DPadUp { get; init; }
    public bool DPadDown { get; init; }
    public bool DPadLeft { get; init; }
    public bool DPadRight { get; init; }
}
