namespace SystemMenu.Tests;

public class WindowsAppsTests
{
    static AppPackage App(string name, string display, long bytes = 50_000_000, bool framework = false, bool system = false) =>
        new(name, name + "_8wekyb3d8bbwe", name + "_1.0.0.0_x64__8wekyb3d8bbwe", display, framework, false, system, bytes);

    static readonly AppPackage[] Installed =
    [
        App("Clipchamp.Clipchamp", "Microsoft Clipchamp", 300_000_000),
        App("Microsoft.BingNews", "Microsoft News"),
        App("Microsoft.WindowsStore", "Microsoft Store"),                  // protected by name
        App("Microsoft.VCLibs.140.00", "VCLibs", framework: true),          // a runtime
        App("Microsoft.Windows.ShellExperienceHost", "Shell", system: true),// part of Windows
        App("Microsoft.Todos", "Microsoft To Do"),
        App("Microsoft.ZuneMusic", "Media Player"),
        App("Microsoft.ZuneVideo", "Films & TV"),
    ];

    [Fact]
    public void WindowsOwnPartsAreNeverRemovable()
    {
        Assert.Equal(["Clipchamp.Clipchamp", "Microsoft.BingNews", "Microsoft.Todos", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo"],
            Installed.Where(WindowsApps.Removable).Select(a => a.Name));
    }

    [Fact]
    public void TheListIsLayeredAndRefusesWindowsParts()
    {
        var (list, problems) = WindowsApps.Parse(
        [
            ("windows-apps.txt", ["Clipchamp.Clipchamp | editor", "Microsoft.BingNews | news", "Microsoft.Zune* | old media apps", "Microsoft.WindowsStore | no", "bad name | x", "Microsoft.Todos |"]),
            ("mine/windows-apps.txt", ["-Microsoft.BingNews"]),
        ]);
        Assert.Equal(["Clipchamp.Clipchamp", "Microsoft.Zune*"], list.Select(s => s.Pattern));
        Assert.Equal(3, problems.Count); // the Store, a name with a space, no why
        Assert.Contains("never removed", problems[0].Message);

        var suggested = WindowsApps.Suggested(Installed, list);
        Assert.Equal(["Clipchamp.Clipchamp", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo"], suggested.Select(s => s.App.Name));
    }

    [Fact]
    public void ATypedNameFindsOneApp()
    {
        Assert.Equal("Microsoft.Todos", WindowsApps.Match(Installed, "microsoft to do").App?.Name);
        Assert.Equal("Clipchamp.Clipchamp", WindowsApps.Match(Installed, "clipchamp").App?.Name);
        var (none, ambiguous) = WindowsApps.Match(Installed, "Zune");
        Assert.Null(none);
        Assert.Equal(2, ambiguous.Count);
        Assert.Null(WindowsApps.Match(Installed, "Microsoft Store").App); // there, but protected
    }

    [Fact]
    public void TheListingMarksSuggestionsBiggestFirst()
    {
        var text = WindowsApps.Format(Installed, [new SuggestedRemoval("Clipchamp.Clipchamp", "editor", "t:1")]);
        var lines = text.Split('\n');
        Assert.StartsWith("* Microsoft Clipchamp", lines[1]);
        Assert.Contains("286 MB", lines[1]);
        Assert.DoesNotContain("Microsoft Store", text);
        Assert.Contains("the 1 marked *", text);
    }

    // The shipped list parses clean: a typo, or a Windows part on it, would otherwise only show
    // in someone's log.
    [Fact]
    public void ShippedListIsClean()
    {
        var file = Path.Combine(Root(), "default", "launcher", "windows-apps.txt");
        var (list, problems) = WindowsApps.Parse([(file, File.ReadAllLines(file))]);
        Assert.Empty(problems);
        Assert.Contains(list, s => s.Pattern == "Clipchamp.Clipchamp");
    }

    // The repo's root, from this file's place in it (test/menu-app.Tests/).
    static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
