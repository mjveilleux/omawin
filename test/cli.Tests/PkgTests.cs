using Omawin.Cli.Pkg;

namespace Omawin.Cli.Tests;

// omawin pkg's decisions: names, the record, which manager, the command lines.
public class PkgTests
{
    static readonly NameTable Names = NameTable.Parse(
        [
            "# comment",
            "python | Python.Python.3.13 | python | python | python | python | python3 | python | mise",
            "ripgrep | BurntSushi.ripgrep.MSVC | ripgrep | ripgrep | ripgrep | ripgrep | ripgrep | rg | -",
            "windows-terminal | Microsoft.WindowsTerminal | windows-terminal | microsoft-windows-terminal | - | - | - | wt | -",
            "handy | cjpais.Handy | - | - | - | - | - | handy | -",
        ],
        [
            "# the bundle",
            "ripgrep     | github:BurntSushi/ripgrep | ^rg\\.zip$ | . | BurntSushi.ripgrep.MSVC",
            "wt          | github:microsoft/terminal | ^wt\\.zip$ | . | Microsoft.WindowsTerminal",
            "noctty      | github:x/noctty | ^n\\.zip$ | . |",
        ]);

    static bool All(string _) => true;

    [Fact]
    public void NamesMergeTheBundlesToolsByWingetId()
    {
        Assert.Equal("ripgrep", Names["ripgrep"]!.BundleName);
        Assert.Equal("wt", Names["windows-terminal"]!.BundleName);
        Assert.Null(Names["wt"]);
        Assert.Equal("noctty", Names["noctty"]!.IdIn(Managers.Bundle));
        Assert.Null(Names["handy"]!.IdIn(Managers.Scoop));
        Assert.Equal("python", Names.ByManagerId(Managers.Winget, "python.python.3.13")!.Name);
    }

