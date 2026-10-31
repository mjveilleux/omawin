namespace Omawin.Windows.Tests;

public class TopTests
{
    static Proc P(uint pid, uint parent, string name, long created, long cpu = 0, long mb = 10) =>
        new(pid, parent, name, created, cpu, mb << 20, 1);

    static readonly Proc[] Before =
    [
        P(4, 0, "System", 1, cpu: 0),
        P(100, 4, "explorer.exe", 10, cpu: 1000),
        P(200, 100, "chrome.exe", 20, cpu: 0),
        P(201, 200, "chrome.exe", 21, cpu: 0),
        P(202, 200, "chrome.exe", 22, cpu: 0),
        P(300, 100, "Code.exe", 30, cpu: 0),
        P(301, 300, "msedgewebview2.exe", 31, cpu: 0),
        P(400, 4, "svchost.exe", 5, cpu: 0),
    ];

    [Fact]
    public void HelpersAndChildrenOfTheSameNameCountTowardTheirApp()
    {
        var byPid = Before.ToDictionary(p => p.Pid);
        Assert.Equal("chrome", ProcessGroups.AppOf(byPid[202], byPid));
        Assert.Equal("Code", ProcessGroups.AppOf(byPid[301], byPid));
        Assert.Equal("explorer", ProcessGroups.AppOf(byPid[100], byPid));
        Assert.Equal(ProcessGroups.WindowsGroup, ProcessGroups.AppOf(byPid[400], byPid));
    }

    [Fact]
    public void AReusedParentPidIsNotAParent()
    {
        // 501's parent pid now belongs to a chrome.exe that started after it: not its parent.
        var procs = new[] { P(500, 1, "chrome.exe", 100), P(501, 500, "chrome.exe", 50) };
        Assert.Equal("chrome", ProcessGroups.AppOf(procs[1], procs.ToDictionary(p => p.Pid)));
        var helper = new[] { P(600, 1, "Code.exe", 100), P(601, 600, "conhost.exe", 50) };
        Assert.Equal("conhost", ProcessGroups.AppOf(helper[1], helper.ToDictionary(p => p.Pid)));
    }

    [Fact]
    public void CpuIsTheShareOfAllCoresOverTheInterval()
    {
        // One second (10^7 ticks) on 4 cores: chrome's tabs used 0.5 s and 0.3 s = 20 %.
        var now = Before.Select(p => p.Pid switch
        {
            201 => p with { CpuTicks = 5_000_000 },
            202 => p with { CpuTicks = 3_000_000 },
            _ => p,
        }).Append(P(700, 100, "new.exe", 70, cpu: 9_000_000)).ToList(); // started since: no rate yet
        var groups = ProcessGroups.Group(now, Before, 10_000_000, 4);
        var chrome = groups[0];
        Assert.Equal("chrome", chrome.Name);
        Assert.Equal(3, chrome.Processes);
        Assert.Equal(20.0, chrome.Cpu, 3);
        Assert.Equal(30L << 20, chrome.PrivateBytes);
        Assert.Equal(0, groups.Single(g => g.Name == "new").Cpu);
    }

    [Fact]
    public void FilterMatchesAppsOrTheirProcesses()
    {
        var groups = ProcessGroups.Group(Before, null, 0, 1);
        Assert.Equal(["Code"], ProcessGroups.Filter(groups, "webview").Select(g => g.Name));
        Assert.Equal(groups.Count, ProcessGroups.Filter(groups, null).Count);
    }

    [Theory]
    [InlineData(512L << 10, "512 KB")]
    [InlineData(340L << 20, "340 MB")]
    [InlineData((long)(1.2 * (1L << 30)), "1.2 GB")]
    public void SizesRead(long bytes, string text) => Assert.Equal(text, ProcessGroups.Size(bytes));
}
