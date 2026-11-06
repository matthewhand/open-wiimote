namespace OpenWiimote.Platform.Linux;

/// <summary>
/// The part of <c>linux/input-event-codes.h</c> that the virtual pad uses.
/// </summary>
/// <remarks>
/// The values are the numbers of the kernel interface, spelled out rather than derived from
/// a name, because the same numbers have to appear in the <c>UI_SET_*BIT</c> calls and in the
/// events that are written to the device.
/// </remarks>
internal static class LinuxInput
{
    public const ushort InputPropDirect = 0x01;
    public const ushort BtnLeft = 0x110;
    public const ushort BtnRight = 0x111;
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvAbs = 0x03;

    public const ushort SynReport = 0x00;

    public const ushort AbsX = 0x00;
    public const ushort AbsY = 0x01;
    public const ushort AbsZ = 0x02;
    public const ushort AbsRx = 0x03;
    public const ushort AbsRy = 0x04;
    public const ushort AbsRz = 0x05;
    public const ushort AbsHat0X = 0x10;
    public const ushort AbsHat0Y = 0x11;

    public const ushort BtnSouth = 0x130;
    public const ushort BtnEast = 0x131;
    public const ushort BtnNorth = 0x133;
    public const ushort BtnWest = 0x134;
    public const ushort BtnTL = 0x136;
    public const ushort BtnTR = 0x137;
    public const ushort BtnSelect = 0x13A;
    public const ushort BtnStart = 0x13B;
    public const ushort BtnMode = 0x13C;
    public const ushort BtnThumbL = 0x13D;
    public const ushort BtnThumbR = 0x13E;
}
