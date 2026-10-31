using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Omawin.MotionCheck;

// The Windows calls: the screen's pixels (GDI), the menu app's window, a key, a plain background.
// Plain P/Invoke, so the project builds on Linux too (the tests); only Program.Run calls in here,
// after checking it's on Windows.
[SupportedOSPlatform("windows")]
static class Win32
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public POINT Pt; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int Size, Style;
        public IntPtr WndProc;
        public int ClsExtra, WndExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSmall;
    }
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint colorref);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);

    public const uint WM_CLOSE = 0x10, WM_DESTROY = 0x2;
    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    // Windows' Animation effects switch (Settings > Accessibility > Visual effects): what WinUI's
    // UISettings.AnimationsEnabled reads, and the menu app with it (Motion.cs).
    const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043, SPIF_SENDCHANGE = 0x2;

    // Physical pixels everywhere (the menu app is per-monitor aware too).
    public static void DpiAware() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2

    public static (int Width, int Height) Screen => (GetSystemMetrics(0), GetSystemMetrics(1));

    public static bool ClientAnimation
    {
        get { int on = 0; SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0); return on != 0; }
        set => SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, new IntPtr(value ? 1 : 0), SPIF_SENDCHANGE);
    }

    // The window's visible frame (DWM's extended frame bounds: without the invisible resize borders).
    public static RECT? Frame(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, 9, out var r, Marshal.SizeOf<RECT>()) == 0 ? r : null;

    // A top-level window of process `pid` titled `title`, shown or not.
    public static IntPtr Find(int pid, string title)
    {
        IntPtr found = IntPtr.Zero;
        var text = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint owner);
            if (owner != pid) return true;
            text.Clear();
            GetWindowText(h, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static void PressEscape()
    {
        keybd_event(0x1B, 0, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0, 2, UIntPtr.Zero); // KEYEVENTF_KEYUP
    }

    // Copies one rectangle of the screen, as composed (CAPTUREBLT: layered and DirectComposition
    // windows included), into memory, as often as asked.
    public sealed class Grabber : IDisposable
    {
        readonly IntPtr screen, memory, bitmap, old, bits;
        public readonly int X, Y, Width, Height;

        public Grabber(int x, int y, int width, int height)
        {
            (X, Y, Width, Height) = (x, y, width, height);
            screen = GetDC(IntPtr.Zero);
            memory = CreateCompatibleDC(screen);
            var info = new BITMAPINFOHEADER
            {
                Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = width, Height = -height, // negative: top row first
                Planes = 1, BitCount = 32,
            };
            bitmap = CreateDIBSection(memory, ref info, 0, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException($"no {width}x{height} bitmap");
            old = SelectObject(memory, bitmap);
        }

        public byte[] Grab()
        {
            if (!BitBlt(memory, 0, 0, Width, Height, screen, X, Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed");
            GdiFlush();
            var bytes = new byte[Width * Height * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose()
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // A window of one colour over the whole primary screen, under the menu (which is always on
    // top) and over anything else: the menu's background is the same every run, and nothing
    // behind it (a console printing) changes while it's measured. Never takes focus. Its own
    // thread runs its messages.
    public sealed class Backdrop : IDisposable
    {
        readonly Thread thread;
        readonly ManualResetEventSlim up = new();
        WndProc? proc; // held: Windows calls it for as long as the window lives
        IntPtr hwnd;

        public Backdrop(byte r, byte g, byte b)
        {
            thread = new Thread(() => Run((uint)(r | g << 8 | b << 16))) { IsBackground = true, Name = "backdrop" };
            thread.Start();
            if (!up.Wait(TimeSpan.FromSeconds(10)) || hwnd == IntPtr.Zero) throw new InvalidOperationException("the backdrop window didn't open");
        }

        void Run(uint colorref)
        {
            DpiAware();
            proc = (h, msg, w, l) =>
            {
                if (msg == WM_DESTROY) PostQuitMessage(0);
                return DefWindowProc(h, msg, w, l);
            };
            var cls = new WNDCLASSEX
            {
                Size = Marshal.SizeOf<WNDCLASSEX>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(proc),
                Instance = GetModuleHandle(null),
                Background = CreateSolidBrush(colorref), // painted by DefWindowProc's WM_ERASEBKGND
                ClassName = "omawin-motion-check-backdrop",
            };
            RegisterClassEx(ref cls);
            var (w, h) = Screen;
            const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls.ClassName, "motion-check backdrop", WS_POPUP,
                0, 0, w, h, IntPtr.Zero, IntPtr.Zero, cls.Instance, IntPtr.Zero);
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
            up.Set();
            while (hwnd != IntPtr.Zero && GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        public void Dispose()
        {
            if (hwnd != IntPtr.Zero) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    // Grabs frames of one rectangle on its own thread (which owns the GDI objects), about 60 a
    // second, each stamped with the clock's time.
    public sealed class Recorder : IDisposable
    {
        readonly int x, y, width, height;
        readonly Stopwatch clock;
        readonly List<(double Ms, byte[] Bytes)> frames = [];
        readonly Thread thread;
        volatile bool stop;
        Exception? failed;

        public Recorder(int x, int y, int width, int height, Stopwatch clock)
        {
            (this.x, this.y, this.width, this.height) = (x, y, width, height);
            this.clock = clock;
            thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "recorder" };
            thread.Start();
        }

        void Loop()
        {
            const double interval = 1000.0 / 60;
            timeBeginPeriod(1);
            try
            {
                using var grabber = new Grabber(x, y, width, height);
                double next = clock.Elapsed.TotalMilliseconds;
                while (!stop)
                {
                    double at = clock.Elapsed.TotalMilliseconds;
                    var bytes = grabber.Grab();
                    lock (frames) frames.Add((at, bytes));
                    next += interval;
                    double now = clock.Elapsed.TotalMilliseconds;
                    if (next < now) next = now; // behind: don't try to catch up with a burst
                    else Thread.Sleep(TimeSpan.FromMilliseconds(next - now));
                }
            }
            catch (Exception ex) { failed = ex; }
            finally { timeEndPeriod(1); }
        }

        // Stops, and hands back every frame, timed from `zeroMs` on the same clock.
        public List<Frame> Stop(double zeroMs)
        {
            stop = true;
            thread.Join();
            if (failed != null) throw new InvalidOperationException("recording failed", failed);
            lock (frames) return frames.Select(f => new Frame(f.Ms - zeroMs, width, height, f.Bytes)).ToList();
        }

        public void Dispose()
        {
            stop = true;
            thread.Join();
        }
    }
}
