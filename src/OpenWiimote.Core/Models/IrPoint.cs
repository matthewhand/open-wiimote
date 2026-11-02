namespace OpenWiimote.Core.Models;

public sealed record IrPoint
{
    public bool IsValid { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Size { get; init; }
}
