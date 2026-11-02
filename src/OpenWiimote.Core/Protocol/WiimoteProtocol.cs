namespace OpenWiimote.Core.Protocol;

public static class WiimoteProtocol
{
    public const int NintendoVendorId = 0x057E;
    public const int WiimoteProductId = 0x0306;
    public const int WiimotePlusProductId = 0x0330;

    public const byte SetLedRumbleReport = 0x11;
    public const byte SetDataReportingReport = 0x12;
    public const byte IrSet1Report = 0x13;
    public const byte RequestStatusReport = 0x15;
    public const byte IrSet2Report = 0x1A;
    public const byte WriteMemoryReport = 0x16;
    public const byte ReadMemoryReport = 0x17;

    /// <summary>
    /// The enable flag of the common output byte. It keeps continuous data reporting on in
    /// the data reporting report, and it selects the register namespace (rather than the
    /// EEPROM) in the read and write memory reports.
    /// </summary>
    public const byte EnableFlag = 0x04;

    /// <summary>
    /// Register that carries the encryption of the extension data. Writing
    /// <see cref="ExtensionEncryptionDisableValue"/> turns the encryption off.
    /// </summary>
    public const int ExtensionEncryptionRegister = 0xA400F0;
    public const byte ExtensionEncryptionDisableValue = 0x55;

    /// <summary>Second register of the sequence that turns the extension encryption off.</summary>
    public const int ExtensionEncryptionRegister2 = 0xA400FB;
    public const byte ExtensionEncryptionDisableValue2 = 0x00;

    /// <summary>Register that holds the six byte identifier of the plugged extension.</summary>
    public const int ExtensionIdRegister = 0xA400FA;
    public const int ExtensionIdLength = 6;

    /// <summary>
    /// First register of the six byte block that holds the gyro of the Wii Motion Plus. A
    /// Motion Plus that has not been activated keeps its registers in this block rather than
    /// in the block of the extension port, which is why the read goes here.
    /// </summary>
    public const int GyroBlockRegister = 0xA60000;

    /// <summary>
    /// Register that switches the IR camera on. 0x08 turns the camera on and starts the
    /// data transfer, 0x00 stops it again.
    /// </summary>
    public const int IrModeRegister = 0xB00030;
    public const byte IrModeEnable = 0x08;

    /// <summary>Register that selects how many IR bytes the camera produces.</summary>
    public const int IrModeNumberRegister = 0xB00033;

    /// <summary>Four points of two bytes, which is what the 0x36 and 0x37 reports carry.</summary>
    public const byte IrModeBasic = 0x01;

    /// <summary>Four points of three bytes with their size, which is what 0x33 carries.</summary>
    public const byte IrModeExtended = 0x03;

    /// <summary>Registers of the two sensitivity blocks of the IR camera.</summary>
    public const int IrSensitivityBlock1Register = 0xB00000;
    public const int IrSensitivityBlock2Register = 0xB0001A;

    /// <summary>
    /// The sensitivity blocks that the Linux <c>hid-wiimote</c> driver writes. The last
    /// byte of the first block and the second byte of the second one are what make the
    /// camera as sensitive as it can be.
    /// </summary>
    public static readonly byte[] IrSensitivityBlock1 =
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x90, 0x00, 0x41];

    public static readonly byte[] IrSensitivityBlock2 = [0x40, 0x00];

    public const byte DataReportButtonsAccelerometer = 0x31;
    public const byte DataReportButtonsExtension = 0x32;
    public const byte DataReportButtonsAccelerometerIr = 0x33;
    public const byte DataReportButtonsExtensionLong = 0x34;
    public const byte DataReportButtonsAccelerometerExtension = 0x35;
    public const byte DataReportButtonsIrExtension = 0x36;
    public const byte DataReportButtonsAccelerometerIrExtension = 0x37;

    public const byte Led1 = 0x10;
    public const byte Led2 = 0x20;
    public const byte Led3 = 0x40;
    public const byte Led4 = 0x80;
    public const byte Rumble = 0x01;

    public const int OutputReportLength = 22;
    public const int InputReportLength = 22;

    public static bool IsSupportedProduct(int productId)
    {
        return productId is WiimoteProductId or WiimotePlusProductId;
    }
}
