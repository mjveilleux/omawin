using Omawin.Cli.Security;
using Omawin.Cli.Themes;

namespace Omawin.Cli.Tests;

public sealed class SecurityTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("omawin-sums-").FullName;
    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void SumsReadSha256sumsFormatAndIgnoreTheRest()
    {
        var a = new string('a', 64);
        var sums = Sums.Parse($"{a}  omawin-1.0-x64.zip\n{new string('B', 64)} *menu.exe\r\nnot a line\n{a}  ../evil.zip\n{new string('z', 64)}  bad.zip\n");
        Assert.Equal(2, sums.Count);
        Assert.Equal(a, sums["omawin-1.0-x64.zip"]);
        Assert.Equal(new string('b', 64), sums["MENU.EXE"]); // names compare like Windows; hashes lower-cased
    }

    [Fact]
    public void AFileMatchesOnlyItsListedHash()
    {
        var file = Path.Combine(dir, "bundle.zip");
        File.WriteAllText(file, "hello\n");
        const string hello = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03"; // sha256sum of "hello\n"
        Assert.True(Sums.Matches(file, Sums.Parse($"{hello}  bundle.zip")));
        Assert.False(Sums.Matches(file, Sums.Parse($"{hello.Replace('5', '6')}  bundle.zip")));
        Assert.False(Sums.Matches(file, Sums.Parse($"{hello}  other.zip"))); // not listed: not trusted
    }

    [Theory]
    [InlineData("colors.toml", true)]
    [InlineData("backgrounds/1-city.webp", true)]
    [InlineData(@"backgrounds\2-moving.mp4", true)]
    [InlineData("neovim.lua", true)]
    [InlineData("btop.theme", true)]
    [InlineData("install.sh", false)]
    [InlineData("setup.ps1", false)]
    [InlineData("tool.exe", false)]
    [InlineData("shortcut.lnk", false)]
    [InlineData("plugins/evil.lua", false)]       // Lua only as the top-level Neovim spec
    [InlineData(".git/hooks/post-checkout", false)]
    [InlineData("../outside.toml", false)]
    [InlineData("/etc/x.toml", false)]
    [InlineData(@"C:\x.toml", false)]
    public void ThemesBringOnlyData(string file, bool allowed) => Assert.Equal(allowed, ThemeFiles.Allowed(file));

    [Fact]
    public void OmarchysNeovimSpecIsPlainDataAndNamesItsPlugins()
    {
        // Omarchy's themes/nord/neovim.lua, verbatim.
        const string nord = "return {\n\t{ \"EdenEast/nightfox.nvim\" },\n\t{\n\t\t\"LazyVim/LazyVim\",\n\t\topts = {\n\t\t\tcolorscheme = \"nordfox\",\n\t\t},\n\t},\n}\n";
        Assert.Equal(["EdenEast/nightfox.nvim", "LazyVim/LazyVim"], ThemeFiles.Spec(nord));
        Assert.Equal(["a/b"], ThemeFiles.Spec("-- a comment\nreturn { { 'a/b', priority = 1000, lazy = false } }"));
    }

    [Theory]
    [InlineData("return { { \"a/b\", config = function() os.execute(\"calc\") end } }")]
    [InlineData("os.execute('calc') return { }")]
    [InlineData("return { { \"a/b\" } } .. x")]
    [InlineData("return { require(\"x\") }")]
    [InlineData("return { colorscheme = vim }")]             // a bare identifier is a value: code
    [InlineData("local x = 1 return { }")]
    public void ASpecThatIsCodeIsRefused(string lua) => Assert.Null(ThemeFiles.Spec(lua));

    [Fact]
    public void EveryOmarchyThemeSpecInThisRepoPasses()
    {
        // The Neovim specs omawin ships (themes/nvim/<theme>.lua, Omarchy's), when there are any.
        var root = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(root); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "themes", "nvim")))
            {
                foreach (var f in Directory.GetFiles(Path.Combine(d.FullName, "themes", "nvim"), "*.lua"))
                    Assert.True(ThemeFiles.Spec(File.ReadAllText(f)) != null, Path.GetFileName(f));
                return;
            }
    }
}
