using Omawin.Bundle;

namespace Omawin.Cli.Tests;

public class BundleTests
{
    static string Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "install", "tools.txt"))) return d.FullName;
        throw new InvalidOperationException("install/tools.txt not found");
    }

    [Fact]
    public void TheShippedListParses()
    {
        var tools = Tools.Parse(File.ReadAllLines(Path.Combine(Repo(), "install", "tools.txt")));
        Assert.True(tools.Count >= 20);
        var jq = tools.Single(t => t.Name == "jq");
        Assert.Equal("jq.exe", jq.Rename);
        Assert.Matches(jq.Asset, "jq-windows-amd64.exe");
        Assert.Equal(@"Programs\noctty", tools.Single(t => t.Name == "noctty").Home);
        Assert.False(tools.Single(t => t.Name == "nvim").Later);
        var font = tools.Single(t => t.Name == "font");
        Assert.True(Tools.Keeps(font, "JetBrainsMonoNerdFont-Bold.ttf"));
        Assert.True(Tools.Keeps(font, "JetBrainsMonoNerdFont-BoldItalic.ttf"));
        Assert.False(Tools.Keeps(font, "JetBrainsMonoNerdFont-ExtraLight.ttf"));  // weights nothing uses
        Assert.False(Tools.Keeps(font, "JetBrainsMonoNerdFontMono-Bold.ttf"));   // the Mono and Propo families aren't used
        Assert.False(Tools.Keeps(font, "README.md"));
        Assert.True(Tools.Keeps(tools.Single(t => t.Name == "nvim"), "bin/nvim.exe"));   // no keep: everything
    }

    [Fact]
    public void EveryBundledWingetIdIsACorePackage()
    {
        // A typo here would install the tool twice: once from the bundle, once through winget.
        var packages = File.ReadAllText(Path.Combine(Repo(), "install", "packages.ps1"));
        foreach (var t in Tools.Parse(File.ReadAllLines(Path.Combine(Repo(), "install", "tools.txt"))).Where(t => t.Provides != null))
            Assert.Contains($"'{t.Provides}'", packages);
    }

    [Theory]
    [InlineData("a | github:o/r | ^x$ | ../bin | X")]   // bin climbs out
    [InlineData("A | github:o/r | ^x$ | . | X")]        // name shape
    [InlineData("a | github:o/r | ^x$")]                // too few fields
    [InlineData("a | github:o/r | ^x$ | . | X | ..\\up")] // home climbs out
    [InlineData("a | github:o/r | ^x$ | . | X | | soon")]    // an unknown flag
    [InlineData("a | github:o/r | ^x$ | . | X | | sfx keep:*.exe")] // a self-extractor goes out whole
    public void BadRowsAreRefused(string row) => Assert.Throws<FormatException>(() => Tools.Parse([row]));

    [Fact]
    public void ATopFolderIsStrippedOnlyWhenEverythingIsInIt()
    {
        Assert.Equal("nvim-win64", Tools.CommonTop(["nvim-win64/", "nvim-win64/bin/nvim.exe", "nvim-win64/share/x"]));
        Assert.Null(Tools.CommonTop(["rg.exe", "doc/rg.1"]));
        Assert.Null(Tools.CommonTop(["a/x", "b/y"]));
    }

    [Fact]
    public void ManifestLinesAreTabSeparatedWithEmptyOptionals() =>
        Assert.Equal("fd\tv10.2.0\tomawin-tool-fd.zip\tabc\t.\tsharkdp.fd\t\t", new Built("fd", "v10.2.0", Tools.FileName("fd"), "abc", ".", "sharkdp.fd", null).Line);

    // Git for Windows goes out as its own self-extractor (~60 MB, against ~160 as a zip of what's
    // inside), and the install runs it rather than tar: the flag reaches the manifest and the name.
    [Fact]
    public void ASelfExtractorKeepsItsExeAndSaysSo()
    {
        var git = Tools.Parse(File.ReadAllLines(Path.Combine(Repo(), "install", "tools.txt"))).Single(t => t.Name == "git");
        Assert.True(git.Sfx);
        Assert.Equal("omawin-tool-git.exe", Tools.FileName(git.Name, git.Sfx));
        Assert.EndsWith("\tsfx", new Built("git", "v2", "omawin-tool-git.exe", "abc", "cmd", "Git.Git", null, Sfx: true).Line);
        Assert.EndsWith("\tlater sfx", new Built("x", "v1", "f", "abc", ".", null, null, Later: true, Sfx: true).Line);
    }

    // git also goes out in four tar.zst parts, unpacked at once (9,616 files took 20 s through one
    // writer). Every file lands in exactly one part, the parts are even, every folder is in the first.
    [Fact]
    public void PartsCarryEveryFileOnceAndEvenly()
    {
        var git = Tools.Parse(File.ReadAllLines(Path.Combine(Repo(), "install", "tools.txt"))).Single(t => t.Name == "git");
        Assert.Equal(4, git.Parts);
        var files = Enumerable.Range(0, 9616).Select(i => $"usr/bin/f{i}.exe").ToList();
        var parts = Tools.Split(files, 4);
        Assert.Equal(files.Order(StringComparer.Ordinal), parts.SelectMany(p => p).Order(StringComparer.Ordinal));
        Assert.All(parts, p => Assert.InRange(p.Count, 2403, 2405));
        Assert.Equal("omawin-tool-git.3.tar.zst", Tools.PartName("git", 3));
    }

    [Fact]
    public void APartedToolsRowEndsWithItsPartsAndTheirHashes()
    {
        var line = new Built("git", "v2", "omawin-tool-git.exe", "abc", "cmd", "Git.Git", null, Sfx: true,
            Parts: [("omawin-tool-git.1.tar.zst", "s1"), ("omawin-tool-git.2.tar.zst", "s2")]).Line;
        Assert.EndsWith("\tsfx\tomawin-tool-git.1.tar.zst,omawin-tool-git.2.tar.zst\ts1,s2", line);
        Assert.Equal(10, line.Split('\t').Length);
    }

    [Theory]
    [InlineData("a | github:o/r | ^x$ | . | X | | parts:1")]
    [InlineData("a | github:o/r | ^x$ | . | X | | parts:99")]
    public void PartsAreBetweenTwoAndSixteen(string row) => Assert.Throws<FormatException>(() => Tools.Parse([row]));
}
