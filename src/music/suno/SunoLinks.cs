using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmawinMusic;

// What's in a Suno link (suno.com/playlist/<id> or suno.com/song/<id>): its name and its songs,
// from the public JSON Suno's own pages load (studio-api.prod.suno.com/api/playlist/<id>/?page=N,
// /api/clip/<id>; no sign-in). Only the list: Suno's audio is encrypted, so it plays in Suno's own
// page (SunoHost.cs); each song keeps its page's link, which is what opening it plays.
public static class SunoLinks
{
    const string Api = "https://studio-api.prod.suno.com/api";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static SunoLinks() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-music/1.0");

    static readonly Regex Link = new(@"^https?://(?:www\.)?suno\.(?:com|ai)/(playlist|song)/([0-9a-f-]{36})", RegexOptions.IgnoreCase);

    public static bool Handles(string url) => Link.IsMatch(url.Trim());

    public static Collection Read(string url)
    {
        var m = Link.Match(url.Trim());
        if (!m.Success) throw new Exception("A Suno link is a playlist or song: suno.com/playlist/<id> or suno.com/song/<id>");
        string kind = m.Groups[1].Value.ToLowerInvariant(), id = m.Groups[2].Value.ToLowerInvariant();
        // The page's own address is what the Suno window opens.
        var c = new Collection { Source = $"https://suno.com/{kind}/{id}", SourceId = "suno:" + id };
        if (kind == "song")
        {
            using var song = Get($"{Api}/clip/{id}");
            var t = ToTrack(song.RootElement);
            c.Title = t.Title;
            c.Tracks.Add(t);
            return c;
        }
        // A playlist comes a page at a time (the first says how many there are in all).
        for (int page = 1; page <= 100; page++)
        {
            using var doc = Get($"{Api}/playlist/{id}/?page={page}");
            var root = doc.RootElement;
            if (page == 1)
            {
                c.Title = Str(root, "name");
                c.Image = Str(root, "image_url");
            }
            int before = c.Tracks.Count;
            if (root.TryGetProperty("playlist_clips", out var clips) && clips.ValueKind == JsonValueKind.Array)
                foreach (var entry in clips.EnumerateArray())
                    if (entry.TryGetProperty("clip", out var clip) && clip.ValueKind == JsonValueKind.Object)
                        c.Tracks.Add(ToTrack(clip));
            int total = root.TryGetProperty("num_total_results", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
            if (c.Tracks.Count == before || c.Tracks.Count >= total) break;
        }
        return c;
    }

    static JsonDocument Get(string url)
    {
        using var response = Http.GetAsync(url).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new Exception(response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "Suno doesn't show that one without signing in (sign in in the Suno window, then play it from there)"
                : $"Suno said {(int)response.StatusCode} {response.ReasonPhrase}");
        return JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    static Track ToTrack(JsonElement clip)
    {
        var id = Str(clip, "id");
        double duration = clip.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
        DateTime? made = DateTime.TryParse(Str(clip, "created_at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
        return new Track
        {
            Id = id,
            Url = "https://suno.com/song/" + id,
            Title = Str(clip, "title") is { Length: > 0 } title ? title : "Untitled",
            Artist = Str(clip, "display_name"),
            Duration = duration,
            Thumb = Str(clip, "image_url"),
            Published = made,
        };
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
