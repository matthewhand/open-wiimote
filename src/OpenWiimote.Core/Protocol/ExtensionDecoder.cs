using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Decodes the extension payload of the data reports and identifies the plugged extension.
/// </summary>
/// <remarks>
/// The layouts follow the Dolphin emulator (<c>WiimoteEmu/Extension/Nunchuk.h</c> and
/// <c>Classic.h</c>), which is written against the same bit assignments that the Linux
/// <c>hid-wiimote</c> driver uses. All buttons of these controllers are active low, so a
/// cleared bit means that the button is pressed. The identifiers are the ones in the
/// extension table of WiiBrew, which is what the registers at 0xA400FA hold once the
/// encryption is off.
/// </remarks>
public static class ExtensionDecoder
{
    /// <summary>Identifier of a Nunchuk, read from <see cref="WiimoteProtocol.ExtensionIdRegister"/>.</summary>
    private static readonly byte[] NunchukId = [0x00, 0x00, 0xA4, 0x20, 0x00, 0x00];

    /// <summary>Identifier of a Classic Controller.</summary>
    private static readonly byte[] ClassicControllerId = [0x00, 0x00, 0xA4, 0x20, 0x01, 0x01];

    /// <summary>
    /// Identifier of a Classic Controller Pro. The controllers of the NES and SNES mini
    /// report the same one, because they use the same data format.
    /// </summary>
    private static readonly byte[] ClassicControllerProId = [0x01, 0x00, 0xA4, 0x20, 0x01, 0x01];

    /// <summary>
    /// A second identifier that this decoder used to take for a Classic Controller Pro. It is
    /// not in the WiiBrew table, but a controller that reports it should keep the Classic
    /// layout rather than fall through to an unknown extension, so it is still accepted.
    /// </summary>
    private static readonly byte[] ClassicControllerProLegacyId = [0x00, 0x00, 0xA4, 0x20, 0x01, 0x20];

    /// <summary>Identifier of a Wii Motion Plus that has been switched on, with nothing attached.</summary>
    private static readonly byte[] MotionPlusId = [0x00, 0x00, 0xA4, 0x20, 0x04, 0x05];

    /// <summary>
    /// Identifier of an inactive Wii Motion Plus. The built-in one of a Wii Remote Plus
    /// reports this until a game switches it on.
    /// </summary>
    private static readonly byte[] MotionPlusInactiveId = [0x00, 0x00, 0xA4, 0x20, 0x00, 0x05];

    /// <summary>Identifier of a Wii Motion Plus with a Nunchuk plugged into it.</summary>
    private static readonly byte[] MotionPlusNunchukId = [0x00, 0x00, 0xA4, 0x20, 0x05, 0x05];

    /// <summary>Identifier of a Wii Motion Plus with a Classic Controller plugged into it.</summary>
    private static readonly byte[] MotionPlusClassicId = [0x00, 0x00, 0xA4, 0x20, 0x07, 0x05];

    /// <summary>
    /// The zero point of the Nunchuk accelerometer. Its eight high bits are read from the
    /// report, so the full value is the ten bit value with 0x200 as the zero point.
    /// </summary>
    private const int NunchukAccelerometerZero = 0x200;

    /// <summary>
    /// The counts of the ten bit Nunchuk axis that make up one gravity. A physical Nunchuk
    /// reports 0x80 for zero gravity and 0xB3 for one gravity, so one gravity is
    /// (0xB3 - 0x80) * 4 = 204 counts.
    /// </summary>
    private const float NunchukCountsPerGravity = 204f;

