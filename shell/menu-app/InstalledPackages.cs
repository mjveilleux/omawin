using System.Text.RegularExpressions;

namespace SystemMenu;

// Install › Remove's list (roadmap 2.10): everything installed, by any manager, with where it came
// from, as `omawin pkg list --tsv` says (src/cli/Pkg: %LOCALAPPDATA%\launcher\packages.tsv, asked
// of every manager the first time). Enter removes through the manager that put it in
// (`omawin pkg remove <source>:<id>`, in a terminal you watch). Pure C#: tested in menu-app.Tests.
public sealed record InstalledPackage(string Name, string Source, string Id, string Version, bool Yours)
{
    // What `omawin pkg remove` finds it by: its manager and id, so two managers' copies of one
    // thing are never mixed up.
    public string Key => Source + ":" + Id;
}

public enum Removal { Manager, Core, Own }

public static class InstalledPackages
{
    // The sources Tab steps through (all first); "other" is any source without a chip of its own.
    public static readonly string[] Filters = ["all", "winget", "scoop", "choco", "mise", "bundle", "other"];
    static readonly string[] Managed = ["winget", "scoop", "choco", "mise"];

    // `omawin pkg list --tsv`: name, source, id, version, by. Anything else it prints isn't a row.
    public static List<InstalledPackage> Parse(IEnumerable<string> lines)
    {
        var list = new List<InstalledPackage>();
        foreach (var raw in lines)
        {
            var f = raw.TrimEnd('\r').Split('\t');
            if (f.Length < 3 || f[0].Trim().Length == 0 || f[1].Trim().Length == 0) continue;
            string At(int i) => i < f.Length ? f[i].Trim() : "";
            list.Add(new InstalledPackage(At(0), At(1).ToLowerInvariant(), At(2), At(3), At(4) == "found" || At(1) == "found"));
        }
        return [.. list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Source, StringComparer.Ordinal)];
    }

    // How it comes off: its manager does it; omawin's core (the tools bundle) stays; anything
    // else (its own installer, found here) has its own uninstaller in Windows' Installed apps.
    public static Removal How(InstalledPackage p) =>
        Managed.Contains(p.Source) ? Removal.Manager : p.Source == "bundle" ? Removal.Core : Removal.Own;

    public static bool InFilter(InstalledPackage p, string filter) => filter switch
    {
        "all" => true,
        "other" => !Filters.Contains(p.Source),
        _ => p.Source == filter,
    };

    // What each source is called on screen.
    public static string Label(string source) => source switch
    {
        "winget" => "winget",
        "scoop" => "Scoop",
        "choco" => "Chocolatey",
        "mise" => "mise",
        "bundle" => "omawin's tools",
        "script" => "its own installer",
        "found" => "found here",
        "all" => "All",
        "other" => "Other",
        _ => source,
    };

    // What the row says it'll do, under its name.
    public static string Says(InstalledPackage p) => How(p) switch
    {
        Removal.Manager => $"{Label(p.Source)} removes it" + (p.Source == "choco" ? " (Chocolatey asks for admin)" : "") + (p.Yours ? "; you installed it yourself" : ""),
        Removal.Core => "part of omawin's core: it stays (omawin pkg move puts your own copy first)",
        _ => "its own uninstaller: Windows' Installed apps",
    };

    // Ids that are safe on a command line: what the managers' ids look like (winget's
    // Publisher.App, scoop's bucket/app, mise's aqua:owner/repo), nothing that could end the
    // command or start another.
    static readonly Regex SafeKey = new(@"^[A-Za-z0-9][A-Za-z0-9._+@/:-]*$");

    // The command for these (the ones a manager can remove, with safe ids), or null for none.
    public static string? RemoveCommand(IEnumerable<InstalledPackage> picked)
    {
        var keys = picked.Where(p => How(p) == Removal.Manager && SafeKey.IsMatch(p.Key) && p.Key.Length < 200)
            .Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return keys.Count == 0 ? null : "omawin pkg remove " + string.Join(' ', keys);
    }
}
