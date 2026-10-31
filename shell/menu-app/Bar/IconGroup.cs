namespace SystemMenu;

// A program's icon as its file holds it (an RT_GROUP_ICON resource: one picture per size), and
// which picture the bar's tray should draw (roadmap 1.8 step 5). An app hands the tray one
// picture, often its 16-pixel one; when that came from its own resources, Tray.cs reads the
// group and takes the size the bar needs instead of stretching the small one. Pure: tested in
// menu-app.Tests (Look/IconGroupTests.cs).
public static class IconGroup
{
    public readonly record struct Entry(int Width, int Height, int BitCount, ushort Id);

    // GRPICONDIR: reserved, type (1: icons), count, then 14-byte entries: width, height (0 means
    // 256), colours, reserved, planes, bit count, bytes, the RT_ICON resource's id.
    public static List<Entry> Parse(ReadOnlySpan<byte> data)
    {
        var list = new List<Entry>();
        if (data.Length < 6 || U16(data, 2) != 1) return list;
        int count = U16(data, 4);
        for (int i = 0; i < count; i++)
        {
            int at = 6 + i * 14;
            if (at + 14 > data.Length) break;
            int w = data[at] == 0 ? 256 : data[at], h = data[at + 1] == 0 ? 256 : data[at + 1];
            list.Add(new Entry(w, h, U16(data, at + 6), (ushort)U16(data, at + 12)));
        }
        return list;
    }

    static int U16(ReadOnlySpan<byte> d, int at) => d[at] | d[at + 1] << 8;

    // The picture to draw for want pixels: square ones, the most colours first (32-bit has
    // alpha), the smallest at or above want (shrunk well by IconScale), else the largest there is
    // (drawn at its own size, never stretched). Null when there's nothing usable.
    public static Entry? Choose(IReadOnlyList<Entry> entries, int want)
    {
        var square = entries.Where(e => e.Width == e.Height && e.Width > 0).ToList();
        if (square.Count == 0) return null;
        int depth = square.Max(e => e.BitCount);
        var best = square.Where(e => e.BitCount == depth).ToList();
        var big = best.Where(e => e.Width >= want).OrderBy(e => e.Width).ToList();
        return big.Count > 0 ? big[0] : best.OrderByDescending(e => e.Width).First();
    }
}
