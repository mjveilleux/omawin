using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OmawinMusic;

// Podcasts: an RSS feed (what every podcast publishes), or an Apple Podcasts link, which
// Apple's lookup turns into the show's feed. Null when the link is neither, so a YouTube
// playlist can be tried instead. Episodes play straight from their audio files.
public static class Feed
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    static Feed() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-music/1.0");

    public static Collection? Read(string url)
    {
        if (Regex.IsMatch(url, @"open\.spotify\.com/(show|episode)"))
            throw new Exception("Spotify keeps its podcasts to itself: paste the show's Apple Podcasts link or RSS feed");
        var apple = Regex.Match(url, @"podcasts\.apple\.com/.*/id(\d+)");
        if (apple.Success) url = AppleFeed(apple.Groups[1].Value);
        else if (Regex.IsMatch(url, @"youtube\.com|youtu\.be")) return null;

        string text;
        try { text = Http.GetStringAsync(url).Result; }
        catch (Exception ex) { throw new Exception("Couldn't open that link: " + (ex.InnerException ?? ex).Message); }
        XDocument doc;
        try { doc = XDocument.Parse(text); }
        catch { return null; } // a web page, not a feed
        var channel = doc.Root?.Element("channel");
        if (channel == null) return null;

        var image = channel.Element(Itunes + "image")?.Attribute("href")?.Value ?? channel.Element("image")?.Element("url")?.Value ?? "";
        var author = channel.Element(Itunes + "author")?.Value ?? "";
        var c = new Collection { Title = channel.Element("title")?.Value.Trim() ?? "", Source = url, SourceId = url, Image = image };
        foreach (var item in channel.Elements("item"))
        {
            var enclosure = item.Element("enclosure");
            var audio = enclosure?.Attribute("url")?.Value ?? "";
            var type = enclosure?.Attribute("type")?.Value ?? "audio";
            if (audio == "" || !(type.StartsWith("audio") || type.StartsWith("video"))) continue;
            c.Tracks.Add(new Track
            {
                Id = item.Element("guid")?.Value ?? audio,
                Audio = audio,
                Title = item.Element("title")?.Value.Trim() ?? "",
                Artist = author,
                Duration = Seconds(item.Element(Itunes + "duration")?.Value),
                Thumb = item.Element(Itunes + "image")?.Attribute("href")?.Value ?? image,
                Published = DateTime.TryParse(item.Element("pubDate")?.Value, out var d) ? d : FixDate(item.Element("pubDate")?.Value),
            });
        }
        if (c.Tracks.Count == 0) throw new Exception("That feed has no episodes to play");
        return c;
    }

    // Apple's public lookup: the podcast's id -> its feed.
    static string AppleFeed(string id)
    {
        var json = Http.GetStringAsync("https://itunes.apple.com/lookup?id=" + id).Result;
        var m = Regex.Match(json, "\"feedUrl\"\\s*:\\s*\"([^\"]+)\"");
        if (!m.Success) throw new Exception("Apple doesn't list a feed for that podcast");
        return m.Groups[1].Value.Replace("\\/", "/");
    }

    // itunes:duration is "3725", "62:05" or "1:02:05".
    static double Seconds(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        double total = 0;
        foreach (var part in s.Trim().Split(':')) total = total * 60 + (double.TryParse(part, out var v) ? v : 0);
        return total;
    }

    // RSS dates are RFC 822 ("Tue, 14 Oct 2026 08:00:00 PDT"), whose zone names .NET doesn't read.
    static DateTime? FixDate(string? s)
    {
        if (s == null) return null;
        var plain = Regex.Replace(s, @"\s+[A-Z]{2,4}$", "");
        return DateTime.TryParse(plain, out var d) ? d : null;
    }
}
