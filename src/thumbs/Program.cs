using System.Text.Json;
using SkiaSharp;

// dotnet run --project src/thumbs -- <out dir> [themes dir]
// Each theme's first background, 320 pixels wide, as the PNG the menu app's theme picker shows
// (shell/menu-app/ThemeThumbs.cs makes the same file itself when one is missing): named
// <theme>--<picture>.png, as it names them, so the install drops them straight into
// %LOCALAPPDATA%\launcher\thumbnails and the picker's first open has every picture.
if (args.Length < 1) { Console.Error.WriteLine("usage: <out dir> [themes dir]"); return 2; }
var outDir = Path.GetFullPath(args[0]);
var themesDir = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(FindRepo(), "themes");
var repo = Path.GetDirectoryName(themesDir)!;
Directory.CreateDirectory(outDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-thumbs");

var themes = Directory.GetFiles(themesDir, "*.json").Order().ToList();
var made = 0;
var failed = new List<string>();
await Parallel.ForEachAsync(themes, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (file, ct) =>
{
    var name = Path.GetFileNameWithoutExtension(file);
    string? source = null;
    try
    {
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct)))
            if (doc.RootElement.TryGetProperty("backgrounds", out var b) && b.ValueKind == JsonValueKind.Array && b.GetArrayLength() > 0)
                source = b[0].GetString();
        if (string.IsNullOrEmpty(source)) return;  // a theme without pictures: the picker shows its colours
        var bytes = source.StartsWith("http://") || source.StartsWith("https://")
            ? await http.GetByteArrayAsync(source, ct)
            : await File.ReadAllBytesAsync(Path.Combine(repo, source), ct);
        var png = Thumbs.Make(bytes) ?? throw new InvalidDataException("not an image Skia can read");
        await File.WriteAllBytesAsync(Path.Combine(outDir, Thumbs.FileName(name, source)), png, ct);
        Interlocked.Increment(ref made);
    }
    catch (Exception ex) { lock (failed) failed.Add($"{name}: {ex.Message}"); }
});
Console.WriteLine($"  {made} pictures of {themes.Count} themes -> {outDir}");
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

    // The menu app's name for it: the theme and its picture's own name, query string left out,
    // so a theme whose first background changes gets a new one.
    public static string FileName(string theme, string source) =>
        $"{theme}--{Path.GetFileNameWithoutExtension(source.Split('?')[0])}.png";

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
