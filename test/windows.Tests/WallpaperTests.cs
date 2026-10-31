namespace Omawin.Windows.Tests;

// A picture per monitor (8.8): left to right, repeating when there are more monitors than pictures.
public class WallpaperTests
{
    [Fact]
    public void LeftToRightThenTopToBottom()
    {
        MonitorPlace[] monitors = [new("right", 1920, 0), new("left", -2560, 0), new("middle", 0, 0), new("under", 0, 1080)];
        Assert.Equal([("left", "a"), ("middle", "b"), ("under", "a"), ("right", "b")], WallpaperPlan.Assign(monitors, ["a", "b"]));
    }

    [Fact]
    public void MorePicturesThanMonitorsUsesTheFirst()
    {
        Assert.Equal([("one", "a")], WallpaperPlan.Assign([new("one", 0, 0)], ["a", "b", "c"]));
        Assert.Empty(WallpaperPlan.Assign([new("one", 0, 0)], []));
    }
}
