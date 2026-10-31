
namespace SystemMenu;

// Windows' settings in the menu (roadmap 1B.3). Typing in the system menu finds its own rows and
// every Windows setting (the catalog's switches first, then Windows' own pages); the launcher
// shows the top few under its apps; Setup › Windows lists them by area for browsing. A row shows
// its value: a switch On or Off, a choice or slider ‹ its value ›, a page ↗.
//
//   Enter              flips a switch, steps a choice, opens a page (and the menu goes)
//   Space              the same, while nothing is typed (once you type, Space is a space)
//   ← / →              a choice or slider: the previous / next value
//   Ctrl+Z             puts the last change back (settings-history.tsv)
//
// Every change goes through WinSettings.Store, off the UI thread (radios and WMI block); the row
// redraws when the value lands (Store.Changed), so a change made in Windows' own Settings shows too.
// Settings that need admin are queued: a row at the top says how many, and choosing it lists
// them and asks once (roadmap 2.9).
public sealed partial class MainWindow
{
    readonly Dictionary<int, SettingEntry> settingRows = []; // row index -> its setting (cleared by Render)
    string? settingsArea;          // showing one area's settings (Setup › Windows › <area>)
    string settingsAreaFrom = "";  // the menu row that opened it (Back returns there)
    bool confirmingAdmin;          // listing the queued admin changes, asking once
    (string Text, string Query)? settingNote; // what the last change did, until the search changes
    bool settingsHooked, settingsRedrawQueued;

    SettingStore Settings => WinSettings.Store;

    // Once: hear changes (ours, another program's, Windows'), and WM_SETTINGCHANGE on this window.
    void HookSettings()
    {
        if (settingsHooked) return;
        settingsHooked = true;
        SettingWatch.HookWindow(hwnd);
        Settings.Changed += _ => RedrawSettingsSoon();
        ExplorerRestart.Changed += RedrawSettingsSoon;
    }

