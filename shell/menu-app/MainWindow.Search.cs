using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SystemMenu.Search;
using Windows.UI;
using STag = SystemMenu.Search.Tag;

namespace SystemMenu;

// One Search (roadmap 1C, plans/one-search.md): Win+Space opens one window with one bar for
// everything omawin can find or do. With nothing typed it shows the bento home; typing shows one
// ranked list of tagged results (apps, settings, commands, files, windows, projects, clipboard,
// themes), each tag capped in All, with the selected one's preview beside it. The filter tabs
// under the bar (Tab, a click, "/" for files, ">" for commands, a filter's name and Tab) narrow
// it to one tag; Win+F and Win+Shift+V open it on Files and Clipboard. Settings switch in place
// (Ctrl+Z undoes); Ctrl+K lists the selected result's other actions.
//
// It's the launcher grown: `launching` is One Search. Some filters are richer views that existed
// before and are kept whole: Files is the file finder (its folder chips move with Ctrl+Tab),
// Clipboard and Projects their pages, Themes the theme picker, and Commands with nothing typed
// is Browse, the old system menu's tiles and sections. The other pages (Audio, Quick settings,
// Packages, …) open from a result, as views; Esc comes back. The ranking, the bar's keys, the
// home's places and the actions are pure files under Search/, tested in menu-app.Tests.
public sealed partial class MainWindow
{
    const double TabsDip = 30;                // the filter tabs' row (MainWindow.xaml)
    STag? searchFilter;                      // the filter token (null: All)
    STag? openFilter;                        // the filter the next showing opens on (OpenSearch → Show)
    List<Result> results = [];               // the result rows on screen, by row
    List<Result> lastResults = [];           // the ones before this drawing (Merger.Stable)
    bool resultsUpdate;                      // this drawing is a late source's answer or a setting's new value: keep the selection
    Result? actionsFor;                      // Ctrl+K: this result's actions are listed
    List<ResultAction> actionList = [];
    int actionsFrom;                         // the row it was on (Esc goes back there)

    // --- opening ------------------------------------------------------------------------------
    // Win+Space (no filter), Win+F (Files), Win+Shift+V (Clipboard), omawin menu (Commands,
    // browsing). The same key again while it's in front on that filter hides it.
    public void OpenSearch(STag? filter, string text = "", string? section = null, bool retiredKey = false)
    {
        try
        {
            if (Showing && GetForegroundWindow() == hwnd && launching && page == null && searchFilter == filter
                && current.Length == 0 && text.Length == 0 && section == null && !retiredKey)
            {
                Dismiss();
                return;
            }
            openFilter = filter;
            Show("", asLauncher: true);
            if (section != null && entries.TryGetValue(section, out var e) && e.IsSubmenu && searchFilter == STag.Command)
            {
                current = section;
                Render();
            }
            if (text.Length > 0) { Input.Text = text; Input.SelectionStart = text.Length; }
            // Win+Alt+Space, retired (plans/one-search.md, guardrail 10): it still opens this, for
            // one release, and says which key does now.
            if (retiredKey) Snip.Say("Win+Space does this now: the system menu is part of One Search");
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // The filter's view, set up without drawing (Show and SetFilter draw after).
    void EnterFilter(STag? tag)
    {
        if (pickingTheme) LeaveThemePicker();
        pickingTheme = false;
        finding = false;
        findFromLauncher = false;
        page = null;
        settingsArea = null;
        confirmingAdmin = false;
        galleryOpen = false;
        backgroundsOf = null;
        confirming = null;
        current = "";
        actionsFor = null;
        lastResults = results = [];
        searchFilter = tag;
        selected = 0;
        switch (tag)
        {
            case STag.File:
                finding = true;
                findFromLauncher = true;
                break;
            case STag.Clipboard:
                page = "clipboard";
                pageArg = "";
                break;
            case STag.Project:
                page = "projects";
                pageArg = "";
                projects = Projects.Latest;
                Projects.Find().ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
                {
                    projects = t.IsCompletedSuccessfully ? t.Result : [];
                    if (page == "projects") Render();
                }));
                break;
            case STag.Theme:
                pickingTheme = true;
                selectKey = "theme:" + themeName;
                break;
        }
    }

    // Tab, a tab clicked, a prefix typed, "Show all": that filter, with these words in the bar.
    void SetFilter(STag? tag, string text)
    {
        EnterFilter(tag);
        if (Input.Text != text)
        {
            Input.Text = text; // TextChanged draws
            Input.SelectionStart = text.Length;
        }
        else Render();
        Swap();
    }

    // "/" or ">" typed first: the Files or Commands filter, the prefix gone (it's the token now).
    bool PrefixToFilter()
    {
        if (!launching || !TabsShown || Input.Text.Length == 0 || Input.Text[0] is not ('/' or '>')) return false;
        SetFilter(Input.Text[0] == '/' ? STag.File : STag.Command, Input.Text[1..].TrimStart());
        return true;
    }

    // The filter tabs and token show on One Search's own views: a filter's list or view, the
    // home, Browse's sections; not on a page or picker opened from a result, nor Ctrl+K's list.
    bool TabsShown => launching && actionsFor == null && settingsArea == null && !confirmingAdmin && !galleryOpen
        && backgroundsOf == null && confirming == null
        && (page == null || page == "clipboard" && searchFilter == STag.Clipboard || page == "projects" && searchFilter == STag.Project);

    // A view inside the filter, which Esc and Backspace leave first.
    // (Browse's sections, and the theme picker opened from Browse, are views inside Commands.)
    bool InView => launching && (!TabsShown || searchFilter == STag.Command && (current.Length > 0 || pickingTheme));

    string SearchText => QueryParser.Parse(Input.Text, searchFilter).Text;

