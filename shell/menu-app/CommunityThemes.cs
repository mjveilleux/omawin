using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace SystemMenu;

// The community theme index on this PC (roadmap 1.10; what's in it: Community.cs): fetched in the
// background a minute after start-up, then daily, from the release CI rebuilds weekly
// (.github/workflows/community-themes.yml), checked against its SHA-256 and unpacked into
// %LOCALAPPDATA%\launcher\community (index.json, thumbs\). The theme picker's Community chip
// (MainWindow.Community.cs) and `omawin theme add|search|preview` (themes/theme.ps1) read it.
public static class CommunityThemes
{
    const string Release = "https://github.com/mjveilleux/omawin/releases/download/community-themes/";
    public const string KeyPrefix = "community:"; // a community tile's key: community:<name>
    static string Dir => Path.Combine(MenuModel.StateDir, "community");
    static string IndexFile => Path.Combine(Dir, "index.json");
    static string SumFile => Path.Combine(Dir, "community-themes.zip.sha256");
    // What the picker asks to be previewed (theme.ps1 preview applies it), and what was applied.
    static string PreviewFile => Path.Combine(MenuModel.StateDir, "theme-preview");
    static string PreviewApplied => Path.Combine(MenuModel.StateDir, "community-preview", "applied");
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
    static List<CommunityTheme> loaded = [];
    static DateTime loadedStamp;
    static readonly Dictionary<string, Theme> themes = [];
    static readonly Dictionary<string, ImageSource> pictures = [];

    // Every theme in the index, most stars first; empty until the first fetch. Read again when
    // the file changed (a daily fetch brought a new one).
    public static List<CommunityTheme> All()
    {
        var stamp = File.GetLastWriteTimeUtc(IndexFile); // a missing file: 1601, also a stamp
        if (stamp == loadedStamp) return loaded;
        try
        {
            var index = JsonSerializer.Deserialize(File.ReadAllText(IndexFile), Json.Default.CommunityIndex);
            loaded = CommunityList.Ordered((index?.Themes ?? []).Where(t => t.Name.Length > 0 && t.Palette.Count > 0)).ToList();
        }
        catch (FileNotFoundException) { loaded = []; }
        catch (DirectoryNotFoundException) { loaded = []; }
        catch (Exception ex) { App.Log(ex); loaded = []; }
        loadedStamp = stamp;
        themes.Clear();
        return loaded;
    }

    public static CommunityTheme? Find(string name) =>
        All().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Its colours as omawin's theme (what a swatch tile draws), from the index's palette with
    // convert-theme.ps1's mapping (ThemePalette.ThemeColors); made once.
    public static Theme ThemeOf(CommunityTheme c)
    {
        if (themes.TryGetValue(c.Name, out var known)) return known;
        var t = new Theme { Name = c.Title, Backgrounds = c.Backgrounds };
        try
        {
            var p = ThemePalette.Resolve(c.Palette, c.Mode == "light");
            var f = ThemePalette.ThemeColors(p);
            t.Mode = f["mode"]; t.Background = f["background"]; t.Surface = f["surface"]; t.Selection = f["selection"];
            t.Border = f["border"]; t.Foreground = f["foreground"]; t.Muted = f["muted"]; t.Comment = f["comment"];
            t.Accent = f["accent"]; t.Purple = f["purple"]; t.Cyan = f["cyan"]; t.Red = f["red"]; t.Green = f["green"];
            t.Yellow = f["yellow"]; t.Gradient = ThemePalette.Gradient(p);
        }
        catch (Exception ex) { App.Log(ex); } // a palette that isn't one: the default colours
        return themes[c.Name] = t;
    }

    // Its thumbnail (thumbs\<name>.jpg, from the index), called back on the UI thread; never for
    // one without. Kept decoded while the picker shows them; Forget lets them go.
    public static void Picture(CommunityTheme c, Action<ImageSource> done)
    {
        if (pictures.TryGetValue(c.Name, out var known)) { done(known); return; }
        if (c.Thumb.Length == 0 || c.Thumb.Contains("..")) return;
        var file = Path.Combine(Dir, c.Thumb.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return;
        var ui = DispatcherQueue.GetForCurrentThread();
        ui.TryEnqueue(async () =>
        {
            try
            {
                if (pictures.TryGetValue(c.Name, out var made)) { done(made); return; }
                var image = new BitmapImage { DecodePixelWidth = 320 };
                using (var stream = await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(file, FileAccessMode.Read))
                    await image.SetSourceAsync(stream);
                pictures[c.Name] = image;
                done(image);
            }
            catch (Exception ex) { App.Log(ex); }
        });
    }

    // ~200 decoded pictures are ~45 MB: let them go when the picker closes (the files stay, and
    // decoding them again on the next open is quick).
    public static void Forget() => pictures.Clear();

    // From App.OnLaunched: a preview the last run left on the desktop (it ended mid-preview) is
    // put back, then the index is fetched a minute after start-up and once a day.
    public static void Start()
    {
        if (File.Exists(PreviewApplied) || File.Exists(PreviewFile)) Preview(null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            while (true)
            {
                try { await Refresh(); } catch (Exception ex) { App.Log(ex); }
                await Task.Delay(TimeSpan.FromDays(1));
            }
        });
    }

    // A community theme across the desktop for a moment (null: yours back). The wish is written
    // here, in the order you move, then `omawin theme preview` applies whatever it says once its
    // turn comes (theme.ps1 Sync-Preview), so quick moves never leave an older one on screen.
    public static void Preview(string? name)
    {
        try
        {
            if (name == null) File.Delete(PreviewFile);
            else File.WriteAllText(PreviewFile, name);
        }
        catch (Exception ex) { App.Log(ex); return; }
        MenuModel.Omawin("theme", "preview");
    }

    // Installing applies the theme for real: the preview's wish goes, with nothing to put back.
    public static void EndPreviewForInstall()
    {
        try { File.Delete(PreviewFile); } catch (Exception ex) { App.Log(ex); }
    }

    // The release's checksum first (a few bytes); the zip only when it changed, and not on a
    // metered connection unless there's no index yet. Unpacked aside, then swapped in, so the
    // picker never reads half an index.
    static async Task Refresh()
    {
        var sum = (await http.GetStringAsync(Release + "community-themes.zip.sha256")).Split(' ', 2)[0].Trim().ToLowerInvariant();
        if (sum.Length != 64) throw new InvalidDataException($"community-themes.zip.sha256: '{sum}' isn't a SHA-256");
        string? have = null;
        try { have = File.ReadAllText(SumFile).Trim(); } catch { }
        if (have == sum && File.Exists(IndexFile)) return;
        if (File.Exists(IndexFile) && Metered()) return;
        var zip = await http.GetByteArrayAsync(Release + "community-themes.zip");
        if (Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() != sum)
            throw new InvalidDataException("community-themes.zip doesn't match its SHA-256 (the release was mid-update?); trying again tomorrow");
        var incoming = Dir + ".new";
        if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
        using (var stream = new MemoryStream(zip)) ZipFile.ExtractToDirectory(stream, incoming); // refuses entries outside the folder
        File.WriteAllText(Path.Combine(incoming, "community-themes.zip.sha256"), sum);
        var old = Dir + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(Dir)) Directory.Move(Dir, old);
        Directory.Move(incoming, Dir);
        try { Directory.Delete(old, true); } catch { } // a thumbnail still open: next time
    }

    static bool Metered()
    {
        var cost = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
        return cost == null || cost.NetworkCostType != Windows.Networking.Connectivity.NetworkCostType.Unrestricted || cost.Roaming;
    }
}
