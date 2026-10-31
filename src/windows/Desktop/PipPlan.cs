namespace Omawin.Windows;

// Picture-in-picture's arithmetic (roadmap 8.6): where the small window goes and where the live
// picture sits inside it. Pure: Desktop/Pip.cs is the window.
public readonly record struct Box(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class PipPlan
{
    // The source's picture as big as fits in the window, its shape kept, centred (bars either side
    // or above and below rather than a stretched picture).
    public static Box Fit(int sourceWidth, int sourceHeight, Box into)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || into.Width <= 0 || into.Height <= 0) return into;
        double scale = Math.Min((double)into.Width / sourceWidth, (double)into.Height / sourceHeight);
        int w = (int)Math.Round(sourceWidth * scale), h = (int)Math.Round(sourceHeight * scale);
        int left = into.Left + (into.Width - w) / 2, top = into.Top + (into.Height - h) / 2;
        return new Box(left, top, left + w, top + h);
    }

    // The window's first place: the bottom-right corner of the work area (the screen less the
    // bar), a quarter of its width (at least 320 px), in the source's shape, a margin from the edges.
    public static Box Corner(Box workArea, int sourceWidth, int sourceHeight, int margin = 16)
    {
        int width = Math.Max(320, workArea.Width / 4);
        double ratio = sourceWidth > 0 && sourceHeight > 0 ? (double)sourceHeight / sourceWidth : 9.0 / 16;
        int height = Math.Min((int)Math.Round(width * ratio), workArea.Height / 2);
        return new Box(workArea.Right - margin - width, workArea.Bottom - margin - height, workArea.Right - margin, workArea.Bottom - margin);
    }

    // The scroll wheel's opacity steps: never so faint it's lost.
    public static byte Opacity(byte now, int wheel) => (byte)Math.Clamp(now + (wheel > 0 ? 25 : -25), 60, 255);
}
