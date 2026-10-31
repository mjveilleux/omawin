namespace SystemMenu;

// The two files install.ps1 leaves for the bar (roadmap 2.2; install/ui.ps1's Start-InstallMark
// and Stop-InstallMark), in %LOCALAPPDATA%\launcher: install-running.txt while an install or
// update runs (its process and log), install-done.txt when it ends (how it went). "key<TAB>value"
// lines. Pure: tested in menu-app.Tests (Look/InstallMarkTests.cs); Bar/Installing.cs watches them.
public static class InstallMark
{
    public const string RunningFile = "install-running.txt", DoneFile = "install-done.txt";

    public sealed record Running(string Kind, int Pid, string Log);
    public sealed record Done(string Kind, string Result, int Seconds, string FailedStep, int Warnings);

    static Dictionary<string, string> Fields(IEnumerable<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var tab = line.IndexOf('\t');
            if (tab > 0) d[line[..tab].Trim()] = line[(tab + 1)..].Trim();
        }
        return d;
    }

    public static Running? ParseRunning(IEnumerable<string> lines)
    {
        var f = Fields(lines);
        if (!f.TryGetValue("pid", out var p) || !int.TryParse(p, out int pid) || pid <= 0) return null;
        return new Running(f.GetValueOrDefault("kind") is { Length: > 0 } k ? k : "install", pid, f.GetValueOrDefault("log", ""));
    }

    public static Done? ParseDone(IEnumerable<string> lines)
    {
        var f = Fields(lines);
        if (!f.TryGetValue("result", out var result) || result.Length == 0) return null;
        int.TryParse(f.GetValueOrDefault("seconds"), out int seconds);
        int.TryParse(f.GetValueOrDefault("warnings"), out int warnings);
        return new Done(f.GetValueOrDefault("kind") is { Length: > 0 } k ? k : "install", result, seconds, f.GetValueOrDefault("failed", ""), warnings);
    }

    // The bar's glyph's tooltip.
    public static string Tip(Running r) => $"omawin {(r.Kind == "update" ? "is updating" : "is installing")} in the background: click for its log";

    // The toast when it ends.
    public static string Toast(Done d)
    {
        string what = d.Kind == "update" ? "omawin's update" : "omawin's install";
        string took = d.Seconds >= 60 ? $"{d.Seconds / 60} min {d.Seconds % 60:00} s" : $"{d.Seconds} s";
        return d.Result switch
        {
            "failed" => $"{what} stopped{(d.FailedStep.Length > 0 ? " at " + d.FailedStep : "")}: its window says why",
            "warnings" => $"{what} is done in {took}, with {d.Warnings} warning{(d.Warnings == 1 ? "" : "s")}",
            _ => $"{what} is done in {took}",
        };
    }

    // An end only says something when it's new (a file touched long after, by a copy or a sync,
    // says nothing).
    public static bool Fresh(DateTime written, DateTime now) => now - written < TimeSpan.FromMinutes(1) && written - now < TimeSpan.FromMinutes(1);
}
