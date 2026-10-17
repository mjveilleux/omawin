// app-icons: the real app icons for the Zebar dock, as PNG data URLs.
//
//   app-icons <hwnd> [<hwnd> ...]
//
// Prints one line per window: "<hwnd> data:image/png;base64,..." (or "<hwnd> -" when there's
// no icon). The icon is the program's own, from its .exe, at 64px (the shell's large icon,
// with transparency); for Store apps hosted by ApplicationFrameHost, and as a fallback, the
// window's own icon. The dock asks once per app and keeps the answer.
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class AppIcons
{
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, ref BITMAP bitmap);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr bitmap); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    const int Size = 64;

    static string ExePath(IntPtr hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        IntPtr process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return null;
        try
        {
            var name = new StringBuilder(1024);
            int size = name.Capacity;
            return QueryFullProcessImageName(process, 0, name, ref size) ? name.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    // The shell's icon for a file, with its alpha channel (Bitmap.FromHbitmap would drop it).
    static Bitmap FromFile(string path)
    {
        IShellItemImageFactory factory;
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
        IntPtr hbitmap;
        factory.GetImage(new SIZE { cx = Size, cy = Size }, 0x4 /* SIIGBF_ICONONLY */, out hbitmap);
        try
        {
            var info = new BITMAP();
            GetObject(hbitmap, Marshal.SizeOf(typeof(BITMAP)), ref info);
            if (info.bmBitsPixel != 32 || info.bmBits == IntPtr.Zero) return null;
            int height = Math.Abs(info.bmHeight);
            // Bottom-up rows unless the height is negative.
            IntPtr first = info.bmHeight > 0 ? info.bmBits + (height - 1) * info.bmWidthBytes : info.bmBits;
            int stride = info.bmHeight > 0 ? -info.bmWidthBytes : info.bmWidthBytes;
            using (var view = new Bitmap(info.bmWidth, height, stride, PixelFormat.Format32bppPArgb, first))
                return new Bitmap(view);
        }
        finally { DeleteObject(hbitmap); }
    }

    static Bitmap FromWindow(IntPtr hwnd)
    {
        IntPtr icon;
        SendMessageTimeout(hwnd, 0x7F /* WM_GETICON */, (IntPtr)1 /* ICON_BIG */, IntPtr.Zero, 2, 200, out icon);
        if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, -14); // GCLP_HICON
        if (icon == IntPtr.Zero) return null;
        using (var i = Icon.FromHandle(icon)) return i.ToBitmap();
    }

    static string DataUrl(Bitmap bitmap)
    {
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        foreach (var arg in args)
        {
            long handle;
            if (!long.TryParse(arg, out handle)) continue;
            var hwnd = (IntPtr)handle;
            string url = "-";
            try
            {
                string exe = ExePath(hwnd);
                Bitmap bitmap = null;
                if (exe != null && !exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                    try { bitmap = FromFile(exe); } catch { }
                if (bitmap == null) bitmap = FromWindow(hwnd);
                if (bitmap != null) using (bitmap) url = DataUrl(bitmap);
            }
            catch { }
            Console.WriteLine(arg + " " + url);
        }
    }
}
