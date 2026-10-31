namespace Omawin.Windows;

// omawin-windows wallpaper spread <picture>… (8.8): a picture per monitor, left to right. The theme
// switch (themes/set-theme.ps1) calls it with the theme's next backgrounds when there's more than
// one monitor; on its own it takes any pictures.
static class WallpaperCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] != "spread") return Program.Fail("omawin-windows wallpaper spread <picture> [picture…]");
        var pictures = args[1..].Select(Path.GetFullPath).ToList();
        if (pictures.FirstOrDefault(p => !File.Exists(p)) is { } missing) return Program.Fail($"no picture at {missing}");
        int set = Com.InStaThread(() => Wallpaper.Spread(pictures));
        return set == 0 ? Program.Fail("Windows listed no monitors") : 0;
    }
}
