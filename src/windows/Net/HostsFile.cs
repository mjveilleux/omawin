using System.Net;
using System.Text.RegularExpressions;

namespace Omawin.Windows;

// The hosts file (roadmap 8.19), edited as entries rather than by hand in an elevated Notepad.
// Pure: it reads and writes text. Lines it doesn't change stay exactly as they were; an entry turned
// off is commented with a marker ("# omawin-off ") so on puts it back as it was. Commands/Hosts.cs
// writes the result through one admin prompt (AdminScripts.HostsWrite), which checks every line
// against LinePattern again.
public sealed record HostsEntry(int Line, string Address, IReadOnlyList<string> Names, bool On);

public sealed partial class HostsFile
{
    public const string OffMarker = "# omawin-off ";

    // What a line of a hosts file may be: blank, a comment, or "<address> <name> [names…] [# comment]".
    public const string LinePattern = @"^\s*(#.*|[0-9A-Fa-f:.]+(%[0-9A-Za-z]+)?(\s+[A-Za-z0-9._-]+)+\s*(#.*)?)?$";

    [GeneratedRegex(LinePattern)] private static partial Regex Line();
    [GeneratedRegex(@"^[A-Za-z0-9_]([A-Za-z0-9._-]{0,251}[A-Za-z0-9_])?$")] private static partial Regex Name();

    readonly List<string> lines;
    public IReadOnlyList<string> Lines => lines;

    public HostsFile(string text)
    {
        lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        // The file's final newline isn't a line of its own.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
    }

    public string Text => string.Join("\r\n", lines) + "\r\n";

    public static bool IsValidLine(string line) => Line().IsMatch(line);
    public static bool IsValidName(string name) => Name().IsMatch(name);
    public static bool IsValidAddress(string address) => IPAddress.TryParse(address, out _) && address.IndexOfAny(['/', ' ']) < 0;

    // The lines that wouldn't pass the elevated side's check (line numbers from 1).
    public IEnumerable<(int Number, string Text)> BadLines() =>
        lines.Select((l, i) => (i + 1, l)).Where(x => !IsValidLine(x.l));

    // Every entry, on (a live line) or off (one this file commented with the marker).
    public List<HostsEntry> Entries()
    {
        var result = new List<HostsEntry>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            bool on = true;
            var t = line.TrimStart();
            if (t.StartsWith(OffMarker, StringComparison.Ordinal)) { on = false; t = t[OffMarker.Length..]; }
            else if (t.StartsWith('#') || t.Length == 0) continue;
            if (Parse(t) is { } e) result.Add(new HostsEntry(i, e.Address, e.Names, on));
        }
        return result;
    }

    static (string Address, List<string> Names)? Parse(string text)
    {
        int hash = text.IndexOf('#');
        var words = (hash >= 0 ? text[..hash] : text).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return null;
        return (words[0], words[1..].ToList());
    }

    // Adds "<address> <name> …" at the end. Null when done (or already there), else why not.
    public string? Add(string address, IReadOnlyList<string> names)
    {
        if (!IsValidAddress(address)) return $"\"{address}\" isn't an IP address";
        if (names.Count == 0) return "give at least one name";
        foreach (var n in names) if (!IsValidName(n)) return $"\"{n}\" isn't a host name";
        var live = Entries().Where(e => e.On).ToList();
        foreach (var n in names)
            foreach (var e in live)
                if (e.Names.Contains(n, StringComparer.OrdinalIgnoreCase) && !SameAddress(e.Address, address))
                    return $"{n} already points at {e.Address} (line {e.Line + 1}): omawin hosts off {n} first";
        var missing = names.Where(n => !live.Any(e => SameAddress(e.Address, address) && e.Names.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
        if (missing.Count == 0) return null;
        // An off entry for exactly this is turned back on rather than added twice.
        var off = Entries().FirstOrDefault(e => !e.On && SameAddress(e.Address, address) && missing.All(n => e.Names.Contains(n, StringComparer.OrdinalIgnoreCase)) && e.Names.Count == missing.Count);
        if (off != null) { lines[off.Line] = Uncomment(lines[off.Line]); return null; }
        lines.Add($"{address}\t{string.Join(' ', missing)}");
        return null;
    }

    // Turns a name off wherever it's live. A line with other names keeps them: the name moves to an
    // off line of its own just below. Null when done, else why not.
    public string? Off(string name)
    {
        var hits = Entries().Where(e => e.On && e.Names.Contains(name, StringComparer.OrdinalIgnoreCase)).OrderByDescending(e => e.Line).ToList();
        if (hits.Count == 0) return $"{name} isn't in the hosts file (omawin hosts lists it)";
        foreach (var e in hits)
        {
            if (e.Names.Count == 1) { lines[e.Line] = OffMarker + lines[e.Line].TrimStart(); continue; }
            var rest = e.Names.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase));
            var comment = lines[e.Line].IndexOf('#') is var h and >= 0 ? " " + lines[e.Line][h..] : "";
            lines[e.Line] = $"{e.Address}\t{string.Join(' ', rest)}{comment}";
            lines.Insert(e.Line + 1, $"{OffMarker}{e.Address}\t{name}");
        }
        return null;
    }

    // Turns an off name back on (the lines this file turned off). Null when done, else why not.
    public string? On(string name)
    {
        var hits = Entries().Where(e => !e.On && e.Names.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0)
            return Entries().Any(e => e.On && e.Names.Contains(name, StringComparer.OrdinalIgnoreCase)) ? null : $"{name} isn't turned off here (omawin hosts lists it)";
        var live = Entries().Where(e => e.On).ToList();
        foreach (var e in hits)
            foreach (var n in e.Names)
                if (live.FirstOrDefault(l => l.Names.Contains(n, StringComparer.OrdinalIgnoreCase) && !SameAddress(l.Address, e.Address)) is { } clash)
                    return $"{n} points at {clash.Address} now (line {clash.Line + 1}): turn that off first";
        foreach (var e in hits) lines[e.Line] = Uncomment(lines[e.Line]);
        return null;
    }

    static string Uncomment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith(OffMarker, StringComparison.Ordinal) ? t[OffMarker.Length..] : line;
    }

    // The lines that differ, as - and +, to show before asking for admin. Hosts files are short and
    // a change touches a line or two, so a plain longest-common-subsequence diff is enough.
    public static List<string> Diff(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
            for (int j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var result = new List<string>();
        int x = 0, y = 0;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && a[x] == b[y]) { x++; y++; }
            // A removed line before the one added in its place, as diff shows them.
            else if (x < a.Count && (y == b.Count || lcs[x + 1, y] >= lcs[x, y + 1])) result.Add("- " + a[x++]);
            else result.Add("+ " + b[y++]);
        }
        return result;
    }

    static bool SameAddress(string a, string b) =>
        IPAddress.TryParse(a, out var x) && IPAddress.TryParse(b, out var y) ? x.Equals(y) : a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
