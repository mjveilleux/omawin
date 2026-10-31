using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SystemMenu;

// Store apps' own logo files (roadmap 1.8 step 4), for AppIcons: the app's package found for the
// current user, its manifest read for the app's Square44x44Logo, the best of the files beside it
// chosen (PackageLogo) and decoded. The shell can hand a Store app's logo back on its coloured
// plate; the package ships unplated ones, per size, and for light backgrounds. Not part of
// AppIcons itself because that class is unsafe (its vtable calls), where await isn't allowed.
public static class StoreLogos
{
    // A Store app's logo files, found once per app: the folder its manifest's Square44x44Logo is
    // in, the logo's file name, every file there (and in qualifier folders such as scale-200)
    // that may be a variant of it, and whether its plate is transparent. Null (remembered) when
    // the package or its logo can't be found: the shell's icon then.
    sealed record Logo(string Dir, string Name, string[] Files, bool Plateless);
    static readonly Dictionary<string, Logo?> logos = [];
    static readonly Lazy<Windows.Management.Deployment.PackageManager> packages = new(() => new());

    // A Store app's logo for drawing `want` pixels wide on a light or dark background, as
    // top-down premultiplied BGRA (what IconScale and WriteableBitmap want), with the file it came
    // from (relative to the logo's folder). Null for anything that isn't a Store app, or one whose
    // package has no suitable file, or when reading it fails: the caller uses the shell's icon.
    // Blocks the calling thread: the work runs on the thread pool, where the WinRT calls complete
    // (the icon worker is an STA thread with no message loop).
    public static (byte[] Pixels, int Width, int Height, string File)? Load(string id, int want, bool light)
    {
        if (PackageLogo.SplitAumid(id) is not { } aumid) return null;
        try { return Task.Run(() => LoadAsync(aumid.Family, aumid.App, want, light)).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    static async Task<(byte[], int, int, string)?> LoadAsync(string family, string app, int want, bool light)
    {
        if (FindLogo(family, app) is not { } logo
            || PackageLogo.Best(logo.Files, logo.Name, want, light, logo.Plateless) is not { } best) return null;
        using var stream = await FileRandomAccessStream.OpenAsync(Path.Combine(logo.Dir, best.File), FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var pixels = data.DetachPixelData();
        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
        if (w <= 0 || h <= 0 || pixels.Length != w * h * 4)
            throw new InvalidDataException($"{best.File}: {w}x{h}, {pixels.Length} bytes");
        return (pixels, w, h, best.File);
    }

    static Logo? FindLogo(string family, string app)
    {
        var key = family + "!" + app;
        lock (logos) if (logos.TryGetValue(key, out var known)) return known;
        Logo? logo = null;
        try
        {
            // The current user's packages (an empty user id): no admin needed.
            var package = packages.Value.FindPackagesForUser(string.Empty, family).FirstOrDefault();
            if (package != null)
            {
                var root = Path.GetFullPath(package.InstalledPath);
                var visual = PackageLogo.Manifest(File.ReadAllText(Path.Combine(root, "AppxManifest.xml")), app);
                if (visual != null)
                {
                    var path = Path.GetFullPath(Path.Combine(root, visual.Logo));
                    var dir = Path.GetDirectoryName(path)!;
                    string name = Path.GetFileName(path), pattern = Path.GetFileNameWithoutExtension(name) + "*" + Path.GetExtension(name);
                    var files = Directory.EnumerateFiles(dir, pattern).Select(f => Path.GetFileName(f)).ToList();
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var folder = Path.GetFileName(sub);
                        if (PackageLogo.IsQualifierFolder(folder))
                            files.AddRange(Directory.EnumerateFiles(sub, pattern).Select(f => Path.Combine(folder, Path.GetFileName(f))));
                    }
                    logo = new Logo(dir, name, [.. files], visual.Plateless);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        lock (logos) logos[key] = logo;
        return logo;
    }
}
