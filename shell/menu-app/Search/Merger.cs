namespace SystemMenu.Search;

// Ranks every source's candidates into one list (plans/one-search.md, guardrails 4-6).
// Scores use the launcher's scale (Launcher.Search): Fuzzy.Match's score, a keyword match a
// little below a name match, +2 when the name starts with what's typed, shorter names first,
// plus your history (History.Boost: up to ~1 for use, up to 3 for picking it after this query).
public static class Merger
{
    // In All, each tag shows its best few; the rest are one "Show all" row away (guardrail 4).
    public static readonly IReadOnlyDictionary<Tag, int> Caps = new Dictionary<Tag, int>
    {
        [Tag.App] = 4, [Tag.Setting] = 4, [Tag.Command] = 3, [Tag.File] = 3,
        [Tag.Window] = 2, [Tag.Project] = 2, [Tag.Clipboard] = 2, [Tag.Theme] = 3,
    };

    // Files are only searched in All from this many letters (or with the Files filter, or "/").
    public const int FilesFrom = 3;

    public static int CapOf(Tag tag) => Caps.TryGetValue(tag, out var n) ? n : 2;

    // A small preference by kind, so near-ties go to what you most likely meant: apps, commands
    // and settings a little ahead of files (there are thousands of files; "dark" should find Dark
    // mode before dark.css). For one or two letters you're most likely launching something, so the
    // preference is stronger. Small next to a real match: it only breaks near-ties.
    public static double Prior(Tag tag, int queryLength)
    {
        double always = tag switch
        {
            Tag.App => 0.3,
            Tag.Command or Tag.Setting => 0.2,
            Tag.Window or Tag.Project => 0.1,
            Tag.File => -0.3,
            _ => 0,
        };
        if (queryLength > 2) return always;
        return always + tag switch
        {
            Tag.App => 0.7,
            Tag.Command => 0.6,
            Tag.Setting => 0.3,
            Tag.Window or Tag.Project => 0.3,
            Tag.Theme => -0.5,
            Tag.Clipboard => -1.0,
            Tag.File => -1.7,
            _ => 0,
        };
    }

    // A result's score for a query, or Fuzzy.None when it doesn't match. boost: History.Boost
    // (passed in, so this stays pure); positions: the matched letters of the name, for highlighting.
    public static double Score(Result r, string query, Func<string, string, double>? boost, List<int>? positions = null)
    {
        var at = new List<int>();
        double score = Fuzzy.Match(r.Name, query, at);
        if (r.Keywords is { Length: > 0 } keywords)
        {
            double k = Fuzzy.Match(keywords, query) - 0.5;
            if (k > score) { score = k; at.Clear(); }
        }
        if (score == Fuzzy.None) return Fuzzy.None;
        if (r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 2;
        score -= 0.02 * Math.Max(0, r.Name.Length - query.Length);
        score += Prior(r.Tag, query.Length);
        if (boost is not null) score += boost(r.Id, query);
        positions?.AddRange(at);
        return score;
    }

    // One list from every source's candidates.
    // - Nothing typed and no filter: empty (the window shows the bento home instead).
    // - Nothing typed with a filter: everything in it, most used first (boost with an empty query).
    // - A filter: every match in it, ranked.
    // - All: each tag's best few, then a "Show all N" row per capped tag, then the package and
    //   web rows (picked, never searched as you type: guardrail 7).
    // Pinned results (the calculator's answer, a pending admin batch) always come first, unranked.
    public static List<Result> Merge(IEnumerable<Result> candidates, ParsedQuery query, Func<string, string, double>? boost = null)
    {
        string text = query.Text;
        if (text.Length == 0 && query.Filter is null) return []; // the home shows instead

        var pinned = new List<Result>();
        var scored = new List<(Result Result, double Score)>();
        foreach (var r in candidates)
        {
            if (query.Filter is Tag only && r.Tag != only) continue;
            if (r.Pinned) { pinned.Add(r); continue; }
            if (text.Length == 0)
            {
                scored.Add((r, boost?.Invoke(r.Id, "") ?? 0));
                continue;
            }
            if (r.Tag == Tag.File && query.Filter is null && text.Length < FilesFrom) continue;
            var positions = new List<int>();
            double s = Score(r, text, boost, positions);
            if (s == Fuzzy.None) continue;
            scored.Add((r with { Positions = positions }, s));
        }

        var ranked = scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Result.Name.Length)
            .ThenBy(x => x.Result.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Result)
            .ToList();

        if (query.Filter is not null || text.Length == 0) return [.. pinned, .. ranked];

        var shown = new List<Result>(pinned);
        var counts = new Dictionary<Tag, int>();
        foreach (var r in ranked)
        {
            counts[r.Tag] = counts.GetValueOrDefault(r.Tag) + 1;
            if (counts[r.Tag] <= CapOf(r.Tag)) shown.Add(r);
        }
        foreach (var (tag, _) in QueryParser.Filters)
        {
            if (counts.GetValueOrDefault(tag) is var n && n > CapOf(tag))
                shown.Add(new Result($"more:{tag}", Tag.More, $"Show all {n} {QueryParser.NameOf(tag)}", "Tab to " + QueryParser.NameOf(tag)));
        }
        shown.Add(new Result("pkg", Tag.Package, $"Find “{text}” in winget, Scoop and Chocolatey", "omawin pkg find"));
        shown.Add(new Result("web", Tag.Web, $"Search the web for “{text}”", "Only when you pick it"));
        return shown;
    }

    // Guardrail 6: results that arrive late never move the selected row. The rows above and at the
    // selection keep their places (those still in the fresh list); everything else from the fresh
    // list follows below them, in its own order.
    public static List<Result> Stable(IReadOnlyList<Result> onScreen, IReadOnlyList<Result> fresh, int selected)
    {
        if (selected < 0 || onScreen.Count == 0) return [.. fresh];
        var freshIds = new HashSet<string>(fresh.Select(f => f.Id));
        var byId = fresh.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        var keep = onScreen.Take(selected + 1).Where(r => freshIds.Contains(r.Id)).Select(r => byId[r.Id]).ToList();
        var kept = new HashSet<string>(keep.Select(k => k.Id));
        return [.. keep, .. fresh.Where(f => !kept.Contains(f.Id))];
    }
}
