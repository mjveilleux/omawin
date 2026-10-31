namespace SystemMenu.Tests.Search;

// Linux's files in the finder: the index Linux writes (wsl/bin/omawin-index-files), read and searched.
public class LinuxFilesTests
{
    static readonly string[] Lines =
    [
        "# archlinux\t/home/you",
        "/home/you/src/app/main.c\t1761900000\t120",
        "/home/you/src/app/README.md\t1761800000\t40",
        "/home/you/notes/main-ideas.txt\t1761700000\t9",
        "/etc/hosts\t1761600000\t200",
        "broken line",
        "relative/path\t1\t2",
    ];

    [Fact]
    public void TheIndexReadsItsHeaderAndRows()
    {
        var index = LinuxFiles.Parse(Lines)!;
        Assert.Equal("archlinux", index.Distro);
        Assert.Equal("/home/you", index.Home);
        Assert.Equal(4, index.Rows.Length);   // the broken and relative lines left out
        var main = index.Rows[0];
        Assert.Equal(("src/app/main.c", 1761900000L, 120L), (main.Relative, main.Time, main.Size));
        Assert.Equal("/etc/hosts", index.Rows[3].Relative);   // outside home: its whole path
    }

    [Theory]
    [InlineData]
    [InlineData("no header")]
    [InlineData("# ", "/x\t1\t2")]
    public void NotAnIndexIsNull(params string[] lines) => Assert.Null(LinuxFiles.Parse(lines));

    [Fact]
    public void SearchFindsByPathBestFirst()
    {
        var index = LinuxFiles.Parse(Lines)!;
        var found = LinuxFiles.Search(index, "main", 10);
        Assert.Equal(["main.c", "main-ideas.txt"], found.Select(f => f.Name).Take(2).OrderBy(n => n.Length));
        var c = found.Single(f => f.Name == "main.c");
        Assert.Equal("src/app", c.RelativeDir);
        Assert.Equal("/home/you/src/app/main.c", c.Row.Path);
        Assert.Single(LinuxFiles.Search(index, "appread", 10));   // letters across the path
        Assert.Empty(LinuxFiles.Search(index, "zzz", 10));
        Assert.Empty(LinuxFiles.Search(index, "  ", 10));
        Assert.Single(LinuxFiles.Search(index, "main", 1));
    }

    [Theory]
    [InlineData("archlinux", "Arch")]
    [InlineData("Ubuntu", "Ubuntu")]
    [InlineData("Ubuntu-24.04", "Ubuntu")]
    [InlineData("Debian", "Debian")]
    public void TheChipNamesTheDistro(string distro, string label) => Assert.Equal(label, LinuxFiles.Label(distro));

    [Fact]
    public void ExplorerReachesLinuxThroughWslLocalhost() =>
        Assert.Equal(@"\\wsl.localhost\archlinux\home\you\a b.txt", LinuxFiles.WindowsPath("archlinux", "/home/you/a b.txt"));
}
