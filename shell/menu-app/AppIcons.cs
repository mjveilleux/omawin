using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SystemMenu;

// Real app icons: the shell's own icon for shell:AppsFolder\<app id> (desktop programs and
// Store apps alike), made on a background STA thread and cached while the menu runs.
//
// Crisp at any scale (roadmap 1.8): each request says how big it's drawn, in units, and the
// monitor's scale; Windows is asked for the next standard icon size at or above those pixels
// (one the app ships, rather than one it resizes), a bigger image is shrunk here to the exact
// pixels (IconScale), a smaller one is never stretched, and the caller draws it 1:1.
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

    // IShellItemImageFactory::GetImage's flags: a bigger image is fine (we shrink it well), and
    // an app's icon rather than a thumbnail of it.
    const int BiggerSizeOk = 0x1, IconOnly = 0x4;
    // Icons are small and few, but files ("path:") have no end, so the cache is bounded: when
    // it's full the oldest half goes. Previews ("big:", 256 px) aren't cached at all.
    const int MaxCached = 2000;
    // An icon, with its size in pixels (it's drawn at Pixels / scale units: one image pixel on one
    // screen pixel).
    sealed record Icon(ImageSource Image, int Pixels);
    static readonly Dictionary<string, Icon?> cache = [];
    static readonly Queue<string> cacheOrder = new();
    static readonly Dictionary<string, List<Action<Icon>>> waiting = [];
    static readonly System.Collections.Concurrent.BlockingCollection<(string Key, string Id, int Pixels)> queue = [];
    static DispatcherQueue? ui;

    // Calls back on the UI thread with the icon and the size to draw it at, in units (right away
    // if it's known already): an app id, or a file as "path:C:\..." (its icon, or a thumbnail for
    // pictures), drawn `units` wide on a monitor at `scale` (XamlRoot.RasterizationScale). The
    // size it calls back with is `units` or less: an icon only made smaller is drawn at its own size.
    public static void Get(string id, double units, double scale, Action<ImageSource, double> done)
    {
        int pixels = IconScale.Pixels(units, scale);
        Request($"{pixels}|{id}", id, pixels, icon => done(icon.Image, icon.Pixels / Math.Max(scale, 0.5)));
    }

    // A file's preview ("big:C:\..."), 256 pixels, not cached: the preview pane fits it itself.
    public static void GetPreview(string path, Action<ImageSource> done) =>
        Request("big:" + path, "big:" + path, 256, icon => done(icon.Image));

    static void Request(string key, string id, int pixels, Action<Icon> done)
    {
        if (cache.TryGetValue(key, out var known)) { if (known != null) done(known); return; }
        if (waiting.TryGetValue(key, out var list)) { list.Add(done); return; }
        waiting[key] = [done];
        if (ui == null)
        {
            ui = DispatcherQueue.GetForCurrentThread();
            var worker = new Thread(Work) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
        queue.Add((key, id, pixels));
    }

    static void Work()
    {
        foreach (var (key, id, want) in queue.GetConsumingEnumerable())
        {
            byte[]? pixels = null;
            int width = 0, height = 0;
            try
            {
                if (id.StartsWith("big:")) (pixels, width, height) = Pixels(id[4..], want, 0);
                else
                {
                    bool app = !id.StartsWith("path:");
                    var (raw, w, h) = Pixels(app ? @"shell:AppsFolder\" + id : id[5..], IconScale.Request(want),
                        BiggerSizeOk | (app ? IconOnly : 0));
                    (pixels, width, height) = IconScale.Fit(raw, w, h, want);
                }
            }
            catch { }
            ui!.TryEnqueue(() =>
            {
                Icon? icon = null;
                if (pixels != null)
                {
                    var bitmap = new WriteableBitmap(width, height);
                    using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
                    bitmap.Invalidate();
                    icon = new Icon(bitmap, Math.Max(width, height));
                }
                if (!id.StartsWith("big:") && cache.TryAdd(key, icon))
                {
                    cacheOrder.Enqueue(key);
                    if (cache.Count > MaxCached)
                        while (cache.Count > MaxCached / 2) cache.Remove(cacheOrder.Dequeue());
                }
                if (waiting.Remove(key, out var callbacks) && icon != null) foreach (var c in callbacks) c(icon);
            });
        }
    }

    // Top-down BGRA, premultiplied: what WriteableBitmap wants.
    static (byte[], int, int) Pixels(string path, int size, int flags)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, ImageFactoryIid, out var factory);
        IntPtr hbitmap;
        try
        {
            var getImage = (delegate* unmanaged[Stdcall]<IntPtr, SIZE, int, IntPtr*, int>)Vtable.Slot(factory, 3);
            Vtable.Check(getImage(factory, new SIZE { cx = size, cy = size }, flags, &hbitmap));
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
