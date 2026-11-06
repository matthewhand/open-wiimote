using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
namespace OpenWiimote.Platform.Windows;

public sealed record WindowsBluetoothRemote(string Address, string Name, string RadioAddress, string RadioName, bool Paired, bool Connected, bool Remembered = false)
{
    public override string ToString() => Name + " · " + Address + (Paired ? " · paired" : "");
}
public sealed record WindowsPairResult(bool Success, string Message);

// Legacy Wii authentication uses six Bluetooth address bytes, not a PIN a person can
// type. This is an independent Win32 implementation of the published Wii protocol.
public static class WindowsWiimotePairing
{
    private static readonly Guid HidService = new("00001124-0000-1000-8000-00805f9b34fb");
    internal static bool IsRemoteName(string? name) => name is "Nintendo RVL-CNT-01" or "Nintendo RVL-CNT-01-TR";
    internal static string SyncPin(ulong radioAddress) => new(Enumerable.Range(0, 6).Select(n => (char)((radioAddress >> (8 * n)) & 255)).ToArray());
    internal static ulong ParseAddress(string address)
    {
        if (address.Length != 17 || address.Where((_, n) => n % 3 == 2).Any(c => c != ':')
            || !address.Where((_, n) => n % 3 != 2).All(Uri.IsHexDigit)
            || !ulong.TryParse(address.Replace(":", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) || value == 0 || value > 0xffffffffffff)
            throw new ArgumentException("Invalid Bluetooth address.");
        return value;
    }
    private static string Format(ulong address) => string.Join(':', Enumerable.Range(0, 6).Select(n => ((address >> (8 * (5 - n))) & 255).ToString("X2")));
    public static IReadOnlyList<WindowsBluetoothRemote> Scan()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var result = new List<WindowsBluetoothRemote>();
        Radios((radio, info) =>
        {
            var search = new Search { Size = Marshal.SizeOf<Search>(), Authenticated = true, Remembered = true, Unknown = true,
                Connected = true, Inquiry = true, Multiplier = 3, Radio = radio };
            var device = new Device { Size = Marshal.SizeOf<Device>(), Name = "" };
            var find = BluetoothFindFirstDevice(ref search, ref device);
            if (find == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is not 0 and not 259) throw new Win32Exception(error, "Windows Bluetooth search failed: " + new Win32Exception(error).Message);
                return;
            }
            try
            {
                do
                {
                    if (IsRemoteName(device.Name)) result.Add(new(Format(device.Address), device.Name, Format(info.Address), info.Name, device.Authenticated, device.Connected, device.Remembered));
                    device.Size = Marshal.SizeOf<Device>();
                } while (BluetoothFindNextDevice(find, ref device));
            }
            finally { BluetoothFindDeviceClose(find); }
        });
        return result.DistinctBy(d => (d.Address, d.RadioAddress)).ToArray();
    }
    public static WindowsPairResult Pair(string address, string radioAddress)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Windows only.");
        var remote = ParseAddress(address); var adapter = ParseAddress(radioAddress);
        WindowsPairResult result = new(false, "The Bluetooth adapter is unavailable. Turn Bluetooth on and retry.");
        Radios((radio, info) =>
        {
            if (info.Address != adapter) return;
            var device = new Device { Size = Marshal.SizeOf<Device>(), Address = remote, Name = "" };
            var status = BluetoothGetDeviceInfo(radio, ref device);
            if (status != 0 || !IsRemoteName(device.Name)) { result = new(false, "The selected Wii Remote is no longer available. Press red SYNC and find it again."); return; }
            // Keep working bonds; never remove or reauthenticate an already bonded device.
            if (!device.Authenticated)
            {
                status = BluetoothAuthenticateDevice(0, radio, ref device, SyncPin(info.Address), 6);
                if (status != 0) { result = Error("Wii Remote authentication", status); return; }
            }
            _ = Services(radio, ref device); // Populate the service cache before enabling HID.
            var service = HidService;
            status = BluetoothSetServiceState(radio, ref device, ref service, 1);
            if (status != 0 && !(status == 0x80070057 && Services(radio, ref device).Contains(HidService)))
            { result = Error("Enabling the Wii Remote input service", status); return; }
            status = BluetoothGetDeviceInfo(radio, ref device);
            result = status == 0 && device.Authenticated
                ? new(true, "Windows saved the pairing and enabled Wii Remote input. Wait for the remote to appear in OpenWiimote.")
                : new(false, "Windows has not confirmed a saved pairing. Press red SYNC and retry; existing bonds were kept.");
        });
        return result;
    }
    private static WindowsPairResult Error(string action, uint status) => new(false, action + " failed (" + status + "): " + new Win32Exception((int)status).Message.TrimEnd('.') + ". Press red SYNC and retry.");
    private static Guid[] Services(nint radio, ref Device device)
    {
        uint count = 0;
        var status = BluetoothEnumerateInstalledServices(radio, ref device, ref count, null);
        if (count == 0 || (status != 0 && status != 234) || count > 64) return [];
        var services = new Guid[count];
        return BluetoothEnumerateInstalledServices(radio, ref device, ref count, services) == 0 ? services.Take((int)count).ToArray() : [];
    }
    private static void Radios(Action<nint, Radio> action)
    {
        var parameters = new FindRadio { Size = Marshal.SizeOf<FindRadio>() };
        var find = BluetoothFindFirstRadio(ref parameters, out var radio);
        if (find == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, "No accessible Bluetooth radio. Turn Bluetooth on and try again. " + new Win32Exception(error).Message);
        }
        try
        {
            do
            {
                try
                {
                    var info = new Radio { Size = Marshal.SizeOf<Radio>(), Name = "" };
                    var status = BluetoothGetRadioInfo(radio, ref info);
                    if (status != 0) throw new Win32Exception((int)status, "Windows could not read the Bluetooth adapter: " + new Win32Exception((int)status).Message);
                    action(radio, info);
                }
                finally { CloseHandle(radio); }
            } while (BluetoothFindNextRadio(find, out radio));
        }
        finally { BluetoothFindRadioClose(find); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FindRadio { public int Size; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Radio
    {
        public int Size; public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
        public uint Class; public ushort Subversion, Manufacturer;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Device
    {
        public int Size; public ulong Address; public uint Class;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Search
    {
        public int Size;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Unknown;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Inquiry;
        public byte Multiplier; public nint Radio;
    }
    internal static (int Device, int Radio, int Search) StructureSizes => (Marshal.SizeOf<Device>(), Marshal.SizeOf<Radio>(), Marshal.SizeOf<Search>());
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)] private static extern nint BluetoothFindFirstRadio(ref FindRadio parameters, out nint radio);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindNextRadio(nint find, out nint radio);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindRadioClose(nint find);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint BluetoothGetRadioInfo(nint radio, ref Radio info);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint BluetoothFindFirstDevice(ref Search search, ref Device device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindNextDevice(nint find, ref Device device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindDeviceClose(nint find);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint BluetoothGetDeviceInfo(nint radio, ref Device device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint BluetoothAuthenticateDevice(nint parent, nint radio, ref Device device, [MarshalAs(UnmanagedType.LPWStr)] string pin, uint length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint BluetoothEnumerateInstalledServices(nint radio, ref Device device, ref uint count, [Out] Guid[]? services);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("bthprops.cpl", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint BluetoothSetServiceState(nint radio, ref Device device, ref Guid service, uint flags);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
