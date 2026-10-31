namespace SystemMenu.Search;

// Your pins on One Search's home (plans/one-search.md; the default chosen in
// open-questions/one-search-pins.md): Ctrl+P on a result puts it in the home's last row, Pinned,
// under Frequent, so no place above it moves (guardrail 2). Four at most, one row, in the order
// you pinned them; Ctrl+P again (on the result, or on its tile) takes it off. Kept as result ids,
// one a line, in %LOCALAPPDATA%\launcher\pins.txt. Pure: tested in menu-app.Tests.
public enum PinChange { Pinned, Unpinned, Full, NotPinnable }

public static class Pins
{
    public const int Most = 4;
    public const string TileKey = "pin:"; // a pin's tile on the home: "pin:<result id>"

    // What can be pinned: what's still there next time. Not a window (it closes), a copy (it
    // scrolls away), the calculator's answer or the special rows.
    public static bool CanPin(Tag tag) => tag is Tag.App or Tag.Setting or Tag.Command or Tag.File or Tag.Project or Tag.Theme;

    // The file's ids, in order, once each; blank lines and comments are not pins.
    public static List<string> Parse(IEnumerable<string> lines)
    {
        var list = new List<string>();
        foreach (var raw in lines)
        {
            var id = raw.Trim();
            if (id.Length == 0 || id[0] == '#' || !id.Contains(':') || list.Contains(id, StringComparer.Ordinal)) continue;
            list.Add(id);
        }
        return list;
    }

    // Ctrl+P: pinned at the end, or unpinned (the others close up, in order). A full row says so
    // rather than dropping one of yours.
    public static (List<string> Pins, PinChange Change) Toggle(IReadOnlyList<string> pins, string id, Tag tag)
    {
        var list = pins.ToList();
        if (list.Remove(id)) return (list, PinChange.Unpinned);
        if (!CanPin(tag)) return (list, PinChange.NotPinnable);
        if (list.Count >= Most) return (list, PinChange.Full);
        list.Add(id);
        return (list, PinChange.Pinned);
    }

    // What the bar says after Ctrl+P.
    public static string Say(PinChange change, string name) => change switch
    {
        PinChange.Pinned => $"Pinned to the home: {name}",
        PinChange.Unpinned => $"Unpinned: {name}",
        PinChange.Full => $"The home holds {Most} pins: unpin one first (Ctrl+P on it)",
        _ => "Only apps, settings, commands, files, projects and themes can be pinned",
    };
}
