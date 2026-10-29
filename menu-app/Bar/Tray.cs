using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SystemMenu;

// The tray's icons (the apps' notification-area icons), for the native bar. Windows has no API to
// read them; what Zebar does (glzr-io/systray-util) is done here too: a window of the taskbar's
// own class, Shell_TrayWnd, kept in front of Explorer's, so Shell_NotifyIcon (which finds the
// taskbar by that class) tells it about every icon. It reads what it's told and hands every
// message on to Explorer's taskbar unchanged, so Windows' own tray goes on working; when this
// process ends, apps simply talk to Explorer's again. Started, every app is asked to add its
// icons again (the "TaskbarCreated" broadcast Explorer sends when it restarts).
public static unsafe class Tray
{
    public sealed class Icon
    {
        public IntPtr Hwnd;
        public uint Id, CallbackMessage, Version;
        public Guid? Guid;
        public string Tip = "";
        public bool Hidden;
        public byte[]? Pixels; // 32x32 BGRA, premultiplied
        public int Stamp; // changes when the picture does
        public string Key => Guid?.ToString() ?? $"{Hwnd}:{Id}";
    }

    public static event Action? Changed;
    static readonly List<Icon> icons = [];
    public static List<Icon> Icons { get { lock (icons) return [.. icons.Where(i => !i.Hidden && i.Pixels != null)]; } }

    static IntPtr spy;
    static bool started;
    const int Size = 32;

    public static void Start()
    {
        if (started) return;
        started = true;
        new Thread(Run) { IsBackground = true, Name = "tray" }.Start();
    }

