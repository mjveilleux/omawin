namespace SystemMenu.Tests.Look;

// The launcher's long list binds only the rows in view, and its padding keeps every row in its
// place (roadmap 1.7). Rows of 44 (42 and a gap of 2), a view of 440: ten rows.
public class RowWindowTests
{
    const double Pitch = 44, Gap = 2, View = 440;

    [Fact]
    public void AtTheTopItsTheFirstScreenfulAndTheExtra() =>
        Assert.Equal(new RowWindow(0, 16), RowWindow.At(1000, 0, View, Pitch, extra: 6));

    [Fact]
    public void AHalfShownRowCountsAtBothEnds()
    {
        // Scrolled 22 into the list: row 0 half shows at the top, row 10 half at the bottom.
        Assert.Equal(new RowWindow(0, 11), RowWindow.At(1000, 22, View, Pitch, extra: 0));
        // Scrolled exactly five rows: rows 5 to 14, nothing of 4 or 15.
        Assert.Equal(new RowWindow(5, 15), RowWindow.At(1000, 220, View, Pitch, extra: 0));
    }

    [Fact]
    public void ItStaysInsideTheList()
    {
        Assert.Equal(new RowWindow(0, 3), RowWindow.At(3, 0, View, Pitch, extra: 6));
        Assert.Equal(new RowWindow(984, 1000), RowWindow.At(1000, 1000 * Pitch - View, View, Pitch, extra: 6));
        Assert.Equal(new RowWindow(0, 0), RowWindow.At(0, 0, View, Pitch, extra: 6));
    }

    [Fact]
    public void AGlideCoversEveryRowItPassesOver()
    {
        // Page down from the top to row 8: rows 0 to 17 show at some point of the glide.
        var glide = RowWindow.Spanning(1000, 0, 8 * Pitch, View, Pitch, extra: 0);
        Assert.Equal(new RowWindow(0, 18), glide);
        Assert.Equal(glide, RowWindow.Spanning(1000, 8 * Pitch, 0, View, Pitch, extra: 0)); // and back up
    }

    [Fact]
    public void CoversIsAboutEveryRow()
    {
        var bound = new RowWindow(10, 30);
        Assert.True(bound.Covers(new RowWindow(10, 30)));
        Assert.True(bound.Covers(new RowWindow(12, 20)));
        Assert.True(bound.Covers(new RowWindow(40, 40))); // nothing needed
        Assert.False(bound.Covers(new RowWindow(9, 20)));
        Assert.False(bound.Covers(new RowWindow(20, 31)));
        Assert.True(bound.Contains(10) && bound.Contains(29) && !bound.Contains(30));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(100, 126)]
    [InlineData(984, 1000)]
    public void ThePaddingKeepsEveryRowInItsPlace(int first, int end)
    {
        const int total = 1000;
        var (above, below) = new RowWindow(first, end).Padding(total, Pitch, Gap);
        int n = end - first;
        double rows = n * (Pitch - Gap) + (n - 1) * Gap; // the bound rows, with the gaps between them
        Assert.Equal(total * Pitch - Gap, above + rows + below); // as tall as the whole list
        Assert.Equal(first * Pitch, above);                      // the first bound row where it belongs
    }

    [Fact]
    public void WithNothingBoundThePaddingIsTheWholeList()
    {
        var (above, below) = new RowWindow(0, 0).Padding(5, Pitch, Gap);
        Assert.Equal(5 * Pitch - Gap, above + below);
    }
}
