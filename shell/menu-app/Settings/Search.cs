// Settings search (roadmap 1B.3): the catalog's switches and Windows' own pages, ranked for what
// was typed. The launcher's fuzzy matcher scores the name, then the keywords and the area a little
// below it; settings omawin can switch in place rank above pages that only open Settings; and what
// you've changed before after typing the same thing comes first (the boost, from the launcher's
// history). One Search's Settings filter is this list. Pure C#: tested in menu-app.Tests.

namespace SystemMenu;

// A result: a catalog entry (switch, choice, slider, action, link) or a page from the index.
public sealed record SettingHit(SettingEntry? Entry, IndexedSetting? Page, List<int> Positions, double Score)
{
    public string Name => Entry?.Name ?? Page!.Name;
    // Stable across queries, for history and the selected row: "setting:<id>" or "page:<uri>#<name>".
    public string Key => Entry != null ? "setting:" + Entry.Id : $"page:{Page!.Uri}#{Page.Name}";
}

public static class SettingSearch
{
    // boost: extra score for a key after typing this query (0 when never used).
    public static List<SettingHit> Rank(IEnumerable<SettingEntry> entries, IEnumerable<IndexedSetting> pages, string query,
        Func<string, string, double>? boost = null, int limit = 40)
    {
        query = query.Trim();
        var hits = new List<SettingHit>();
        if (query.Length == 0) return hits;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            names.Add(e.Name);
            if (Score(e.Name, e.Keywords, Catalog.AreaLabel(e.Area), query) is not var (score, positions)) continue;
            score += 0.5; // switched in place: better than a page that only opens Settings
            var hit = new SettingHit(e, null, positions, 0);
            hits.Add(hit with { Score = score + (boost?.Invoke(hit.Key, query) ?? 0) });
        }
        var pagesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pages)
        {
            // A page with the same name as a switch is the same thing, said worse; and Windows'
            // index repeats names across pages: the first of each is enough.
            if (names.Contains(p.Name) || !pagesSeen.Add(p.Name + "\n" + p.Page)) continue;
            if (Score(p.Name, p.Keywords, p.Page, query) is not var (score, positions)) continue;
            var hit = new SettingHit(null, p, positions, 0);
            hits.Add(hit with { Score = score + (boost?.Invoke(hit.Key, query) ?? 0) });
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Name.Length).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit).ToList();
    }

    // The name's score with its matched letters; else the keywords' or the place's, lower and
    // without letters to highlight. Null: no match at all.
    static (double Score, List<int> Positions)? Score(string name, string keywords, string place, string query)
    {
        var positions = new List<int>();
        double score = Fuzzy.Match(name, query, positions);
        if (score != Fuzzy.None)
        {
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 2;
            else if (WordStarts(name, query)) score += 1;
        }
        foreach (var keyword in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            double k = Fuzzy.Match(keyword, query) - 0.5;
            // A keyword typed whole ("hz", "ext") is as good as the name.
            if (keyword.Equals(query, StringComparison.OrdinalIgnoreCase)) k += 2.5;
            if (k > score) { score = k; positions = []; }
        }
        // Every word somewhere in name, keywords and place ("explorer hidden"): a weaker match.
        if (score == Fuzzy.None)
        {
            var all = name + " " + keywords + " " + place;
            if (query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => all.Contains(w, StringComparison.OrdinalIgnoreCase)))
                score = query.Length * 0.3;
        }
        if (score == Fuzzy.None) return null;
        score -= 0.02 * Math.Max(0, name.Length - query.Length); // shorter names first
        return (score, positions);
    }

    static bool WordStarts(string name, string query) =>
        name.Split([' ', '-', '(', '/'], StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(query, StringComparison.OrdinalIgnoreCase));
}
