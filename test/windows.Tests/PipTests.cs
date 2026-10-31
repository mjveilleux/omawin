namespace Omawin.Windows.Tests;

// Picture-in-picture's arithmetic (8.6).
public class PipTests
{
    [Fact]
    public void FitKeepsTheShapeAndCentres()
    {
        // A 16:9 window in a square: bars above and below.
        Assert.Equal(new Box(0, 72, 400, 297), PipPlan.Fit(1920, 1080, new Box(0, 0, 400, 370)));
        // A tall one in a wide one: bars either side.
        Assert.Equal(new Box(150, 0, 250, 200), PipPlan.Fit(500, 1000, new Box(0, 0, 400, 200)));
        Assert.Equal(new Box(0, 0, 10, 10), PipPlan.Fit(0, 0, new Box(0, 0, 10, 10)));
    }

    [Fact]
    public void CornerIsBottomRightOfTheWorkArea()
    {
        var work = new Box(0, 40, 2560, 1440);
        var c = PipPlan.Corner(work, 1920, 1080);
        Assert.Equal(640, c.Width);
        Assert.Equal(360, c.Height);
        Assert.Equal(2560 - 16, c.Right);
        Assert.Equal(1440 - 16, c.Bottom);
        // A small screen still gets 320 px; a very tall source is held to half the height.
        Assert.Equal(320, PipPlan.Corner(new Box(0, 0, 1000, 700), 1, 10).Width);
        Assert.Equal(350, PipPlan.Corner(new Box(0, 0, 1000, 700), 1, 10).Height);
    }

    [Fact]
    public void OpacityStepsStayVisible()
    {
        Assert.Equal(230, PipPlan.Opacity(255, -120));
        Assert.Equal(255, PipPlan.Opacity(250, 120));
        Assert.Equal(60, PipPlan.Opacity(70, -120));
    }
}
