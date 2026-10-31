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
