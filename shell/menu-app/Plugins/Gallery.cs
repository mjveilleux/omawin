namespace SystemMenu.Plugins;

// The plugin gallery's page (Install › Plugins › Gallery, `omawin plugin gallery`): every plugin
// omawin can show, one row each: the ones that ship with it (shell/plugins), the ones downloaded
// from the gallery (%LOCALAPPDATA%\omawin\plugins), and the rest of the gallery's index
// (GalleryIndex.cs), with whether each is on (%LOCALAPPDATA%\launcher\plugins.txt) and whether it
// runs. Enter turns one on or off through `omawin plugin add|remove` (bin/preset.ps1), which
// downloads a gallery plugin first; the page itself only reads.
//
// Pure: no WinUI, no Windows calls (the folders are handed in), so menu-app.Tests runs it anywhere.
public sealed record GalleryRow(string Id, string Name, string Author, string Description, string Category, int Stars,
    bool Shipped, bool Downloaded, bool On, string Verdict, string Why, string Repo = "")
{
    // Enter turns it off when it's on; else on, when the host can run it (and it's downloaded or
    // can be).
    public bool CanTurnOn => Verdict is Verdicts.Runs or Verdicts.Partly;
}

public static class PluginFolders
{
    // Where a plugin's folder is: omawin's own (shell/plugins/<id>) first, else one downloaded
    // from the gallery. Null when there's neither, or the id isn't one (a line of plugins.txt
    // someone edited: nothing outside these folders is ever read).
    public static string? Find(string id, string shippedDir, string downloadedDir)
    {
        if (!GalleryIndex.IsSafeId(id)) return null;
        foreach (var dir in new[] { Path.Combine(shippedDir, id), Path.Combine(downloadedDir, id) })
            if (File.Exists(Path.Combine(dir, "manifest.json"))) return dir;
        return null;
    }

    // plugins.txt: the ids that are on, one a line, # for comments; each once, in order.
    public static List<string> ReadOn(IEnumerable<string> lines)
    {
        var on = new List<string>();
        foreach (var raw in lines)
        {
            var id = raw.Trim();
            if (id.Length == 0 || id.StartsWith('#') || on.Contains(id)) continue;
            on.Add(id);
        }
        return on;
    }

    // The plugins in a folder (one subfolder each, with a manifest), by id; unreadable ones left out.
    public static List<Manifest> Manifests(string dir)
    {
        var found = new List<Manifest>();
        if (!Directory.Exists(dir)) return found;
        foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
        {
            try
            {
                var file = Path.Combine(sub, "manifest.json");
                if (!File.Exists(file)) continue;
                if (Manifest.Parse(File.ReadAllText(file), out _) is { } m && GalleryIndex.IsSafeId(m.Id)) found.Add(m);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }
}

public static class Gallery
{
    // Every row: what's on first, then omawin's own, then what runs, then by stars.
    public static List<GalleryRow> Rows(IEnumerable<Manifest> shipped, IEnumerable<Manifest> downloaded, GalleryIndex? index, ICollection<string> on)
    {
        var listed = (index?.Plugins ?? []).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new Dictionary<string, GalleryRow>(StringComparer.Ordinal);
        foreach (var m in shipped)
            rows.TryAdd(m.Id, Local(m, shipped: true));
        foreach (var m in downloaded)
            rows.TryAdd(m.Id, Local(m, shipped: false));
        foreach (var p in listed.Values)
            rows.TryAdd(p.Id, new GalleryRow(p.Id, p.Name, p.Author, p.Description, p.Category, p.Stars, false, false, on.Contains(p.Id), p.Verdict, p.Why, p.Repo));
        return [.. rows.Values
            .OrderByDescending(r => r.On)
            .ThenByDescending(r => r.Shipped)
            .ThenBy(r => Array.IndexOf(Verdicts.All, r.Verdict))
            .ThenByDescending(r => r.Stars)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];

        // One on this PC: it runs as far as the gallery knows (the gallery's verdict when it's
        // listed; omawin's own always do), with the gallery's stars and category.
        GalleryRow Local(Manifest m, bool shipped)
        {
            listed.TryGetValue(m.Id, out var p);
            var verdict = shipped || p == null ? Verdicts.Runs : p.Verdict;
            var why = shipped ? "comes with omawin" : p?.Why ?? "downloaded";
            return new GalleryRow(m.Id, p?.Name ?? (m.BarWidget?.DisplayName ?? (m.Name.Length > 0 ? m.Name : m.Id)), p?.Author ?? m.Author ?? "",
                p?.Description ?? m.Description ?? "", p?.Category ?? m.BarWidget?.Category ?? "", p?.Stars ?? 0, shipped, !shipped, on.Contains(m.Id), verdict, why, p?.Repo ?? "");
        }
    }

    // A search: fuzzy on the name (what you'd type), and on the author, category and description
    // below it; the rows' own order among equals.
    public static List<GalleryRow> Search(List<GalleryRow> rows, string query)
    {
        if (query.Trim().Length == 0) return rows;
        return [.. rows
            .Select((r, i) => (Row: r, Index: i, Score: Math.Max(Fuzzy.Match(r.Name, query), Fuzzy.Match($"{r.Author} {r.Category} {r.Description}", query) - 2)))
            .Where(x => x.Score > Fuzzy.None)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Row)];
    }

    // What `omawin plugin` is asked to do with a row on Enter: remove when it's on, add when it
    // can run; null when there's nothing to do (it can't run yet).
    public static string? Verb(GalleryRow row) => row.On ? "remove" : row.CanTurnOn ? "add" : null;
}
