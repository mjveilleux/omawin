namespace SystemMenu;

// Linux's files for the finder (roadmap 3.7). fff over \\wsl.localhost would crawl (9P is slow),
// so Linux keeps its own list: wsl/bin/omawin-index-files (a systemd timer and path unit there)
// writes %LOCALAPPDATA%\launcher\linux-files.tsv, a header "# <distro>\t<home>" then one file per
// line: its Linux path, modified time (Unix seconds) and size. The finder searches it as one more
// root, under the distro's name; a hit's path is a Linux path, which `omawin launch editor` opens
// in Linux. Pure (no WinUI), so it's tested anywhere.
public static class LinuxFiles
{
    public sealed record Row(string Path, string Relative, long Time, long Size);
    public sealed record Index(string Distro, string Home, Row[] Rows);
    public sealed record Found(Row Row, string Name, string RelativeDir, double Score);

    // The index file's lines; null when it isn't one (no header).
    public static Index? Parse(IEnumerable<string> lines)
    {
        using var e = lines.GetEnumerator();
        if (!e.MoveNext() || !e.Current.StartsWith("# ")) return null;
        var head = e.Current[2..].Split('\t');
        string distro = head[0].Trim(), home = head.Length > 1 ? head[1].Trim().TrimEnd('/') : "";
        if (distro.Length == 0) return null;
        var rows = new List<Row>();
        while (e.MoveNext())
        {
            var p = e.Current.Split('\t');
            if (p.Length != 3 || !p[0].StartsWith('/')) continue;
            string relative = home.Length > 0 && p[0].StartsWith(home + "/") ? p[0][(home.Length + 1)..] : p[0];
            rows.Add(new Row(p[0], relative, long.TryParse(p[1], out var t) ? t : 0, long.TryParse(p[2], out var s) ? s : 0));
        }
        return new Index(distro, home, [.. rows]);
    }

    // The best matches by fzy's score on the path under home, best first. Only paths holding the
    // query's letters in order are scored (no typo matches here: over a whole home they'd be noise,
    // and the check keeps a big index quick).
    public static List<Found> Search(Index index, string query, int limit)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var letters = query.Replace(" ", "").ToLowerInvariant();
        var found = new List<Found>();
        foreach (var row in index.Rows)
        {
            if (!InOrder(row.Relative, letters)) continue;
            var score = Fuzzy.Match(row.Relative, query);
            if (score == Fuzzy.None) continue;
            int slash = row.Relative.LastIndexOf('/');
            found.Add(new Found(row, row.Relative[(slash + 1)..], slash > 0 ? row.Relative[..slash] : "", score));
        }
        return [.. found.OrderByDescending(f => f.Score).ThenByDescending(f => f.Row.Time).Take(limit)];
    }

    static bool InOrder(string text, string letters)
    {
        int j = 0;
        for (int i = 0; i < text.Length && j < letters.Length; i++)
            if (char.ToLowerInvariant(text[i]) == letters[j]) j++;
        return j == letters.Length;
    }

    // The distro's name as the finder's chip says it.
    public static string Label(string distro) => distro.ToLowerInvariant() switch
    {
        "archlinux" or "arch" => "Arch",
        var d when d.StartsWith("ubuntu") => "Ubuntu",
        _ => distro,
    };

    // A Linux path as Windows reaches it, for Explorer: \\wsl.localhost\<distro>\home\you\x.
    public static string WindowsPath(string distro, string linuxPath) =>
        $@"\\wsl.localhost\{distro}{linuxPath.Replace('/', '\\')}";

    // --- the file, read again only when it changed ----------------------------------------------
    static readonly object gate = new();
    static string? loadedFile;
    static DateTime loadedAt;
    static Index? current;

    public static Index? Load(string file)
    {
        DateTime stamp;
        try
        {
            if (!File.Exists(file)) return null;
            stamp = File.GetLastWriteTimeUtc(file);
        }
        catch { return null; }
        lock (gate)
        {
            if (file == loadedFile && stamp == loadedAt) return current;
            try { current = Parse(File.ReadLines(file)); }
            catch { return current; }
            loadedFile = file;
            loadedAt = stamp;
            return current;
        }
    }
}
