namespace OpenWiimote.Core.Models;

public sealed class WiimoteReport
{
    public required byte ReportId { get; init; }
    public bool HasButtons { get; init; }
    public WiimoteButton Buttons { get; init; }
    public bool HasBattery { get; init; }
    public int BatteryRaw { get; init; }
    public bool HasAccelerometer { get; init; }
    public float AccelX { get; init; }
    public float AccelY { get; init; }
    public float AccelZ { get; init; }
    public bool HasStatusFlags { get; init; }
    public bool ExtensionConnected { get; init; }
    public byte LedState { get; init; }
    public byte[]? ExtensionData { get; init; }
    public IrPoint[]? IrPoints { get; init; }

    /// <summary>Set for the reply to a read memory request, input report 0x21.</summary>
    public bool HasMemoryReply { get; init; }

    /// <summary>The low two bytes of the read offset. The upper byte is known from the request.</summary>
    public int MemoryOffset { get; init; }

    /// <summary>Zero when the read succeeded, a non zero error code otherwise.</summary>
    public byte MemoryError { get; init; }

    /// <summary>The bytes that were read, shorter than the requested size at the end of memory.</summary>
    public byte[]? MemoryData { get; init; }

    /// <summary>Set for the result report, input report 0x22.</summary>
    public bool HasAck { get; init; }

    /// <summary>The output report that the result report belongs to.</summary>
    public byte AckReportId { get; init; }

    /// <summary>Zero when the output report was accepted, a non zero error code otherwise.</summary>
    public byte AckError { get; init; }
}
