namespace SystemMenu.Tests.Settings;

public class BuiltinRulesTests
{
    // The keys omawin's GlazeWM config binds as plain Win+<key>: Win+L never (Windows keeps it),
    // Win+Shift+S and Win+Enter not (DisabledHotkeys can't single them out), comments skipped.
    [Fact]
    public void HotkeyLettersAreThePlainWinBindings()
    {
        string[] config =
        [
            "    bindings: ['lwin+h']",
            "    bindings: ['lwin+shift+s', 'lwin+enter', 'alt+f']",
            "    bindings: ['lwin+f', 'lwin+shift+f']",
            "    # bindings: ['lwin+q']",
            "    bindings: [\"RWIN+2\", 'lwin+1', 'lwin+l']",
            "  - commands: ['lwin+z']",
        ];
        Assert.Equal("FH12", WinHotkeys.Letters(config));
    }

    // The shipped config: if a rebinding drops one of these, the switch stops covering that key.
    [Fact]
    public void ShippedConfigsWinKeys()
    {
        var file = Path.Combine(CatalogTests.RepoRoot(), "config", "glazewm", "config.yaml");
        var letters = WinHotkeys.Letters(File.ReadAllLines(file));
        Assert.Contains('H', letters); // dictation, over Windows' voice typing
        Assert.Contains('A', letters); // the audio page, over Quick settings
        Assert.Contains('1', letters); // workspaces, over the taskbar's apps
        Assert.DoesNotContain('L', letters);
        Assert.DoesNotContain('V', letters); // Win+V stays Windows' clipboard
    }

    [Fact]
    public void DisablingKeepsWhatWasThereAndUndoingRemovesOnlyOurs()
    {
        var on = WinHotkeys.Disable("qa", "AFH");
        Assert.Equal("QAFH", on);
        Assert.True(WinHotkeys.AllDisabled(on, "AFH"));
        Assert.False(WinHotkeys.AllDisabled("QA", "AFH"));
        // A was there before omawin: it stays.
        Assert.Equal("QA", WinHotkeys.Restore(on, "AFH", "qa"));
        // Nothing before: the value goes.
        Assert.Null(WinHotkeys.Restore(WinHotkeys.Disable(null, "AFH"), "AFH", null));
    }