    [Fact]
    public void TheShippedNamesFileParsesWithEveryRowFull()
    {
        var root = FindRepo();
        var lines = File.ReadAllLines(Path.Combine(root, "default", "packages", "names.txt"));
        var rows = lines.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        Assert.All(rows, l => Assert.Equal(9, l.Split('|').Length));
        var table = NameTable.Parse(lines, File.ReadAllLines(Path.Combine(root, "install", "tools.txt")));
        Assert.All(table.All.Where(n => n.Prefer != null), n => Assert.NotNull(n.IdIn(n.Prefer!)));
        Assert.All(table.All.SelectMany(n => n.Ids.Values), id => Assert.True(Choose.SafeId(id), id));
    }

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "bin", "commands.txt"))) return d.FullName;
        throw new InvalidOperationException("repo not found");
    }

    // --- choosing a manager ---

    [Fact]
    public void FromWinsThenThePinThenTheNamesPickThenYourOrder()
    {
        Assert.Equal(new UseManager("scoop", "python"), Choose.Manager("python", "scoop", "winget", [], Names, All));
        Assert.Equal(new UseManager("winget", "Python.Python.3.13"), Choose.Manager("python", null, "winget", [], Names, All));
        Assert.Equal(new UseManager("mise", "python"), Choose.Manager("python", null, null, ["scoop"], Names, All));
        Assert.Equal(new UseManager("scoop", "ripgrep"), Choose.Manager("ripgrep", null, null, ["scoop", "winget"], Names, All));
        Assert.Equal(new UseManager("winget", "BurntSushi.ripgrep.MSVC"), Choose.Manager("ripgrep", null, null, [], Names, All));
    }

    [Fact]
    public void AManagerNotOnThisPcIsPassedOver()
    {
        Assert.Equal(new UseManager("winget", "Python.Python.3.13"), Choose.Manager("python", null, null, [], Names, m => m != "mise"));
        Assert.IsType<CantChoose>(Choose.Manager("python", "mise", null, [], Names, m => m != "mise"));
        // handy is only in winget.
        Assert.Equal(new UseManager("winget", "cjpais.Handy"), Choose.Manager("handy", null, null, ["scoop", "choco"], Names, All));
        Assert.Contains("winget", Assert.IsType<CantChoose>(Choose.Manager("handy", null, null, [], Names, m => m == "scoop")).Message);
        Assert.IsType<CantChoose>(Choose.Manager("handy", "scoop", null, [], Names, All));
    }

    [Fact]
    public void AnUnknownNameIsSearchedForInOrder()
    {
        var s = Assert.IsType<SearchIn>(Choose.Manager("lazysql", null, null, ["scoop"], Names, m => m != "choco"));
        Assert.Equal(["scoop", "winget", "mise"], s.Managers);
        s = Assert.IsType<SearchIn>(Choose.Manager("lazysql", "choco", null, [], Names, All));
        Assert.Equal(["choco"], s.Managers);
        Assert.Equal("lazysql", s.Query);
    }

    [Fact]
    public void ManagerColonIdIsFrom()
    {
        var s = Assert.IsType<SearchIn>(Choose.Manager("scoop:fd", null, null, [], Names, All));
        Assert.Equal(["scoop"], s.Managers);
        Assert.Equal("fd", s.Query);
    }

    [Fact]
    public void OnlyAnExactHitIsInstalledUnasked()
    {
        Installed[] hits = [new("BurntSushi.ripgrep.MSVC", "14", "RipGrep MSVC"), new("BurntSushi.ripgrep.GNU", "14", "RipGrep GNU")];
        Assert.Equal(hits[0], Choose.Exact(hits, "burntsushi.ripgrep.msvc"));
        Assert.Null(Choose.Exact(hits, "ripgrep"));
        Assert.Null(Choose.Exact([new("rga", "1")], "rg"));
        Assert.Equal("extras/neovide", Choose.Exact([new("extras/neovide", "1")], "neovide")!.Id);
    }

    [Fact]
    public void PreferAndPinsAreReadAndWritten()
    {
        Assert.Equal(["scoop", "winget"], Choose.Prefer(["Scoop", "", "bundle", "winget", "scoop"]));
        var pins = Choose.SetPin(["fd\twinget", "go\tmise"], "fd", "scoop").ToList();
        Assert.Equal(["go\tmise", "fd\tscoop"], pins);
        Assert.Equal("scoop", Choose.Pin(pins, "FD"));
        Assert.Null(Choose.Pin(["x\tnonsense"], "x"));
    }

    [Theory]
    [InlineData("BurntSushi.ripgrep.MSVC", true)]
    [InlineData("extras/neovide", true)]
    [InlineData("python@3.12", true)]
    [InlineData("a b", false)]
    [InlineData("x&calc", false)]
    [InlineData("-y", false)]
    [InlineData("\"q\"", false)]
    public void SafeIds(string id, bool safe) => Assert.Equal(safe, Choose.SafeId(id));

    // --- the record ---

    [Fact]
    public void TheRecordReadsLibPs1sFiveColumnsAndWritesSeven()
    {
        var rows = PackageRecord.Parse(["# header", "ripgrep\tbundle\tripgrep\t14.1.1\tC:\\x\\ripgrep", "Python.Python.3.13\twinget\tPython.Python.3.13", "Zoom.Zoom\tfound\tZoom.Zoom", "", "fd\tscoop\tfd\t10\t\tuser\tfound"]);
        Assert.Equal(4, rows.Count);
        Assert.Equal(new PackageRow("ripgrep", "bundle", "ripgrep", "14.1.1", "C:\\x\\ripgrep"), rows[0]);
        Assert.False(rows[1].Yours);
        Assert.True(rows[2].Yours);
        Assert.True(rows[3].Yours);
        var written = PackageRecord.Format(rows).ToList();
        Assert.StartsWith("#", written[0]);
        Assert.Equal("fd\tscoop\tfd\t10\t\tuser\tfound", written[1]);
        Assert.Equal(rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase), PackageRecord.Parse(written));
    }

    [Fact]
    public void ANameFindsItsRowsHoweverTheyWereNamed()
    {
        List<PackageRow> rows = [new("Python.Python.3.13", "winget", "Python.Python.3.13"), new("scoop:python", "scoop", "python"), new("ripgrep", "bundle", "ripgrep"), new("jq", "winget", "jqlang.jq")];
        Assert.Equal(2, PackageRecord.Find(rows, "python", Names).Count);
        Assert.Single(PackageRecord.Find(rows, "scoop:python", Names));
        Assert.Single(PackageRecord.Find(rows, "jqlang.jq", Names));
        Assert.Single(PackageRecord.Find(rows, "ripgrep", Names));
    }

    [Fact]
    public void ARefreshKeepsWhatOmawinPutInAndAddsWhatsYours()
    {
        List<PackageRow> rows =
        [
            new("ripgrep", "bundle", "ripgrep", "14.1.1"),
            new("Python.Python.3.13", "winget", "Python.Python.3.13", "3.13.0"),
            new("Pinned.Old", "winget", "Pinned.Old", "1.0"),              // not in winget's export: kept
            new("fd", "scoop", "fd", "10.0"),                              // gone from scoop: dropped
            new("Zoom.Zoom", "found", "Zoom.Zoom"),                        // the install's "found": now winget's
            new("go", "mise", "go", "1.23"),                               // mise didn't answer: as it was
        ];
        var listed = new Dictionary<string, IReadOnlyList<Installed>>
        {
            ["winget"] = [new("Python.Python.3.13", "3.13.1"), new("Zoom.Zoom", "6.0"), new("BurntSushi.ripgrep.MSVC", "14.1.1"), new("Git.Git", "2.47.0")],
            ["scoop"] = [new("ripgrep", "14.1.1"), new("python", "3.12.0")],
        };
        var merged = PackageRecord.Merge(rows, listed, Names).ToDictionary(r => r.Key);
        Assert.Equal("3.13.1", merged["winget:Python.Python.3.13"].Version);
        Assert.Equal("omawin", merged["winget:Python.Python.3.13"].By);
        Assert.True(merged.ContainsKey("winget:Pinned.Old"));
        Assert.False(merged.ContainsKey("scoop:fd"));
        Assert.Equal(new PackageRow("Zoom.Zoom", "winget", "Zoom.Zoom", "6.0", By: "found"), merged["winget:Zoom.Zoom"]);
        Assert.Equal("1.23", merged["mise:go"].Version);
        // Second copies of a name get their own: ripgrep is the bundle's, so winget's goes by its id.
        Assert.Equal("BurntSushi.ripgrep.MSVC", merged["winget:BurntSushi.ripgrep.MSVC"].Name);
        Assert.Equal("scoop:ripgrep", merged["scoop:ripgrep"].Name);
        Assert.Equal("python", merged["scoop:python"].Name);
        Assert.Equal("Git.Git", merged["winget:Git.Git"].Name);
        Assert.All(merged.Values.Where(r => r.Source is "winget" or "scoop" && r.Id != "Python.Python.3.13" && r.Id != "Pinned.Old"), r => Assert.True(r.Yours));
        Assert.Equal(merged.Count, merged.Values.Select(r => r.Name.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void InstalledKeysMatchThePackagesPage()
    {
        Assert.Equal("winget:git.git", PackageRecord.InstalledKey("winget", "Git.Git"));
        Assert.Equal("scoop:neovide", PackageRecord.InstalledKey("scoop", "extras/neovide"));
        Assert.Null(PackageRecord.InstalledKey("mise", "go"));
    }

    // --- command lines ---

    [Fact]
    public void WingetInstallsPerUserUnlessAsked()
    {
        var a = CommandLines.Install("winget", "jqlang.jq", null);
        Assert.Equal(["install", "--id", "jqlang.jq", "--exact", "--scope", "user"], a[..6]);
        Assert.Contains("--disable-interactivity", a);
        Assert.Equal("machine", CommandLines.Install("winget", "x", "machine")[5]);
        Assert.Equal(["use", "--global", "go@latest"], CommandLines.Install("mise", "go", null));
        Assert.True(CommandLines.NeedsAdmin("choco", null));
        Assert.False(CommandLines.NeedsAdmin("winget", "user"));
    }

    [Fact]
    public void WingetsCodesAreRead()
    {
        Assert.True(CommandLines.Winget(unchecked((int)0x8A150061)).Ok);
        Assert.True(CommandLines.Winget(unchecked((int)0x8A15002B)).Ok);
        Assert.False(CommandLines.Winget(unchecked((int)0x8A150101)).Ok);
        Assert.Equal("winget exit 0x80070005", CommandLines.Winget(unchecked((int)0x80070005)).Text);
        Assert.True(CommandLines.Gone("winget", unchecked((int)0x8A150014)));
        Assert.False(CommandLines.Gone("scoop", 1));
    }

    [Fact]
    public void LinuxPackagesAreArgumentsNeverPartOfTheScript()
    {
        var a = CommandLines.Linux("install", ["fd", "ripgrep"]);
        Assert.Equal(["--", "sh", "-c"], a[..3]);
        Assert.DoesNotContain("fd", a[3]);
        Assert.Equal(["omawin-pkg", "fd", "ripgrep"], a[4..]);
    }

    // --- arguments ---

    [Fact]
    public void FlagsTakeValuesEitherWay()
    {
        var a = PkgArgs.Parse(["install", "go", "--from", "Mise", "fd", "--machine"], out var error);
        Assert.Null(error);
        Assert.Equal(("install", "mise", "machine"), (a.Verb, a.From, a.Scope));
        Assert.Equal(["go", "fd"], a.Names);
        Assert.Equal("scoop", PkgArgs.Parse(["move", "fd", "--to=scoop"], out _).To);
        Assert.Equal("list", PkgArgs.Parse(["update", "-List"], out _).Verb);
        PkgArgs.Parse(["install", "--from"], out error);
        Assert.NotNull(error);
        PkgArgs.Parse(["install", "x", "--frm", "y"], out error);
        Assert.Contains("--frm", error);
    }
}
