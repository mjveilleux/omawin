namespace SystemMenu.Tests.Settings;

public class CommandsTests
{
    [Fact]
    public void SetThenGetThenUndo()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        Assert.Equal(0, SettingCommands.Setting(store, ["set", "explorer.open-to", "This", "PC"]).Code);
        Assert.Equal("1\tThis PC", SettingCommands.Setting(store, ["get", "explorer.open-to"]).Text);
        Assert.Equal(0, SettingCommands.Setting(store, ["undo"]).Code);
        Assert.Equal("2\tHome", SettingCommands.Setting(store, ["get", "explorer.open-to"]).Text);
    }

    [Fact]
    public void MistakesSayWhatWouldWork()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        Assert.Contains("one of 1 (This PC)", SettingCommands.Setting(store, ["set", "explorer.open-to", "garage"]).Text);
        Assert.Contains("no setting 'look.nope'", SettingCommands.Setting(store, ["toggle", "look.nope"]).Text);
        Assert.Contains("look, explorer", SettingCommands.Setting(store, ["list", "colours"]).Text);
        Assert.Equal(2, SettingCommands.Setting(store, ["frobnicate"]).Code);
    }

    [Fact]
    public void ListShowsValuesAndMarksAdmin()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        var lines = SettingCommands.Setting(store, ["list", "developer"]).Text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^developer\.long-paths\s+Off\s+Long paths  \[admin\]$", lines[0]);
    }

    [Fact]
    public async Task AdminAsksBeforeThePrompt()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        SettingCommands.Setting(store, ["set", "developer.long-paths", "on"]);
        var ask = await SettingCommands.Settings(store, ["admin"], dir.File("a.txt"), dir.File("s.txt"));
        Assert.Equal(3, ask.Code);
        Assert.Contains("Long paths: On", ask.Text);
    }

    [Fact]
    public async Task SaveKeepsTheFileAndAddsWhatChanged()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        var file = dir.File("settings.txt");
        File.WriteAllLines(file, ["# mine", "explorer.open-to = 1", "someone.else = kept"]);
        SettingCommands.Setting(store, ["toggle", "look.dark-apps"]);
        var r = await SettingCommands.Settings(store, ["save"], file, dir.File("shared.txt"));
        Assert.Equal(0, r.Code);
        Assert.Equal(["# mine", "explorer.open-to = 2", "someone.else = kept", "look.dark-apps = on"], File.ReadAllLines(file));
    }

    [Fact]
    public async Task ApplySetsTheAccountsValuesOverTheSharedOnes()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        File.WriteAllLines(dir.File("shared.txt"), ["look.dark-apps = on", "explorer.open-to = 3", "look.retired = on"]);
        File.WriteAllLines(dir.File("account.txt"), ["explorer.open-to = 1"]);
        var r = await SettingCommands.Settings(store, ["apply", "-NoAdmin"], dir.File("account.txt"), dir.File("shared.txt"));
        Assert.Equal(("on", "1"), (windows.Values["look.dark-apps"], windows.Values["explorer.open-to"]));
        Assert.Contains("skipped look.retired", r.Text);
        Assert.Equal(["apply", "apply"], store.History.Changes().Select(c => c.Source));
    }

    // An update: only what settings.txt changed since the last apply; a setting changed on this
    // PC since stays as you left it; admin ones wait in the queue, never prompting.
    [Fact]
    public async Task AnUpdateAppliesOnlyWhatChanged()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        string account = dir.File("account.txt"), shared = dir.File("shared.txt"), applied = dir.File("applied.txt");
        File.WriteAllLines(account, ["look.dark-apps = on", "explorer.open-to = 3"]);
        await SettingCommands.Settings(store, ["apply", "-NoAdmin"], account, shared, applied);
        SettingCommands.Setting(store, ["set", "look.dark-apps", "off"]); // changed here, by hand

        var same = await SettingCommands.Settings(store, ["apply", "-NoAdmin", "-Changed"], account, shared, applied);
        Assert.Contains("hasn't changed", same.Text);
        Assert.Equal("off", windows.Values["look.dark-apps"]);

        File.WriteAllLines(account, ["look.dark-apps = on", "explorer.open-to = 1", "developer.long-paths = on"]);
        var r = await SettingCommands.Settings(store, ["apply", "-NoAdmin", "-Changed"], account, shared, applied);
        Assert.Equal(0, r.Code);
        Assert.Equal(("off", "1"), (windows.Values["look.dark-apps"], windows.Values["explorer.open-to"]));
        Assert.Equal([("developer.long-paths", "on")], store.AdminQueue.Items());
        Assert.Contains("hasn't changed", (await SettingCommands.Settings(store, ["apply", "-NoAdmin", "-Changed"], account, shared, applied)).Text);
    }

    [Fact]
    public void ChangedComparesIdsAndValuesIgnoringCase()
    {
        var wanted = new List<(string, string)> { ("a.one", "On"), ("a.two", "3"), ("a.new", "x") };
        var changed = SettingValueFile.Changed(wanted, [("A.ONE", "on"), ("a.two", "2")]);
        Assert.Equal([("a.two", "3"), ("a.new", "x")], changed);
        Assert.Equal(["a.one = on", "a.two = 3"], SettingValueFile.Record([("a.one", "on"), ("a.two", "2")], [("A.two", "3")]).Skip(1));
    }
}

