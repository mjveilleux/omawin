using System.Text.Json;
using System.Text.RegularExpressions;

namespace Omawin.Cli.Pkg;

// Reading what each package manager prints. Pure: the text in, packages out, so every format is
// tested without the managers (test/cli.Tests/PkgParseTests.cs).
public static partial class Parse
{
    // `winget export -o <file> --include-versions`: {"Sources":[{"Packages":[{"PackageIdentifier","Version"}]}]}.
    public static List<Installed> WingetExport(string json)
    {
        var list = new List<Installed>();
        using var doc = TryJson(json);
        if (doc == null || !doc.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array) return list;
        foreach (var source in sources.EnumerateArray())
        {
            if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in packages.EnumerateArray())
                if (Str(p, "PackageIdentifier") is { Length: > 0 } id) list.Add(new Installed(id, Str(p, "Version") ?? ""));
        }
        return list;
    }

    // `scoop export`: JSON with apps[] (Name, Version, Source) in Scoop 0.4 and later; older Scoop
    // printed a line per app, "name (v:1.2.3) [bucket]".
    public static List<Installed> ScoopExport(string text)
    {
        var list = new List<Installed>();
        using (var doc = TryJson(text))
            if (doc != null)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
                    foreach (var a in apps.EnumerateArray())
                        if (Str(a, "Name") is { Length: > 0 } name) list.Add(new Installed(name, Str(a, "Version") ?? ""));
                return list;
            }
        foreach (var line in Lines(text))
            if (ScoopLine().Match(line) is { Success: true } m) list.Add(new Installed(m.Groups[1].Value, m.Groups[2].Value));
        return list;
    }
    [GeneratedRegex(@"^\s*(\S+)\s+\(v:([^)]*)\)")] private static partial Regex ScoopLine();

    // Chocolatey's -r (--limit-output) lines: "id|version" (choco list), "id|version|available|pinned"
    // (choco outdated); anything else (warnings) is skipped.
    public static List<Installed> ChocoLimited(string text, bool outdated = false)
    {
        var list = new List<Installed>();
        foreach (var line in Lines(text))
        {
            var f = line.Trim().Split('|');
            if (f.Length < 2 || f[0].Length == 0 || f[0].Contains(' ')) continue;
            list.Add(outdated && f.Length >= 3 ? new Installed(f[0], f[2]) : new Installed(f[0], f[1]));
        }
        return list;
    }

    // `mise ls --json`: {"tool": [{"version", "installed", "active", ...}]}: each installed version.
    public static List<Installed> MiseList(string json)
    {
        var list = new List<Installed>();
        using var doc = TryJson(json);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var tool in doc.RootElement.EnumerateObject())
        {
            if (tool.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var v in tool.Value.EnumerateArray())
            {
                if (v.TryGetProperty("installed", out var installed) && installed.ValueKind == JsonValueKind.False) continue;
                list.Add(new Installed(tool.Name, Str(v, "version") ?? ""));
            }
        }
        return list;
    }

    // `mise outdated --json`: {"tool": {"current", "latest", ...}}: the version waiting.
    public static List<Installed> MiseOutdated(string json)
    {
        var list = new List<Installed>();
        using var doc = TryJson(json);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var tool in doc.RootElement.EnumerateObject())
            if (tool.Value.ValueKind == JsonValueKind.Object && Str(tool.Value, "latest") is { Length: > 0 } latest)
                list.Add(new Installed(tool.Name, latest));
        return list;
    }

    // `mise registry`: "<tool>  <backend> [backend...]" a line.
    public static List<Installed> MiseRegistry(string text, string query) =>
        Lines(text).Select(l => l.Trim().Split(' ', 2)[0])
            .Where(t => t.Length > 0 && t.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(t => new Installed(t, "", t)).ToList();

    // A table the way winget and PowerShell's Format-Table print one (winget search / list /
    // upgrade, scoop search / status): a header, a line of dashes under it, then rows whose columns
    // start where the header's words do. Returns each row's cells, in the header's order. winget's
    // header is in the PC's language, so callers read columns by position, not by name.
    public static List<string[]> Table(string text, out string[] header)
    {
        header = [];
        var lines = Lines(text).Select(StripProgress).ToList();
        var dashes = lines.FindIndex(l => l.Trim().Length > 2 && l.Trim().All(c => c is '-' or ' '));
        var rows = new List<string[]>();
        if (dashes < 1) return rows;
        var head = lines[dashes - 1];
        // Where each column starts. PowerShell's dashes line marks them exactly (a run of dashes
        // per column, so "Installed Version" is one column); winget's is one unbroken line, and
        // its header's words (none with a space inside) mark them.
        var starts = new List<int>();
        var dashLine = lines[dashes];
        var source = dashLine.Trim().Contains(' ') ? dashLine : head;
        for (int i = 0; i < source.Length; i++)
            if (source[i] != ' ' && (i == 0 || source[i - 1] == ' ')) starts.Add(i);
        header = starts.Select((s, i) => Cut(head, s, i + 1 < starts.Count ? starts[i + 1] : int.MaxValue)).ToArray();
        foreach (var line in lines.Skip(dashes + 1))
        {
            // The table ends at a blank line (winget's list may print a second table after one,
            // "require explicit targeting", which isn't this one's).
            if (line.Trim().Length == 0) { if (rows.Count > 0) break; continue; }
            rows.Add(starts.Select((s, i) => Cut(line, s, i + 1 < starts.Count ? starts[i + 1] : int.MaxValue)).ToArray());
        }
        return rows;
    }

    static string Cut(string line, int from, int to) =>
        from >= line.Length ? "" : line[from..Math.Min(line.Length, to)].Trim();

    // winget draws progress with block characters and a spinner (-\|/) on the same line it then
    // overwrites with a carriage return: only what's after the last one counts.
    static string StripProgress(string line)
    {
        var cr = line.LastIndexOf('\r');
        return cr >= 0 ? line[(cr + 1)..] : line;
    }

    // winget's search / list / upgrade tables: Name, Id, Version, then Available (upgrade, list)
    // or Match / Source. By position: the header is translated. Ids winget cut short ("…") are
    // left out: they can't be installed by. So is a footer ("3 upgrades available."): no version.
    public static List<Installed> WingetTable(string text, bool available = false)
    {
        var rows = Table(text, out var header);
        var list = new List<Installed>();
        if (header.Length < 3) return list;
        foreach (var r in rows)
        {
            var id = r[1];
            if (id.Length == 0 || id.EndsWith('…') || id.Contains(' ') || r[2].Length == 0) continue;
            var version = available ? (r.Length > 3 ? r[3] : "") : r[2];
            list.Add(new Installed(id, version, r[0]));
        }
        return list;
    }

    // `scoop search`: Name, Version, Source, Binaries. `scoop status`: Name, Installed Version,
    // Latest Version, ... (the waiting version third).
    public static List<Installed> ScoopTable(string text, bool status = false) =>
        Table(text, out var header).Where(r => r.Length >= 2 && r[0].Length > 0)
            .Select(r => new Installed(r[0], status ? (r.Length > 2 ? r[2] : "") : r[1], r.Length > 2 && !status ? r[2] : "")).ToList();

    // The last meaningful line of a manager's output, for a failure: no progress bars, spinners or blanks.
    public static string Tail(IEnumerable<string> lines) =>
        lines.Select(StripProgress).Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.Any(c => c is >= '▀' and <= '▟') && l is not ("-" or "\\" or "|" or "/"))
            .LastOrDefault() ?? "";

    static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r'));

    static JsonDocument? TryJson(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        try { return JsonDocument.Parse(text[start..]); }
        catch (JsonException) { return null; }
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;
}
