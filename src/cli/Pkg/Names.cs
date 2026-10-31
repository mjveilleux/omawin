namespace Omawin.Cli.Pkg;

// The package managers omawin pkg drives. Bundle is omawin's own tools bundle (install/tools.txt):
// it's installed and updated by omawin's install, so pkg reads it but never installs into it.
public static class Managers
{
    public const string Winget = "winget", Scoop = "scoop", Choco = "choco", Mise = "mise", Bundle = "bundle";

    // The ones you can install with, in omawin's default order of preference.
    public static readonly string[] Installable = [Winget, Scoop, Choco, Mise];

    public static string Label(string manager) => manager switch
    {
        Winget => "winget",
        Scoop => "Scoop",
        Choco => "Chocolatey",
        Mise => "mise",
        Bundle => "the tools bundle",
        "linux" => "Linux",
        _ => manager,
    };

    public static bool IsInstallable(string manager) => Installable.Contains(manager);
}

// One name and its id in each manager (default/packages/names.txt).
public sealed record PackageName(string Name, IReadOnlyDictionary<string, string> Ids, string? Command, string? Prefer, string? BundleName)
{
    public string? IdIn(string manager) => manager == Managers.Bundle ? BundleName : Ids.GetValueOrDefault(manager);
}

public sealed class NameTable
{
    // The columns of names.txt after the name, in order.
    static readonly string[] Columns = [Managers.Winget, Managers.Scoop, Managers.Choco, Managers.Mise, "arch", "apt"];

    readonly Dictionary<string, PackageName> byName = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<PackageName> All => byName.Values;

    public PackageName? this[string name] => byName.GetValueOrDefault(name);

    // names.txt's rows, then install/tools.txt's tools: one already named (by its winget id) is
    // marked bundled; one that isn't is added under the bundle's name.
    public static NameTable Parse(IEnumerable<string> names, IEnumerable<string> tools)
    {
        var table = new NameTable();
        foreach (var raw in names)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split('|').Select(x => x.Trim()).ToArray();
            if (f.Length < 2 || f[0].Length == 0) continue;
            var ids = new Dictionary<string, string>();
            for (int i = 0; i < Columns.Length; i++)
                if (Field(f, i + 1) is { } id) ids[Columns[i]] = id;
            table.byName[f[0]] = new PackageName(f[0], ids, Field(f, 7), Field(f, 8), null);
        }
        foreach (var raw in tools)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split('|').Select(x => x.Trim()).ToArray();
            if (f[0].Length == 0) continue;
            var winget = Field(f, 4);
            var known = table.byName.GetValueOrDefault(f[0])
                ?? (winget == null ? null : table.byName.Values.FirstOrDefault(n => string.Equals(n.IdIn(Managers.Winget), winget, StringComparison.OrdinalIgnoreCase)));
            if (known != null) table.byName[known.Name] = known with { BundleName = f[0] };
            else
            {
                var ids = new Dictionary<string, string>();
                if (winget != null) ids[Managers.Winget] = winget;
                table.byName[f[0]] = new PackageName(f[0], ids, f[0], null, f[0]);
            }
        }
        return table;
    }

    static string? Field(string[] f, int i) => i < f.Length && f[i].Length > 0 && f[i] != "-" ? f[i] : null;

    // The omawin name a manager's id belongs to, if names.txt has it.
    public PackageName? ByManagerId(string manager, string id) =>
        byName.Values.FirstOrDefault(n => string.Equals(n.IdIn(manager), id, StringComparison.OrdinalIgnoreCase));
}
