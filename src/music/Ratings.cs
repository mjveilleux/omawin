namespace OmawinMusic;

// Your ratings, 1 to 5 stars a track, in ~/Music/omawin/ratings.tsv: one line each (id, stars,
// when, length, artist, title), so it reads and edits as plain text. Not JSON: the library's
// loader and the bar's watcher take every .json there for a playlist.
//
//   5  love it        the station plays it, and what's like it, most
//   4  like it        the station plays it and what's like it
//   3  fine
//   2  not for me     the station rarely plays it
//   1  never again    skipped at once, and never played again, in any playlist
//
// A skip is not a rating. The station (Station.cs) is built from these.
public static class Ratings
{
    public static readonly string FilePath = Path.Combine(Library.Root, "ratings.tsv");

    public record Rating(string Id, int Stars, DateTime At, double Duration, string Artist, string Title);

    static readonly object gate = new();
    static Dictionary<string, Rating> cache = new();
    static DateTime cacheStamp = DateTime.MinValue;

    // A track's key: its YouTube id, else its link (a podcast episode, a stream).
    public static string Key(Track t) => t.Id != "" ? t.Id : t.Audio != "" ? t.Audio : t.Url;

    public static IReadOnlyDictionary<string, Rating> All()
    {
        lock (gate)
        {
            var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (stamp != cacheStamp)
            {
                cache = Read();
                cacheStamp = stamp;
            }
            return new Dictionary<string, Rating>(cache);
        }
    }

    public static int Stars(Track t) => All().TryGetValue(Key(t), out var r) ? r.Stars : 0;

    // stars 1-5 rates it; 0 takes the rating away.
    public static void Rate(Track t, int stars)
    {
        if (stars is < 0 or > 5) throw new Exception("a rating is 1 to 5 stars (0 takes it away)");
        lock (gate)
        {
            var all = All().ToDictionary(kv => kv.Key, kv => kv.Value);
            var key = Key(t);
            if (key == "") throw new Exception("nothing to rate");
            if (stars == 0) all.Remove(key);
            else all[key] = new Rating(key, stars, DateTime.Now, t.Duration, t.Artist, t.Title);
            Write(all.Values);
            cache = all;
            cacheStamp = File.GetLastWriteTimeUtc(FilePath);
        }
    }

    // How you rate an artist: the average of their rated tracks, and how many that is.
    public static Dictionary<string, (double Average, int Count)> Artists() =>
        All().Values.Where(r => r.Artist != "")
            .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.Average(r => r.Stars), g.Count()), StringComparer.OrdinalIgnoreCase);

    static Dictionary<string, Rating> Read()
    {
        var all = new Dictionary<string, Rating>();
        if (!File.Exists(FilePath)) return all;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            if (line.StartsWith('#') || line.Trim() == "") continue;
            var f = line.Split('\t');
            if (f.Length < 2 || !int.TryParse(f[1], out var stars) || stars is < 1 or > 5) continue; // a hand edit gone wrong: skipped
            DateTime.TryParse(f.ElementAtOrDefault(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at);
            double.TryParse(f.ElementAtOrDefault(3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var length);
            all[f[0]] = new Rating(f[0], stars, at, length, f.ElementAtOrDefault(4) ?? "", f.ElementAtOrDefault(5) ?? "");
        }
        return all;
    }

    // Written aside, then moved over the old file, so a crash never leaves half a file.
    static void Write(IEnumerable<Rating> all)
    {
        Directory.CreateDirectory(Library.Root);
        static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        var lines = new List<string> { "# id\tstars\twhen\tseconds\tartist\ttitle  (omawin music ratings: 5 love, 4 like, 3 fine, 2 not for me, 1 never)" };
        lines.AddRange(all.OrderByDescending(r => r.At).Select(r =>
            string.Join('\t', r.Id, r.Stars, r.At.ToString("s"), Math.Round(r.Duration).ToString(System.Globalization.CultureInfo.InvariantCulture), Clean(r.Artist), Clean(r.Title))));
        var temp = FilePath + ".new";
        File.WriteAllLines(temp, lines);
        File.Move(temp, FilePath, overwrite: true);
    }
}
