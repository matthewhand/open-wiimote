namespace OpenWiimote.Core.Models;

public sealed record NunchukState
{
    public float StickX { get; init; }
    public float StickY { get; init; }
    public bool ButtonC { get; init; }
    public bool ButtonZ { get; init; }
    public float AccelX { get; init; }
    public float AccelY { get; init; }
    public float AccelZ { get; init; } = 1f;
}
