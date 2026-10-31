using SystemMenu.Tests.Settings;

namespace SystemMenu.Tests;

sealed class FakeHost : IDevDriveHost
{
    public int Build { get; set; } = 26100;
    public List<(char Letter, bool ReFS)> Drives = [('C', false), ('D', false)];
    public List<string> Created = [], Folders = [];
    public IReadOnlyList<(char Letter, bool ReFS)> Volumes() => Drives;
    public Task<(bool Ran, string? Error)> CreateAsync(string vhdx, int gb, char letter)
    {
        Created.Add($"{vhdx} {gb} {letter}");
        Drives.Add((letter, true));
        return Task.FromResult((true, (string?)null));
    }
    public void MakeFolder(string path) => Folders.Add(path);
}

public class DevDriveTests
{
    [Fact]
    public async Task CreateSaysWhatThenMakesOneOnAFreeLetter()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        var history = new SettingHistory(dir.File("env-history.tsv"));
        var ask = await DevDrive.Answer(host, new FakeEnv(), history, ["create"]);
        Assert.Equal(3, ask.Code);
        Assert.Contains("E: a ReFS drive of up to 100 GB", ask.Text);
        Assert.Empty(host.Created);

        Assert.Equal(0, (await DevDrive.Answer(host, new FakeEnv(), history, ["create", "--yes"])).Code);
        Assert.Equal([@"C:\DevDrive\dev.vhdx 100 E"], host.Created);

        Assert.Contains("at least 50", (await DevDrive.Answer(host, new FakeEnv(), history, ["create", "20"])).Text);
        Assert.Contains("in use", (await DevDrive.Answer(host, new FakeEnv(), history, ["create", "d:"])).Text);
        Assert.Contains("Windows 11", (await DevDrive.Answer(new FakeHost { Build = 19045 }, new FakeEnv(), history, ["create"])).Text);
    }

    [Fact]
    public async Task CachesMoveOnlyWhereYouHaventSetThem()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        host.Drives.Add(('E', true));
        var env = new FakeEnv();
        env.User["GOCACHE"] = @"C:\mine\gocache";                // yours: kept
        var history = new SettingHistory(dir.File("env-history.tsv"));

        var ask = await DevDrive.Answer(host, env, history, ["caches", "E"]);
        Assert.Equal(3, ask.Code);
        Assert.Contains(@"npm_config_cache = E:\packages\npm", ask.Text);
        Assert.Contains("Kept, set by you: GOCACHE", ask.Text);

        Assert.Equal(0, (await DevDrive.Answer(host, env, history, ["caches", "E", "--yes"])).Code);
        Assert.Equal(@"E:\packages\nuget", env.User["NUGET_PACKAGES"]);
        Assert.Equal(@"C:\mine\gocache", env.User["GOCACHE"]);
        Assert.Equal(DevDrive.CacheVars.Length - 1, host.Folders.Count);
        // Each one undone like any variable.
        await EnvVars.Answer(env, history, ["undo"]);
        Assert.Equal(DevDrive.CacheVars.Length - 1, DevDrive.CacheVars.Count(c => env.User.ContainsKey(c.Var)));
    }

    [Fact]
    public void TheFirstFreeLetterFromD() =>
        Assert.Equal('F', DevDrive.FreeLetter(['C', 'd', 'E']));
}
