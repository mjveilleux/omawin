namespace OmawinMusic;

// Your station: an endless queue built from your ratings (Ratings.cs), never saved as a playlist.
//
// Seeds: your 4- and 5-star tracks (a 5 counts three times as often), or, before you've rated
// anything, the tracks of your music playlists. A seed's YouTube radio (watch?v=<id>&list=RD<id>:
// the "Start radio" YouTube Music has, read by yt-dlp; the first 60 of it, ~5 s) gives the
// candidates. Out go 1-star tracks, artists you average 1.5 stars or less on (over two tracks or
// more), what's played or queued lately, and the other kind of length: a songs station keeps
// tracks under 10 minutes, a jams station the longer ones (the hour-long mixes). What's left is
// ranked by how you rated it and its artist, with chance in it so it doesn't repeat itself; a
// 2-star track only comes up now and then.
public static class Station
{
    public const string Kind = "station", Songs = "songs", Jams = "jams";
    const double JamSeconds = 10 * 60;
    const int MixSize = 60;

    // A seed's radio, read once while the player runs.
    static readonly Dictionary<string, List<Track>> mixes = new();

    public static bool IsJam(Track t) => t.Duration >= JamSeconds;

    static bool Fits(Track t, string flavor) => t.Duration <= 0 || IsJam(t) == (flavor == Jams);

    // Songs or jams, when you don't say: the kind of the track playing now, else the kind most of
    // your 4- and 5-star tracks are, else songs.
    public static string Flavor(string? asked, Track? playing)
    {
        if (asked is Songs or Jams) return asked;
        if (playing != null && playing.Duration > 0) return IsJam(playing) ? Jams : Songs;
        var liked = Ratings.All().Values.Where(r => r.Stars >= 4 && r.Duration > 0).ToList();
        if (liked.Count > 0) return liked.Count(r => r.Duration >= JamSeconds) * 2 > liked.Count ? Jams : Songs;
        return Songs;
    }

    public static Collection Start(string flavor)
    {
        var c = new Collection
        {
            Kind = Kind,
            Slug = Kind,
            Title = flavor == Jams ? "Your station · jams" : "Your station · songs",
            Source = flavor,
        };
        c.Tracks.AddRange(More(flavor, new HashSet<string>(), 6));
        if (c.Tracks.Count == 0) throw new Exception("Nothing to build a station from yet: rate a few tracks 4 or 5 stars, or add a playlist");
        return c;
    }

    // The next `count` tracks, none of them in `recent` (played or already queued).
    public static List<Track> More(string flavor, ISet<string> recent, int count)
    {
        var ratings = Ratings.All();
        var artists = Ratings.Artists();
        var seeds = Seeds(flavor, ratings);
        if (seeds.Count == 0) return new();

        var pool = new Dictionary<string, Track>();
        var mine = new HashSet<string>(); // from your own playlists: ranked a little lower than new finds
        void Consider(Track t, bool own)
        {
            var key = Ratings.Key(t);
            if (key == "" || recent.Contains(key) || pool.ContainsKey(key) || !Fits(t, flavor)) return;
            if (ratings.TryGetValue(key, out var r) && r.Stars == 1) return;
            if (artists.TryGetValue(t.Artist, out var a) && a.Count >= 2 && a.Average <= 1.5) return;
            pool[key] = t;
            if (own) mine.Add(key);
        }
        // A few seeds' radios, so one station mixes several of the things you like. A seed's radio
        // is mostly songs, so a jams station may find few there: more seeds are tried then.
        for (int tries = 0; tries < 6 && pool.Count < count * 4; tries++)
            foreach (var t in Mix(Pick(seeds).Id)) Consider(t, own: false);
        // Your own playlists' tracks of that length too: the hour-long mixes live there.
        foreach (var t in Library.All().Where(c => c.Kind == Library.Music).SelectMany(c => c.Tracks)) Consider(t, own: true);

        return pool.Values
            .Select(t => (t, score: Score(t, ratings, artists) - (mine.Contains(Ratings.Key(t)) ? 0.3 : 0)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(count)
            .Select(x => x.t)
            .ToList();
    }

    // Seeds: your 4s and 5s that fit, as (id, weight); else your playlists' tracks.
    static List<(string Id, double Weight)> Seeds(string flavor, IReadOnlyDictionary<string, Ratings.Rating> ratings)
    {
        var rated = ratings.Values
            .Where(r => r.Stars >= 4 && IsYouTubeId(r.Id) && (r.Duration <= 0 || (r.Duration >= JamSeconds) == (flavor == Jams)))
            .Select(r => (r.Id, Weight: r.Stars == 5 ? 3.0 : 1.0))
            .ToList();
        if (rated.Count > 0) return rated;
        return Library.All().Where(c => c.Kind == Library.Music)
            .SelectMany(c => c.Tracks)
            .Where(t => IsYouTubeId(t.Id) && Fits(t, flavor) && (!ratings.TryGetValue(t.Id, out var r) || r.Stars >= 3))
            .Select(t => (t.Id, Weight: 1.0))
            .DistinctBy(s => s.Id)
            .ToList();
    }

    static (string Id, double Weight) Pick(List<(string Id, double Weight)> seeds)
    {
        double roll = Random.Shared.NextDouble() * seeds.Sum(s => s.Weight);
        foreach (var s in seeds)
            if ((roll -= s.Weight) <= 0) return s;
        return seeds[^1];
    }

    static double Score(Track t, IReadOnlyDictionary<string, Ratings.Rating> ratings, Dictionary<string, (double Average, int Count)> artists)
    {
        double score = 1 + Random.Shared.NextDouble(); // chance, so the order isn't the same each time
        if (ratings.TryGetValue(Ratings.Key(t), out var r))
            score += r.Stars switch { 5 => 2, 4 => 1, 2 => Random.Shared.NextDouble() < 0.1 ? 0 : -10, _ => 0 };
        if (artists.TryGetValue(t.Artist, out var a)) score += (a.Average - 3) * 0.6;
        return score;
    }

    static List<Track> Mix(string id)
    {
        lock (mixes)
            if (mixes.TryGetValue(id, out var known)) return known;
        List<Track> tracks;
        try { tracks = YouTube.Read($"https://www.youtube.com/watch?v={id}&list=RD{id}", false, MixSize).Tracks; }
        catch (Exception ex)
        {
            Program.Log(new Exception($"station: couldn't read the radio of {id}: {ex.Message}"));
            tracks = new();
        }
        lock (mixes) mixes[id] = tracks;
        return tracks;
    }

    static bool IsYouTubeId(string id) => id.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
