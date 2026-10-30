namespace SystemMenu.Tests.Settings;

public class CatalogTests
{
    const string Dark = @"look.dark-apps | - | Dark mode for apps | dark | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme dword on=0 off=1 broadcast=ImmersiveColorSet |";

    [Fact]
    public void ParsesARegSwitch()
    {
        var e = Catalog.ParseLine(Dark, "t:1");
        Assert.Equal("look", e.Area);
        Assert.Equal(SettingKind.Switch, e.Kind);
        Assert.Equal("reg", e.How.Verb);
        Assert.Equal(("0", "1"), (e.How.On, e.How.Off));
        Assert.Equal("ImmersiveColorSet", e.How.Broadcast);
        Assert.Equal(("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme"), Catalog.SplitReg(e.How.Target));
    }

    [Fact]
    public void QuotesGroupPathsAndValuesWithSpaces()
    {
        var e = Catalog.ParseLine(@"explorer.open-to | - | Opens to | home | choice | reg HKCU\A\B\LaunchTo dword values=""1:This PC,2:Home"" |", "t:1");
        Assert.Equal(["1", "2"], e.Choices.Select(c => c.Value));
        Assert.Equal("This PC", e.Choices[0].Label);
        var f = Catalog.ParseLine(@"system.fast-startup | - | Fast startup | boot | switch | reg ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled"" dword on=1 off=0 | admin restart", "t:2");
        Assert.Equal("HiberbootEnabled", Catalog.SplitReg(f.How.Target).Value);
        Assert.True(f.Admin);
        Assert.Equal(Takes.Restart, f.Takes);
    }

    [Theory]
    [InlineData(@"nope.x | - | X | x | switch | reg HKCU\A\B dword on=1 off=0 |", "area")]                // unknown area
    [InlineData(@"look.x | - | X | x | switch | reg HKCU\A\B dword on=1 |", "on= and off=")]              // half a switch
    [InlineData(@"look.x | - | X | x | switch | reg HKLM\A\B dword on=1 off=0 |", "admin")]               // HKLM without admin
    [InlineData(@"look.x | - | X | x | slider | spi pointer-speed |", "range")]                           // slider without range
    [InlineData(@"look.x | - | X | x | switch | reg HKCU\A\B on=1 off=0 |", "type")]                      // reg without a type
    [InlineData(@"look.x | - | X | x | switch | reg ""HKCU\A\B dword on=1 off=0 |", "quote")]             // unclosed quote
    public void RejectsBadLines(string line, string because) =>
        Assert.Contains(because, Assert.Throws<FormatException>(() => Catalog.ParseLine(line, "t:1")).Message);

    [Fact]
    public void LaterLayersReplaceByIdAndKeepOrder()
    {
        var (entries, problems) = Catalog.Parse([
            ("repo", [Dark, @"look.transparency | - | T | t | switch | reg HKCU\A\EnableTransparency dword on=1 off=0 |"]),
            ("account", [Dark.Replace("Dark mode for apps", "Mine")]),
        ]);
        Assert.Empty(problems);
        Assert.Equal(["look.dark-apps", "look.transparency"], entries.Select(e => e.Id));
        Assert.Equal("Mine", entries[0].Name);
    }

    [Fact]
    public void BuildRangeHidesEntries()
    {
        var e = Catalog.ParseLine(@"explorer.end-task | - | End task | kill | switch | reg HKCU\A\TaskbarEndTask dword on=1 off=0 | minBuild=22631", "t:1");
        Assert.False(e.FitsBuild(22621));
        Assert.True(e.FitsBuild(26100));
    }

    // The shipped catalog parses with no problems and no duplicate ids: a typo in
    // launcher/settings.txt would otherwise only show up as a missing row on someone's PC.
    [Fact]
    public void ShippedCatalogIsClean()
    {
        var file = Path.Combine(RepoRoot(), "launcher", "settings.txt");
        var (entries, problems) = Catalog.Parse([(file, File.ReadAllLines(file))]);
        Assert.Empty(problems);
        Assert.NotEmpty(entries);
        // Parse merges repeated ids (that's how layers override), so count the raw lines here.
        var ids = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('|')[0].Trim()).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // The repo's root, from this file's place in it (menu-app.Tests/Settings/).
    internal static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
