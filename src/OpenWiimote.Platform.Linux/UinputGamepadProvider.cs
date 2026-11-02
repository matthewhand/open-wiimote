using System.Runtime.InteropServices;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Virtual;

namespace OpenWiimote.Platform.Linux;

public sealed class UinputGamepadProvider : IVirtualGamepadProvider
{
    public string BackendName => "Linux uinput";

    public string BackendDescription =>
        "Creates a virtual Xbox-style evdev controller through /dev/uinput. No daemon is required.";

    public bool Probe(out string? error)
    {
        error = null;

        if (!OperatingSystem.IsLinux())
        {
            error = "The Linux uinput backend can only be used on Linux.";
            return false;
        }

        if (!File.Exists(UinputGamepad.UinputPath))
        {
            error = $"{UinputGamepad.UinputPath} does not exist. Load the uinput kernel module or install a kernel with uinput enabled.";
            return false;
        }

        var fd = UinputGamepad.TryOpen(out var openError);
        if (fd < 0)
        {
            error = openError;
            return false;
        }

        UinputGamepad.CloseHandle(fd);
        return true;
    }

    public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
    {
        gamepad = null;
        error = null;

        try
        {
            gamepad = new UinputGamepad();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
    }
}

public sealed class UinputGamepad : IVirtualGamepad
{
    internal const string UinputPath = "/dev/uinput";

    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;

    private const ulong UiDevCreate = 0x5501;
    private const ulong UiDevDestroy = 0x5502;
    private const ulong UiDevSetup = 0x405C5503;
    private const ulong UiAbsSetup = 0x401C5504;
    private const ulong UiSetEvBit = 0x40045564;
    private const ulong UiSetKeyBit = 0x40045565;
    private const ulong UiSetAbsBit = 0x40045567;

    private readonly object _gate = new();
    private readonly GamepadEventBuilder _events = new();
    private int _fd;
    private bool _disposed;

    public UinputGamepad()
    {
        _fd = TryOpen(out var error);
        if (_fd < 0)
        {
            throw new IOException(error ?? $"Unable to open {UinputPath}.");
        }

        try
        {
            Configure();
        }
        catch
        {
            CloseHandle(_fd);
            _fd = -1;
            throw;
        }
    }

    public bool IsConnected => !_disposed && _fd >= 0;

    public void Submit(XboxGamepadState state)
    {
        lock (_gate)
        {
            if (_disposed || _fd < 0)
            {
                return;
            }

            var events = _events.Advance(state);
            if (events.Length == 0)
            {
                return;
            }

            WriteEvents(events);
        }
    }

    private unsafe void Configure()
    {
        SetBit(UiSetEvBit, LinuxInput.EvKey);
        SetBit(UiSetEvBit, LinuxInput.EvAbs);

        foreach (var (_, code) in GamepadEventBuilder.KeyMap)
        {
            SetBit(UiSetKeyBit, code);
        }

        SetAbs(LinuxInput.AbsX, short.MinValue, short.MaxValue);
        SetAbs(LinuxInput.AbsY, short.MinValue, short.MaxValue);
        SetAbs(LinuxInput.AbsRx, short.MinValue, short.MaxValue);
        SetAbs(LinuxInput.AbsRy, short.MinValue, short.MaxValue);
        SetAbs(LinuxInput.AbsZ, 0, byte.MaxValue);
        SetAbs(LinuxInput.AbsRz, 0, byte.MaxValue);
        SetAbs(LinuxInput.AbsHat0X, -1, 1);
        SetAbs(LinuxInput.AbsHat0Y, -1, 1);

        var setup = new UinputSetup
        {
            Id = new InputId
            {
                BusType = 0x0003,
                Vendor = 0x045E,
                Product = 0x028E,
                Version = 0x0114
            },
            Name = "OpenWiimote Virtual Xbox 360 Controller",
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

    /// <summary>
    /// Enables one event type, key or axis of the device.
    /// </summary>
    /// <remarks>
    /// The <c>UI_SET_*BIT</c> requests declare their argument as a pointer to an <c>int</c>,
    /// but the uinput driver reads the bit number out of the argument itself. Passing the
    /// address of the number instead makes the driver take the low half of the address for
    /// the number and reject it with <c>EINVAL</c>, which is what used to stop the device from
    /// being created at all.
    /// </remarks>
    private void SetBit(ulong request, ushort bit)
    {
        Ioctl(_fd, request, bit);
    }

    private unsafe void SetAbs(ushort code, int minimum, int maximum)
    {
        SetBit(UiSetAbsBit, code);

        var setup = new UinputAbsSetup
        {
            Code = code,
            Info = new InputAbsInfo
            {
                Value = 0,
                Minimum = minimum,
                Maximum = maximum,
                Fuzz = 0,
                Flat = 0,
                Resolution = 0
            }
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
            var bytes = (nuint)(events.Length * sizeof(InputEvent));
            var written = write(_fd, ptr, bytes);
            if (written < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new IOException($"Writing to {UinputPath} failed: {Marshal.GetPInvokeErrorMessage(errno)}");
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

    internal static int TryOpen(out string? error)
    {
        error = null;

        if (!OperatingSystem.IsLinux())
        {
            error = "uinput is only available on Linux.";
            return -1;
        }

        var fd = open(UinputPath, O_WRONLY | O_NONBLOCK);
        if (fd >= 0)
        {
            return fd;
        }

        var errno = Marshal.GetLastPInvokeError();
        error =
            $"Unable to open {UinputPath}: {Marshal.GetPInvokeErrorMessage(errno)}. " +
            "Add your user to the input group and install a udev rule granting access to /dev/uinput.";
        return -1;
    }

    internal static void CloseHandle(int fd)
    {
        if (fd >= 0)
        {
            close(fd);
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

            _disposed = true;

            if (_fd >= 0)
            {
                try
                {
                    Ioctl(_fd, UiDevDestroy, 0);
                }
                catch
                {
                }

                CloseHandle(_fd);
                _fd = -1;
            }
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

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
