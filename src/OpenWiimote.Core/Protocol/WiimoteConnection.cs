using HidSharp;
using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Protocol;

public sealed class WiimoteConnection : IDisposable
{
    private readonly HidDevice _device;
    private readonly HidStream _stream;
    private readonly byte[] _readBuffer;
    private readonly int _outputReportLength;
    private readonly int _statusRequestIntervalMs;
    private volatile bool _irEnabled;
    private byte _irCameraMode;
    private readonly object _writeGate = new();
    private readonly object _stateGate = new();
    private readonly KernelHidAccelerometer? _accelerometer;
    private readonly KernelHidLeds? _leds;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private WiimoteState _state = new();
    private byte _ledMask;
    private bool _rumbling;
    private int _disconnectedRaised;
    private bool _disposed;
    private WiimoteExtensionType _forcedExtensionType = WiimoteExtensionType.None;
    private WiimoteExtensionType _detectedExtensionType = WiimoteExtensionType.None;
    private WiimoteExtensionType _passthroughType = WiimoteExtensionType.None;
    private bool _extensionInitialized;
    private DateTimeOffset _lastExtensionInit = DateTimeOffset.MinValue;
    private byte _reportMode;

    private WiimoteConnection(
        HidDevice device,
        HidStream stream,
        string deviceKey,
        WiimoteWatcherOptions options)
    {
        _device = device;
        _stream = stream;
        DeviceKey = deviceKey;
        DevicePath = device.DevicePath ?? string.Empty;
        ProductName = HidDeviceIdentity.GetDisplayName(device);
        BluetoothAddress = HidDeviceIdentity.TryGetBluetoothAddress(device);
        _ledMask = options.LedMask;
        _statusRequestIntervalMs = options.StatusRequestIntervalMs;
        _irEnabled = options.IrEnabled;
        KernelDriver = HidSysfs.TryGetDriverName(device.DevicePath);
        var maxInput = device.GetMaxInputReportLength();
        _readBuffer = new byte[Math.Max(WiimoteProtocol.InputReportLength, maxInput)];

        var maxOutput = device.GetMaxOutputReportLength();
        _outputReportLength = maxOutput >= WiimoteProtocol.OutputReportLength
            ? maxOutput
            : WiimoteProtocol.OutputReportLength;

        _accelerometer = KernelDriver is null ? null : KernelHidAccelerometer.TryOpen(device);
        _leds = KernelDriver is null ? null : KernelHidLeds.TryCreate(device.DevicePath);
    }

    public string DeviceKey { get; }
    public string DevicePath { get; }

    /// <summary>
    /// The Linux kernel driver that owns the remote, for example "wiimote", or null when the
    /// device is driven from user space. A kernel owned remote rejects output reports, and the
    /// driver controls the report mode, the LEDs and the rumble on its own.
    /// </summary>
    public string? KernelDriver { get; }

    public bool IsKernelOwned => KernelDriver is not null;
    public string ProductName { get; }
    public string? BluetoothAddress { get; }
    public bool IsAlive { get; private set; } = true;
    public byte LedMask => _ledMask;
    public bool IsRumbling => _rumbling;
    public WiimoteState Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<WiimoteState>? SnapshotUpdated;
    public event EventHandler<WiimoteReport>? ReportReceived;
    public event EventHandler<WiimoteConnection>? Disconnected;
    public event EventHandler<string>? Log;

