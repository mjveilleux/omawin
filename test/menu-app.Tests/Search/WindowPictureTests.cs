namespace SystemMenu.Tests.Search;

// Where One Search puts a window's live picture (roadmap 8.5): the preview's box in pixels, and
// the window fitted inside it.
public class WindowPictureTests
{
    [Fact]
    public void TheBoxIsScaledToPixelsAndKeptInsideItsInset()
    {
        // 360 × 230 DIPs at (20, 100), 150%, 6 in from each side.
        var box = WindowPicture.ToPixels(20, 100, 360, 230, 1.5, inset: 6);
        Assert.Equal(new PixelBox(39, 159, 561, 486), box);
    }

    [Fact]
    public void FractionsRoundInwardsSoThePictureNeverSpillsOver()
    {
        var box = WindowPicture.ToPixels(10.5, 10.5, 100.25, 50.25, 1.25);
        Assert.Equal(new PixelBox(14, 14, 138, 75), box);
    }

    [Fact]
    public void ABoxWithNoRoomIsEmpty()
    {
        Assert.True(WindowPicture.ToPixels(0, 0, 10, 10, 1, inset: 6).Empty);
        Assert.True(WindowPicture.ToPixels(0, 0, 0, 100, 2).Empty);
    }

    [Fact]
    public void AWideWindowFillsTheWidthCentredTopToBottom()
    {
        var at = WindowPicture.Fit(3840, 2160, new PixelBox(0, 0, 400, 300));
        Assert.Equal(new PixelBox(0, 37, 400, 262), at);
    }

    [Fact]
    public void ATallWindowFillsTheHeightCentredSideToSide()
    {
        var at = WindowPicture.Fit(1000, 2000, new PixelBox(100, 100, 500, 400));
        Assert.Equal(new PixelBox(225, 100, 375, 400), at);
    }

    [Fact]
    public void ASmallWindowIsNeverBlownUp()
    {
        var at = WindowPicture.Fit(200, 100, new PixelBox(0, 0, 400, 300));
        Assert.Equal(new PixelBox(100, 100, 300, 200), at);
    }

    [Fact]
    public void AWindowWithNoSizeHasNoPicture()
    {
        Assert.True(WindowPicture.Fit(0, 0, new PixelBox(0, 0, 400, 300)).Empty);
        Assert.True(WindowPicture.Fit(800, 600, default).Empty);
    }
}
