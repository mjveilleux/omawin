using System.Runtime.InteropServices;

namespace Omawin.Windows;

// The saved Wi-Fi networks and one's password (8.21), from the WLAN API (wlanapi.h, documented).
// WlanGetProfile hands the key over in plain text (WLAN_PROFILE_GET_PLAINTEXT_KEY) to callers Windows
// allows; to others the key comes back encrypted and FromProfile says so.
public static unsafe partial class WlanProfile
{
    const uint WLAN_PROFILE_GET_PLAINTEXT_KEY = 0x4;
    const int wlan_intf_opcode_current_connection = 7;
    const int InterfaceInfoSize = 532;   // WLAN_INTERFACE_INFO: GUID, WCHAR[256], state
    const int ProfileInfoSize = 516;     // WLAN_PROFILE_INFO: WCHAR[256], flags

    [LibraryImport("wlanapi.dll")] private static partial uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [LibraryImport("wlanapi.dll")] private static partial uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [LibraryImport("wlanapi.dll")] private static partial uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [LibraryImport("wlanapi.dll")] private static partial uint WlanGetProfileList(IntPtr handle, in Guid iface, IntPtr reserved, out IntPtr list);
    [LibraryImport("wlanapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint WlanGetProfile(IntPtr handle, in Guid iface, string profileName, IntPtr reserved, out IntPtr xml, ref uint flags, out uint grantedAccess);
    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(IntPtr handle, in Guid iface, int opcode, IntPtr reserved, out uint dataSize, out IntPtr data, out int valueType);
    [LibraryImport("wlanapi.dll")] private static partial void WlanFreeMemory(IntPtr memory);

    // The WLAN service's handle and the adapters' ids; closed by Dispose.
    sealed class Session : IDisposable
    {
        public readonly IntPtr Handle;
        public readonly List<Guid> Interfaces = [];

        public Session()
        {
            uint r = WlanOpenHandle(2, IntPtr.Zero, out _, out Handle);
            if (r != 0) throw new InvalidOperationException(r == 1062 ? "the WLAN service isn't running (no Wi-Fi on this PC?)" : $"WlanOpenHandle: {r}");
            if (WlanEnumInterfaces(Handle, IntPtr.Zero, out var list) != 0 || list == IntPtr.Zero) return;
            try
            {
                int count = *(int*)list;
                for (int i = 0; i < count; i++) Interfaces.Add(*(Guid*)((byte*)list + 8 + i * InterfaceInfoSize));
            }
            finally { WlanFreeMemory(list); }
        }

        public void Dispose() { if (Handle != IntPtr.Zero) WlanCloseHandle(Handle, IntPtr.Zero); }
    }

    // The saved networks' profile names, every adapter's, without repeats.
    public static List<string> Saved()
    {
        using var s = new Session();
        var names = new List<string>();
        foreach (var iface in s.Interfaces)
        {
            if (WlanGetProfileList(s.Handle, iface, IntPtr.Zero, out var list) != 0 || list == IntPtr.Zero) continue;
            try
            {
                int count = *(int*)list;
                for (int i = 0; i < count; i++)
                {
                    var name = new string((char*)((byte*)list + 8 + i * ProfileInfoSize));
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                }
            }
            finally { WlanFreeMemory(list); }
        }
        return names;
    }

    // The profile in use on a connected adapter, or null (not connected; or Windows 11 24H2's
    // location consent withholds it, in which case the network is named on the command line).
    public static string? Connected()
    {
        using var s = new Session();
        foreach (var iface in s.Interfaces)
        {
            // WLAN_CONNECTION_ATTRIBUTES: isState, wlanConnectionMode, then strProfileName (WCHAR[256]).
            if (WlanQueryInterface(s.Handle, iface, wlan_intf_opcode_current_connection, IntPtr.Zero, out var size, out var data, out _) != 0 || data == IntPtr.Zero) continue;
            try
            {
                if (size < 8 + 512 || *(int*)data != 1) continue; // wlan_interface_state_connected
                var name = new string((char*)((byte*)data + 8));
                if (name.Length > 0) return name;
            }
            finally { WlanFreeMemory(data); }
        }
        return null;
    }

    // The profile's XML with the key in plain text where Windows allows it; null when no adapter has it.
    public static string? ProfileXml(string profileName)
    {
        using var s = new Session();
        foreach (var iface in s.Interfaces)
        {
            uint flags = WLAN_PROFILE_GET_PLAINTEXT_KEY;
            if (WlanGetProfile(s.Handle, iface, profileName, IntPtr.Zero, out var xml, ref flags, out _) != 0 || xml == IntPtr.Zero) continue;
            try { return Marshal.PtrToStringUni(xml); }
            finally { WlanFreeMemory(xml); }
        }
        return null;
    }
}
