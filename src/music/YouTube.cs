using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmawinMusic;

// Links yt-dlp understands (YouTube, and most music sites): what's in them, and the address to
// stream one video's audio from. yt-dlp is winget's yt-dlp.yt-dlp; for YouTube it runs JavaScript with Bun.
public static class YouTube
{
    // limit: only the first that many entries (a YouTube radio mix runs to a thousand and more,
    // and reading them all takes half a minute).
    public static Collection Read(string url, bool playlistOnly, int limit = 0)
    {
        // YouTube's podcast pages (youtube.com/show/VL<playlist>) are playlists underneath; read
        // as a page, yt-dlp finds no title or channel.
        url = Regex.Replace(url, @"youtube\.com/show/VL([\w-]+).*", "youtube.com/playlist?list=$1");
        var (code, output, error) = limit > 0
            ? YtDlp.Run("--flat-playlist", "--playlist-end", limit.ToString(), "-J", url)
            : YtDlp.Run("--flat-playlist", "-J", url);
        if (code != 0 || !output.TrimStart().StartsWith('{'))
            throw new Exception(YtDlp.Reason(error) ?? "That link couldn't be read");
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var c = new Collection { Source = url, SourceId = Str(root, "id") };
        if (Str(root, "_type") == "playlist")
        {
            c.Title = Str(root, "title");
            // A generic name ("Videos", "Uploads from ...", "show"): the channel's is better.
            var channel = Str(root, "channel") is { Length: > 0 } ch ? ch : Str(root, "uploader");
            if (channel != "" && Regex.IsMatch(c.Title, @"^(|show|videos|shorts|podcasts?|uploads from .*|.* - videos)$", RegexOptions.IgnoreCase))
                c.Title = channel;
            foreach (var e in root.GetProperty("entries").EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var t = ToTrack(e);
                // Private and deleted videos stay listed, but can't be played.
                if (t.Title is "[Private video]" or "[Deleted video]") continue;
                c.Tracks.Add(t);
            }
        }
        else
        {
            if (playlistOnly) throw new Exception("That's one video, not a podcast: paste its RSS feed, an Apple Podcasts link or a YouTube playlist");
            var t = ToTrack(root);
            if (t.Url == "") t.Url = url;
            c.Title = t.Title;
            c.Tracks.Add(t);
        }
        return c;
    }

    // The address to stream a video's audio from: AAC (m4a) first, which Windows plays itself.
    // Good for a few hours (YouTube's links expire).
    public static string StreamUrl(string url)
    {
        var (code, output, error) = YtDlp.Run("-f", "bestaudio[ext=m4a]/bestaudio/best", "-g", "--no-playlist", url);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(l => l.StartsWith("http"));
        if (code != 0 || line == null) throw new Exception(YtDlp.Reason(error) ?? "no stream for " + url);
        return line;
    }

    // The day a video came out (one more yt-dlp run, ~3 s); null when it can't tell.
    public static DateTime? Date(string url)
    {
        var (code, output, _) = YtDlp.Run("--skip-download", "--no-playlist", "--print", "upload_date", url);
        return code == 0 && DateTime.TryParseExact(output.Trim(), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }

    static Track ToTrack(JsonElement e)
    {
        var id = Str(e, "id");
        var url = Str(e, "webpage_url");
        if (url == "") url = Str(e, "url");
        bool youtube = Str(e, "ie_key") == "Youtube" || Str(e, "extractor_key") == "Youtube" || url.Contains("youtube.com") || url.Contains("youtu.be");
        if (youtube && id != "") url = "https://www.youtube.com/watch?v=" + id;
        // YouTube's picture by id (its 4:3 copy, letterboxed; the bar crops it). Otherwise the
        // largest picture the site lists.
        var thumb = youtube && id != "" ? $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" : "";
        if (thumb == "" && e.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0)
            thumb = Str(thumbs[thumbs.GetArrayLength() - 1], "url");
        if (thumb == "") thumb = Str(e, "thumbnail");
        var artist = Str(e, "artist");
        if (artist == "") artist = Str(e, "channel");
        if (artist == "") artist = Str(e, "uploader");
        var date = Str(e, "upload_date"); // yyyyMMdd, when yt-dlp knows it
        return new Track
        {
            Id = id,
            Url = url,
            Title = Str(e, "title"),
            Artist = artist,
            Duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
            Thumb = thumb,
            Published = DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var p) ? p : null,
        };
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

// Runs yt-dlp with the PATH from the registry, since whoever started this (the bar) may hold one
// from before yt-dlp was installed.
public static class YtDlp
{
    static string? exe;

    public static void FreshPath()
    {
        var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) + ";" +
                   Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("Path", Environment.ExpandEnvironmentVariables(path));
    }

    public static string Exe()
    {
        if (exe != null) return exe;
        var links = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links");
        foreach (var dir in (Environment.GetEnvironmentVariable("Path") ?? "").Split(';').Prepend(links))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "yt-dlp.exe");
                if (dir.Trim() != "" && File.Exists(candidate)) return exe = candidate;
            }
            catch { }
        }
        throw new Exception("yt-dlp isn't installed (winget install yt-dlp.yt-dlp)");
    }

    // bun.exe: on the PATH, in ~/.bun (its own installer), or where winget unpacked it (winget's
    // package doesn't always put it on the PATH).
    static string? bun;
    public static string? Bun()
    {
        if (bun != null) return bun == "" ? null : bun;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var places = (Environment.GetEnvironmentVariable("Path") ?? "").Split(';')
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bun", "bin"))
            .Append(Path.Combine(local, "Microsoft", "WinGet", "Links"));
        bun = places.Select(d => { try { return Path.Combine(d.Trim(), "bun.exe"); } catch { return ""; } }).FirstOrDefault(File.Exists) ?? "";
        if (bun == "")
        {
            try
            {
                bun = Directory.GetDirectories(Path.Combine(local, "Microsoft", "WinGet", "Packages"), "Oven-sh.Bun*")
                    .SelectMany(d => Directory.GetFiles(d, "bun.exe", SearchOption.AllDirectories)).FirstOrDefault() ?? "";
            }
            catch { }
        }
        return bun == "" ? null : bun;
    }

    public static (int code, string output, string error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Exe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.ArgumentList.Add("--encoding");
        info.ArgumentList.Add("utf-8");
        // YouTube needs a JavaScript runtime: Bun (omawin's), named outright, Deno switched off.
        if (Bun() is { } bun)
        {
            info.ArgumentList.Add("--no-js-runtimes");
            info.ArgumentList.Add("--js-runtimes");
            info.ArgumentList.Add("bun:" + bun);
        }
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var error = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEndAsync();
        // A stuck yt-dlp (a hung network read) would otherwise hold its caller forever.
        if (!p.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new Exception("yt-dlp took over a minute; stopped it");
        }
        p.WaitForExit(); // the output is read to its end
        return (p.ExitCode, output.Result, error.Result);
    }

    // yt-dlp's last "ERROR: ..." line, without its prefixes: why a link failed.
    public static string? Reason(string error)
    {
        var line = error.Split('\n').LastOrDefault(l => l.StartsWith("ERROR:"));
        if (line == null) return null;
        line = Regex.Replace(line["ERROR:".Length..].Trim(), @"^\[[^\]]+\]\s*[\w-]+:\s*", "");
        return line.Length > 120 ? line[..120] + "…" : line;
    }
}
