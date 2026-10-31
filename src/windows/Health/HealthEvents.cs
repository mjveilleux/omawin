using System.Globalization;
using System.Xml.Linq;

namespace Omawin.Windows;

// Why the PC restarted, crashed or woke up (roadmap 8.15): Windows writes every one of these to its
// event logs and shows none of them. This file turns the events into plain sentences. Pure: the
// events come in as the XML Windows renders them (EventLog.cs reads them), so test/windows.Tests
// feeds it real events' XML.

// One event: who wrote it, its number, when (UTC), and its data by name (EventData's <Data Name=…>)
// and in order (older providers leave the names out).
public sealed record LogEvent(string Provider, int Id, DateTimeOffset Time, IReadOnlyDictionary<string, string> Data, IReadOnlyList<string> Values);

// A line of the report: when, what happened, and how bad (Bad: a crash or a lost restart).
public sealed record Finding(DateTimeOffset Time, string Text, bool Bad);

public static class HealthEvents
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    // The providers, as the System log names them.
    public const string KernelPower = "Microsoft-Windows-Kernel-Power", BugCheck = "Microsoft-Windows-WER-SystemErrorReporting",
        User32 = "User32", KernelGeneral = "Microsoft-Windows-Kernel-General", PowerTroubleshooter = "Microsoft-Windows-Power-Troubleshooter",
        AppError = "Application Error", AppHang = "Application Hang", BootPerformance = "Microsoft-Windows-Diagnostics-Performance";

    // What `omawin health` asks for: (log, XPath). Each readable by a standard account except the
    // boot-performance log, which may need admin or the Event Log Readers group (it's then skipped
    // with a note). Days counted back from now.
    public static (string Log, string XPath)[] Queries(int days) =>
    [
        ("System", Q(KernelPower, "EventID=41", days)),            // came back without a clean shutdown
        ("System", Q(BugCheck, "EventID=1001", days)),             // a blue screen, with its code
        ("System", Q(User32, "EventID=1074", days)),               // who restarted or shut it down, and why
        ("System", Q(KernelGeneral, "EventID=12", days)),          // each start, to count them
        ("System", Q(PowerTroubleshooter, "EventID=1", days)),     // what woke it from sleep
        ("Application", Q(AppError, "EventID=1000", days)),        // app crashes
        ("Application", Q(AppHang, "EventID=1002", days)),         // apps that stopped responding and were closed
    ];

    public static (string Log, string XPath) BootQuery(int days) =>
        ("Microsoft-Windows-Diagnostics-Performance/Operational", $"*[System[(EventID>=100 and EventID<=110) and {Since(days)}]]");

    static string Q(string provider, string ids, int days) => $"*[System[Provider[@Name='{provider}'] and ({ids}) and {Since(days)}]]";

    // The last `days` days as an XPath time filter (milliseconds).
    public static string Since(int days) => $"TimeCreated[timediff(@SystemTime) <= {days * 86_400_000L}]";

    // One event as EvtRender(EvtRenderEventXml) gives it.
    public static LogEvent Parse(string xml)
    {
        var e = XElement.Parse(xml);
        var sys = e.Element(Ns + "System") ?? throw new FormatException("no <System>");
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        var eventData = e.Element(Ns + "EventData")?.Elements(Ns + "Data");
        // UserData (some providers) holds one element of the provider's own, its children the fields.
        var userData = e.Element(Ns + "UserData")?.Elements().FirstOrDefault()?.Elements();
        foreach (var d in eventData ?? userData ?? [])
        {
            values.Add(d.Value);
            if (d.Attribute("Name")?.Value is { } name) data[name] = d.Value;
            else if (d.Name.Namespace != Ns) data[d.Name.LocalName] = d.Value;
        }
        var provider = sys.Element(Ns + "Provider")?.Attribute("Name")?.Value ?? "";
        int id = int.TryParse(sys.Element(Ns + "EventID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var stamp = sys.Element(Ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
        var time = stamp != null && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t : DateTimeOffset.MinValue;
        return new LogEvent(provider, id, time, data, values);
    }

    // A sentence for one event; null for the ones only counted (each start).
    public static Finding? Explain(LogEvent e)
    {
        string? text = (e.Provider, e.Id) switch
        {
            (KernelPower, 41) when Num(e, "BugcheckCode") != 0 => $"Crashed (blue screen, stop code 0x{Num(e, "BugcheckCode"):X}) and restarted",
            (KernelPower, 41) when Num(e, "PowerButtonTimestamp") != 0 => "Was switched off by holding the power button",
            (KernelPower, 41) when Num(e, "SleepInProgress") != 0 => "Lost power or froze while going to sleep or asleep",
            (KernelPower, 41) => "Restarted without shutting down first (power lost, or it froze)",
            (BugCheck, 1001) => $"Blue screen: {StopCode(Get(e, "param1", 0))}" + (Get(e, "param2", 1) is { Length: > 0 } dump ? $" (dump: {dump})" : ""),
            (User32, 1074) => Shutdown(e),
            (PowerTroubleshooter, 1) => $"Woke from sleep: {WakeSource(Get(e, "WakeSourceText"))}",
            (AppError, 1000) => $"{AppName(Get(e, "AppName", 0))} crashed" + (Get(e, "ModuleName", 3) is { Length: > 0 } m && !SameApp(m, Get(e, "AppName", 0)) ? $" (in {m})" : ""),
            (AppHang, 1002) => $"{AppName(Get(e, "AppName", 0))} stopped responding and was closed",
            (BootPerformance, 100) => BootTime(e),
            (BootPerformance, >= 101 and <= 110) => SlowedBoot(e),
            _ => null,
        };
        if (text == null) return null;
        bool bad = e.Provider is KernelPower or BugCheck || e.Provider == AppError;
        return new Finding(e.Time, text, bad);
    }

    // "restart, by Windows Update: Operating System: Service pack (Planned)". Windows words param5
    // ("restart", "power off") and param3 in its own language, so they're shown as they are.
    static string Shutdown(LogEvent e)
    {
        var process = Get(e, "param1", 0);
        // "C:\Windows\system32\winlogon.exe (MYPC)": the computer's name follows in parentheses.
        int paren = process.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) process = process[..paren];
        var exe = WindowsPath.FileNameWithoutExtension(process);
        var who = exe.ToLowerInvariant() switch
        {
            "mousocoreworker" or "usoclient" or "trustedinstaller" or "wuauclt" or "musnotification" or "musnotificationux" => "Windows Update",
            "winlogon" or "startmenuexperiencehost" or "explorer" or "shellexperiencehost" => "you (Start, or Ctrl+Alt+Del)",
            "shutdown" => "the shutdown command",
            "" => "a program",
            _ => exe,
        };
        var user = Get(e, "param7", 6);
        var kind = Get(e, "param5", 4);
        var reason = Get(e, "param3", 2);
        var comment = Get(e, "param6", 5);
        var text = $"{(kind.Length > 0 ? Cap(kind) : "Shut down or restarted")}, by {who}";
        if (user.Length > 0 && who != "you (Start, or Ctrl+Alt+Del)" && !user.StartsWith("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)) text += $" (as {user})";
        if (reason.Length > 0) text += ": " + reason;
        if (comment.Length > 0) text += $" \"{comment}\"";
        return text;
    }

    // param1 of 1001: "0x0000009f (0x0000000000000003, …)". The code alone, and its name for the
    // common ones, so it can be searched.
    public static string StopCode(string param1)
    {
        var code = param1.Split(' ', 2)[0];
        if (!code.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !ulong.TryParse(code[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            return param1.Length > 0 ? param1 : "unknown stop code";
        var name = n switch
        {
            0x0A => "IRQL_NOT_LESS_OR_EQUAL",
            0x1A => "MEMORY_MANAGEMENT",
            0x3B => "SYSTEM_SERVICE_EXCEPTION",
            0x50 => "PAGE_FAULT_IN_NONPAGED_AREA",
            0x7E => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
            0x9F => "DRIVER_POWER_STATE_FAILURE",
            0xD1 => "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
            0xEF => "CRITICAL_PROCESS_DIED",
            0x116 => "VIDEO_TDR_FAILURE",
            0x124 => "WHEA_UNCORRECTABLE_ERROR",
            0x133 => "DPC_WATCHDOG_VIOLATION",
            0x139 => "KERNEL_SECURITY_CHECK_FAILURE",
            0x1E => "KMODE_EXCEPTION_NOT_HANDLED",
            0x154 => "UNEXPECTED_STORE_EXCEPTION",
            _ => null,
        };
        return name != null ? $"0x{n:X} {name}" : $"0x{n:X}";
    }

    // Windows writes "Device -USB Composite Device" or "Timer - Windows Update would like to restart".
    static string WakeSource(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return "unknown (often the network card or a timer; powercfg /lastwake may say)";
        int dash = t.IndexOf('-');
        if (dash > 0)
        {
            var kind = t[..dash].Trim();
            var what = t[(dash + 1)..].Trim();
            return kind.Equals("Timer", StringComparison.OrdinalIgnoreCase) ? $"a timer ({what})"
                 : kind.Equals("Device", StringComparison.OrdinalIgnoreCase) ? $"a device ({what})" : t;
        }
        return t;
    }

    static string BootTime(LogEvent e)
    {
        var ms = Num(e, "BootTime");
        if (ms == 0) return "Started";
        var main = Num(e, "MainPathBootTime");
        var text = $"Took {Seconds(ms)} to start";
        if (main > 0 && main < ms) text += $" ({Seconds(main)} to the desktop, the rest getting ready after sign-in)";
        return text;
    }

    // 101 an app, 102 a driver, 103 a service, 104-110 other causes: what slowed a start, by how much.
    static string SlowedBoot(LogEvent e)
    {
        var name = Get(e, "FriendlyName") is { Length: > 0 } f ? f : Get(e, "Name");
        var kind = e.Id switch { 101 => "the app", 102 => "the driver", 103 => "the service", _ => "" };
        var by = Num(e, "DegradationTime");
        var what = name.Length > 0 ? (kind.Length > 0 ? $"{kind} {name}" : name) : "something";
        return by > 0 ? $"Start slowed by {what}, {Seconds(by)} longer" : $"Start slowed by {what}";
    }

    static string Seconds(ulong ms) => (ms / 1000.0).ToString(ms >= 10_000 ? "0" : "0.0", CultureInfo.InvariantCulture) + " s";

    // "chrome.exe" → "chrome.exe" kept (people search by it), but never empty.
    static string AppName(string name) => name.Length > 0 ? name : "An app";

    static bool SameApp(string module, string app) => module.Equals(app, StringComparison.OrdinalIgnoreCase);

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // A named field, else the unnamed one at this position (older event layouts).
    public static string Get(LogEvent e, string name, int position = -1) =>
        e.Data.TryGetValue(name, out var v) ? v : position >= 0 && position < e.Values.Count ? e.Values[position] : "";

    // A number field, written in decimal or as 0x… hex.
    public static ulong Num(LogEvent e, string name)
    {
        var v = Get(e, name).Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : 0;
        return ulong.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    // The report: crashes and restarts newest first, app crashes counted per app (one line each,
    // the newest time), wakes and starts summarized. `now` and `zone` are passed in so tests are fixed.
    public static List<string> Report(IEnumerable<LogEvent> events, int days, DateTimeOffset now, TimeZoneInfo zone)
    {
        var all = events.ToList();
        var lines = new List<string>();
        string When(DateTimeOffset t) => Ago(t, now, zone);

        // A blue screen writes both a Kernel-Power 41 and a 1001 a minute or two apart: keep the 1001
        // (it names the stop code) and drop the 41 beside it.
        var bugchecks = all.Where(e => e.Provider == BugCheck && e.Id == 1001).Select(e => e.Time).ToList();
        var restarts = all.Where(e => e.Provider is KernelPower or BugCheck || e.Provider == User32)
            .Where(e => !(e.Provider == KernelPower && Num(e, "BugcheckCode") != 0 && bugchecks.Any(b => Math.Abs((b - e.Time).TotalMinutes) < 15)))
            .Select(Explain).OfType<Finding>().OrderByDescending(f => f.Time).ToList();
        int starts = all.Count(e => e.Provider == KernelGeneral && e.Id == 12);
        lines.Add($"Restarts and crashes, last {days} days ({starts} start{(starts == 1 ? "" : "s")} in all):");
        if (restarts.Count == 0) lines.Add("  none: every shutdown was a clean one");
        foreach (var f in restarts.Take(25)) lines.Add($"  {(f.Bad ? "!" : " ")} {When(f.Time),-18} {f.Text}");

        var crashes = all.Where(e => e.Provider is AppError or AppHang)
            .GroupBy(e => (Get(e, "AppName", 0).ToLowerInvariant(), e.Provider))
            .Select(g => (Newest: g.Max(e => e.Time), Count: g.Count(), Text: Explain(g.OrderByDescending(e => e.Time).First())!.Text))
            .OrderByDescending(c => c.Count).ThenByDescending(c => c.Newest).ToList();
        lines.Add("");
        lines.Add("Apps that crashed or hung:");
        if (crashes.Count == 0) lines.Add("  none");
        foreach (var c in crashes.Take(15))
            lines.Add($"    {When(c.Newest),-18} {c.Text}{(c.Count > 1 ? $", {c.Count} times" : "")}");

        var wakes = all.Where(e => e.Provider == PowerTroubleshooter && e.Id == 1).OrderByDescending(e => e.Time).ToList();
        lines.Add("");
        lines.Add("What woke it from sleep:");
        if (wakes.Count == 0) lines.Add("  nothing recorded");
        foreach (var w in wakes.Take(10)) lines.Add($"    {When(w.Time),-18} {Explain(w)!.Text["Woke from sleep: ".Length..]}");

        var boots = all.Where(e => e.Provider == BootPerformance).OrderByDescending(e => e.Time).ToList();
        if (boots.Count > 0)
        {
            lines.Add("");
            lines.Add("Slow starts:");
            foreach (var b in boots.Take(12)) lines.Add($"    {When(b.Time),-18} {Explain(b)?.Text}");
        }
        return lines;
    }

    // "today 08:12", "yesterday 23:40", "Mon 14:05" within the week, else "2026-10-12 09:30".
    public static string Ago(DateTimeOffset t, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(t, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today) return "today " + clock;
        if (local.Date == today.AddDays(-1)) return "yesterday " + clock;
        if (local.Date > today.AddDays(-7)) return local.ToString("ddd", CultureInfo.InvariantCulture) + " " + clock;
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
