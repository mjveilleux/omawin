using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using SystemMenu;

// dotnet run --project src/community-themes -- <out dir> [--page <saved html>] [--limit <n>]
//
// The community theme index (roadmap 1.10), for the theme picker's Community chip: every theme
// omarchy.org/themes lists, plus themes/community-extra.txt, each read at its default branch's
// current commit, which the index pins (`omawin theme add` installs that commit). For each: the
// repo's facts (GitHub's API: owner, licence, stars, last push), its palette (colors.toml, or
// alacritty.toml for older themes, read and filled in as convert-theme.ps1 does: Community.cs),
// whether it's light (light.mode), its backgrounds' names, and a 320-pixel thumbnail of the
// screenshot omarchy.org hosts (else the repo's preview.png, else its first background). Nothing
// is cloned and nothing a theme holds runs: only data is read.
//
// Writes <out dir>/index/ (index.json, thumbs/<name>.jpg) and, packed, community-themes.zip and
// community-themes.zip.sha256, which .github/workflows/community-themes.yml publishes and the
// menu app fetches (CommunityThemes.cs). GITHUB_TOKEN, when set, lifts the API's rate limit
// (~3 requests a theme); when the API refuses anyway (its limit spent, or it's out of reach), a
// theme is read without it (ReadWithoutApi): its commit from git, its files straight from
// raw.githubusercontent.com, no stars, licence or list of backgrounds. --page reads a saved copy
// of the page; --limit reads only the first n.
var positional = new List<string>();
string? pageFile = null;
int limit = int.MaxValue;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--page" && i + 1 < args.Length) pageFile = args[++i];
    else if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out var n)) limit = n;
    else positional.Add(args[i]);
}
if (positional.Count != 1) { Console.Error.WriteLine("usage: <out dir> [--page <saved html>] [--limit <n>]"); return 2; }
var outDir = Path.GetFullPath(positional[0]);
var indexDir = Path.Combine(outDir, "index");
var thumbsDir = Path.Combine(indexDir, "thumbs");
if (Directory.Exists(indexDir)) Directory.Delete(indexDir, true); // a fresh index: no thumbnails of themes gone since
Directory.CreateDirectory(thumbsDir);
var repoRoot = FindRepo();

using var web = Http.Client();
using var api = Http.Client();
api.BaseAddress = new Uri("https://api.github.com/");
api.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
    api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

// --- the list ---------------------------------------------------------------------------------
var html = pageFile != null ? await File.ReadAllTextAsync(pageFile) : await Http.Text(web, CommunityList.Page);
var listed = CommunityList.Parse(html);
Console.WriteLine($"  omarchy.org/themes: {listed.Count} themes");
// The page is the source: one that suddenly lists next to nothing changed shape, and publishing
// an empty index would empty every PC's Community chip.
if (listed.Count < 20) { Console.Error.WriteLine("  ! omarchy.org/themes lists fewer than 20 themes: has the page changed? (CommunityList.Card)"); return 1; }
var extraFile = Path.Combine(repoRoot, "themes", "community-extra.txt");
var extra = File.Exists(extraFile) ? CommunityList.Extra(File.ReadAllLines(extraFile)) : [];
var candidates = listed.Select(t => (Title: (string?)t.Title, t.Repo, Screenshot: (string?)t.Screenshot))
    .Concat(extra.Select(r => (Title: (string?)null, Repo: r, Screenshot: (string?)null)))
    .DistinctBy(t => t.Repo, StringComparer.OrdinalIgnoreCase)
    .Take(limit).ToList();
Console.WriteLine($"  {candidates.Count} repos ({extra.Count} from community-extra.txt)");

// --- each theme, at a pinned commit -----------------------------------------------------------
var read = new List<CommunityTheme>();
var apiRefused = false;
var skipped = new List<string>();
await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (c, ct) =>
{
    try
    {
        CommunityTheme theme;
        if (apiRefused) theme = await ReadWithoutApi(c.Repo, c.Title, c.Screenshot, ct);
        else
        {
            try { theme = await Read(c.Repo, c.Title, c.Screenshot, ct); }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                if (!apiRefused) Console.WriteLine($"  GitHub's API refused ({ex.Message}): reading the rest without it");
                apiRefused = true;
                theme = await ReadWithoutApi(c.Repo, c.Title, c.Screenshot, ct);
            }
        }
        lock (read) read.Add(theme);
    }
    catch (Exception ex) { lock (skipped) skipped.Add($"{c.Repo}: {ex.Message}"); }
});
// Two repos giving one name (a fork, a rename): the one with more stars, as only one can be
// installed under it.
var themes = CommunityList.Ordered(read).DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
foreach (var dropped in read.Except(themes)) skipped.Add($"{dropped.Repo}: the name '{dropped.Name}' is taken by one with more stars");

