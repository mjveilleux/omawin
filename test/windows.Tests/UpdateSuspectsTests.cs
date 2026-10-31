namespace Omawin.Windows.Tests;

// 8.44: crashes that began right after an update are pinned on it; ones that were always there aren't.
public class UpdateSuspectsTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Since = Now.AddDays(-UpdateSuspects.DaysNeeded);

    static LogEvent Ev(string provider, int id, DateTimeOffset at, params (string, string)[] data) =>
        new(provider, id, at, data.ToDictionary(d => d.Item1, d => d.Item2), data.Select(d => d.Item2).ToList());

    static LogEvent BlueScreen(DateTimeOffset at) => Ev(HealthEvents.BugCheck, 1001, at, ("param1", "0x0000009f (0x3)"));
    static LogEvent Crash(string app, DateTimeOffset at) => Ev(HealthEvents.AppError, 1000, at, ("AppName", app));

    static InstalledUpdate Installed(DateTimeOffset at, string title) => new(at.UtcDateTime, title);

    const string Cumulative = "2026-10 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5031234)";

    [Fact]
    public void BlueScreensThatBeganAfterAnUpdateNameIt()
    {
        var at = Now.AddDays(-10);
        var installs = new[]
        {
            Installed(at, Cumulative),
            Installed(at.AddMinutes(20), "2026-10 Cumulative Update for .NET Framework 3.5 and 4.8.1 (KB5031000)"),
            Installed(Now.AddDays(-2), "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.401.1)"),
        };
        var events = new[] { BlueScreen(at.AddDays(1)), BlueScreen(at.AddDays(3)), BlueScreen(at.AddDays(5)), BlueScreen(at.AddDays(8)) };
        var s = Assert.Single(UpdateSuspects.Find(installs, events, Since, Now));
        Assert.Equal("blue screens", s.Problem);
        Assert.Equal(0, s.Before);
        Assert.Equal(4, s.After);
        Assert.Equal(2, s.Updates.Count); // the batch, not the daily definitions

        var lines = UpdateSuspects.Report([s], Now, TimeZoneInfo.Utc);
        Assert.Equal("Started after an update:", lines[0]);
        Assert.StartsWith("  ! Blue screens started after KB5031000 and KB5031234, installed 2026-10-21", lines[1]);
        Assert.EndsWith(": 4 since, none in the two weeks before.", lines[1]);
        Assert.Contains(lines, l => l.Contains("omawin updates uninstall KB5031234 takes it off"));
    }

    [Fact]
    public void ProblemsThatWereAlreadyThereArentPinned()
    {
        var at = Now.AddDays(-10);
        var events = Enumerable.Range(1, 20).Select(i => Crash("chrome.exe", at.AddDays(-i * 0.7 + 7))).ToList(); // steady, before and after
        Assert.Empty(UpdateSuspects.Find([Installed(at, Cumulative)], events, Since, Now));
    }

    [Fact]
    public void ProblemsThatStartWeeksLaterArentPinned()
    {
        var at = Now.AddDays(-20);
        var events = new[] { Crash("app.exe", at.AddDays(6)), Crash("app.exe", at.AddDays(7)), Crash("app.exe", at.AddDays(8)) };
        Assert.Empty(UpdateSuspects.Find([Installed(at, Cumulative)], events, Since, Now));
    }

    [Fact]
    public void AnInstallWhoseWeeksBeforeArentReadIsntJudged()
    {
        var at = Now.AddDays(-10);
        var events = new[] { BlueScreen(at.AddHours(5)), BlueScreen(at.AddDays(1)), BlueScreen(at.AddDays(2)) };
        Assert.Single(UpdateSuspects.Find([Installed(at, Cumulative)], events, Since, Now));
        // The log only reaches 5 days before the install: the two weeks before can't be counted.
        Assert.Empty(UpdateSuspects.Find([Installed(at, Cumulative)], events, at.AddDays(-5), Now));
    }

    [Fact]
    public void AKernelPowerStopBesideItsBlueScreenCountsOnce()
    {
        var at = Now.AddDays(-10);
        var events = new List<LogEvent>();
        foreach (var d in new[] { 1, 2, 3 })
        {
            events.Add(BlueScreen(at.AddDays(d)));
            events.Add(Ev(HealthEvents.KernelPower, 41, at.AddDays(d).AddMinutes(-2), ("BugcheckCode", "159")));
        }
        var problems = UpdateSuspects.Problems(events);
        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.Equal("blue screens", p.Kind));
        Assert.Equal("unexpected restarts", UpdateSuspects.Problems([Ev(HealthEvents.KernelPower, 41, at, ("BugcheckCode", "0"))])[0].Kind);
    }

    [Theory]
    [InlineData("KB5031234", "5031234")]
    [InlineData("kb5031234", "5031234")]
    [InlineData(" 5031234 ", "5031234")]
    [InlineData("KB12", null)]
    [InlineData("5031234; rm", null)]
    public void KbNumbers(string text, string? kb) => Assert.Equal(kb, UpdateSuspects.ParseKb(text));

    [Fact]
    public void KbFromATitle()
    {
        Assert.Equal("5031234", UpdateSuspects.Kb(Cumulative));
        Assert.Null(UpdateSuspects.Kb("Intel Corporation - Display - 31.0.101.5186"));
    }

    [Fact]
    public void UninstallScriptKeepsAWayBackAndOnlyTakesTheNamedKb()
    {
        var s = AdminScripts.UpdateUninstall("5031234", UpdatePlan.PauseValues(Now, 7), "s");
        int restore = s.IndexOf("Checkpoint-Computer", StringComparison.Ordinal);
        int wusa = s.IndexOf("'/kb:5031234'", StringComparison.Ordinal);
        int pause = s.IndexOf("'PauseUpdatesExpiryTime' -Value '2026-11-07T12:00:00Z'", StringComparison.Ordinal);
        Assert.True(restore > 0 && wusa > restore && pause > wusa, "restore point, then the uninstall, then the pause");
        Assert.Contains("'KB5031234(\\D|$)", s);              // DISM only on the package that names it
        Assert.DoesNotContain("Sort-Object InstallTime", s);  // never "the newest rollup" as a guess
        Assert.Throws<ArgumentException>(() => AdminScripts.UpdateUninstall("KB5031234", [], "s"));
        Assert.Throws<ArgumentException>(() => AdminScripts.UpdateUninstall("5031234'; rm", [], "s"));
    }
}