    public static WiimoteConnection Open(
        HidDevice device,
        WiimoteWatcherOptions options,
        string? deviceKey = null)
    {
        var stream = device.Open();
        try
        {
            stream.ReadTimeout = 250;
            stream.WriteTimeout = 1000;
            return new WiimoteConnection(device, stream, deviceKey ?? HidDeviceIdentity.GetKey(device), options);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Start()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null) return;
            _cts = new CancellationTokenSource();
            // Capture before scheduling: Dispose may run before the task is started.
            var token = _cts.Token;
            _runTask = Task.Run(() => RunAsync(token));
        }
    }

    public bool SetLedMask(byte mask)
    {
        _ledMask = (byte)(mask & 0xF0);

        if (IsKernelOwned)
        {
            var applied = _leds?.SetMask(_ledMask) ?? false;
            if (!applied)
            {
                Log?.Invoke(
                    this,
                    $"Could not switch the player LEDs of {ProductName}. The kernel owns the LEDs; " +
                    "install packaging/60-openwiimote.rules so they can be written.");
            }

            return applied;
        }

        return SendLedRumble();
    }

    /// <summary>
    /// Blinks the LED of one player twice, which tells the players apart without a label.
    /// </summary>
    public async Task ShowPlayerPatternAsync(int playerNumber)
    {
        playerNumber = Math.Clamp(playerNumber, 1, 4);
        byte pattern = (byte)(0x10 << (playerNumber - 1));

        SetLedMask(0);
        await Task.Delay(150);
        SetLedMask(pattern);
        await Task.Delay(250);
        SetLedMask(0);
        await Task.Delay(150);
        SetLedMask(pattern);
    }

    public async Task CycleLedsAsync()
    {
        for (int i = 0; i < 4; i++)
        {
            SetLedMask((byte)(0x10 << i));
            await Task.Delay(200);
        }
        SetLedMask(_ledMask);
    }

    public bool SetRumble(bool enabled)
    {
        _rumbling = enabled;

        // A kernel owned remote ignores the rumble output report; the kernel driver exposes
        // rumble through its force feedback device, which is not driven from here yet.
        return !IsKernelOwned && SendLedRumble();
    }

    public bool RequestStatus()
    {
        return IsKernelOwned || SendReport(WiimoteProtocol.RequestStatusReport);
    }

    public void SetExtensionType(WiimoteExtensionType extensionType)
    {
        _forcedExtensionType = extensionType;
    }

    /// <summary>The extension that the reports are decoded as, either forced or detected.</summary>
    private WiimoteExtensionType EffectiveExtensionType =>
        _forcedExtensionType != WiimoteExtensionType.None ? _forcedExtensionType : _detectedExtensionType;

    /// <summary>
    /// The extension the state reports. A Motion Plus in one of its passthrough modes hands
    /// its frames to the controller plugged into it, and that inner controller is the one the
    /// app can decode and map; the Motion Plus itself is named in the log instead.
    /// </summary>
    private WiimoteExtensionType ReportedExtensionType =>
        EffectiveExtensionType == WiimoteExtensionType.MotionPlus && _passthroughType != WiimoteExtensionType.None
            ? _passthroughType
            : EffectiveExtensionType;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);

            if (IsKernelOwned)
            {
                Log?.Invoke(
                    this,
                    $"The kernel {KernelDriver} driver owns {ProductName}; its reporting mode is left to the driver. " +
                    (_accelerometer is null
                        ? "The accelerometer device could not be opened, so only button reports will arrive."
                        : $"Accelerometer reporting is kept on through {_accelerometer.EventPath}.") +
                    (_leds is null
                        ? " The player LEDs are not writable from user space."
                        : $" Player LEDs are driven through the {_leds.Count} kernel LED devices."));
            }
            else
            {
                SetLedMask(_ledMask);

                if (_irEnabled)
                {
                    await EnableIrAsync().ConfigureAwait(false);
                }

                SetDataReporting();
                RequestStatus();
            }

            var lastStatusRequest = DateTimeOffset.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - lastStatusRequest >= TimeSpan.FromMilliseconds(_statusRequestIntervalMs))
                {
                    if (IsKernelOwned)
                    {
                        RefreshBatteryFromKernelDriver();
                    }
                    else
                    {
                        RequestStatus();
                    }

                    lastStatusRequest = DateTimeOffset.UtcNow;
                }

                int read;
                try
                {
                    read = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (read > 0)
                {
                    ProcessReport(_readBuffer.AsSpan(0, read));
                }
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Log?.Invoke(this, $"Read loop stopped for {ProductName}: {ex.Message}");
            }
        }
        finally
        {
            IsAlive = false;
            RaiseDisconnected();
        }
    }

    /// <summary>
    /// A kernel owned remote takes its status requests, so the battery level comes from the
    /// power supply the driver publishes instead.
    /// </summary>
    private void RefreshBatteryFromKernelDriver()
    {
        if (HidSysfs.TryReadBatteryCapacity(DevicePath) is not { } capacity)
        {
            return;
        }

        var raw = (int)Math.Round(capacity * WiimoteReportParser.MaxBatteryCode / 100.0);
        WiimoteState next;
        lock (_stateGate)
        {
            if (Math.Abs(_state.BatteryRaw - raw) <= 1)
            {
                return;
            }

            next = _state with
            {
                BatteryRaw = raw,
                BatteryPercent = WiimoteReportParser.BatteryPercent(raw)
            };
            _state = next;
        }

        SnapshotUpdated?.Invoke(this, next);
    }

    private void ProcessReport(ReadOnlySpan<byte> data)
    {
        var report = WiimoteReportParser.Parse(data);
        if (report is null)
        {
            return;
        }

        if (report.HasMemoryReply)
        {
            HandleMemoryReply(report);
        }

        if (report.HasAck && report.AckError != 0)
        {
            Log?.Invoke(
                this,
                $"{ProductName} rejected output report 0x{report.AckReportId:X2} with error 0x{report.AckError:X2}.");
        }

        // A write or the identifier reply can be lost while the extension is settling, so
        // the initialisation is repeated while the extension data arrives without an
        // identified extension. A reply with an identifier that is not known stops the
        // retries, because repeating them would not change the answer.
        if (!IsKernelOwned &&
            _extensionInitialized &&
            _forcedExtensionType == WiimoteExtensionType.None &&
            _detectedExtensionType == WiimoteExtensionType.None &&
            report.ExtensionData is { Length: >= 6 } &&
            DateTimeOffset.UtcNow - _lastExtensionInit > TimeSpan.FromSeconds(2))
        {
            InitializeExtension();
        }

        // A status report announces a newly plugged or removed extension. The remote stops
        // data reporting when that happens, and a new extension has to be initialised before
        // its reports can be decoded, so both cases are handled before the data mode is set.
        var extensionPlugged = report.HasStatusFlags &&
            report.ExtensionConnected &&
            !_extensionInitialized &&
            !IsKernelOwned;
        // A status report can still carry the last extension bytes. Detection has to be
        // cleared before those bytes are decoded, or the unplugged controller stays on screen.
        var extensionRemoved = report.HasStatusFlags &&
            !report.ExtensionConnected &&
            (_extensionInitialized ||
             _detectedExtensionType != WiimoteExtensionType.None ||
             _state.ExtensionType != WiimoteExtensionType.None ||
             _state.ClassicController is not null ||
             _state.Nunchuk is not null);

        WiimoteState next;
        lock (_stateGate)
        {
            next = _state;

            if (report.HasButtons)
            {
                next = next with { Buttons = report.Buttons };
            }

            if (report.HasBattery)
            {
                next = next with
                {
                    BatteryRaw = report.BatteryRaw,
                    BatteryPercent = WiimoteReportParser.BatteryPercent(report.BatteryRaw)
                };
            }

            if (report.HasAccelerometer)
            {
                next = next with
                {
                    AccelX = report.AccelX,
                    AccelY = report.AccelY,
                    AccelZ = report.AccelZ
                };
            }

            if (report.HasStatusFlags)
            {
                next = next with { ExtensionConnected = report.ExtensionConnected };
            }

            if (report.IrPoints != null)
            {
                next = next with { IrPoints = report.IrPoints };
            }

            if (extensionRemoved)
            {
                // The gyro lived in the Motion Plus that is gone, so the readings leave the
                // state together with it instead of staying frozen at the last value.
                ResetExtension();
                next = next with
                {
                    ExtensionType = _forcedExtensionType,
                    ExtensionConnected = false,
                    Nunchuk = null,
                    ClassicController = null,
                    GyroX = 0,
                    GyroY = 0,
                    GyroZ = 0
                };
            }

            next = DecodeExtension(report, next);

            next = next with { LastReport = DateTimeOffset.UtcNow };
            _state = next;
        }


        ReportReceived?.Invoke(this, report);
        SnapshotUpdated?.Invoke(this, next);

        if (extensionPlugged)
        {
            InitializeExtension();
        }

        // A status report resets the remote's data reporting mode back to buttons only,
        // so the data stream has to be re-armed. The Linux hid-wiimote driver does the same
        // thing when it receives a status report.
        if (report.ReportId == WiimoteReportParser.StatusReport && !IsKernelOwned)
        {
            RefreshIrCameraMode();
            SetDataReporting();
        }
    }

    /// <summary>
    /// Decodes the extension payload of a data report with the extension that was detected
    /// when the remote was initialised.
    /// </summary>
    private WiimoteState DecodeExtension(WiimoteReport report, WiimoteState next)
    {
        if (report.ExtensionData is not { Length: >= 6 } extensionData)
        {
            return next;
        }

        var extensionType = EffectiveExtensionType;

        if (extensionType == WiimoteExtensionType.None)
        {
            return next;
        }

        if (extensionType == WiimoteExtensionType.Nunchuk)
        {
            var nunchuk = ExtensionDecoder.DecodeNunchuk(extensionData);
            return nunchuk is null
                ? next
                : next with { ExtensionType = extensionType, Nunchuk = nunchuk, ClassicController = null };
        }

        if (extensionType is WiimoteExtensionType.ClassicController or WiimoteExtensionType.ClassicControllerPro)
        {
            var classic = ExtensionDecoder.DecodeClassicController(extensionData);
            return classic is null
                ? next
                : next with { ExtensionType = extensionType, ClassicController = classic, Nunchuk = null };
        }

        // A Motion Plus in a passthrough mode alternates its own gyro frames with the frames
        // of the controller plugged into it, so only every other report holds input the app
        // can use.
        if (extensionType == WiimoteExtensionType.MotionPlus &&
            _passthroughType != WiimoteExtensionType.None)
        {
            if (ExtensionDecoder.IsMotionPlusFrame(extensionData))
            {
                return next;
            }

            if (_passthroughType == WiimoteExtensionType.Nunchuk)
            {
                var innerNunchuk = ExtensionDecoder.DecodePassthroughNunchuk(extensionData);
                return innerNunchuk is null
                    ? next
                    : next with
                    {
                        ExtensionType = _passthroughType,
                        Nunchuk = innerNunchuk,
                        ClassicController = null
                    };
            }

            var innerClassic = ExtensionDecoder.DecodePassthroughClassicController(extensionData);
            return innerClassic is null
                ? next
                : next with
                {
                    ExtensionType = _passthroughType,
                    ClassicController = innerClassic,
                    Nunchuk = null
                };
        }

        return next with { ExtensionType = extensionType };
    }

    /// <summary>Handles the identifier of the plugged extension.</summary>
    private void HandleMemoryReply(WiimoteReport report)
    {
        if (report.MemoryError != 0)
        {
            return;
        }

        if (report.MemoryOffset != (WiimoteProtocol.ExtensionIdRegister & 0xFFFF) ||
            report.MemoryData is not { Length: >= WiimoteProtocol.ExtensionIdLength } identifier)
        {
            return;
        }

        if (IsBlankIdentifier(identifier))
        {
            // The registers read as all zero or all one while the encryption is still on,
            // so this is not an answer, only a hint to try the handshake again.
            Log?.Invoke(
                this,
                $"{ProductName}: the extension identifier is not readable yet; the handshake will be retried.");
            return;
        }

        _detectedExtensionType = ExtensionDecoder.DetectType(identifier);
        _passthroughType = ExtensionDecoder.DetectPassthroughType(identifier);

        WiimoteState next;
        lock (_stateGate)
        {
            next = _state with
            {
                ExtensionConnected = true,
                ExtensionType = ReportedExtensionType,
                Nunchuk = null,
                ClassicController = null
            };
            _state = next;
        }

        SnapshotUpdated?.Invoke(this, next);

        if (_detectedExtensionType == WiimoteExtensionType.Unknown)
        {
            Log?.Invoke(
                this,
                $"The extension of {ProductName} reported the unknown identifier " +
                string.Join(' ', identifier.Take(WiimoteProtocol.ExtensionIdLength).Select(b => $"{b:X2}")) +
                ". Its data is shown as an unknown extension.");
        }
        else if (_passthroughType != WiimoteExtensionType.None)
        {
            Log?.Invoke(
                this,
                $"{ProductName}: {_detectedExtensionType} extension detected with a {_passthroughType} " +
                "plugged into it. Controller frames are decoded; MotionPlus gyro support is experimental and unavailable.");
        }
        else
        {
            Log?.Invoke(this, $"{ProductName}: {_detectedExtensionType} extension detected.");
        }

        SetDataReporting();
    }

    private static bool IsBlankIdentifier(ReadOnlySpan<byte> identifier)
    {
        var allZero = true;
        var allOne = true;

        foreach (var value in identifier)
        {
            allZero &= value == 0x00;
            allOne &= value == 0xFF;
        }

        return allZero || allOne;
    }

    /// <summary>
    /// Turns the encryption of the extension data off, which is what makes the Nunchuk and
    /// the Classic Controller report their buttons and sticks. The remote encrypts the
    /// extension bytes until 0x55 is written to 0xA400F0 and 0x00 to 0xA400FB.
    /// </summary>
    private void InitializeExtension()
    {
        if (!_extensionInitialized)
        {
            Log?.Invoke(
                this,
                $"{ProductName}: extension initialisation started (encryption off, identifier read).");
        }

        _extensionInitialized = true;
        _lastExtensionInit = DateTimeOffset.UtcNow;

        WriteMemory(
            WiimoteProtocol.ExtensionEncryptionRegister,
            WiimoteProtocol.ExtensionEncryptionDisableValue);
        WriteMemory(
            WiimoteProtocol.ExtensionEncryptionRegister2,
            WiimoteProtocol.ExtensionEncryptionDisableValue2);

        ReadExtensionId();
    }

    private void ResetExtension()
    {
        _extensionInitialized = false;
        _detectedExtensionType = WiimoteExtensionType.None;
        _passthroughType = WiimoteExtensionType.None;
        _lastExtensionInit = DateTimeOffset.MinValue;
    }

    private bool WriteMemory(int offset, byte value) => WriteMemory(offset, [value]);

    private bool WriteMemory(int offset, byte[] data)
    {
        if (data.Length is 0 or > 16)
        {
            return false;
        }

        return SendReport(WiimoteProtocol.WriteMemoryReport, report =>
        {
            report[1] |= WiimoteProtocol.EnableFlag;
            report[2] = (byte)(offset >> 16);
            report[3] = (byte)(offset >> 8);
            report[4] = (byte)offset;
            report[5] = (byte)data.Length;
            data.CopyTo(report, 6);
        });
    }

    private bool ReadExtensionId()
    {
        return ReadMemory(WiimoteProtocol.ExtensionIdRegister, WiimoteProtocol.ExtensionIdLength);
    }

    /// <summary>
    /// Asks the remote for a block of its memory. The size field is four bits wide, so the
    /// remote answers a size of sixteen for a request of zero.
    /// </summary>
    private bool ReadMemory(int offset, byte length)
    {
        return SendReport(WiimoteProtocol.ReadMemoryReport, report =>
        {
            report[1] |= WiimoteProtocol.EnableFlag;
            report[2] = (byte)(offset >> 16);
            report[3] = (byte)(offset >> 8);
            report[4] = (byte)offset;
            report[5] = 0x00;
            report[6] = length;
        });
    }

    private bool SetDataReporting()
    {
        // The remote only fills the IR and extension bytes with a report mode that has room
        // for them, so the mode follows what is switched on and plugged in. 0x33 and 0x37
        // both carry the twelve bytes of the IR camera, 0x31 and 0x35 do not.
        var mode = (Snapshot.ExtensionConnected, _irEnabled) switch
        {
            (true, true) => WiimoteProtocol.DataReportButtonsAccelerometerIrExtension,
            (true, false) => WiimoteProtocol.DataReportButtonsAccelerometerExtension,
            (false, true) => WiimoteProtocol.DataReportButtonsAccelerometerIr,
            _ => WiimoteProtocol.DataReportButtonsAccelerometer
        };

        return SetReportMode(mode);
    }

    /// <summary>
    /// Switches the IR camera on or off while the connection is running and moves the data
    /// reporting mode to a mode with or without IR bytes.
    /// </summary>
    public bool SetIrEnabled(bool enabled)
    {
        if (IsKernelOwned || !IsAlive)
        {
            return false;
        }

        if (_irEnabled == enabled)
        {
            return true;
        }

        _irEnabled = enabled;

        // The camera needs a pause between the single register writes, so the sequence runs
        // in the background and the data reporting mode follows when it finishes.
        _ = ApplyIrAsync(enabled);
        return true;
    }

    private async Task ApplyIrAsync(bool enabled)
    {
        try
        {
            if (enabled)
            {
                await EnableIrAsync().ConfigureAwait(false);
            }
            else
            {
                DisableIr();
            }

            SetDataReporting();
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Could not change the IR camera of {ProductName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the initialisation order of the IR camera: enable both IR reports, switch the
    /// mode register on, write the two sensitivity blocks, select the format that matches
    /// the data reporting mode, and switch the mode register on again.
    /// </summary>
    /// <remarks>
    /// The camera settles into a random state when the bytes arrive back to back, so the
    /// writes are spaced by 50 ms the way WiiBrew documents it.
    /// </remarks>
    private async Task EnableIrAsync()
    {
        Log?.Invoke(this, $"{ProductName}: IR camera switched on (sensitivity blocks written).");

        SendReport(WiimoteProtocol.IrSet1Report, report => report[1] |= WiimoteProtocol.EnableFlag);
        await Task.Delay(50).ConfigureAwait(false);
        SendReport(WiimoteProtocol.IrSet2Report, report => report[1] |= WiimoteProtocol.EnableFlag);
        await Task.Delay(50).ConfigureAwait(false);

        WriteMemory(WiimoteProtocol.IrModeRegister, WiimoteProtocol.IrModeEnable);
        await Task.Delay(50).ConfigureAwait(false);
        WriteMemory(WiimoteProtocol.IrSensitivityBlock1Register, WiimoteProtocol.IrSensitivityBlock1);
        await Task.Delay(50).ConfigureAwait(false);
        WriteMemory(WiimoteProtocol.IrSensitivityBlock2Register, WiimoteProtocol.IrSensitivityBlock2);
        await Task.Delay(50).ConfigureAwait(false);

        _irCameraMode = CurrentIrCameraMode;
        WriteMemory(WiimoteProtocol.IrModeNumberRegister, _irCameraMode);
        await Task.Delay(50).ConfigureAwait(false);
        WriteMemory(WiimoteProtocol.IrModeRegister, WiimoteProtocol.IrModeEnable);
    }

    /// <summary>
    /// The 0x33 report has twelve IR bytes, so the camera has to produce the extended
    /// format. The 0x36 and 0x37 reports only have ten bytes, which fit the basic format.
    /// </summary>
    private byte CurrentIrCameraMode => Snapshot.ExtensionConnected
        ? WiimoteProtocol.IrModeBasic
        : WiimoteProtocol.IrModeExtended;

    /// <summary>
    /// Re-selects the IR format after an extension was plugged in or removed, because the
    /// report mode and the camera format have to match byte for byte.
    /// </summary>
    private void RefreshIrCameraMode()
    {
        if (!_irEnabled)
        {
            return;
        }

        var desired = CurrentIrCameraMode;
        if (desired == _irCameraMode)
        {
            return;
        }

        _irCameraMode = desired;
        WriteMemory(WiimoteProtocol.IrModeNumberRegister, desired);
        WriteMemory(WiimoteProtocol.IrModeRegister, WiimoteProtocol.IrModeEnable);
    }

    private void DisableIr()
    {
        _irCameraMode = 0;
        WriteMemory(WiimoteProtocol.IrModeRegister, 0x00);
        SendReport(WiimoteProtocol.IrSet1Report);
        SendReport(WiimoteProtocol.IrSet2Report);
    }

    public bool SetReportMode(byte reportMode)
    {
        if (IsKernelOwned)
        {
            return false;
        }

        var sent = SendReport(WiimoteProtocol.SetDataReportingReport, report =>
        {
            // The enable flag keeps continuous reporting on. Without it the remote only
            // sends a report after the data changed, so a remote that lies still would look
            // disconnected to the watcher.
            report[1] = WiimoteProtocol.EnableFlag;
            report[2] = reportMode;
        });

        if (sent && reportMode != _reportMode)
        {
            _reportMode = reportMode;
            Log?.Invoke(this, $"{ProductName}: data reporting mode 0x{reportMode:X2} in use.");
        }

        return sent;
    }

    private bool SendLedRumble()
    {
        return SendReport(WiimoteProtocol.SetLedRumbleReport, report =>
        {
            report[1] = (byte)(_ledMask | (_rumbling ? WiimoteProtocol.Rumble : 0));
        });
    }

    private bool SendReport(byte reportId, Action<byte[]>? fill = null)
    {
        if (_disposed)
        {
            return false;
        }

        var report = new byte[_outputReportLength];
        report[0] = reportId;
        fill?.Invoke(report);

        if (_rumbling)
        {
            report[1] |= WiimoteProtocol.Rumble;
        }
        else
        {
            report[1] &= 0xFE;
        }

        try
        {
            lock (_writeGate)
            {
                _stream.Write(report);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Write failed for {ProductName}: {ex.Message}");
            return false;
        }
    }

    private void RaiseDisconnected()
    {
        if (Interlocked.Exchange(ref _disconnectedRaised, 1) != 0)
        {
            return;
        }

        Disconnected?.Invoke(this, this);
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            IsAlive = false;
        }

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _stream.Dispose();
        }
        catch
        {
        }

        _accelerometer?.Dispose();
        _cts?.Dispose();
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(BluetoothAddress)
            ? ProductName
            : $"{ProductName} ({BluetoothAddress})";
    }
}
