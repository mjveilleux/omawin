using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SystemMenu;

// The theme picker's pictures: each theme's first background, small, as a PNG in
// %LOCALAPPDATA%\launcher\thumbnails, named <theme>--<picture>.png (made once, the first time
// the picker shows that theme; the backgrounds themselves are the ones
// themes/set-theme.ps1 downloads into launcher\backgrounds\<theme>, fetched here if missing).
public static class ThemeThumbs
{
    const uint Width = 320; // a tile's picture is ~120 wide: sharp at 250% scale
    static readonly string Dir = Path.Combine(MenuModel.StateDir, "thumbnails");
    static readonly Dictionary<string, ImageSource> cache = [];
    static readonly SemaphoreSlim making = new(2); // two downloads at a time, not 23
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Calls back on the UI thread with the picture, right away if it's been loaded before;
    // never, for a theme without backgrounds or when making it fails.
    public static void Get(string file, Theme theme, Action<ImageSource> done)
    {
        if (theme.Backgrounds.Length == 0) return;
        // Named for the theme and its picture, so a theme whose first background changes gets
        // a new one.
        var source = theme.Backgrounds[0];
        var key = Key(file, source);
        if (cache.TryGetValue(key, out var known)) { done(known); return; }
        var ui = DispatcherQueue.GetForCurrentThread();
        var thumb = Path.Combine(Dir, key + ".png");
        Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(thumb)) await Make(file, source, thumb);
                ui.TryEnqueue(async () =>
                {
                    try
                    {
                        if (cache.TryGetValue(key, out var made)) { done(made); return; }
                        // Decoded now, not when it's first drawn: what Preload is for.
                        var image = new BitmapImage { DecodePixelWidth = (int)Width };
                        using (var stream = await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(thumb, FileAccessMode.Read))
                            await image.SetSourceAsync(stream);
                        cache[key] = image;
                        done(image);
                    }
                    catch (Exception ex) { App.Log(ex); }
                });
            }
            catch (Exception ex) { App.Log(ex); }
        });
    }

    // The picture's name: the theme and its picture's own, so a theme whose first background
    // changes gets a new one (src/thumbs names CI's the same way).
    static string Key(string file, string source) => $"{file}--{Path.GetFileNameWithoutExtension(source.Split('?')[0])}";

    // Once the menu app is up and idle: every theme's picture that's already on this PC (the
    // release brings them all: src/thumbs) read and decoded, so the picker's first open shows every
    // tile's picture in its first frame (roadmap 1.9). Ones not here yet are left for the picker
    // to make: nothing is downloaded at start-up.
    public static void Preload()
    {
        foreach (var (file, theme) in MenuModel.Themes())
            if (theme.Backgrounds.Length > 0 && File.Exists(Path.Combine(Dir, Key(file, theme.Backgrounds[0]) + ".png")))
                Get(file, theme, _ => { });
    }

    // After start-up, every theme's backgrounds, one at a time, into launcher\backgrounds\<theme>
    // (where themes/set-theme.ps1 looks first), so switching to any theme for the first time
    // doesn't wait on a download (roadmap 1.9). Only on an unmetered connection: a metered one
    // gets a theme's pictures when you pick it. Ones already here are skipped, so once they're all
    // in, this is a look at each file and nothing more.
    public static void FetchBackgrounds() => Task.Run(async () =>
    {
        try
        {
            var cost = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            if (cost == null || cost.NetworkCostType != Windows.Networking.Connectivity.NetworkCostType.Unrestricted || cost.Roaming) return;
            foreach (var (file, theme) in MenuModel.Themes())
                foreach (var source in theme.Backgrounds.Where(s => s.StartsWith("https://") || s.StartsWith("http://")))
                {
                    var image = Path.Combine(MenuModel.StateDir, "backgrounds", file, Path.GetFileName(new Uri(source).LocalPath));
                    if (File.Exists(image)) continue;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
                        var bytes = await http.GetByteArrayAsync(source);
                        await File.WriteAllBytesAsync(image + ".part", bytes);
                        File.Move(image + ".part", image, overwrite: true);
                    }
                    catch (Exception ex) { App.Log(ex); }
                }
        }
        catch (Exception ex) { App.Log(ex); }
    });

    static async Task Make(string file, string source, string thumb)
    {
        await making.WaitAsync();
        try
        {
            if (File.Exists(thumb)) return;
            string image;
            if (source.StartsWith("http://") || source.StartsWith("https://"))
            {
                image = Path.Combine(MenuModel.StateDir, "backgrounds", file, Path.GetFileName(new Uri(source).LocalPath));
                if (!File.Exists(image))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(image)!);
                    var bytes = await http.GetByteArrayAsync(source);
                    await File.WriteAllBytesAsync(image + ".part", bytes);
                    File.Move(image + ".part", image, overwrite: true);
                }
            }
            else image = Path.Combine(MenuModel.Repo, source);

            // Windows' own decoders (WebP included), scaled down, saved as a PNG.
            var input = await StorageFile.GetFileFromPathAsync(image);
            using var stream = await input.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            uint height = Math.Max(1, (uint)Math.Round((double)decoder.PixelHeight * Width / decoder.PixelWidth));
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = Width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            Directory.CreateDirectory(Dir);
            using (var output = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                output.Seek(0);
                using var saved = File.Create(thumb + ".part");
                await output.AsStreamForRead().CopyToAsync(saved);
            }
            File.Move(thumb + ".part", thumb, overwrite: true);
            // The theme's older pictures, from before its first background changed.
            foreach (var old in Directory.GetFiles(Dir, file + "--*.png").Where(f => f != thumb))
                try { File.Delete(old); } catch { }
        }
        finally { making.Release(); }
    }
}
