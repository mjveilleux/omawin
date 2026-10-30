namespace Omawin.Cli.Tests;

public class RouterTests
{
    static readonly CommandTable Table = CommandTable.Parse(
    [
        "# a comment",
        "launcher | The launcher | app --launcher",
        "",
        "theme set | Apply a theme | pwsh themes/set-theme.ps1",
        "theme list | Every theme | pwsh themes/theme.ps1 list",
        "not a row",
        "",
        "launch terminal | A terminal | builtin launch-terminal",
        "restart menu now | Three words | run menu-app\\build.ps1",
    ]);

    [Fact]
    public void ReadsRowsAndSkipsTheRest()
    {
        Assert.Equal(["launcher", "theme set", "theme list", "launch terminal", "restart menu now"], Table.Commands.Select(c => c.Name));
        Assert.Equal("pwsh themes/theme.ps1 list", Table.Find("theme list")!.Action);
    }

    [Fact]
    public void TheLongestCommandWinsAndTheRestArePassedOn()
    {
        var r = Assert.IsType<RunCommand>(Table.Resolve(["theme", "set", "gruvbox", "-Background", "x"]));
        Assert.Equal("theme set", r.Command.Name);
        Assert.Equal(["gruvbox", "-Background", "x"], r.Extra);
        Assert.Equal("restart menu now", Assert.IsType<RunCommand>(Table.Resolve(["restart", "menu", "now"])).Command.Name);
        Assert.Empty(Assert.IsType<RunCommand>(Table.Resolve(["launcher"])).Extra);
    }

    [Fact]
    public void AGroupAloneIsItsHelpAndAWrongNameInItIsAnError()
    {
        Assert.Equal("theme", Assert.IsType<ShowHelp>(Table.Resolve(["theme"])).Group);
        Assert.Equal(new NotInGroup("theme", "theme sett nord"), Table.Resolve(["theme", "sett", "nord"]));
        Assert.Equal(new Unknown("nope"), Table.Resolve(["nope"]));
        // "launch" is a group; "launcher" is a command: a prefix of a name isn't a group.
        Assert.IsType<RunCommand>(Table.Resolve(["launcher", "projects"]));
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "help" }, null)]
    [InlineData(new[] { "--help", "theme" }, "theme")]
    [InlineData(new[] { "-h" }, null)]
    public void HelpForms(string[] args, string? group) =>
        Assert.Equal(group, Assert.IsType<ShowHelp>(Table.Resolve(args)).Group);

    [Fact]
    public void VersionOnlyAlone()
    {
        Assert.IsType<ShowVersion>(Table.Resolve(["--version"]));
        Assert.IsType<ShowVersion>(Table.Resolve(["-v"]));
        Assert.IsType<Unknown>(Table.Resolve(["--version", "x"]));
    }

    [Fact]
    public void HelpPadsOneColumnAndSeparatesSections()
    {
        var all = Table.Help(null)!;
        Assert.StartsWith("omawin <command> [arguments]", all);
        Assert.Contains("  launcher          The launcher\n\n  theme set", all); // width: longest name + 2
        Assert.Equal("  theme set   Apply a theme\n  theme list  Every theme\n", Table.Help("theme"));
        Assert.Null(Table.Help("nope"));
    }
}
