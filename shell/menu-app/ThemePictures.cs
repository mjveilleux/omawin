namespace SystemMenu;

// A theme's backgrounds by name: what the background picker (MainWindow.Tiles.cs), ThemeThumbs,
// themes/set-theme.ps1 -Background and CI's src/thumbs all agree on. Pure (no WinUI, no files),
// so menu-app.Tests checks it, and src/thumbs compiles this same file so its thumbnails are named
// exactly as the menu app looks for them.
public static class ThemePictures
{
    // A background's own name: its file name without the extension, a URL's query string left
    // out ("…/3-village-square.jpg?raw=1" is "3-village-square"). Both slashes count, so an
    // installed theme's Windows path names the same on any system CI runs on.
    public static string Name(string source)
    {
        var path = source.Split('?')[0];
        var file = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    // Its thumbnail's file: the theme and the picture's name, so a theme whose pictures change
    // gets new ones rather than stale ones.
    public static string ThumbName(string theme, string source) => $"{theme}--{Name(source)}.png";

    // What a tile says: "3-village-square" is "Village square" (Omarchy numbers its backgrounds
    // to order them; the number means nothing to you).
    public static string Label(string source)
    {
        var name = Name(source);
        int i = 0;
        while (i < name.Length && char.IsAsciiDigit(name[i])) i++;
        // Only a leading number followed by a separator is an order number ("1-leaves", not "80s").
        if (i > 0 && i < name.Length && name[i] is '-' or '_' or ' ' or '.') name = name[(i + 1)..];
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        while (name.Contains("  ")) name = name.Replace("  ", " ");
        if (name.Length == 0) return Name(source);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    // Which of the theme's backgrounds is on the desktop: set-theme.ps1 remembers its source in
    // %LOCALAPPDATA%\launcher\background. The same source first; else the same picture's name (a
    // theme re-imported at a newer Omarchy commit keeps its file names); -1 when it's none of them.
    public static int Current(IReadOnlyList<string> backgrounds, string? saved)
    {
        saved = saved?.Trim();
        if (string.IsNullOrEmpty(saved)) return -1;
        for (int i = 0; i < backgrounds.Count; i++)
            if (string.Equals(backgrounds[i], saved, StringComparison.OrdinalIgnoreCase)) return i;
        var name = Name(saved);
        for (int i = 0; i < backgrounds.Count; i++)
            if (string.Equals(Name(backgrounds[i]), name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // Typing narrows the grid: every word somewhere in the picture's name or its label.
    public static bool Matches(string source, string query)
    {
        var text = Name(source) + " " + Label(source);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    // Of the thumbnail files in the folder (names only), the theme's that none of its backgrounds
    // uses any more (Omarchy renamed or dropped a picture): safe to delete. Another theme's are
    // never touched, even one whose name starts the same ("catppuccin" and "catppuccin-latte").
    public static List<string> Stale(string theme, IEnumerable<string> files, IReadOnlyList<string> backgrounds)
    {
        var wanted = backgrounds.Select(b => ThumbName(theme, b)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prefix = theme + "--";
        return files.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !f[prefix.Length..].Contains("--") // "a--b--c.png" is theme "a--b"'s, if there's one
                && !wanted.Contains(f))
            .ToList();
    }
}
