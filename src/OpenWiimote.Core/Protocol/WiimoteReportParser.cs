using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Protocol;

/// <summary>
/// Parses raw Wii Remote HID reports.
/// </summary>
/// <remarks>
/// <para>
/// Report layouts follow the byte offsets used by the Linux <c>hid-wiimote</c> driver
/// (<c>drivers/hid/hid-wiimote-core.c</c>). The driver dispatches strictly on the report
/// id byte at offset 0 and hands the remaining payload to per-report handlers, so the
/// layouts below are expressed as offsets into the raw report (payload offset + 1).
/// </para>
/// <para>
/// The button and accelerometer bit assignments are cross checked against the Dolphin
/// emulator (<c>WiimoteCommon/DataReport.cpp</c>) and the wiiuse library, which agree
/// with the kernel driver.
/// </para>
/// </remarks>
public static class WiimoteReportParser
{
    /// <summary>Status report: BB*2 LF 00 00 VV.</summary>
    public const byte StatusReport = 0x20;

    /// <summary>Data report, also used as the read-memory reply.</summary>
    public const byte DataReport = 0x21;

    /// <summary>Return report (reply to a request command).</summary>
    public const byte ReturnReport = 0x22;

    /// <summary>Core buttons only.</summary>
    public const byte CoreButtonsReport = 0x30;

    /// <summary>Core buttons and accelerometer.</summary>
    public const byte CoreButtonsAccelReport = 0x31;

    /// <summary>Core buttons and extension controller.</summary>
    public const byte CoreButtonsExtensionReport = 0x32;

    /// <summary>Core buttons, accelerometer and infrared pointer.</summary>
    public const byte CoreButtonsAccelIrReport = 0x33;

    /// <summary>Core buttons and extension controller (19 byte extension payload).</summary>
    public const byte CoreButtonsExtensionLongReport = 0x34;

    /// <summary>Core buttons, accelerometer and extension controller.</summary>
    public const byte CoreButtonsAccelExtensionReport = 0x35;

    /// <summary>Core buttons, infrared pointer and extension controller.</summary>
    public const byte CoreButtonsIrExtensionReport = 0x36;

    /// <summary>Core buttons, accelerometer, infrared pointer and extension controller.</summary>
    public const byte CoreButtonsAccelIrExtensionReport = 0x37;

    /// <summary>
    /// Buttons carried by the first button byte: D-pad and Plus. The two upper bits are
    /// reserved for the low bits of the X accelerometer value.
    /// </summary>
    private const int FirstButtonMask = 0x1F;

    /// <summary>
    /// Buttons carried by the second button byte: Two, One, B, A, Minus and Home. The two
    /// middle bits are reserved for the low bits of the Y and Z accelerometer values.
    /// </summary>
    private const int SecondButtonMask = 0x9F;

    /// <summary>Extension payload offset for <see cref="CoreButtonsExtensionReport"/>.</summary>
    private const int ShortExtensionOffset = 3;

    /// <summary>Extension payload length for <see cref="CoreButtonsExtensionReport"/>.</summary>
    private const int ShortExtensionLength = 8;

    /// <summary>Extension payload offset for <see cref="CoreButtonsAccelExtensionReport"/>.</summary>
    private const int AccelExtensionOffset = 6;

    /// <summary>Extension payload length for <see cref="CoreButtonsAccelExtensionReport"/>.</summary>
    private const int AccelExtensionLength = 16;

    /// <summary>Extension payload offset for <see cref="CoreButtonsExtensionLongReport"/>.</summary>
    private const int LongExtensionOffset = 3;

    /// <summary>Extension payload length for <see cref="CoreButtonsExtensionLongReport"/>.</summary>
    private const int LongExtensionLength = 19;

    /// <summary>Extension payload offset for <see cref="CoreButtonsIrExtensionReport"/>.</summary>
    private const int IrExtensionOffset = 13;