    // --- drawing ------------------------------------------------------------------------------
    // One Search's part of Render, for every filter whose view isn't one of the older ones.
    TileLayout? RenderOneSearch((bool Launcher, TileLayout Layout)? built)
    {
        var q = QueryParser.Parse(Input.Text, searchFilter);
        string text = q.Text;
        Placeholder.Text = q.Filter is STag f ? $"Search {QueryParser.NameOf(f).ToLowerInvariant()}…" : "Search apps, settings, commands, files…";

        if (text.Length == 0 && q.Filter is null)
        {
            SetCrumbs("", null);
            var home = Home(launcher: true, built);
            FooterLeft.Text = NoteFor("") ?? $"{launchItems.Count(i => i.IsApp)} apps · {theme.Name}";
            SetKeys(("←↑↓→", "move"), ("↵", "open"), ("tab", "filter"), ("esc", "close"));
            return home;
        }
        if (text.Length == 0 && q.Filter == STag.Command)
        {
            // Browse: the menu's sections, as the system menu had them.
            if (current.Length == 0)
            {
                SetCrumbs("", null);
                Placeholder.Text = "Search commands, or browse…";
                SetKeys(("←↑↓→", "move"), ("↵", "open"), ("tab", "filter"), ("esc", "back"));
                FooterLeft.Text = "Browse: everything the system menu had";
                return Home(launcher: false, built);
            }
            RenderMenuList("");
            return null;
        }
        if (text.Length == 0 && q.Filter == STag.Setting)
        {
            RenderSettingAreas();
            return null;
        }

        SetCrumbs(q.Filter == STag.Command ? current : "", null);
        var history = History.Load();
        var fresh = Merger.Merge(Candidates(q), q, (id, typed) => history.Boost(ResultIds.HistoryKey(id), typed));
        results = resultsUpdate ? Merger.Stable(lastResults, fresh, selected) : fresh;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.Data is SettingEntry e) settingRows[i] = e;
            rows.Add((null, () => Activate(r)));
        }
        if (results.Count == 0) AddRow(Glyph(0xF0156), "No matches", Hex(theme.Comment), () => { }, dim: true);
        else Virtualize((v, i) => DrawResult(v, results[i]));
        int found = results.Count(r => r.Tag is not (STag.More or STag.Package or STag.Web or STag.Calculator));
        FooterLeft.Text = NoteFor(text) ?? (q.Filter is STag only ? $"{found} {QueryParser.NameOf(only).ToLowerInvariant()}" : $"{found} found");
        SetResultKeys();
        return null;
    }

    void SetResultKeys()
    {
        var r = selected < results.Count ? results[selected] : null;
        SetKeys(("↑↓", "move"), ("↵", ActionPlan.EnterWord(r)), ("ctrl k", "actions"), ("tab", "filter"), ("esc", Input.Text.Length > 0 ? "clear" : "back"));
    }

    // The Settings filter before anything's typed: what's waiting, the areas to browse, undo.
    void RenderSettingAreas()
    {
        HookSettings();
        SetCrumbs("", null);
        Placeholder.Text = "Search Windows settings…";
        AddPendingRows();
        var all = Settings.Entries();
        foreach (var area in Catalog.Areas)
        {
            int count = all.Count(e => e.Area == area);
            if (count == 0) continue;
            var a = area;
            AddRow(Catalog.AreaIcon(area), Catalog.AreaLabel(area), Hex(theme.Green), () => OpenSettingsArea(a), Tag.Chevron, sub: $"{count} settings");
        }
        if (Settings.History.LastUndoable() is { } last && Settings.Find(last.Id) is { } undoable)
            AddRow(Glyph(0xF054C), $"Undo: {undoable.Name} back to {Settings.LabelNow(undoable, last.Before)}", Hex(theme.Yellow),
                UndoSetting, Tag.Text, "ctrl z");
        FooterLeft.Text = NoteFor("") ?? $"{all.Count} settings omawin changes in place; type for every Windows setting";
        SetKeys(("↵", "open"), ("ctrl z", "undo"), ("tab", "filter"), ("esc", "back"));
    }

    void OpenSettingsArea(string area)
    {
        settingsArea = area;
        settingsAreaFrom = "";
        Settings.Forget(e => e.Area == area && e.How.Verb == "powercfg"); // no change event: read fresh
        selected = 0;
        Render();
        Slide(1);
    }

    // Ctrl+K: the selected result's actions, as rows; its preview stays beside them.
    void RenderActions()
    {
        var r = actionsFor!;
        SetCrumbs("", "Actions");
        Placeholder.Text = r.Name;
        foreach (var a in actionList)
        {
            var action = a;
            AddRow(Glyph(TagLook.Glyph(r.Tag)), a.Label, PaintColor(r.Tag), () => { actionsFor = null; RunAction(action); }, Tag.Text, a.Key ?? "");
        }
        FooterLeft.Text = r.Name;
        SetKeys(("↑↓", "move"), ("↵", "do it"), ("esc", "back"));
    }

    void ToggleActions()
    {
        if (actionsFor != null) { CloseActions(); return; }
        if (tileMode || selected >= results.Count) return;
        var r = results[selected];
        actionList = ActionsOf(r);
        if (actionList.Count == 0) return;
        actionsFor = r;
        actionsFrom = selected;
        selected = 0;
        Render();
    }

    void CloseActions()
    {
        actionsFor = null;
        resultsUpdate = true; // the list as it was, the same row selected
        selected = actionsFrom;
        Render();
    }

    void RunAction(ResultAction a)
    {
        try { _ = a.Run(); } catch (Exception ex) { App.Log(ex); }
    }

    // A result row: its icon (an app's or file's own, a theme's colours), the name with the
    // letters that matched, where it is; on the right a setting's value, a key, or the tag's word.
    void DrawResult(RowView v, Result r)
    {
        var color = PaintColor(r.Tag);
        var sub = r.Detail.Length > 0 ? r.Detail : null;
        var highlight = r.Positions is { Count: > 0 } p ? p.ToList() : null;
        DrawRow(v, r.Icon ?? Glyph(TagLook.Glyph(r.Tag)), r.Name, color, Tag.Text, "", sub: sub,
            swatch: r.Data is ThemeRef t ? t.Theme : null, appId: r.IconKey, highlight: highlight);
        if (r.Data is SettingEntry e && e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider)
        {
            var value = Settings.Peek(e);
            bool reading = value == null && !Settings.TryCached(e, out _);
            if (e.Kind == SettingKind.Switch && value == "on") SetRight(v, Tag.Installed, "", pill: "On", pillColor: theme.Green);
            else if (e.Kind == SettingKind.Switch) SetRight(v, Tag.Text, reading ? "…" : value == null ? "?" : "Off");
            else SetRight(v, Tag.Text, reading ? "…" : $"‹ {Settings.LabelNow(e, value)} ›");
            return;
        }
        if (r.Row == RowKind.Link) { SetRight(v, Tag.Text, "↗"); return; }
        if (r.Key != null) { SetRight(v, Tag.Text, r.Key.ToLowerInvariant()); return; }
        SetRight(v, Tag.Text, TagLook.Word(r.Tag));
        v.RightText.Foreground = Brush(color, 0xC0);
        v.RightText.CharacterSpacing = 120;
    }

    Color PaintColor(STag tag) => TagLook.PaintOf(tag) switch
    {
        Paint.Accent => Hex(theme.Accent),
        Paint.Green => Hex(theme.Green),
        Paint.Yellow => Hex(theme.Yellow),
        Paint.Cyan => Hex(theme.Cyan),
        Paint.Purple => Hex(theme.Purple),
        Paint.Red => Hex(theme.Red),
        Paint.Muted => Hex(theme.Muted),
        Paint.Gradient => GradientAt(0.6),
        Paint.Foreground => Hex(theme.Foreground),
        _ => Hex(theme.Comment),
    };

    // --- the filter tabs and the token ---------------------------------------------------------
    readonly List<(STag? Tag, TextBlock Glyph, TextBlock Word, Border Line)> tabViews = [];

    // Underlined words, not pills (guardrail 12): each tag's icon and word, the selected one in its
    // colour with a line under it.
    void DrawTabs()
    {
        SearchTabs.Visibility = TabsShown ? Visibility.Visible : Visibility.Collapsed;
        if (!TabsShown) return;
        if (tabViews.Count == 0)
        {
            foreach (var tag in new STag?[] { null }.Concat(QueryParser.Filters.Select(f => (STag?)f.Tag)))
            {
                var glyph = new TextBlock { Text = Glyph(TagLook.Glyph(tag)), FontFamily = iconFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                var word = new TextBlock { Text = TagLook.TabWord(tag), FontFamily = monoFont, FontSize = 10.5, CharacterSpacing = 120, VerticalAlignment = VerticalAlignment.Center };
                var line = new Border { Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 5, 0, 0) };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, word } };
                var tab = new StackPanel { Children = { label, line }, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Colors.Transparent) };
                var t = tag;
                tab.Tapped += (_, _) => { SetFilter(t, Input.Text.Length > 0 && Input.Text[0] is '/' or '>' ? Input.Text[1..] : Input.Text); Input.Focus(FocusState.Programmatic); };
                SearchTabs.Children.Add(tab);
                tabViews.Add((tag, glyph, word, line));
            }
        }
        var shown = QueryParser.Parse(Input.Text, searchFilter).Filter;
        foreach (var (tag, glyph, word, line) in tabViews)
        {
            bool on = tag == shown;
            var color = tag is STag t ? PaintColor(t) : Hex(theme.Foreground);
            glyph.Foreground = on ? new SolidColorBrush(color) : Brush(theme.Comment);
            word.Foreground = on ? new SolidColorBrush(color) : Brush(theme.Comment);
            word.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            line.Background = on ? new SolidColorBrush(color) : new SolidColorBrush(Colors.Transparent);
        }
    }

    // The filter as a token at the start of the bar: a hairline box in its colour, icon and word.
    Border Token(STag tag)
    {
        var color = PaintColor(tag);
        return new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 4), VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1), BorderBrush = Brush(color, 0x90), Background = Brush(color, 0x14),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock { Text = Glyph(TagLook.Glyph(tag)), FontFamily = iconFont, FontSize = 12, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = TagLook.TabWord(tag), FontFamily = monoFont, FontSize = 10.5, CharacterSpacing = 120, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
    }

    // The footer of a filter's older view (the finder's, the theme picker's): their own Tab moves
    // to Ctrl+Tab, since Tab moves between the filters.
    void FilterViewKeys()
    {
        if (!TabsShown || keysShown.Any(k => k.Label == "filter")) return;
        var keys = keysShown.Select(k => k.Key == "tab" ? (Key: "ctrl tab", k.Label) : k).ToList();
        int esc = keys.FindIndex(k => k.Key == "esc");
        keys.Insert(esc < 0 ? keys.Count : esc, ("tab", "filter"));
        SetKeys([.. keys]);
    }

    // --- keys -----------------------------------------------------------------------------------
    // One Search's keys, before the window's own (OnKey). True when handled.
    bool SearchKey(Windows.System.VirtualKey key, bool ctrl, bool shift)
    {
        if (!launching) return false;
        var bar = new BarState(Input.Text, searchFilter, actionsFor != null, InView);
        switch (key)
        {
            case Windows.System.VirtualKey.Tab when TabsShown && !ctrl:
                Apply(SearchKeys.Tab(bar, back: shift));
                return true;
            // The view's own Tab: the finder's folders, the theme picker's Built in | Community.
            case Windows.System.VirtualKey.Tab when TabsShown && ctrl:
                if (finding) CycleScope(shift ? -1 : 1);
                else if (pickingTheme) ToggleCommunity();
                return true;
            case Windows.System.VirtualKey.Escape:
                Apply(SearchKeys.Escape(bar));
                return true;
            case Windows.System.VirtualKey.Back when Input.Text.Length == 0:
                var answer = SearchKeys.Backspace(bar);
                if (answer.Move == BarMove.None) return false;
                Apply(answer);
                return true;
            case Windows.System.VirtualKey.K when ctrl && (actionsFor != null || page == null && !tileMode && selected < results.Count):
                ToggleActions();
                return true;
            // Ctrl+P: pin the selected result to the home, or unpin it; on the home, a pin's tile.
            case Windows.System.VirtualKey.P when ctrl && actionsFor == null && page == null && !tileMode && !finding && selected < results.Count
                    && Pins.CanPin(results[selected].Tag):
                TogglePin(results[selected]);
                return true;
            case Windows.System.VirtualKey.P when ctrl && tileMode && OnHome && selected < tiles.Count && tiles[selected].Spec.Key.StartsWith(Pins.TileKey):
                var tile = tiles[selected].Spec;
                SavePins(Pins.Toggle(PinList, tile.Key[Pins.TileKey.Length..], STag.More).Pins);
                Render();
                FooterLeft.Text = Pins.Say(PinChange.Unpinned, tile.Label);
                return true;
            // Ctrl+Enter: show a file in its folder; Ctrl+Shift+Enter: run an item as administrator.
            case Windows.System.VirtualKey.Enter when ctrl && actionsFor == null && page == null && !tileMode && !finding && selected < results.Count:
                var r = results[selected];
                var verb = shift ? Verb.RunAsAdmin : Verb.ShowInFolder;
                if (ActionPlan.For(r.Tag, r.Row, FactsOf(r)).Any(s => s.Verb == verb) && Do(r, verb) is { } run) _ = run();
                return true;
            default:
                return false;
        }
    }

    void Apply(BarAnswer a)
    {
        switch (a.Move)
        {
            case BarMove.SetFilter: SetFilter(a.Filter, a.Text); break;
            case BarMove.CloseActions: CloseActions(); break;
            case BarMove.ClearText: Input.Text = ""; break;
            case BarMove.Back: Back(hideAtTop: false); break;
            case BarMove.Hide: Dismiss(); break;
        }
    }

    // --- doing ----------------------------------------------------------------------------------
    // Enter on a result. What you pick after typing something ranks first for it next time (the
    // launcher's history; apps are remembered by Launch, settings by their id like the rest).
    void Activate(Result r)
    {
        string text = SearchText;
        if (r.Tag is not (STag.App or STag.More or STag.Package or STag.Web or STag.Calculator) && !r.Pinned)
            History.Load().Add(ResultIds.HistoryKey(r.Id), text);
        switch (r.Tag)
        {
            case STag.More when Enum.TryParse<STag>(r.Id["more:".Length..], out var tag):
                SetFilter(tag, text);
                return;
            case STag.Package:
                EnterPage("packages");
                Input.Text = text;
                Input.SelectionStart = text.Length;
                Slide(1);
                return;
            case STag.Web:
                HideMenu();
                // Only ever when picked (guardrail 7): nothing is sent anywhere while you type.
                try { Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(text)) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { App.Log(ex); }
                return;
        }
        try { _ = r.Primary?.Invoke(); } catch (Exception ex) { App.Log(ex); }
    }

    ResultFacts FactsOf(Result r) => new(
        Program: r.Data is LaunchItem { IsApp: false, BuiltIn: null } item && !Launcher.Link.IsMatch(item.Command.Trim()),
        Folder: r.Data is FileSearch.Hit { IsDir: true },
        Undoable: Settings.History.LastUndoable() != null,
        Pinned: PinList.Contains(r.Id));

    // What each action does, for this result.
    List<ResultAction> ActionsOf(Result r) => ActionPlan.Bind(ActionPlan.For(r.Tag, r.Row, FactsOf(r)), verb => Do(r, verb));

    Func<Task>? Do(Result r, Verb verb)
    {
        Func<Task> Now(Action a) => () => { a(); return Task.CompletedTask; };
        string text = SearchText;
        switch (r.Data, verb)
        {
            case (_, Verb.Open or Verb.Switch or Verb.Run or Verb.SwitchTo or Verb.Paste or Verb.Apply or Verb.CopyAnswer
                    or Verb.Filter or Verb.FindPackages or Verb.SearchWeb):
                return Now(() => Activate(r));
            case (LaunchItem item, Verb.RunAsAdmin):
                return Now(() => { Launcher.Remember(item, text); HideMenu(); RunAsAdmin(item.Command); });
            case (LaunchItem, Verb.AppSettings):
                return Now(() => { HideMenu(); QuickSettings.Open("ms-settings:appsfeatures"); });
            case (SettingEntry e, Verb.Next or Verb.Previous):
                return Now(() => StepSetting(e, verb == Verb.Next ? 1 : -1));
            case (SettingEntry e, Verb.CopyCommand):
                return Now(() =>
                {
                    var value = Settings.Peek(e);
                    ClipboardHistory.SetText(e.Kind == SettingKind.Switch || value == null ? $"omawin setting toggle {e.Id}" : $"omawin setting set {e.Id} {value}");
                    Dismiss();
                });
            case (_, Verb.Undo):
                return Now(UndoSetting);
            case (OmawinCommand c, Verb.RunInTerminal):
                return Now(() => RunCommand(c, CommandList.ArgumentsIn(text, c), RunIn.Terminal));
            case (OmawinCommand c, Verb.CopyCommand):
                return Now(() => { ClipboardHistory.SetText((c.Line + " " + CommandList.ArgumentsIn(text, c)).Trim()); Dismiss(); });
            case (MenuEntry e, Verb.RunInTerminal) when e.Kind == "omawin":
                return Now(() => { HideMenu(); MenuModel.Omawin("launch", "terminal", "--", "pwsh", "-NoLogo", "-NoExit", "-Command", "omawin " + e.Argument); });
            case (MenuEntry e, Verb.CopyCommand) when e.Kind == "omawin":
                return Now(() => { ClipboardHistory.SetText("omawin " + e.Argument); Dismiss(); });
            case (FileSearch.Hit hit, Verb.ShowInFolder):
                return Now(() => OpenFile(hit, text, reveal: true));
            case (FileSearch.Hit hit, Verb.CopyPath):
                return Now(() => { ClipboardHistory.SetText(hit.Path); Dismiss(); });
            case (FileSearch.Hit hit, Verb.TerminalHere):
                return Now(() => { HideMenu(); MenuModel.Omawin("launch", "terminal", "--dir", hit.IsDir ? hit.Path : Path.GetDirectoryName(hit.Path)!); });
            case (FileSearch.Hit hit, Verb.OpenInEditor):
                return Now(() => { HideMenu(); MenuModel.Omawin("launch", "editor", hit.Path); });
            case (OpenWindow w, Verb.Close):
                return Now(() => { Bar.Wm.Command($"--id {w.Id} close"); openWindows.RemoveAll(o => o.Id == w.Id); Render(); });
            case (Project p, Verb.TerminalHere):
                return Now(() => { HideMenu(); MenuModel.Omawin(p.Wsl ? ["launch", "terminal", "--wsl", "--dir", p.Path] : ["launch", "terminal", "--dir", p.Path]); });
            case (Project p, Verb.OpenInEditor) when !p.Wsl:
                return Now(() => { HideMenu(); MenuModel.Omawin("launch", "editor", p.Path); });
            case (Project p, Verb.CopyPath):
                return Now(() => { ClipboardHistory.SetText(p.Path); Dismiss(); });
            case (ClipFile f, Verb.Copy):
                return Now(() => { try { ClipboardHistory.SetEntry(f.File); } catch (IOException) { } Dismiss(); });
            case (ClipFile f, Verb.Delete):
                return Now(() => { try { File.Delete(f.File); } catch (IOException) { } clipTexts.Remove(f.File); clipList = null; Render(); });
            case (ThemeRef t, Verb.Backgrounds):
                return Now(() => { OpenBackgrounds(t.File); Render(); Slide(1); });
            case (_, Verb.Pin):
                return Now(() => TogglePin(r));
            default:
                return null;
        }
    }

    // A program from your items, elevated (Windows asks first: that's the prompt saying so).
    static void RunAsAdmin(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var m = System.Text.RegularExpressions.Regex.Match(command, "^\"([^\"]+)\"\\s*(.*)$");
        if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(command, @"^(\S+)\s*(.*)$");
        if (!m.Success) return;
        try { Process.Start(new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value) { UseShellExecute = true, Verb = "runas" })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { } // declined
        catch (Exception ex) { App.Log(ex); }
    }

    // An omawin command: hidden, as its key would run it, or in a terminal, as if typed there
    // (CommandList.RunsIn). One that needs words fills the bar and waits for them.
    void RunCommand(OmawinCommand c, string args, RunIn how)
    {
        if (c.NeedsArguments && args.Length == 0 && how == RunIn.Hidden)
        {
            SetFilter(STag.Command, c.Name + " ");
            return;
        }
        string[] words = [.. c.Name.Split(' '), .. Catalog.Tokens(args)];
        HideMenu(instant: CapturesScreen(c.Line));
        if (how == RunIn.Hidden) MenuModel.Omawin(words);
        else MenuModel.Omawin("launch", "terminal", "--", "pwsh", "-NoLogo", "-NoExit", "-Command", (c.Line + " " + args).Trim());
    }

    // --- the sources ------------------------------------------------------------------------------
    // Every source's candidates for a query; the merger ranks them (Search/Merger.cs). Fast ones are
    // in memory; files and windows answer later (FileResults, WindowResults) and the list is merged
    // again without moving the selected row.
    List<Result> Candidates(ParsedQuery q)
    {
        bool Want(STag tag) => q.Filter is null || q.Filter == tag;
        var list = new List<Result>();
        if (q.Filter is null && q.Text.Length > 0 && Calc.TryEvaluate(q.Text, out var value))
        {
            string answer = Calc.Format(value);
            list.Add(new Result("calc", STag.Calculator, "= " + answer, q.Text,
                Primary: () => { ClipboardHistory.SetText(answer); Dismiss(); return Task.CompletedTask; }) { Pinned = true, Icon = Glyph(0xF00EC) });
        }
        if (Want(STag.App)) list.AddRange(AppResults());
        if (Want(STag.Setting)) list.AddRange(SettingResults(q.Text, pending: q.Filter == STag.Setting));
        if (Want(STag.Command)) list.AddRange(CommandResults(q.Text));
        if (Want(STag.Window)) list.AddRange(WindowResults());
        if (Want(STag.Project)) list.AddRange(ProjectResults());
        if (Want(STag.Clipboard)) list.AddRange(ClipResults());
        if (Want(STag.Theme)) list.AddRange(ThemeResults());
        if (Want(STag.File) && (q.Filter == STag.File || q.Text.Length >= Merger.FilesFrom)) list.AddRange(FileResults(q.Text));
        return list;
    }

    // A late source answered: merge again, if the list it's for is still on screen.
    void SourceUpdated()
    {
        if (!appWindow.IsVisible || !launching || page != null || actionsFor != null || tileMode || finding || pickingTheme || settingsArea != null) return;
        resultsUpdate = true;
        Render();
    }

    // Apps and your items (Launcher.Load); built-in pages are your items too, and open their view.
    List<LaunchItem>? appsFor;
    List<Result> appResults = [];
    List<Result> AppResults()
    {
        if (ReferenceEquals(appsFor, launchItems)) return appResults;
        appsFor = launchItems;
        return appResults = [.. launchItems.Select(item => new Result("app:" + item.Key, STag.App, item.Name,
            item.IsApp ? item.Linux ?? "" : item.Hint.TrimEnd(' ', '›'), string.Join(' ', item.Keywords),
            Primary: () => { Launch(item, SearchText); return Task.CompletedTask; })
            { Icon = item.Icon, IconKey = item.IconKey, Data = item })];
    }

    // The catalog's settings, switched in place, and every page Windows indexes, as links that say
    // they open Settings (guardrail 8). Typed: what Settings' own search finds (Settings/Search.cs);
    // in the Settings filter with nothing typed, the whole catalog.
    List<Result> SettingResults(string text, bool pending)
    {
        HookSettings();
        var list = new List<Result>();
        if (pending)
        {
            int queued = Settings.AdminQueue.Count;
            if (queued > 0)
                list.Add(new Result("admin-queue", STag.Setting, $"Apply {queued} admin change{(queued == 1 ? "" : "s")}", "One prompt for all of them",
                    Primary: () => { confirmingAdmin = true; selected = 0; Render(); return Task.CompletedTask; }) { Pinned = true, Admin = true, Icon = Glyph(0xF0498) });
            if (ExplorerRestart.Pending)
                list.Add(new Result("explorer-restart", STag.Setting, "Restart Explorer to apply", "The taskbar blinks once",
                    Primary: () => Task.Run(ExplorerRestart.Now)) { Pinned = true, Icon = Glyph(0xF0450) });
        }
        if (text.Length == 0)
        {
            foreach (var e in Settings.Entries()) list.Add(FromSetting(e));
            return list;
        }
        foreach (var hit in WinSettings.Search(text, 60))
        {
            if (hit.Entry is { } e) list.Add(FromSetting(e));
            else if (hit.Page is { } p)
                list.Add(new Result(hit.Key, STag.Setting, p.Name, p.Page + " · opens Settings", p.Keywords + " " + p.Page,
                    Primary: () => { HideMenu(); WinSettings.Open(p); return Task.CompletedTask; }) { Row = RowKind.Link, Icon = Glyph(0xF0493), Data = p });
        }
        return list;
    }

    Result FromSetting(SettingEntry e)
    {
        var notes = new List<string> { Catalog.AreaLabel(e.Area) };
        if (e.Admin) notes.Add("needs admin");
        if (e.Takes.HasFlag(Takes.Restart)) notes.Add("after a restart");
        else if (e.Takes.HasFlag(Takes.SignOut)) notes.Add("after sign-out");
        else if (e.How.Flags.Contains("restart-explorer")) notes.Add("restarts Explorer");
        return new Result("setting:" + e.Id, STag.Setting, e.Name, string.Join(" · ", notes), e.Keywords + " " + Catalog.AreaLabel(e.Area),
            Primary: () =>
            {
                if (e.Kind is SettingKind.Link or SettingKind.Action) HideMenu();
                DoSetting(() => Settings.Toggle(e), e, SearchText);
                return Task.CompletedTask;
            })
        {
            Row = e.Kind switch { SettingKind.Switch => RowKind.Switch, SettingKind.Choice => RowKind.Choice, SettingKind.Slider => RowKind.Slider, _ => RowKind.Link },
            State = () => Settings.Peek(e),
            Step = by => { StepSetting(e, by); return Task.CompletedTask; },
            Admin = e.Admin,
            Icon = Catalog.IconOf(e),
            Data = e,
        };
    }

    // omawin's commands (bin/commands.txt) and every row of the menu (default/launcher/menu.txt):
    // its sections open Browse there, its rows do what they did in the system menu.
    List<OmawinCommand>? commands;
    DateTime commandsStamp;
    Dictionary<string, MenuEntry>? menuFor;
    List<Result> menuResults = [];

    List<Result> CommandResults(string text)
    {
        var file = Path.Combine(MenuModel.Repo, "bin", "commands.txt");
        var stamp = File.GetLastWriteTimeUtc(file);
        if (commands == null || stamp != commandsStamp || !ReferenceEquals(menuFor, entries))
        {
            try { commands = CommandList.Shown(CommandList.Parse(File.ReadAllLines(file)), entries.Values.Select(e => e.Action ?? "")); }
            catch (IOException) { commands = []; }
            commandsStamp = stamp;
            menuFor = entries;
            menuResults = [.. entries.Values.Where(e => !(e.Kind == "run" && e.Argument.StartsWith("ms-settings:"))).Select(FromMenu)];
        }
        var list = new List<Result>(menuResults);
        foreach (var c in commands)
        {
            var args = CommandList.ArgumentsIn(text, c);
            var command = c;
            list.Add(new Result("cmd:" + c.Name, STag.Command, args.Length > 0 ? c.Name + " " + args : c.Name, c.Short, c.Summary,
                Primary: () => { RunCommand(command, CommandList.ArgumentsIn(SearchText, command), CommandList.RunsIn(command)); return Task.CompletedTask; })
            { Key = c.Key, Icon = Glyph(0xF07B7), Data = c });
        }
        return list;
    }

    Result FromMenu(MenuEntry e)
    {
        bool have = e.Package != null && installed.Contains(e.Package);
        var where = MenuModel.PathLabel(entries, e.Parent);
        var detail = e.IsSubmenu ? (where.Length > 0 ? where + " · section" : "Section") : have ? (where + " · installed").TrimStart(' ', '·') : where;
        return new Result("menu:" + e.Id, STag.Command, e.Label, detail, e.Id.Replace('.', ' ') + (e.Action is { } a ? " " + a : ""),
            Primary: () =>
            {
                if (e.IsSubmenu) { EnterFilter(STag.Command); current = e.Id; Input.Text = ""; Render(); Slide(1); }
                else Pick(e);
                return Task.CompletedTask;
            })
        { Icon = e.Icon, Data = e };
    }

    // Open windows, from GlazeWM (Bar.Wm); asked again when it's been a second.
    List<OpenWindow> openWindows = [];
    DateTime windowsAsked;
    bool windowsHooked;

    List<Result> WindowResults()
    {
        if (!windowsHooked)
        {
            windowsHooked = true;
            Bar.Wm.Windows += list => DispatcherQueue.TryEnqueue(() => { openWindows = list; SourceUpdated(); });
        }
        if (DateTime.Now - windowsAsked > TimeSpan.FromSeconds(1))
        {
            windowsAsked = DateTime.Now;
            Bar.Wm.QueryWindows();
        }
        return [.. openWindows.Select(w => new Result("win:" + w.Id, STag.Window, w.Name, w.Process + (w.Focused ? " · where you were" : ""), w.Process,
            Primary: () => { Bar.Wm.Command($"focus --container-id {w.Id}"); HideMenu(); return Task.CompletedTask; })
        { Icon = Glyph(0xF05AF), Data = w })];
    }

    List<Result> ProjectResults()
    {
        if (Projects.Latest is { } latest) projects = latest;
        else if (projects == null && !findingProjects)
        {
            findingProjects = true;
            Projects.Find().ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                findingProjects = false;
                if (t.IsCompletedSuccessfully) projects ??= t.Result;
                SourceUpdated();
            }));
        }
        return [.. (projects ?? []).Select(FromProject)];
    }

    Result FromProject(Project p) => new("project:" + p.Path, STag.Project, p.Name, p.Shown + (p.Wsl ? " · " + Projects.Linux.Name : ""), "repo project",
        Primary: () => { HideMenu(); Projects.Open(p); return Task.CompletedTask; })
    { Icon = Glyph(0xE83A), Data = p };

    // The clipboard history: each entry's first line, read once (a file never changes once written).
    sealed record ClipFile(string File);
    readonly Dictionary<string, string> clipTexts = [];
    List<Result>? clipList;
    DateTime clipStamp;

    List<Result> ClipResults()
    {
        var stamp = Directory.Exists(ClipboardHistory.Dir) ? Directory.GetLastWriteTimeUtc(ClipboardHistory.Dir) : default;
        if (clipList != null && stamp == clipStamp) return clipList;
        clipStamp = stamp;
        var files = ClipboardHistory.Entries();
        foreach (var gone in clipTexts.Keys.Except(files).ToList()) clipTexts.Remove(gone);
        return clipList = [.. files.Select(f =>
        {
            if (!clipTexts.TryGetValue(f, out var line)) clipTexts[f] = line = OneLine(f, 90);
            return new Result("clip:" + Path.GetFileName(f), STag.Clipboard, line, "Copied " + Ago(ClipboardHistory.When(f)),
                Primary: () => { Paste(f); return Task.CompletedTask; }) { Icon = Glyph(ClipEntries.IsImage(f) ? 0xF02E9 : 0xF014C), Data = new ClipFile(f) };
        })];
    }

    // Themes, read once per showing (each is a file).
    sealed record ThemeRef(string File, Theme Theme);
    List<Result>? themeList;
    string themeListFor = "";

    List<Result> ThemeResults()
    {
        if (themeList != null && themeListFor == themeName) return themeList;
        themeListFor = themeName;
        return themeList = [.. MenuModel.Themes().Select(t => new Result("theme:" + t.File, STag.Theme, t.Theme.Name,
            (t.File == themeName ? "Current · " : "") + (t.Theme.Mode == "light" ? "Light" : "Dark"), t.File + " " + t.Theme.Mode,
            Primary: () =>
            {
                RunPowerShell($"-File \"{Path.Combine(MenuModel.ThemesDir, "set-theme.ps1")}\" {t.File}", script: false);
                ApplyTheme(t.File); // repaint at once; set-theme.ps1 does the rest
                themeList = null;
                Render();
                return Task.CompletedTask;
            })
        { Data = new ThemeRef(t.File, t.Theme) })];
    }

    // Files, from fff (FileSearch), off the UI thread: typing never waits for them (guardrail 11).
    // What's returned until the new answer lands is the last one; the merger keeps those that
    // still match, and the answer is merged in below the selected row (guardrail 6).
    string filesFor = "";
    List<Result> fileResults = [];
    int fileToken;

    List<Result> FileResults(string text)
    {
        if (text == filesFor) return fileResults;
        filesFor = text;
        int token = ++fileToken;
        Task.Run(() => FileSearch.Search(text, limit: 30)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (token != fileToken || !t.IsCompletedSuccessfully) return;
            fileResults = [.. t.Result.Select(FromFile)];
            SourceUpdated();
        }));
        return fileResults;
    }

    Result FromFile(FileSearch.Hit hit) => new("file:" + hit.Path, STag.File, hit.Name,
        hit.RelativeDir.Length > 0 ? $"{hit.RootLabel} › {hit.RelativeDir.Replace("\\", " › ")}" : hit.RootLabel, null,
        Primary: () => { OpenFile(hit, SearchText, reveal: false); return Task.CompletedTask; })
    {
        Icon = Glyph(hit.IsDir ? 0xF024B : 0xF0214), IconKey = hit.IsDir ? null : "path:" + hit.Path, Data = hit,
    };

    // A new showing reads the files-backed sources again.
    void ForgetSources()
    {
        pinList = null;
        themeList = null;
        clipList = null;
        filesFor = "";
        fileResults = [];
        appsFor = null;
    }

    // --- the preview ------------------------------------------------------------------------------
    // The selected result, beside the list: a file's or a copy's own preview; for the rest its icon
    // (an app's real one, a theme's picture) big, its name, where it is, facts, what Enter does.
    void ShowResultPreview(Result r)
    {
        switch (r.Data)
        {
            case FileSearch.Hit hit: ShowFilePreview(hit); return;
            case ClipFile clip: ShowClipPreviewOf(clip.File); return;
        }
        int token = ++previewToken;
        var color = PaintColor(r.Tag);
        Preview.Visibility = Visibility.Visible;
        Preview.Background = Brush(Hex(theme.Surface), 0xB0);
        Preview.BorderBrush = Brush(Hex(theme.Border), 0x80);
        PreviewArt.Visibility = Visibility.Visible;
        PreviewArt.Height = 150;
        // One soft glow in the tag's colour behind the icon (guardrail 12).
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x30, color.R, color.G, color.B), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 1 });
        PreviewArt.Background = glow;
        PreviewArt.Child = new TextBlock
        {
            Text = r.Icon ?? Glyph(TagLook.Glyph(r.Tag)), FontFamily = iconFont, FontSize = 60, Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (r.IconKey != null)
            AppIcons.Get(r.IconKey, 72, IconScaleNow, (image, units) =>
            {
                if (token != previewToken) return;
                PreviewArt.Child = new Image { Source = image, Width = units, Height = units, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            });
        if (r.Data is ThemeRef t)
        {
            // The theme as one bar, its palette blending (guardrail 12), over its first picture.
            var bar = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
            var stops = t.Theme.Gradient.Length > 0 ? t.Theme.Gradient : [t.Theme.Accent];
            for (int i = 0; i < stops.Length; i++) bar.GradientStops.Add(new GradientStop { Color = Hex(stops[i]), Offset = stops.Length == 1 ? 0 : i / (double)(stops.Length - 1) });
            var art = new Grid();
            art.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = bar, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14) });
            PreviewArt.Background = Brush(t.Theme.Background);
            PreviewArt.Child = art;
            ThemeThumbs.Get(t.File, t.Theme, picture =>
            {
                if (token != previewToken) return;
                PreviewArt.Background = new ImageBrush { ImageSource = picture, Stretch = Stretch.UniformToFill };
            });
        }

        PreviewName.Text = r.Name;
        PreviewName.Foreground = Brush(theme.Foreground);
        PreviewPath.Children.Clear();
        var parts = r.Detail.Split([" › ", " · "], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) PreviewPath.Children.Add(new TextBlock { Text = "›", FontSize = 12, Foreground = Brush(theme.Comment) });
            PreviewPath.Children.Add(new TextBlock { Text = parts[i], FontFamily = textFont, FontSize = 12, Foreground = Brush(i == 0 ? theme.Accent : theme.Muted) });
        }

        PreviewFacts.Children.Clear();
        PreviewFacts.Children.Add(Pill(TagLook.Word(r.Tag) is { Length: > 0 } w ? w : "MORE", ColorHex(color)));
        if (r.Data is SettingEntry e)
        {
            var value = Settings.Peek(e);
            if (value != null && e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider)
                PreviewFacts.Children.Add(Pill(Settings.LabelNow(e, value), value == "on" ? theme.Green : theme.Muted));
        }
        if (r.Admin) PreviewFacts.Children.Add(Pill("needs admin", theme.Red));
        if (r.Key != null) PreviewFacts.Children.Add(Pill(r.Key, theme.Muted));

        PreviewText.FontFamily = textFont;
        PreviewText.Foreground = Brush(theme.Muted);
        PreviewText.TextWrapping = TextWrapping.Wrap;
        PreviewTextBox.Background = Brush(Hex(theme.Background), 0x90);
        PreviewText.Text = PreviewWords(r);
        PreviewTextBox.Visibility = PreviewText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // What the preview says under the facts: what Enter and Ctrl+K do, and for a command its
    // whole summary.
    string PreviewWords(Result r)
    {
        var lines = new List<string>();
        switch (r.Data)
        {
            case OmawinCommand c:
                lines.Add(c.Line);
                lines.Add(c.Summary);
                lines.Add(CommandList.RunsIn(c) == RunIn.Terminal ? "Enter runs it in a terminal, so you see what it says." : "Enter runs it, as its key would.");
                break;
            case MenuEntry m when m.Confirm:
                lines.Add("Enter asks first.");
                break;
            case SettingEntry s:
                lines.Add(s.Kind switch
                {
                    SettingKind.Switch => "Enter switches it; Ctrl+Z puts it back.",
                    SettingKind.Choice or SettingKind.Slider => "← and → choose; Ctrl+Z puts it back.",
                    SettingKind.Link => "Windows doesn't let this change from outside: Enter opens its page in Settings.",
                    _ => "Enter does it.",
                });
                if (s.Admin) lines.Add("It needs admin: it waits with the others for one prompt.");
                break;
            case IndexedSetting:
                lines.Add("Opens Windows' own page for it.");
                break;
        }
        if (r.Tag is not (STag.More or STag.Package or STag.Web or STag.Calculator))
            lines.Add(!Pins.CanPin(r.Tag) ? "Ctrl+K: more actions." : PinList.Contains(r.Id) ? "Ctrl+K: more actions. Ctrl+P: unpin it from the home." : "Ctrl+K: more actions. Ctrl+P: pin it to the home.");
        return string.Join("\n\n", lines);
    }

    static string ColorHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // --- the home -------------------------------------------------------------------------------
    // The bento (Search/Home.cs places it; this fills it). Short names for the quick switches.
    static readonly Dictionary<string, string> SwitchNames = new()
    {
        ["connections.wifi"] = "Wi-Fi", ["connections.bluetooth"] = "Bluetooth", ["look.dark-apps"] = "Dark mode", ["privacy.microphone"] = "Microphone",
    };

    TileLayout OneHome()
    {
        var layout = new TileLayout();
        var byKey = new Dictionary<string, LaunchItem>();
        foreach (var i in launchItems) byKey.TryAdd(i.Key, i);
        var history = History.Load();
        var byUse = launchItems.Where(i => history.Items.ContainsKey(i.Key)).OrderByDescending(i => history.Boost(i.Key, "")).Select(i => i.Key);
        var here = new HashSet<string>(HomePlan.Switches.Where(id => Settings.Find(id) != null));
        var rows = HomePlan.Arrange([.. byKey.Values.Select(i => new HomeItem(i.Key, i.BuiltIn, i.IsApp))], byUse, here, PinList);
        foreach (var row in rows)
        {
            var specs = row.Slots.Select(s => HomeTile(s, byKey)).OfType<TileSpec>().ToList();
            if (specs.Count == 0) continue;
            if (row.Top) layout.Top(specs[0], specs.Skip(1));
            else layout.Flow(specs, row.Title, row.Slots[0].Style == HomeStyle.Switch ? SwitchRowDip : TileRowDip);
        }
        return layout;
    }

    const double SwitchRowDip = 56;

    TileSpec? HomeTile(HomeSlot slot, Dictionary<string, LaunchItem> byKey)
    {
        var style = slot.Style switch { HomeStyle.Hero => "hero", HomeStyle.Wide => "wide", HomeStyle.Switch => "switch", _ => "tile" };
        if (slot.Style == HomeStyle.Pin) return PinTile(slot.Key[Pins.TileKey.Length..], byKey);
        if (slot.Key == HomePlan.Browse)
            return new TileSpec
            {
                Key = slot.Key, Style = style, ColSpan = slot.ColSpan, Icon = Glyph(0xF035C), Label = "Browse", Color = Hex(theme.Purple),
                Sub = string.Join(" · ", MenuModel.Children(entries, "").Select(e => e.Label).Take(5)),
                Run = () => SetFilter(STag.Command, ""),
            };
        if (slot.Key == HomePlan.Theme)
            return new TileSpec
            {
                Key = slot.Key, Style = style, ColSpan = slot.ColSpan, Icon = Glyph(0xF03D8), Label = theme.Name, Color = Hex(theme.Accent),
                Sub = "Themes", Swatch = theme, Run = () => SetFilter(STag.Theme, ""),
            };
        if (slot.Key.StartsWith(HomePlan.SettingPrefix))
        {
            if (Settings.Find(slot.Key[HomePlan.SettingPrefix.Length..]) is not { } e) return null;
            HookSettings();
            var value = Settings.Peek(e);
            bool on = value == "on";
            return new TileSpec
            {
                Key = slot.Key, Style = style, Icon = Catalog.IconOf(e), Label = SwitchNames.GetValueOrDefault(e.Id, e.Name),
                Sub = value == null ? "…" : on ? "On" : "Off", Current = on, Color = Hex(on ? theme.Green : theme.Comment),
                Run = () => DoSetting(() => Settings.Toggle(e), e, ""),
            };
        }
        if (!byKey.TryGetValue(slot.Key, out var item)) return null;
        var spec = new TileSpec
        {
            Key = item.Key, BuiltIn = item.BuiltIn, Style = style, ColSpan = slot.ColSpan, Icon = item.Icon, Label = item.Name,
            Color = KindColor(item), AppId = item.IconKey, Run = () => Launch(item),
        };
        if (slot.Style == HomeStyle.Hero && item.Command.Trim() == "omawin.exe launch terminal") { spec.Hint = "Win+Enter"; spec.Sub = "in the folder you're working in"; }
        if (item.BuiltIn == "projects") spec.Sub = ProjectsSub();
        if (item.BuiltIn == "clipboard") { spec.Sub = ClipSub(); spec.SubMono = true; }
        return spec;
    }

    // --- pins (Ctrl+P; Search/Pins.cs) -----------------------------------------------------------
    // Your pinned results' ids, read once a showing; %LOCALAPPDATA%\launcher\pins.txt.
    static string PinsFile => Path.Combine(MenuModel.StateDir, "pins.txt");
    List<string>? pinList;

    List<string> PinList
    {
        get
        {
            if (pinList != null) return pinList;
            try { return pinList = File.Exists(PinsFile) ? Pins.Parse(File.ReadAllLines(PinsFile)) : []; }
            catch (IOException) { return pinList = []; }
        }
    }

    void SavePins(List<string> list)
    {
        pinList = list;
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            File.WriteAllLines(PinsFile, list);
        }
        catch (IOException ex) { App.Log(ex); }
    }

    // The home: nothing typed, no filter, no view open.
    bool OnHome => launching && page == null && searchFilter == null && current.Length == 0 && !pickingTheme && Input.Text.Length == 0;

    // Ctrl+P (or Ctrl+K's Pin) on a result: on the home's Pinned row, or off it. The list stays as
    // it was, the same row selected; the footer says what happened.
    void TogglePin(Result r)
    {
        var (list, change) = Pins.Toggle(PinList, r.Id, r.Tag);
        if (change is PinChange.Pinned or PinChange.Unpinned) SavePins(list);
        if (actionsFor != null) CloseActions();
        else { resultsUpdate = true; Render(); }
        FooterLeft.Text = Pins.Say(change, r.Name);
    }

    // A pin's tile: an app as its own tile, anything else as its result (icon, name, tag colour);
    // Enter does what Enter on the result does. Gone (a file deleted, an app uninstalled): no tile,
    // and the pin waits in case it comes back.
    TileSpec? PinTile(string id, Dictionary<string, LaunchItem> byKey)
    {
        var key = Pins.TileKey + id;
        if (id.StartsWith(ResultIds.App, StringComparison.Ordinal) && byKey.TryGetValue(id[ResultIds.App.Length..], out var item))
            return new TileSpec { Key = key, BuiltIn = item.BuiltIn, Icon = item.Icon, Label = item.Name, Color = KindColor(item), AppId = item.IconKey, Run = () => Launch(item) };
        if (PinnedResult(id) is not { } r) return null;
        var spec = new TileSpec
        {
            Key = key, Icon = r.Icon ?? Glyph(TagLook.Glyph(r.Tag)), Label = r.Name, Color = PaintColor(r.Tag), AppId = r.IconKey,
            Sub = TagLook.Word(r.Tag).ToLowerInvariant(), Run = () => Activate(r),
        };
        if (r.Data is SettingEntry { Kind: SettingKind.Switch or SettingKind.Choice or SettingKind.Slider } e)
        {
            HookSettings();
            var value = Settings.Peek(e);
            spec.Sub = value == null ? "…" : Settings.LabelNow(e, value);
            spec.Current = value == "on";
            if (spec.Current) spec.Color = Hex(theme.Green);
        }
        return spec;
    }

    // A pinned id back to its result, from the source that made it.
    Result? PinnedResult(string id)
    {
        string After(string prefix) => id[prefix.Length..];
        try
        {
            if (id.StartsWith("setting:", StringComparison.Ordinal))
                return Settings.Find(After("setting:")) is { } e ? FromSetting(e) : null;
            if (id.StartsWith("page:", StringComparison.Ordinal)) // one of Windows' own pages: found again by its name
                return SettingResults(id[(id.LastIndexOf('#') + 1)..], pending: false).FirstOrDefault(r => r.Id == id);
            if (id.StartsWith("cmd:", StringComparison.Ordinal) || id.StartsWith("menu:", StringComparison.Ordinal))
                return CommandResults("").FirstOrDefault(r => r.Id == id);
            if (id.StartsWith("theme:", StringComparison.Ordinal))
                return ThemeResults().FirstOrDefault(r => r.Id == id);
            if (id.StartsWith("project:", StringComparison.Ordinal))
            {
                var path = After("project:");
                if (ProjectResults().FirstOrDefault(r => r.Id == id) is { } known) return known;
                bool linux = path.StartsWith('/'); // a WSL project, before the projects are found again
                return linux || Directory.Exists(path) ? FromProject(new Project(linux, path, 0, false)) : null;
            }
            if (id.StartsWith("file:", StringComparison.Ordinal))
            {
                var path = After("file:");
                bool dir = !FileSearch.IsLinux(path) && Directory.Exists(path);
                if (!dir && !File.Exists(path) && !FileSearch.IsLinux(path)) return null;
                var name = Path.GetFileName(path.TrimEnd('\\', '/'));
                return FromFile(new FileSearch.Hit(path, name.Length > 0 ? name : path, Path.GetDirectoryName(path) ?? "", "", dir, 0, null, null, false, 0));
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return null;
    }

    // A quick switch: its icon, name and state; On glows in green with a hairline ring, Off is
    // outlined and dim (guardrail 12).
    void BuildSwitchTile(TileView v, TileSpec s, TextBlock label)
    {
        var body = new Grid { Padding = new Thickness(12, 0, 12, 0), ColumnSpacing = 10 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        v.IconTile.VerticalAlignment = VerticalAlignment.Center;
        v.IconTile.BorderThickness = new Thickness(1);
        v.IconTile.BorderBrush = Brush(s.Color, s.Current ? (byte)0xA0 : (byte)0x50);
        body.Children.Add(v.IconTile);
        label.FontSize = 12.5;
        label.FontWeight = FontWeights.SemiBold;
        var state = new TextBlock
        {
            Text = (s.Sub ?? "").ToUpperInvariant(), FontFamily = monoFont, FontSize = 10, CharacterSpacing = 120,
            Foreground = s.Current ? new SolidColorBrush(s.Color) : Brush(theme.Comment),
        };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { label, state } };
        Grid.SetColumn(text, 1);
        body.Children.Add(text);
        v.Inner.Children.Add(body);
    }
}
