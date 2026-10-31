// Your Windows settings as a file, so they follow you to a new PC (roadmap 1B.5): the account
// folder's settings.txt (~/omawin-account/settings.txt) or the PC's shared folder's, one
// "id = value" per line. `omawin settings save` writes it, `omawin settings apply` (and install)
// sets what it says. Values, not catalog lines, so it lives beside launcher/, not in it.
// Pure C#: tested in menu-app.Tests.

namespace SystemMenu;

public static class SettingValueFile
{
    public const string Header =
        "# Windows settings, set on a new PC by `omawin settings apply` (install runs it). Written by\n" +
        "# `omawin settings save`; edit freely. id = value (omawin setting list shows both).";

    // "explorer.hidden = on" -> (explorer.hidden, on). Comments, blank lines and anything without
    // an id before the = are not values.
    public static (string Id, string Value)? ParseLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] == '#') return null;
        int eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var id = line[..eq].Trim();
        return id.Length == 0 || id.Contains(' ') ? null : (id, line[(eq + 1)..].Trim());
    }

    // Every layer's values, later layers winning (the shared folder's, then this account's), in
    // the order first seen.
    public static List<(string Id, string Value)> Read(IEnumerable<string[]> layers)
    {
        var order = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lines in layers)
            foreach (var line in lines)
                if (ParseLine(line) is var (id, value))
                {
                    if (!values.ContainsKey(id)) order.Add(id);
                    values[id] = value;
                }
        return order.Select(id => (id, values[id])).ToList();
    }

    public sealed record Saved(List<string> Lines, int Count, int Added);

    // Merge, never clobber: the file's own lines (comments, order, ids this omawin doesn't know)
    // stay; a known id's value is brought up to date; ids changed through omawin that the file
    // doesn't have yet are added at the end. current: an id's value now (null: can't tell, or not
    // a setting that has a value; then its line is left as it is, or not added).
    public static Saved Merge(string[]? existing, IEnumerable<string> changedIds, Func<string, (string Id, string? Value)?> current)
    {
        var lines = existing is { Length: > 0 } ? existing.ToList() : [.. Header.Split('\n')];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            if (ParseLine(lines[i]) is not var (id, _)) continue;
            seen.Add(id);
            if (current(id) is (var canonical, { } value)) lines[i] = $"{canonical} = {value}";
        }
        int added = 0;
        foreach (var id in changedIds)
        {
            if (seen.Contains(id) || current(id) is not (var canonical, { } value)) continue;
            lines.Add($"{canonical} = {value}");
            seen.Add(id);
            added++;
        }
        return new(lines, lines.Count(l => ParseLine(l) != null), added);
    }
}
