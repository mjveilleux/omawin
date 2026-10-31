namespace Omawin.Cli.Pkg;

// Which manager a package comes from, decided before anything runs.
public abstract record Choice;
public sealed record UseManager(string Manager, string Id) : Choice;            // install this id there
public sealed record SearchIn(IReadOnlyList<string> Managers, string Query) : Choice; // not a name omawin knows: look it up, in this order
public sealed record CantChoose(string Message) : Choice;

public static class Choose
{
    // In order: --from; your pin for this name (omawin pkg move); omawin's own pick for it
    // (names.txt's prefer); your order (omawin pkg prefer); then every manager. The first one on
    // this PC that has the name wins. "manager:id" (as omawin pkg list shows a second copy) is --from.
    public static Choice Manager(string typed, string? from, string? pin, IReadOnlyList<string> prefer, NameTable names, Func<string, bool> present)
    {
        var name = typed;
        if (from == null && typed.IndexOf(':') is > 0 and var colon && Managers.IsInstallable(typed[..colon]))
        {
            from = typed[..colon];
            name = typed[(colon + 1)..];
        }
        var known = names[name];
        if (from != null)
        {
            if (!Managers.IsInstallable(from)) return new CantChoose($"no package manager \"{from}\" ({string.Join(", ", Managers.Installable)})");
            if (!present(from)) return new CantChoose($"{Managers.Label(from)} isn't on this PC (omawin pkg install {from} --from winget, or the launcher's Packages page)");
            if (known == null) return new SearchIn([from], name);
            return known.IdIn(from) is { } id ? new UseManager(from, id)
                : new CantChoose($"omawin doesn't know {name} in {Managers.Label(from)}: omawin pkg install <its {Managers.Label(from)} id> --from {from}");
        }
        var order = new List<string>();
        if (pin != null) order.Add(pin);
        if (known?.Prefer != null) order.Add(known.Prefer);
        order.AddRange(prefer);
        order.AddRange(Managers.Installable);
        var candidates = order.Where(Managers.IsInstallable).Distinct().Where(present).ToList();
        if (candidates.Count == 0) return new CantChoose("no package manager is on this PC (winget comes with Windows: the App Installer from the Store)");
        if (known == null) return new SearchIn(candidates, name);
        foreach (var m in candidates)
            if (known.IdIn(m) is { } id) return new UseManager(m, id);
        var where = Managers.Installable.Where(m => known.IdIn(m) != null).Select(Managers.Label).ToList();
        return new CantChoose(where.Count == 0
            ? $"{name} comes with omawin's tools bundle only (omawin install)"
            : $"{name} is in {string.Join(", ", where)}, and none of them is on this PC");
    }

    // Your order (pkg-prefer.txt): only managers you can install with, each once.
    public static List<string> Prefer(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim().ToLowerInvariant()).Where(Managers.IsInstallable).Distinct().ToList();

    // pkg-pins.tsv: name <TAB> manager.
    public static string? Pin(IEnumerable<string> lines, string name) =>
        lines.Select(l => l.Split('\t')).Where(f => f.Length >= 2 && string.Equals(f[0].Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(f => f[1].Trim()).LastOrDefault(Managers.IsInstallable);

    public static IEnumerable<string> SetPin(IEnumerable<string> lines, string name, string manager) =>
        lines.Where(l => l.Trim().Length > 0 && !string.Equals(l.Split('\t')[0].Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Append(name + "\t" + manager);

    // The search hit to install without asking: the one whose id (or name, or Scoop app without
    // its bucket) is exactly what was typed. A near match is never installed unasked: null, ask.
    public static Installed? Exact(IReadOnlyList<Installed> hits, string query)
    {
        var exact = hits.Where(h => string.Equals(h.Id, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        exact = hits.Where(h => string.Equals(h.Name, query, StringComparison.OrdinalIgnoreCase) || string.Equals(h.Id.Split('/')[^1], query, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count == 1 ? exact[0] : null;
    }

    // What every manager's command line can carry: ids, versions, buckets ("extras/neovide"),
    // never a space, quote or shell character. Typed names are checked against it before they
    // reach a command line (Chocolatey and Scoop run through cmd.exe and PowerShell).
    public static bool SafeId(string id) =>
        id.Length is > 0 and < 200 && char.IsAsciiLetterOrDigit(id[0]) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '/' or '@');
}
