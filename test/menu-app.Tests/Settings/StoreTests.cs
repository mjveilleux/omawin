namespace SystemMenu.Tests.Settings;

public class StoreTests
{
    [Fact]
    public void AChangeIsWrittenRecordedAndConfirmed()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        var r = store.Toggle(store.Find("look.dark-apps")!);
        Assert.True(r.Ok);
        Assert.Equal("Dark mode for apps: On", r.Message);
        Assert.Equal("on", windows.Values["look.dark-apps"]);
        var change = Assert.Single(store.History.Changes());
        Assert.Equal(("look.dark-apps", "off", "on", "menu"), (change.Id, change.Before, change.After, change.Source));
    }

    [Fact]
    public void AskingForWhatItAlreadyIsWritesNothing()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        var r = store.Set(store.Find("explorer.open-to")!, "Home");
        Assert.Equal("File Explorer opens to: already Home", r.Message);
        Assert.Empty(windows.Writes);
        Assert.Empty(store.History.Changes());
    }

    [Fact]
    public void AValueWindowsKeepsIsReportedNotHidden()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        windows.Ignored.Add("look.dark-apps"); // a policy holds it
        var r = store.Set(store.Find("look.dark-apps")!, "on");
        Assert.False(r.Ok);
        Assert.Contains("Windows still says Off", r.Message);
    }

    [Fact]
    public void AdminChangesAreQueuedNeverWritten()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        var r = store.Toggle(store.Find("developer.long-paths")!);
        Assert.True(r.Queued);
        Assert.Empty(windows.Writes);
        Assert.Equal([("developer.long-paths", "on")], store.AdminQueue.Items());
        // A second press steps from the queued value, not from Windows' (still off).
        store.Toggle(store.Find("developer.long-paths")!);
        Assert.Empty(store.AdminQueue.Items()); // back to what it is: nothing left to do
    }

    [Fact]
    public void UndoWalksBackOneChangeAtATime()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        store.Set(store.Find("explorer.open-to")!, "1");
        store.Set(store.Find("explorer.open-to")!, "3");
        store.Toggle(store.Find("look.dark-apps")!);

        Assert.StartsWith("Undone: Dark mode for apps: Off", store.UndoLast().Message);
        Assert.StartsWith("Undone: File Explorer opens to: This PC", store.UndoLast().Message);
        Assert.StartsWith("Undone: File Explorer opens to: Home", store.UndoLast().Message);
        Assert.Equal("Nothing to undo", store.UndoLast().Message);
        Assert.Equal(("off", "2"), (windows.Values["look.dark-apps"], windows.Values["explorer.open-to"]));
    }

    [Fact]
    public void UndoPastAValueChangedElsewhereStillMovesOn()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        store.Toggle(store.Find("look.dark-apps")!);   // off -> on
        windows.Values["look.dark-apps"] = "off";       // put back in Windows' own Settings
        store.Forget();
        Assert.StartsWith("Undone: Dark mode for apps: already Off", store.UndoLast().Message);
        Assert.Equal("Nothing to undo", store.UndoLast().Message);
    }

    [Fact]
    public void UndoWithoutAKnownEarlierValueSaysSo()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        windows.Values.Remove("look.dark-apps"); // Windows wouldn't say
        store.Set(store.Find("look.dark-apps")!, "on");
        Assert.Contains("wasn't known", store.UndoLast().Message);
    }

    [Fact]
    public async Task ASlowValueArrivesLaterWithAChangeEvent()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        windows.Slow.Add("look.dark-apps");
        var arrived = new TaskCompletionSource<string>();
        store.Changed += id => arrived.TrySetResult(id);
        Assert.Null(store.Peek(store.Find("look.dark-apps")!)); // "…" for now
        Assert.Equal("look.dark-apps", await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("off", store.Peek(store.Find("look.dark-apps")!));
    }

    [Fact]
    public async Task ADeclinedPromptKeepsTheQueue()
    {
        using var dir = new TempDir();
        var (store, _) = Fixture.Store(dir);
        store.RunAdmin = _ => Task.FromResult(new AdminRun(false, []));
        store.Toggle(store.Find("developer.long-paths")!);
        var r = await store.ApplyAdminAsync();
        Assert.False(r.Ok);
        Assert.Single(store.AdminQueue.Items());
    }

    [Fact]
    public async Task OnePromptAppliesEverythingQueuedAndRecordsIt()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        IReadOnlyList<string>? plan = null;
        store.RunAdmin = lines =>
        {
            plan = lines;
            windows.Values["developer.long-paths"] = "on";   // what the elevated script does
            windows.Values["developer.vm-platform"] = "on";
            return Task.FromResult(new AdminRun(true, []));
        };
        store.Toggle(store.Find("developer.long-paths")!);
        store.Toggle(store.Find("developer.vm-platform")!);
        Assert.Equal(2, store.DescribeAdmin().Count);
        Assert.Contains("(needs a restart)", store.DescribeAdmin()[1]);

        var r = await store.ApplyAdminAsync();
        Assert.True(r.Ok);
        Assert.Contains("restart Windows", r.Message);
        Assert.Equal([@"developer.long-paths	reg	HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled	dword	1",
            "developer.vm-platform	builtin	windows-feature	VirtualMachinePlatform	on"], plan);
        Assert.Empty(store.AdminQueue.Items());
        Assert.Equal(["admin", "admin"], store.History.Changes().Select(c => c.Source));
    }

    [Fact]
    public void LinksAreOpenedNotRecorded()
    {
        using var dir = new TempDir();
        var (store, windows) = Fixture.Store(dir);
        Assert.Equal("Night light: opened", store.Toggle(store.Find("display.night-light")!).Message);
        Assert.Equal(["display.night-light="], windows.Writes);
        Assert.Empty(store.History.Changes());
    }
}

public class HistoryTests
{
    [Fact]
    public void TrimmingKeepsUndoLinesPointingAtTheirChange()
    {
        string[] lines =
        [
            "1\ta\toff\ton\tmenu",      // 0: dropped
            "2\tb\t1\t2\tmenu",         // 1
            "3\ta\ton\toff\tundo:0",    // 2: undid a dropped line: dropped with it
            "4\tb\t2\t1\tundo:1",       // 3: now undoes line 0
        ];
        var kept = SettingHistory.TrimLines(lines, 3);
        Assert.Equal(["2\tb\t1\t2\tmenu", "4\tb\t2\t1\tundo:0"], kept);
        Assert.Null(SettingHistory.LastUndoable(kept));
    }

    [Fact]
    public void FieldsNeverCarryTabs()
    {
        var line = SettingHistory.Format(1, "sound.output", "Speakers\t(Realtek)", "x", "menu");
        Assert.Equal(5, line.Split('\t').Length);
    }
}
