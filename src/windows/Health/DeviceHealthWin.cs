using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Reads 8.41's hardware health, unelevated: the disks and devices from WMI (the scripting API,
// WbemScripting.SWbemLocator, called by name through Dispatch, so no generated COM types), the
// battery from its own driver (IOCTL_BATTERY_QUERY_INFORMATION, as Microsoft's battery sample does).
// DeviceHealth.cs words what comes back.
public static unsafe partial class DeviceHealthWin
{
    public sealed record Result(IReadOnlyList<DiskHealth>? Disks, IReadOnlyList<BatteryHealth> Batteries, IReadOnlyList<DeviceProblem>? Devices, IReadOnlyList<string> Notes);

    public static Result Read()
    {
        var notes = new List<string>();
        var (disks, devices) = Com.InStaThread(() => (Disks(notes), Devices(notes)));
        List<BatteryHealth> batteries;
        try { batteries = Batteries(); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            batteries = [];
            notes.Add($"(the battery couldn't be read: {ex.Message})");
        }
        return new Result(disks, batteries, devices, notes);
    }

    // ---- WMI ----------------------------------------------------------------------------------------

    static Dispatch Connect(string ns)
    {
        using var locator = Dispatch.Create("WbemScripting.SWbemLocator");
        return locator.CallObject("ConnectServer", ".", ns);
    }

    // An SWbemObjectSet's objects. ItemIndex, not Item (which takes an object path).
    static IEnumerable<Dispatch> Objects(Dispatch set)
    {
        int count = Convert.ToInt32(set.Get("Count") ?? 0);
        for (int i = 0; i < count; i++)
            if (set.Call("ItemIndex", i) is Dispatch d) yield return d;
    }

    // A property by name through Properties_, which every SWbemObject has (direct names on the object
    // are a scripting convenience some clients don't get). Null for a NULL or an array.
    static object? Prop(Dispatch obj, string name)
    {
        using var props = obj.GetObject("Properties_");
        using var prop = props.CallObject("Item", name);
        return prop.Get("Value");
    }

    // Scripting hands uint64 over as a string, smaller integers as numbers.
    static long? Number(object? v) => v switch
    {
        int i => i,
        long l => l,
        string s when long.TryParse(s, out var n) => n,
        _ => null,
    };

    static int? Small(object? v) => Number(v) is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    static List<DiskHealth>? Disks(List<string> notes)
    {
        try
        {
            using var storage = Connect(@"root\Microsoft\Windows\Storage");
            using var set = storage.CallObject("ExecQuery", "SELECT * FROM MSFT_PhysicalDisk", "WQL", 0);
            var disks = new List<DiskHealth>();
            bool countersRefused = false;
            foreach (var disk in Objects(set))
                using (disk)
                {
                    int? wear = null, temp = null, tempMax = null;
                    long? hours = null, readErrors = null, writeErrors = null;
                    // The reliability counters often need admin (check); without them the disk's own
                    // health verdict still shows.
                    try
                    {
                        using var counters = disk.CallObject("Associators_", "MSFT_PhysicalDiskToStorageReliabilityCounter");
                        foreach (var c in Objects(counters))
                            using (c)
                            {
                                wear = Small(Prop(c, "Wear"));
                                temp = Small(Prop(c, "Temperature"));
                                tempMax = Small(Prop(c, "TemperatureMax"));
                                hours = Number(Prop(c, "PowerOnHours"));
                                readErrors = Number(Prop(c, "ReadErrorsUncorrected"));
                                writeErrors = Number(Prop(c, "WriteErrorsUncorrected"));
                            }
                    }
                    catch (InvalidOperationException) { countersRefused = true; }
                    disks.Add(new DiskHealth(
                        (Prop(disk, "FriendlyName") as string)?.Trim() is { Length: > 0 } name ? name : "Disk",
                        DeviceHealth.MediaName(Small(Prop(disk, "MediaType"))),
                        DeviceHealth.BusName(Small(Prop(disk, "BusType"))),
                        DeviceHealth.HealthName(Small(Prop(disk, "HealthStatus"))),
                        wear, temp, tempMax, hours, readErrors, writeErrors));
                }
            if (countersRefused)
                notes.Add("(disks' wear and temperature: Windows gives them to admin only here; omawin health from an admin terminal shows them)");
            return disks;
        }
        catch (InvalidOperationException ex)
        {
            notes.Add($"(disks: Windows' storage information couldn't be read: {ex.Message})");
            return null;
        }
    }

