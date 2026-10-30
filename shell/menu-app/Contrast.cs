using System.Globalization;

namespace SystemMenu;

// Readable colours from any theme (plans/09-for-everyone.md §4). Themes are Omarchy's palettes,
// kept as they are, and some of their colours are too faint for text: a theme's "comment" is
// 1.7:1 on its background in Everforest, and Rosé Pine's accent is 3.1:1. WCAG asks 4.5:1 for
// text and 3:1 for icons and borders. So text never uses a palette colour directly when it might
// be faint: Readable() keeps the colour's hue and moves it toward the foreground just enough.
// Pure, so menu-app.Tests checks every shipped theme.
public static class Contrast
{
    public const double Text = 4.5;      // WCAG AA, normal text
    public const double Graphic = 3.0;   // WCAG AA, icons, borders, focus rings, large text

    // "#rrggbb" (or "#aarrggbb", whose alpha is ignored) to 0..1 channels.
    public static (double R, double G, double B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"not a #rrggbb colour: {hex}");
        return (((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }

    static double Channel(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    // WCAG relative luminance.
    public static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    // 1 (the same) to 21 (black on white); order doesn't matter.
    public static double Ratio(string a, string b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    public static string Mix(string from, string to, double t)
    {
        var (r1, g1, b1) = Parse(from);
        var (r2, g2, b2) = Parse(to);
        static int C(double a, double b, double t) => (int)Math.Round((a + (b - a) * t) * 255);
        return $"#{C(r1, r2, t):x2}{C(g1, g2, t):x2}{C(b1, b2, t):x2}";
    }

    // color itself when it reaches min against background; else the least mix toward the theme's
    // foreground that does (the foreground reaches it in every shipped theme, and when it doesn't,
    // the best of the two is returned rather than inventing a colour outside the theme).
    public static string Readable(string color, string background, string foreground, double min = Text)
    {
        if (Ratio(color, background) >= min) return color;
        if (Ratio(foreground, background) < min) return Ratio(foreground, background) > Ratio(color, background) ? foreground : color;
        double lo = 0, hi = 1;
        for (int i = 0; i < 20; i++) // the ratio grows with t toward a readable foreground
        {
            var mid = (lo + hi) / 2;
            if (Ratio(Mix(color, foreground, mid), background) >= min) hi = mid; else lo = mid;
        }
        return Mix(color, foreground, hi);
    }
}
