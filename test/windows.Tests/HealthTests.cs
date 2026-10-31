namespace Omawin.Windows.Tests;

// The event log's events as Windows renders them (EvtRender's XML), turned into sentences.
public class HealthTests
{
    static string Event(string provider, int id, string time, string data) =>
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System>" +
        $"<Provider Name='{provider}' Guid='{{331c3b3a-2005-44c2-ac5e-77220c37d6b4}}'/><EventID Qualifiers='0'>{id}</EventID>" +
        "<Version>8</Version><Level>1</Level><Task>63</Task>" +
        $"<TimeCreated SystemTime='{time}'/><EventRecordID>1</EventRecordID><Channel>System</Channel><Computer>PC</Computer>" +
        $"</System>{data}</Event>";

    static string Named(params (string Name, string Value)[] fields) =>
        "<EventData>" + string.Concat(fields.Select(f => $"<Data Name='{f.Name}'>{f.Value}</Data>")) + "</EventData>";

    static LogEvent Parse(string provider, int id, string data, string time = "2026-10-29T07:15:02.5000000Z") =>
        HealthEvents.Parse(Event(provider, id, time, data));

    [Fact]
    public void ParsesNamedDataAndTimeAsUtc()
    {
        var e = Parse(HealthEvents.KernelPower, 41, Named(("BugcheckCode", "0"), ("SleepInProgress", "4")));
        Assert.Equal(HealthEvents.KernelPower, e.Provider);
        Assert.Equal(41, e.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 7, 15, 2, 500, TimeSpan.Zero), e.Time);
        Assert.Equal("4", e.Data["SleepInProgress"]);
    }

    [Theory]
    [InlineData("0", "0", "0", "Restarted without shutting down first")]
    [InlineData("159", "0", "0", "Crashed (blue screen, stop code 0x9F)")]
    [InlineData("0", "133436571234567890", "0", "holding the power button")]
    [InlineData("0", "0", "4", "asleep")]
    public void KernelPower41SaysWhatKindOfStop(string bugcheck, string button, string sleep, string says)
    {
        var e = Parse(HealthEvents.KernelPower, 41, Named(("BugcheckCode", bugcheck), ("PowerButtonTimestamp", button), ("SleepInProgress", sleep)));
        var f = HealthEvents.Explain(e)!;
        Assert.Contains(says, f.Text);
        Assert.True(f.Bad);
    }

    [Fact]
    public void BlueScreenNamesItsStopCode()
    {
        var e = Parse(HealthEvents.BugCheck, 1001, Named(("param1", "0x0000009f (0x0000000000000003, 0xffffa00b1c2d3e40)"), ("param2", @"C:\WINDOWS\MEMORY.DMP"), ("param3", "abc")));
        Assert.Equal(@"Blue screen: 0x9F DRIVER_POWER_STATE_FAILURE (dump: C:\WINDOWS\MEMORY.DMP)", HealthEvents.Explain(e)!.Text);
        Assert.Equal("0x123", HealthEvents.StopCode("0x00000123"));
        Assert.Equal("weird", HealthEvents.StopCode("weird"));
    }

    [Fact]
    public void WindowsUpdateRestartIsNamed()
    {
        var e = Parse(HealthEvents.User32, 1074, Named(
            ("param1", @"C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe (DESKTOP-1)"), ("param2", "DESKTOP-1"),
            ("param3", "Operating System: Service pack (Planned)"), ("param4", "0x80020010"), ("param5", "restart"),
            ("param6", ""), ("param7", @"NT AUTHORITY\SYSTEM")));
        Assert.Equal("Restart, by Windows Update: Operating System: Service pack (Planned)", HealthEvents.Explain(e)!.Text);
        Assert.False(HealthEvents.Explain(e)!.Bad);
    }

    [Fact]
    public void AnotherProgramsRestartNamesItAndTheAccount()
    {
        var e = Parse(HealthEvents.User32, 1074, Named(
            ("param1", @"C:\Program Files\Vendor\updater.exe (PC)"), ("param3", "Application: Installation (Planned)"),
            ("param5", "restart"), ("param6", "installing drivers"), ("param7", @"PC\me")));
        Assert.Equal(@"Restart, by updater (as PC\me): Application: Installation (Planned) ""installing drivers""", HealthEvents.Explain(e)!.Text);
    }

    [Fact]
    public void AppCrashReadsNamedOrUnnamedData()
    {
        var named = Parse(HealthEvents.AppError, 1000, Named(("AppName", "Code.exe"), ("AppVersion", "1.95.0.0"), ("ModuleName", "ntdll.dll"), ("ExceptionCode", "c0000005")));
        Assert.Equal("Code.exe crashed (in ntdll.dll)", HealthEvents.Explain(named)!.Text);
        var unnamed = Parse(HealthEvents.AppError, 1000, "<EventData><Data>game.exe</Data><Data>1.0</Data><Data>5f00</Data><Data>game.exe</Data></EventData>");
        Assert.Equal("game.exe crashed", HealthEvents.Explain(unnamed)!.Text);
    }

    [Fact]
    public void WakeSourceIsWorded()
    {
        var timer = Parse(HealthEvents.PowerTroubleshooter, 1, Named(("WakeSourceText", "Timer - Windows will execute 'NT TASK\\Microsoft\\Windows\\UpdateOrchestrator\\Reboot'")));
        Assert.StartsWith("Woke from sleep: a timer (Windows will execute", HealthEvents.Explain(timer)!.Text);
        var device = Parse(HealthEvents.PowerTroubleshooter, 1, Named(("WakeSourceText", "Device -USB Composite Device")));
        Assert.Equal("Woke from sleep: a device (USB Composite Device)", HealthEvents.Explain(device)!.Text);
        var unknown = Parse(HealthEvents.PowerTroubleshooter, 1, Named(("WakeSourceText", "Unknown")));
        Assert.Contains("unknown", HealthEvents.Explain(unknown)!.Text);
    }

    [Fact]
    public void SlowBootsSayHowLong()
    {
        var boot = Parse(HealthEvents.BootPerformance, 100, Named(("BootTime", "84321"), ("MainPathBootTime", "21000")));
        Assert.Equal("Took 84 s to start (21 s to the desktop, the rest getting ready after sign-in)", HealthEvents.Explain(boot)!.Text);
        var app = Parse(HealthEvents.BootPerformance, 101, Named(("Name", "OneDrive.exe"), ("FriendlyName", "Microsoft OneDrive"), ("DegradationTime", "5400")));
        Assert.Equal("Start slowed by the app Microsoft OneDrive, 5.4 s longer", HealthEvents.Explain(app)!.Text);
    }

    [Fact]
    public void UserDataFieldsAreRead()
    {
        var xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='X'/><EventID>7</EventID>" +
                  "<TimeCreated SystemTime='2026-10-29T07:15:02Z'/></System><UserData><Thing xmlns='urn:x'><A>1</A><B>two</B></Thing></UserData></Event>";
        var e = HealthEvents.Parse(xml);
        Assert.Equal("two", e.Data["B"]);
        Assert.Equal(["1", "two"], e.Values);
    }

    [Fact]
    public void ReportDropsTheKernelPowerEventBesideABlueScreenAndCountsCrashes()
    {
        var events = new List<LogEvent>
        {
            Parse(HealthEvents.KernelPower, 41, Named(("BugcheckCode", "159")), "2026-10-30T08:00:00Z"),
            Parse(HealthEvents.BugCheck, 1001, Named(("param1", "0x0000009f (0x3)")), "2026-10-30T08:01:30Z"),
            Parse(HealthEvents.KernelGeneral, 12, Named(), "2026-10-30T08:01:00Z"),
            Parse(HealthEvents.KernelGeneral, 12, Named(), "2026-10-29T08:01:00Z"),
            Parse(HealthEvents.AppError, 1000, Named(("AppName", "Code.exe"), ("ModuleName", "x.dll")), "2026-10-28T08:00:00Z"),
            Parse(HealthEvents.AppError, 1000, Named(("AppName", "Code.exe"), ("ModuleName", "x.dll")), "2026-10-30T09:00:00Z"),
        };
        var lines = HealthEvents.Report(events, 30, new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Contains("Restarts and crashes, last 30 days (2 starts in all):", lines);
        Assert.Single(lines, l => l.Contains("Blue screen: 0x9F"));
        Assert.DoesNotContain(lines, l => l.Contains("Crashed (blue screen"));
        Assert.Single(lines, l => l.Contains("Code.exe crashed (in x.dll), 2 times") && l.Contains("today 09:00"));
    }

    [Fact]
    public void ReportSaysWhenThereIsNothing()
    {
        var lines = HealthEvents.Report([], 7, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        Assert.Contains("  none: every shutdown was a clean one", lines);
        Assert.Contains("  nothing recorded", lines);
    }

    [Fact]
    public void TimesReadAsDays()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero); // a Saturday
        Assert.Equal("today 08:05", HealthEvents.Ago(now.AddHours(-3).AddMinutes(-55), now, TimeZoneInfo.Utc));
        Assert.Equal("yesterday 23:40", HealthEvents.Ago(new DateTimeOffset(2026, 10, 30, 23, 40, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc));
        Assert.Equal("Mon 14:05", HealthEvents.Ago(new DateTimeOffset(2026, 10, 26, 14, 5, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc));
        Assert.Equal("2026-10-12 09:30", HealthEvents.Ago(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void QueriesFilterByProviderAndTime()
    {
        var q = HealthEvents.Queries(30);
        Assert.Contains(q, x => x.Log == "System" && x.XPath == "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");
        Assert.Contains(q, x => x.Log == "Application" && x.XPath.Contains("'Application Error'"));
    }
}
