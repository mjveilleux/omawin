using System.Runtime.InteropServices;

namespace Omawin.Windows;

// The clipboard's text, read and replaced from a console program (user32's clipboard needs no
// window of ours: OpenClipboard(NULL) ties it to this task). Used by `omawin clip` (8.14) and by
// `omawin who-has` with no path (the path Explorer's "Copy as path" left there).
public static unsafe partial class Clipboard
{
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] private static partial IntPtr GetClipboardData(uint format);
    [LibraryImport("user32.dll")] private static partial IntPtr SetClipboardData(uint format, IntPtr data);
    [LibraryImport("kernel32.dll")] private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] private static partial IntPtr GlobalFree(IntPtr mem);
    [LibraryImport("kernel32.dll")] private static partial void* GlobalLock(IntPtr mem);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr mem);
    [LibraryImport("kernel32.dll")] private static partial nuint GlobalSize(IntPtr mem);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(uint exStyle, string className, string? title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    // Another program may have it open for a moment (a clipboard manager reading what changed).
    static bool Open(IntPtr owner = 0)
    {
        for (int i = 0; i < 20; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(25);
        }
        return false;
    }

    // The text on the clipboard, or null when there's none (a picture, files, nothing).
    public static string? GetText()
    {
        if (!Open()) throw new IOException("another program is holding the clipboard");
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            var p = (char*)GlobalLock(h);
            if (p == null) return null;
            try
            {
                // Up to the first NUL, and never past the block (a program may leave it unterminated).
                int max = (int)Math.Min((ulong)GlobalSize(h) / 2, int.MaxValue);
                int n = 0;
                while (n < max && p[n] != '\0') n++;
                return new string(p, 0, n);
            }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    // Replaces the clipboard with this text alone (which also drops any formatting). Opened with a
    // window of ours (a message-only one): opened with none, EmptyClipboard leaves the clipboard
    // without an owner, and Windows documents that SetClipboardData then fails.
    public static void SetText(string text)
    {
        const nint HWND_MESSAGE = -3;
        var owner = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        if (owner == IntPtr.Zero) throw new IOException("couldn't make a window to own the clipboard");
        try { SetText(text, owner); }
        finally { DestroyWindow(owner); }
    }

    static void SetText(string text, IntPtr owner)
    {
        var bytes = (nuint)((text.Length + 1) * 2);
        var h = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (h == IntPtr.Zero) throw new OutOfMemoryException();
        var p = (char*)GlobalLock(h);
        if (p == null) { GlobalFree(h); throw new OutOfMemoryException(); }
        fixed (char* s = text) Buffer.MemoryCopy(s, p, (long)bytes, text.Length * 2);
        p[text.Length] = '\0';
        GlobalUnlock(h);
        if (!Open(owner)) { GlobalFree(h); throw new IOException("another program is holding the clipboard"); }
        try
        {
            EmptyClipboard();
            // On success the clipboard owns the memory; only a failure leaves it ours to free.
            if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero) { GlobalFree(h); throw new IOException("Windows refused the new clipboard text"); }
        }
        finally { CloseClipboard(); }
    }
}
