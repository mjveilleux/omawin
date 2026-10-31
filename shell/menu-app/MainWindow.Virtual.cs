using Microsoft.UI.Xaml;

namespace SystemMenu;

// The launcher's search, virtualized (roadmap 1.7): a query can match a thousand apps, and binding
// a pooled row to each (its text runs, its icon, its tag) cost more than a frame per key. Now every
// match is a row in `rows` (what Enter runs), but only the rows in view and a few either side are
// bound to pooled views (RowWindow.cs); the list's padding stands for the rest, so the scrolling,
// the cascade and the lift work as before on the same views. Scrolling binds the rows it's about
// to show before it moves. Menus and pages (short, some with titles) still draw every row.
public sealed partial class MainWindow
{
    // Set while the list is virtualized: draws list row i into a pooled view.
    Action<RowView, int>? drawRow;
    RowWindow bound;             // the list rows pool[0..used) are showing
    const int Overscan = 6;      // rows bound beyond the view, either side
    const int Slack = 2;         // a scroll that comes within this many rows of the bound ones' end binds afresh
    const double GlideScreens = 3; // a scroll further than this many screens jumps (a glide would pass rows it can't bind in time)

    // The rows' height to cover: the view, or before the window has its size, what it will have.
    double VirtualView => Math.Max(ViewHeight, bodyDip);

    // Every drawing starts unvirtualized (Render, first thing).
    void EndVirtual()
    {
        drawRow = null;
        bound = default;
        Rows.Padding = new Thickness(0);
    }

    // The rows are already in `rows`; draw only the first screenful of them.
    void Virtualize(Action<RowView, int> draw)
    {
        drawRow = draw;
        BindRows(RowWindow.At(rows.Count, 0, VirtualView, Pitch, Overscan));
    }

    // Binds pooled views to list rows [window.First, window.End), in order, and pads the list
    // above and below so each sits at its own row's place.
    void BindRows(RowWindow window)
    {
        if (drawRow == null) return;
        used = 0;
        liftedRow = null; // NextRow clears every view's lift
        for (int i = window.First; i < window.End; i++)
        {
            var v = NextRow(RowDip - 2, 28);
            v.Index = i; // the list's row, not the pool's: hover, click and the cascade go by it
            drawRow(v, i);
        }
        for (int i = used; i < pool.Count; i++) pool[i].Slot.Visibility = Visibility.Collapsed;
        bound = window;
        var (above, below) = window.Padding(rows.Count, Pitch, 2); // Rows' Spacing (MainWindow.xaml)
        Rows.Padding = new Thickness(0, above, 0, below);
        if (ViewAt(selected) is { } lifted) { liftedRow = lifted; LiftRow(lifted, true); }
    }

    // Before scrolling from `from` to `to`: binds the rows that will show on the way. Returns
    // whether to glide there: a glide much further than a screen or two jumps instead.
    bool EnsureBound(double from, double to, bool animate)
    {
        if (drawRow == null) return animate;
        double view = VirtualView;
        if (animate && Math.Abs(to - from) > GlideScreens * view) animate = false;
        double start = animate ? from : to;
        if (!bound.Covers(RowWindow.Spanning(rows.Count, start, to, view, Pitch, Slack)))
            BindRows(RowWindow.Spanning(rows.Count, start, to, view, Pitch, Overscan));
        return animate;
    }

    // The view drawing list row `index`, if one is.
    RowView? ViewAt(int index)
    {
        int p = drawRow != null ? index - bound.First : index;
        return index >= 0 && index < rows.Count && p >= 0 && p < used ? pool[p] : null;
    }
}
