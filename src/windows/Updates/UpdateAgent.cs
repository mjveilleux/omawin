using System.Diagnostics;
using Microsoft.Win32;

namespace Omawin.Windows;

// Windows Update's state, read without admin (8.18): the registry for the restart, the pause and the
// active hours; the Windows Update Agent (Microsoft.Update.Session, documented automation) for what's
// waiting and what was installed. The agent's search is offline: its last check's answer, from its
// own store, in about a second, with no download and nothing started.
public static class UpdateAgent
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
    // When omawin set a restart (shutdown.exe can't be asked): "yyyy-MM-ddTHH:mm:ssZ".
    public static readonly string RestartFile = Path.Combine(Dir, "update-restart.txt");

    public static UpdateState Read(bool withAgent)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var now = DateTimeOffset.UtcNow;
        bool pending = false;
        foreach (var path in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        })
            using (var k = hklm.OpenSubKey(path))
                pending |= k != null;

        DateTimeOffset? paused = null;
        int? start = null, end = null;
        bool automatic = false;
        using (var ux = hklm.OpenSubKey(UpdatePlan.UxKey))
        {
            paused = UpdatePlan.PausedUntil(ux?.GetValue("PauseUpdatesExpiryTime") as string, now);
            start = ux?.GetValue("ActiveHoursStart") as int?;
            end = ux?.GetValue("ActiveHoursEnd") as int?;
            automatic = ux?.GetValue("SmartActiveHoursState") is 1;
        }

        IReadOnlyList<PendingUpdate>? waiting = null;
        IReadOnlyList<InstalledUpdate>? recent = null;
        string? error = null;
        if (withAgent)
        {
            try
            {
                (waiting, recent, var agentSaysRestart) = Com.InStaThread(Agent);
                pending |= agentSaysRestart;
            }
            catch (InvalidOperationException ex) { error = ex.Message; }
        }
        return new UpdateState(pending, paused, start, end, automatic, ScheduledRestart(now), waiting, recent, error);
    }

    static (IReadOnlyList<PendingUpdate>, IReadOnlyList<InstalledUpdate>, bool) Agent()
    {
        bool restart = false;
        try
        {
            using var info = Dispatch.Create("Microsoft.Update.SystemInfo");
            restart = info.Get("RebootRequired") is true;
        }
        catch (InvalidOperationException) { } // older agents: the registry already answers

        using var session = Dispatch.Create("Microsoft.Update.Session");
        session.Set("ClientApplicationID", "omawin");
        using var searcher = session.CallObject("CreateUpdateSearcher");
        searcher.Set("Online", false);
        var pending = new List<PendingUpdate>();
        using (var result = searcher.CallObject("Search", "IsInstalled=0 and IsHidden=0"))
        using (var updates = result.GetObject("Updates"))
            foreach (var u in updates.Items())
                using (u)
                {
                    var kbs = u.Get("KBArticleIDs") is Dispatch ids ? Use(ids, d => d.Strings()) : [];
                    int behaviour = u.Get("InstallationBehavior") is Dispatch b ? Use(b, d => Convert.ToInt32(d.Get("RebootBehavior") ?? 0)) : 0;
                    pending.Add(new PendingUpdate(u.Get("Title") as string ?? "(untitled)", kbs, u.Get("IsDownloaded") is true, behaviour != 0));
                }

        return (pending, Installed(searcher, 50), restart);
    }

    // What Windows Update installed, newest first, from the last `max` entries of its history (8.44
    // compares them with the crashes). No search: the history is the agent's own record.
    public static IReadOnlyList<InstalledUpdate> History(int max) => Com.InStaThread(() =>
    {
        using var session = Dispatch.Create("Microsoft.Update.Session");
        session.Set("ClientApplicationID", "omawin");
        using var searcher = session.CallObject("CreateUpdateSearcher");
        return (IReadOnlyList<InstalledUpdate>)Installed(searcher, max);
    });

    static List<InstalledUpdate> Installed(Dispatch searcher, int max)
    {
        var installed = new List<InstalledUpdate>();
        int total = Convert.ToInt32(searcher.Call("GetTotalHistoryCount") ?? 0);
        if (total > 0)
            using (var history = searcher.CallObject("QueryHistory", 0, Math.Min(max, total)))
                foreach (var h in history.Items())
                    using (h)
                        // Operation 1 = installation, ResultCode 2 = succeeded. Date is UTC (check).
                        if (h.Get("Operation") is 1 && h.Get("ResultCode") is 2 && h.Get("Date") is DateTime when)
                            installed.Add(new InstalledUpdate(when, h.Get("Title") as string ?? ""));
        installed.Sort((a, b) => b.WhenUtc.CompareTo(a.WhenUtc));
        return installed;
    }

    static T Use<T>(Dispatch d, Func<Dispatch, T> read)
    {
        using (d) return read(d);
    }

    static DateTimeOffset? ScheduledRestart(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(RestartFile)) return null;
            var at = UpdatePlan.PausedUntil(File.ReadAllText(RestartFile).Trim(), now);
            if (at == null) File.Delete(RestartFile); // passed: the restart happened or was called off
            return at;
        }
        catch (IOException) { return null; }
    }

    // A restart at a time (shutdown.exe /r /t: no admin; Windows warns signed-in users before it).
    // Recorded so status can say so.
    public static string? ScheduleRestart(DateTime whenLocal, int seconds)
    {
        var failed = Shutdown($"/r /t {seconds} /d p:2:17 /c \"omawin: restart to finish installing updates\"");
        if (failed != null) return failed;
        Directory.CreateDirectory(Dir);
        File.WriteAllText(RestartFile, UpdatePlan.Stamp(new DateTimeOffset(whenLocal)));
        return null;
    }

    public static string? CancelRestart()
    {
        try { File.Delete(RestartFile); } catch (IOException) { }
        return Shutdown("/a");
    }

    // Windows' own shutdown.exe, by its full path. Exit 1116: nothing was scheduled to cancel.
    static string? Shutdown(string args)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
        if (p == null) return "shutdown.exe didn't start";
        var err = p.StandardError.ReadToEnd().Trim();
        p.WaitForExit();
        return p.ExitCode switch
        {
            0 => null,
            1116 => "no restart was set",
            1190 => "a restart is already set (omawin updates restart cancel first)",
            _ => err.Length > 0 ? err : $"shutdown.exe failed ({p.ExitCode})",
        };
    }
}