    // Many values can land at once (a page of rows read in the background): one redraw for them.
    void RedrawSettingsSoon()
    {
        if (settingsRedrawQueued) return;
        settingsRedrawQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            settingsRedrawQueued = false;
            if (appWindow.IsVisible && (settingRows.Count > 0 || settingsArea != null || confirmingAdmin)) Render();
        });
    }

    SettingEntry? SelectedSetting => !tileMode && settingRows.TryGetValue(selected, out var e) ? e : null;

    // --- the views ---------------------------------------------------------------------------
    // Setup › Windows › <area>: its settings, narrowed by what's typed. True when it drew.
    bool RenderSettingsViews(string query)
    {
        if (confirmingAdmin) { RenderAdminConfirm(); return true; }
        if (settingsArea is not { } area) return false;
        HookSettings();
        SetCrumbs(settingsAreaFrom, null);
        var label = Catalog.AreaLabel(area);
        Placeholder.Text = $"Search {label}…";
        AddPendingRows();
        var shown = Settings.Entries().Where(e => e.Area == area).ToList();
        if (query.Length > 0)
        {
            var hits = SettingSearch.Rank(shown, [], query, null, 200);
            foreach (var hit in hits) AddSettingRow(hit.Entry!, query, hit.Positions, inArea: true);
        }
        else foreach (var e in shown) AddSettingRow(e, "", null, inArea: true);
        if (rows.Count == 0) AddRow(char.ConvertFromUtf32(0xF0156), "No matches", Hex(theme.Comment), () => { }, dim: true);
        if (query.Length == 0 && Settings.History.LastUndoable() is { } last && Settings.Find(last.Id) is { } undoable)
            AddRow(char.ConvertFromUtf32(0xF054C), $"Undo: {undoable.Name} back to {Settings.LabelNow(undoable, last.Before)}", Hex(theme.Yellow),
                () => DoSetting(() => Settings.UndoLast(), null, query), Tag.Text, "ctrl z");
        FooterLeft.Text = NoteFor(query) ?? $"{shown.Count} settings · {label}";
        SetKeys(("↵", "change"), ("←→", "choose"), ("ctrl z", "undo"), ("esc", "back"));
        return true;
    }

    // The search's settings, under the menu's own rows (or the launcher's apps). Returns how many.
    int AddSettingResults(string query, int limit)
    {
        if (query.Length < 2) return 0;
        HookSettings();
        int before = rows.Count;
        if (rows.Count == 0) AddPendingRows();
        foreach (var hit in WinSettings.Search(query, limit))
        {
            if (hit.Entry is { } e) AddSettingRow(e, query, hit.Positions, inArea: false, hit);
            else if (hit.Page is { } p)
                AddRow(char.ConvertFromUtf32(0xF0493), p.Name, Hex(theme.Muted), () =>
                {
                    WinSettings.Remember(hit, query);
                    HideMenu();
                    WinSettings.Open(p);
                }, Tag.Text, "↗", sub: p.Page, highlight: hit.Positions);
        }
        if (NoteFor(query) is { } note) FooterLeft.Text = note;
        return rows.Count - before;
    }

    // Admin changes waiting, and an Explorer restart a change needs: pinned at the top.
    void AddPendingRows()
    {
        int queued = Settings.AdminQueue.Count;
        if (queued > 0)
            AddRow(char.ConvertFromUtf32(0xF0498), $"Apply {queued} admin change{(queued == 1 ? "" : "s")}", Hex(theme.Red),
                () => { confirmingAdmin = true; selected = 0; Render(); }, Tag.Text, "one prompt");
        if (ExplorerRestart.Pending)
            AddRow(char.ConvertFromUtf32(0xF0450), "Restart Explorer to apply", Hex(theme.Yellow),
                () => Task.Run(ExplorerRestart.Now), Tag.Text, "taskbar blinks once");
    }

    void AddSettingRow(SettingEntry e, string query, List<int>? positions, bool inArea, SettingHit? hit = null)
    {
        settingRows[rows.Count] = e;
        bool hasValue = e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider;
        var value = hasValue ? Settings.Peek(e) : null;
        var queued = e.Admin ? Settings.AdminQueue.Items().FirstOrDefault(i => i.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)).Value : null;
        bool reading = hasValue && value == null && !Settings.TryCached(e, out _);
        var notes = new List<string>();
        if (!inArea) notes.Add(Catalog.AreaLabel(e.Area));
        if (e.Admin) notes.Add(queued != null ? $"admin: {Settings.LabelNow(e, queued)} waiting" : "admin");
        if (e.Takes.HasFlag(Takes.Restart)) notes.Add("after a restart");
        else if (e.Takes.HasFlag(Takes.SignOut)) notes.Add("after sign-out");
        else if (e.How.Flags.Contains("restart-explorer")) notes.Add("restarts Explorer");
        var color = Hex(e.Kind is SettingKind.Link or SettingKind.Action ? theme.Cyan : theme.Accent);
        Action run = () =>
        {
            if (hit != null) WinSettings.Remember(hit, query);
            if (e.Kind is SettingKind.Link or SettingKind.Action) HideMenu();
            DoSetting(() => Settings.Toggle(e), e, query);
        };
        var sub = notes.Count > 0 ? string.Join(" · ", notes) : null;
        switch (e.Kind)
        {
            case SettingKind.Switch when value == "on":
                AddRow(Catalog.IconOf(e), e.Name, color, run, Tag.Installed, "On", sub: sub, highlight: positions); break;
            case SettingKind.Switch:
                AddRow(Catalog.IconOf(e), e.Name, color, run, Tag.Text, reading ? "…" : value == null ? "?" : "Off", sub: sub, highlight: positions); break;
            case SettingKind.Choice or SettingKind.Slider:
                AddRow(Catalog.IconOf(e), e.Name, color, run, Tag.Text, reading ? "…" : $"‹ {Settings.LabelNow(e, value)} ›", sub: sub, highlight: positions); break;
            default:
                AddRow(Catalog.IconOf(e), e.Name, color, run, Tag.Text, e.Kind == SettingKind.Link ? "↗" : "run", sub: sub, highlight: positions); break;
        }
    }

    // The queued admin changes, said in words, then one question (roadmap 2.9).
    void RenderAdminConfirm()
    {
        SetCrumbs(settingsAreaFrom, "Admin changes");
        Placeholder.Text = "These need admin";
        var lines = Settings.DescribeAdmin();
        AddRow(char.ConvertFromUtf32(0xF0498), $"Yes, apply {(lines.Count == 1 ? "it" : $"all {lines.Count}")} (one prompt)", Hex(theme.Red), () =>
        {
            confirmingAdmin = false;
            DoSetting(() => Settings.ApplyAdminAsync().GetAwaiter().GetResult(), null, Input.Text.Trim());
        });
        AddRow(char.ConvertFromUtf32(0xF0156), "No, not now (they stay queued)", Hex(theme.Comment), () => { confirmingAdmin = false; Render(); });
        AddRow(char.ConvertFromUtf32(0xF0A7A), "Forget them", Hex(theme.Comment), () => { Settings.AdminQueue.Clear(); confirmingAdmin = false; Render(); });
        foreach (var line in lines) AddRow(char.ConvertFromUtf32(0xF0493), line, Hex(theme.Muted), () => { }, dim: true);
        FooterLeft.Text = "Windows asks for an administrator once, for all of them";
        SetKeys(("↵", "choose"), ("esc", "back"));
    }

    // --- doing --------------------------------------------------------------------------------
    // Off the UI thread; the note says what happened, and the row redraws with the new value.
    void DoSetting(Func<SettingOutcome> change, SettingEntry? e, string query)
    {
        Task.Run(() =>
        {
            SettingOutcome r;
            try { r = change(); }
            catch (Exception ex) { App.Log(ex); r = new(false, ex.Message); }
            DispatcherQueue.TryEnqueue(() =>
            {
                settingNote = (r.Message, query);
                if (appWindow.IsVisible) Render();
                else Snip.Say(r.Message); // a link or action closed the menu: the note at the bottom of the screen
            });
        });
    }

    string? NoteFor(string query) => settingNote is { } n && n.Query == query ? n.Text : null;

    void StepSetting(SettingEntry e, int by) => DoSetting(() => Settings.Step(e, by), e, Input.Text.Trim());

    void UndoSetting() => DoSetting(() => Settings.UndoLast(), null, Input.Text.Trim());

    // A menu row "settings <area>" (Setup › Windows), "settings undo" or "settings admin".
    void OpenSettings(MenuEntry from)
    {
        HookSettings();
        settingsAreaFrom = from.Parent;
        switch (from.Argument)
        {
            case "undo": UndoSetting(); return;
            case "admin": confirmingAdmin = true; break;
            case var area when Catalog.Areas.Contains(area):
                settingsArea = area;
                // Power settings have no change event: read them fresh each time the area opens.
                Settings.Forget(e => e.Area == area && e.How.Verb == "powercfg");
                break;
            default: return;
        }
        // Back from here is the submenu the row is in, even when it was found by searching.
        if (!launching) current = from.Parent;
        Input.Text = "";
        selected = 0;
        Render();
        Slide(1);
    }

    // Esc / Backspace / ← from a settings view. True when it went back.
    bool LeaveSettings()
    {
        if (confirmingAdmin) { confirmingAdmin = false; Render(); Slide(-1); return true; }
        if (settingsArea == null) return false;
        var area = settingsArea;
        settingsArea = null;
        selectKey = entries.Values.FirstOrDefault(m => m.Kind == "settings" && m.Argument == area)?.Id;
        selected = Math.Max(0, MenuModel.Children(entries, current).FindIndex(c => c.Id == selectKey));
        Render();
        Slide(-1);
        return true;
    }

    // The keys, before the window's own (OnKey). True when handled.
    bool SettingKey(Windows.System.VirtualKey key, bool ctrl, bool shift)
    {
        if (finding || (launching && page != null)) return false;
        switch (key)
        {
            case Windows.System.VirtualKey.Z when ctrl:
                UndoSetting(); return true;
            case Windows.System.VirtualKey.Space when !ctrl && Input.Text.Length == 0 && SelectedSetting != null:
                RunSelected(); return true;
            case Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Left when !ctrl && SelectedSetting is { Kind: SettingKind.Choice or SettingKind.Slider } e:
                StepSetting(e, key == Windows.System.VirtualKey.Right ? 1 : -1); return true;
            default:
                return false;
        }
    }
}
