using System.Text.Json;
using SystemMenu.Search;

namespace SystemMenu.Tests.Search;

// The small pure pieces of One Search's window: GlazeWM's window list, the bar's keys, the
// requests the keys send, the home's places, the actions, the tags' looks.
public class SearchPiecesTests
{
    // --- windows -------------------------------------------------------------------------------
    [Fact]
    public void GlazeWmsWindowsAreReadAndTheFocusedOneGoesLast()
    {
        using var doc = JsonDocument.Parse("""
            {"windows":[
              {"type":"window","id":"a1","title":"Terminal","processName":"noctty","hasFocus":true},
              {"type":"window","id":"b2","title":"  ","processName":"slack","hasFocus":false},
              {"type":"workspace","id":"w","name":"1"},
              {"type":"window","title":"no id"},
              {"type":"window","id":"c3","title":"GitHub - Firefox","processName":"firefox"}
            ]}
            """);
        var list = WindowList.Read(doc.RootElement);
        Assert.Equal(["b2", "c3", "a1"], list.Select(w => w.Id));
        Assert.Equal("slack", list[0].Name); // no title: the program
    }

    [Fact]
    public void AnAnswerWithoutWindowsIsNone()
    {
        using var doc = JsonDocument.Parse("""{"monitors":[]}""");
        Assert.Empty(WindowList.Read(doc.RootElement));
    }

    // --- keys (guardrail 9) --------------------------------------------------------------------
    [Fact]
    public void AFiltersNameAndTabLocksItAndTheNameGoes()
    {
        Assert.Equal(new BarAnswer(BarMove.SetFilter, Tag.Setting, ""), SearchKeys.Tab(new("set", null), back: false));
    }

    [Fact]
    public void TabKeepsWhatWasTypedWhenItIsNoFilterName()
    {
        Assert.Equal(new BarAnswer(BarMove.SetFilter, Tag.App, "dark"), SearchKeys.Tab(new("dark", null), back: false));
        Assert.Equal(new BarAnswer(BarMove.SetFilter, null, "dark"), SearchKeys.Tab(new("dark", Tag.App), back: true));
    }

    [Fact]
    public void APrefixBecomesItsToken()
    {
        Assert.Equal(new BarAnswer(BarMove.SetFilter, Tag.File, "road"), SearchKeys.Tab(new("/road", null), back: false));
        Assert.Equal(new BarAnswer(BarMove.SetFilter, Tag.Command, "theme"), SearchKeys.Tab(new(">theme", Tag.Setting), back: false));
    }

    [Fact]
    public void EscStepsBackOneThingAtATime()
    {
        Assert.Equal(BarMove.CloseActions, SearchKeys.Escape(new("dark", Tag.Setting, ActionsOpen: true, InView: true)).Move);
        Assert.Equal(new BarAnswer(BarMove.ClearText, Tag.Setting), SearchKeys.Escape(new("dark", Tag.Setting, InView: true)));
        Assert.Equal(BarMove.Back, SearchKeys.Escape(new("", Tag.Setting, InView: true)).Move);
        Assert.Equal(new BarAnswer(BarMove.SetFilter, null, ""), SearchKeys.Escape(new("", Tag.Setting)));
        Assert.Equal(BarMove.Hide, SearchKeys.Escape(new("", null)).Move);
    }

    [Fact]
    public void BackspaceOnAnEmptyBarDropsTheFilterButIsALetterOtherwise()
    {
        Assert.Equal(BarMove.None, SearchKeys.Backspace(new("x", Tag.File)).Move);
        Assert.Equal(new BarAnswer(BarMove.SetFilter, null, ""), SearchKeys.Backspace(new("", Tag.File)));
        Assert.Equal(BarMove.Back, SearchKeys.Backspace(new("", Tag.Command, InView: true)).Move);
        Assert.Equal(BarMove.None, SearchKeys.Backspace(new("", null)).Move);
    }

    // --- requests ------------------------------------------------------------------------------
    [Fact]
    public void RequestsOpenOnAFilterAPageOrBrowsing()
    {
        Assert.Equal(new SearchRequest(null, null, "", false, null, false, ""), SearchRequest.Parse(["--search"]));
        Assert.Equal(Tag.File, SearchRequest.Parse(["--search", "file"]).Filter);
        Assert.Equal(Tag.Clipboard, SearchRequest.Parse(["--search", "clipboard"]).Filter);
        var settings = SearchRequest.Parse(["--search", "Settings", "dark", "mode"]);
        Assert.Equal((Tag.Setting, "dark mode"), (settings.Filter, settings.Text));
        var vpn = SearchRequest.Parse(["--search", "vpn", "Work", "VPN"]);
        Assert.Equal(("vpn", "Work VPN", (Tag?)null), (vpn.Page, vpn.PageArg, vpn.Filter));
        var browse = SearchRequest.Parse(["--search", "command", "--browse", "install"]);
        Assert.Equal((Tag.Command, true, "install", ""), (browse.Filter, browse.Browse, browse.Section, browse.Text));
        Assert.True(SearchRequest.Parse(["--search", "--retired-key"]).RetiredKey);
    }

