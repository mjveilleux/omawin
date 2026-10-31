using System.Text;
using System.Text.RegularExpressions;

namespace SystemMenu;

// The notification history (roadmap 8.22): every notification Windows showed, kept so you can
// search it, grouped by app, with verification codes and links picked out. A feed hands them in
// (INotificationFeed: Windows' UserNotificationListener, NotificationHistory.cs); this is the
// store and the reading of them. One file a month, %LOCALAPPDATA%\launcher\notifications\
// <yyyy-MM>.tsv, a line each: when, app, title, body, Windows' id; tabs, newlines and
// backslashes escaped. Months past the last six are deleted. Pure (files and text, no Windows
// calls), so it's tested anywhere (NoticeLogTests).
public sealed record Notice(DateTimeOffset When, string App, string Title, string Body, uint Id = 0)
{
    public string Text => Title.Length == 0 ? Body : Body.Length == 0 ? Title : Title + " " + Body;
    // The same notification seen twice (read at start, then told of) is one: its app, id and time.
    public string Key => $"{App}|{Id}|{When.UtcTicks}";
}

// Where notifications come from. The history doesn't care which: Windows' listener (with
// package identity and your consent) is the one there is.
public interface INotificationFeed
{
    // Starts handing notifications in; returns why it can't, or null when it runs.
    Task<string?> Start(Action<Notice> arrived);
}

public static class NoticeLog
{
    public const int KeepMonths = 6;

    public static string Line(Notice n) => string.Join('\t',
        n.When.ToString("o"), Escape(n.App), Escape(n.Title), Escape(n.Body), n.Id.ToString());

    public static Notice? Parse(string line)
    {
        var p = line.Split('\t');
        if (p.Length < 4 || !DateTimeOffset.TryParse(p[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)) return null;
        uint id = p.Length > 4 && uint.TryParse(p[4], out var i) ? i : 0;
        return new Notice(when, Unescape(p[1]), Unescape(p[2]), Unescape(p[3]), id);
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");

    static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 == s.Length) { b.Append(s[i]); continue; }
            char c = s[++i];
            b.Append(c switch { 't' => '\t', 'n' => '\n', _ => c });
        }
        return b.ToString();
    }

    public static string FileFor(string dir, DateTimeOffset when) => Path.Combine(dir, when.ToLocalTime().ToString("yyyy-MM") + ".tsv");

    public static void Append(string dir, Notice n)
    {
        Directory.CreateDirectory(dir);
        File.AppendAllText(FileFor(dir, n.When), Line(n) + "\n", Encoding.UTF8);
    }

    // The newest first, from the last `months` files.
    public static List<Notice> Load(string dir, int months = KeepMonths)
    {
        var list = new List<Notice>();
        foreach (var file in Months(dir).Take(months))
        {
            try
            {
                foreach (var line in File.ReadLines(file, Encoding.UTF8))
                    if (Parse(line) is { } n) list.Add(n);
            }
            catch (IOException) { }
        }
        return [.. list.OrderByDescending(n => n.When)];
    }

    // The month files, newest first (their names sort by date).
    static IEnumerable<string> Months(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.tsv").Where(f => Regex.IsMatch(Path.GetFileName(f), @"^\d{4}-\d{2}\.tsv$")).OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            : [];

    // Months past the newest `keep` deleted (only the history's own files).
    public static List<string> Trim(string dir, int keep = KeepMonths)
    {
        var old = Months(dir).Skip(keep).ToList();
        foreach (var f in old)
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return old;
    }

    // What's typed, matched against the app, title and body (every word somewhere, fzy-scored);
    // all of them, newest first, when nothing is typed.
    public static List<Notice> Search(IReadOnlyList<Notice> notices, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [.. notices];
        return [.. notices
            .Select(n => (n, score: Fuzzy.Match($"{n.App} {n.Text}".Replace('\n', ' '), query)))
            .Where(x => x.score != Fuzzy.None)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.n.When)
            .Select(x => x.n)];
    }

    // Apps by how many notifications they sent, most first: the page's groups.
    public static List<(string App, int Count, DateTimeOffset Latest)> Apps(IEnumerable<Notice> notices) =>
        [.. notices.GroupBy(n => n.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().App, g.Count(), g.Max(n => n.When)))
            .OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3)];

    // A verification code, as Omarchy's notifications spot them: 4 to 8 digits (or 3-3 split) in
    // a notification that says it's a code. Six digits are the usual length, so they win; a year
    // is never a code when something else could be.
    static readonly Regex CodeWord = new(@"\b(code|verification|verify|otp|one[- ]time|passcode|pin|security|2fa|login|sign[- ]in)\b", RegexOptions.IgnoreCase);
    static readonly Regex Digits = new(@"(?<![\d.,:/-])(\d{3}[- ]\d{3}|\d{4,8})(?!\d)(?![.,:/]\d)");

    public static string? Code(string text)
    {
        if (!CodeWord.IsMatch(text)) return null;
        var found = Digits.Matches(text).Select(m => m.Value.Replace("-", "").Replace(" ", "")).ToList();
        if (found.Count == 0) return null;
        var notYears = found.Where(d => !(d.Length == 4 && (d.StartsWith("19") || d.StartsWith("20")))).ToList();
        if (notYears.Count > 0) found = notYears;
        return found.FirstOrDefault(d => d.Length == 6) ?? found[0];
    }

    // Links in a notification, without the punctuation a sentence puts after them.
    static readonly Regex Link = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

    public static List<string> Links(string text) =>
        [.. Link.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']')).Distinct()];
}
