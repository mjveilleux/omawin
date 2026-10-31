using System.Runtime.InteropServices;

namespace Omawin.Windows;

// A picture per monitor (roadmap 8.8): the theme's backgrounds spread across the monitors, left to
// right, instead of one picture stretched or repeated. Windows' own IDesktopWallpaper
// (shobjidl_core.h, documented since Windows 8), called by vtable slot so it stays AOT-safe.
public static unsafe partial class Wallpaper
{
    static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    static readonly Guid IID_IDesktopWallpaper = new("B92B56A9-8B55-4E14-9A89-0199BBB6F93B");
    const int DWPOS_FILL = 4;
    // IDesktopWallpaper's methods after IUnknown's three, in the header's order.
    const int SetWallpaperSlot = 3, GetMonitorDevicePathAtSlot = 5, GetMonitorDevicePathCountSlot = 6, GetMonitorRectSlot = 7, SetPositionSlot = 10;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint obj);

    // Sets them, filling each monitor. Run on a thread with COM (Com.InStaThread).
    public static int Spread(IReadOnlyList<string> pictures)
    {
        Check(CoCreateInstance(CLSID_DesktopWallpaper, 0, 0x4 /* CLSCTX_LOCAL_SERVER */ | 0x1, IID_IDesktopWallpaper, out var wp), "IDesktopWallpaper");
        try
        {
            var vt = *(nint**)wp;
            uint count;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)vt[GetMonitorDevicePathCountSlot])(wp, &count), "GetMonitorDevicePathCount");
            var monitors = new List<MonitorPlace>();
            for (uint i = 0; i < count; i++)
            {
                char* id;
                if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)vt[GetMonitorDevicePathAtSlot])(wp, i, &id) < 0 || id == null) continue;
                try
                {
                    RECT r;
                    // A monitor Windows remembers but isn't attached now has no rectangle: skipped.
                    if (((delegate* unmanaged[Stdcall]<nint, char*, RECT*, int>)vt[GetMonitorRectSlot])(wp, id, &r) >= 0 && r.Right > r.Left)
                        monitors.Add(new MonitorPlace(new string(id), r.Left, r.Top));
                }
                finally { Marshal.FreeCoTaskMem((nint)id); }
            }
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)vt[SetPositionSlot])(wp, DWPOS_FILL), "SetPosition");
            var plan = WallpaperPlan.Assign(monitors, pictures);
            foreach (var (monitor, picture) in plan)
                fixed (char* m = monitor)
                fixed (char* p = picture)
                    Check(((delegate* unmanaged[Stdcall]<nint, char*, char*, int>)vt[SetWallpaperSlot])(wp, m, p), "SetWallpaper");
            return plan.Count;
        }
        finally { Marshal.Release(wp); }
    }

    static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what}: {Marshal.GetExceptionForHR(hr)?.Message ?? $"0x{hr:X8}"}");
    }
}
