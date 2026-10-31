namespace Omawin.Windows.Tests;

// Windows Update without surprises (8.18): the values the pause writes, the hours and times typed,
// and the sentences.
public class UpdatesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 31, 14, 5, 0, TimeSpan.Zero);

    [Fact]
    public void PauseWritesSettingsOwnValuesInUtc()
    {
        var v = UpdatePlan.PauseValues(new DateTimeOffset(2026, 10, 31, 15, 5, 0, TimeSpan.FromHours(1)), 7);
        Assert.Equal(6, v.Count);
        Assert.Contains(("PauseUpdatesStartTime", "2026-10-31T14:05:00Z"), v);
        Assert.Contains(("PauseUpdatesExpiryTime", "2026-11-07T14:05:00Z"), v);
        Assert.Contains(("PauseQualityUpdatesEndTime", "2026-11-07T14:05:00Z"), v);
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdatePlan.PauseValues(Now, 36));
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdatePlan.PauseValues(Now, 0));
    }

    [Fact]
    public void AnExpiredPauseIsNoPause()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 14, 5, 0, TimeSpan.Zero), UpdatePlan.PausedUntil("2026-11-07T14:05:00Z", Now));
        Assert.Null(UpdatePlan.PausedUntil("2026-10-01T00:00:00Z", Now));
        Assert.Null(UpdatePlan.PausedUntil(null, Now));
        Assert.Null(UpdatePlan.PausedUntil("soon", Now));
    }

    [Theory]
    [InlineData("9-18", 9, 18)]
    [InlineData("9:00-18:00", 9, 18)]
    [InlineData("22-6", 22, 6)]   // across midnight: 8 hours
    [InlineData("0-18", 0, 18)]
    public void HoursParse(string text, int start, int end) => Assert.Equal((start, end), UpdatePlan.ParseHours(text, out _));

    [Theory]
    [InlineData("8-8", "same hour")]
    [InlineData("6-1", "at most 18")]   // 19 hours
    [InlineData("9", "two hours")]
    [InlineData("9-24", "two hours")]
    [InlineData("nine-five", "two hours")]
    public void HoursRefused(string text, string why)
    {
        Assert.Null(UpdatePlan.ParseHours(text, out var said));
        Assert.Contains(why, said);
    }

    [Fact]
    public void RestartTimeIsTheNextOne()
    {
        var evening = new DateTime(2026, 10, 31, 21, 0, 0);
        var (when, seconds) = UpdatePlan.Next(UpdatePlan.Tonight, evening);
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0), when);
        Assert.Equal(6 * 3600, seconds);
        Assert.Equal(new DateTime(2026, 10, 31, 22, 30, 0), UpdatePlan.Next(UpdatePlan.ParseTime("22:30")!.Value, evening).When);
        // A time a few seconds away is tomorrow's, not a restart without warning.
        Assert.Equal(new DateTime(2026, 11, 1, 21, 0, 30), UpdatePlan.Next(new TimeOnly(21, 0, 30), evening).When);
        Assert.Null(UpdatePlan.ParseTime("25:00"));
        Assert.Equal(new TimeOnly(3, 0), UpdatePlan.ParseTime("3"));
    }

    [Fact]
    public void ReportSaysTheRestartFirstAndListsWhatWaits()
    {
        var s = new UpdateState(true, Now.AddDays(3), 9, 18, false, null,
            [new PendingUpdate("2026-10 Cumulative Update for Windows 11 (KB5099999)", ["5099999"], true, true),
             new PendingUpdate("Defender definitions", ["2267602"], false, false)],
            [new InstalledUpdate(new DateTime(2026, 10, 30, 10, 0, 0), "Some update")], null);
        var lines = UpdatePlan.Report(s, Now, TimeZoneInfo.Utc).ToList();
        Assert.StartsWith("A restart is owed", lines[0]);
        Assert.Contains("paused until Tue 3 Nov 14:05", lines[1]);
        Assert.Contains("09:00 to 18:00", lines[2]);
        Assert.Contains("  2026-10 Cumulative Update for Windows 11 (KB5099999)  (downloaded, needs a restart)", lines);
        Assert.Contains("  Defender definitions (KB2267602)", lines);
        Assert.Contains(lines, l => l.Contains("Some update"));
    }

    [Fact]
    public void ReportSaysWhenARestartIsSet()
    {
        var s = new UpdateState(true, null, null, null, true, Now.AddHours(13), null, null, "the agent is busy");
        var lines = UpdatePlan.Report(s, Now, TimeZoneInfo.Utc).ToList();
        Assert.Contains("set for tomorrow 03:05", lines[0]);
        Assert.Contains("aren't paused", lines[1]);
        Assert.Contains("Windows picks them", lines[2]);
        Assert.Contains(lines, l => l.Contains("couldn't be read (the agent is busy)"));
    }

    [Fact]
    public void PauseScriptWritesOnlyCheckedValues()
    {
        var s = AdminScripts.UpdatePause(UpdatePlan.PauseValues(Now, 7), "s");
        Assert.Contains(@"'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'", s);
        Assert.Contains("-Name 'PauseUpdatesExpiryTime' -Value '2026-11-07T14:05:00Z' -PropertyType String", s);
        Assert.Throws<ArgumentException>(() => AdminScripts.UpdatePause([("Evil", "2026-11-07T14:05:00Z")], "s"));
        Assert.Throws<ArgumentException>(() => AdminScripts.UpdatePause([("PauseUpdatesExpiryTime", "'; rm")], "s"));
        var resume = AdminScripts.UpdatePause([], "s");
        Assert.Equal(6, resume.Split("Remove-ItemProperty").Length - 1);
    }

    [Fact]
    public void ActiveHoursScript()
    {
        var s = AdminScripts.UpdateActiveHours((9, 18), "s");
        Assert.Contains("'ActiveHoursStart' -Value 9 ", s);
        Assert.Contains("'ActiveHoursEnd' -Value 18 ", s);
        Assert.Contains("'SmartActiveHoursState' -Value 0 ", s);
        Assert.Contains("'SmartActiveHoursState' -Value 1 ", AdminScripts.UpdateActiveHours(null, "s"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdminScripts.UpdateActiveHours((9, 24), "s"));
    }
}
