using System.Text.Json;

namespace SystemMenu;

// What omawin's viewer (roadmap 8.25, ViewerWindow.cs) does with a file: pictures drawn, text
// shown (JSON laid out, a log from its end), and how a file reaches it: `--view <path>` from
// omawin-view.exe, the forwarder Windows starts for a type you gave omawin (`omawin open-with`).
// Pure (no WinUI), so it's tested anywhere (ViewerFileTests).
public static class ViewerFile
{
    public enum Kind { Picture, Text, Other }

    public static readonly string[] Pictures = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "ico", "svg", "heic", "avif"];
    public static readonly string[] Texts = ["md", "markdown", "json", "jsonc", "log", "txt", "csv", "tsv", "yaml", "yml", "toml", "ini", "xml"];

    public static Kind KindOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Pictures.Contains(ext) ? Kind.Picture : Texts.Contains(ext) ? Kind.Text : Kind.Other;
    }

    // The menu app's requests are words split on spaces (App.HandleRequest), so a path travels
    // escaped: "C:\My Pictures\a b.png" as one word.
    public static string Encode(string path) => Uri.EscapeDataString(path);

    public static string? Decode(string word)
    {
        try
        {
            var path = Uri.UnescapeDataString(word);
            return path.Length > 0 && Path.IsPathFullyQualified(path) ? path : null;
        }
        catch (UriFormatException) { return null; }
    }

    // The pictures beside this one, in the folder's order as Explorer sorts by name (numbers by
    // value: 2.png before 10.png), for ← and →. The current one is always in the list.
    public static List<string> Siblings(IEnumerable<string> folderFiles, string current)
    {
        var list = folderFiles.Where(f => KindOf(f) == Kind.Picture).ToList();
        if (!list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Add(current);
        list.Sort((a, b) => Natural(Path.GetFileName(a), Path.GetFileName(b)));
        return list;
    }

    // The one `by` steps away, wrapping round; the same one when it's alone.
    public static string Step(List<string> siblings, string current, int by)
    {
        int at = siblings.FindIndex(s => s.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (at < 0 || siblings.Count == 0) return current;
        return siblings[((at + by) % siblings.Count + siblings.Count) % siblings.Count];
    }

    // Names compared as Explorer does: runs of digits by their value, the rest without case.
    public static int Natural(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var x = a.AsSpan(si, i - si).TrimStart('0');
                var y = b.AsSpan(sj, j - sj).TrimStart('0');
                if (x.Length != y.Length) return x.Length - y.Length;
                int c = x.SequenceCompareTo(y);
                if (c != 0) return c;
                continue;
            }
            int d = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (d != 0) return d;
            i++;
            j++;
        }
        return (a.Length - i) - (b.Length - j);
    }

    // At most this much text is shown: the start of a file, or a log's end (the latest lines).
    public const int MaxText = 1 << 20;

    // The text to show for a file's contents: JSON laid out with indents (as it is when it isn't
    // valid JSON), and a note when only part of a big file is shown.
    public static (string Text, string? Note) Shown(string path, string text, long fileLength)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        string? note = fileLength > MaxText
            ? ext == "log" ? $"The last {MaxText / 1024} KB of {Size(fileLength)}" : $"The first {MaxText / 1024} KB of {Size(fileLength)}"
            : null;
        if (ext is "json" or "jsonc" && fileLength <= MaxText) text = Indented(text) ?? text;
        return (text.Replace("\t", "    "), note);
    }

    // Written back with indents by JSON's own writer (no reflection: safe for a trimmed build).
    static string? Indented(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
                doc.RootElement.WriteTo(writer);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return null; }
    }

    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1 << 20 ? $"{bytes / 1024.0:F0} KB" : bytes < 1 << 30 ? $"{bytes / 1048576.0:F1} MB" : $"{bytes / 1073741824.0:F1} GB";
}
