using System.Globalization;

namespace Omawin.Windows;

// A process list that reads like Task Manager's Processes tab, in a terminal (roadmap 8.17): by app,
// each app's helpers counted toward it, sorted by what it costs. Pure: Snapshot.cs takes the
// processes from Windows; test/windows.Tests builds them by hand.

// One process at one moment. Created (a FILETIME) tells a reused pid from the old one; CpuTicks is
// user + kernel time in 100 ns ticks; PrivateBytes is its private working set (Task Manager's Memory).
public readonly record struct Proc(uint Pid, uint Parent, string Name, long Created, long CpuTicks, long PrivateBytes, uint Session);

public sealed record AppGroup(string Name, int Processes, long PrivateBytes, double Cpu, IReadOnlyList<Proc> Members);

public static class ProcessGroups
{
    // Programs that only ever run for another and should count toward it, not show as their own app.
    static readonly HashSet<string> Helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedgewebview2.exe", "crashpad_handler.exe", "conhost.exe", "OpenConsole.exe", "WerFault.exe",
        "CefSharp.BrowserSubprocess.exe", "QtWebEngineProcess.exe",
    };

    // What Windows itself runs, grouped as one line instead of a hundred.
    static readonly HashSet<string> Windows = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe",
        "svchost.exe", "fontdrvhost.exe", "dwm.exe", "Memory Compression", "spoolsv.exe", "WmiPrvSE.exe",
        "dllhost.exe", "sihost.exe", "taskhostw.exe", "RuntimeBroker.exe", "SearchIndexer.exe", "SearchHost.exe",
        "StartMenuExperienceHost.exe", "ShellExperienceHost.exe", "TextInputHost.exe", "ctfmon.exe", "audiodg.exe",
        "SecurityHealthService.exe", "MsMpEng.exe", "NisSrv.exe", "lsaiso.exe", "LsaIso.exe", "wlanext.exe",
        "dasHost.exe", "unsecapp.exe", "SgrmBroker.exe", "MpDefenderCoreService.exe", "WUDFHost.exe", "smartscreen.exe",
    };

    public const string WindowsGroup = "Windows";

    // Groups one snapshot, with CPU worked out against an earlier one taken `elapsedTicks` (100 ns)
    // before, on `cores` logical processors. "System Idle" is left out: its CPU is the free CPU.
    public static List<AppGroup> Group(IReadOnlyList<Proc> now, IReadOnlyList<Proc>? before, long elapsedTicks, int cores)
    {
        var byPid = now.ToDictionary(p => p.Pid);
        var earlier = before?.GroupBy(p => (p.Pid, p.Created)).ToDictionary(g => g.Key, g => g.First().CpuTicks);
        double Cpu(Proc p)
        {
            if (earlier == null || elapsedTicks <= 0 || cores <= 0) return 0;
            if (!earlier.TryGetValue((p.Pid, p.Created), out var was)) return 0; // started since: no rate yet
            return Math.Max(0, (p.CpuTicks - was) * 100.0 / (elapsedTicks * (double)cores));
        }

        var groups = new Dictionary<string, List<Proc>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in now)
        {
            if (p.Pid == 0) continue;
            var key = AppOf(p, byPid);
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
            members.Add(p);
        }
        return groups.Select(g => new AppGroup(g.Key, g.Value.Count, g.Value.Sum(p => p.PrivateBytes), g.Value.Sum(Cpu), g.Value))
            .OrderByDescending(g => g.Cpu).ThenByDescending(g => g.PrivateBytes).ToList();
    }

    // The app a process belongs to: Windows' own processes as one; a helper, or a process started by
    // one of the same name (a browser's tabs), climbs to its parent. A parent created after the child
    // isn't its parent (its pid was reused), so the climb stops there.
    public static string AppOf(Proc p, IReadOnlyDictionary<uint, Proc> byPid)
    {
        if (Windows.Contains(p.Name)) return WindowsGroup;
        var at = p;
        for (int depth = 0; depth < 32; depth++)
        {
            if (!byPid.TryGetValue(at.Parent, out var parent) || parent.Pid == at.Pid || parent.Created > at.Created) break;
            bool climb = Helpers.Contains(at.Name) || parent.Name.Equals(at.Name, StringComparison.OrdinalIgnoreCase);
            if (!climb || Windows.Contains(parent.Name) || parent.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) break;
            at = parent;
        }
        return Display(at.Name);
    }

    // "Code.exe" → "Code".
    public static string Display(string exe) => exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;

    // "1.2 GB", "340 MB", "12 KB".
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes >> 20).ToString(CultureInfo.InvariantCulture) + " MB",
        _ => (bytes >> 10).ToString(CultureInfo.InvariantCulture) + " KB",
    };

    // The groups whose app or any of whose processes match what was typed (part of a name).
    public static List<AppGroup> Filter(List<AppGroup> groups, string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? groups
        : groups.Where(g => g.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                            || g.Members.Any(m => m.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))).ToList();
}
