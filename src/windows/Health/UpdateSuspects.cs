using System.Globalization;
using System.Text.RegularExpressions;

namespace Omawin.Windows;

// The update that broke it (roadmap 8.44): when crashes, blue screens or unexpected restarts start
// right after an update installed, `omawin health` says so, and `omawin updates uninstall <KB>` takes
// it off. Pure: the problems are 8.15's events (HealthEvents), the installs Windows Update's history
// (UpdateAgent), so test/windows.Tests feeds it both.

// One kind of problem that began after these updates (installed together), with how many there
// were in the two weeks before and since, and the first one after.
public sealed record Suspect(IReadOnlyList<InstalledUpdate> Updates, string Problem, int Before, int After, DateTimeOffset First);

public static partial class UpdateSuspects
{
    // The windows compared: two weeks before the install and two weeks after.
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);
    // How far back an install is looked at; the events must reach a window further back than that.
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(30);
    // Days of events `omawin health` reads so every install in the lookback has its whole window before it.
    public static int DaysNeeded => (int)(Lookback + Window).TotalDays;

    [GeneratedRegex(@"\bKB(\d{6,8})\b", RegexOptions.IgnoreCase)] private static partial Regex KbInTitle();

    // "2026-10 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5031234)" → "5031234".
    public static string? Kb(string title) => KbInTitle().Match(title) is { Success: true } m ? m.Groups[1].Value : null;

    // "KB5031234", "kb5031234" or "5031234" → "5031234"; null when it isn't a KB number.
    public static string? ParseKb(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return t.Length is >= 6 and <= 8 && t.All(char.IsAsciiDigit) ? t : null;
    }

    // Updates that arrive every day or carry no code to blame: Defender's definitions, the malicious
    // software removal tool. Left out, else the latest of them would always be the "cause".
    public static bool Ignored(string title) =>
        title.Contains("Security Intelligence Update", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Definition Update", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Malicious Software Removal Tool", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("KB2267602", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("KB890830", StringComparison.OrdinalIgnoreCase);

    // Each problem event's kind: blue screens (1001, or a Kernel-Power 41 with a stop code when no
    // 1001 sits beside it), unexpected restarts (41 without one), each app's crashes (1000). Null for
    // events that aren't problems.
    public static List<(DateTimeOffset Time, string Kind)> Problems(IEnumerable<LogEvent> events)
    {
        var all = events.ToList();
        var bugchecks = all.Where(e => e.Provider == HealthEvents.BugCheck && e.Id == 1001).Select(e => e.Time).ToList();
        var list = new List<(DateTimeOffset, string)>();
        foreach (var e in all)
        {
            string? kind = (e.Provider, e.Id) switch
            {
                (HealthEvents.BugCheck, 1001) => "blue screens",
                (HealthEvents.KernelPower, 41) when HealthEvents.Num(e, "BugcheckCode") != 0 =>
                    bugchecks.Any(b => Math.Abs((b - e.Time).TotalMinutes) < 15) ? null : "blue screens",
                (HealthEvents.KernelPower, 41) => "unexpected restarts",
                (HealthEvents.AppError, 1000) => $"{App(HealthEvents.Get(e, "AppName", 0))} crashes",
                _ => null,
            };
            if (kind != null) list.Add((e.Time, kind));
        }
        return list;
    }

    static string App(string name) => name.Length > 0 ? name : "app";

    // For each kind of problem, the latest update after which it began: at least 3 in the two weeks
    // since, at least 3 times as many as in the two weeks before (counting none as one), and the
    // first within 3 days of the install. `since` is where the events start: an install whose
    // window before reaches past it isn't judged, since its "before" would be undercounted.
    public static List<Suspect> Find(IEnumerable<InstalledUpdate> installs, IEnumerable<LogEvent> events, DateTimeOffset since, DateTimeOffset now)
    {
        var candidates = installs
            .Where(i => !Ignored(i.Title))
            .Select(i => (Update: i, When: new DateTimeOffset(DateTime.SpecifyKind(i.WhenUtc, DateTimeKind.Utc))))
            .Where(i => i.When > now - Lookback && i.When - Window >= since && i.When <= now)
            .OrderByDescending(i => i.When)
            .ToList();
        var suspects = new List<Suspect>();
        foreach (var kind in Problems(events).GroupBy(p => p.Kind))
        {
            var times = kind.Select(p => p.Time).Order().ToList();
            foreach (var (update, when) in candidates)
            {
                int before = times.Count(t => t >= when - Window && t < when);
                int after = times.Count(t => t >= when && t < when + Window);
                var first = times.FirstOrDefault(t => t >= when);
                if (after >= 3 && after >= 3 * Math.Max(before, 1) && first != default && first - when < TimeSpan.FromDays(3))
                {
                    // Updates install in batches (the month's cumulative update, .NET's, a driver):
                    // every one installed within the hour is named, since any of them may be it.
                    var batch = candidates.Where(c => Math.Abs((c.When - when).TotalHours) <= 1).Select(c => c.Update).ToList();
                    suspects.Add(new Suspect(batch, kind.Key, before, after, first));
                    break; // the latest one that fits
                }
            }
        }
        return suspects.OrderByDescending(s => s.After).ToList();
    }

    // "Blue screens started after KB5031234, installed Tue 14:05: 4 since, none in the two weeks
    // before." and what to do: one line per update in the batch.
    public static List<string> Report(IReadOnlyList<Suspect> suspects, DateTimeOffset now, TimeZoneInfo zone)
    {
        var lines = new List<string>();
        if (suspects.Count == 0) return lines;
        lines.Add("Started after an update:");
        foreach (var s in suspects)
        {
            var newest = s.Updates.MaxBy(u => u.WhenUtc)!;
            var installed = HealthEvents.Ago(new DateTimeOffset(DateTime.SpecifyKind(newest.WhenUtc, DateTimeKind.Utc)), now, zone);
            var names = string.Join(" and ", s.Updates.Select(u => Kb(u.Title) is { } kb ? "KB" + kb : u.Title));
            var before = s.Before == 0 ? "none" : s.Before.ToString(CultureInfo.InvariantCulture);
            lines.Add($"  ! {Cap(s.Problem)} started after {names}, installed {installed}: {s.After} since, {before} in the two weeks before.");
            foreach (var u in s.Updates)
                lines.Add(Kb(u.Title) is { } kb
                    ? $"      omawin updates uninstall KB{kb} takes it off ({Short(u.Title)}; asks first, pauses updates a week)"
                    : $"      {Short(u.Title)} has no KB number: a driver rolls back in Device Manager › the device › Driver › Roll Back Driver");
        }
        return lines;
    }

    // Titles run long ("2026-10 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB…)").
    static string Short(string title)
    {
        var t = KbInTitle().Replace(title, "").Replace("()", "").Trim();
        return t.Length <= 70 ? t : t[..67].TrimEnd() + "…";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
