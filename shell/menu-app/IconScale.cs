namespace SystemMenu;

// Icon sizes (roadmap 1.8): ask Windows for the size an icon is drawn at, and when what comes
// back is bigger, shrink it here by area averaging: each new pixel is the mean of the pixels it
// covers, weighted by how much of each it covers. Never scaled up: a smaller icon stays its own
// size. Pure (no WinUI), so menu-app.Tests checks it.
public static class IconScale
{
    // The sizes apps ship icons in (Windows' own list); a request is the next one up, so Windows
    // hands back an image drawn for that size instead of a resized one.
    public static readonly int[] Standard = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    // The pixels a picture `units` wide covers at this scale (150% is 1.5); 48 when the size isn't
    // known yet (a control not laid out: NaN).
    public static int Pixels(double units, double scale) =>
        double.IsFinite(units) && units > 0 ? Math.Max(1, (int)Math.Round(units * (double.IsFinite(scale) ? Math.Max(scale, 0.5) : 1))) : 48;

    // The size to ask Windows for, to draw `pixels` wide.
    public static int Request(int pixels)
    {
        foreach (var s in Standard) if (s >= pixels) return s;
        return Standard[^1];
    }

    // A top-down BGRA picture (premultiplied: averaging it is right) made to fit `max` by `max`,
    // its proportions kept. Smaller already: returned as it is.
    public static (byte[] Pixels, int Width, int Height) Fit(byte[] pixels, int width, int height, int max)
    {
        if (width <= max && height <= max) return (pixels, width, height);
        double ratio = (double)max / Math.Max(width, height);
        int w = Math.Max(1, (int)Math.Round(width * ratio)), h = Math.Max(1, (int)Math.Round(height * ratio));
        double sx = (double)width / w, sy = (double)height / h;
        var output = new byte[w * h * 4];
        var sum = new double[4];
        for (int y = 0; y < h; y++)
        {
            double y0 = y * sy, y1 = y0 + sy;
            for (int x = 0; x < w; x++)
            {
                double x0 = x * sx, x1 = x0 + sx, area = 0;
                Array.Clear(sum);
                for (int iy = (int)y0; iy < Math.Min(height, (int)Math.Ceiling(y1)); iy++)
                {
                    double wy = Math.Min(y1, iy + 1) - Math.Max(y0, iy);
                    if (wy <= 0) continue;
                    for (int ix = (int)x0; ix < Math.Min(width, (int)Math.Ceiling(x1)); ix++)
                    {
                        double wx = Math.Min(x1, ix + 1) - Math.Max(x0, ix);
                        if (wx <= 0) continue;
                        double weight = wx * wy;
                        int i = (iy * width + ix) * 4;
                        for (int c = 0; c < 4; c++) sum[c] += pixels[i + c] * weight;
                        area += weight;
                    }
                }
                int o = (y * w + x) * 4;
                for (int c = 0; c < 4; c++) output[o + c] = (byte)Math.Clamp(Math.Round(sum[c] / area), 0, 255);
            }
        }
        return (output, w, h);
    }
}
