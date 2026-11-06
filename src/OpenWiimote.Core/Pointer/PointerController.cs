using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.Core.Pointer;

/// <summary>One explicitly selected remote owns the pointer. Output is always off on startup.</summary>
public sealed class PointerController : IDisposable
{
    private readonly object _gate = new();
    private readonly IPointerBackend _backend;
    private readonly Action<PointerPreferences> _save;
    private readonly TimeProvider _clock;
    private readonly Timer _watchdog;
    private readonly Dictionary<string, object> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(DateTimeOffset At, CameraPoint Point)> _samples = new();
    private readonly List<CameraPoint> _draft = [];
    private readonly HashSet<string> _suppressed = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? InputClaimed;
    private volatile HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);
    public bool OwnsInput(string key) => _claimed.Contains(key);
    private void PublishClaim()
    {
        var claimed = new HashSet<string>(_suppressed, StringComparer.OrdinalIgnoreCase);
        if ((_enabled || _calibrating) && _preferences.DeviceKey is { } key) claimed.Add(key);
        _claimed = claimed;
    }
    private PointerPreferences _preferences;
    private IPointerOutput? _output;
    private double[] _transform = [];
    private DateTimeOffset? _lastIr, _calibrationStarted;
    private long _lastFrame = -1;
    private IrPoint[] _irPoints = [];
    private bool _enabled, _calibrating, _armed, _previousA, _disposed, _visible;
    private string? _error, _calibrationDisplay;
    private double? _x, _y;

    public PointerController(IPointerBackend backend, PointerPreferences? preferences, Action<PointerPreferences> save,
        TimeProvider? clock = null)
    {
        _backend = backend; _save = save; _clock = clock ?? TimeProvider.System;
        _preferences = Copy(preferences ?? new());
        _watchdog = new Timer(_ => Tick(), null, 50, 50);
    }
    private static string Identity(string key) => HidDeviceIdentity.TryFormatBluetoothAddress(key) ?? key.Trim();
    private string CalibrationKey => Identity(_preferences.DeviceKey ?? "") + "|" + _preferences.DisplayId;
    private PointerDisplay? Display() => _backend.GetDisplays().FirstOrDefault(d => d.Id == _preferences.DisplayId);
    private bool Fresh => _lastIr is { } at && _clock.GetUtcNow() - at is var age && age >= TimeSpan.Zero && age <= TimeSpan.FromMilliseconds(250);
    private bool Stable => Fresh && _visible && _samples.Count >= 5 && _samples.Last().At - _samples.First().At >= TimeSpan.FromMilliseconds(175)
        && _samples.Max(s => s.Point.X) - _samples.Min(s => s.Point.X) <= 8 && _samples.Max(s => s.Point.Y) - _samples.Min(s => s.Point.Y) <= 8;
    private bool Calibrated(PointerDisplay? display) => display is not null && _preferences.Calibrations.TryGetValue(CalibrationKey, out var c)
        && c is not null && c.DisplayFingerprint == display.Fingerprint && PointerMath.TryTransform(c.Corners, out _);

    public PointerSnapshot Snapshot()
    {
        lock (_gate)
        {
            IReadOnlyList<PointerDisplay> displays;
            try { displays = _backend.GetDisplays(); } catch (Exception ex) { displays = []; _error = ex.Message; }
            var display = displays.FirstOrDefault(d => d.Id == _preferences.DisplayId);
            return new(_enabled, displays.Count > 0, _error, _preferences.DeviceKey, _preferences.DisplayId,
                Calibrated(display), _calibrating, _draft.Count, Stable, Fresh && _visible, _x, _y, displays)
            {
                IrPoints = Fresh ? _irPoints.ToArray() : [],
                LastIrReport = Fresh ? _lastIr : null
            };
        }
    }
    public void Connected(string key, object owner)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _connections[key] = owner;
            if (string.Equals(key, _preferences.DeviceKey, StringComparison.OrdinalIgnoreCase)) { Stop(); ResetSamples(); CancelCore(); }
        }
    }
    public void Disconnected(string key, object owner)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(key, out var current) || !ReferenceEquals(current, owner)) return;
            _connections.Remove(key);
            if (string.Equals(key, _preferences.DeviceKey, StringComparison.OrdinalIgnoreCase)) { Stop(); ResetSamples(); CancelCore(); }
        }
    }
    public void Select(string key, string displayId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.ContainsKey(key)) throw new ArgumentException("Choose a connected remote.");
            if (!_backend.GetDisplays().Any(d => d.Id == displayId)) throw new ArgumentException("Choose an available display.");
            Stop(); CancelCore(); ResetSamples();
            var next = Copy(_preferences); next.DeviceKey = key; next.DisplayId = displayId;
            _save(Copy(next)); _preferences = next; _error = null;
        }
    }
    public void Enable(bool enabled)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            if (!enabled) { CancelCore(); return; }
            var display = Display();
            if (!_connections.ContainsKey(_preferences.DeviceKey ?? "")) throw new ArgumentException("Connect the selected remote first.");
            if (!Calibrated(display)) throw new ArgumentException("Calibrate this remote for the selected display first.");
            if (!Fresh || !_visible) throw new ArgumentException("Aim at the powered sensor bar until two infrared points are visible.");
            CancelCore();
            PointerMath.TryTransform(_preferences.Calibrations[CalibrationKey].Corners, out _transform);
            _output = _backend.Create(display!); _enabled = true; _armed = false; _error = null;
            PublishClaim();
            InputClaimed?.Invoke(_preferences.DeviceKey!);
        }
    }
    public void BeginCalibration()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.ContainsKey(_preferences.DeviceKey ?? "") || Display() is null) throw new ArgumentException("Select a connected remote and display first.");
            Stop(); CancelCore(); ResetSamples(); _calibrating = true; _calibrationDisplay = Display()!.Fingerprint; _calibrationStarted = _clock.GetUtcNow(); _error = null;
            PublishClaim();
            InputClaimed?.Invoke(_preferences.DeviceKey!);
        }
    }
    public void CancelCalibration() { lock (_gate) { CancelCore(); Stop(); } }
    private void CancelCore() { if (_calibrating && _preferences.DeviceKey is { } key) _suppressed.Add(key); _calibrating = false; _draft.Clear(); _calibrationStarted = null; PublishClaim(); }
    public void CaptureCorner() { lock (_gate) CaptureCore(); }
    private void CaptureCore()
    {
        if (!_calibrating) throw new ArgumentException("Start calibration first.");
        if (!Stable) { _error = "Hold steady on the target with both sensor-bar points visible, then press A again."; return; }
        _draft.Add(new(_samples.Average(s => s.Point.X), _samples.Average(s => s.Point.Y))); _samples.Clear(); _error = null;
        if (_draft.Count != 4) return;
        try
        {
            var display = Display() ?? throw new ArgumentException("The display disconnected. Start calibration again.");
            if (display.Fingerprint != _calibrationDisplay) throw new ArgumentException("The display layout changed. Start calibration again.");
            if (!PointerMath.TryTransform(_draft.ToArray(), out _)) throw new ArgumentException("The four targets do not form a usable calibration. Previous calibration was kept; try again.");
            var next = Copy(_preferences); next.Calibrations[CalibrationKey] = new(display.Fingerprint, _draft.ToArray());
            _save(Copy(next)); _preferences = next;
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _calibrating = false; _calibrationStarted = null; if (_preferences.DeviceKey is { } key) _suppressed.Add(key); PublishClaim(); }
    }
    public void Update(string key, object owner, WiimoteState state)
    {
        lock (_gate)
        {
            if (_disposed || !_connections.TryGetValue(key, out var current) || !ReferenceEquals(owner, current)) return;
            if (state.Buttons == WiimoteButton.None && _suppressed.Remove(key)) PublishClaim();
            if (!string.Equals(key, _preferences.DeviceKey, StringComparison.OrdinalIgnoreCase)) return;
            if (state.Buttons.HasFlag(WiimoteButton.Home)) { Stop(); CancelCore(); _error = "Pointer stopped with Home."; return; }
            if (state.IrFrameNumber <= _lastFrame || state.LastIrReport is null) return;
            _lastFrame = state.IrFrameNumber; _lastIr = state.LastIrReport;
            _irPoints = state.IrPoints.Where(p => p is { IsValid: true } && p.X is >= 0 and <= 1023 && p.Y is >= 0 and <= 767).Take(4).ToArray();
            var a = state.Buttons.HasFlag(WiimoteButton.A); var b = state.Buttons.HasFlag(WiimoteButton.B);
            _visible = Fresh && PointerMath.TryMidpoint(state.IrPoints, out _);
            if (!_visible) { LoseTracking(); _previousA = a; return; }
            PointerMath.TryMidpoint(state.IrPoints, out var point);
            _samples.Enqueue((_lastIr.Value, point));
            while (_samples.Count > 0 && (_lastIr.Value - _samples.Peek().At > TimeSpan.FromMilliseconds(350) || _samples.Count > 100)) _samples.Dequeue();
            if (_calibrating && a && !_previousA) CaptureCore();
            _previousA = a;
            if (!_enabled || _output is null) return;
            if (!a && !b) _armed = true;
            try
            {
                if (!PointerMath.TryMap(point, _transform, out var x, out var y)) { LoseTracking(); return; }
                _x = x; _y = y;
                _output.Move((int)Math.Round(x * PointerMath.AxisMax), (int)Math.Round(y * PointerMath.AxisMax), _armed && a, _armed && b, true);
            }
            catch (Exception ex) { Stop(); _error = "Pointer output stopped: " + ex.Message; }
        }
    }
    private void ResetSamples() { _irPoints = []; _lastIr = null; _lastFrame = -1; _visible = false; _samples.Clear(); _previousA = false; _armed = false; _x = _y = null; }
    private void LoseTracking()
    {
        _samples.Clear(); _visible = false; _armed = false;
        try { _output?.Move(0, 0, false, false, false); } catch (Exception ex) { Stop(); _error = ex.Message; }
    }
    public void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!Fresh) LoseTracking();
            if (_calibrating && _calibrationStarted is { } at && _clock.GetUtcNow() - at > TimeSpan.FromMinutes(2)) { CancelCore(); _error = "Calibration timed out. Start again."; }
            if (!_enabled && !_calibrating) return;
            try
            {
                if (Display() is null || (_calibrating && Display()!.Fingerprint != _calibrationDisplay) || (_enabled && !Calibrated(Display()))) { Stop(); CancelCore(); _error = "Display layout changed. Select the display and calibrate again."; }
            }
            catch (Exception ex) { Stop(); CancelCore(); _error = ex.Message; }
        }
    }
    private void Stop()
    {
        if ((_enabled || _calibrating) && _preferences.DeviceKey is { } key) _suppressed.Add(key);
        _enabled = false; _armed = false; PublishClaim();
        var output = _output; _output = null;
        try { output?.Move(0, 0, false, false, false); } catch { }
        try { output?.Dispose(); } catch { }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; Stop(); CancelCore(); _watchdog.Dispose(); } }
    private static PointerPreferences Copy(PointerPreferences p) => new()
    {
        DeviceKey = p.DeviceKey, DisplayId = p.DisplayId,
        Calibrations = (p.Calibrations ?? new()).Where(pair => pair.Value is not null && PointerMath.TryTransform(pair.Value.Corners, out _))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .ToDictionary(pair => pair.Key, pair => pair.Value with { Corners = pair.Value.Corners.ToArray() }, StringComparer.OrdinalIgnoreCase)
    };
}
