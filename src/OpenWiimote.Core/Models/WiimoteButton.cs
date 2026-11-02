namespace OpenWiimote.Core.Models;

[Flags]
public enum WiimoteButton : ushort
{
    None = 0x0000,
    Two = 0x0001,
    One = 0x0002,
    B = 0x0004,
    A = 0x0008,
    Minus = 0x0010,
    Home = 0x0080,
    Left = 0x0100,
    Right = 0x0200,
    Down = 0x0400,
    Up = 0x0800,
    Plus = 0x1000
}
