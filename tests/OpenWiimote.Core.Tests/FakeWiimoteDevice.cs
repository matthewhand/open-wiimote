using System.Collections.Concurrent;
using HidSharp;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// A Wii Remote that only exists inside the test process.
/// </summary>
/// <remarks>
/// <para>
/// The watcher, the connection and the report parser are the production ones; only the HID
/// device underneath them is replaced. That is what lets the whole chain run without
/// hardware, and it is the reason the fake builds its reports from the real protocol
/// constants instead of a recorded capture.
/// </para>
/// <para>
/// The fake answers what a real remote answers: it acknowledges every output report with a
/// return report, it answers a read of the identifier register with the identifier of the
/// extension that the test configured, and it answers a read of the Motion Plus block with
/// the gyro reading of the test. Everything else - the status reports and the data reports -
/// is queued by the test with <see cref="Queue"/>.
/// </para>
/// </remarks>
internal sealed class FakeWiimoteDevice : HidDevice
{
    private readonly BlockingCollection<byte[]> _input = new();
    private readonly List<byte[]> _written = [];
    private readonly object _writtenGate = new();
    private readonly string _devicePath;
    private volatile bool _detached;

    public FakeWiimoteDevice(
        string devicePath = "fake://020000000001",
        int vendorId = WiimoteProtocol.NintendoVendorId,
        int productId = WiimoteProtocol.WiimoteProductId)
    {
        _devicePath = devicePath;
        VendorID = vendorId;
        ProductID = productId;
    }

    /// <summary>When true the remote cannot be opened, the way a hidraw node without a rule behaves.</summary>
    public bool OpenFails { get; init; }
    public bool ReportLengthsFail { get; init; }
    public FakeHidStream? LastOpenedStream { get; private set; }

    /// <summary>The bytes the remote answers a read of 0xA400FA with, or null to answer nothing.</summary>
    public byte[]? ExtensionIdentifier { get; init; }

    /// <summary>
    /// The angular velocity that the Motion Plus registers of the remote carry. It defaults
    /// to a remote that lies still, which reads as the zero point of the gyro.
    /// </summary>
    public GyroReading Gyro { get; init; }

    /// <summary>The reports the connection and the UI sent to the remote, oldest first.</summary>
    public IReadOnlyList<byte[]> WrittenReports
    {
        get
        {
            lock (_writtenGate)
            {
                return _written.Select(report => report.ToArray()).ToArray();
            }
        }
    }

    public IReadOnlyList<byte> WrittenReportIds => WrittenReports.Select(report => report[0]).ToArray();

    public override int VendorID { get; }

    public override int ProductID { get; }

    public override int ReleaseNumberBcd => 0x0100;

    public override string DevicePath => _devicePath;

    public override string GetFileSystemName() => "fake";

    public override string GetFriendlyName() => "Fake Wii Remote";

    public override int GetMaxInputReportLength() => WiimoteProtocol.InputReportLength;

    public override int GetMaxOutputReportLength() => ReportLengthsFail
        ? throw new IOException("Synthetic report descriptor failure")
        : WiimoteProtocol.OutputReportLength;

    public override int GetMaxFeatureReportLength() => 0;

    /// <summary>
    /// Makes the remote behave like it was switched off: every further read fails the way a
    /// hidraw node that disappeared does. The device stays in the enumeration, so the watcher
    /// sees the failure of the stream rather than a device that is gone.
    /// </summary>
    public void Detach() => _detached = true;

    internal bool IsDetached => _detached;

    /// <summary>Queues one input report, which the next read hands to the connection.</summary>
    public void Queue(params byte[] report)
    {
        _input.Add(report);
    }

    /// <summary>Queues the status report that announces a plugged or unplugged extension.</summary>
    public void QueueStatusReport(
        bool extensionConnected,
        byte ledMask = WiimoteProtocol.Led1,
        WiimoteButton buttons = WiimoteButton.None)
    {
        // BB BB LF 00 00 VV: the two button bytes come first, the flags carry the LED state
        // and the extension flag, and the battery is fully charged.
        Queue(
            WiimoteReportParser.StatusReport,
            (byte)((int)buttons >> 8),
            (byte)((int)buttons & 0xFF),
            (byte)(ledMask | (extensionConnected ? 0x02 : 0x00)),
            0x00,
            0x00,
            0xC8);
    }