    [Fact]
    public void ARequestsFirstWordIsAFilterOnlyByItsWholeName()
    {
        // "set" could be the start of what to find: it's text, not Settings.
        var r = SearchRequest.Parse(["--search", "set", "up"]);
        Assert.Equal(((Tag?)null, "set up"), (r.Filter, r.Text));
        Assert.Equal("dark", SearchRequest.Parse(["--search", "all", "dark"]).Text);
    }

    // --- the home (guardrail 2) ----------------------------------------------------------------
    static readonly HomeItem[] Items =
    [
        new("Terminal", null, false), new("Projects", "projects", false), new("Clipboard", "clipboard", false),
        new("Files", "files", false), new("Menu", "menu", false), new("Audio", "audio", false), new("Gmail", null, false),
        new("app.code", null, true), new("app.slack", null, true), new("app.paint", null, true),
        new("app.edge", null, true), new("app.zoom", null, true),
    ];
    static readonly HashSet<string> AllSwitches = [.. HomePlan.Switches];

    [Fact]
    public void TheHomeKeepsItsPlacesWhateverYouUse()
    {
        var before = HomePlan.Arrange(Items, [], AllSwitches);
        var after = HomePlan.Arrange(Items, ["app.zoom", "Menu", "app.slack", "Terminal"], AllSwitches);
        // Every row but Frequent is the same; Frequent keeps four places, now holding what you use.
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count - 1; i++) Assert.Equal(before[i].Slots, after[i].Slots);
        Assert.Equal(["app.zoom", "app.slack", "app.code", "app.paint"], after[^1].Slots.Select(s => s.Key));
    }

    [Fact]
    public void TheHomesRowsAreTerminalYourItemsBrowseAndThemeQuickAndFrequent()
    {
        var rows = HomePlan.Arrange(Items, [], AllSwitches);
        Assert.True(rows[0].Top);
        Assert.Equal(["Terminal", "Projects", "Clipboard"], rows[0].Slots.Select(s => s.Key));
        Assert.Equal(HomeStyle.Hero, rows[0].Slots[0].Style);
        Assert.Equal(["Files", "Audio", "Gmail"], rows[1].Slots.Select(s => s.Key)); // Menu is the Browse tile
        Assert.Equal([HomePlan.Browse, HomePlan.Theme], rows[2].Slots.Select(s => s.Key));
        Assert.Equal("Quick", rows[3].Title);
        Assert.Equal(HomePlan.Switches.Select(s => HomePlan.SettingPrefix + s), rows[3].Slots.Select(s => s.Key));
        Assert.Equal("Frequent", rows[4].Title);
    }

    [Fact]
    public void ASwitchThisPcDoesntHaveIsLeftOut()
    {
        var rows = HomePlan.Arrange(Items, [], new HashSet<string> { "look.dark-apps" });
        Assert.Equal(["setting:look.dark-apps"], rows.Single(r => r.Title == "Quick").Slots.Select(s => s.Key));
    }

    // --- pins (Ctrl+P) -------------------------------------------------------------------------
    [Fact]
    public void PinsGoLastSoNothingAboveMoves()
    {
        var before = HomePlan.Arrange(Items, ["app.zoom"], AllSwitches);
        var after = HomePlan.Arrange(Items, ["app.zoom"], AllSwitches, ["setting:look.dark-apps", "file:C:\\notes.md"]);
        Assert.Equal(before.Count + 1, after.Count);
        for (int i = 0; i < before.Count; i++) Assert.Equal(before[i].Slots, after[i].Slots);
        Assert.Equal("Pinned", after[^1].Title);
        Assert.Equal(["pin:setting:look.dark-apps", "pin:file:C:\\notes.md"], after[^1].Slots.Select(s => s.Key));
        Assert.All(after[^1].Slots, s => Assert.Equal(HomeStyle.Pin, s.Style));
    }

    [Fact]
    public void APinnedAppIsntInFrequentToo()
    {
        var rows = HomePlan.Arrange(Items, ["app.zoom", "app.slack"], AllSwitches, ["app:app.zoom"]);
        Assert.DoesNotContain("app.zoom", rows.Single(r => r.Title == "Frequent").Slots.Select(s => s.Key));
        Assert.Equal(["pin:app:app.zoom"], rows[^1].Slots.Select(s => s.Key));
    }

    [Fact]
    public void CtrlPPinsAtTheEndUnpinsAndNeverDropsOneOfYours()
    {
        var (one, c1) = Pins.Toggle([], "file:a", Tag.File);
        Assert.Equal(["file:a"], one);
        Assert.Equal(PinChange.Pinned, c1);
        var full = new List<string> { "a:1", "a:2", "a:3", "a:4" };
        Assert.Equal(PinChange.Full, Pins.Toggle(full, "cmd:update", Tag.Command).Change);
        var (three, c2) = Pins.Toggle(full, "a:2", Tag.App);
        Assert.Equal(["a:1", "a:3", "a:4"], three);
        Assert.Equal(PinChange.Unpinned, c2);
        Assert.Equal(PinChange.NotPinnable, Pins.Toggle([], "win:42", Tag.Window).Change);
        Assert.Equal(PinChange.NotPinnable, Pins.Toggle([], "clip:x", Tag.Clipboard).Change);
    }

    [Fact]
    public void ThePinsFileIsIdsOnceEach()
    {
        Assert.Equal(["app:x", "setting:look.dark-apps"], Pins.Parse(["# mine", " app:x ", "", "setting:look.dark-apps", "app:x", "junk"]));
    }

    [Fact]
    public void CtrlKOffersPinOrUnpin()
    {
        Assert.Equal("Pin to the home", ActionPlan.For(Tag.File, RowKind.None, new())[^1].Label);
        Assert.Equal("Unpin from the home", ActionPlan.For(Tag.Project, RowKind.None, new(Pinned: true))[^1].Label);
        Assert.DoesNotContain(ActionPlan.For(Tag.Window, RowKind.None, new()), a => a.Verb == Verb.Pin);
    }

    // --- actions -------------------------------------------------------------------------------
    [Fact]
    public void EnterIsTheFirstActionAndCtrlKListsTheRest()
    {
        Assert.Equal(Verb.Switch, ActionPlan.For(Tag.Setting, RowKind.Switch, new())[0].Verb);
        Assert.Equal([Verb.Next, Verb.Previous, Verb.CopyCommand, Verb.Undo, Verb.Pin],
            ActionPlan.For(Tag.Setting, RowKind.Choice, new(Undoable: true)).Select(a => a.Verb));
        Assert.Equal(Verb.Open, ActionPlan.For(Tag.Setting, RowKind.Link, new())[0].Verb);
        Assert.DoesNotContain(ActionPlan.For(Tag.File, RowKind.None, new(Folder: true)), a => a.Verb == Verb.OpenInEditor);
        Assert.DoesNotContain(ActionPlan.For(Tag.App, RowKind.None, new()), a => a.Verb == Verb.RunAsAdmin);
        Assert.Contains(ActionPlan.For(Tag.App, RowKind.None, new(Program: true)), a => a.Verb == Verb.RunAsAdmin);
    }

    [Fact]
    public void AnActionTheWindowCantDoIsLeftOut()
    {
        var bound = ActionPlan.Bind(ActionPlan.For(Tag.File, RowKind.None, new()), v => v == Verb.CopyPath ? null : () => Task.CompletedTask);
        Assert.DoesNotContain(bound, a => a.Label == "Copy path");
        Assert.Equal("Open", bound[0].Label);
    }

    [Theory]
    [InlineData(Tag.Setting, RowKind.Switch, "switch")]
    [InlineData(Tag.Setting, RowKind.Choice, "next")]
    [InlineData(Tag.Setting, RowKind.Link, "open ↗")]
    [InlineData(Tag.Command, RowKind.None, "run")]
    [InlineData(Tag.Clipboard, RowKind.None, "paste")]
    public void TheFooterSaysWhatEnterDoes(Tag tag, RowKind row, string word)
    {
        Assert.Equal(word, ActionPlan.EnterWord(new Result("x", tag, "x", "") { Row = row }));
    }

    // --- looks (guardrail 3) -------------------------------------------------------------------
    [Fact]
    public void EveryFilterHasItsOwnColourIconAndWord()
    {
        var tags = QueryParser.Filters.Select(f => f.Tag).ToList();
        Assert.Equal(tags.Count, tags.Select(TagLook.PaintOf).Distinct().Count());
        Assert.Equal(tags.Count, tags.Select(t => TagLook.Glyph(t)).Distinct().Count());
        Assert.All(tags, t => Assert.NotEqual("", TagLook.Word(t)));
        Assert.Equal("SETTING", TagLook.Word(Tag.Setting));
    }

    // --- history ids ---------------------------------------------------------------------------
    [Fact]
    public void AppsKeepTheirHistoryKeysAndEverythingElseUsesItsId()
    {
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", ResultIds.HistoryKey("app:Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        Assert.Equal("setting:look.dark-apps", ResultIds.HistoryKey("setting:look.dark-apps"));
    }
}
