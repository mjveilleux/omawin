using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Picture-in-picture for any window (roadmap 8.6): a small always-on-top window showing another
// one live, through the DWM thumbnail API (what Alt+Tab and the taskbar's previews use: the
// compositor draws it, nothing is captured or copied). Drag it anywhere, resize it at its edges,
// scroll to make it fainter or stronger, double-click to go to the window, right-click to close.
// It closes by itself when the window does.
public static unsafe partial class Pip
{
    const uint WS_POPUP = 0x80000000, WS_THICKFRAME = 0x00040000, WS_VISIBLE = 0x10000000;
    const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80;
    const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_NCHITTEST = 0x0084, WM_NCLBUTTONDBLCLK = 0x00A3,
        WM_RBUTTONUP = 0x0205, WM_NCRBUTTONUP = 0x00A5, WM_MOUSEWHEEL = 0x020A;
    const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8;
    const uint EVENT_OBJECT_DESTROY = 0x8001, MONITOR_DEFAULTTONEAREST = 2;
    public const string ClassName = "omawin-pip";

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        public int fVisible, fSourceClientAreaOnly;
    }

    [LibraryImport("user32.dll", SetLastError = true)] private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint msg, nuint w, nint l);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(MSG* msg);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TranslateMessage(MSG* msg);
    [LibraryImport("user32.dll")] private static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessageW(nint hwnd, uint msg, nuint w, nint l);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetClientRect(nint hwnd, RECT* r);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, uint* pid);
    [LibraryImport("user32.dll")] private static partial nint SetWinEventHook(uint min, uint max, nint module, nint proc, uint pid, uint thread, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);
    [LibraryImport("dwmapi.dll")] private static partial int DwmRegisterThumbnail(nint destination, nint source, nint* thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUnregisterThumbnail(nint thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUpdateThumbnailProperties(nint thumbnail, DWM_THUMBNAIL_PROPERTIES* properties);
    [LibraryImport("dwmapi.dll")] private static partial int DwmQueryThumbnailSourceSize(nint thumbnail, SIZE* size);

    static nint source, window, thumbnail;
    static byte opacity = 255;

    // Shows `target` until it or the picture closes. Returns null, or why it couldn't.
    public static string? Run(nint target)
    {
        source = target;
        var instance = GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc,
                hInstance = instance,
                hCursor = LoadCursorW(0, 32512), // the arrow
                lpszClassName = (nint)cls,
            };
            if (RegisterClassExW(&wc) == 0) return $"no window class ({Marshal.GetLastPInvokeError()})";
        }

        // Its place from the source's shape on the source's monitor; the thumbnail is registered
        // once the window exists (DWM needs both), then the window is sized to the real shape.
        MONITORINFO mi = new() { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(MonitorFromWindow(source, MONITOR_DEFAULTTONEAREST), &mi);
        var work = new Box(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
        var first = PipPlan.Corner(work, 16, 9);
        // Title "omawin pip"; topmost; a tool window, so no taskbar button and GlazeWM leaves it be.
        window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, ClassName, "omawin pip", WS_POPUP | WS_THICKFRAME,
            first.Left, first.Top, first.Width, first.Height, 0, 0, instance, 0);
        if (window == 0) return $"no window ({Marshal.GetLastPInvokeError()})";
        nint t;
        if (DwmRegisterThumbnail(window, source, &t) < 0) { DestroyWindow(window); return "Windows wouldn't show that window (DWM)"; }
        thumbnail = t;
        SIZE size;
        if (DwmQueryThumbnailSourceSize(thumbnail, &size) >= 0)
        {
            var placed = PipPlan.Corner(work, size.cx, size.cy);
            SetWindowPos(window, 0, placed.Left, placed.Top, placed.Width, placed.Height, 0x0014 /* SWP_NOZORDER | SWP_NOACTIVATE */);
        }
        Fit();
        ShowWindow(window, 4 /* SW_SHOWNOACTIVATE */);

        // The source closing closes the picture: an out-of-context event from its process, no polling.
        uint pid;
        GetWindowThreadProcessId(source, &pid);
        SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, 0,
            (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&OnDestroy, pid, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        return null;
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);

    static void Fit()
    {
        if (thumbnail == 0) return;
        RECT client;
        GetClientRect(window, &client);
        SIZE size;
        var into = new Box(client.Left, client.Top, client.Right, client.Bottom);
        var at = DwmQueryThumbnailSourceSize(thumbnail, &size) >= 0 ? PipPlan.Fit(size.cx, size.cy, into) : into;
        var p = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
            rcDestination = new RECT { Left = at.Left, Top = at.Top, Right = at.Right, Bottom = at.Bottom },
            opacity = opacity,
            fVisible = 1,
        };
        DwmUpdateThumbnailProperties(thumbnail, &p);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint w, nint l)
    {
        switch (msg)
        {
            case WM_SIZE: Fit(); return 0;
            case WM_NCHITTEST:
                // Drag from anywhere inside; Windows' own edges resize.
                var hit = DefWindowProcW(hwnd, msg, w, l);
                return hit == 1 /* HTCLIENT */ ? 2 /* HTCAPTION */ : hit;
            case WM_NCLBUTTONDBLCLK:
                if (IsWindow(source)) SetForegroundWindow(source);
                DestroyWindow(hwnd);
                return 0;
            case WM_RBUTTONUP or WM_NCRBUTTONUP:
                DestroyWindow(hwnd);
                return 0;
            case WM_MOUSEWHEEL:
                opacity = PipPlan.Opacity(opacity, (short)((long)w >> 16));
                Fit();
                return 0;
            case WM_DESTROY:
                if (thumbnail != 0) DwmUnregisterThumbnail(thumbnail);
                thumbnail = 0;
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }

    [UnmanagedCallersOnly]
    static void OnDestroy(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == source && idObject == 0 /* OBJID_WINDOW */) PostMessageW(window, WM_CLOSE, 0, 0);
    }
}
