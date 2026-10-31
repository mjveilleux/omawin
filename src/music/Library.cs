using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OmawinMusic;

public class Track
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = ""; // a page yt-dlp turns into a stream (YouTube)
    public string Audio { get; set; } = ""; // or the audio file itself (a podcast episode)
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Duration { get; set; }
    public string Thumb { get; set; } = "";
    public DateTime? Published { get; set; }
}

// A music playlist or a podcast: a title, where it came from, and its tracks (episodes).
public class Collection
{
    [JsonIgnore] public string Slug { get; set; } = "";
    [JsonIgnore] public string Kind { get; set; } = Library.Music;
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Image { get; set; } = "";
    public bool Renamed { get; set; } // named by you: a refresh keeps the name
    public DateTime Added { get; set; }
    public DateTime Updated { get; set; }
    public List<Track> Tracks { get; set; } = new();
}

// Everything is files in ~/Music/omawin, one JSON each: playlists at the top, podcasts in
// podcasts\, radio stations in radio\, Suno playlists in suno\. Only names and links: the audio is
// streamed. Adding the same link again (or refreshing) rewrites the file under the same name.
public static class Library
{
    public const string Music = "music", Podcasts = "podcasts", Radio = "radio", Suno = "suno";

    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "omawin");

    public static string Folder(string kind) => kind == Music ? Root : Path.Combine(Root, kind);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<Collection> All() => Load(Music).Concat(Load(Podcasts)).Concat(Load(Radio)).Concat(Load(Suno)).ToList();

    static IEnumerable<Collection> Load(string kind)
    {
        var folder = Folder(kind);
        if (!Directory.Exists(folder)) return Enumerable.Empty<Collection>();
        var list = new List<Collection>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var c = JsonSerializer.Deserialize<Collection>(File.ReadAllText(file), Json);
                if (c == null) continue;
                c.Slug = Path.GetFileNameWithoutExtension(file);
                c.Kind = kind;
                if (c.Image == "") c.Image = c.Tracks.FirstOrDefault()?.Thumb ?? "";
                list.Add(c);
            }
            catch { } // a broken file shouldn't hide the others
        }
        return list.OrderByDescending(c => c.Added);
    }

    public static Collection? Get(string slug) => All().FirstOrDefault(c => c.Slug == slug);

    public static void Remove(string slug)
    {
        var c = Get(slug);
        if (c != null) File.Delete(Path.Combine(Folder(c.Kind), slug + ".json"));
    }

    // Reads a link and saves what's in it. Music: a YouTube playlist, mix, or one long video
    // (yt-dlp, so SoundCloud and Bandcamp too). Podcasts: an RSS feed, an Apple Podcasts link,
    // or a YouTube playlist; anything else is refused with the reason.
    public static Collection Add(string kind, string url)
    {
        var c = kind == Podcasts ? Feed.Read(url) ?? PodcastPlaylist(url)
            : kind == Suno ? SunoLinks.Read(url) // its list; Suno's own page plays it (SunoHost.cs)
            : YouTube.Read(url, playlistOnly: false);
        c.Kind = kind;
        c.Tracks.RemoveAll(t => t.Url == "" && t.Audio == "");
        if (c.Tracks.Count == 0) throw new Exception("That link has nothing playable in it");
        if (kind == Podcasts)
        {
            c.Tracks = c.Tracks.OrderByDescending(t => t.Published ?? DateTime.MinValue).ToList();
            // A YouTube show lists no dates (its playlist is newest first): the newest episode's
            // day is looked up, so the shows can be ordered by their latest release.
            var newest = c.Tracks[0];
            if (newest.Published == null && newest.Url != "") newest.Published = YouTube.Date(newest.Url);
        }
        if (string.IsNullOrWhiteSpace(c.Title)) c.Title = "Untitled";
        if (c.Image == "") c.Image = c.Tracks[0].Thumb;
        c.Updated = DateTime.Now;

        var same = Load(kind).FirstOrDefault(x => x.SourceId != "" && x.SourceId == c.SourceId);
        if (same?.Renamed == true) (c.Title, c.Renamed) = (same.Title, true);
        c.Slug = same?.Slug ?? UniqueSlug(c.Title);
        c.Added = same?.Added ?? DateTime.Now;
        Save(c);
        return c;
    }

    // A radio station: one live stream, played from where it is (nothing to list, nothing to end).
    // Its name is yours, else the stream's own (icy-name: NTS, SomaFM and most icecast and
    // shoutcast servers send one), else its address. A web page is refused with the reason.
    public static Collection AddRadio(string url, string name)
    {
        url = url.Trim();
        if (!Regex.IsMatch(url, @"^https?://\S+$"))
            throw new Exception("A station is a stream link: omawin music add radio <url> [name]");
        var (icy, error) = Probe(url);
        if (error != null) throw new Exception(error);
        var same = Load(Radio).FirstOrDefault(x => x.SourceId == url);
        name = name.Trim();
        if (name == "")
        {
            name = Short(icy);
            if (name == "") name = new Uri(url).Host;
        }
        // Two channels of one station send the same icy-name: the second gets a number.
        if (same == null) name = UniqueStationName(name);
        var c = new Collection
        {
            Kind = Radio,
            Title = name,
            Source = url,
            SourceId = url,
            Tracks = { new Track { Id = url, Audio = url, Title = name } },
            Updated = DateTime.Now,
        };
        if (same?.Renamed == true) (c.Title, c.Renamed) = (same.Title, true);
        c.Slug = same?.Slug ?? UniqueSlug(c.Title);
        c.Added = same?.Added ?? DateTime.Now;
        Save(c);
        return c;
    }

    // A name no other station has: NTS Radio, then NTS Radio 2.
    static string UniqueStationName(string name)
    {
        var taken = Load(Radio).Select(c => c.Title).ToHashSet();
        var candidate = name;
        for (int i = 2; taken.Contains(candidate); i++) candidate = $"{name} {i}";
        return candidate;
    }

    // A stream's own name often carries its slogan after it ("Groove Salad: a nicely chilled
    // plate of ambient beats and grooves. [SomaFM]"): the station is what comes first.
    static string Short(string name)
    {
        name = Regex.Split(name, @"\s*[:|(\[]\s*")[0].Trim();
        return name.Length > 40 ? name[..40].TrimEnd() : name;
    }

    // What a stream says about itself before any sound: its content type (a station is audio, not
    // a page) and its icy-name. Reading the headers only leaves the stream unplayed.
    static (string name, string? error) Probe(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.TryAddWithoutValidation("User-Agent", "omawin-music");
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return ("", $"the stream said {(int)response.StatusCode} {response.ReasonPhrase}");
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (type.Contains("html") || type.Contains("json") || type.Contains("xml"))
                return ("", "That's a web page, not a stream: paste the station's stream link (an .mp3, .aac or .ogg address)");
            var name = Header(response, "icy-name") ?? Header(response, "icy-description") ?? "";
            return (name.Trim(), null);
        }
        catch (Exception ex) { return ("", "couldn't reach that stream: " + ex.Message); }
    }

    static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    // Your own name for it. The file keeps its name (the slug), so nothing else changes.
    public static Collection Rename(string slug, string title)
    {
        var c = Get(slug) ?? throw new Exception("no playlist " + slug);
        if (string.IsNullOrWhiteSpace(title)) throw new Exception("rename <slug> <name>");
        c.Title = title.Trim();
        c.Renamed = true;
        Save(c);
        return c;
    }

    static void Save(Collection c)
    {
        Directory.CreateDirectory(Folder(c.Kind));
        File.WriteAllText(Path.Combine(Folder(c.Kind), c.Slug + ".json"), JsonSerializer.Serialize(c, Json), new UTF8Encoding(false));
    }

    // Not a feed: a YouTube playlist is the one other thing a podcast can be.
    static Collection PodcastPlaylist(string url)
    {
        if (!Regex.IsMatch(url, @"youtube\.com|youtu\.be"))
            throw new Exception("That's not a podcast feed: paste its RSS feed, an Apple Podcasts link or a YouTube playlist");
        return YouTube.Read(url, playlistOnly: true);
    }

    // Read it again from where it came from: a podcast's new episodes, a playlist's changes.
    // A station is where it is: refreshing asks the stream for its name again, nothing more.
    public static Collection Refresh(string slug)
    {
        var c = Get(slug) ?? throw new Exception("no playlist " + slug);
        return c.Kind == Radio ? AddRadio(c.Source, "") : Add(c.Kind, c.Source);
    }

    // Slugs are unique across every folder, so commands only need the slug.
    static string UniqueSlug(string title)
    {
        var slug = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        if (slug == "") slug = "untitled";
        var taken = All().Select(c => c.Slug).ToHashSet();
        var name = slug;
        for (int i = 2; taken.Contains(name); i++) name = $"{slug}-{i}";
        return name;
    }
}
