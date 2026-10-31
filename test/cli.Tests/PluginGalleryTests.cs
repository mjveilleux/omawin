using System.Text.Json;
using Omawin.PluginGallery;
using SystemMenu.Plugins;

namespace Omawin.Cli.Tests;

// The plugin gallery's index builder (src/plugin-gallery): which of the marketplace's plugins it
// picks, and the verdict it gives each. No network: fixtures in Omarchy's catalog.json shape, and
// the real host's files (src/qs-host), so a verdict follows the host as it grows.
public class PluginGalleryTests
{
    static string Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "qs-host", "qml"))) return d.FullName;
        throw new InvalidOperationException("src/qs-host not found");
    }

    static readonly HostCapabilities Host = HostCapabilities.Read(Path.Combine(Repo(), "src", "qs-host"), null);

    const string Sha1 = "24f49823c6749204b17dca2481a68e8a12200eda";
    const string Sha2 = "c7a818f0fe25ecfd4aaa1270e8e79f226985d8e8";

    // One catalog entry, as plugins.omarchy.org writes them; `change` edits it.
    static string Plugin(string name, int stars, string repo, Action<Dictionary<string, object?>>? change = null)
    {
        var p = new Dictionary<string, object?>
        {
            ["id"] = "x." + name.ToLowerInvariant(), ["name"] = name, ["author"] = "Someone", ["description"] = name + " in the bar",
            ["category"] = "Productivity", ["license"] = "MIT", ["version"] = "1.0.0", ["repo"] = repo, ["stars"] = stars,
            ["kind"] = "Bar widget", ["status"] = "Available", ["verificationStatus"] = "verified", ["repositoryLayout"] = "root-plugin",
            ["installAvailable"] = true, ["upstreamValidatedCommit"] = Sha1, ["listingValidatedCommit"] = Sha2,
        };
        change?.Invoke(p);
        return JsonSerializer.Serialize(p);
    }

    static List<CatalogPlugin> Pick(int limit, params string[] plugins)
    {
        using var doc = JsonDocument.Parse($$"""{ "generatedAt": "2026-10-31T02:30:23Z", "plugins": [{{string.Join(",", plugins)}}] }""");
        return Catalog.Pick(doc.RootElement, limit);
    }

    [Fact]
    public void PicksVerifiedBarWidgetsMostStarredFirst()
    {
        var picked = Pick(10,
            Plugin("Few", 2, "https://github.com/a/few"),
            Plugin("Many", 90, "https://github.com/a/many.git"),
            Plugin("Panel", 500, "https://github.com/a/panel", p => p["kind"] = "Panel"),
            Plugin("Both", 40, "https://github.com/a/both", p => p["kind"] = "Service + Bar widget"),
            Plugin("Unverified", 500, "https://github.com/a/unverified", p => p["verificationStatus"] = "unverified"),
            Plugin("Manual", 500, "https://github.com/a/manual", p => p["status"] = "Manual setup"),
            Plugin("Suite", 500, "https://github.com/a/suite", p => p["repositoryLayout"] = "suite"),
            Plugin("NoInstall", 500, "https://github.com/a/noinstall", p => p["installAvailable"] = false),
            Plugin("Elsewhere", 500, "https://gitlab.com/a/elsewhere"),
            Plugin("Again", 1, "https://github.com/a/many"));
        Assert.Equal(["Many", "Both", "Few"], picked.Select(p => p.Name));
        // .git dropped: the repository is named once, and its archive is under that name.
        Assert.Equal("https://github.com/a/many", picked[0].Repo);
        Assert.Equal(2, Pick(2, Plugin("A", 1, "https://github.com/a/a"), Plugin("B", 2, "https://github.com/a/b"), Plugin("C", 3, "https://github.com/a/c")).Count);
    }

    [Fact]
    public void TakesTheNewestValidatedCommit()
    {
        Assert.Equal(Sha1, Pick(1, Plugin("A", 1, "https://github.com/a/a")).Single().Commit);
        Assert.Equal(Sha2, Pick(1, Plugin("A", 1, "https://github.com/a/a", p => p["upstreamValidatedCommit"] = null)).Single().Commit);
        // A short or missing commit can't pin what was reviewed: left out.
        Assert.Empty(Pick(1, Plugin("A", 1, "https://github.com/a/a", p => { p["upstreamValidatedCommit"] = "24f4982"; p["listingValidatedCommit"] = null; })));
    }

    [Fact]
    public void AnEntryNeedsABarWidgetAndAUsableId()
    {
        var plugin = Pick(1, Plugin("Timer", 7, "https://github.com/a/timer")).Single();
        var runs = new ScanResult(Verdicts.Runs, [], []);
        var entry = Catalog.Entry(plugin, """{ "id": "a.timer", "name": "T", "version": "0.2", "kinds": ["bar-widget"], "entryPoints": { "barWidget": "BarWidget.qml" } }""", runs);
        Assert.NotNull(entry);
        Assert.Equal("a.timer", entry.Id);         // the manifest's id: what it's installed as
        Assert.Equal("Timer", entry.Name);         // the catalog's words
        Assert.Equal("0.2", entry.Version);
        Assert.Equal($"https://codeload.github.com/a/timer/zip/{Sha1}", entry.Download);
        Assert.Null(Catalog.Entry(plugin, """{ "id": "a.timer", "entryPoints": { "service": "Service.qml" } }""", runs));
        Assert.Null(Catalog.Entry(plugin, """{ "id": "../timer", "entryPoints": { "barWidget": "BarWidget.qml" } }""", runs));
        Assert.Null(Catalog.Entry(plugin, "not json", runs));
    }

    static ScanResult Scan(params (string Path, string Text)[] files) => Scanner.Scan(files.ToDictionary(f => f.Path, f => f.Text), Host);

    [Fact]
    public void TheShippedPluginRuns()
    {
        var dir = Path.Combine(Repo(), "shell", "plugins", "omawin.music");
        var files = Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".qml") || f.EndsWith(".js")).ToDictionary(Path.GetFileName, File.ReadAllText)!;
        var scan = Scanner.Scan(files!, Host);
        Assert.Equal(Verdicts.Runs, scan.Verdict);
    }

    [Fact]
    public void AModuleTheHostLacksIsANeed()
    {
        var scan = Scan(("BarWidget.qml", """
            import QtQuick
            import Quickshell.Hyprland
            import Qt5Compat.GraphicalEffects
            import "lib" as Lib
            import qs.Ui
            BarWidget { Text { text: Hyprland.focusedMonitor.name } }
            """));
        Assert.Equal(Verdicts.NotYet, scan.Verdict);
        Assert.Equal(["Qt5Compat.GraphicalEffects", "Quickshell.Hyprland"], scan.Needs);
    }

    [Fact]
    public void AQuickshellTypeOrMemberTheHostLacksIsANeed()
    {
        var scan = Scan(("Service.qml", """
            pragma Singleton
            import QtQuick
            import Quickshell
            import Quickshell.Io
            Singleton {
              property var all: Quickshell.screens
              function go() { Quickshell.execDetached(["omawin", "x"]); return Quickshell.env("HOME") }
              IpcHandler { target: "x" }
              Process { command: ["omawin-music", "watch"] }
            }
            """));
        Assert.Equal(["Quickshell Singleton", "Quickshell.Io IpcHandler", "Quickshell.screens"], scan.Needs);
        Assert.Empty(scan.Commands);
    }

    [Fact]
    public void OmarchysOwnTypesAreCheckedWhenKnown()
    {
        var host = HostCapabilities.Read(Path.Combine(Repo(), "src", "qs-host"), null);
        host.OmarchyTypes["Toggle"] = "qs.Ui";
        host.OmarchyTypes["Util"] = "qs.Commons";
        host.OmarchyTypes["Button"] = "qs.Ui";
        var files = new Dictionary<string, string>
        {
            ["BarWidget.qml"] = """
                import QtQuick
                import qs.Commons as Commons
                import qs.Ui
                BarWidget {
                  Toggle { }
                  WidgetButton { }
                  Row { }
                  Clock { }
                  property string s: Commons.Util.pad(1) + Text.ElideRight
                }
                """,
            ["Clock.qml"] = "import QtQuick\nItem {}",
            // QtQuick.Controls has a Button too: with it imported, the name may be Qt's.
            ["Panel.qml"] = "import QtQuick.Controls\nimport qs.Ui\nButton { }",
        };
        var scan = Scanner.Scan(files, host);
        Assert.Equal(["qs.Commons Util", "qs.Ui Toggle"], scan.Needs);
    }

    [Fact]
    public void LinuxProgramsMakeItPartly()
    {
        var scan = Scan(("BarWidget.qml", """
            import QtQuick
            import Quickshell
            import Quickshell.Io
            import qs.Ui
            BarWidget {
              // command: ["commented-out"]
              Process { command: ["notify-send", "hi"] }
              Process { command: ["/usr/bin/hyprctl", "-j", "monitors"] }
              Process { command: ["sh", "-c", "wl-copy < /tmp/x"] }
              Process { command: ["bash", "-c", "pactl list"] }
              Process { command: [root.script, "x"] }
              Process { command: ["curl", "-s", "https://example.com"] }
              function f() { Quickshell.execDetached(['omarchy-launch-browser']); bar.run("xdg-open https://x") }
              property string label: "Process { command: [\"not-code\"] }"
            }
            """), ("scripts/x.sh", "#!/bin/bash\necho"));
        Assert.Equal(Verdicts.Partly, scan.Verdict);
        Assert.Equal(["hyprctl", "notify-send", "omarchy-launch-browser", "pactl", "sh scripts", "wl-copy", "xdg-open"], scan.Commands);
    }

    [Fact]
    public void TheIndexRoundTripsAndDropsWhatItCantTrust()
    {
        GalleryEntry E(string id, string repo = "https://github.com/a/b", string commit = Sha1, string verdict = Verdicts.Runs) =>
            new() { Id = id, Name = id, Repo = repo, Commit = commit, Verdict = verdict };
        var index = new GalleryIndex
        {
            GeneratedAt = "2026-10-31T03:00:00Z",
            Plugins = [E("a.timer"), E("..\\up"), E("b.x", repo: "https://example.com/a/b"), E("c.x", commit: "main"), E("d.x", verdict: "maybe"), E("e.x", repo: "https://github.com/a/b.git")],
        };
        var back = GalleryIndex.Parse(index.ToJson(), out var error);
        Assert.Null(error);
        Assert.Equal(["a.timer"], back!.Plugins.Select(p => p.Id));
        Assert.Null(GalleryIndex.Parse("{", out error));
        Assert.NotNull(error);
    }
}