    /// <summary>Extension payload length for <see cref="CoreButtonsIrExtensionReport"/>.</summary>
    private const int IrExtensionLength = 9;

    /// <summary>Offset of the read bytes in the memory report, after the common input report.</summary>
    private const int MemoryPayloadOffset = 6;

    /// <summary>Extension payload offset for <see cref="CoreButtonsAccelIrExtensionReport"/>.</summary>
    private const int AccelIrExtensionOffset = 16;

    /// <summary>Extension payload length for <see cref="CoreButtonsAccelIrExtensionReport"/>.</summary>
    private const int AccelIrExtensionLength = 6;

    /// <summary>
    /// Battery code reported by a fully charged remote. The status report carries a
    /// battery code between 0 and 0xC8 (200), where 200 means fully charged.
    /// </summary>
    public const int MaxBatteryCode = 200;

    /// <summary>
    /// The raw value of an axis that does not accelerate. The zero point of the
    /// accelerometer is 0x80 in the byte that carries the eight high bits of an axis,
    /// which is 0x200 for the full ten bit value. The Linux <c>hid-wiimote</c> driver
    /// subtracts the same value from every axis.
    /// </summary>
    public const int AccelerometerZero = 0x200;

    /// <summary>
    /// The number of counts of the ten bit axis value that make up one gravity unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The remote stores the calibration of its accelerometer in its EEPROM: the zero
    /// point of every axis is at 0x16 and 0x20, the value that one gravity produces on
    /// that axis is at 0x1A and 0x24. Both are the high eight bits of the ten bit axis
    /// value. A real remote holds 0x80 for the zero point and 0x9A for one gravity, so
    /// one gravity is 0x9A - 0x80 = 26 bytes, which is 26 * 4 = 104 counts.
    /// </para>
    /// <para>
    /// The wiiuse library reads the same two values from the EEPROM and normalises the
    /// readings with them, and the Dolphin emulator writes the same pair into the EEPROM
    /// of the emulated remote, so the games see the same tilt on the emulator as on the
    /// hardware. 256 counts per gravity would only cover the +/- 2 g that fits into the
    /// ten bit range, while the ADXL330 of the remote is rated for at least +/- 3 g.
    /// </para>
    /// </remarks>
    public const float CountsPerGravity = 104f;

