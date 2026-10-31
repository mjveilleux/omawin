namespace Omawin.Cli.Tests;

// The real bin/commands.txt: every row must route and every action must be one omawin can run.
// Catches a typo'd builtin, a script that was moved or renamed, or two rows with one name.
public class ShippedCommandsTests
{
    static readonly string RepoRoot = FindRepo();
    static readonly CommandTable Table = CommandTable.Parse(File.ReadAllLines(Path.Combine(RepoRoot, "bin", "commands.txt")));

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "bin", "commands.txt"))) return d.FullName;
        throw new InvalidOperationException("bin/commands.txt not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void ThereAreCommands() => Assert.True(Table.Commands.Count > 50, $"only {Table.Commands.Count} rows read");

    [Fact]
    public void NamesAreUniqueAndAtMostThreeWords()
    {
        Assert.Empty(Table.Commands.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(Table.Commands.Where(c => c.Name.Split(' ').Length > 3).Select(c => c.Name));
    }

    [Fact]
    public void EveryCommandRoutesToItself()
    {
        foreach (var c in Table.Commands)
            Assert.Equal(c.Name, Assert.IsType<RunCommand>(Table.Resolve(c.Name.Split(' '))).Command.Name);
    }

    [Fact]
    public void EveryActionIsOneOmawinCanRun()
    {
        var bad = Table.Commands
            .Select(c => (c.Name, Plan: Actions.Plan(c, [], @"C:\r", @"C:\u")))
            .Where(x => x.Plan is BadAction)
            .Select(x => $"{x.Name}: {((BadAction)x.Plan).Message}");
        Assert.Empty(bad);
    }

    [Fact]
    public void EveryRepoScriptExists()
    {
        var missing = Table.Commands
            .Where(c => c.Action.StartsWith("pwsh ") && !c.Action.Contains("{user}"))
            .Select(c => (c.Name, Script: c.Action[5..].Trim().Split(' ')[0]))
            .Where(x => !File.Exists(Path.Combine(RepoRoot, x.Script.Replace('\\', '/'))))
            .Select(x => $"{x.Name}: {x.Script}");
        Assert.Empty(missing);
    }
}
