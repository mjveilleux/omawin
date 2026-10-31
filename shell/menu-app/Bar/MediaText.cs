namespace SystemMenu;

// What the bar's media widget says (roadmap 1.6): the song or video Windows' media controls know
// about, short enough for the bar. Pure C#: tested in menu-app.Tests (Look/MediaTextTests.cs).
public static class MediaText
{
    // The bar's words: the title first (it's what you look for; the artist is cut first), then
    // the artist unless the title already names it ("Artist - Song" from a video site). max: in
    // characters, an ellipsis at the end when cut.
    public static string Label(string? title, string? artist, string? app, int max = 36)
    {
        title = Clean(title);
        artist = Clean(artist);
        string text = title.Length > 0
            ? artist.Length > 0 && !title.Contains(artist, StringComparison.OrdinalIgnoreCase) ? $"{title} · {artist}" : title
            : artist.Length > 0 ? artist : AppName(app) ?? "";
        return Cut(text, max);
    }

    // The tooltip: everything, uncut, and what the clicks do.
    public static string Tip(string? title, string? artist, string? app, bool playing)
    {
        var lines = new List<string>();
        if (Clean(title) is { Length: > 0 } t) lines.Add(t);
        if (Clean(artist) is { Length: > 0 } a) lines.Add(a);
        var from = AppName(app);
        lines.Add((from != null ? from + " · " : "") + (playing ? "playing" : "paused"));
        lines.Add("Click: play/pause · right click: previous");
        return string.Join('\n', lines);
    }

    // The app playing it, from its id in Windows' media controls: a program's file name
    // ("Spotify.exe"), a Store app's AUMID ("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"), or a
    // browser's own name ("Chrome", "MSEdge"). Null for ids that name nothing readable (a hash).
    public static string? AppName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var name = id.Trim();
        if (name.LastIndexOf('!') is int bang and >= 0)
        {
            // A Store app: its entry's name, or its package's when that's the usual "App".
            var entry = name[(bang + 1)..];
            name = entry.Equals("App", StringComparison.OrdinalIgnoreCase) ? name[..bang].Split('_')[0] : entry;
        }
        if (name.LastIndexOfAny(['\\', '/']) is int slash and >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.LastIndexOf('.') is int dot and >= 0) name = name[(dot + 1)..]; // "SpotifyAB.SpotifyMusic"
        if (name.Length == 0 || name.Length >= 12 && name.All(Uri.IsHexDigit)) return null; // Firefox's "308046B0AF4A39CB"
        return name.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            _ => char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static string Cut(string s, int max)
    {
        if (max < 2 || s.Length <= max) return s;
        // Never in the middle of a surrogate pair (an emoji in a title).
        int at = max - 1;
        if (char.IsHighSurrogate(s[at - 1])) at--;
        return s[..at].TrimEnd(' ', '·', '-') + "…";
    }
}
