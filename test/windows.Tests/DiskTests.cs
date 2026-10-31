namespace Omawin.Windows.Tests;

// omawin disk (8.3): what counts as a cache or build output, and what may never be emptied.
public class DiskTests
{
    const string Home = @"C:\Users\me", Local = @"C:\Users\me\AppData\Local", Temp = @"C:\Users\me\AppData\Local\Temp\";

    static IReadOnlyList<DevCache> Caches(Dictionary<string, string>? env = null) =>
        DevCaches.All(n => env != null && env.TryGetValue(n, out var v) ? v : null, Local, Home, Temp);

    [Fact]
    public void CachesFollowTheirEnvironmentVariables()
    {
        var plain = Caches();
        Assert.Equal(@"C:\Users\me\AppData\Local\npm-cache", DevCaches.Find(plain, "npm")!.Folder);
        Assert.Equal(@"C:\Users\me\.cargo\registry", DevCaches.Find(plain, "cargo registry")!.Folder);
        var moved = Caches(new() { ["npm_config_cache"] = @"D:\caches\npm", ["CARGO_HOME"] = @"D:\cargo" });
        Assert.Equal(@"D:\caches\npm", DevCaches.Find(moved, "npm")!.Folder);
        Assert.Equal(@"D:\cargo\git", DevCaches.Find(moved, "cargo git")!.Folder);
        Assert.True(DevCaches.Find(plain, "Temp")!.OldFilesOnly);
    }

    [Fact]
    public void FindTakesAUniqueStart()
    {
        var c = Caches();
        Assert.Equal("Bun", DevCaches.Find(c, "bun")!.Name);
        Assert.Equal("Maven", DevCaches.Find(c, "mav")!.Name);
        Assert.Null(DevCaches.Find(c, "cargo"));    // registry or git: say which
        Assert.Null(DevCaches.Find(c, "nothing"));
    }

    [Fact]
    public void EveryShippedCacheMayBeEmptied()
    {
        foreach (var c in Caches()) Assert.True(DevCaches.SafeToEmpty(c.Folder, Home, Local), c.Folder);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"C:\Users\me\")]
    [InlineData(@"C:\Users\me\AppData\Local")]
    [InlineData(@"C:\Users\me\AppData")]
    [InlineData(@"C:\Users\me\.cargo")]
    [InlineData(@"C:\Users\me\Documents")]
    [InlineData(@"D:\work\project")]
    [InlineData(@"\\server\share\cache")]
    public void NeverEmptied(string folder) => Assert.False(DevCaches.SafeToEmpty(folder, Home, Local));

    [Theory]
    [InlineData("node_modules", new[] { "package.json", "src" }, new string[0], "node_modules")]
    [InlineData("target", new[] { "Cargo.toml", "src" }, new string[0], "target")]
    [InlineData("bin", new[] { "App.csproj", "Program.cs" }, new string[0], "bin")]
    [InlineData("obj", new[] { "App.fsproj" }, new string[0], "obj")]
    [InlineData(".venv", new[] { "main.py" }, new[] { "pyvenv.cfg" }, ".venv")]
    [InlineData(".next", new[] { "next.config.mjs", "package.json" }, new string[0], ".next")]
    [InlineData("build", new[] { "build.gradle.kts" }, new string[0], "build")]
    public void BuildOutputBesideItsMarker(string folder, string[] beside, string[] inside, string kind) =>
        Assert.Equal(kind, DevCaches.Match(folder, beside, inside)?.Folder);

    [Theory]
    [InlineData("target", new[] { "README.md" }, new string[0])]          // a folder called target, no Cargo.toml
    [InlineData("bin", new[] { "install.sh" }, new string[0])]            // a scripts folder
    [InlineData(".venv", new[] { "main.py" }, new[] { "notes.txt" })]     // not a virtual environment
    [InlineData("build", new[] { "Makefile" }, new string[0])]
    [InlineData("src", new[] { "package.json" }, new string[0])]
    public void NotBuildOutput(string folder, string[] beside, string[] inside) =>
        Assert.Null(DevCaches.Match(folder, beside, inside));

    [Fact]
    public void WalksSkipHiddenAndSystemFolders()
    {
        Assert.True(DevCaches.Skip(".git"));
        Assert.True(DevCaches.Skip("AppData"));
        Assert.True(DevCaches.Skip("$Recycle.Bin"));
        Assert.False(DevCaches.Skip("src"));
    }

    [Fact]
    public void Wording()
    {
        Assert.Equal("512 B", DevCaches.Size(512));
        Assert.Equal("3 MB", DevCaches.Size(3 * 1024 * 1024));
        Assert.Equal("1.5 GB", DevCaches.Size(1536L * 1024 * 1024));
        var now = new DateTime(2026, 10, 31);
        Assert.Equal("today", DevCaches.Ago(now.AddHours(-3), now));
        Assert.Equal("5 weeks ago", DevCaches.Ago(now.AddDays(-36), now));
        Assert.Equal("4 months ago", DevCaches.Ago(now.AddDays(-125), now));
        Assert.Equal("2 years ago", DevCaches.Ago(now.AddDays(-800), now));
    }

    [Fact]
    public void WslDiskFromTheRegistry()
    {
        Assert.Equal(@"C:\Users\me\AppData\Local\wsl\{id}\ext4.vhdx", DevCaches.WslDisk(@"\\?\C:\Users\me\AppData\Local\wsl\{id}", null));
        Assert.Equal(@"D:\WSL\Arch\disk.vhdx", DevCaches.WslDisk(@"D:\WSL\Arch\", "disk.vhdx"));
    }
}
