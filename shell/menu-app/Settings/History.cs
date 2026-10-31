// Every settings change omawin makes, with the value before it, so any of them can be undone:
// %LOCALAPPDATA%\launcher\settings-history.tsv (plans/02-settings-and-one-search.md §6).
//
//   <unix seconds>\t<id>\t<before>\t<after>\t<source>
//
// source is menu | command | apply | admin | undo:<line>, where <line> is the 0-based line an
// undo put back, so undoing twice walks back two changes and never undoes an undo. before is "?"
// when it couldn't be read (undo then says so instead of guessing). Append-only; trimmed to the
// last Keep lines when the menu app starts. Pure C# and plain files: tested in menu-app.Tests.

namespace SystemMenu;

public sealed record SettingChange(int Line, long At, string Id, string Before, string After, string Source)
{
    public bool IsUndo => Source.StartsWith("undo:", StringComparison.Ordinal);
    public int? Undid => IsUndo && int.TryParse(Source[5..], out var n) ? n : null;
}

public sealed class SettingHistory(string path)
{
    public const int Keep = 1000;
    public string FilePath { get; } = path;
    readonly object gate = new();

    public void Append(string id, string? before, string after, string source, DateTimeOffset? at = null)
    {
        var line = Format((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(), id, before ?? "?", after, source);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, line + "\n");
        }
    }

    public string[] Lines()
    {
        lock (gate)
        {
            try { return File.Exists(FilePath) ? File.ReadAllLines(FilePath) : []; }
            catch (IOException) { return []; }
        }
    }

    public List<SettingChange> Changes() => Parse(Lines());
    public SettingChange? LastUndoable() => LastUndoable(Lines());
    public IEnumerable<string> ChangedIds() => Changes().Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase);

    // Keep the newest lines. An undo line names a line by number, so the numbers in the kept
    // part are shifted by what's dropped; undo lines whose target is dropped can go too.
    public void Trim(int keep = Keep)
    {
        lock (gate)
        {
            string[] lines;
            try { lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : []; }
            catch (IOException) { return; }
            if (lines.Length <= keep) return;
            File.WriteAllLines(FilePath, TrimLines(lines, keep));
        }
    }

    // --- pure parts --------------------------------------------------------------------------
    // Tabs and line breaks can't be in a field: a value with one (a choice label typed by hand)
    // has them turned into spaces.
    public static string Format(long at, string id, string before, string after, string source) =>
        string.Join('\t', at.ToString(), Clean(id), Clean(before), Clean(after), Clean(source));

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static List<SettingChange> Parse(string[] lines)
    {
        var list = new List<SettingChange>();
        for (int i = 0; i < lines.Length; i++)
        {
            var p = lines[i].Split('\t');
            if (p.Length == 5 && long.TryParse(p[0], out var at)) list.Add(new(i, at, p[1], p[2], p[3], p[4]));
        }
        return list;
    }

    // The newest change not undone yet (and not an undo itself).
    public static SettingChange? LastUndoable(string[] lines)
    {
        var changes = Parse(lines);
        var undone = changes.Select(c => c.Undid).OfType<int>().ToHashSet();
        for (int i = changes.Count - 1; i >= 0; i--)
            if (!changes[i].IsUndo && !undone.Contains(changes[i].Line)) return changes[i];
        return null;
    }

    public static string[] TrimLines(string[] lines, int keep)
    {
        int drop = Math.Max(0, lines.Length - keep);
        var kept = new List<string>();
        var moved = new Dictionary<int, int>(); // old line number -> new one
        for (int i = drop; i < lines.Length; i++)
        {
            var p = lines[i].Split('\t');
            if (p.Length == 5 && p[4].StartsWith("undo:", StringComparison.Ordinal) && int.TryParse(p[4][5..], out var n))
            {
                // An undo always names an earlier line: already renumbered, or dropped, and then
                // so is the reason to keep this one.
                if (!moved.TryGetValue(n, out var to)) continue;
                p[4] = "undo:" + to;
                moved[i] = kept.Count;
                kept.Add(string.Join('\t', p));
            }
            else { moved[i] = kept.Count; kept.Add(lines[i]); }
        }
        return [.. kept];
    }
}
