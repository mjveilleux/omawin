namespace Omawin.Cli.Pkg;

// One row of %LOCALAPPDATA%\launcher\packages.tsv: where a package on this PC came from.
//   name, source, id, version, folder   (install/lib.ps1, Set-PackageSource, writes these five)
//   scope, by                           (omawin pkg adds these two; lib.ps1 carries them along)
// source: bundle, winget, scoop, choco, mise, npm, script (its own installer), found (yours,
// from somewhere omawin can't tell). by: omawin, or found when a manager listed it and omawin
// didn't put it in; found ones are yours, and `omawin pkg update` leaves them alone.
public sealed record PackageRow(string Name, string Source, string Id, string Version = "", string Folder = "", string Scope = "", string By = "omawin")
{
    public bool Yours => By == "found" || Source == "found";
    public string Key => Source + ":" + Id;
}

public static class PackageRecord
{
    public const string Header = "# where each package came from (omawin pkg update updates it there): name, source, id, version, folder, scope, by";

    public static List<PackageRow> Parse(IEnumerable<string> lines)
    {
        var rows = new List<PackageRow>();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length < 3 || f[0].Length == 0) continue;
            string At(int i) => i < f.Length ? f[i].Trim() : "";
            var by = At(6);
            rows.Add(new PackageRow(f[0].Trim(), At(1), At(2), At(3), At(4), At(5), by.Length > 0 ? by : f[1].Trim() == "found" ? "found" : "omawin"));
        }
        return rows;
    }

    // Sorted by name, so the file reads as a list and diffs stay small.
    public static IEnumerable<string> Format(IEnumerable<PackageRow> rows) =>
        new[] { Header }.Concat(rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Source, StringComparer.Ordinal)
            .Select(r => string.Join('\t', Clean(r.Name), Clean(r.Source), Clean(r.Id), Clean(r.Version), Clean(r.Folder), Clean(r.Scope), Clean(r.By))));

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // The rows a name the user typed means: the row's name, its id, "source:id", or the id
    // names.txt gives that name in the row's manager (a winget row named by its id is "python").
    public static List<PackageRow> Find(IEnumerable<PackageRow> rows, string typed, NameTable names)
    {
        var known = names[typed];
        return rows.Where(r =>
            string.Equals(r.Name, typed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Key, typed, StringComparison.OrdinalIgnoreCase) ||
            (r.Source != Managers.Bundle && string.Equals(r.Id, typed, StringComparison.OrdinalIgnoreCase)) ||
            (known != null && string.Equals(known.IdIn(r.Source), r.Id, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    // A name for a package a manager listed that the record doesn't have: names.txt's name for it,
    // else its id, else "manager:id" (always free: a manager's ids are unique), so two managers'
    // copies of one thing get two rows.
    public static string NameFor(string manager, string id, NameTable names, ISet<string> taken)
    {
        if (names.ByManagerId(manager, id)?.Name is { } name && !taken.Contains(name)) return name;
        if (!taken.Contains(id)) return id;
        return manager + ":" + id;
    }

    // packages.tsv after asking each manager what it has (omawin pkg list --refresh). listed holds
    // only the managers that answered: rows of the others stay as they were. A row a manager no
    // longer lists is dropped (removed outside omawin), except winget's: its export leaves out a
    // package whose installed version its catalogue no longer has, so absence there proves nothing.
    // Packages listed that the record hasn't got come in as found (yours, left alone by update).
    public static List<PackageRow> Merge(IReadOnlyList<PackageRow> rows, IReadOnlyDictionary<string, IReadOnlyList<Installed>> listed, NameTable names)
    {
        var result = new List<PackageRow>();
        foreach (var row in rows)
        {
            if (!listed.TryGetValue(row.Source, out var now)) { result.Add(row); continue; }
            var here = now.FirstOrDefault(p => string.Equals(p.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (here != null) result.Add(row with { Version = here.Version.Length > 0 ? here.Version : row.Version });
            else if (row.Source == Managers.Winget) result.Add(row);
        }
        var taken = new HashSet<string>(result.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(result.Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
        // A package the install recorded as found (source "found", its id the winget id) and now
        // listed by winget becomes a winget row, still yours.
        foreach (var (manager, now) in listed.OrderBy(l => Array.IndexOf(Managers.Installable, l.Key)))
            foreach (var p in now)
            {
                if (!keys.Add(manager + ":" + p.Id)) continue;
                var wasFound = result.FindIndex(r => r.Source == "found" && string.Equals(r.Id, p.Id, StringComparison.OrdinalIgnoreCase));
                if (wasFound >= 0 && manager == Managers.Winget)
                {
                    result[wasFound] = result[wasFound] with { Source = manager, Version = p.Version, By = "found" };
                    continue;
                }
                var name = NameFor(manager, p.Id, names, taken);
                taken.Add(name);
                result.Add(new PackageRow(name, manager, p.Id, p.Version, By: "found"));
            }
        return result;
    }

    // installed.txt's lines (the Packages page's ✓ marks: winget:<id>, choco:<id>, scoop:<app>,
    // lowercase) for a source and id, or null for a manager the page doesn't show.
    public static string? InstalledKey(string source, string id) => source switch
    {
        Managers.Winget or Managers.Choco => (source + ":" + id).ToLowerInvariant(),
        Managers.Scoop => ("scoop:" + id.Split('/')[^1]).ToLowerInvariant(),
        _ => null,
    };
}

// A package a manager says it has.
public sealed record Installed(string Id, string Version, string Name = "");
