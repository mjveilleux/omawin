namespace SystemMenu.Tests.Packages;

// Install › Remove's list (shell/menu-app/InstalledPackages.cs), from `omawin pkg list --tsv`.
public class InstalledPackagesTests
{
    static readonly string[] Tsv =
    [
        "  asking winget ...",                                  // not a row
        "ripgrep\tbundle\tripgrep\t14.1.0\tomawin",
        "fd\tscoop\tfd\t10.2.0\tfound",
        "Firefox\twinget\tMozilla.Firefox\t131.0\tomawin",
        "go\tmise\tgo\t1.23.2\tomawin",
        "Thing\tscript\tthing\t\tomawin",
    ];

    [Fact]
    public void EveryRowWithItsSourceSortedByName()
    {
        var list = InstalledPackages.Parse(Tsv);
        Assert.Equal(["fd scoop", "Firefox winget", "go mise", "ripgrep bundle", "Thing script"], list.Select(p => $"{p.Name} {p.Source}"));
        Assert.True(list[0].Yours);
        Assert.Equal("winget:Mozilla.Firefox", list[1].Key);
    }

    [Fact]
    public void ManagersRemoveOmawinsCoreStaysTheRestHaveTheirOwnUninstaller()
    {
        var list = InstalledPackages.Parse(Tsv).ToDictionary(p => p.Name);
        Assert.Equal(Removal.Manager, InstalledPackages.How(list["go"]));
        Assert.Equal(Removal.Core, InstalledPackages.How(list["ripgrep"]));
        Assert.Equal(Removal.Own, InstalledPackages.How(list["Thing"]));
        Assert.Contains("you installed it yourself", InstalledPackages.Says(list["fd"]));
        Assert.Equal("omawin pkg remove scoop:fd winget:Mozilla.Firefox mise:go", InstalledPackages.RemoveCommand(list.Values));
        Assert.Null(InstalledPackages.RemoveCommand([list["ripgrep"], list["Thing"]]));
    }

    [Fact]
    public void AnIdThatCouldBreakTheCommandLineIsNeverOnIt()
    {
        var bad = new InstalledPackage("x", "winget", "a; Remove-Item ~ -Recurse", "", false);
        var quote = new InstalledPackage("y", "scoop", "y\"&calc", "", false);
        Assert.Null(InstalledPackages.RemoveCommand([bad, quote]));
        Assert.Equal("omawin pkg remove mise:aqua:cli/cli", InstalledPackages.RemoveCommand([new InstalledPackage("gh", "mise", "aqua:cli/cli", "", false)]));
    }

    [Fact]
    public void TheSourceFilters()
    {
        var list = InstalledPackages.Parse(Tsv);
        Assert.Equal(["Thing"], list.Where(p => InstalledPackages.InFilter(p, "other")).Select(p => p.Name));
        Assert.Equal(5, list.Count(p => InstalledPackages.InFilter(p, "all")));
        Assert.Equal(["ripgrep"], list.Where(p => InstalledPackages.InFilter(p, "bundle")).Select(p => p.Name));
    }
}
