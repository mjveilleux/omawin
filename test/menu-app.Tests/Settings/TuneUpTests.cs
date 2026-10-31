namespace SystemMenu.Tests.Settings;

public class TuneUpTests
{
    static readonly SettingEntry[] Catalog_ =
    [
        Catalog.ParseLine(@"system.shake | - | Shake | aero | switch | reg HKCU\A\DisallowShaking dword on=0 off=1 |", "t:1"),
        Catalog.ParseLine(@"look.menu-delay | - | Menu delay | menu | choice | spi menu-delay values=""400:Windows default,100:Fast,0:Instant"" |", "t:2"),
        Catalog.ParseLine(@"system.widgets | - | Widgets | news | switch | reg HKLM\A\AllowNewsAndInterests dword on=- off=0 | admin", "t:3"),
        Catalog.ParseLine(@"system.snap-bar | - | Snap bar | snap | switch | reg HKCU\A\EnableSnapBar dword on=1 off=0 | check", "t:4"),
        Catalog.ParseLine(@"input.pointer-speed | - | Pointer speed | mouse | slider | spi pointer-speed range=1..20 |", "t:5"),
        Catalog.ParseLine(@"developer.restart-explorer | - | Restart Explorer | explorer | action | builtin restart-explorer |", "t:6"),
    ];

    static List<Recommendation> Recs(params string[] lines) =>
        lines.Select((l, i) => TuneUp.ParseLine(l, $"r:{i + 1}")).ToList();

    [Fact]
    public void ParsesALineAndKeepsPipesInWhy()
    {
        var r = TuneUp.ParseLine("system.shake | off | auto | Shaking minimizes | everything else.", "r:1");
        Assert.Equal(("system.shake", "off", TuneUpWhen.Auto), (r.Id, r.Value, r.When));
        Assert.Equal("Shaking minimizes | everything else.", r.Why);
    }

    [Theory]
    [InlineData("system.shake | off | auto", "4")]               // no why
    [InlineData("system.shake | off | always | because", "when")] // unknown when
    [InlineData("system.shake | off | auto |  ", "why")]          // empty why
    public void RejectsBadLines(string line, string because) =>
        Assert.Contains(because, Assert.Throws<FormatException>(() => TuneUp.ParseLine(line, "r:1")).Message);

    // A value the setting can't take, a setting that isn't there, and admin set without asking
    // would each reach the PC as a failed or silent change; the check stops them at the file.
    [Fact]
    public void ChecksAgainstTheCatalog()
    {
        var problems = TuneUp.Check(Recs(
            "system.shake | maybe | auto | x",
            "look.menu-delay | 250 | auto | x",
            "system.widgets | off | auto | x",
            "input.pointer-speed | 40 | ask | x",
            "developer.restart-explorer | on | ask | x",
            "nope.missing | off | ask | x",
            "look.menu-delay | 100 | auto | fine",
            "system.widgets | off | ask | fine"), Catalog_);
        Assert.Equal(6, problems.Count);
        Assert.Contains(problems, p => p.Message.Contains("on or off"));
        Assert.Contains(problems, p => p.Message.Contains("isn't one of 400, 100, 0"));
        Assert.Contains(problems, p => p.Message.Contains("only be 'ask'"));
        Assert.Contains(problems, p => p.Message.Contains("1..20"));
        Assert.Contains(problems, p => p.Message.Contains("no value"));
        Assert.Contains(problems, p => p.Message.Contains("no setting 'nope.missing'"));
    }

    [Fact]
    public void ReportsWhereEachStands()
    {
        var recs = Recs(
            "system.shake | off | auto | a",
            "look.menu-delay | 100 | auto | b",
            "system.widgets | off | ask | c",
            "system.snap-bar | off | auto | d");
        var now = new Dictionary<string, string?> { ["system.shake"] = "off", ["look.menu-delay"] = "400", ["system.widgets"] = null };
        var rows = TuneUp.Report(recs, Catalog_, e => e.Id == "system.widgets" ? throw new InvalidOperationException() : now.GetValueOrDefault(e.Id), 26100);

        Assert.Equal([TuneUpState.Applied, TuneUpState.NotApplied, TuneUpState.Unreadable, TuneUpState.Hidden], rows.Select(r => r.State));
        Assert.Equal(["look.menu-delay"], TuneUp.ToApply(rows, autoOnly: true).Select(r => r.Rec.Id));

        var text = TuneUp.Format(rows);
        Assert.Contains("1 of 3 applied", text);
        Assert.Contains("Menu delay: Fast (now Windows default)", text);
        Assert.Contains("1 more wait to be checked", text);
        Assert.DoesNotContain("      a", text); // an applied one doesn't repeat why
    }

    // The shipped list parses and every line fits the shipped catalog: a typo there would
    // otherwise only show as a recommendation that never applies on someone's PC.
    [Fact]
    public void ShippedTuneUpFitsTheShippedCatalog()
    {
        var dir = Path.Combine(CatalogTests.RepoRoot(), "default", "launcher");
        var (entries, catalogProblems) = Catalog.Parse([(Path.Combine(dir, "settings.txt"), File.ReadAllLines(Path.Combine(dir, "settings.txt")))]);
        Assert.Empty(catalogProblems);
        var file = Path.Combine(dir, "tune-up.txt");
        var (recs, problems) = TuneUp.Parse([(file, File.ReadAllLines(file))]);
        Assert.Empty(problems);
        Assert.NotEmpty(recs);
        Assert.Empty(TuneUp.Check(recs, entries).Select(p => $"{p.Origin}: {p.Message}"));
    }
}
