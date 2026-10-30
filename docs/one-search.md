# One Search (design)

**Status: proposed** ([roadmap.md](roadmap.md), phase 1C). One search bar for everything omawin can find or do, every result tagged with what it is, filters by tag, and a bento home when you haven't typed yet. It replaces the separate launcher home, system menu tree and pages as places you have to know about; they become filters and views of the same window. **Win+Space is the one key for all of it; Win+Alt+Space is retired** (guardrail 10). Clickable mockup: [omawin One Search](https://claude.ai/artifact/FCAA1kuXE7H6BjjfVzrN8z) (private link); **its full source is in the repo: [`docs/design/one-search/mockup.dc.html`](design/one-search/mockup.dc.html)** (how to read it: [README](design/one-search/README.md)).

## What it is

- **One window, one bar.** Type anything: an app, a setting, a file, a command, an open window, a project, something you copied, a theme, a sum.
- **Every result has a tag** (a coloured label and icon): Apps, Settings, Commands, Files, Windows, Projects, Clipboard, Themes; plus Calculator, Packages and Web as special rows.
- **Filter by tag** without leaving the bar: Tab moves through the filter chips; typing a filter's name and Tab locks it ("set" Tab → Settings); `/` searches files and `>` commands; Backspace on an empty bar removes the filter. The active filter shows as a token in the bar, so you always know what you're searching.
- **Empty bar: the bento home.** Big Terminal tile, Projects and Clipboard showing what's in them, quick switches (Wi-Fi, Bluetooth, Dark mode, Microphone), the theme, your most used apps. For browsing and one-key habits.
- **Typing: a ranked list with a preview.** Results on the left, the selected one's details and its main action on the right.
- **Settings act in place.** A switch shows On / Off and Enter flips it; a choice steps through its values; Ctrl+Z undoes (phase 1B).
- **Ctrl+K: actions for the selected result**, the same idea for every tag (open, run as admin, show in folder, copy path, terminal here, who has it open, pin to home, …).

## Guardrails (the decisions that keep it A+)

1. **Bento for the empty state, a list for results.** Mixed-size tiles are great for a fixed home you learn by position, and bad for ranked results that change on every key: the eye can't scan them in order. So tiles never show search results.
2. **The home doesn't move.** Tiles keep their places (muscle memory); only their contents update (the latest project, the last copy). No "suggested for you" shuffling, nothing promoted.
3. **Few tags, always the same.** Eight kinds at most in the chip row, each with a fixed colour **and** an icon and a word (never colour alone). Rare kinds (Packages, Web, Calculator) appear as rows, not chips. A plugin's results join an existing tag or bring one tag of their own, never several.
4. **"All" is capped per tag.** Without a filter, each tag shows its best few (apps 4, settings 4, commands 3, files 3, the rest 2), and a "Show all 12 Settings" row jumps into that filter. One tag can't flood the list (files would, by sheer number).
5. **Ranking is explainable.** Name match first (start of name, start of a word, then letters in order), then what you've picked for this query before, then how often you use it, then a small prior by tag: for one or two letters, apps and commands come before files. Files need three letters or `/`. The same query gives the same order until your habits change.
6. **The selected row never jumps.** Slow sources (files, packages) arrive later and are added below what's on screen, so Enter never opens something that slid under the cursor.
7. **Enter does the obvious thing; nothing happens without a key.** Enter opens, switches or runs; anything that removes or needs admin says so first. Space types a space (no surprise toggles while you type). The web is never searched as you type: "Search the web for …" is a row you pick, last.
8. **Honest labels.** A setting Windows won't let us change from outside is a link that says *Opens Settings*, never a fake switch.
9. **Typing is never lost.** Changing the filter keeps what you typed; Esc steps back one thing at a time (actions, then the text, then the filter, then the window).
10. **One key for everything: Win+Space.** Decided 2026-10-30 by the maintainer: **Win+Alt+Space (the system menu) is retired**; everything it did is in Win+Space (settings in the Settings filter, its actions in Commands, its sections in the Browse view and on the home). The other keys stay and open the same window on a filter: Win+F on Files, Win+Shift+V on Clipboard. Retiring Win+Alt+Space, step by step: for one release it still opens One Search (on All) and the toast says *Win+Space does this now*; then the binding is removed from `glazewm/config.yaml` by a migration, the key is free, and `AGENTS.md`'s key list and `manual/05-system-menu.md` are updated in the same change. `omawin menu` stays as a command (it opens One Search on the Browse view), so scripts and the dock keep working.
11. **Speed is a feature.** Under 50 ms from key to window, under 16 ms per keystroke (phase 1); typing never waits on a source.
12. **One look: futuristic and minimal.** Hairline borders (white at ~5%) instead of boxes; a translucent window with one soft glow in the theme's accent; light type (a thin, large search text; small uppercase monospace labels with wide tracking for tags, filters and keys); outlined icons and outlined, glowing states (an *On* switch glows in green, the selected row is a faint accent wash with a hairline ring) rather than filled blocks; filters as underlined tabs, not pills; one row height and one type scale; the theme's palette and the shell's motion tokens; crisp icons (1.8). **The theme shows as one bar** whose colours blend into each other (its palette as a single gradient), on the home's Theme tile and in a theme's preview. No emoji, no decoration that isn't information.

