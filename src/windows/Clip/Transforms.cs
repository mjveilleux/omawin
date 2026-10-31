using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Omawin.Windows;

// What `omawin clip <name>` does to the clipboard's text (roadmap 8.14): the developer's small
// conversions, without pasting into a website. Pure: test/windows.Tests checks every one. The
// clipboard page's Ctrl+K (plans/07-fixing-windows.md 8.14) takes the same list.
public sealed record Transform(string Name, string Says, Func<string, string?> Apply);

public static class Transforms
{
    public static readonly Transform[] All =
    [
        new("plain", "the text alone, its formatting dropped", s => s),
        new("json", "JSON, indented", s => Json(s, indented: true)),
        new("json-line", "JSON on one line", s => Json(s, indented: false)),
        new("base64", "Base64 encoded (UTF-8)", s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))),
        new("unbase64", "Base64 decoded (URL-safe Base64 too)", FromBase64),
        new("url", "URL encoded", Uri.EscapeDataString),
        new("unurl", "URL decoded (+ as a space)", s => Uri.UnescapeDataString(s.Replace('+', ' '))),
        new("date", "a Unix timestamp (seconds or milliseconds) as a date, local time", s => FromTimestamp(s, TimeZoneInfo.Local)),
        new("timestamp", "a date as a Unix timestamp (seconds)", ToTimestamp),
        new("trim", "each line's trailing spaces gone, and blank lines at the ends", Trim),
        new("lower", "lower case", s => s.ToLowerInvariant()),
        new("upper", "UPPER CASE", s => s.ToUpperInvariant()),
        new("path", @"a Windows path with / and a WSL path with \ (C:\a\b ⇄ /mnt/c/a/b)", SwapPath),
    ];

    public static Transform? Find(string name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // JsonDocument and Utf8JsonWriter: no serializer, nothing reflection-based. Comments and trailing
    // commas (as in a settings file) are accepted and dropped. Null when it isn't JSON.
    static string? Json(string s, bool indented)
    {
        try
        {
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                doc.WriteTo(w);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return null; }
    }

    // Standard or URL-safe (- and _), padded or not. Null when it isn't Base64 or isn't text.
    static string? FromBase64(string s)
    {
        var t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
        if (t.Length == 0) return null;
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        try
        {
            var bytes = Convert.FromBase64String(t);
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; } // not UTF-8: binary, which the clipboard's text can't hold
    }

    // 10 digits or fewer: seconds; 13: milliseconds (what JavaScript's Date.now() gives).
    public static string? FromTimestamp(string s, TimeZoneInfo zone)
    {
        var t = s.Trim();
        if (!long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return null;
        try
        {
            var at = t.TrimStart('-').Length >= 12 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
            return TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // ISO 8601 and the forms close to it; a date without an offset is taken as local time.
    static string? ToTimestamp(string s) =>
        DateTimeOffset.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)
            ? at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            : null;

    static string Trim(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join(s.Contains("\r\n") ? "\r\n" : "\n", lines);
    }

    // C:\Users\me\x ⇄ /mnt/c/Users/me/x, the way WSL mounts drives. Anything else: \ and / swapped.
    static string? SwapPath(string s)
    {
        var t = s.Trim().Trim('"');
        if (t.Length == 0) return null;
        if (t.Length >= 2 && char.IsAsciiLetter(t[0]) && t[1] == ':')
            return "/mnt/" + char.ToLowerInvariant(t[0]) + t[2..].Replace('\\', '/');
        if (t.StartsWith("/mnt/", StringComparison.Ordinal) && t.Length >= 6 && char.IsAsciiLetter(t[5]) && (t.Length == 6 || t[6] == '/'))
            return char.ToUpperInvariant(t[5]) + ":" + (t.Length == 6 ? "\\" : t[6..].Replace('/', '\\'));
        return t.Contains('\\') ? t.Replace('\\', '/') : t.Replace('/', '\\');
    }
}
