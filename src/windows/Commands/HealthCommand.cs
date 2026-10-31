namespace Omawin.Windows;

// omawin health [days] (8.15): why the PC restarted, crashed or woke up, from Windows' own event
// logs, in sentences. 30 days unless said; read only.
static class HealthCommand
{
    public static int Run(string[] args)
    {
        int days = args.Length > 0 && int.TryParse(args[0], out var d) && d is > 0 and <= 365 ? d : 30;
        var events = new List<LogEvent>();
        var notes = new List<string>();
        foreach (var (log, xpath) in HealthEvents.Queries(days))
            try { events.AddRange(EventLog.Query(log, xpath)); }
            catch (UnauthorizedAccessException) { notes.Add($"(the {log} log needs admin to read here)"); }
        var (bootLog, bootXPath) = HealthEvents.BootQuery(days);
        try { events.AddRange(EventLog.Query(bootLog, bootXPath, 100)); }
        catch (UnauthorizedAccessException) { notes.Add("(slow starts: their log needs admin or the Event Log Readers group to read; omawin health from an admin terminal shows them)"); }

        if (RestartPending()) Console.WriteLine("Windows is waiting for a restart to finish installing updates.\n");
        foreach (var line in HealthEvents.Report(events, days, DateTimeOffset.Now, TimeZoneInfo.Local)) Console.WriteLine(line);
        if (notes.Count > 0) { Console.WriteLine(); foreach (var n in notes) Console.WriteLine(n); }
        return 0;
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
