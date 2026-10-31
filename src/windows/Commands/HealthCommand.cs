namespace Omawin.Windows;

// omawin health [days] (8.15, 8.41, 8.44): why the PC restarted, crashed or woke up, from Windows'
// own event logs, in sentences; the update a run of crashes started after; and the disks, battery
// and devices. 30 days unless said; read only, no admin.
//   omawin health [days]      all of it
//   omawin health hardware    the disks, battery and devices alone
static class HealthCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("hardware", StringComparison.OrdinalIgnoreCase))
        {
            Hardware();
            return 0;
        }
        int days = args.Length > 0 && int.TryParse(args[0], out var d) && d is > 0 and <= 365 ? d : 30;
        var now = DateTimeOffset.Now;
        var notes = new List<string>();
        // Enough days back that every update in 8.44's lookback has its two weeks before it; the
        // report itself shows only the days asked for.
        var (events, since) = ReadEvents(Math.Max(days, UpdateSuspects.DaysNeeded), now, notes);
        var (bootLog, bootXPath) = HealthEvents.BootQuery(days);
        try { events.AddRange(EventLog.Query(bootLog, bootXPath, 100)); }
        catch (UnauthorizedAccessException) { notes.Add("(slow starts: their log needs admin or the Event Log Readers group to read; omawin health from an admin terminal shows them)"); }

        if (RestartPending()) Console.WriteLine("Windows is waiting for a restart to finish installing updates.\n");

        try
        {
            var suspects = UpdateSuspects.Report(UpdateSuspects.Find(UpdateAgent.History(100), events, since, now), now, TimeZoneInfo.Local);
            if (suspects.Count > 0)
            {
                foreach (var line in suspects) Console.WriteLine(line);
                Console.WriteLine();
            }
        }
        catch (InvalidOperationException ex) { notes.Add($"(Windows Update's history couldn't be read, so crashes weren't compared with updates: {ex.Message})"); }

        var shown = events.Where(e => e.Time >= now.AddDays(-days));
        foreach (var line in HealthEvents.Report(shown, days, now, TimeZoneInfo.Local)) Console.WriteLine(line);
        Console.WriteLine();
        Hardware();
        if (notes.Count > 0) { Console.WriteLine(); foreach (var n in notes) Console.WriteLine(n); }
        return 0;
    }

    // The problem events (HealthEvents.Queries) of the last `days`, and where they really start: a
    // log holding more than a query returns (500, newest first) is only covered back to its oldest
    // event returned, so a comparison with earlier days would undercount.
    public static (List<LogEvent> Events, DateTimeOffset Since) ReadEvents(int days, DateTimeOffset now, List<string> notes)
    {
        const int max = 500;
        var events = new List<LogEvent>();
        var since = now.AddDays(-days);
        foreach (var (log, xpath) in HealthEvents.Queries(days))
            try
            {
                var got = EventLog.Query(log, xpath, max);
                if (got.Count >= max && got.Min(e => e.Time) is var oldest && oldest > since) since = oldest;
                events.AddRange(got);
            }
            catch (UnauthorizedAccessException) { notes.Add($"(the {log} log needs admin to read here)"); }
        return (events, since);
    }

    static void Hardware()
    {
        DeviceHealthWin.Result r;
        try { r = DeviceHealthWin.Read(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Disks, battery and devices couldn't be read ({ex.Message}).");
            return;
        }
        foreach (var line in DeviceHealth.Report(r.Disks, r.Batteries, r.Devices, r.Notes)) Console.WriteLine(line);
    }

    // Windows Update and the servicing stack leave these keys while a restart is owed (readable
    // without admin): 8.18's first answer, before its page and bar line exist.
    static bool RestartPending()
    {
        using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        foreach (var path in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        })
            using (var key = hklm.OpenSubKey(path))
                if (key != null) return true;
        return false;
    }
}