    /// <summary>
    /// Maps the six byte extension identifier to the extension that it belongs to, or
    /// <see cref="WiimoteExtensionType.Unknown"/> when it is not one of the known ones.
    /// </summary>
    public static WiimoteExtensionType DetectType(ReadOnlySpan<byte> identifier)
    {
        if (identifier.Length < WiimoteProtocol.ExtensionIdLength)
        {
            return WiimoteExtensionType.Unknown;
        }

        var id = identifier[..WiimoteProtocol.ExtensionIdLength];

        if (id.SequenceEqual(NunchukId))
        {
            return WiimoteExtensionType.Nunchuk;
        }

        if (id.SequenceEqual(ClassicControllerId))
        {
            return WiimoteExtensionType.ClassicController;
        }

        if (id.SequenceEqual(ClassicControllerProId) || id.SequenceEqual(ClassicControllerProLegacyId))
        {
            return WiimoteExtensionType.ClassicControllerPro;
        }

        // A Motion Plus is the extension itself: what is plugged into it, if anything, sits
        // behind it, and the payload of that inner controller is not decoded yet. The type is
        // still reported, so the UI can say what is attached instead of guessing.
        if (id.SequenceEqual(MotionPlusId) ||
            id.SequenceEqual(MotionPlusInactiveId) ||
            id.SequenceEqual(MotionPlusNunchukId) ||
            id.SequenceEqual(MotionPlusClassicId))
        {
            return WiimoteExtensionType.MotionPlus;
        }

        return WiimoteExtensionType.Unknown;
    }

    /// <summary>
    /// The controller that a Motion Plus is passing through, or
    /// <see cref="WiimoteExtensionType.None"/> when the identifier is not a Motion Plus in one
    /// of its passthrough modes.
    /// </summary>
    public static WiimoteExtensionType DetectPassthroughType(ReadOnlySpan<byte> identifier)
    {
        if (identifier.Length < WiimoteProtocol.ExtensionIdLength)
        {
            return WiimoteExtensionType.None;
        }

        var id = identifier[..WiimoteProtocol.ExtensionIdLength];

        if (id.SequenceEqual(MotionPlusNunchukId))
        {
            return WiimoteExtensionType.Nunchuk;
        }

        return id.SequenceEqual(MotionPlusClassicId)
            ? WiimoteExtensionType.ClassicController
            : WiimoteExtensionType.None;
    }

    /// <summary>
    /// True when those six extension bytes hold the Motion Plus gyro rather than the
    /// controller plugged into it. In a passthrough mode the Motion Plus alternates its own
    /// frames with the frames of the inner controller, and bit 1 of the last byte says which
    /// of the two this is.
    /// </summary>
    public static bool IsMotionPlusFrame(ReadOnlySpan<byte> data)
    {
        return data.Length >= 6 && (data[5] & 0x02) != 0;
    }

    /// <summary>
    /// Decodes the six byte Nunchuk payload: stick X, stick Y, accelerometer X, Y and Z,
    /// and the button byte, which also carries the two low bits of every axis.
    /// </summary>
    public static NunchukState? DecodeNunchuk(byte[] data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        var buttons = data[5];

        return new NunchukState
        {
            StickX = Math.Clamp((data[0] - 0x80) / 127f, -1f, 1f),
            StickY = Math.Clamp((data[1] - 0x80) / 127f, -1f, 1f),
            ButtonC = (buttons & 0x02) == 0,
            ButtonZ = (buttons & 0x01) == 0,
            AccelX = NunchukAxis(data[2], (buttons >> 2) & 0x03),
            AccelY = NunchukAxis(data[3], (buttons >> 4) & 0x03),
            AccelZ = NunchukAxis(data[4], (buttons >> 6) & 0x03)
        };
    }

    /// <summary>
    /// Decodes the six bytes of a Nunchuk that sits behind a Motion Plus in Nunchuk
    /// passthrough mode. The layout is not the plain one: the least significant bit of every
    /// accelerometer axis is dropped, the remaining low bits move one place up, and the two
    /// buttons move two places down.
    /// </summary>
    public static NunchukState? DecodePassthroughNunchuk(byte[] data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        var buttons = data[5];

        // The low bits of the axes sit one position higher than in the plain layout: AX<1> at
        // bit 4, AY<1> at bit 5 and AZ<2:1> at bits 7:6, where bit 7 is the lower of the two.
        // Byte 4 carries AZ<9:3> and gives its lowest bit to the flag, and AZ<0> is dropped
        // by the passthrough, so it reads as zero.
        var azHigh = (byte)((data[4] & 0xFE) | ((buttons >> 6) & 0x01));

        return new NunchukState
        {
            StickX = Math.Clamp((data[0] - 0x80) / 127f, -1f, 1f),
            StickY = Math.Clamp((data[1] - 0x80) / 127f, -1f, 1f),
            // The buttons moved two places down, and they are active low like every other
            // extension button.
            ButtonC = (buttons & 0x08) == 0,
            ButtonZ = (buttons & 0x04) == 0,
            AccelX = NunchukAxis(data[2], (buttons >> 3) & 0x02),
            AccelY = NunchukAxis(data[3], (buttons >> 4) & 0x02),
            AccelZ = NunchukAxis(azHigh, (buttons >> 6) & 0x02)
        };
    }