// --- thumbnails -------------------------------------------------------------------------------
long thumbBytes = 0;
await Parallel.ForEachAsync(themes, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (t, ct) =>
{
    if (t.Screenshot.Length == 0) return; // the picker shows its colours
    try
    {
        var jpeg = Thumb.Make(await Http.Bytes(web, t.Screenshot, ct), t.Palette["background"])
            ?? throw new InvalidDataException("not an image Skia can read");
        await File.WriteAllBytesAsync(Path.Combine(thumbsDir, t.Name + ".jpg"), jpeg, ct);
        t.Thumb = $"thumbs/{t.Name}.jpg";
        Interlocked.Add(ref thumbBytes, jpeg.Length);
    }
    catch (Exception ex) { lock (skipped) skipped.Add($"{t.Repo}: no thumbnail ({ex.Message})"); }
});

// --- written and packed -----------------------------------------------------------------------
var index = new CommunityIndex { Generated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), Themes = themes };
var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
await File.WriteAllTextAsync(Path.Combine(indexDir, "index.json"), json);
var zip = Path.Combine(outDir, "community-themes.zip");
File.Delete(zip);
ZipFile.CreateFromDirectory(indexDir, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip))).ToLowerInvariant();
await File.WriteAllTextAsync(zip + ".sha256", $"{hash}  community-themes.zip\n");

Console.WriteLine($"  {themes.Count} themes ({themes.Count(t => t.Mode == "light")} light), {themes.Count(t => t.Thumb.Length > 0)} thumbnails ({thumbBytes / 1024.0 / 1024.0:0.0} MB)");
Console.WriteLine($"  community-themes.zip {new FileInfo(zip).Length / 1024.0 / 1024.0:0.0} MB, sha256 {hash}");
foreach (var s in skipped.Order()) Console.WriteLine($"  - {s}");
// Some themes always fail (a repo deleted, one with no palette); most failing means GitHub or
// the network did, and the last good index stays published.
if (themes.Count < candidates.Count / 2) { Console.Error.WriteLine("  ! fewer than half the themes were read"); return 1; }
return 0;

// One theme: its facts, its default branch's commit, and at that commit its palette, light.mode,
// backgrounds and preview.
async Task<CommunityTheme> Read(string slug, string? title, string? screenshot, CancellationToken ct)
{
    var repo = await Http.Json(api, $"repos/{slug}", ct);
    if (repo["archived"]?.GetValue<bool>() == true) throw new InvalidDataException("archived");
    var fullName = repo["full_name"]!.GetValue<string>(); // where it lives now, after a rename
    var branch = repo["default_branch"]!.GetValue<string>();
    var commit = await Http.Json(api, $"repos/{fullName}/commits/{Uri.EscapeDataString(branch)}", ct);
    var sha = commit["sha"]!.GetValue<string>();
    var tree = await Http.Json(api, $"repos/{fullName}/git/trees/{commit["commit"]!["tree"]!["sha"]!.GetValue<string>()}?recursive=1", ct);
    var files = tree["tree"]!.AsArray().Where(e => e?["type"]?.GetValue<string>() == "blob")
        .Select(e => e!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    string Raw(string path) => $"https://raw.githubusercontent.com/{fullName}/{sha}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    Dictionary<string, string>? colors =
        files.Contains("colors.toml") ? ThemePalette.FromColorsToml(await Http.Text(web, Raw("colors.toml"), ct))
        : files.Contains("alacritty.toml") ? ThemePalette.FromAlacritty(await Http.Text(web, Raw("alacritty.toml"), ct))
        : throw new InvalidDataException("no colors.toml or alacritty.toml");
    if (colors == null || colors.Count == 0) throw new InvalidDataException("no colors in its palette");
    var palette = ThemePalette.Kept(ThemePalette.Resolve(colors, files.Contains("light.mode")));

    var name = CommunityList.ThemeName(fullName) ?? throw new InvalidDataException("its repo gives no usable theme name");
    // Its wallpapers as convert-theme.ps1 picks them from a clone: backgrounds/'s pictures, by name.
    var backgrounds = files.Where(f => f.StartsWith("backgrounds/") && f.Count(ch => ch == '/') == 1
            && Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif"
            && Path.GetFileName(f) != "omarchy.webp")
        .Order(StringComparer.OrdinalIgnoreCase).Select(Raw).ToArray();
    var license = repo["license"]?["spdx_id"]?.GetValue<string>() ?? "";
    return new CommunityTheme
    {
        Name = name,
        Title = title ?? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' ').Replace('_', ' ')),
        Repo = fullName, Commit = sha,
        Author = repo["owner"]?["login"]?.GetValue<string>() ?? fullName.Split('/')[0],
        License = license == "NOASSERTION" ? "" : license,
        Stars = repo["stargazers_count"]?.GetValue<int>() ?? 0,
        Updated = repo["pushed_at"]?.GetValue<string>() ?? "",
        Mode = palette["mode"], Palette = palette, Backgrounds = backgrounds,
        // omarchy.org's screenshot (the whole desktop in that theme), else what the author chose to
        // show (preview.png, Omarchy's own switcher's picture), else its first wallpaper.
        Screenshot = screenshot ?? (files.Contains("preview.png") ? Raw("preview.png") : backgrounds.FirstOrDefault() ?? ""),
    };
}

