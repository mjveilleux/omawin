// omawin's recommended Windows settings (default/launcher/tune-up.txt, roadmap 8G): which catalog
// settings omawin recommends, at what value, whether it sets them by itself, and why. This file
// only parses, checks and reports: reading and setting each value is the settings service's
// (Settings/), handed in as functions, so menu-app.Tests runs all of it anywhere.

namespace SystemMenu;

public enum TuneUpWhen { Auto, Ask }

public sealed record Recommendation(string Id, string Value, TuneUpWhen When, string Why, string Origin);

public enum TuneUpState
{
    Applied,      // the setting has the recommended value
    NotApplied,   // it has another one
    Unreadable,   // its handler couldn't say (a builtin not on this PC, a value Windows hides)
    Hidden,       // not shown on this PC: not yet verified ("check"), or not for this Windows build
}

public sealed record TuneUpRow(Recommendation Rec, SettingEntry Entry, TuneUpState State, string? Current);

public static class TuneUp
{
    // Every line of every layer, later layers replacing earlier ones by id (as settings.txt).
    public static (List<Recommendation> Recs, List<Catalog.Problem> Problems) Parse(IEnumerable<(string File, string[] Lines)> layers)
    {
        var byId = new Dictionary<string, Recommendation>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var problems = new List<Catalog.Problem>();
        foreach (var (file, lines) in layers)
            for (int i = 0; i < lines.Length; i++)
            {
                var origin = $"{Path.GetFileName(file)}:{i + 1}";
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                try
                {
                    var r = ParseLine(line, origin);
                    if (!byId.ContainsKey(r.Id)) order.Add(r.Id);
                    byId[r.Id] = r;
                }
                catch (FormatException ex) { problems.Add(new(origin, ex.Message)); }
            }
        return (order.Select(id => byId[id]).ToList(), problems);
    }

    // id | value | when | why. The why may hold '|' of its own: only the first three split.
    public static Recommendation ParseLine(string line, string origin)
    {
        var f = line.Split('|', 4).Select(p => p.Trim()).ToArray();
        if (f.Length != 4) throw new FormatException($"{f.Length} fields, want 4: id | value | when | why");
        if (f[0].Length == 0 || f[1].Length == 0) throw new FormatException("an id and a value are needed");
        var when = f[2] switch
        {
            "auto" => TuneUpWhen.Auto,
            "ask" => TuneUpWhen.Ask,
            var w => throw new FormatException($"when '{w}': auto or ask"),
        };
        if (f[3].Length == 0) throw new FormatException($"{f[0]}: say why it's recommended");
        return new Recommendation(f[0], f[1], when, f[3], origin);
    }

    // Each recommendation against the catalog: the setting exists, the value is one it takes, and
    // only a setting that needs no admin is set by itself (no admin by default: an admin change is
    // always asked, and all of them then go in the one prompt).
    public static List<Catalog.Problem> Check(IEnumerable<Recommendation> recs, IEnumerable<SettingEntry> catalog)
    {
        var byId = catalog.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var problems = new List<Catalog.Problem>();
        foreach (var r in recs)
        {
            if (!byId.TryGetValue(r.Id, out var e)) { problems.Add(new(r.Origin, $"no setting '{r.Id}' in settings.txt")); continue; }
            if (ValueProblem(e, r.Value) is { } why) problems.Add(new(r.Origin, $"{r.Id}: {why}"));
            if (r.When == TuneUpWhen.Auto && e.Admin) problems.Add(new(r.Origin, $"{r.Id} needs admin, so it can only be 'ask'"));
            if (r.When == TuneUpWhen.Auto && e.Takes.HasFlag(Takes.Restart)) problems.Add(new(r.Origin, $"{r.Id} takes a restart, so it can only be 'ask'"));
        }
        return problems;
    }