    static void Run()
    {
        try
        {
            // Not before Explorer's taskbar: at sign-in this app can start first, and a window of
            // the taskbar's class must never be there without the real one behind it.
            for (int i = 0; i < 1200 && Explorer() == IntPtr.Zero; i++) Thread.Sleep(100);
            var name = Marshal.StringToHGlobalUni("Shell_TrayWnd");
            var cls = new WNDCLASSEX { cbSize = (uint)sizeof(WNDCLASSEX), lpfnWndProc = &WndProc, hInstance = GetModuleHandle(IntPtr.Zero), lpszClassName = name };
            if (RegisterClassEx(ref cls) == 0) App.Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "tray: RegisterClassEx"));
            // As systray-util makes it: an ordinary window, never shown (topmost, tool, app window).
            const int Default = unchecked((int)0x80000000); // CW_USEDEFAULT
            spy = CreateWindowEx(0x8 | 0x80 | 0x40000, name, name, 0x00CF0000, Default, Default, Default, Default, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero); // WS_OVERLAPPEDWINDOW
            if (spy == IntPtr.Zero) { App.Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "tray: CreateWindowEx")); return; }
            KeepInFront();
            SetTimer(spy, 1, 100, IntPtr.Zero); // Explorer's taskbar comes to the front now and then
            // Every app: add your icons again (to this window now).
            SendNotifyMessage((IntPtr)0xFFFF, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void KeepInFront() => SetWindowPos(spy, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // TOPMOST; NOSIZE, NOMOVE, NOACTIVATE

    // Explorer's own taskbar: the Shell_TrayWnd that explorer.exe owns (never another spy's, such
    // as Zebar's, so two can never hand messages round in a circle).
    static IntPtr Explorer()
    {
        IntPtr found = IntPtr.Zero, at = IntPtr.Zero;
        while ((at = FindWindowEx(IntPtr.Zero, at, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (at == spy) continue;
            GetWindowThreadProcessId(at, out uint pid);
            try { using var p = Process.GetProcessById((int)pid); if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) { found = at; break; } }
            catch { }
        }
        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == 0x0113) { KeepInFront(); return IntPtr.Zero; } // WM_TIMER
            var explorer = Explorer();
            if (message == 0x004A) // WM_COPYDATA: what Shell_NotifyIcon (and the app-bar calls) send
            {
                var data = (COPYDATASTRUCT*)lParam;
                if (data->dwData == 1 && data->cbData >= 8 + 32 && data->lpData != IntPtr.Zero) Read((byte*)data->lpData, data->cbData);
                // 3: an app asking where its icon is (Shell_NotifyIconGetRect): the mouse, as
                // systray-util answers (1: its left and right, 2: its top and bottom).
                else if (data->dwData == 3 && data->lpData != IntPtr.Zero && data->cbData >= 8)
                {
                    GetCursorPos(out var at);
                    uint which = *(uint*)((byte*)data->lpData + 4);
                    if (which == 1) return (IntPtr)((at.X & 0xFFFF) | (at.X << 16));
                    if (which == 2) return (IntPtr)(((at.Y + 1) & 0xFFFF) | ((at.Y + 1) << 16));
                    return IntPtr.Zero;
                }
                return explorer != IntPtr.Zero ? SendMessage(explorer, message, wParam, lParam) : (IntPtr)1;
            }
            // The taskbar's other messages, handed on as systray-util does: its own (past WM_USER)
            // posted, WM_ACTIVATEAPP and WM_COMMAND sent.
            if (explorer != IntPtr.Zero && message > 0x0400) { PostMessage(explorer, message, wParam, lParam); return DefWindowProc(hwnd, message, wParam, lParam); }
            if (explorer != IntPtr.Zero && message is 0x0400 or 0x001C or 0x0111) return SendMessage(explorer, message, wParam, lParam);
        }
        catch (Exception ex) { App.Log(ex); }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    // TRAYNOTIFYDATA: a signature, the NIM_ message, then NOTIFYICONDATA as a 32-bit process
    // lays it out (handles as 32 bits).
    static void Read(byte* p, uint length)
    {
        if (*(uint*)p != 0x34753423) return;
        uint what = *(uint*)(p + 4);
        byte* nid = p + 8;
        uint flags = *(uint*)(nid + 12);
        var hwnd = (IntPtr)(*(int*)(nid + 4));
        uint id = *(uint*)(nid + 8);
        Guid? guid = (flags & 0x20) != 0 && length >= 8 + 952 ? *(Guid*)(nid + 936) : null;
        Icon? icon;
        bool changed = false;
        lock (icons)
        {
            icon = icons.FirstOrDefault(i => guid != null ? i.Guid == guid : i.Hwnd == hwnd && i.Id == id);
            if (what == 2) // NIM_DELETE
            {
                if (icon != null) { icons.Remove(icon); changed = true; }
            }
            else if (what is 0 or 1 or 4) // NIM_ADD, NIM_MODIFY, NIM_SETVERSION
            {
                if (icon == null) icons.Add(icon = new Icon { Hwnd = hwnd, Id = id, Guid = guid });
                if (what == 4) icon.Version = *(uint*)(nid + 800);
                if ((flags & 0x1) != 0) icon.CallbackMessage = *(uint*)(nid + 16);
                if ((flags & 0x4) != 0) icon.Tip = new string((char*)(nid + 24), 0, 128).Split('\0')[0].Trim(); // szTip: 128 at most
                if ((flags & 0x8) != 0)
                {
                    uint state = *(uint*)(nid + 280), mask = *(uint*)(nid + 284);
                    if ((mask & 1) != 0) icon.Hidden = (state & 1) != 0;
                }
                if ((flags & 0x2) != 0) { icon.Pixels = Picture((IntPtr)(*(int*)(nid + 20))); icon.Stamp++; }
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    // The icon drawn into a 32x32 BGRA picture, premultiplied (what WriteableBitmap takes). Old
    // icons with no alpha of their own get it from their mask (DrawIconEx leaves it at 0).
    static byte[]? Picture(IntPtr hicon)
    {
        if (hicon == IntPtr.Zero) return null;
        var info = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = Size, biHeight = -Size, biPlanes = 1, biBitCount = 32 };
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (!DrawIconEx(dc, 0, 0, hicon, Size, Size, 0, IntPtr.Zero, 0x3)) return null; // DI_NORMAL
            var pixels = new byte[Size * Size * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 0; i < pixels.Length; i += 4) if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) pixels[i + 3] = 255;
            return pixels;
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); }
    }

    // A click on the icon at screen point (x, y), as the taskbar sends it: version 4 icons get
    // the point and their id packed, and NIN_SELECT / WM_CONTEXTMENU after the button.
    public static void Click(Icon icon, bool right, int x, int y)
    {
        GetWindowThreadProcessId(icon.Hwnd, out uint pid);
        AllowSetForegroundWindow((int)pid); // so its window or menu can come to the front
        uint down = right ? 0x0204u : 0x0201u, up = right ? 0x0205u : 0x0202u; // WM_R/LBUTTONDOWN, UP
        uint after = right ? 0x007Bu : 0x0400u; // WM_CONTEXTMENU, NIN_SELECT
        foreach (var m in new[] { down, up, after })
        {
            if (icon.Version >= 4)
            {
                var wParam = (IntPtr)((x & 0xFFFF) | (y << 16));
                var lParam = (IntPtr)((m & 0xFFFF) | (icon.Id << 16));
                SendNotifyMessage(icon.Hwnd, icon.CallbackMessage, wParam, lParam);
            }
            else if (m != after || icon.Version == 3) SendNotifyMessage(icon.Hwnd, icon.CallbackMessage, (IntPtr)icon.Id, (IntPtr)m);
        }
    }

    // --- Win32 --------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public uint cbData; public IntPtr lpData; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint ex, IntPtr cls, IntPtr name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SendNotifyMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, uint step, IntPtr brush, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
}