    [Fact]
    public void StartupApprovedBytes()
    {
        Assert.True(StartupApproved.IsEnabled(null));
        Assert.True(StartupApproved.IsEnabled(StartupApproved.Enabled()));
        Assert.True(StartupApproved.IsEnabled([6, 0, 0, 0]));
        var off = StartupApproved.Disabled(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(StartupApproved.IsEnabled(off));
        Assert.Equal(12, off.Length);
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.False(StartupApproved.IsEnabled([7]));
    }

    [Fact]
    public void StartupEntriesMatchByName()
    {
        string[] run = ["OneDrive", "com.squirrel.Teams.Teams", "Discord", "DiscordPTB"];
        Assert.Equal("OneDrive", StartupApproved.RunName(run, "onedrive"));
        Assert.Equal("Discord", StartupApproved.RunName(run, "Discord")); // exact beats the prefix
        Assert.Null(StartupApproved.RunName(run, "MSTeams"));
        Assert.Null(StartupApproved.RunName(["DiscordA", "DiscordB"], "Discord")); // ambiguous
        Assert.True(StartupApproved.IsPackageOf("MSTeams_8wekyb3d8bbwe", "MSTeams"));
        Assert.False(StartupApproved.IsPackageOf("MSTeamsBeta_8wekyb3d8bbwe", "MSTeams"));
        Assert.Equal([true, true, false, false, false], new[] { 2, 4, 0, 1, 3 }.Select(s => StartupApproved.TaskEnabled(s) == true));
    }

    [Fact]
    public void LayoutHotkeyOffFreesOnlyAltShift()
    {
        var defaults = new Dictionary<string, string?>();          // nothing set: Windows' defaults
        Assert.Equal("on", LayoutHotkeys.Read(defaults));
        var off = LayoutHotkeys.Off(defaults);
        Assert.Equal(new Dictionary<string, string> { ["Hotkey"] = "3", ["Language Hotkey"] = "3" }, off);
        var after = new Dictionary<string, string?>(off.Select(p => KeyValuePair.Create(p.Key, (string?)p.Value)));
        Assert.Equal("off", LayoutHotkeys.Read(after));            // Ctrl+Shift for layouts stays

        // Ctrl+Shift already for languages, Alt+Shift for layouts: only the layout one changes.
        var mixed = new Dictionary<string, string?> { ["Hotkey"] = "2", ["Language Hotkey"] = "2", ["Layout Hotkey"] = "1" };
        Assert.Equal(new Dictionary<string, string> { ["Layout Hotkey"] = "3" }, LayoutHotkeys.Off(mixed));
    }

    [Fact]
    public void LayoutHotkeyOnPutsBackWhatWasRemembered()
    {
        var before = new Dictionary<string, string?> { ["Hotkey"] = "1", ["Language Hotkey"] = null, ["Layout Hotkey"] = "2" };
        var back = LayoutHotkeys.On(LayoutHotkeys.Load(LayoutHotkeys.Save(before)));
        Assert.Equal("1", back["Hotkey"]);
        Assert.Null(back["Language Hotkey"]);                     // wasn't set: deleted again
        Assert.Equal("2", back["Layout Hotkey"]);
        Assert.Equal("1", LayoutHotkeys.On(null)["Language Hotkey"]); // nothing remembered: the default
    }

    static readonly SettingEntry Indexer = Catalog.ParseLine(
        "system.search-indexer | - | Indexer | wsearch | switch | builtin service WSearch on=delayed off=disabled | admin", "t:1");

    [Fact]
    public void ServiceStartModes()
    {
        Assert.Equal("delayed", ServiceStart.Mode(2, 1));
        Assert.Equal("auto", ServiceStart.Mode(2, null));
        Assert.Equal("disabled", ServiceStart.Mode(4, null));
        Assert.Null(ServiceStart.Mode(null, null));
        Assert.Equal("off", ServiceStart.Switch(Indexer, "disabled"));
        Assert.Equal("on", ServiceStart.Switch(Indexer, "manual"));
        Assert.Equal(("delayed", "disabled"), (ServiceStart.ModeFor(Indexer, "on"), ServiceStart.ModeFor(Indexer, "off")));
    }

    [Fact]
    public void DevRuntimesSkipLaunchersAndKeepEachProgramOnce()
    {
        var picked = DevRuntimes.Pick(
        [
            @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\python.exe",  // the Store's alias
            @"C:\Users\u\AppData\Local\mise\shims\node.exe",               // a shim
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Users\u\.bun\bin\bun.exe",
            @"C:\tools\rg.exe",
            @"C:\Users\u\AppData\Local\mise\installs\node\22.1.0\node.exe",
        ]);
        Assert.Equal([("node", @"C:\Program Files\nodejs\node.exe"), ("node", @"C:\Users\u\AppData\Local\mise\installs\node\22.1.0\node.exe"),
            ("bun", @"C:\Users\u\.bun\bin\bun.exe")], picked);
        var value = DevRuntimes.Encode(picked);
        Assert.Equal(picked, DevRuntimes.Decode(value));
        Assert.Equal("node (2 versions), bun", DevRuntimes.Describe(value));
    }

    [Fact]
    public void AdminPlanLines()
    {
        Assert.Equal("system.search-indexer\tbuiltin\tservice\tWSearch\tdisabled", SettingAdminQueue.PlanLine(Indexer, "off"));
        var policy = Catalog.ParseLine(@"look.ai-recall | - | Recall | ai | switch | reg HKCU\Software\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis dword on=- off=1 | admin", "t:2");
        Assert.Equal("look.ai-recall\treg\tHKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsAI\\DisableAIDataAnalysis\tdword\t1", SettingAdminQueue.PlanLine(policy, "off"));
        // Anywhere else in HKCU an admin line has no path: the script would refuse it anyway.
        var other = Catalog.ParseLine(@"look.x | - | X | x | switch | reg HKCU\Software\X\Y dword on=1 off=0 | admin", "t:3");
        Assert.Null(SettingAdminQueue.PlanLine(other, "on"));
    }

    // Every admin line in the shipped catalog reaches the elevated script; one that can't would
    // be queued, then dropped at the prompt with "nothing queued has an admin path".
    [Fact]
    public void EveryShippedAdminLineHasAnAdminPath()
    {
        var file = Path.Combine(CatalogTests.RepoRoot(), "default", "launcher", "settings.txt");
        var (entries, _) = Catalog.Parse([(file, File.ReadAllLines(file))]);
        var sample = (SettingEntry e) => e.Kind switch
        {
            SettingKind.Switch => "on",
            SettingKind.Choice => e.Choices.Count > 0 ? e.Choices[0].Value : "x",
            SettingKind.Action when e.How.Target == "firewall-dev" => @"node=C:\nodejs\node.exe",
            _ => "",
        };
        Assert.Empty(entries.Where(e => e.Admin && SettingAdminQueue.PlanLine(e, sample(e)) == null).Select(e => e.Id));
    }

    [Fact]
    public async Task AnAdminActionIsQueuedWithWhatWasFoundHere()
    {
        using var dir = new TempDir();
        var entries = new List<SettingEntry>
        {
            Catalog.ParseLine("developer.firewall-dev | - | Dev servers on private networks | firewall | action | builtin firewall-dev | admin", "t:1"),
        };
        var windows = new FakeWindows();
        var store = new SettingStore(() => entries, _ => windows,
            new SettingHistory(dir.File("h.tsv")), new SettingAdminQueue(dir.File("q.tsv")));
        string found = "";
        store.ResolveAdmin = (e, v) => found.Length == 0 ? (null, "none found") : (found, null);

        var none = store.Toggle(entries[0]);
        Assert.False(none.Ok);
        Assert.Empty(store.AdminQueue.Items());

        found = @"node=C:\nodejs\node.exe|bun=C:\bun\bun.exe";
        var r = store.Toggle(entries[0]);
        Assert.True(r.Queued);
        Assert.Contains("(node, bun)", r.Message);
        Assert.Equal(["Dev servers on private networks: node, bun"], store.DescribeAdmin());

        IReadOnlyList<string>? plan = null;
        store.RunAdmin = lines => { plan = lines; return Task.FromResult(new AdminRun(true, [])); };
        Assert.True((await store.ApplyAdminAsync()).Ok);
        Assert.Equal(["developer.firewall-dev\tbuiltin\tfirewall-dev\t\t" + found], plan);
        Assert.Empty(windows.Writes); // the elevated run did it, not this side
    }
}