    /// <summary>
    /// Queues a report of buttons and accelerometer, which is the mode the connection asks
    /// for while no extension is plugged in.
    /// </summary>
    public void QueueButtonsAndAccelerometerReport(byte firstButtonByte = 0x00, byte secondButtonByte = 0x00)
    {
        // BB BB AA AA AA, with 0x80 as the zero point of every axis and 0x9A as one gravity.
        Queue(
            WiimoteReportParser.CoreButtonsAccelReport,
            firstButtonByte,
            secondButtonByte,
            0x80,
            0x80,
            0x9A);
    }

    /// <summary>
    /// Queues a report that carries the six extension bytes of the plugged controller, which
    /// is the mode the connection asks for while an extension is plugged in.
    /// </summary>
    public void QueueExtensionReport(ReadOnlySpan<byte> extensionData)
    {
        var report = new byte[WiimoteProtocol.InputReportLength];
        report[0] = WiimoteReportParser.CoreButtonsAccelExtensionReport;
        report[3] = 0x80;
        report[4] = 0x80;
        report[5] = 0x9A;
        extensionData[..Math.Min(extensionData.Length, 6)].CopyTo(report.AsSpan(6));
        Queue(report);
    }

    protected override DeviceStream OpenDeviceDirectly(OpenConfiguration openConfig)
    {
        if (OpenFails)
        {
            throw new UnauthorizedAccessException($"{_devicePath} is not readable by this user");
        }

        return LastOpenedStream = new FakeHidStream(this);
    }

    /// <summary>Takes the next queued report, or reports that none arrived within the timeout.</summary>
    internal bool TryTakeInput(TimeSpan timeout, out byte[]? report)
    {
        return _input.TryTake(out report, (int)timeout.TotalMilliseconds);
    }

    /// <summary>
    /// The most bytes one reply carries: the size nibble of the reply header counts up to
    /// sixteen, so a larger request has to be read in several goes.
    /// </summary>
    private const int MaximumReplyLength = 16;

    /// <summary>
    /// Answers a read of the two register blocks that the app asks for: the identifier of the
    /// extension at 0xA400FA and the gyro of the Motion Plus at 0xA60000. Reading anything
    /// else gets no answer, the way a read of memory that does not exist times out.
    /// </summary>
    private byte[]? TryRead(int register, int requestedSize)
    {
        var size = Math.Clamp(requestedSize, 0, MaximumReplyLength);

        return register switch
        {
            WiimoteProtocol.ExtensionIdRegister when ExtensionIdentifier is { } identifier =>
                Take(identifier, size),
            WiimoteProtocol.GyroBlockRegister => Take(EncodeGyro(Gyro), size),
            _ => null
        };
    }

    private static byte[] Take(byte[] memory, int size) => memory.AsSpan(0, Math.Min(size, memory.Length)).ToArray();

    /// <summary>
    /// Queues the reply to a read: BB BB SE OO OO DD..., where the upper nibble of SE is the
    /// number of bytes minus one, the lower nibble is the error code, and OO OO echoes the low
    /// two bytes of the register that was read.
    /// </summary>
    private void QueueMemoryReply(int register, byte[] data)
    {
        var reply = new byte[6 + data.Length];
        reply[0] = WiimoteReportParser.DataReport;
        reply[3] = (byte)(((data.Length - 1) & 0x0F) << 4);
        reply[4] = (byte)(register >> 8);
        reply[5] = (byte)register;
        data.CopyTo(reply, 6);
        Queue(reply);
    }

