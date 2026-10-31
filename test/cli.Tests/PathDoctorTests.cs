namespace Omawin.Cli.Tests;

// omawin path (bin/path-doctor.cs): what it flags, which program it says wins, and what a fix
// keeps. A fake file system: Windows paths as plain strings, so it runs on Linux.
public class PathDoctorTests
{
    static readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["%USERPROFILE%"] = @"C:\Users\me", ["%SystemRoot%"] = @"C:\Windows", ["%LOCALAPPDATA%"] = @"C:\Users\me\AppData\Local",
    };
    static string Expand(string s) => Vars.Aggregate(s, (acc, v) => acc.Replace(v.Key, v.Value, StringComparison.OrdinalIgnoreCase));

    static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows\system32", @"C:\Windows", @"C:\Program Files\nodejs", @"C:\Program Files\Git\cmd",
        @"C:\Users\me\.local\share\mise\shims", @"C:\Users\me\AppData\Local\Microsoft\WindowsApps", @"C:\Users\me\.cargo\bin",
    };
    static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\nodejs\node.exe", @"C:\Users\me\.local\share\mise\shims\node.cmd",
        @"C:\Program Files\Git\cmd\git.exe", @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\python.exe",
        @"C:\Users\me\.cargo\bin\cargo.exe",
    };

    const string System = @"%SystemRoot%\system32;%SystemRoot%;C:\Program Files\nodejs\;C:\Program Files\Git\cmd;";
    const string User = @"%USERPROFILE%\.local\share\mise\shims;;C:\Gone\bin;%LOCALAPPDATA%\Microsoft\WindowsApps; C:\Windows\System32;C:\Windows\system32\;relative\bin;""C:\Program Files\nodejs"";%NOT_SET%\bin;%USERPROFILE%\.cargo\bin";

    static List<PathEntry> Analyze() => PathDoctor.Analyze(System, User, Expand, Folders.Contains);

    [Fact]
    public void FlagsEachKindOfProblem()
    {
        var problems = Analyze().Where(e => e.Problem != null).ToDictionary(e => e.Raw, e => e.Problem);
        Assert.Equal("empty", problems[""]);
        Assert.Equal("missing", problems[@"C:\Gone\bin"]);
        Assert.Equal("quotes or spaces around it", problems[@" C:\Windows\System32"]);
        Assert.Equal("duplicate (also in the system PATH)", problems[@"C:\Windows\system32\"]);   // case and trailing \ don't matter
        Assert.Equal("not a full path", problems[@"relative\bin"]);
        Assert.Equal("quotes or spaces around it", problems[@"""C:\Program Files\nodejs"""]);
        Assert.Equal("uses a variable that isn't set", problems[@"%NOT_SET%\bin"]);
        // The system PATH's trailing ; isn't an empty entry, and its own entries are fine.
        Assert.DoesNotContain(Analyze(), e => e.Scope == "system" && e.Problem != null);
    }

    // The mise problem (docs/dev-envs.md): node installed for every account beats your shims,
    // since Windows searches the system PATH first.
    [Fact]
    public void SaysWhichProgramWinsAndWhy()
    {
        var winners = PathDoctor.Winners(Analyze(), ["node", "python", "git", "cargo", "rg"], [".COM", ".EXE", ".BAT", ".CMD"], Files.Contains)
            .ToDictionary(w => w.Command);
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", winners["node"].File);
        Assert.Equal([@"C:\Users\me\.local\share\mise\shims\node.cmd"], winners["node"].Others);
        Assert.Contains("system PATH wins over yours", winners["node"].Note);
        Assert.Contains("app alias from the Store", winners["python"].Note);
        Assert.Null(winners["git"].Note);
        Assert.Equal("user", winners["cargo"].Scope);
        Assert.False(winners.ContainsKey("rg"));
    }

    [Fact]
    public void FixKeepsGoodEntriesRawAndInOrder()
    {
        Assert.Equal(@"%USERPROFILE%\.local\share\mise\shims;%LOCALAPPDATA%\Microsoft\WindowsApps;%USERPROFILE%\.cargo\bin", PathDoctor.FixedUser(Analyze()));
    }

    [Fact]
    public void ReportsLengthPastTheLimit()
    {
        var entries = Analyze();
        Assert.Contains("past 2047", PathDoctor.Report(entries, [], 3000));
        Assert.DoesNotContain("past 2047", PathDoctor.Report(entries, [], 900));
        Assert.Contains("omawin path fix", PathDoctor.Report(entries, [], 900));
    }
}
