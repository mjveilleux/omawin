using Omawin.Cli.Profile;

namespace Omawin.Cli.Tests;

// `omawin profile menu`'s arithmetic: trace.log's marks into runs, medians and the report.
// Ticks here are at 1 MHz, so 1000 ticks is 1 ms.
public class MenuProfileTests
{
    const long Freq = 1_000_000;
    static string M(string label, long ms) => $"{label}\t{ms * 1000}";

    [Fact]
    public void ParseSortsByStampAndSkipsJunk()
    {
        var marks = MenuProfile.Parse(["menu painted\t3000", "not a mark", "driver key\t1000\r", "menu shown\t2000", "bad\tx"]);
        Assert.Equal(["driver key", "menu shown", "menu painted"], marks.Select(m => m.Label));
    }

    [Fact]
    public void StepsMatchWholeWordsOnly()
    {
        Assert.True(MenuProfile.Is("menu handle --launcher", "menu handle"));
        Assert.True(MenuProfile.Is("menu handle", "menu handle"));
        Assert.False(MenuProfile.Is("menu handler", "menu handle"));
        Assert.False(MenuProfile.Is("menu painted", "menu paint"));
    }

    [Fact]
    public void RunsAreTimedFromTheirKeyAndEndAtTheNext()
    {
        var marks = MenuProfile.Parse([
            "driver surface launcher\t0",
            M("driver key", 10), M("omawin process-created", 30), M("menu shown", 50), M("menu painted", 60),
            "driver surface menu\t61000",
            M("driver key", 100), M("menu shown", 140),
            // a late frame from the first run's window can't count for the second: it's after this key
            M("driver key", 200), M("menu painted", 230),
        ]);
        var runs = MenuProfile.Runs(marks, Freq, ["omawin process-created", "menu shown", "menu painted"]);
        Assert.Equal(3, runs.Count);
        Assert.Equal("launcher", runs[0].Group);
        Assert.Equal(20, runs[0].At("omawin process-created"));
        Assert.Equal(50, runs[0].At("menu painted"));
        Assert.Equal("menu", runs[1].Group);
        Assert.Null(runs[1].At("menu painted"));
        Assert.Equal("menu", runs[2].Group);
        Assert.Equal(30, runs[2].At("menu painted"));
    }

    [Fact]
    public void TheFirstMarkOfAStepCounts()
    {
        var marks = MenuProfile.Parse([M("driver key", 0), M("menu handle --launcher", 5), M("menu handle --launcher", 9)]);
        Assert.Equal(5, MenuProfile.Runs(marks, Freq, ["menu handle"])[0].At("menu handle"));
    }

    [Fact]
    public void AHandRunStartsAtTheFirstMarkAfterReady()
    {
        // Pressed by hand: nothing marks the key, so the first sign of it is the zero.
        var marks = MenuProfile.Parse(["driver surface launcher\t0", M("driver ready", 1), M("driver note", 2), M("omawin process-created", 500), M("menu painted", 540)]);
        var run = Assert.Single(MenuProfile.Runs(marks, Freq, ["omawin process-created", "menu painted"]));
        Assert.Equal(0, run.At("omawin process-created"));
        Assert.Equal(40, run.At("menu painted"));
    }

    [Fact]
    public void MedianIsTheLowerMiddleAndIgnoresMissing()
    {
        Assert.Equal(3, MenuProfile.Median([5, null, 1, 3]));
        Assert.Equal(2, MenuProfile.Median([4, 1, 2, 3]));
        Assert.Null(MenuProfile.Median([null, null]));
    }

    [Fact]
    public void SurfaceReportHasATimelinePerSurfaceAndASummary()
    {
        var lines = new List<string>();
        long t = 0;
        foreach (var surface in new[] { "launcher", "files" })
        {
            lines.Add(M("driver surface " + surface, t));
            foreach (var painted in new[] { 40, 30, 50 })
            {
                t += 1000;
                lines.AddRange([M("driver key", t), M("menu request", t + 20), M("menu shown", t + painted - 8), M("menu painted", t + painted)]);
            }
        }
        var report = MenuProfile.Report(lines, Freq, typing: false);
        Assert.Contains("Launcher (Win+Space): ms since the key", report);
        Assert.Contains("File finder (Win+F)", report);
        Assert.Contains("menu app woke (signal)", report);
        Assert.DoesNotContain("menu app got the emit", report); // no run took that path
        var summary = report[report.IndexOf("Key to first frame")..];
        Assert.Matches(@"Launcher \(Win\+Space\)\s+40\n", summary);
    }

    [Fact]
    public void TypingReportHasARowPerPrefixAndTheBudget()
    {
        var lines = new List<string>();
        long t = 0;
        foreach (var run in new[] { 1, 2 })
            foreach (var (typed, ms) in new[] { ("v", 9), ("vi", 12), ("vis", 20 + run) })
            {
                t += 1000;
                lines.AddRange([M("driver type " + typed, t - 1), M("driver key", t), M("menu typed", t + 2), M("menu rendered", t + ms - 3), M("menu list-painted", t + ms)]);
            }
        var report = MenuProfile.Report(lines, Freq, typing: true);
        Assert.Contains("(median over 2 runs)", report);
        Assert.Matches("\"vi\"\\s+-\\s+2.0\\s+9.0\\s+12\\s+12\\n", report);
        Assert.Contains("Over the 16 ms budget: \"vis\" takes 21.0 ms", report);
    }

    [Fact]
    public void NoRunsSaysSo() => Assert.StartsWith("No runs", MenuProfile.Report(["menu painted\t5"], Freq, typing: false));
}