## What happens to today's pieces

| Today | In One Search |
|---|---|
| The launcher's tile home (Win+Space) | the bento home, kept and refined |
| The system menu's tile home and tree (Win+Alt+Space, **retired**) | the Settings and Commands filters; the tree's sections become a *Browse* view under Commands (Install, Update, Style, System…) for discovery, still searchable; a *Menu* tile on the home opens it |
| Launcher pages (Projects, Clipboard, Localhost, Audio, Quick settings, Packages, About, Shortcuts) | filters (Projects, Clipboard) or views opened from a result (Audio, Quick settings, Packages, Localhost) |
| The file finder (Win+F) | the Files filter, with its preview, chips for places, and content search (`#`, phase 6) |
| The theme picker | the Themes filter, as a grid of pictures (themes are pictures; a grid is right there) |
| Settings search (phase 1B) | the Settings filter |

## Open questions (to decide with the maintainer)

- **Pinned items on the home:** which tiles are fixed by omawin and which are yours to pin (Ctrl+P on any result)?
- **Grid views inside filters:** themes yes; apps as a grid when filtered to Apps with an empty query, or always a list?
- **Window size:** fixed (as today) or growing with results?
- **Previews on narrow screens:** hide the preview pane below a width, or show it under the list?

## Building it

Mostly a re-organisation of the menu app: `MainWindow.xaml.cs`'s modes (launcher, system menu, finder, pages) become filters over one result model. The pieces, with example code (C# 12, AOT-safe: no reflection; names are suggestions).

### 1. The result model

```csharp
namespace SystemMenu.Search;

// What a result is. Kept small: everything a row, the preview and the actions need.
public enum Tag { App, Setting, Command, File, Window, Project, Clipboard, Theme, Calculator, Package, Web, More }

public enum SettingKind { None, Switch, Choice, Link, Page }

public sealed record Result(
    string Id,                 // stable across queries (for history and stable ordering)
    Tag Tag,
    string Name,
    string Detail,             // the dimmed second line
    string? Keywords = null,   // matched when the name isn't
    string? Key = null,        // a key binding to show ("Win Shift S")
    SettingKind Setting = SettingKind.None,
    Func<bool>? IsOn = null,   // a switch's state, read lazily (only for rows on screen)
    Func<string>? Value = null,// a choice's current value
    Func<Task>? Primary = null,// what Enter does
    IReadOnlyList<ResultAction>? Actions = null) // what Ctrl+K lists
{
    public int Uses { get; init; }          // how often it's been picked (history)
}

public sealed record ResultAction(string Label, string? Key, Func<Task> Run);
```

### 2. Sources, one per tag

