using System.Globalization;

namespace Omawin.Windows;

// Monitor profiles that follow the dock (roadmap 8.7): the layout (each monitor's resolution,
// place and rotation; the primary is the one at 0,0) saved per set of monitors, and put back when
// that set is plugged in again. Pure: the file, the matching and the wording;
// Desktop/DisplayConfig.cs reads and sets Windows' display configuration.
public sealed record DisplayPlace(string Id, string Name, int Width, int Height, int X, int Y, int Rotation)
{
    public bool Primary => X == 0 && Y == 0;
}

public static class DisplayProfiles
{
    public const string Header = "# omawin display: the layout for each set of monitors (omawin display save writes it; edit freely).\n" +
        "# [the monitors] then one line each: id | name | width x height | x,y | rotation (degrees)";

    // A set of monitors, whatever order Windows lists them in.
    public static string Key(IEnumerable<string> ids) => string.Join(" + ", ids.Order(StringComparer.OrdinalIgnoreCase));

    // A monitor's lasting name from its device path (\\?\DISPLAY#DELA0F3#5&2b1f&0&UID4352#{guid}):
    // the model and its instance, which stay the same across restarts and driver updates. (check:
    // the instance on a dock that changes ports.)
    public static string IdFromPath(string devicePath)
    {
        var parts = devicePath.Split('#');
        return parts.Length >= 3 ? parts[1] + "#" + parts[2] : devicePath;
    }

    public static Dictionary<string, List<DisplayPlace>> Parse(IEnumerable<string> lines)
    {
        var all = new Dictionary<string, List<DisplayPlace>>(StringComparer.OrdinalIgnoreCase);
        List<DisplayPlace>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']') { all[line[1..^1].Trim()] = current = []; continue; }
            if (current == null) continue;
            var f = line.Split('|', StringSplitOptions.TrimEntries);
            if (f.Length != 5) continue;
            var size = f[2].Split('x', StringSplitOptions.TrimEntries);
            var at = f[3].Split(',', StringSplitOptions.TrimEntries);
            if (size.Length == 2 && at.Length == 2 && Int(size[0], out var w) && Int(size[1], out var h) && Int(at[0], out var x) && Int(at[1], out var y)
                && Int(f[4], out var r) && r is 0 or 90 or 180 or 270 && w > 0 && h > 0)
                current.Add(new DisplayPlace(f[0], f[1], w, h, x, y, r));
        }
        return all;
    }

    static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);

    // The file with this set's layout put in (replacing its old one), the others kept as they were.
    public static List<string> Save(string[]? existing, IReadOnlyList<DisplayPlace> layout)
    {
        var key = Key(layout.Select(p => p.Id));
        var lines = existing is { Length: > 0 } ? Remove(existing, key) : [.. Header.Split('\n')];
        if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
        lines.Add($"[{key}]");
        lines.AddRange(layout.OrderBy(p => p.X).ThenBy(p => p.Y).Select(Line));
        return lines;
    }

    // The file without one set's section (its [heading] to the next); everything else as it was.
    public static List<string> Remove(IEnumerable<string> existing, string key)
    {
        var lines = existing.ToList();
        int start = lines.FindIndex(l => l.Trim().Equals($"[{key}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return lines;
        int end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        lines.RemoveRange(start, end - start);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    static string Line(DisplayPlace p) => $"{p.Id} | {p.Name} | {p.Width} x {p.Height} | {p.X},{p.Y} | {p.Rotation}";

    // The saved layout for the monitors plugged in now, or null.
    public static List<DisplayPlace>? For(Dictionary<string, List<DisplayPlace>> saved, IEnumerable<string> ids) =>
        saved.TryGetValue(Key(ids), out var layout) && layout.Count > 0 ? layout : null;

    // Whether `now` already is the saved layout (nothing to do, so applying after every display
    // change never loops).
    public static bool Same(IReadOnlyList<DisplayPlace> saved, IReadOnlyList<DisplayPlace> now) =>
        saved.Count == now.Count && saved.All(s => now.Any(n => n.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase)
            && n.Width == s.Width && n.Height == s.Height && n.X == s.X && n.Y == s.Y && n.Rotation == s.Rotation));

    // A saved layout must have exactly one monitor at 0,0 (Windows' primary is always there).
    public static string? Problem(IReadOnlyList<DisplayPlace> layout) =>
        layout.Count(p => p.Primary) == 1 ? null : "a layout needs exactly one monitor at 0,0 (the main one)";

    public static string Describe(DisplayPlace p) =>
        $"{p.Name}: {p.Width} × {p.Height} at {p.X},{p.Y}{(p.Rotation != 0 ? $", turned {p.Rotation}°" : "")}{(p.Primary ? " (main)" : "")}";
}
