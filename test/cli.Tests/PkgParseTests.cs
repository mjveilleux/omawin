using Omawin.Cli.Pkg;

namespace Omawin.Cli.Tests;

// What each package manager prints, as it prints it, read the way omawin pkg reads it.
public class PkgParseTests
{
    [Fact]
    public void WingetExportGivesEveryPackageAcrossSources()
    {
        var json = """
            {"$schema":"https://aka.ms/winget-packages.schema.2.0.json","CreationDate":"2026-10-31T10:00:00",
             "Sources":[
              {"Packages":[{"PackageIdentifier":"Git.Git","Version":"2.47.0"},{"PackageIdentifier":"jqlang.jq","Version":"1.7.1"}],
               "SourceDetails":{"Name":"winget"}},
              {"Packages":[{"PackageIdentifier":"9NBLGGH4NNS1"}],"SourceDetails":{"Name":"msstore"}}]}
            """;
        Assert.Equal([new Installed("Git.Git", "2.47.0"), new Installed("jqlang.jq", "1.7.1"), new Installed("9NBLGGH4NNS1", "")], Parse.WingetExport(json));
    }

    [Fact]
    public void WingetExportThatFailedIsNothing() => Assert.Empty(Parse.WingetExport("Failed in attempting to update the source: winget"));

    // winget's spinner overwrites itself with carriage returns before the table starts, the Match
    // column has spaces in it, and ids too long for the column end in "…".
    [Fact]
    public void WingetSearchReadsColumnsByPosition()
    {
        var text = "   - \r   \\ \r" +
            "Name                 Id                          Version Match        Source\n" +
            "------------------------------------------------------------------------------\n" +
            "RipGrep MSVC         BurntSushi.ripgrep.MSVC     14.1.1  Command: rg  winget\n" +
            "RipGrep GNU          BurntSushi.ripgrep.GNU      14.1.1  Command: rg  winget\n" +
            "Some Long Name       Publisher.AVeryLongPackage… 1.0                  winget\n";
        Assert.Equal([new Installed("BurntSushi.ripgrep.MSVC", "14.1.1", "RipGrep MSVC"), new Installed("BurntSushi.ripgrep.GNU", "14.1.1", "RipGrep GNU")],
            Parse.WingetTable(text));
    }

    [Fact]
    public void WingetUpgradeGivesTheVersionWaitingAndSkipsTheFooterAndTheSecondTable()
    {
        var text =
            "Name   Id        Version Available Source\n" +
            "-----------------------------------------\n" +
            "Git    Git.Git   2.44.0  2.47.0    winget\n" +
            "jq     jqlang.jq 1.7.0   1.7.1     winget\n" +
            "2 upgrades available.\n" +
            "\n" +
            "The following packages have an upgrade available, but require explicit targeting for upgrade:\n" +
            "Name   Id        Version Available Source\n" +
            "-----------------------------------------\n" +
            "Zoom   Zoom.Zoom 5.0     6.0       winget\n";
        Assert.Equal([new Installed("Git.Git", "2.47.0", "Git"), new Installed("jqlang.jq", "1.7.1", "jq")], Parse.WingetTable(text, available: true));
    }

    [Fact]
    public void WingetTableInAnotherLanguageStillReads()
    {
        var text = "Nom    ID        Version Source\n-------------------------------\nGit    Git.Git   2.47.0  winget\n";
        Assert.Equal([new Installed("Git.Git", "2.47.0", "Git")], Parse.WingetTable(text));
    }

    [Fact]
    public void ScoopExportJsonAndTheOlderLines()
    {
        var json = """{"buckets":[{"Name":"main"}],"apps":[{"Name":"ripgrep","Version":"14.1.1","Source":"main"},{"Name":"neovide","Version":"0.13.3","Source":"extras"}]}""";
        Assert.Equal([new Installed("ripgrep", "14.1.1"), new Installed("neovide", "0.13.3")], Parse.ScoopExport(json));
        Assert.Equal([new Installed("ripgrep", "14.1.1"), new Installed("7zip", "24.08")], Parse.ScoopExport("ripgrep (v:14.1.1) [main]\n7zip (v:24.08) *global* [main]\n"));
    }

    // PowerShell's Format-Table: a run of dashes per column, so a header with a space in it is
    // one column.
    [Fact]
    public void ScoopStatusGivesTheLatestVersion()
    {
        var text =
            "\n" +
            "Name    Installed Version Latest Version Missing Dependencies Info\n" +
            "----    ----------------- -------------- -------------------- ----\n" +
            "git     2.44.0            2.47.0\n" +
            "fd      10.1.0            10.2.0                              Held package\n" +
            "\n";
        Assert.Equal([new Installed("git", "2.47.0"), new Installed("fd", "10.2.0")], Parse.ScoopTable(text, status: true));
    }

    [Fact]
    public void ScoopSearchGivesNameVersionAndBucket()
    {
        var text = "Results from local buckets...\n\nName    Version Source Binaries\n----    ------- ------ --------\nripgrep 14.1.1  main\nrga     0.10.6  main   rga.exe\n";
        Assert.Equal([new Installed("ripgrep", "14.1.1", "main"), new Installed("rga", "0.10.6", "main")], Parse.ScoopTable(text));
    }

    [Fact]
    public void ChocolateyLimitedOutputSkipsItsWarnings()
    {
        Assert.Equal([new Installed("git", "2.47.0"), new Installed("jq", "1.7.1")],
            Parse.ChocoLimited("Chocolatey v2.3.0\ngit|2.47.0\njq|1.7.1\nWARNING: something\n"));
        Assert.Equal([new Installed("git", "2.47.1")], Parse.ChocoLimited("git|2.47.0|2.47.1|false\n", outdated: true));
    }

    [Fact]
    public void MiseListsInstalledVersionsOnly()
    {
        var json = """
            {"node":[{"version":"22.11.0","requested_version":"lts","installed":true,"active":true}],
             "go":[{"version":"1.23.2","installed":true},{"version":"1.24.0","installed":false}]}
            """;
        Assert.Equal([new Installed("node", "22.11.0"), new Installed("go", "1.23.2")], Parse.MiseList(json));
        Assert.Equal([new Installed("node", "23.1.0")], Parse.MiseOutdated("""{"node":{"requested":"latest","current":"22.11.0","latest":"23.1.0"}}"""));
    }

    [Fact]
    public void TheTailSkipsProgressBars() =>
        Assert.Equal("Installer failed with exit code: 1603", Parse.Tail(["Downloading", "  ██████████▒▒▒  1.0 MB / 2.0 MB", "Installer failed with exit code: 1603", "  ", "\\"]));
}