    /// <summary>
    /// Encodes a reading the way the Motion Plus stores it: the low byte of yaw, roll and
    /// pitch first, then their upper six bits together with the mode flags of the axes. The
    /// fake reports the slow mode on every axis, which is the mode of a remote that turns no
    /// faster than 440 degrees per second.
    /// </summary>
    private static byte[] EncodeGyro(GyroReading gyro)
    {
        var block = new byte[MotionPlusGyro.BlockLength];
        var yaw = RawGyro(gyro.Yaw);
        var roll = RawGyro(gyro.Roll);
        var pitch = RawGyro(gyro.Pitch);

        block[0] = (byte)yaw;
        block[1] = (byte)roll;
        block[2] = (byte)pitch;
        // Bits 5:0 of the last three bytes are the upper bits of the rates. Bit 6 of the third
        // byte is the slow mode flag of the yaw, bit 7 the one of the pitch, and bit 6 of the
        // fourth byte the one of the roll. Bit 7 of the fourth byte stays clear: no extension
        // hangs off this fake Motion Plus.
        block[3] = (byte)(((yaw >> 8) & 0x3F) | 0x40 | 0x80);
        block[4] = (byte)(((roll >> 8) & 0x3F) | 0x40);
        block[5] = (byte)((pitch >> 8) & 0x3F);
        return block;
    }

    /// <summary>Turns a rate into the raw fourteen bit value of a slow mode axis.</summary>
    private static int RawGyro(short degreesPerSecond)
    {
        var raw = MotionPlusGyro.ZeroPoint + (degreesPerSecond * MotionPlusGyro.CountsPerDegreePerSecond);
        return (int)Math.Round(Math.Clamp(raw, 0, 0x3FFF));
    }

    /// <summary>
    /// Records an output report and answers it the way the remote does: a write or a request
    /// is acknowledged with a return report, and a read is answered with the memory that
    /// <see cref="TryRead"/> knows about.
    /// </summary>
    internal void Write(byte[] buffer, int offset, int count)
    {
        if (count == 0)
        {
            return;
        }

        var report = buffer.AsSpan(offset, count).ToArray();
        lock (_writtenGate)
        {
            _written.Add(report);
        }

        switch (report[0])
        {
            case WiimoteProtocol.ReadMemoryReport when report.Length >= 7:
            {
                var register = (report[2] << 16) | (report[3] << 8) | report[4];
                var size = (report[5] << 8) | report[6];
                if (TryRead(register, size) is { } data)
                {
                    QueueMemoryReply(register, data);
                }

                break;
            }

            case WiimoteProtocol.WriteMemoryReport:
            case WiimoteProtocol.SetLedRumbleReport:
            case WiimoteProtocol.SetDataReportingReport:
            case WiimoteProtocol.IrSet1Report:
            case WiimoteProtocol.IrSet2Report:
                Queue(WiimoteReportParser.ReturnReport, 0x00, 0x00, report[0], 0x00);
                break;
        }
    }
}

/// <summary>
/// The input and output stream of a <see cref="FakeWiimoteDevice"/>: reads hand out the
/// queued reports and time out when the queue is empty, which is what makes the connection's
/// read loop keep asking.
/// </summary>
internal sealed class FakeHidStream : HidStream
{
    private readonly FakeWiimoteDevice _device;
    private bool _closed;

    public FakeHidStream(FakeWiimoteDevice device)
        : base(device)
    {
        _device = device;
    }

    public bool IsClosed => _closed;

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override bool CanTimeout => true;

    public override int ReadTimeout { get; set; } = 250;

    public override int WriteTimeout { get; set; } = 1000;

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_closed)
        {
            throw new ObjectDisposedException(nameof(FakeHidStream));
        }

        if (_device.IsDetached)
        {
            throw new IOException("the fake remote is no longer available");
        }

        if (!_device.TryTakeInput(TimeSpan.FromMilliseconds(Math.Max(ReadTimeout, 1)), out var report))
        {
            throw new TimeoutException("the fake remote had no report to send");
        }

        var length = Math.Min(count, report!.Length);
        report.AsSpan(0, length).CopyTo(buffer.AsSpan(offset));
        return length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_closed)
        {
            throw new ObjectDisposedException(nameof(FakeHidStream));
        }

        _device.Write(buffer, offset, count);
    }

    public override void GetFeature(byte[] buffer, int offset, int count)
    {
    }

    public override void SetFeature(byte[] buffer, int offset, int count)
    {
    }

    protected override void Dispose(bool disposing)
    {
        _closed = true;
        base.Dispose(disposing);
    }
}
