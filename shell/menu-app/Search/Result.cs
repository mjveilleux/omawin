namespace SystemMenu.Search;

// One Search (plans/one-search.md): one bar for everything omawin can find or do. Every
// result is tagged with what it is; the tag picks its colour, icon and filter.
//
// This file, QueryParser.cs and Merger.cs are pure C#: no WinUI, no P/Invoke, no files, so the
// test project (menu-app.Tests) links them directly and they run anywhere. Keep them that way.

// The kinds of result. The first eight are the filters in the bar (at most eight: guardrail 3);
// the rest only ever appear as rows.
public enum Tag { App, Setting, Command, File, Window, Project, Clipboard, Theme, Calculator, Package, Web, More }

// How a settings row behaves: a switch flips on Enter, a choice or slider steps with ← / →, a
// link opens Windows' own page (guardrail 8: honest labels).
public enum RowKind { None, Switch, Choice, Slider, Link }

public sealed record ResultAction(string Label, string? Key, Func<Task> Run);

// A result: what a row, the preview pane and the actions panel need. The positional part is
// what every source fills; the rest is optional.
public sealed record Result(
    string Id,                                  // stable across queries: history, pins, stable order
    Tag Tag,
    string Name,
    string Detail,                              // the dimmed second line
    string? Keywords = null,                    // matched (a little below the name) when the name isn't
    Func<Task>? Primary = null,                 // what Enter does
    IReadOnlyList<ResultAction>? Actions = null) // what Ctrl+K lists
{
    public string? Key { get; init; }           // a key binding to show ("Win Shift S")
    public RowKind Row { get; init; }
    public Func<string?>? State { get; init; }  // a setting's current value ("On", "144 Hz"), read only for rows drawn
    public Func<int, Task>? Step { get; init; } // ← / → on a choice or slider
    public bool Admin { get; init; }            // needs the one admin prompt (shown with a shield)
    public bool Pinned { get; init; }           // always first, unranked (the calculator's answer, a pending admin batch)
    public string? Icon { get; init; }          // a Nerd Font glyph
    public string? IconKey { get; init; }       // an app's or file's real icon (AppIcons)
    public IReadOnlyList<int>? Positions { get; init; } // matched letters in Name, for highlighting (set by the merger)
    public object? Data { get; init; }          // what it stands for (a file hit, a clipboard file, a theme), for the preview
}

// Result ids are "<kind>:<what>". The launcher's history (launch-history.json) keeps apps and
// your items by their own key, as it always has, and everything else by its result id, so the
// ranking learned before One Search carries over without rewriting the file.
public static class ResultIds
{
    public const string App = "app:";

    public static string HistoryKey(string id) => id.StartsWith(App, StringComparison.Ordinal) ? id[App.Length..] : id;
}

// Where results come from: one source per tag (apps, settings, commands, files, …). A source
// returns its candidates for a query; it never ranks across sources (the merger does).
public interface ISearchSource
{
    Tag Tag { get; }

    // Fast sources (in memory) answer at once. Slow ones (files, packages) return what they have
    // and raise Updated when more arrives; the window then merges again, keeping the selected row
    // where it is (Merger.Stable).
    IReadOnlyList<Result> Query(string query, CancellationToken cancel);

    event Action? Updated;
}
