namespace Omawin.Cli.Tests;

public class ActionTests
{
    const string Repo = @"C:\Users\you\omawin";
    const string User = @"C:\Users\you\omawin-account";

    static Plan Plan(string action, params string[] extra) =>
        Actions.Plan(new Command("x y", "", action, 0), extra, Repo, User);

    [Fact]
    public void AppRequestsGetTheWordsAsTyped() =>
        Assert.Equal(new SendToApp("--launcher projects"), Plan("app --launcher", "projects"));

    [Fact]
    public void PwshScriptsRunFromTheRepoWithQuotedExtras()
    {
        var p = Assert.IsType<StartProcess>(Plan("pwsh themes/set-theme.ps1 -NextBackground", "a b"));
        Assert.Equal("pwsh", p.File);
        Assert.Equal(@"-NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\Users\you\omawin\themes\set-theme.ps1 -NextBackground ""a b""", p.Arguments);
    }

    [Fact]
    public void UserScriptsComeFromTheAccountFolderEvenWithSpaces()
    {
        var p = Assert.IsType<StartProcess>(Actions.Plan(new Command("x", "", "pwsh {user}/reports/run.ps1", 0), [], Repo, @"C:\Users\A Person\omawin-account"));
        Assert.Contains(@"-File ""C:\Users\A Person\omawin-account\reports\run.ps1""", p.Arguments);
    }

    [Fact]
    public void RunStartsTheProgramWithRepoFilledIn()
    {
        var p = Assert.IsType<StartProcess>(Plan("run {repo}\\bin\\tool.exe --fast", "x"));
        Assert.Equal((@"C:\Users\you\omawin\bin\tool.exe", "--fast x"), (p.File, p.Arguments));
    }

    [Fact]
    public void BuiltinsKeepQuotedWordsWhole()
    {
        var b = Assert.IsType<RunBuiltin>(Plan("builtin launch-terminal --title rename -- pwsh -File \"{repo}\\launcher\\rename-workspace.ps1\"", "more"));
        Assert.Equal("launch-terminal", b.Name);
        Assert.Equal(["--title", "rename", "--", "pwsh", "-File", @"C:\Users\you\omawin\launcher\rename-workspace.ps1", "more"], b.Args);
    }

    [Theory]
    [InlineData("frobnicate now", "unknown action")]
    [InlineData("builtin self-destruct", "no builtin")]
    [InlineData("pwsh", "needs a script")]
    [InlineData("run", "needs a program")]
    public void BadRowsSayWhy(string action, string because) =>
        Assert.Contains(because, Assert.IsType<BadAction>(Plan(action)).Message);

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData(@"C:\a b\", @"""C:\a b\\""")]       // a trailing backslash is doubled before the closing quote
    [InlineData(@"say ""hi""", @"""say \""hi\""""")]
    [InlineData(@"a\""b", @"""a\\\""b""")]          // backslashes before a quote: doubled, plus one
    [InlineData(@"C:\no\spaces", @"C:\no\spaces")]  // no quotes needed: left alone
    public void QuoteMatchesWindowsArgumentRules(string arg, string quoted) =>
        Assert.Equal(quoted, Actions.Quote(arg));

    [Theory]
    [InlineData(@"C:\r", "a\\b.ps1", @"C:\r\a\b.ps1")]
    [InlineData(@"C:\r\", "a.ps1", @"C:\r\a.ps1")]
    [InlineData(@"C:\r", @"D:\x.ps1", @"D:\x.ps1")]
    [InlineData(@"C:\r", @"\\server\share\x.ps1", @"\\server\share\x.ps1")]
    public void WindowsJoinLetsRootedPathsStandAlone(string root, string relative, string joined) =>
        Assert.Equal(joined, Actions.WindowsJoin(root, relative));
}
