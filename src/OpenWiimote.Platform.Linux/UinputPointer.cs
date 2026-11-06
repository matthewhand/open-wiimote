using System.Runtime.InteropServices;
using OpenWiimote.Core.Pointer;

namespace OpenWiimote.Platform.Linux;

/// <summary>
/// An absolute pointer on <c>/dev/uinput</c>. The desktop maps axis 0..65535 onto the screen.
/// It is a direct pointer, the same shape as the host's working absolute mouse. A pen tool
/// made the desktop treat it as a drawing tablet, which does not move the cursor.
/// </summary>
public sealed class UinputPointer : IPointerOutput
{
    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;
    private const ulong UiDevCreate = 0x5501;
    private const ulong UiDevDestroy = 0x5502;
    private const ulong UiDevSetup = 0x405C5503;
    private const ulong UiAbsSetup = 0x401C5504;
    private const ulong UiSetEvBit = 0x40045564;
    private const ulong UiSetKeyBit = 0x40045565;
    private const ulong UiSetAbsBit = 0x40045567;
    private const ulong UiSetPropBit = 0x4004556e;

    private readonly object _gate = new();
    private int _fd;
    private bool _disposed;
    private bool _aiming;
    private bool _left;
    private bool _right;
    private int _x = -1;
    private int _y = -1;

    public UinputPointer(string? label = null)
    {
        _fd = UinputGamepad.TryOpen(out var error);
        if (_fd < 0)
        {
            throw new IOException(error ?? "Unable to open /dev/uinput.");
        }

        try
        {
            Configure(label);
        }
        catch
        {
            UinputGamepad.CloseHandle(_fd);
            _fd = -1;
            throw;
        }
    }

    public bool IsConnected => !_disposed && _fd >= 0;

    public void Move(int x, int y, bool left, bool right, bool aiming)
    {
        lock (_gate)
        {
            if (_disposed || _fd < 0)
            {
                return;
            }

            var nextLeft = aiming && left;
            var nextRight = aiming && right;
            if (aiming == _aiming && nextLeft == _left && nextRight == _right && (!aiming || (x == _x && y == _y)))
            {
                return;
            }

            var events = new List<InputEvent>(6);
            if (aiming && x != _x)
            {
                events.Add(InputEvent.Abs(LinuxInput.AbsX, x));
            }

            if (aiming && y != _y)
            {
                events.Add(InputEvent.Abs(LinuxInput.AbsY, y));
            }

            if (nextLeft != _left)
            {
                events.Add(InputEvent.Key(LinuxInput.BtnLeft, nextLeft));
            }

            if (nextRight != _right)
            {
                events.Add(InputEvent.Key(LinuxInput.BtnRight, nextRight));
            }

            events.Add(InputEvent.Sync());
            WriteEvents(events.ToArray());
            _x = aiming ? x : _x;
            _y = aiming ? y : _y;
            _left = nextLeft;
            _right = nextRight;
            _aiming = aiming;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_fd >= 0)
            {
                try { Move(0, 0, false, false, false); } catch { }
            }
            _disposed = true;
            if (_fd < 0)
            {
                return;
            }

            try
            {
                Ioctl(_fd, UiDevDestroy, 0);
            }
            catch
            {
            }

            UinputGamepad.CloseHandle(_fd);
            _fd = -1;
        }
    }

    private unsafe void Configure(string? label)
    {
        SetBit(UiSetEvBit, LinuxInput.EvKey);
        SetBit(UiSetEvBit, LinuxInput.EvAbs);
        SetBit(UiSetPropBit, LinuxInput.InputPropDirect);
        SetBit(UiSetKeyBit, LinuxInput.BtnLeft);
        SetBit(UiSetKeyBit, LinuxInput.BtnRight);
        // 100 units per millimetre over 0..65535 is about 655 mm, close to the absolute
        // mouse this host already maps onto the screen.
        SetAbs(LinuxInput.AbsX, 0, PointerMath.AxisMax, 100);
        SetAbs(LinuxInput.AbsY, 0, PointerMath.AxisMax, 100);

        var name = string.IsNullOrWhiteSpace(label) ? "OpenWiimote Virtual Mouse" : "OpenWiimote Mouse " + label.Trim();
        if (name.Length > 79)
        {
            name = name[..79];
        }

        var setup = new UinputSetup
        {
            Id = new InputId { BusType = 0x0003, Vendor = 0x057E, Product = 0x0307, Version = 1 },
            Name = name,
            FfEffectsMax = 0
        };
        var setupPtr = Marshal.AllocHGlobal(Marshal.SizeOf<UinputSetup>());
        try
        {
            Marshal.StructureToPtr(setup, setupPtr, false);
            Ioctl(_fd, UiDevSetup, setupPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(setupPtr);
        }

        Ioctl(_fd, UiDevCreate, 0);
        Thread.Sleep(50);
    }

    private void SetBit(ulong request, ushort bit) => Ioctl(_fd, request, bit);

    private unsafe void SetAbs(ushort code, int minimum, int maximum, int resolution)
    {
        SetBit(UiSetAbsBit, code);
        var setup = new UinputAbsSetup
        {
            Code = code,
            Info = new InputAbsInfo { Minimum = minimum, Maximum = maximum, Resolution = resolution }
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<UinputAbsSetup>());
        try
        {
            Marshal.StructureToPtr(setup, ptr, false);
            Ioctl(_fd, UiAbsSetup, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private unsafe void WriteEvents(InputEvent[] events)
    {
        fixed (InputEvent* ptr = events)
        {
            var written = write(_fd, ptr, (nuint)(events.Length * sizeof(InputEvent)));
            if (written != events.Length * sizeof(InputEvent))
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new IOException($"Writing the virtual mouse failed: {Marshal.GetPInvokeErrorMessage(errno)}");
            }
        }
    }

    private static void Ioctl(int fd, ulong request, nint argument)
    {
        if (ioctl(fd, (nuint)request, argument) < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw new IOException($"uinput ioctl 0x{request:X} failed: {Marshal.GetPInvokeErrorMessage(errno)}");
        }
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, nint argument);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint write(int fd, InputEvent* buffer, nuint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct InputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct UinputSetup
    {
        public InputId Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string Name;

        public uint FfEffectsMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputAbsInfo
    {
        public int Value;
        public int Minimum;
        public int Maximum;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputAbsSetup
    {
        public ushort Code;
        public ushort Padding;
        public InputAbsInfo Info;
    }
}
