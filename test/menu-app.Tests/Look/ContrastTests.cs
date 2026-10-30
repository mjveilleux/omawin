using System.Text.Json.Nodes;

namespace SystemMenu.Tests.Look;

public class ContrastTests
{
    [Theory]
    [InlineData("#000000", "#ffffff", 21.0)]
    [InlineData("#ffffff", "#ffffff", 1.0)]
    [InlineData("#777777", "#ffffff", 4.48)]   // the classic just-fails grey
    [InlineData("#ff1e66f5", "#eff1f5", 4.34)] // alpha ignored: Catppuccin Latte's accent
    public void RatiosMatchWcag(string a, string b, double ratio)
    {
        Assert.Equal(ratio, Contrast.Ratio(a, b), 2);
        Assert.Equal(ratio, Contrast.Ratio(b, a), 2);
    }

    [Fact]
    public void ReadableLeavesGoodColoursAlone() =>
        Assert.Equal("#c0caf5", Contrast.Readable("#c0caf5", "#1a1b26", "#c0caf5"));

    [Theory]
    [InlineData("#859289", "#2d353b", "#d3c6aa")] // Everforest's comment (1.7:1), dark
    [InlineData("#1e66f5", "#eff1f5", "#4c4f69")] // Latte's accent, light
    [InlineData("#2d353b", "#2d353b", "#d3c6aa")] // the background itself
    [InlineData("#000000", "#2d353b", "#d3c6aa")] // darker than a dark background: the ratio dips before it rises
    public void ReadableReachesTextContrastWithTheFewestChanges(string color, string bg, string fg)
    {
        var fixedColor = Contrast.Readable(color, bg, fg);
        Assert.True(Contrast.Ratio(fixedColor, bg) >= Contrast.Text, $"{fixedColor} on {bg}: {Contrast.Ratio(fixedColor, bg):0.00}");
        Assert.NotEqual(fg, fixedColor); // a mix, not just the foreground
    }

    [Fact]
    public void BadColoursAreAnErrorNotAGuess()
    {
        Assert.Throws<FormatException>(() => Contrast.Parse("teal"));
        Assert.Throws<FormatException>(() => Contrast.Parse("#12345"));
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "themes", "colors"))) return d.FullName;
        throw new InvalidOperationException("themes/ not found");
    }

    public static TheoryData<string> Themes()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "themes"), "*.json").Order()) data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    // Every shipped theme (themes/<name>.json, what the menu app draws with): its text colours
    // are readable on the background and on a selected row, its accent visible as a graphic,
    // and Readable() makes its faint colours (comment, a pale accent) readable as text.
    [Theory]
    [MemberData(nameof(Themes))]
    public void ShippedThemesAreReadable(string theme)
    {
        var t = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "themes", theme + ".json")))!;
        string C(string key) => t[key]!.GetValue<string>();
        foreach (var (text, bg) in new[] { ("foreground", "background"), ("muted", "background"), ("foreground", "selection"), ("muted", "selection") })
            Assert.True(Contrast.Ratio(C(text), C(bg)) >= Contrast.Text, $"{theme}: {text} on {bg} is {Contrast.Ratio(C(text), C(bg)):0.00}:1");
        Assert.True(Contrast.Ratio(C("accent"), C("background")) >= Contrast.Graphic, $"{theme}: accent is {Contrast.Ratio(C("accent"), C("background")):0.00}:1");
        foreach (var faint in new[] { "comment", "accent", "red", "green", "yellow" })
            Assert.True(Contrast.Ratio(Contrast.Readable(C(faint), C("background"), C("foreground")), C("background")) >= Contrast.Text, $"{theme}: {faint}");
    }
}