    /// <summary>
    /// Decodes the six bytes of a Classic Controller that sits behind a Motion Plus in that
    /// passthrough mode.
    /// </summary>
    /// <remarks>
    /// The Classic layout survives the passthrough almost unchanged: the axes and the
    /// triggers stay where they are and only the low bit of each left stick axis carries
    /// something else, the two d-pad buttons that the plain layout keeps in the last byte.
    /// Putting those two bits back lets the plain decoder do the work.
    /// </remarks>
    public static ClassicControllerState? DecodePassthroughClassicController(byte[] data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        var normal = new byte[6];
        data.AsSpan(0, 6).CopyTo(normal);

        normal[5] = (byte)((data[5] & 0xFC) | (data[0] & 0x01) | ((data[1] & 0x01) << 1));

        // The bits those two buttons replaced belong to the left stick, whose low bit is
        // dropped in this mode anyway.
        normal[0] = (byte)(data[0] & 0xFE);
        normal[1] = (byte)(data[1] & 0xFE);

        return DecodeClassicController(normal);
    }

    private static float NunchukAxis(byte high, int low)
    {
        return (((high << 2) | low) - NunchukAccelerometerZero) / NunchukCountsPerGravity;
    }

    /// <summary>
    /// Decodes the six byte Classic Controller payload.
    /// </summary>
    /// <remarks>
    /// The two sticks are packed across the first three bytes: the left stick has six bits
    /// per axis with its X value in bits 0-5 of the first byte and its Y value in bits 0-5
    /// of the second one. The right stick has five bits per axis and is spread over the
    /// remaining bits. Both triggers are five bit values without a neutral point.
    /// </remarks>
    public static ClassicControllerState? DecodeClassicController(byte[] data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        var leftStickX = data[0] & 0x3F;
        var leftStickY = data[1] & 0x3F;
        var rightStickX = ((data[2] & 0x80) >> 7)
            | (((data[1] & 0xC0) >> 6) << 1)
            | (((data[0] & 0xC0) >> 6) << 3);
        var rightStickY = data[2] & 0x1F;
        var leftTrigger = ((data[3] & 0xE0) >> 5) | (((data[2] & 0x60) >> 5) << 3);
        var rightTrigger = data[3] & 0x1F;

        var buttons = (ushort)(data[4] | (data[5] << 8));
        bool Pressed(ushort mask) => (buttons & mask) == 0;

        return new ClassicControllerState
        {
            LeftStickX = Math.Clamp((leftStickX - 32) / 32f, -1f, 1f),
            LeftStickY = Math.Clamp((leftStickY - 32) / 32f, -1f, 1f),
            RightStickX = Math.Clamp((rightStickX - 16) / 16f, -1f, 1f),
            RightStickY = Math.Clamp((rightStickY - 16) / 16f, -1f, 1f),
            LeftTrigger = Math.Clamp(leftTrigger / 31f, 0f, 1f),
            RightTrigger = Math.Clamp(rightTrigger / 31f, 0f, 1f),
            ButtonA = Pressed(0x1000),
            ButtonB = Pressed(0x4000),
            ButtonX = Pressed(0x0800),
            ButtonY = Pressed(0x2000),
            ButtonL = Pressed(0x0020),
            ButtonR = Pressed(0x0002),
            ButtonZL = Pressed(0x8000),
            ButtonZR = Pressed(0x0400),
            ButtonMinus = Pressed(0x0010),
            ButtonPlus = Pressed(0x0004),
            ButtonHome = Pressed(0x0008),
            DPadUp = Pressed(0x0100),
            DPadDown = Pressed(0x0040),
            DPadLeft = Pressed(0x0200),
            DPadRight = Pressed(0x0080)
        };
    }
}
