/// <summary>
/// Reads and calibrates Wii Remote accelerometer values from raw HID report bytes.
/// </summary>
/// <remarks>
/// Extracts the accelerometer zero point and counts-per-gravity constants so that
/// the parsing logic can be tested independently with various raw byte configurations.
/// The Linux hid-wiimote driver reports 10-bit accelerometer values where:
/// - 0x200 (512) = no acceleration (zero gravity)
/// - One gravity unit = 104 counts (derived from EEPROM calibration: 0x16/0x20 zero,
///   0x1A/0x24 one gravity; real remote: 0x80 zero, 0x9A one gravity = 26 * 4 = 104 counts)
/// </remarks>
public static class AccelerometerReader
{
    /// <summary>
    /// The raw value at which the accelerometer reports zero gravity.
    /// </summary>
    public const int AccelerometerZero = 0x200;

    /// <summary>
    /// The number of counts that make up one gravity unit.
    /// One gravity = 104 counts (10-bit resolution: 0x80 zero, 0x9A one gravity = 26 * 4).
    /// </summary>
    public const float CountsPerGravity = 104f;

    /// <summary>
    /// Reads three 10-bit accelerometer axes from the raw report bytes.
    /// </summary>
    /// <param data="raw report bytes at offsets 1-5 as used by hid-wiimote driver"/>
    /// <param x="output: X axis value in gravity units (g)"/>
    /// <param y="output: Y axis value in gravity units (g)"/>
    /// <param z="output: Z axis value in gravity units (g)"/>
    public static void ReadAxes(ReadOnlySpan<byte> data, out float x, out float y, out float z)
    {
        var firstButtonByte = data[1];
        var secondButtonByte = data[2];

        var xRaw = (data[3] << 2) | ((firstButtonByte >> 5) & 0x03);
        var yRaw = (data[4] << 2) | ((secondButtonByte >> 4) & 0x02);
        var zRaw = (data[5] << 2) | ((secondButtonByte >> 5) & 0x02);

        x = AdjustAxis(xRaw);
        y = AdjustAxis(yRaw);
        z = AdjustAxis(zRaw);
    }

    /// <summary>
    /// Converts a 10-bit raw accelerometer value to gravity units.
    /// </summary>
    /// <remarks>
    /// Subtracts the zero point (0x200 = 512) and divides by counts-per-gravity (104).
    /// Result is positive gravity in the direction of the axis.
    /// </remarks>
    public static float AdjustAxis(int raw)
    {
        return (raw - AccelerometerZero) / CountsPerGravity;
    }
}