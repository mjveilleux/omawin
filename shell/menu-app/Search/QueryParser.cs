namespace SystemMenu.Search;

// What was typed, split into the filter it asks for and the words to match.
// Contents: the files filter searching inside files (# prefix) rather than names.
public readonly record struct ParsedQuery(Tag? Filter, string Text, bool Contents = false);

public static class QueryParser
{
    // The filters in the bar, in their order (Tab moves through them), with the names they show.
    public static readonly IReadOnlyList<(Tag Tag, string Name)> Filters =
    [
        (Tag.App, "Apps"), (Tag.Setting, "Settings"), (Tag.Command, "Commands"), (Tag.File, "Files"),
        (Tag.Window, "Windows"), (Tag.Project, "Projects"), (Tag.Clipboard, "Clipboard"), (Tag.Theme, "Themes"),
    ];

    // The filter comes from the token in the bar (Tab, a chip, the key that opened the window),
    // unless what's typed starts with a prefix: "/" files, ">" commands, "#" inside files.
    // A prefix wins over the token, and leaves it alone: deleting the prefix brings the token back.
    public static ParsedQuery Parse(string raw, Tag? token)
    {
        if (raw.Length > 0)
        {
            switch (raw[0])
            {
                case '/': return new(Tag.File, raw[1..].Trim());
                case '>': return new(Tag.Command, raw[1..].Trim());
                case '#': return new(Tag.File, raw[1..].Trim(), Contents: true);
            }
        }
        return new(token, raw.Trim());
    }

    // "set" + Tab locks the Settings filter: the first filter whose name starts with what's typed.
    public static Tag? TagFromTyped(string typed)
    {
        typed = typed.Trim();
        if (typed.Length == 0) return null;
        foreach (var (tag, name) in Filters)
            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return tag;
        return null;
    }

    // Tab and Shift+Tab: All (null), then each filter in order, round again.
    public static Tag? NextFilter(Tag? current, bool back = false)
    {
        int count = Filters.Count;
        int at = current is Tag t ? IndexOf(t) : -1; // -1: All
        int next = back ? at - 1 : at + 1;
        if (next < -1) next = count - 1;
        if (next >= count) next = -1;
        return next == -1 ? null : Filters[next].Tag;
    }

    public static string NameOf(Tag tag)
    {
        foreach (var (t, name) in Filters) if (t == tag) return name;
        return tag.ToString();
    }

    static int IndexOf(Tag tag)
    {
        for (int i = 0; i < Filters.Count; i++) if (Filters[i].Tag == tag) return i;
        return -1;
    }
}
