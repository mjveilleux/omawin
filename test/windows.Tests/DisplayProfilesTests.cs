namespace Omawin.Windows.Tests;

// Monitor profiles (8.7): the file, a set of monitors found whatever its order, nothing to do when
// it's already as saved.
public class DisplayProfilesTests
{
    static readonly DisplayPlace Laptop = new("BOE0A2B#4&1a2b&0&UID8388688", "Built-in screen", 1920, 1200, 2560, 240, 0);
    static readonly DisplayPlace Dell = new("DELA0F3#5&2b1f&0&UID4352", "DELL U2723QE", 2560, 1440, 0, 0, 0);

    [Fact]
    public void IdIsTheModelAndInstance() =>
        Assert.Equal("DELA0F3#5&2b1f&0&UID4352", DisplayProfiles.IdFromPath(@"\\?\DISPLAY#DELA0F3#5&2b1f&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));

    [Fact]
    public void SavedLayoutsReadBack()
    {
        var lines = DisplayProfiles.Save(null, [Laptop, Dell]);
        Assert.StartsWith("# omawin display", lines[0]);
        Assert.Contains("DELA0F3#5&2b1f&0&UID4352 | DELL U2723QE | 2560 x 1440 | 0,0 | 0", lines);
        var read = DisplayProfiles.Parse(lines);
        var layout = DisplayProfiles.For(read, [Dell.Id, Laptop.Id])!; // any order
        Assert.Equal(2, layout.Count);
        Assert.True(DisplayProfiles.Same(layout, [Laptop, Dell]));
        Assert.Null(DisplayProfiles.For(read, [Laptop.Id]));
    }

    [Fact]
    public void SavingAgainReplacesOnlyThatSet()
    {
        var lines = DisplayProfiles.Save(null, [Laptop]);
        lines = DisplayProfiles.Save([.. lines, "# my note"], [Laptop, Dell]);
        var moved = Dell with { X = -2560 };
        var laptopAt0 = Laptop with { X = 0, Y = 0 };
        lines = DisplayProfiles.Save([.. lines], [laptopAt0, moved]);
        var read = DisplayProfiles.Parse(lines);
        Assert.Equal(2, read.Count);
        Assert.Contains("# my note", lines);
        Assert.Equal(-2560, DisplayProfiles.For(read, [Laptop.Id, Dell.Id])!.Single(d => d.Id == Dell.Id).X);
        var forgotten = DisplayProfiles.Remove(lines, DisplayProfiles.Key([Laptop.Id, Dell.Id]));
        Assert.Single(DisplayProfiles.Parse(forgotten));
    }

    [Fact]
    public void DifferentLayoutsAreNotTheSame()
    {
        Assert.False(DisplayProfiles.Same([Laptop, Dell], [Laptop, Dell with { Width = 3840, Height = 2160 }]));
        Assert.False(DisplayProfiles.Same([Laptop, Dell], [Laptop]));
        Assert.False(DisplayProfiles.Same([Laptop, Dell], [Laptop with { Rotation = 90 }, Dell]));
    }

    [Fact]
    public void BadLinesAreSkippedAndAMainMonitorIsNeeded()
    {
        var read = DisplayProfiles.Parse(["[a + b]", "a | A | 10 x 10 | 0,0 | 45", "b | B | wide | 0,0 | 0", "c | C | 10 x 10 | 0,0 | 90"]);
        Assert.Equal(["c"], read["a + b"].Select(d => d.Id));
        Assert.NotNull(DisplayProfiles.Problem([Laptop]));
        Assert.Null(DisplayProfiles.Problem([Laptop, Dell]));
        Assert.Equal("DELL U2723QE: 2560 × 1440 at 0,0 (main)", DisplayProfiles.Describe(Dell));
    }
}
