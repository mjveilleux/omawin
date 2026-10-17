using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SystemMenu;

// Real app icons: the shell's own icon for shell:AppsFolder\<app id> (desktop programs and
// Store apps alike), made on a background STA thread and cached while the menu runs.
public static class AppIcons
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, ref BITMAP bitmap);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr bitmap); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    const int Size = 48;
    // Icons are small and few, but files ("path:") have no end, so the cache is bounded: when
    // it's full the oldest half goes. Previews ("big:", 256 px) aren't cached at all.
    const int MaxCached = 2000;
    static readonly Dictionary<string, ImageSource?> cache = [];
    static readonly Queue<string> cacheOrder = new();
    static readonly Dictionary<string, List<Action<ImageSource>>> waiting = [];
    static readonly System.Collections.Concurrent.BlockingCollection<string> queue = [];
    static DispatcherQueue? ui;

    // Calls back on the UI thread with the icon (right away if it's known already).
    public static void Get(string appId, Action<ImageSource> done)
    {
        if (cache.TryGetValue(appId, out var known)) { if (known != null) done(known); return; }
        if (waiting.TryGetValue(appId, out var list)) { list.Add(done); return; }
        waiting[appId] = [done];
        if (ui == null)
        {
            ui = DispatcherQueue.GetForCurrentThread();
            var worker = new Thread(Work) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
        queue.Add(appId);
    }

    static void Work()
    {
        foreach (var id in queue.GetConsumingEnumerable())
        {
            byte[]? pixels = null;
            int width = 0, height = 0;
            // An app id, or a file (as "path:C:\...": its icon, or a thumbnail for pictures;
            // "big:C:\..." the same at preview size).
            try
            {
                (pixels, width, height) = id.StartsWith("big:") ? Pixels(id[4..], 256)
                    : Pixels(id.StartsWith("path:") ? id[5..] : @"shell:AppsFolder\" + id, Size);
            }
            catch { }
            ui!.TryEnqueue(() =>
            {
                ImageSource? image = null;
                if (pixels != null)
                {
                    var bitmap = new WriteableBitmap(width, height);
                    using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
                    bitmap.Invalidate();
                    image = bitmap;
                }
                if (!id.StartsWith("big:") && cache.TryAdd(id, image))
                {
                    cacheOrder.Enqueue(id);
                    if (cache.Count > MaxCached)
                        while (cache.Count > MaxCached / 2) cache.Remove(cacheOrder.Dequeue());
                }
                if (waiting.Remove(id, out var callbacks) && image != null) foreach (var c in callbacks) c(image);
            });
        }
    }

    // Top-down BGRA, premultiplied: what WriteableBitmap wants.
    static (byte[], int, int) Pixels(string path, int size)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        factory.GetImage(new SIZE { cx = size, cy = size }, 0, out var hbitmap);
        try
        {
            var info = new BITMAP();
            GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref info);
            if (info.bmBitsPixel != 32 || info.bmBits == IntPtr.Zero) throw new InvalidOperationException();
            int w = info.bmWidth, h = Math.Abs(info.bmHeight), stride = info.bmWidthBytes;
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int from = info.bmHeight > 0 ? h - 1 - y : y; // bottom-up unless the height is negative
                Marshal.Copy(info.bmBits + from * stride, pixels, y * w * 4, w * 4);
            }
            return (pixels, w, h);
        }
        finally { DeleteObject(hbitmap); }
    }
}