// One theme without GitHub's API: its default branch's commit from git (ls-remote), and at that
// commit its palette, light.mode and preview.png asked for by name. Owner from the link; stars,
// licence and backgrounds unknown (the picker sorts it last; installing still brings its
// backgrounds, from the clone).
async Task<CommunityTheme> ReadWithoutApi(string slug, string? title, string? screenshot, CancellationToken ct)
{
    var sha = await Git.Head($"https://github.com/{slug}", ct);
    string Raw(string path) => $"https://raw.githubusercontent.com/{slug}/{sha}/{path}";
    var colors = await Http.TextOrNull(web, Raw("colors.toml"), ct) is { } toml ? ThemePalette.FromColorsToml(toml)
        : await Http.TextOrNull(web, Raw("alacritty.toml"), ct) is { } alacritty ? ThemePalette.FromAlacritty(alacritty)
        : throw new InvalidDataException("no colors.toml or alacritty.toml");
    if (colors == null || colors.Count == 0) throw new InvalidDataException("no colors in its palette");
    var palette = ThemePalette.Kept(ThemePalette.Resolve(colors, await Http.TextOrNull(web, Raw("light.mode"), ct) != null));
    var name = CommunityList.ThemeName(slug) ?? throw new InvalidDataException("its repo gives no usable theme name");
    return new CommunityTheme
    {
        Name = name,
        Title = title ?? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' ').Replace('_', ' ')),
        Repo = slug, Commit = sha, Author = slug.Split('/')[0],
        Mode = palette["mode"], Palette = palette,
        Screenshot = screenshot ?? (await Http.Exists(web, Raw("preview.png"), ct) ? Raw("preview.png") : ""),
    };
}

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "themes", "colors"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

static class Http
{
    public static HttpClient Client()
    {
        var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, MaxConnectionsPerServer = 8 })
        { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-community-themes");
        return client;
    }

    // A few tries: GitHub answers the odd request with a 502 or a reset under load.
    static async Task<T> Retry<T>(Func<Task<T>> get)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await get(); }
            catch (HttpRequestException ex) when (attempt < 3 && ex.StatusCode is null or >= HttpStatusCode.InternalServerError)
            { await Task.Delay(TimeSpan.FromSeconds(2 * attempt)); }
            catch (TaskCanceledException) when (attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(2 * attempt)); }
        }
    }

    public static Task<string> Text(HttpClient c, string url, CancellationToken ct = default) => Retry(() => c.GetStringAsync(url, ct));

    // A file that may not be there: null for a 404.
    public static async Task<string?> TextOrNull(HttpClient c, string url, CancellationToken ct = default)
    {
        try { return await Text(c, url, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public static async Task<bool> Exists(HttpClient c, string url, CancellationToken ct = default)
    {
        using var response = await Retry(() => c.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct));
        return response.IsSuccessStatusCode;
    }
    public static Task<byte[]> Bytes(HttpClient c, string url, CancellationToken ct = default) => Retry(() => c.GetByteArrayAsync(url, ct));
    public static async Task<JsonNode> Json(HttpClient c, string url, CancellationToken ct = default) =>
        JsonNode.Parse(await Text(c, url, ct)) ?? throw new InvalidDataException($"{url}: empty");
}

static class Git
{
    static readonly System.Text.RegularExpressions.Regex Sha = new("^[0-9a-f]{40}$");

    // The commit a remote's HEAD (its default branch) is at, from `git ls-remote`.
    public static async Task<string> Head(string url, CancellationToken ct)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "ls-remote", "--", url, "HEAD" }) info.ArgumentList.Add(a);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0"; // a repo gone private asks for a password: fail instead
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("git didn't start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var sha = output.Split('\t', 2)[0].Trim();
        return process.ExitCode == 0 && Sha.IsMatch(sha) ? sha : throw new InvalidDataException("git ls-remote found no HEAD");
    }
}

static class Thumb
{
    // As src/thumbs: a tile's picture is ~120 units wide, so 320 pixels is sharp at 250% scale.
    // JPEG, not PNG: ~200 screenshots as PNGs would be ~15 MB; as JPEGs they're ~3.
    public const int Width = 320;

    // The picture, Width pixels wide, its proportions kept, scaled with a cubic filter, on the
    // theme's background (for a transparent preview.png); null when it isn't an image.
    public static byte[]? Make(byte[] image, string background)
    {
        using var original = SKBitmap.Decode(image);
        if (original == null || original.Width == 0) return null;
        var height = Math.Max(1, (int)Math.Round((double)original.Height * Width / original.Width));
        using var small = original.Resize(new SKImageInfo(Width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (small == null) return null;
        using var flat = new SKBitmap(new SKImageInfo(Width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColor.TryParse(background, out var under) ? under : SKColors.Black);
            canvas.DrawBitmap(small, 0, 0);
        }
        using var data = flat.Encode(SKEncodedImageFormat.Jpeg, 82);
        return data.ToArray();
    }
}
