namespace SystemMenu;

// Which rows of a long list are drawn (roadmap 1.7). The launcher's search can match a thousand
// apps; binding a pooled row to each costs more than a frame per key, so only the rows in view
// and a few either side are bound to views, and the list's padding stands for the rest, at the
// rows' own pitch, so every row keeps the place it would have had. Rows [First, End).
// Pure arithmetic (no WinUI): tested in test/menu-app.Tests.
public readonly record struct RowWindow(int First, int End)
{
    public int Count => End - First;

    public bool Contains(int row) => row >= First && row < End;

    // Every row of `other` is in this one.
    public bool Covers(RowWindow other) => other.Count == 0 || (other.First >= First && other.End <= End);

    // The rows any part of which shows anywhere between scrolled to `fromY` and to `toY` (a
    // glide passes through all of them), and `extra` rows either side, within [0, total).
    public static RowWindow Spanning(int total, double fromY, double toY, double viewHeight, double pitch, int extra)
    {
        if (total <= 0 || pitch <= 0 || double.IsNaN(fromY) || double.IsNaN(toY)) return new(0, Math.Max(0, Math.Min(total, extra)));
        double top = Math.Max(0, Math.Min(fromY, toY));
        double bottom = Math.Max(fromY, toY) + Math.Max(0, viewHeight);
        int first = (int)Math.Floor(top / pitch) - extra;
        int end = (int)Math.Ceiling(bottom / pitch) + extra;
        first = Math.Clamp(first, 0, total);
        return new(first, Math.Clamp(end, first, total));
    }

    // The rows in view at `y`, and `extra` either side.
    public static RowWindow At(int total, double y, double viewHeight, double pitch, int extra) =>
        Spanning(total, y, y, viewHeight, pitch, extra);

    // The padding above and below the bound rows that stands for the unbound ones, so the list is
    // as tall as if every row were there (total pitches less the gap after the last) and row i
    // starts at i pitches. A pitch is a row and the panel's `gap` under it; the panel puts no gap
    // after its last child, so with nothing bound the padding below gives that gap back.
    public (double Above, double Below) Padding(int total, double pitch, double gap) =>
        (First * pitch, Math.Max(0, (total - End) * pitch - (Count > 0 ? 0 : gap)));
}
