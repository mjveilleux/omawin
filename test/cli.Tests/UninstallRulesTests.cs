namespace Omawin.Cli.Tests;

// `omawin uninstall`'s decisions (bin/uninstall.cs, built here with OMAWIN_TESTS: only its rules,
// none of the Windows calls).
public sealed class UninstallRulesTests
{
    [Fact]
    public void TheNewestBackupOfThatFileComesBack()
    {
        var siblings = new[]
        {
            @"C:\Users\a\.bashrc.backup-20261014-090000",
            @"C:\Users\a\.bashrc.backup-20261031-101500",
            @"C:\Users\a\.bash_profile.backup-20261101-000000", // another file's
            @"C:\Users\a\.bashrc",
        };
        Assert.Equal(@"C:\Users\a\.bashrc.backup-20261031-101500", UninstallRules.LatestBackup(@"C:\Users\a\.bashrc", siblings));
        Assert.Null(UninstallRules.LatestBackup(@"C:\Users\a\.wslconfig", siblings));
        // A folder's (a junction's) path may end in a slash.
        Assert.Equal(@"C:\x\nvim.backup-20261020-120000", UninstallRules.LatestBackup(@"C:\x\nvim\", new[] { @"C:\x\nvim.backup-20261020-120000", @"C:\x\nvim-data" }));
    }

    [Fact]
    public void OnlyOmawinsFoldersLeaveThePath()
    {
        var raw = @"%USERPROFILE%\.local\bin;C:\Tools;%LOCALAPPDATA%\omawin\tools\git\cmd;;C:\Users\a\AppData\Local\omawin\tools\nvim\bin;C:\Users\a\AppData\Local\omawinish";
        Func<string, string> expand = e => e.Replace("%USERPROFILE%", @"C:\Users\a").Replace("%LOCALAPPDATA%", @"C:\Users\a\AppData\Local");
        Func<string, bool> ours = p => UninstallRules.IsUnder(p, @"C:\Users\a\AppData\Local\omawin");
        var kept = UninstallRules.WithoutEntries(raw, expand, ours, out var removed);
        Assert.Equal(2, removed);
        // The rest stays as written (unexpanded); a folder that only starts with the same letters isn't inside.
        Assert.Equal(@"%USERPROFILE%\.local\bin;C:\Tools;C:\Users\a\AppData\Local\omawinish", kept);
    }

    [Theory]
    [InlineData(@"C:\Users\a\omawin\config\git\gitconfig", true)]
    [InlineData("C:/Users/a/omawin/config/git/gitconfig", true)]   // git's own way of writing it
    [InlineData(@"c:\users\A\OMAWIN", true)]
    [InlineData(@"C:\Users\a\omawin-account\git\gitconfig", false)] // your account folder's: stays
    [InlineData("", false)]
    public void WhatPointsIntoTheCheckout(string value, bool inside)
    {
        Assert.Equal(inside, UninstallRules.PointsInto(value, @"C:\Users\a\omawin"));
    }

    [Fact]
    public void ItsStubsAreToldFromYourFiles()
    {
        Assert.True(UninstallRules.IsOurStub("# Managed by omawin: loads C:\\x\\profile.ps1\nif (Test-Path 'x') { . 'x' }\n"));
        Assert.True(UninstallRules.IsOurStub("\uFEFF# omawin: the config is in the repo\nconfig-file = x\n"));
        Assert.False(UninstallRules.IsOurStub("alias ll='ls -l'\n# Managed by omawin\n"));
        Assert.False(UninstallRules.IsOurStub(null));
        Assert.True(UninstallRules.IsOurRprofile("source('C:/Users/a/omawin/config/r/rprofile.R')\n"));
        Assert.False(UninstallRules.IsOurRprofile("options(digits = 3)\nsource('C:/Users/a/omawin/config/r/rprofile.R')"));
    }

    [Fact]
    public void PackagesTsvReadsEachSource()
    {
        var rows = UninstallRules.ParsePackages(new[]
        {
            "# where each package came from: name, source, id, version, folder",
            "nvim\tbundle\tnvim\tv0.11.4\tC:\\Users\\a\\AppData\\Local\\omawin\\tools\\nvim",
            "",
            "Python.Python.3.13\twinget\tPython.Python.3.13",
            "broken",
        });
        Assert.Equal(2, rows.Count);
        Assert.Equal(("nvim", "bundle", "v0.11.4"), (rows[0].Name, rows[0].Source, rows[0].Version));
        Assert.Equal(("winget", "", ""), (rows[1].Source, rows[1].Version, rows[1].Where));
    }

    [Fact]
    public void TypedNumbersFlipTicks()
    {
        var ticks = new[] { true, true, false, true, false };
        Assert.Equal(new[] { false, true, true, true, false }, UninstallRules.ApplyChoice(ticks, "1 3", out _));
        Assert.Equal(new[] { true, false, true, false, false }, UninstallRules.ApplyChoice(ticks, "2-4", out _));
        Assert.Equal(new[] { true, true, true, true, true }, UninstallRules.ApplyChoice(ticks, " ALL ", out _));
        Assert.Equal(new[] { false, false, false, false, false }, UninstallRules.ApplyChoice(ticks, "none", out _));
        Assert.Equal(ticks, UninstallRules.ApplyChoice(ticks, "1,1", out _));        // flipped twice
        Assert.Null(UninstallRules.ApplyChoice(ticks, "6", out var error));
        Assert.Contains("1 to 5", error);
        Assert.Null(UninstallRules.ApplyChoice(ticks, "4-2", out _));
        Assert.Null(UninstallRules.ApplyChoice(ticks, "keep git", out _));
        Assert.Equal(new[] { true, true, false, true, false }, ticks);               // the input is left as it was
    }
}
