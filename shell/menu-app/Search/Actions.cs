namespace SystemMenu.Search;

// What can be done with a result: Enter's one thing (the first) and the rest, which Ctrl+K lists
// (plans/one-search.md: the same idea for every tag). Pure: which actions a kind of result has,
// in what order, with what words and keys; the window binds each verb to what it does.

public enum Verb
{
    Open, Switch, Next, Previous, Undo, Run, RunInTerminal, RunAsAdmin, CopyCommand, ShowInFolder, CopyPath,
    TerminalHere, OpenInEditor, SwitchTo, Close, Paste, Copy, Delete, Apply, Backgrounds, CopyAnswer, Filter,
    FindPackages, SearchWeb, AppSettings,
}

public sealed record ActionSpec(Verb Verb, string Label, string? Key = null);

// What the actions depend on beyond the tag. Program: an item that starts a program we can name
// (so it can run as admin); Folder: a file result that is a folder.
public sealed record ResultFacts(bool Program = false, bool Folder = false, bool Undoable = false);

public static class ActionPlan
{
    // The actions, Enter's first. Settings are switched, stepped or opened by their kind.
    public static List<ActionSpec> For(Tag tag, RowKind row, ResultFacts facts)
    {
        var list = new List<ActionSpec>();
        switch (tag)
        {
            case Tag.App:
                list.Add(new(Verb.Open, "Open", "↵"));
                if (facts.Program) list.Add(new(Verb.RunAsAdmin, "Run as administrator", "ctrl shift ↵"));
                list.Add(new(Verb.AppSettings, "Installed apps (uninstall, repair)"));
                break;
            case Tag.Setting:
                list.Add(row switch
                {
                    RowKind.Switch => new(Verb.Switch, "Switch", "↵"),
                    RowKind.Choice or RowKind.Slider => new(Verb.Next, "Next value", "→"),
                    _ => new(Verb.Open, "Open in Settings", "↵"),
                });
                if (row is RowKind.Choice or RowKind.Slider) list.Add(new(Verb.Previous, "Previous value", "←"));
                if (row is RowKind.Switch or RowKind.Choice or RowKind.Slider) list.Add(new(Verb.CopyCommand, "Copy as an omawin command"));
                if (facts.Undoable) list.Add(new(Verb.Undo, "Undo the last change", "ctrl z"));
                break;
            case Tag.Command:
                list.Add(new(Verb.Run, "Run", "↵"));
                list.Add(new(Verb.RunInTerminal, "Run in a terminal"));
                list.Add(new(Verb.CopyCommand, "Copy the command"));
                break;
            case Tag.File:
                list.Add(new(Verb.Open, "Open", "↵"));
                list.Add(new(Verb.ShowInFolder, "Show in folder", "ctrl ↵"));
                list.Add(new(Verb.CopyPath, "Copy path"));
                list.Add(new(Verb.TerminalHere, "Terminal here"));
                if (!facts.Folder) list.Add(new(Verb.OpenInEditor, "Open in the editor"));
                break;
            case Tag.Window:
                list.Add(new(Verb.SwitchTo, "Switch to it", "↵"));
                list.Add(new(Verb.Close, "Close it"));
                break;
            case Tag.Project:
                list.Add(new(Verb.Open, "Open the project", "↵"));
                list.Add(new(Verb.TerminalHere, "Terminal here"));
                list.Add(new(Verb.OpenInEditor, "Open in the editor"));
                list.Add(new(Verb.CopyPath, "Copy path"));
                break;
            case Tag.Clipboard:
                list.Add(new(Verb.Paste, "Paste", "↵"));
                list.Add(new(Verb.Copy, "Copy (don't paste)"));
                list.Add(new(Verb.Delete, "Delete from the history", "ctrl x"));
                break;
            case Tag.Theme:
                list.Add(new(Verb.Apply, "Apply", "↵"));
                list.Add(new(Verb.Backgrounds, "Its backgrounds"));
                break;
            case Tag.Calculator:
                list.Add(new(Verb.CopyAnswer, "Copy the answer", "↵"));
                break;
            case Tag.More:
                list.Add(new(Verb.Filter, "Show them all", "↵"));
                break;
            case Tag.Package:
                list.Add(new(Verb.FindPackages, "Find packages", "↵"));
                break;
            case Tag.Web:
                list.Add(new(Verb.SearchWeb, "Search the web", "↵"));
                break;
        }
        return list;
    }

    // The specs with what each does; a verb the window can't do for this result (null) is left out.
    public static List<ResultAction> Bind(IEnumerable<ActionSpec> specs, Func<Verb, Func<Task>?> how)
    {
        var list = new List<ResultAction>();
        foreach (var s in specs)
            if (how(s.Verb) is { } run) list.Add(new ResultAction(s.Label, s.Key, run));
        return list;
    }

    // Enter's word for the footer ("↵ switch").
    public static string EnterWord(Result? r) => r switch
    {
        null => "open",
        { Tag: Tag.Setting, Row: RowKind.Switch } => "switch",
        { Tag: Tag.Setting, Row: RowKind.Choice or RowKind.Slider } => "next",
        { Tag: Tag.Setting } => "open ↗",
        { Tag: Tag.Command } => "run",
        { Tag: Tag.Window } => "switch to",
        { Tag: Tag.Clipboard } => "paste",
        { Tag: Tag.Theme } => "apply",
        { Tag: Tag.Calculator } => "copy",
        { Tag: Tag.More } => "show all",
        { Tag: Tag.Package } => "find",
        { Tag: Tag.Web } => "search",
        _ => "open",
    };
}
