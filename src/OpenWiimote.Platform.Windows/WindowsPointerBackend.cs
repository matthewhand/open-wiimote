using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenWiimote.Core.Pointer;

namespace OpenWiimote.Platform.Windows;

public sealed class WindowsPointerBackend : IPointerBackend
{
    public IReadOnlyList<PointerDisplay> GetDisplays()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var dpi = new DpiScope();
        var displays = new List<PointerDisplay>();
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var number = new string(info.Device.Where(char.IsDigit).ToArray());
            var name = "Display " + number + ((info.Bounds.Right - info.Bounds.Left) / (double)(info.Bounds.Bottom - info.Bounds.Top) > 2.1 ? " · Ultrawide" : "");
            displays.Add(new(info.Device, name, info.Bounds.Left, info.Bounds.Top,
                info.Bounds.Right - info.Bounds.Left, info.Bounds.Bottom - info.Bounds.Top));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return displays.OrderBy(d => d.X).ThenBy(d => d.Y).ToArray();
    }
    public IPointerOutput Create(PointerDisplay display) => new WindowsPointerOutput(display, GetDisplays,
        VirtualDesktop, Send);
    private static PointerDisplay VirtualDesktop()
    {
        using var dpi = new DpiScope();
        return new("virtual", "Virtual desktop", GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
    }
    private readonly struct DpiScope : IDisposable
    {
        private readonly nint _previous;
        public DpiScope() { _previous = SetThreadDpiAwarenessContext(-4); }
        public void Dispose() { if (_previous != 0) SetThreadDpiAwarenessContext(_previous); }
    }
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    public bool CheckOutputAtCurrentCursor()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var dpi = new DpiScope();
        if (!GetCursorPos(out var point)) return false;
        var display = GetDisplays().FirstOrDefault(d => point.X >= d.X && point.Y >= d.Y && point.X < d.X + d.Width && point.Y < d.Y + d.Height);
        if (display is null) return false;
        using var output = Create(display);
        output.Move((int)Math.Round((point.X - display.X) * 65535.0 / (display.Width - 1)),
            (int)Math.Round((point.Y - display.Y) * 65535.0 / (display.Height - 1)), false, false, true);
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    private static void Send(NativeInput[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) != inputs.Length)
            throw new IOException("Windows blocked pointer output. Use a normal desktop window at the same privilege level; protected or elevated windows cannot receive it.");
    }
    internal delegate bool MonitorCallback(nint monitor, nint hdc, nint rect, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public int Size; public Rect Bounds, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] internal struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
}

internal sealed class WindowsPointerOutput(PointerDisplay target, Func<IReadOnlyList<PointerDisplay>> displays,
    Func<PointerDisplay> virtualDesktop, Action<WindowsPointerBackend.NativeInput[]> send) : IPointerOutput
{
    private readonly object _gate = new();
    private bool _left, _right, _disposed;
    public void Move(int x, int y, bool left, bool right, bool aiming)
    {
        lock (_gate)
        {
            if (_disposed) return;
            uint flags = 0;
            var nextLeft = aiming && left; var nextRight = aiming && right;
            var dx = 0; var dy = 0;
            if (aiming)
            {
                if (!displays().Any(d => d.Fingerprint == target.Fingerprint)) throw new IOException("Selected display changed. Calibrate again.");
                var desktop = virtualDesktop();
                (dx, dy) = ToVirtualAxes(x, y, target, desktop);
                flags = 0x0001 | 0x8000 | 0x4000; // MOVE, ABSOLUTE, VIRTUALDESK
            }
            if (nextLeft != _left) flags |= nextLeft ? 0x0002u : 0x0004u;
            if (nextRight != _right) flags |= nextRight ? 0x0008u : 0x0010u;
            if (flags == 0) return;
            // On partial insertion, conservatively retain button ownership so cleanup
            // attempts a release even if SendInput reports failure after a button-down.
            _left |= nextLeft; _right |= nextRight;
            send([new() { Data = new() { Mouse = new() { X = dx, Y = dy, Flags = flags } } }]);
            _left = nextLeft; _right = nextRight;
        }
    }
    internal static (int X, int Y) ToVirtualAxes(int x, int y, PointerDisplay target, PointerDisplay desktop)
    {
        if (desktop.Width <= 1 || desktop.Height <= 1 || target.Width <= 1 || target.Height <= 1) throw new IOException("Invalid display geometry.");
        var px = target.X + Math.Clamp(x, 0, 65535) / 65535.0 * (target.Width - 1);
        var py = target.Y + Math.Clamp(y, 0, 65535) / 65535.0 * (target.Height - 1);
        return ((int)Math.Round(Math.Clamp((px - desktop.X) / (desktop.Width - 1), 0, 1) * 65535),
            (int)Math.Round(Math.Clamp((py - desktop.Y) / (desktop.Height - 1), 0, 1) * 65535));
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { Move(0, 0, false, false, false); } finally { _disposed = true; }
        }
    }
}
