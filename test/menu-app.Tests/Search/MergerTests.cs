using SystemMenu.Search;

namespace SystemMenu.Tests.Search;

public class MergerTests
{
    // A small, realistic index: the kinds of thing One Search holds.
    static readonly Result[] Index =
    [
        new("app:code", Tag.App, "Visual Studio Code", "App", "vscode code editor"),
        new("app:firefox", Tag.App, "Firefox", "App", "browser web"),
        new("app:slack", Tag.App, "Slack", "App", "chat"),
        new("app:terminal", Tag.App, "Terminal", "noctty", "shell console"),
        new("app:taskmgr", Tag.App, "Task Manager", "Windows", "processes taskmgr"),
        new("setting:dark", Tag.Setting, "Dark mode", "Personalization", "theme light colors") { Row = RowKind.Switch },
        new("setting:ext", Tag.Setting, "File name extensions", "Explorer", "ext explorer show") { Row = RowKind.Switch },
        new("setting:refresh", Tag.Setting, "Refresh rate", "Display", "hz monitor") { Row = RowKind.Choice },
        new("command:snip", Tag.Command, "Screenshot a region", "omawin capture", "snip capture"),
        new("command:restart", Tag.Command, "Restart", "System", "reboot"),
        new("file:roadmap", Tag.File, "roadmap.md", "~/src/omawin/docs"),
        new("file:darknotes", Tag.File, "dark-mode-notes.md", "Documents"),
        new("file:darkpng", Tag.File, "dark.png", "Pictures"),
        new("file:darkcss", Tag.File, "dark.css", "~/src/app"),
        new("file:darkjson", Tag.File, "dark.json", "~/src/app"),
        new("theme:tokyo", Tag.Theme, "Tokyo Night", "Dark", "dark blue"),
        new("theme:gruvbox", Tag.Theme, "Gruvbox", "Dark", "dark retro"),
        new("clip:npm", Tag.Clipboard, "npm run dev", "Copied 4 min ago"),
    ];

    static List<Result> Search(string raw, Tag? token = null, Func<string, string, double>? boost = null) =>
        Merger.Merge(Index, QueryParser.Parse(raw, token), boost);

    static List<Result> Real(List<Result> rows) =>
        rows.Where(r => r.Tag is not (Tag.More or Tag.Package or Tag.Web)).ToList();

    [Fact]
    public void DarkPutsTheSettingFirst()
    {
        var rows = Search("dark");
        Assert.Equal("setting:dark", rows[0].Id);
    }

    [Fact]
    public void VsFindsVisualStudioCode()
    {
        Assert.Equal("app:code", Search("vs")[0].Id);
    }

    [Fact]
    public void TheSlashPrefixSearchesOnlyFiles()
    {
        var rows = Search("/road");
        Assert.Single(rows);
        Assert.Equal("file:roadmap", rows[0].Id);
    }

    [Fact]
    public void NothingTypedAndNoFilterShowsTheHomeNotAList()
    {
        Assert.Empty(Search(""));
    }

    [Fact]
    public void NothingTypedWithAFilterListsEverythingInItMostUsedFirst()
    {
        var rows = Search("", Tag.Setting, boost: (id, _) => id == "setting:refresh" ? 5 : 0);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(Tag.Setting, r.Tag));
        Assert.Equal("setting:refresh", rows[0].Id);
    }

    [Fact]
    public void AllCapsEachTagAndOffersTheRestInOneRow()
    {
        // "dark" matches four files by name; All shows three and a "Show all" row.
        var rows = Search("dark");
        Assert.Equal(Merger.CapOf(Tag.File), rows.Count(r => r.Tag == Tag.File));
        var more = Assert.Single(rows, r => r.Tag == Tag.More);
        Assert.Equal("more:File", more.Id);
        Assert.Contains("Show all 4 Files", more.Name);
    }

    [Fact]
    public void AFilterShowsEveryMatchWithoutCapsOrExtraRows()
    {
        var rows = Search("dark", Tag.File);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(Tag.File, r.Tag));
    }

    [Fact]
    public void AllEndsWithThePackageAndWebRowsInThatOrder()
    {
        var rows = Search("dark");
        Assert.Equal(Tag.Package, rows[^2].Tag);
        Assert.Equal(Tag.Web, rows[^1].Tag);
        Assert.Contains("“dark”", rows[^1].Name);
    }

    [Fact]
    public void ShortQueriesLeaveFilesOutOfAll()
    {
        Assert.DoesNotContain(Search("da"), r => r.Tag == Tag.File);
        Assert.Contains(Search("da", Tag.File), r => r.Tag == Tag.File); // the filter still finds them
    }

    [Fact]
    public void KeywordsFindWhatTheNameDoesNot()
    {
        Assert.Contains(Real(Search("reboot")), r => r.Id == "command:restart");
    }

    [Fact]
    public void HistoryLiftsWhatYouPickedForThisQuery()
    {
        // With no history the setting wins "dark"; after picking the theme for "dark" often, it leads.
        var rows = Search("dark", boost: (id, q) => id == "theme:tokyo" && q == "dark" ? 6 : 0);
        Assert.Equal("theme:tokyo", rows[0].Id);
    }

    [Fact]
    public void MatchedLettersAreMarkedForHighlighting()
    {
        var first = Search("dark")[0];
        Assert.Equal([0, 1, 2, 3], first.Positions);
    }

    [Fact]
    public void PinnedResultsComeFirstUnranked()
    {
        var calc = new Result("calc", Tag.Calculator, "= 42", "6*7 · Enter copies") { Pinned = true };
        var rows = Merger.Merge([.. Index, calc], QueryParser.Parse("6*7", null));
        Assert.Equal("calc", rows[0].Id);
    }

    [Fact]
    public void NoMatchLeavesOnlyThePackageAndWebRows()
    {
        var rows = Search("zzqx");
        Assert.Equal([Tag.Package, Tag.Web], rows.Select(r => r.Tag));
    }

    [Fact]
    public void LateResultsNeverMoveTheSelectedRow()
    {
        var onScreen = Search("dark");
        int selected = 2;
        var selectedId = onScreen[selected].Id;
        // A slow source answers: a better-scoring file arrives and the order would change.
        var newcomer = new Result("file:dark", Tag.File, "dark", "~/new");
        var fresh = Merger.Merge([.. Index, newcomer], QueryParser.Parse("dark", null));
        var shown = Merger.Stable(onScreen, fresh, selected);
        Assert.Equal(selectedId, shown[selected].Id);
        Assert.Equal(onScreen.Take(selected + 1).Select(r => r.Id), shown.Take(selected + 1).Select(r => r.Id));
        Assert.Equal(fresh.Select(r => r.Id).OrderBy(x => x), shown.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public void StableDropsRowsThatNoLongerMatch()
    {
        var onScreen = Search("dark");
        var fresh = Search("darkm"); // narrower: some rows above the selection are gone
        var shown = Merger.Stable(onScreen, fresh, selected: 1);
        Assert.Equal(fresh.Select(r => r.Id).OrderBy(x => x), shown.Select(r => r.Id).OrderBy(x => x));
    }
}
