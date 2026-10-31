// The Windows calls the settings code makes (plans/02-settings-and-one-search.md §3), source-generated
// (LibraryImport: no runtime marshalling stubs, fine under Native AOT). menu-app.Tests compiles
// this too, so the round-trip can run the real handlers on a Windows runner.

using System.Runtime.InteropServices;

namespace SystemMenu;

static unsafe partial class SettingsNative
{
    // --- SystemParametersInfo ----------------------------------------------------------------
    public const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, void* pvParam, uint winIni);

    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] public static partial uint GetDoubleClickTime();

    // --- WM_SETTINGCHANGE broadcast ----------------------------------------------------------
    public const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;
    public const nint HWND_BROADCAST = 0xFFFF;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nuint wParam, string? lParam, uint flags, uint timeoutMs, out nuint result);

    // --- the shell's refresh after Explorer's view settings change ---------------------------
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_IDLIST = 0x0000;
    [LibraryImport("shell32.dll")]
    public static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    // --- registry change notification --------------------------------------------------------
    public const uint REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4, REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;
    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(nint key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint filter, nint evt, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    // --- power settings (powrprof) -----------------------------------------------------------
    [LibraryImport("powrprof.dll")] public static partial uint PowerGetActiveScheme(nint root, Guid** scheme);
    [LibraryImport("powrprof.dll")] public static partial uint PowerReadACValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint* value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerReadDCValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint* value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerWriteACValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerWriteDCValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerSetActiveScheme(nint root, Guid* scheme);
    [LibraryImport("kernel32.dll")] public static partial nint LocalFree(nint mem);

    // --- resource strings ("@%SystemRoot%\system32\shell32.dll,-21817") ----------------------
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, char* outBuf, uint outChars, nint reserved);

    // --- WinINet: tell running programs the proxy changed -----------------------------------
    public const uint INTERNET_OPTION_REFRESH = 37, INTERNET_OPTION_SETTINGS_CHANGED = 39;
    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InternetSetOption(nint handle, uint option, nint buffer, uint length);

    // --- window subclassing, to hear WM_SETTINGCHANGE on the menu app's own window ----------
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    // --- the desktop's icons (bin/desktop-icons.ps1's Add-Type block, in C#) -------------------
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    // --- display modes -----------------------------------------------------------------------
    // DEVMODEW (220 bytes); only the display fields are used (dmPelsWidth / Height, dmDisplayFrequency).
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVMODE
    {
        public fixed char dmDeviceName[32];
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        public fixed char dmFormName[32];
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint DM_DISPLAYFREQUENCY = 0x00400000, CDS_UPDATEREGISTRY = 0x1;
    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplaySettings(string? device, int mode, DEVMODE* devMode);
    [LibraryImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ChangeDisplaySettingsEx(string? device, DEVMODE* devMode, nint hwnd, uint flags, nint param);

    // --- HDR ("advanced colour") per display path --------------------------------------------
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9, DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo // DISPLAYCONFIG_PATH_INFO: 72 bytes
    {
        public LUID SourceAdapter; public uint SourceId, SourceModeIndex, SourceStatus;
        public LUID TargetAdapter; public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator, ScanLineOrdering, TargetAvailable, TargetStatus;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModeInfo // DISPLAYCONFIG_MODE_INFO: 64 bytes; the union isn't read here
    {
        public uint InfoType, Id; public LUID Adapter;
        public fixed byte Union[48];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader { public uint Type, Size; public LUID Adapter; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit 0 supported, bit 1 enabled, bit 2 wide colour enforced, bit 3 force-disabled
        public uint ColorEncoding, BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SetAdvancedColorState { public DeviceInfoHeader Header; public uint Value; } // bit 0: enable

    [LibraryImport("user32.dll")] public static partial int GetDisplayConfigBufferSizes(uint flags, uint* paths, uint* modes);
    [LibraryImport("user32.dll")] public static partial int QueryDisplayConfig(uint flags, uint* paths, PathInfo* pathArray, uint* modes, ModeInfo* modeArray, nint topology);
    [LibraryImport("user32.dll")] public static partial int DisplayConfigGetDeviceInfo(void* packet);
    [LibraryImport("user32.dll")] public static partial int DisplayConfigSetDeviceInfo(void* packet);
}