```csharp
// A source answers a query with candidates; it never ranks across sources (the merger does).
public interface ISearchSource
{
    Tag Tag { get; }
    // Fast sources (in memory: apps, settings, commands, windows, themes) answer synchronously.
    // Slow ones (files, packages) return what they have now and call Updated when more arrives.
    IReadOnlyList<Result> Query(string query, CancellationToken cancel);
    event Action? Updated;
    int MinQueryLength => 0;   // files: 3, unless the Files filter or "/" is active
}
```

Sources to write, from what exists: `AppSource` (`Launcher.cs`, `AppsFolder.cs`, `AppIcons.cs`), `SettingSource` (phase 1B's catalog), `CommandSource` (`bin/commands.txt` and `launcher/menu.txt`'s actions), `FileSource` (`FileSearch.cs`, fff), `WindowSource` (GlazeWM's IPC, `Bar/GlazeWm.cs`), `ProjectSource` and `ClipboardSource` (`Pages.cs`, `Background.cs`), `ThemeSource` (`MenuModel`, `ThemeThumbs.cs`). Plugins add sources through `launcher-provider` ([plugins.md](plugins.md)), one tag each.

### 3. The query: filter token and prefixes

```csharp
public readonly record struct ParsedQuery(Tag? Filter, string Text);

public static class QueryParser
{
    // The filter comes from the token in the bar (Tab, a chip, a key's starting filter),
    // or from a prefix typed at the start: "/" files, ">" commands, "#" inside files.
    public static ParsedQuery Parse(string raw, Tag? token) => raw switch
    {
        ['/', .. var rest] => new(Tag.File, rest.Trim()),
        ['>', .. var rest] => new(Tag.Command, rest.Trim()),
        _ => new(token, raw.Trim()),
    };

    // "set" + Tab locks the Settings filter: the first tag whose name starts with what's typed.
    static readonly (Tag Tag, string Name)[] Filters =
    [
        (Tag.App, "Apps"), (Tag.Setting, "Settings"), (Tag.Command, "Commands"), (Tag.File, "Files"),
        (Tag.Window, "Windows"), (Tag.Project, "Projects"), (Tag.Clipboard, "Clipboard"), (Tag.Theme, "Themes"),
    ];

    public static Tag? TagFromTyped(string typed)
    {
        if (typed.Length == 0) return null;
        foreach (var (tag, name) in Filters)
            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return tag;
        return null;
    }
}
```

### 4. Ranking and merging (guardrails 4, 5 and 6)

```csharp
public static class Merger
{
    static readonly Dictionary<Tag, int> Caps = new()
    {
        [Tag.App] = 4, [Tag.Setting] = 4, [Tag.Command] = 3, [Tag.File] = 3,
        [Tag.Window] = 2, [Tag.Project] = 2, [Tag.Clipboard] = 2, [Tag.Theme] = 3,
    };

    // Short queries favour things you launch; files need three letters or the Files filter.
    static double Prior(Tag tag, int queryLength) => queryLength > 2 ? 0 : tag switch
    {
        Tag.App => 10, Tag.Command => 8, Tag.Setting => 5, Tag.Window or Tag.Project => 4,
        Tag.File => -25, Tag.Clipboard => -10, Tag.Theme => -5, _ => 0,
    };

    // Fuzzy.Score is the launcher's fzy-style scorer (Fuzzy.cs): name first, then keywords.
    // History.Boost is Launcher.cs's: picks for this query weigh most, then overall use.
    public static double Score(Result r, string q) =>
        Math.Max(Fuzzy.Score(r.Name, q), r.Keywords is null ? 0 : Fuzzy.Score(r.Keywords, q) * 0.5)
        + History.Boost(r.Id, q) + r.Uses * 1.6 + Prior(r.Tag, q.Length);

    // All (no filter): each tag's best few, then "Show all N <tag>" rows, then Packages and Web.
    public static List<Result> Merge(IEnumerable<Result> candidates, ParsedQuery query)
    {
        var ranked = candidates
            .Select(r => (r, s: Score(r, query.Text)))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => x.r.Name.Length)
            .Select(x => x.r).ToList();
        if (query.Filter is not null || query.Text.Length == 0) return ranked;

        var shown = new List<Result>();
        var counts = new Dictionary<Tag, int>();
        foreach (var r in ranked)
        {
            counts[r.Tag] = counts.GetValueOrDefault(r.Tag) + 1;
            if (counts[r.Tag] <= Caps.GetValueOrDefault(r.Tag, 2)) shown.Add(r);
        }
        foreach (var (tag, n) in counts.Where(c => c.Value > Caps.GetValueOrDefault(c.Key, 2)))
            shown.Add(new Result($"more:{tag}", Tag.More, $"Show all {n} {tag}s", "Tab to filter"));
        shown.Add(new Result("pkg", Tag.Package, $"Find “{query.Text}” in winget, Scoop and Chocolatey", "omawin pkg find"));
        shown.Add(new Result("web", Tag.Web, $"Search the web for “{query.Text}”", "Only when you pick it"));
        return shown;
    }

    // Guardrail 6: late results never move the selected row. Rows already on screen keep their
    // order; newcomers are inserted only below the selection.
    public static List<Result> Stable(List<Result> onScreen, List<Result> fresh, int selected)
    {
        var keep = onScreen.Take(selected + 1).Where(r => fresh.Any(f => f.Id == r.Id)).ToList();
        return [.. keep, .. fresh.Where(f => keep.All(k => k.Id != f.Id))];
    }
}
```

### 5. Keys in the bar

```csharp
// The bar's KeyDown (not a global hook): what each key does, in one place.
void OnSearchKey(object sender, KeyRoutedEventArgs e)
{
    var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
    switch (e.Key)
    {
        case VirtualKey.Tab:
            // "set" + Tab locks Settings; otherwise Tab / Shift+Tab move through the filters.
            if (filter is null && QueryParser.TagFromTyped(Input.Text) is Tag t) { SetFilter(t); Input.Text = ""; }
            else CycleFilter(back: IsShiftDown());
            e.Handled = true; break;
        case VirtualKey.Down: Select(+1); e.Handled = true; break;
        case VirtualKey.Up: Select(-1); e.Handled = true; break;
        case VirtualKey.Enter: _ = Selected?.Primary?.Invoke(); e.Handled = true; break;
        case VirtualKey.Escape:   // one step back at a time (guardrail 9)
            if (actionsOpen) CloseActions(); else if (Input.Text.Length > 0) Input.Text = "";
            else if (filter is not null) SetFilter(null); else Dismiss();
            e.Handled = true; break;
        case VirtualKey.Back when Input.Text.Length == 0 && filter is not null:
            SetFilter(null); e.Handled = true; break;
        case VirtualKey.K when ctrl: ToggleActions(); e.Handled = true; break;
        case VirtualKey.Z when ctrl && Input.Text.Length == 0: _ = Settings.UndoLast(); e.Handled = true; break;
    }
}
```

### 6. Opening on a filter (the keys)

`App.HandleRequest` gains `--search [filter]`: `omawin.exe launcher` (Win+Space) → `--search`; `files` (Win+F) → `--search file`; `launcher clipboard` (Win+Shift+V) → `--search clipboard`; `menu` (Win+Alt+Space during its one alias release, and `omawin menu`) → `--search command --browse`. The window, list, preview and home are one view; only the starting filter differs.

### 7. Tests

The merger, parser and ranking are plain C# with no UI: unit tests (a small `menu-app.Tests` project, run in CI) for caps, priors, prefixes, `TagFromTyped`, stable ordering and the per-query history. Ranking fixtures: queries and the expected top three (`"dark"` → Dark mode first; `"vs"` → Visual Studio Code; `"/road"` → roadmap.md only).

### 8. Order of work

1. The result model, parser and merger with their tests (no UI change).
2. Sources over what exists (apps, commands, settings catalog stub, files, windows, projects, clipboard, themes).
3. The window: bar + token, filter tabs, list + preview, actions panel, footer; the bento home from `MainWindow.Tiles.cs`; the look of guardrail 12.
4. Keys: `--search [filter]`; Win+Alt+Space's alias release, then its migration (guardrail 10).
5. Measured with `omawin profile menu` before and after (roadmap 1.1).
