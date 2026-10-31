using SystemMenu.Tests.Settings;

namespace SystemMenu.Tests;

// Environment variables in dictionaries: the PC's change only when the prompt is approved.
sealed class FakeEnv : IEnvStore
{
    public readonly Dictionary<string, string> User = new(StringComparer.OrdinalIgnoreCase), Machine = new(StringComparer.OrdinalIgnoreCase);
    public bool Approve = true;
    public int Announced, Prompts;

    Dictionary<string, string> Of(EnvScope s) => s == EnvScope.User ? User : Machine;
    public IReadOnlyList<EnvVar> All(EnvScope scope) => Of(scope).Select(p => new EnvVar(p.Key, p.Value, false)).ToList();
    public EnvVar? Get(EnvScope scope, string name) => Of(scope).TryGetValue(name, out var v) ? new EnvVar(name, v, false) : null;
    public string? SetUser(string name, string? value) { if (value == null) User.Remove(name); else User[name] = value; return null; }
    public Task<(bool Ran, string? Error)> SetMachineAsync(string name, string? value)
    {
        Prompts++;
        if (!Approve) return Task.FromResult((false, (string?)null));
        if (value == null) Machine.Remove(name); else Machine[name] = value;
        return Task.FromResult((true, (string?)null));
    }
    public void Announce() => Announced++;
}

public class EnvVarsTests
{
    [Fact]
    public async Task SetAndUndoPutBackExactlyWhatWasThere()
    {
        using var dir = new TempDir();
        var env = new FakeEnv();
        var history = new SettingHistory(dir.File("env-history.tsv"));
        env.User["EDITOR"] = "nvim";

        Assert.Equal(0, (await EnvVars.Answer(env, history, ["set", "EDITOR", "code", "--wait"])).Code);
        Assert.Equal("code --wait", env.User["EDITOR"]);
        Assert.Equal(0, (await EnvVars.Answer(env, history, ["set", "GOPATH", @"%USERPROFILE%\go"])).Code);
        Assert.Equal(2, env.Announced);

        // Undo walks back: GOPATH wasn't set, so it goes; then EDITOR is nvim again.
        Assert.StartsWith("Undone", (await EnvVars.Answer(env, history, ["undo"])).Text);
        Assert.False(env.User.ContainsKey("GOPATH"));
        await EnvVars.Answer(env, history, ["undo"]);
        Assert.Equal("nvim", env.User["EDITOR"]);
        Assert.Equal(1, (await EnvVars.Answer(env, history, ["undo"])).Code); // nothing left
    }

    [Fact]
    public async Task ThePcsAreShownThenSetInOnePrompt()
    {
        using var dir = new TempDir();
        var env = new FakeEnv();
        var history = new SettingHistory(dir.File("env-history.tsv"));
        var ask = await EnvVars.Answer(env, history, ["set", "JAVA_HOME", @"C:\jdk", "-Machine"]);
        Assert.Equal(3, ask.Code);
        Assert.Contains(@"JAVA_HOME = C:\jdk", ask.Text);
        Assert.Equal(0, env.Prompts);

        env.Approve = false;
        Assert.Contains("Not now", (await EnvVars.Answer(env, history, ["set", "JAVA_HOME", @"C:\jdk", "-Machine", "--yes"])).Text);
        Assert.Empty(history.Changes());

        env.Approve = true;
        Assert.Equal(0, (await EnvVars.Answer(env, history, ["set", "JAVA_HOME", @"C:\jdk", "-Machine", "--yes"])).Code);
        Assert.Equal(@"C:\jdk", env.Machine["JAVA_HOME"]);
        Assert.Equal("env:machine:JAVA_HOME", Assert.Single(history.Changes()).Id);
    }

    [Theory]
    [InlineData("PATH", true)]
    [InlineData("ProgramFiles(x86)", true)]
    [InlineData("_private", true)]
    [InlineData("1abc", false)]
    [InlineData("A=B", false)]
    [InlineData("with space", false)]
    public void Names(string name, bool ok) => Assert.Equal(ok, EnvVars.ValidName(name));

    [Fact]
    public void ListingShowsAPathAsItsEntries()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\a;C:\b;";
        env.Machine["OS"] = "Windows_NT";
        var text = EnvVars.Format(env, null);
        Assert.Contains("2 entries", text);
        Assert.Contains(@"C:\b", text);
        Assert.Contains("for every account (1;", text);
        Assert.DoesNotContain("Windows_NT", EnvVars.Format(env, EnvScope.User));
    }
}
