using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Windows' display configuration (QueryDisplayConfig / SetDisplayConfig, documented, what
// Settings › Display uses): the monitors in use with their resolution, place and rotation, and a
// saved layout put back. Refresh rate and scaling are left as Windows has them.
public static unsafe partial class DisplayConfig
{
    const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20, SDC_APPLY = 0x80, SDC_SAVE_TO_DATABASE = 0x200, SDC_ALLOW_CHANGES = 0x400;
    const uint MODE_SOURCE = 1, GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo // DISPLAYCONFIG_PATH_INFO: 72 bytes
    {
        public LUID SourceAdapter; public uint SourceId, SourceModeIndex, SourceStatus;
        public LUID TargetAdapter; public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator, ScanLineOrdering, TargetAvailable, TargetStatus;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO: 64 bytes. For a source mode the union starts with width, height,
    // pixel format, then the position.
    [StructLayout(LayoutKind.Sequential)]
    struct ModeInfo
    {
        public uint InfoType, Id; public LUID Adapter;
        public uint Width, Height, PixelFormat; public int X, Y;
        public fixed byte Rest[28];
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TargetName // DISPLAYCONFIG_TARGET_DEVICE_NAME: 420 bytes
    {
        public uint Type, Size; public LUID Adapter; public uint Id;
        public uint Flags, OutputTechnology;
        public ushort EdidManufacturer, EdidProduct;
        public uint ConnectorInstance;
        public fixed char FriendlyName[64];
        public fixed char DevicePath[128];
    }

    [LibraryImport("user32.dll")] private static partial int GetDisplayConfigBufferSizes(uint flags, uint* paths, uint* modes);
    [LibraryImport("user32.dll")] private static partial int QueryDisplayConfig(uint flags, uint* paths, PathInfo* pathArray, uint* modes, ModeInfo* modeArray, nint topology);
    [LibraryImport("user32.dll")] private static partial int SetDisplayConfig(uint paths, PathInfo* pathArray, uint modes, ModeInfo* modeArray, uint flags);
    [LibraryImport("user32.dll")] private static partial int DisplayConfigGetDeviceInfo(void* packet);

    // DISPLAYCONFIG_ROTATION: 1 identity, 2 90°, 3 180°, 4 270°.
    static int Degrees(uint rotation) => rotation is >= 1 and <= 4 ? (int)(rotation - 1) * 90 : 0;
    static uint Rotation(int degrees) => (uint)(degrees / 90 + 1);

    static (PathInfo[] Paths, ModeInfo[] Modes) Query()
    {
        for (int attempt = 0; attempt < 3; attempt++) // a monitor arriving between the two calls: again
        {
            uint np, nm;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &np, &nm) != 0) break;
            var paths = new PathInfo[np];
            var modes = new ModeInfo[nm];
            int r;
            fixed (PathInfo* p = paths)
            fixed (ModeInfo* m = modes)
                r = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &np, p, &nm, m, 0);
            if (r == 0) return (paths[..(int)np], modes[..(int)nm]);
            if (r != 122) break; // ERROR_INSUFFICIENT_BUFFER: the set changed
        }
        throw new InvalidOperationException("Windows didn't give its display configuration");
    }

    static (string Id, string Name) Name(in PathInfo path)
    {
        var n = new TargetName { Type = GET_TARGET_NAME, Size = (uint)sizeof(TargetName), Adapter = path.TargetAdapter, Id = path.TargetId };
        if (DisplayConfigGetDeviceInfo(&n) != 0) return ($"target-{path.TargetId}", "Monitor");
        var name = new string(n.FriendlyName);
        var device = new string(n.DevicePath);
        return (DisplayProfiles.IdFromPath(device), name.Length > 0 ? name : "Built-in screen");
    }

    // The monitors in use now (a cloned pair counts once, as the first).
    public static List<DisplayPlace> Current()
    {
        var (paths, modes) = Query();
        var list = new List<DisplayPlace>();
        var seen = new HashSet<uint>();
        foreach (var p in paths)
        {
            if (p.SourceModeIndex >= modes.Length || modes[p.SourceModeIndex].InfoType != MODE_SOURCE || !seen.Add(p.SourceModeIndex)) continue;
            var m = modes[p.SourceModeIndex];
            var (id, name) = Name(p);
            list.Add(new DisplayPlace(id, name, (int)m.Width, (int)m.Height, m.X, m.Y, Degrees(p.Rotation)));
        }
        return list;
    }

    // Each monitor in `layout` to its saved size, place and rotation, in one change Windows
    // checks and may adjust (a resolution the monitor doesn't offer). Saved in Windows' own
    // database too, so its next start keeps it. Null when done; else why not.
    public static string? Apply(IReadOnlyList<DisplayPlace> layout)
    {
        var (paths, modes) = Query();
        int matched = 0;
        for (int i = 0; i < paths.Length; i++)
        {
            ref var p = ref paths[i];
            if (p.SourceModeIndex >= modes.Length || modes[p.SourceModeIndex].InfoType != MODE_SOURCE) continue;
            var (id, _) = Name(p);
            var want = layout.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (want == null) continue;
            ref var m = ref modes[p.SourceModeIndex];
            m.Width = (uint)want.Width;
            m.Height = (uint)want.Height;
            m.X = want.X;
            m.Y = want.Y;
            p.Rotation = Rotation(want.Rotation);
            matched++;
        }
        if (matched == 0) return "none of the saved monitors is plugged in";
        int r;
        fixed (PathInfo* pp = paths)
        fixed (ModeInfo* mp = modes)
            r = SetDisplayConfig((uint)paths.Length, pp, (uint)modes.Length, mp,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);
        return r == 0 ? null : $"Windows refused the layout (error {r})";
    }
}
