using SystemMenu.Plugins;

namespace SystemMenu.Tests.Plugins;

// The plugin gallery's page (Plugins/Gallery.cs): which rows it shows, in what order, and what
// Enter does with each; and where a plugin's folder is found.
public sealed class GalleryTests : IDisposable
{
    const string Sha = "24f49823c6749204b17dca2481a68e8a12200eda";
    readonly string root = Path.Combine(Path.GetTempPath(), "omawin-gallery-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    static Manifest M(string id, string name) => new() { Id = id, Name = name, Version = "1.0", Kinds = ["bar-widget"] };

    static GalleryEntry E(string id, string name, int stars, string verdict, params string[] needs) =>
        new() { Id = id, Name = name, Author = "Someone", Repo = "https://github.com/a/" + id, Commit = Sha, Stars = stars, Verdict = verdict, Needs = [.. needs] };

    [Fact]
    public void RowsPutWhatsOnFirstThenOmawinsOwnThenWhatRuns()
    {
        var index = new GalleryIndex
        {
            Plugins =
            [
                E("a.later", "Later", 900, Verdicts.NotYet, "Quickshell.Hyprland"),
                E("a.partly", "Partly", 50, Verdicts.Partly),
                E("a.runs", "Runs", 10, Verdicts.Runs),
                E("a.popular", "Popular", 80, Verdicts.Runs),
                E("a.got", "Got it", 5, Verdicts.Partly),
            ],
        };
        var rows = Gallery.Rows([M("omawin.music", "Music")], [M("a.got", "Got")], index, ["a.got"]);
        Assert.Equal(["a.got", "omawin.music", "a.popular", "a.runs", "a.partly", "a.later"], rows.Select(r => r.Id));
        var got = rows[0];
        Assert.True(got.On);
        Assert.True(got.Downloaded);
        Assert.Equal("Got it", got.Name);    // the gallery's words over the manifest's
        Assert.Equal(Verdicts.Partly, got.Verdict);
        var music = rows[1];
        Assert.True(music.Shipped);
        Assert.Equal(Verdicts.Runs, music.Verdict);
        Assert.Equal("not yet: needs Quickshell.Hyprland", rows[^1].Why);
    }

    [Fact]
    public void EnterTurnsOnWhatRunsAndOffWhatsOn()
    {
        var index = new GalleryIndex { Plugins = [E("a.runs", "Runs", 1, Verdicts.Runs), E("a.later", "Later", 1, Verdicts.NotYet)] };
        var rows = Gallery.Rows([], [], index, ["a.later"]).ToDictionary(r => r.Id);
        Assert.Equal("add", Gallery.Verb(rows["a.runs"]));
        // On already (plugins.txt says so) though it can't run: Enter still turns it off.
        Assert.Equal("remove", Gallery.Verb(rows["a.later"]));
        Assert.Null(Gallery.Verb(Gallery.Rows([], [], index, []).Single(r => r.Id == "a.later")));
    }

    [Fact]
    public void WithoutAnIndexTheLocalOnesStillShow()
    {
        var rows = Gallery.Rows([M("omawin.music", "Music")], [], null, []);
        Assert.Equal("Music", Assert.Single(rows).Name);
    }

    [Fact]
    public void SearchFindsByNameFirst()
    {
        var index = new GalleryIndex
        {
            Plugins = [E("a.pomodoro", "Pomodoro", 1, Verdicts.Runs), E("a.timer", "Timer", 9, Verdicts.Runs) ],
        };
        index.Plugins[1].Description = "A pomodoro-style timer";
        var rows = Gallery.Rows([], [], index, []);
        Assert.Equal(["a.pomodoro", "a.timer"], Gallery.Search(rows, "pomodoro").Select(r => r.Id));
        Assert.Empty(Gallery.Search(rows, "zzzzqqq"));
        Assert.Equal(2, Gallery.Search(rows, " ").Count);
    }

    [Fact]
    public void FindsAPluginsFolderShippedFirst()
    {
        string shipped = Path.Combine(root, "shipped"), downloaded = Path.Combine(root, "downloaded");
        foreach (var dir in new[] { Path.Combine(shipped, "omawin.music"), Path.Combine(downloaded, "omawin.music"), Path.Combine(downloaded, "a.timer") })
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""{ "id": "{{Path.GetFileName(dir)}}", "name": "x", "version": "1" }""");
        }
        Directory.CreateDirectory(Path.Combine(downloaded, "a.empty"));
        Assert.Equal(Path.Combine(shipped, "omawin.music"), PluginFolders.Find("omawin.music", shipped, downloaded));
        Assert.Equal(Path.Combine(downloaded, "a.timer"), PluginFolders.Find("a.timer", shipped, downloaded));
        Assert.Null(PluginFolders.Find("a.empty", shipped, downloaded));
        Assert.Null(PluginFolders.Find("..", shipped, downloaded));
        Assert.Null(PluginFolders.Find(@"..\shipped\omawin.music", shipped, downloaded));
        Assert.Equal(["a.timer", "omawin.music"], PluginFolders.Manifests(downloaded).Select(m => m.Id));
    }

    [Fact]
    public void ReadsWhatsOn()
    {
        Assert.Equal(["omawin.music", "a.timer"], PluginFolders.ReadOn(["# on", "", " omawin.music ", "a.timer", "omawin.music"]));
    }
}