// omawin tune-up: the recommended settings against the fixture's Windows, through the same store.
public class TuneUpCommandTests
{
    static readonly List<Recommendation> Recs = new[]
    {
        "look.dark-apps | on | auto | dark",
        "explorer.open-to | 2 | auto | already so",
        "developer.long-paths | on | ask | needs admin",
        "input.pointer-speed | 15 | ask | faster",
        "look.unverified | off | auto | not checked yet",
    }.Select((l, i) => TuneUp.ParseLine(l, $"tune-up.txt:{i + 1}")).ToList();

    // The fixture's catalog plus a "check" line, hidden from the report and never read.
    static List<SettingEntry> Catalog_() =>
        [.. Fixture.Entries(), Catalog.ParseLine(@"look.unverified | - | Unverified | x | switch | reg HKCU\A\X dword on=1 off=0 | check", "t:1")];

    [Fact]
    public async Task ReportSaysWhatsAppliedAndWhatsWaiting()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        var r = await SettingCommands.TuneUp(store, [], Recs, Catalog_(), 26100);
        Assert.Equal(0, r.Code);
        Assert.Contains("1 of 4 applied", r.Text);
        Assert.Contains("--  Dark mode for apps: on (now off)", r.Text);
        Assert.Contains("1 more wait to be checked", r.Text);
        Assert.Empty(windows.Writes);
    }

    [Fact]
    public async Task ApplyListsFirstThenSetsAndRunsTheOnePrompt()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        List<string>? plan = null;
        store.RunAdmin = p => { plan = [.. p]; return Task.FromResult(new AdminRun(true, [])); };

        var ask = await SettingCommands.TuneUp(store, ["apply"], Recs, Catalog_(), 26100);
        Assert.Equal(3, ask.Code);
        Assert.Contains("sets 3 Windows settings (1 in one admin prompt)", ask.Text);
        Assert.Contains("Long paths: On [admin]", ask.Text);
        Assert.Empty(windows.Writes); // nothing before the yes

        var yes = await SettingCommands.TuneUp(store, ["apply", "--yes"], Recs, Catalog_(), 26100);
        Assert.Equal(0, yes.Code);
        Assert.Equal(["look.dark-apps=on", "input.pointer-speed=15"], windows.Writes);
        Assert.Equal("developer.long-paths", Assert.Single(plan!).Split('\t')[0]);
        Assert.All(store.History.Changes(), c => Assert.Equal("tune-up", c.Source));
        Assert.Equal(0, store.AdminQueue.Count);
    }

    [Fact]
    public async Task AutoSetsOnlyTheAutoOnesWithoutAsking()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        var r = await SettingCommands.TuneUp(store, ["apply", "-Auto"], Recs, Catalog_(), 26100);
        Assert.Equal(0, r.Code);
        Assert.Equal(["look.dark-apps=on"], windows.Writes);
        Assert.Equal(0, store.AdminQueue.Count);
    }

    [Fact]
    public async Task NothingLeftAndMistakes()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        var done = await SettingCommands.TuneUp(store, ["apply"], [Recs[1]], Catalog_(), 26100);
        Assert.Equal(0, done.Code);
        Assert.Contains("already applied", done.Text);
        Assert.Equal(2, (await SettingCommands.TuneUp(store, ["frobnicate"], Recs, Catalog_(), 26100)).Code);
    }
}

public class ValueFileTests
{
    [Theory]
    [InlineData("explorer.hidden = on", "explorer.hidden", "on")]
    [InlineData("  sound.output=Speakers (Realtek)  ", "sound.output", "Speakers (Realtek)")]
    public void ReadsValueLines(string line, string id, string value) => Assert.Equal((id, value), SettingValueFile.ParseLine(line));

    [Theory]
    [InlineData("# look.dark-apps = on")]
    [InlineData("= on")]
    [InlineData("two words = on")]
    [InlineData("")]
    public void IgnoresWhatIsntOne(string line) => Assert.Null(SettingValueFile.ParseLine(line));

    [Fact]
    public void ANewFileStartsWithItsHeader()
    {
        var saved = SettingValueFile.Merge(null, ["look.dark-apps"], id => (id, "on"));
        Assert.StartsWith("# Windows settings", saved.Lines[0]);
        Assert.Equal("look.dark-apps = on", saved.Lines[^1]);
        Assert.Equal((1, 1), (saved.Count, saved.Added));
    }
}
