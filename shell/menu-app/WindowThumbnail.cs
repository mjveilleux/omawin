using System.Runtime.InteropServices;

namespace SystemMenu;

// Another window, live, drawn into one of ours by the compositor (DWM's thumbnail API: what
// Alt+Tab and the taskbar's previews use; nothing is captured or copied, and it moves as the
// window does). Roadmap 8.5: One Search's Windows show the selected one in the preview. DWM draws
// it above the window's XAML, at a place in that window's pixels (WindowPicture.cs works it out).
// GlazeWM cloaks the windows of other workspaces (hide_method: cloak), and DWM keeps drawing
// cloaked windows; a minimized one has no picture, so the caller keeps its icon then.
public sealed partial class WindowThumbnail : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        public int fVisible, fSourceClientAreaOnly;
    }

    const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [LibraryImport("dwmapi.dll")] private static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUnregisterThumbnail(nint thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUpdateThumbnailProperties(nint thumbnail, in DWM_THUMBNAIL_PROPERTIES properties);
    [LibraryImport("dwmapi.dll")] private static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);

    nint handle;
    public nint Source { get; }

    WindowThumbnail(nint source, nint handle) { Source = source; this.handle = handle; }

    // A picture of `source` in `destination` (a top-level window of this process), not shown
    // until Place; null when there's nothing to draw (the window's gone or minimized).
    public static WindowThumbnail? Of(nint destination, nint source)
    {
        if (source == 0 || !IsWindow(source) || IsIconic(source)) return null;
        return DwmRegisterThumbnail(destination, source, out var thumbnail) >= 0 && thumbnail != 0 ? new WindowThumbnail(source, thumbnail) : null;
    }

    // The source's size now, in its pixels (it can change while shown: Place asks each time).
    public (int Width, int Height) SourceSize => handle != 0 && DwmQueryThumbnailSourceSize(handle, out var s) >= 0 ? (s.cx, s.cy) : (0, 0);

    // Drawn fitted into `box` (pixels of the destination window), or hidden when the box is
    // empty (the preview scrolled away or collapsed). False when DWM refused.
    public bool Place(PixelBox box)
    {
        if (handle == 0) return false;
        var (w, h) = SourceSize;
        var at = WindowPicture.Fit(w, h, box);
        var p = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = at.Left, Top = at.Top, Right = at.Right, Bottom = at.Bottom },
            opacity = 255,
            fVisible = at.Empty ? 0 : 1,
            fSourceClientAreaOnly = 0, // the whole window, its title bar too: what you'd see
        };
        return DwmUpdateThumbnailProperties(handle, p) >= 0;
    }

    public void Dispose()
    {
        if (handle != 0) DwmUnregisterThumbnail(handle);
        handle = 0;
    }
}
