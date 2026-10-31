namespace SystemMenu.Tests.Look;

// Icons are fetched for the pixels they're drawn at and only ever made smaller (roadmap 1.8).
public class IconScaleTests
{
    [Theory]
    [InlineData(26, 1.0, 26)]
    [InlineData(26, 1.5, 39)]
    [InlineData(46, 2.0, 92)]
    [InlineData(double.NaN, 1.5, 48)]   // not laid out yet
    public void PixelsAreUnitsTimesTheScale(double units, double scale, int pixels) =>
        Assert.Equal(pixels, IconScale.Pixels(units, scale));

    [Theory]
    [InlineData(39, 40)]
    [InlineData(48, 48)]
    [InlineData(92, 96)]
    [InlineData(300, 256)]
    public void TheRequestIsTheNextStandardSizeUp(int pixels, int request) =>
        Assert.Equal(request, IconScale.Request(pixels));

    static byte[] Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var p = new byte[w * h * 4];
        for (int i = 0; i < p.Length; i += 4) { p[i] = b; p[i + 1] = g; p[i + 2] = r; p[i + 3] = a; }
        return p;
    }

    [Fact]
    public void ASmallerIconIsNeverStretched()
    {
        var p = Solid(32, 32, 1, 2, 3, 255);
        var (same, w, h) = IconScale.Fit(p, 32, 32, 39);
        Assert.Same(p, same);
        Assert.Equal((32, 32), (w, h));
    }

    [Fact]
    public void ABiggerOneIsShrunkToTheExactPixelsKeepingItsShape()
    {
        var (_, w, h) = IconScale.Fit(Solid(48, 48, 0, 0, 0, 255), 48, 48, 39);
        Assert.Equal((39, 39), (w, h));
        var (_, ww, wh) = IconScale.Fit(Solid(256, 128, 0, 0, 0, 255), 256, 128, 64);
        Assert.Equal((64, 32), (ww, wh));
    }

    [Fact]
    public void ShrinkingAveragesWhatEachPixelCovers()
    {
        // A 2x2 checkerboard of black and white halves to one mid-grey pixel; a solid colour stays itself.
        var board = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255 };
        var (p, _, _) = IconScale.Fit(board, 2, 2, 1);
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, p);
        var (solid, _, _) = IconScale.Fit(Solid(48, 48, 10, 20, 30, 200), 48, 48, 39);
        Assert.All(Enumerable.Range(0, solid.Length / 4), i => Assert.Equal(new byte[] { 10, 20, 30, 200 }, solid[(i * 4)..(i * 4 + 4)]));
    }
}
