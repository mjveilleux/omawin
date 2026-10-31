using Omawin.Cli.Pkg;
using Omawin.Cli.Remove;

namespace Omawin.Cli.Tests;

// omawin remove <part> (src/cli/Remove): the plan from the real install/packages.ps1, and what
// running it asks the PC to do.
public sealed class RemoveTests
{
    static readonly string Repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static readonly List<Preset> Presets = PresetFile.Parse(File.ReadAllLines(Path.Combine(Repo, "install", "packages.ps1")));

    static RemoveFacts Facts(string[] picked, params PackageRow[] record) => new(Presets, picked, record, "/appdata");

    [Fact]
    public void EveryPresetInPackagesPs1IsRead()
    {
        // A preset written another way (over several lines) would be missing here.
        Assert.Contains(Presets, p => p.Name == "dictation" && p.Winget.SequenceEqual(["cjpais.Handy"]) && p.Tools.SequenceEqual(["handy"]));
        Assert.Contains(Presets, p => p.Name == "music" && p.Plugin == "omawin.music" && p.Winget.Contains("shinchiro.mpv"));
        Assert.Contains(Presets, p => p.Name == "ai-agents" && p.Extras.SequenceEqual(["claude", "opencode"]));
        Assert.True(Presets.Count >= 10);
        Assert.All(Presets, p => Assert.NotEqual("", p.Summary));
    }

    [Fact]
    public void DictationUnpicksStopsRemovesHandyAndItsModel()
    {
        var plan = RemovePlanner.For(["dictation"], Facts(["dictation"], new PackageRow("Handy", "winget", "cjpais.Handy")));
        Assert.Null(plan.Error);
        Assert.Equal(["RunOmawin", "StopProcess", "RemovePackage", "DeleteFolder"], plan.Steps.Select(s => s.GetType().Name));
        Assert.Equal(["preset", "remove", "dictation"], ((RunOmawin)plan.Steps[0]).Words);
        Assert.Equal("winget:cjpais.Handy", ((RemovePackage)plan.Steps[2]).Key);
        Assert.Contains("parakeet", ((DeleteFolder)plan.Steps[3]).Path);
        Assert.Contains(plan.Left, l => l.Contains("Win+H"));
    }

    [Fact]
    public void WhatsYoursWhatsSharedAndWhatAnotherPresetNeedsStays()
    {
        var plan = RemovePlanner.For(["music"], Facts(["music", "docker"],
            new PackageRow("mpv", "winget", "shinchiro.mpv", By: "found"),
            new PackageRow("yt-dlp", "winget", "yt-dlp.yt-dlp"),
            new PackageRow(".NET Desktop Runtime 9", "winget", "Microsoft.DotNet.DesktopRuntime.9")));
        Assert.Equal(["winget:yt-dlp.yt-dlp"], plan.Steps.OfType<RemovePackage>().Select(p => p.Key));
        Assert.Contains(plan.Left, l => l.StartsWith("mpv: you installed it yourself"));
        Assert.Contains(plan.Left, l => l.StartsWith(".NET Desktop Runtime 9: other programs use it"));
        Assert.Contains(plan.Left, l => l.Contains(@"Music\omawin"));

        var shared = new Preset("other", "x", null, ["yt-dlp.yt-dlp"], [], []);
        var both = new RemoveFacts([.. Presets, shared], ["music", "other"], [new PackageRow("yt-dlp", "winget", "yt-dlp.yt-dlp")], "/appdata");
        Assert.Empty(RemovePlanner.For(["plugin", "music"], both).Steps.OfType<RemovePackage>());
    }

    [Fact]
    public void NamesThatMeanSomethingElse()
    {
        Assert.Equal("ai-agents", RemovePlanner.For(["agents"], Facts(["ai-agents"])).Part);
        Assert.Equal(["theme", "remove", "nord"], ((RunOmawin)RemovePlanner.For(["theme", "nord"], Facts([])).Steps.Single()).Words);
        Assert.Equal(["plugin", "remove", "b.omadoro"], ((RunOmawin)RemovePlanner.For(["plugin", "b.omadoro"], Facts([])).Steps.Single()).Words);
        Assert.Contains("never removes your Linux", RemovePlanner.For(["linux"], Facts([])).Error);
        Assert.StartsWith("no part 'frobnicate'", RemovePlanner.For(["frobnicate"], Facts([])).Error);
        Assert.Contains("nothing of go", RemovePlanner.For(["go"], Facts([])).Error);
    }

    sealed class FakeHost : RemoveHost
    {
        public override TextWriter Out { get; } = new StringWriter();
        public string? Answer = "y";
        public List<string> Did = [];
        public override string? Ask(string prompt) => Answer;
        public override int Omawin(string[] words) { Did.Add("omawin " + string.Join(' ', words)); return 0; }
        public override int Pkg(string[] args) { Did.Add("pkg " + string.Join(' ', args)); return 0; }
        public override void Stop(string process) => Did.Add("stop " + process);
        public override bool Delete(string folder) { Did.Add("delete " + folder.Replace('\\', '/').Split('/')[^1]); return true; }
    }

    [Fact]
    public void ItAsksOnceThenDoesItInOrder()
    {
        var facts = Facts(["dictation"], new PackageRow("Handy", "winget", "cjpais.Handy"));
        var host = new FakeHost();
        Assert.Equal(0, new RemoveCommand(host, facts).Run(["dictation"]));
        Assert.Equal(["omawin preset remove dictation", "stop handy", "pkg remove winget:cjpais.Handy", "delete parakeet-tdt-0.6b-v3-int8"], host.Did);

        var no = new FakeHost { Answer = "n" };
        new RemoveCommand(no, facts).Run(["dictation"]);
        Assert.Empty(no.Did);
        var dry = new FakeHost();
        new RemoveCommand(dry, facts).Run(["dictation", "--dry-run"]);
        Assert.Empty(dry.Did);
        Assert.Contains("stop handy", dry.Out.ToString());
        var nobody = new FakeHost { Answer = null };
        Assert.Equal(1, new RemoveCommand(nobody, facts).Run(["dictation"]));
        Assert.Empty(nobody.Did);
    }
}
