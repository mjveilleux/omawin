using SystemMenu.Search;

namespace SystemMenu.Tests.Search;

public class CommandListTests
{
    static OmawinCommand C(string name, string summary, string action) => new(name, summary, action);

    [Fact]
    public void TheKeysOwnEntryPointsAndStartersAreLeftOut()
    {
        var all = CommandList.Parse([
            "# a comment", "",
            "launcher | One Search (Win+Space) | app --search",
            "files | One Search on Files (Win+F) | app --search file",
            "menu | Browse | app --search command --browse",
            "search | One Search | app --search",
            "start | Starts it | app --start-hidden",
            "start bar | The bar | builtin start-bar",
            "starship | Not a starter | run starship",
            "update | Update omawin | pwsh bin/update.ps1",
            "broken row without parts",
        ]);
        var shown = CommandList.Shown(all, []).Select(c => c.Name).ToList();
        Assert.Equal(["starship", "update"], shown);
    }

    [Fact]
    public void AMenuRowThatRunsTheSameCommandWins()
    {
        var all = CommandList.Parse(["update | Update omawin | pwsh bin/update.ps1", "check | Health check | pwsh bin/check.ps1"]);
        var shown = CommandList.Shown(all, ["omawin update", "!omawin check --deep", "run ms-settings:"]);
        Assert.Equal(["check"], shown.Select(c => c.Name));
    }

    [Theory]
    [InlineData("Screenshot (Win+Shift+S): drag a region", "Screenshot", "Win Shift S")]
    [InlineData("Apply a theme: omawin theme set gruvbox", "Apply a theme", null)]
    [InlineData("Fill the theme into GlazeWM's config and reload it (Alt+Shift+R)", "Fill the theme into GlazeWM's config and reload it", "Alt Shift R")]
    [InlineData("Keep the PC awake; lasts while the menu app runs", "Keep the PC awake", null)]
    public void ARowSaysItsFirstClauseAndItsKey(string summary, string shortText, string? key)
    {
        Assert.Equal(shortText, CommandList.ShortOf(summary));
        Assert.Equal(key, CommandList.KeyOf(summary));
    }

    [Theory]
    [InlineData("theme set gruvbox", "gruvbox")]
    [InlineData("omawin theme set  tokyo night ", "tokyo night")]
    [InlineData("theme set", "")]
    [InlineData("theme settings", "")]
    [InlineData("theme", "")]
    public void WordsAfterTheWholeNameAreItsArguments(string query, string args)
    {
        Assert.Equal(args, CommandList.ArgumentsIn(query, C("theme set", "Apply a theme: <name>", "pwsh themes/set-theme.ps1")));
    }

    [Theory]
    [InlineData("app --snip", RunIn.Hidden)]
    [InlineData("run omawin-music", RunIn.Hidden)]
    [InlineData("run omawin-windows top", RunIn.Terminal)]
    [InlineData("builtin launch-terminal", RunIn.Hidden)]
    [InlineData("builtin terminal-here", RunIn.Hidden)]
    [InlineData("builtin pkg update", RunIn.Terminal)]
    [InlineData("pwsh bin/check.ps1", RunIn.Terminal)]
    [InlineData("ask --setting list", RunIn.Terminal)]
    public void ScriptsAndAnswersRunInATerminalWindowsOfTheirOwnHidden(string action, RunIn expected)
    {
        Assert.Equal(expected, CommandList.RunsIn(C("x", "x", action)));
    }

    [Fact]
    public void TheRealCommandsFileParses()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "commands.txt");
        var all = CommandList.Parse(File.ReadAllLines(file));
        Assert.Contains(all, c => c.Name == "capture screenshot" && c.Key == "Win Shift S");
        Assert.DoesNotContain(CommandList.Shown(all, []), c => c.Name is "launcher" or "files" or "menu");
        Assert.All(all, c => Assert.NotEqual("", c.Short));
    }
}
