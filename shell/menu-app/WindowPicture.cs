namespace SystemMenu;

// Where a window's live picture goes (roadmap 8.5: One Search's Windows, its preview): the
// preview's box, from the layout's device-independent units to the screen's pixels, and the
// picture fitted inside it in the window's own shape. Pure arithmetic, tested in menu-app.Tests;
// WindowThumbnail.cs hands the result to DWM.
public readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Empty => Width <= 0 || Height <= 0;
}

public static class WindowPicture
{
    // A box laid out at (x, y), width × height in DIPs, on a monitor at `scale` (1.5 at 150%),
    // `inset` DIPs in from each side: the pixels DWM draws into. Rounded outwards at the start
    // and inwards at the end, so the picture never spills over the box's rounded corners.
    public static PixelBox ToPixels(double x, double y, double width, double height, double scale, double inset = 0)
    {
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;
        double l = (x + inset) * scale, t = (y + inset) * scale;
        double r = (x + width - inset) * scale, b = (y + height - inset) * scale;
        var box = new PixelBox((int)Math.Ceiling(l), (int)Math.Ceiling(t), (int)Math.Floor(r), (int)Math.Floor(b));
        return box.Empty ? default : box;
    }

    // The source (sourceWidth × sourceHeight pixels) as big as fits in the box, its shape kept,
    // centred; never drawn bigger than it is (a small dialog stays sharp, as in Alt+Tab).
    public static PixelBox Fit(int sourceWidth, int sourceHeight, PixelBox into)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || into.Empty) return default;
        double scale = Math.Min(1, Math.Min((double)into.Width / sourceWidth, (double)into.Height / sourceHeight));
        int w = Math.Max(1, (int)Math.Round(sourceWidth * scale)), h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        int left = into.Left + (into.Width - w) / 2, top = into.Top + (into.Height - h) / 2;
        return new PixelBox(left, top, left + w, top + h);
    }
}
