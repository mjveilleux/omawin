using System.Text.Json;
using SkiaSharp;
using SystemMenu;

// dotnet run --project src/thumbs -- <out dir> [themes dir] [--first]
// Every background of every theme, 320 pixels wide, as the PNGs the menu app shows: the theme
// picker's tile (a theme's first background) and the background picker's grid (all of them).
// shell/menu-app/ThemeThumbs.cs makes the same file itself when one is missing. Named
// <theme>--<picture>.png by ThemePictures.cs, the file both compile, so the install drops them
// straight into %LOCALAPPDATA%\launcher\thumbnails and both pickers' first open has every
// picture. --first: only each theme's first background (the theme picker's alone).
var first = args.Contains("--first");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
if (positional.Length < 1) { Console.Error.WriteLine("usage: <out dir> [themes dir] [--first]"); return 2; }
var outDir = Path.GetFullPath(positional[0]);
var themesDir = positional.Length > 1 ? Path.GetFullPath(positional[1]) : Path.Combine(FindRepo(), "themes");
var repo = Path.GetDirectoryName(themesDir)!;
Directory.CreateDirectory(outDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-thumbs");

// Each theme's pictures, as (theme, source) pairs: one list, so six download at a time across
// themes rather than one theme's eight in a row.
var themes = Directory.GetFiles(themesDir, "*.json").Order().ToList();
var pictures = new List<(string Theme, string Source)>();
foreach (var file in themes)
{
    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
    if (!doc.RootElement.TryGetProperty("backgrounds", out var b) || b.ValueKind != JsonValueKind.Array) continue; // a theme without pictures: the picker shows its colours
    var sources = b.EnumerateArray().Select(e => e.GetString()).OfType<string>().Where(s => s.Length > 0);
    foreach (var source in first ? sources.Take(1) : sources)
        pictures.Add((Path.GetFileNameWithoutExtension(file), source));
}

var made = 0;
long bytesOut = 0;
var failed = new List<string>();
await Parallel.ForEachAsync(pictures, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (picture, ct) =>
{
    var (name, source) = picture;
    try
    {
        var bytes = source.StartsWith("http://") || source.StartsWith("https://")
            ? await http.GetByteArrayAsync(source, ct)
            : await File.ReadAllBytesAsync(Path.Combine(repo, source), ct);
        var png = Thumbs.Make(bytes) ?? throw new InvalidDataException("not an image Skia can read");
        await File.WriteAllBytesAsync(Path.Combine(outDir, ThemePictures.ThumbName(name, source)), png, ct);
        Interlocked.Increment(ref made);
        Interlocked.Add(ref bytesOut, png.Length);
    }
    catch (Exception ex) { lock (failed) failed.Add($"{name} {ThemePictures.Name(source)}: {ex.Message}"); }
});
Console.WriteLine($"  {made} pictures of {themes.Count} themes, {bytesOut / 1024.0 / 1024.0:0.0} MB -> {outDir}");
foreach (var f in failed) Console.Error.WriteLine($"  ! {f}");
return failed.Count == 0 ? 0 : 1;

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "themes", "colors"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

public static class Thumbs
{
    // A tile's picture is ~120 units wide: 320 pixels is sharp at 250% scale (ThemeThumbs.Width).
    public const int Width = 320;

    // The picture, Width pixels wide, its proportions kept, scaled down with a cubic filter
    // (Mitchell: sharp without ringing); null when it isn't an image.
    public static byte[]? Make(byte[] image)
    {
        using var original = SKBitmap.Decode(image);
        if (original == null || original.Width == 0) return null;
        var height = Math.Max(1, (int)Math.Round((double)original.Height * Width / original.Width));
        using var small = original.Resize(new SKImageInfo(Width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (small == null) return null;
        using var data = small.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
