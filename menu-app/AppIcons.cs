using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SystemMenu;

// Real app icons: the shell's own icon for shell:AppsFolder\<app id> (desktop programs and
// Store apps alike), made on a background STA thread and cached while the menu runs.
public static unsafe class AppIcons
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, in Guid riid, out IntPtr factory);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, ref BITMAP bitmap);

    // IShellItemImageFactory, called by its vtable (GetImage is its one method, slot 3): built-in
    // COM ([ComImport]) can't be compiled as one native program (Native AOT).
    static readonly Guid ImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
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
        SHCreateItemFromParsingName(path, IntPtr.Zero, ImageFactoryIid, out var factory);
        IntPtr hbitmap;
        try
        {
            var getImage = (delegate* unmanaged[Stdcall]<IntPtr, SIZE, int, IntPtr*, int>)Vtable.Slot(factory, 3);
            Vtable.Check(getImage(factory, new SIZE { cx = size, cy = size }, 0, &hbitmap));
        }
        finally { Marshal.Release(factory); }
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
