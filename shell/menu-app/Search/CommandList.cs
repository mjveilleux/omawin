namespace SystemMenu.Search;

// One Search's Commands: every row of bin/commands.txt (what `omawin help` lists), next to the
// system menu's own rows (default/launcher/menu.txt). Pure: the window reads the file and builds
// the results; this file decides what a row says, which rows are left out, and how Enter runs one.

// One row of commands.txt: "<name> | <summary> | <action>".
public sealed record OmawinCommand(string Name, string Summary, string Action)
{
    // What you'd type: "omawin theme set".
    public string Line => "omawin " + Name;

    // Its summary names words it needs ("<name>", "<folder>"): Enter fills the bar and waits for them.
    public bool NeedsArguments => Summary.Contains('<');

    // The summary's first clause, for the row's second line: "Apply a theme".
    public string Short => CommandList.ShortOf(Summary);

    // The key that runs it, when the summary says: "(Win+Shift+S)" -> "Win Shift S".
    public string? Key => CommandList.KeyOf(Summary);

    // The first word of the action: app, ask, pwsh, run or builtin.
    public string Kind => Action.Split(' ', 2)[0];
}

// How Enter runs a command. Hidden: as a key runs it (a request to this app, or a program that
// opens its own window). Terminal: in a terminal, as if typed there, so its output and questions
// can be seen (a script, a package command, an `ask` that prints its answer).
public enum RunIn { Hidden, Terminal }

public static class CommandList
{
    // commands.txt's rows. Comments, blank lines and rows without three parts are skipped, as
    // omawin.exe skips them.
    public static List<OmawinCommand> Parse(IEnumerable<string> lines)
    {
        var list = new List<OmawinCommand>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('|', 3);
            if (parts.Length < 3) continue;
            var name = parts[0].Trim();
            if (name.Length == 0) continue;
            list.Add(new OmawinCommand(name, parts[1].Trim(), parts[2].Trim()));
        }
        return list;
    }

    // Left out of search: the keys' own entry points (they open this window, so they'd only
    // reopen it) and what starts things in the background at sign-in.
    public static bool Hidden(OmawinCommand c) =>
        c.Name is "launcher" or "files" or "menu" or "search" || c.Name == "start" || c.Name.StartsWith("start ", StringComparison.Ordinal);

    // The commands to show: not hidden, and not one a menu row already runs exactly (the menu's
    // row has the nicer name: "Update" over "update"). menuActions: menu.txt's actions.
    public static List<OmawinCommand> Shown(IEnumerable<OmawinCommand> commands, IEnumerable<string> menuActions)
    {
        var inMenu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in menuActions)
        {
            var a = action.Trim().TrimStart('!').Trim();
            if (a.StartsWith("omawin ", StringComparison.OrdinalIgnoreCase)) inMenu.Add(a["omawin ".Length..].Trim());
        }
        return [.. commands.Where(c => !Hidden(c) && !inMenu.Contains(c.Name))];
    }

    public static RunIn RunsIn(OmawinCommand c) => c.Kind switch
    {
        "app" => RunIn.Hidden,
        // Programs open their own window, except omawin's console tools.
        "run" => c.Action.Contains("omawin-windows", StringComparison.OrdinalIgnoreCase) ? RunIn.Terminal : RunIn.Hidden,
        // Opening a terminal, a project or the editor is its own window already.
        "builtin" => c.Action.StartsWith("builtin launch-", StringComparison.Ordinal) || c.Action.StartsWith("builtin terminal-here", StringComparison.Ordinal)
            ? RunIn.Hidden : RunIn.Terminal,
        _ => RunIn.Terminal,
    };

    // The words typed after a command's name, when what's typed starts with its whole name
    // ("theme set gruvbox" -> "gruvbox"; "omawin " in front is allowed). "" when it doesn't.
    public static string ArgumentsIn(string query, OmawinCommand c)
    {
        query = query.Trim();
        if (query.StartsWith("omawin ", StringComparison.OrdinalIgnoreCase)) query = query["omawin ".Length..].TrimStart();
        return query.Length > c.Name.Length && query.StartsWith(c.Name + " ", StringComparison.OrdinalIgnoreCase)
            ? query[(c.Name.Length + 1)..].Trim()
            : "";
    }

    // "Screenshot (Win+Shift+S): drag a region" -> "Screenshot"; "Apply a theme: omawin theme set
    // gruvbox" -> "Apply a theme". At most 80 characters.
    public static string ShortOf(string summary)
    {
        int end = summary.Length;
        foreach (var stop in new[] { ": ", "; ", " (" })
        {
            int at = summary.IndexOf(stop, StringComparison.Ordinal);
            if (at > 0 && at < end) end = at;
        }
        var text = summary[..end].Trim();
        return text.Length > 80 ? text[..79] + "…" : text;
    }

    // The first "(Win+…)", "(Alt+…)" or "(Ctrl+…)" in a summary, as the footer writes keys.
    public static string? KeyOf(string summary)
    {
        var m = System.Text.RegularExpressions.Regex.Match(summary, @"\(((?:Win|Alt|Ctrl)\+[^),;]+)");
        return m.Success ? m.Groups[1].Value.Trim().Replace('+', ' ') : null;
    }
}