    static List<DeviceProblem>? Devices(List<string> notes)
    {
        try
        {
            using var cimv2 = Connect(@"root\cimv2");
            using var set = cimv2.CallObject("ExecQuery",
                "SELECT Name, ConfigManagerErrorCode, PNPDeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", "WQL", 0);
            var list = new List<DeviceProblem>();
            foreach (var d in Objects(set))
                using (d)
                {
                    int code = Small(Prop(d, "ConfigManagerErrorCode")) ?? 0;
                    if (!DeviceHealth.IsProblem(code)) continue;
                    var name = (Prop(d, "Name") as string)?.Trim();
                    list.Add(new DeviceProblem(string.IsNullOrEmpty(name) ? "Unknown device" : name, code, Prop(d, "PNPDeviceID") as string ?? ""));
                }
            return list;
        }
        catch (InvalidOperationException ex)
        {
            notes.Add($"(devices couldn't be read: {ex.Message})");
            return null;
        }
    }

    // ---- the battery --------------------------------------------------------------------------------

    // GUID_DEVCLASS_BATTERY, which batteries also register as their device interface class.
    static readonly Guid BatteryClass = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");
    const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    // CTL_CODE(FILE_DEVICE_BATTERY, 0x10 / 0x11, METHOD_BUFFERED, FILE_READ_ACCESS).
    const uint IOCTL_BATTERY_QUERY_TAG = 0x294040, IOCTL_BATTERY_QUERY_INFORMATION = 0x294044;
    const int BatteryInformation = 0, BatteryDeviceName = 4;

    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceData { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public nuint Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct QueryInformation { public uint BatteryTag; public int InformationLevel; public int AtRate; }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static partial IntPtr SetupDiGetClassDevs(in Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, in Guid classGuid, uint index, ref InterfaceData data);
    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, byte* detail, uint size, out uint required, IntPtr deviceInfo);
    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(IntPtr device, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, IntPtr overlapped);

    // Every battery of the PC's own (a desktop has none, and the list is empty).
    static List<BatteryHealth> Batteries()
    {
        var list = new List<BatteryHealth>();
        var set = SetupDiGetClassDevs(BatteryClass, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            // A PC has a few at most; the bound keeps a misbehaving driver list from looping.
            for (uint i = 0; i < 16; i++)
            {
                var data = new InterfaceData { cbSize = (uint)sizeof(InterfaceData) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, BatteryClass, i, ref data)) break; // ERROR_NO_MORE_ITEMS
                if (DevicePath(set, ref data) is { } path && Battery(path) is { } b) list.Add(b);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    // SP_DEVICE_INTERFACE_DETAIL_DATA_W: a DWORD cbSize (8 on 64-bit, with its padding), then the path.
    static string? DevicePath(IntPtr set, ref InterfaceData data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, null, 0, out uint required, IntPtr.Zero);
        if (required < 8) return null;
        var buffer = new byte[required];
        fixed (byte* b = buffer)
        {
            *(uint*)b = 8;
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, b, required, out _, IntPtr.Zero)) return null;
            return new string((char*)(b + 4));
        }
    }

    // Null when the slot is empty (no tag) or it isn't a system battery.
    static BatteryHealth? Battery(string path)
    {
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return null;
        try
        {
            uint wait = 0, tag = 0;
            if (!DeviceIoControl(h, IOCTL_BATTERY_QUERY_TAG, &wait, 4, &tag, 4, out _, IntPtr.Zero) || tag == 0) return null;

            var query = new QueryInformation { BatteryTag = tag, InformationLevel = BatteryInformation };
            var info = stackalloc byte[DeviceHealth.BatteryInformationSize];
            if (!DeviceIoControl(h, IOCTL_BATTERY_QUERY_INFORMATION, &query, (uint)sizeof(QueryInformation), info, DeviceHealth.BatteryInformationSize, out uint got, IntPtr.Zero))
                return null;

            query.InformationLevel = BatteryDeviceName;
            var name = stackalloc char[128];
            string text = DeviceIoControl(h, IOCTL_BATTERY_QUERY_INFORMATION, &query, (uint)sizeof(QueryInformation), name, 128 * sizeof(char), out uint nameBytes, IntPtr.Zero)
                ? new string(name, 0, (int)Math.Min(127, nameBytes / 2)).TrimEnd('\0')
                : "";
            return DeviceHealth.ReadBattery(new ReadOnlySpan<byte>(info, (int)got), text);
        }
        finally { Win32.CloseHandle(h); }
    }
}