    // Whether a setting of this kind takes this value; null when it does.
    public static string? ValueProblem(SettingEntry e, string value) => e.Kind switch
    {
        SettingKind.Switch => value is "on" or "off" ? null : $"a switch is on or off, not '{value}'",
        SettingKind.Choice => e.Choices.Count == 0 || e.Choices.Any(c => c.Value == value) ? null
            : $"'{value}' isn't one of {string.Join(", ", e.Choices.Select(c => c.Value))}",
        SettingKind.Slider => int.TryParse(value, out var n) && e.Range is { } range && n >= range.Min && n <= range.Max ? null
            : $"'{value}' isn't a number in {e.Range?.Min}..{e.Range?.Max}",
        _ => $"a {e.Kind.ToString().ToLowerInvariant()} has no value to recommend",
    };

    // Where each recommendation stands on this PC. current reads a setting's value as the catalog
    // spells it ("on", "100"), or null when it can't be read; unverified: show "check" lines anyway.
    public static List<TuneUpRow> Report(IEnumerable<Recommendation> recs, IEnumerable<SettingEntry> catalog,
        Func<SettingEntry, string?> current, int build, bool unverified = false)
    {
        var byId = catalog.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<TuneUpRow>();
        foreach (var r in recs)
        {
            if (!byId.TryGetValue(r.Id, out var e)) continue; // Check reports it
            if ((e.Unverified && !unverified) || !e.FitsBuild(build)) { rows.Add(new(r, e, TuneUpState.Hidden, null)); continue; }
            string? now;
            try { now = current(e); }
            catch (Exception) { now = null; } // one setting that can't be read doesn't stop the list
            var state = now is null ? TuneUpState.Unreadable
                : string.Equals(now, r.Value, StringComparison.OrdinalIgnoreCase) ? TuneUpState.Applied : TuneUpState.NotApplied;
            rows.Add(new(r, e, state, now));
        }
        return rows;
    }

    // What `omawin tune-up apply` sets: everything recommended that isn't applied yet (the 'ask'
    // ones too, since you asked), or with autoOnly, only the 'auto' ones (install and update).
    public static List<TuneUpRow> ToApply(IEnumerable<TuneUpRow> rows, bool autoOnly) =>
        rows.Where(r => r.State == TuneUpState.NotApplied && (!autoOnly || r.Rec.When == TuneUpWhen.Auto)).ToList();

    // The report for a terminal: what's applied, what's left (and how to apply it), each with why.
    public static string Format(IReadOnlyList<TuneUpRow> rows)
    {
        var shown = rows.Where(r => r.State != TuneUpState.Hidden).ToList();
        var sb = new System.Text.StringBuilder();
        int applied = shown.Count(r => r.State == TuneUpState.Applied);
        sb.AppendLine($"omawin's recommended Windows settings: {applied} of {shown.Count} applied");
        foreach (var r in shown)
        {
            var mark = r.State switch { TuneUpState.Applied => "ok  ", TuneUpState.NotApplied => "--  ", _ => "??  " };
            var value = Label(r.Entry, r.Rec.Value);
            var now = r.State == TuneUpState.NotApplied ? $" (now {Label(r.Entry, r.Current!)})"
                : r.State == TuneUpState.Unreadable ? " (can't read it here)" : "";
            var admin = r.Entry.Admin ? ", admin" : "";
            sb.AppendLine($"{mark}{r.Entry.Name}: {value}{now}{admin}");
            if (r.State != TuneUpState.Applied) sb.AppendLine($"      {r.Rec.Why}");
        }
        var left = ToApply(rows, autoOnly: false);
        if (left.Count > 0)
            sb.AppendLine().Append($"omawin tune-up apply sets the {left.Count} left")
              .Append(left.Any(r => r.Entry.Admin) ? $" ({left.Count(r => r.Entry.Admin)} of them in one admin prompt)" : "")
              .AppendLine("; each is undone like any setting (omawin setting undo).");
        int hidden = rows.Count - shown.Count;
        if (hidden > 0) sb.AppendLine($"{hidden} more wait to be checked on this Windows before they're offered.");
        return sb.ToString();
    }

    // A value as the setting shows it: a choice's label, a switch's on / off.
    static string Label(SettingEntry e, string value) =>
        e.Choices.FirstOrDefault(c => c.Value == value)?.Label ?? value;
}
