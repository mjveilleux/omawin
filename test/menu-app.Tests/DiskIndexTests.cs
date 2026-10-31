namespace SystemMenu.Tests;

// Whole-disk search's index (8.2): the walk, finding by path, changes, searching, the file.
// A real folder tree under the temp folder stands in for a drive.
public sealed class DiskIndexTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "omawin-disk-index-" + Guid.NewGuid().ToString("N"));
    static readonly DiskIndex.Skip NoSkip = new([]);

    public DiskIndexTests()
    {
        Make("Users/me/Documents/Report 2026.docx");
        Make("Users/me/Documents/notes.md");
        Make("Users/me/repos/app/src/main.rs");
        Make("Users/me/repos/app/node_modules/left-pad/index.js");
        Make("Users/me/Pictures/report-cover.png");
        Make("Program Files/Tool/report.exe");
        Directory.CreateDirectory(Path.Combine(root, "Users/me/Reports"));
    }

    void Make(string relative)
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    string P(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void WalkFindsEverythingAndPathsComeBack()
    {
        var index = DiskIndex.Walk([root], NoSkip);
        int slot = index.Find(P("Users/me/repos/app/src/main.rs"));
        Assert.True(slot >= 0);
        Assert.Equal(P("Users/me/repos/app/src/main.rs"), index.PathOf(slot));
        Assert.Equal(-1, index.Find(P("Users/me/nope.txt")));
        // Any case, as Windows does.
        Assert.Equal(slot, index.Find(P("Users/ME/repos/app/SRC/main.rs")));
    }

    [Fact]
    public void SkippedNamesAndPathsAreLeftOut()
    {
        var index = DiskIndex.Walk([root], new DiskIndex.Skip(["node_modules"], [P("Program Files")]));
        Assert.Equal(-1, index.Find(P("Users/me/repos/app/node_modules/left-pad/index.js")));
        Assert.Empty(index.Search("index", 10));
        Assert.Empty(index.Search("report.exe", 10));
    }

    [Fact]
    public void SearchRanksFoldersAndNamesLikeTheFinder()
    {
        var index = DiskIndex.Walk([root], NoSkip);
        var hits = index.Search("report", 10);
        // Every name holding the word, any case; folders first, then by fzy's score on the name.
        Assert.Equal("Reports", hits[0].Name);
        Assert.True(hits[0].IsDir);
        Assert.Equal("report.exe", hits[1].Name);
        Assert.Equal(["Report 2026.docx", "report-cover.png"], hits.Skip(2).Select(h => h.Name).Order());
        // Every word has to be there; *.png keeps pictures only.
        Assert.Equal(["Report 2026.docx"], index.Search("2026 rep", 10).Select(h => h.Name));
        Assert.Equal(["report-cover.png"], index.Search("rep *.png *.jpg", 10).Select(h => h.Name));
        Assert.Equal(["repos", "Reports"], index.Search("rep", 10, foldersOnly: true).Select(h => h.Name));
    }

    [Fact]
    public void PathWordsAndHomeFirst()
    {
        var index = DiskIndex.Walk([root], NoSkip);
        Assert.Equal(["main.rs"], index.Search("src/ma", 10).Select(h => h.Name));
        Make("Users/me/Documents/same.txt");
        Make("Program Files/Tool/same.txt");
        index = DiskIndex.Walk([root], NoSkip);
        // Two equal names: the one under the preferred folder (home) first.
        var hits = index.Search("same", 10, preferred: P("Users/me"));
        Assert.Equal(P("Users/me/Documents/same.txt"), hits[0].Path);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void ChangesKeepItCurrent()
    {
        var index = DiskIndex.Walk([root], NoSkip);
        Make("Users/me/Documents/new plan.txt");
        Assert.True(index.Created(P("Users/me/Documents/new plan.txt"), dir: false, NoSkip));
        Assert.Single(index.Search("plan", 10));
        // A folder moved in arrives as one event: what's in it is walked.
        Make("Users/me/Downloads/archive/inner/deep.txt");
        Assert.True(index.Created(P("Users/me/Downloads"), dir: true, NoSkip));
        Assert.Equal(P("Users/me/Downloads/archive/inner/deep.txt"), Assert.Single(index.Search("deep", 10)).Path);
        // Renamed in its folder: found by the new name only, from the walk and from later.
        index.Renamed(P("Users/me/Documents/notes.md"), P("Users/me/Documents/journal.md"), dir: false, NoSkip);
        Assert.Empty(index.Search("notes", 10));
        Assert.True(index.Find(P("Users/me/Documents/journal.md")) >= 0);
        index.Renamed(P("Users/me/Documents/new plan.txt"), P("Users/me/Documents/old plan.txt"), dir: false, NoSkip);
        Assert.Equal(-1, index.Find(P("Users/me/Documents/new plan.txt")));
        Assert.True(index.Find(P("Users/me/Documents/old plan.txt")) >= 0);
        // A folder removed takes what's in it along.
        Assert.True(index.Deleted(P("Users/me/repos")));
        Assert.Empty(index.Search("main", 10));
        // Inside a skipped folder, or outside the roots: nothing.
        Assert.False(index.Created(P("Users/me/x/node_modules/a.js"), dir: false, new DiskIndex.Skip(["node_modules"])));
        Assert.False(index.Created(Path.Combine(Path.GetTempPath(), "elsewhere", "a.txt"), dir: false, NoSkip));
        // A folder deleted and made again is found again, and only once.
        Assert.True(index.Created(P("Users/me/repos"), dir: true, NoSkip));
        Assert.True(index.Created(P("Users/me/repos"), dir: true, NoSkip));
        Assert.Single(index.Search("main", 10));
    }

    [Fact]
    public void SavedAndLoadedIsTheSame()
    {
        var index = DiskIndex.Walk([root], NoSkip);
        Make("Users/me/later.txt");
        index.Created(P("Users/me/later.txt"), dir: false, NoSkip);
        index.Deleted(P("Users/me/Documents/notes.md"));
        var file = Path.Combine(root, "state", "index.bin");
        index.Save(file);
        var loaded = DiskIndex.Load(file);
        Assert.NotNull(loaded);
        Assert.Equal(index.Live, loaded!.Live);
        Assert.Equal(index.Built, loaded.Built);
        Assert.True(loaded.Find(P("Users/me/later.txt")) >= 0);
        Assert.Equal(-1, loaded.Find(P("Users/me/Documents/notes.md")));
        Assert.Equal(index.Search("report", 10).Select(h => h.Path), loaded.Search("report", 10).Select(h => h.Path));
        // A file that isn't one, or is cut short, is no index.
        File.WriteAllText(file, "nope");
        Assert.Null(DiskIndex.Load(file));
        index.Save(file);
        var bytes = File.ReadAllBytes(file);
        File.WriteAllBytes(file, bytes[..(bytes.Length / 2)]);
        Assert.Null(DiskIndex.Load(file));
        Assert.Null(DiskIndex.Load(Path.Combine(root, "missing.bin")));
    }

    [Fact]
    public void LinksAreListedNotFollowed()
    {
        Directory.CreateSymbolicLink(P("Users/me/loop"), P("Users"));
        var index = DiskIndex.Walk([root], NoSkip);
        Assert.True(index.Find(P("Users/me/loop")) >= 0);
        Assert.Equal(-1, index.Find(P("Users/me/loop/me")));
    }
}
