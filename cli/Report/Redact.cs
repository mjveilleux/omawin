using System.Text.RegularExpressions;

namespace Omawin.Cli.Report;

// `omawin report` (docs/plan/09-for-everyone.md §7): no telemetry, so a report is text the user
// reads and chooses to post. Before it's shown, everything that identifies the PC, the person or
// their accounts is replaced.
public static partial class Redact
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")] private static partial Regex Email();
    // GitHub, OpenAI/Anthropic-style, Slack and AWS keys, and JWTs.
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|xox[abpr]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})")] private static partial Regex Token();
    // "password=…", "token: …", "Authorization: Bearer …": the value after the key.
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization)(\s*[:=]\s*)(bearer\s+)?(""[^""]*""|'[^']*'|\S+)")] private static partial Regex KeyValue();
    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")] private static partial Regex IPv4();

    // user and machine are this PC's names; home the profile folder; account the account folder
    // (docs/work.md), whose paths say who the person works for. Paths go first, so
    // C:\Users\ana\x becomes ~\x rather than C:\Users\<user>\x.
    public static string Text(string text, string user, string machine, string home, string? account = null)
    {
        // Below the account folder even the file names are private (a client's or employer's name).
        if (account is { Length: > 0 })
            foreach (var form in new[] { account.TrimEnd('\\', '/'), account.TrimEnd('\\', '/').Replace('\\', '/') })
                text = Regex.Replace(text, Regex.Escape(form) + @"(?:[\\/][^\s""'<>|]*)?", "<account>", RegexOptions.IgnoreCase);
        text = ReplacePath(text, home, "~");
        text = Token().Replace(text, "<token>");
        text = KeyValue().Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + "<secret>");
        text = Email().Replace(text, "<email>");
        text = IPv4().Replace(text, m => m.Value is "127.0.0.1" or "0.0.0.0" ? m.Value : "<ip>");
        if (user.Length >= 2) text = Regex.Replace(text, $@"(?<![A-Za-z0-9]){Regex.Escape(user)}(?![A-Za-z0-9])", "<user>", RegexOptions.IgnoreCase);
        if (machine.Length >= 2) text = Regex.Replace(text, $@"(?<![A-Za-z0-9]){Regex.Escape(machine)}(?![A-Za-z0-9])", "<pc>", RegexOptions.IgnoreCase);
        return text;
    }

    static string ReplacePath(string text, string path, string with)
    {
        var p = path.TrimEnd('\\', '/');
        return text.Replace(p, with, StringComparison.OrdinalIgnoreCase)
                   .Replace(p.Replace('\\', '/'), with, StringComparison.OrdinalIgnoreCase);
    }
}
