using System.Globalization;

namespace Omawin.Windows;

// Windows Update without surprises (roadmap 8.18): what's waiting, whether a restart is owed, the
// pause and the active hours, in sentences; and the checked values the changes write. Pure:
// Commands/UpdatesCommand.cs reads the registry and the Windows Update Agent, Admin/AdminScripts.cs
// writes through one prompt.
public sealed record PendingUpdate(string Title, IReadOnlyList<string> Kbs, bool Downloaded, bool NeedsRestart);
public sealed record InstalledUpdate(DateTime WhenUtc, string Title);

public sealed record UpdateState(
    bool RestartPending,
    DateTimeOffset? PausedUntil,
    int? ActiveStart, int? ActiveEnd, bool ActiveHoursAutomatic,
    DateTimeOffset? RestartScheduled,
    IReadOnlyList<PendingUpdate>? Pending,
    IReadOnlyList<InstalledUpdate>? Recent,
    string? AgentError);

public static class UpdatePlan
{
    // Where Settings › Windows Update keeps the pause and the active hours (HKLM, readable by every
    // account; written only through the admin prompt). (check: the value names on 24H2.)
    public const string UxKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    // The values Settings' "Pause updates" writes: the whole pause, and quality and feature
    // updates each, as UTC times. Resuming removes all six.
    public static readonly string[] PauseValueNames =
    [
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
    ];

    // Windows lets a pause run five weeks at most, in steps of a week in Settings; any whole
    // number of days up to that is accepted.
    public const int MaxPauseDays = 35;

    // Windows' limit on active hours: at most 18 of them (Settings refuses more).
    public const int MaxActiveHours = 18;

    public static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static IReadOnlyList<(string Name, string Value)> PauseValues(DateTimeOffset now, int days)
    {
        if (days is < 1 or > MaxPauseDays) throw new ArgumentOutOfRangeException(nameof(days), $"a pause is 1 to {MaxPauseDays} days");
        string start = Stamp(now), end = Stamp(now.AddDays(days));
        return
        [
            ("PauseUpdatesStartTime", start), ("PauseUpdatesExpiryTime", end),
            ("PauseQualityUpdatesStartTime", start), ("PauseQualityUpdatesEndTime", end),
            ("PauseFeatureUpdatesStartTime", start), ("PauseFeatureUpdatesEndTime", end),
        ];
    }

    // PauseUpdatesExpiryTime as Settings writes it ("2026-11-07T10:00:00Z"); null when absent,
    // unreadable, or already past (an expired pause is no pause).
    public static DateTimeOffset? PausedUntil(string? expiry, DateTimeOffset now) =>
        DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) && t > now ? t : null;

    // "9-18" (or "9:00-18:00"): active hours from 9 to 18. Null with why when it isn't.
    public static (int Start, int End)? ParseHours(string text, out string? why)
    {
        why = null;
        var parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Hour(parts[0], out var start) || !Hour(parts[1], out var end))
        {
            why = "active hours are two hours of the day, like 9-18";
            return null;
        }
        int span = (end - start + 24) % 24;
        if (span == 0) { why = "the start and the end are the same hour"; return null; }
        if (span > MaxActiveHours) { why = $"Windows allows at most {MaxActiveHours} active hours ({start}-{end} is {span})"; return null; }
        return (start, end);
    }

    static bool Hour(string s, out int hour)
    {
        if (s.EndsWith(":00", StringComparison.Ordinal)) s = s[..^3];
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out hour) && hour is >= 0 and <= 23;
    }

    // "21:30", "3", "03:00": a time of day for the restart. Null when it isn't one.
    public static TimeOnly? ParseTime(string text)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h is >= 0 and <= 23) return new TimeOnly(h, 0);
        return TimeOnly.TryParseExact(text, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    // When `at` next comes (today if it's still ahead by a minute, else tomorrow), as local time
    // and as the seconds shutdown.exe's /t takes.
    public static (DateTime When, int Seconds) Next(TimeOnly at, DateTime nowLocal)
    {
        var when = nowLocal.Date + at.ToTimeSpan();
        if (when <= nowLocal.AddMinutes(1)) when = when.AddDays(1);
        return (when, (int)Math.Ceiling((when - nowLocal).TotalSeconds));
    }

    // The default restart: 03:00, when nobody is using it, and outside any sane active hours.
    public static readonly TimeOnly Tonight = new(3, 0);

    public static IEnumerable<string> Report(UpdateState s, DateTimeOffset now, TimeZoneInfo zone)
    {
        string Local(DateTimeOffset t) => Day(TimeZoneInfo.ConvertTime(t, zone), TimeZoneInfo.ConvertTime(now, zone));

        if (s.RestartPending)
            yield return s.RestartScheduled is { } at && at > now
                ? $"A restart is owed for updates; it's set for {Local(at)} (omawin updates restart cancel to call it off)."
                : "A restart is owed for updates. omawin updates restart tonight sets it for 03:00 (or restart <time>).";
        else if (s.RestartScheduled is { } at && at > now)
            yield return $"A restart is set for {Local(at)} (omawin updates restart cancel to call it off).";
        else
            yield return "No restart is owed.";

        yield return s.PausedUntil is { } until
            ? $"Updates are paused until {Local(until)} (omawin updates resume ends it)."
            : "Updates aren't paused (omawin updates pause [days], a week unless said).";

        if (s.ActiveHoursAutomatic)
            yield return "Active hours: Windows picks them from when you use the PC (omawin updates hours 9-18 to set them).";
        else if (s.ActiveStart is { } a && s.ActiveEnd is { } b)
            yield return $"Active hours: {a:00}:00 to {b:00}:00; Windows doesn't restart itself then (omawin updates hours <start>-<end>).";

        if (s.AgentError != null)
        {
            yield return "";
            yield return $"What's waiting couldn't be read ({s.AgentError}).";
        }
        else if (s.Pending is { } pending)
        {
            yield return "";
            if (pending.Count == 0) yield return "Nothing is waiting to install (as of Windows Update's last check).";
            else
            {
                yield return $"Waiting to install ({pending.Count}, as of Windows Update's last check):";
                foreach (var u in pending)
                {
                    var notes = new List<string>();
                    if (u.Downloaded) notes.Add("downloaded");
                    if (u.NeedsRestart) notes.Add("needs a restart");
                    yield return $"  {Title(u)}" + (notes.Count > 0 ? $"  ({string.Join(", ", notes)})" : "");
                }
            }
        }
        if (s.Recent is { Count: > 0 } recent)
        {
            yield return "";
            yield return "Installed lately:";
            foreach (var u in recent.Take(8))
                yield return $"  {Local(new DateTimeOffset(DateTime.SpecifyKind(u.WhenUtc, DateTimeKind.Utc)))}  {u.Title}";
        }
    }

    // Windows Update's titles already end with the KB number in brackets; one without it gets them.
    static string Title(PendingUpdate u) =>
        u.Kbs.Count == 0 || u.Kbs.All(k => u.Title.Contains("KB" + k, StringComparison.OrdinalIgnoreCase))
            ? u.Title
            : $"{u.Title} (KB{string.Join(", KB", u.Kbs)})";

    // "today 03:00", "tomorrow 03:00", "Fri 7 Nov 10:00".
    static string Day(DateTimeOffset t, DateTimeOffset now)
    {
        var time = t.ToString("HH:mm", CultureInfo.InvariantCulture);
        int days = (t.Date - now.Date).Days;
        return days switch
        {
            0 => "today " + time,
            1 => "tomorrow " + time,
            _ => t.ToString("ddd d MMM ", CultureInfo.InvariantCulture) + time,
        };
    }
}