    public static WiimoteReport? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return null;
        }

        // Some Bluetooth connections wrap HID input reports with 0xA1 as the first byte.
        if (data[0] == 0xA1 && data.Length >= 3)
        {
            data = data.Slice(1);
        }

        // The report id always sits at offset 0. Matching a later byte would misread
        // unrelated reports (for example the 0x21 data report) as button reports.
        return data[0] switch
        {
            StatusReport => ParseStatus(data),
            DataReport => ParseMemoryReply(data),
            ReturnReport => ParseResult(data),
            CoreButtonsReport => ParseCoreButtons(data),
            CoreButtonsAccelReport => ParseCoreAccel(data),
            CoreButtonsExtensionReport => ParseCoreExtension(data, ShortExtensionOffset, ShortExtensionLength),
            CoreButtonsAccelIrReport => ParseCoreAccelIr(data),
            CoreButtonsExtensionLongReport => ParseCoreExtension(data, LongExtensionOffset, LongExtensionLength),
            CoreButtonsAccelExtensionReport => ParseCoreAccelExtension(data, AccelExtensionOffset, AccelExtensionLength),
            CoreButtonsIrExtensionReport => ParseCoreIrExtension(data),
            CoreButtonsAccelIrExtensionReport => ParseCoreAccelIrExtension(data),
            _ => null
        };
    }

    private static WiimoteReport ParseStatus(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            return new WiimoteReport { ReportId = StatusReport };
        }

        var flags = data[3];

        return new WiimoteReport
        {
            ReportId = StatusReport,
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasStatusFlags = true,
            ExtensionConnected = (flags & 0x02) != 0,
            LedState = (byte)((flags >> 4) & 0x0F),
            HasBattery = true,
            BatteryRaw = data[6]
        };
    }

    /// <summary>
    /// Parses the reply to a read memory request: BB BB SE OO OO DD*16, where SE carries
    /// the number of bytes read minus one in its upper nibble and the error code in its
    /// lower nibble, and OO OO is the low half of the offset that was read.
    /// </summary>
    private static WiimoteReport ParseMemoryReply(ReadOnlySpan<byte> data)
    {
        if (data.Length < MemoryPayloadOffset)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        var size = ((data[3] >> 4) & 0x0F) + 1;
        var available = Math.Min(size, data.Length - MemoryPayloadOffset);

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasMemoryReply = true,
            MemoryOffset = (data[4] << 8) | data[5],
            MemoryError = (byte)(data[3] & 0x0F),
            MemoryData = available > 0
                ? data.Slice(MemoryPayloadOffset, available).ToArray()
                : Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Parses the result report: BB BB RR EE, where RR is the output report that the
    /// remote answers and EE is zero when it was accepted.
    /// </summary>
    private static WiimoteReport ParseResult(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasAck = true,
            AckReportId = data[3],
            AckError = data[4]
        };
    }

    private static WiimoteReport ParseCoreButtons(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return new WiimoteReport { ReportId = data.Length > 0 ? data[0] : (byte)0 };
        }

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data)
        };
    }

    private static WiimoteReport ParseCoreAccel(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        AccelerometerReader.ReadAxes(data, out var x, out var y, out var z);

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasAccelerometer = true,
            AccelX = x,
            AccelY = y,
            AccelZ = z
        };
    }

    private static WiimoteReport ParseCoreExtension(ReadOnlySpan<byte> data, int extensionOffset, int extensionLength)
    {
        if (data.Length < 3)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        byte[]? extension = null;
        if (data.Length >= extensionOffset + extensionLength)
        {
            extension = data.Slice(extensionOffset, extensionLength).ToArray();
        }

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            ExtensionData = extension
        };
    }

    private static WiimoteReport ParseCoreAccelExtension(ReadOnlySpan<byte> data, int extensionOffset, int extensionLength)
    {
        if (data.Length < 6)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        AccelerometerReader.ReadAxes(data, out var x, out var y, out var z);

        byte[]? extension = null;
        if (data.Length >= extensionOffset + extensionLength)
        {
            extension = data.Slice(extensionOffset, extensionLength).ToArray();
        }

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasAccelerometer = true,
            AccelX = x,
            AccelY = y,
            AccelZ = z,
            ExtensionData = extension
        };
    }

    private static WiimoteReport ParseCoreAccelIr(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return new WiimoteReport { ReportId = data.Length > 0 ? data[0] : (byte)0 };
        }

        AccelerometerReader.ReadAxes(data, out var x, out var y, out var z);
        var irPoints = ReadExtendedIr(data, 6);

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasAccelerometer = true,
            AccelX = x,
            AccelY = y,
            AccelZ = z,
            IrPoints = irPoints
        };
    }

    private static WiimoteReport ParseCoreIrExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        byte[]? extension = null;
        if (data.Length >= IrExtensionOffset + IrExtensionLength)
        {
            extension = data.Slice(IrExtensionOffset, IrExtensionLength).ToArray();
        }
        // DRM_KIE carries ten IR bytes, which is the basic format with two points per
        // five bytes.
        var irPoints = ReadBasicIr(data, 3);

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            ExtensionData = extension,
            IrPoints = irPoints
        };
    }

    private static WiimoteReport ParseCoreAccelIrExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return new WiimoteReport { ReportId = data[0] };
        }

        AccelerometerReader.ReadAxes(data, out var x, out var y, out var z);
        byte[]? extension = null;
        if (data.Length >= AccelIrExtensionOffset + AccelIrExtensionLength)
        {
            extension = data.Slice(AccelIrExtensionOffset, AccelIrExtensionLength).ToArray();
        }

        // DRM_KAIE is BB*2 AA*3 II*10 EE*6, so the IR points start after the accelerometer
        // and use the basic format because the report only has ten bytes for them.
        var irPoints = ReadBasicIr(data, 6);

        return new WiimoteReport
        {
            ReportId = data[0],
            HasButtons = true,
            Buttons = ReadButtons(data),
            HasAccelerometer = true,
            AccelX = x,
            AccelY = y,
            AccelZ = z,
            ExtensionData = extension,
            IrPoints = irPoints
        };
    }

    /// <summary>
    /// Reads the four points of the extended format: three bytes per point, with the size
    /// of the point in the low nibble of the third byte.
    /// </summary>
    private static IrPoint[] ReadExtendedIr(ReadOnlySpan<byte> data, int startOffset)
    {
        var points = new IrPoint[4];
        for (var i = 0; i < 4; i++)
        {
            var baseOffset = startOffset + i * 3;
            if (data.Length < baseOffset + 3)
            {
                points[i] = new IrPoint { IsValid = false };
                continue;
            }

            var xLow = data[baseOffset];
            var yLow = data[baseOffset + 1];
            var high = data[baseOffset + 2];

            var x = xLow | ((high & 0x30) << 4);
            var y = yLow | ((high & 0xC0) << 2);
            var size = high & 0x0F;

            points[i] = CreatePoint(x, y, size);
        }
        return points;
    }

    /// <summary>
    /// Reads the four points of the basic format: two points per five bytes, with the two
    /// high bits of every coordinate packed into the third byte of each pair.
    /// </summary>
    private static IrPoint[] ReadBasicIr(ReadOnlySpan<byte> data, int startOffset)
    {
        var points = new IrPoint[4];
        for (var pair = 0; pair < 2; pair++)
        {
            var baseOffset = startOffset + pair * 5;
            if (data.Length < baseOffset + 5)
            {
                points[pair * 2] = new IrPoint { IsValid = false };
                points[(pair * 2) + 1] = new IrPoint { IsValid = false };
                continue;
            }

            var x1Low = data[baseOffset];
            var y1Low = data[baseOffset + 1];
            var shared = data[baseOffset + 2];
            var x2Low = data[baseOffset + 3];
            var y2Low = data[baseOffset + 4];

            points[pair * 2] = CreatePoint(
                x1Low | (((shared >> 4) & 0x03) << 8),
                y1Low | (((shared >> 6) & 0x03) << 8),
                0);

            points[(pair * 2) + 1] = CreatePoint(
                x2Low | ((shared & 0x03) << 8),
                y2Low | (((shared >> 2) & 0x03) << 8),
                0);
        }
        return points;
    }

    /// <summary>
    /// Builds one point. Slot that the camera does not see read as 0xFF bytes, which is
    /// 0x3FF on both axes because the coordinates are ten bits wide.
    /// </summary>
    private static IrPoint CreatePoint(int x, int y, int size)
    {
        return new IrPoint
        {
            IsValid = !(x == 0x3FF && y == 0x3FF),
            X = x,
            Y = y,
            Size = size
        };
    }

    /// <summary>
    /// Reads the button mask. The first payload byte holds the D-pad and Plus, the second
    /// one Two, One, B, A, Minus and Home. The bits that are not buttons carry the low
    /// bits of the accelerometer values in the reports that also contain an accelerometer.
    /// </summary>
    private static WiimoteButton ReadButtons(ReadOnlySpan<byte> data)
    {
        return (WiimoteButton)(((data[1] & FirstButtonMask) << 8) | (data[2] & SecondButtonMask));
    }

    

    public static int BatteryPercent(int raw)
    {
        return Math.Clamp(raw * 100 / MaxBatteryCode, 0, 100);
    }
}
