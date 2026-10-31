using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SystemMenu;

// The community theme index (roadmap 1.10): every theme omarchy.org/themes lists (and
// themes/community-extra.txt), read by CI at a pinned commit (src/community-themes), published as
// the release asset community-themes.zip (index.json and thumbs/), and shown by the theme picker's
// Community chip (MainWindow.Community.cs). Pure (no WinUI, no files, no network): CI compiles this
// same file, menu-app.Tests checks it, and the menu app reads the index it describes.
public sealed class CommunityIndex
{
    public string Generated { get; set; } = "";     // when CI read it (UTC, ISO 8601)
    public List<CommunityTheme> Themes { get; set; } = [];
}

public sealed class CommunityTheme
{
    public string Name { get; set; } = "";          // omawin's name for it, as `theme install` names its link (CommunityList.ThemeName)
    public string Title { get; set; } = "";         // as omarchy.org shows it
    public string Repo { get; set; } = "";          // owner/repo on GitHub
    public string Commit { get; set; } = "";        // the commit read: what `omawin theme add` installs
    public string Author { get; set; } = "";        // the repo's owner
    public string License { get; set; } = "";       // SPDX id, or "" when the repo names none
    public int Stars { get; set; }
    public string Updated { get; set; } = "";       // the repo's last push
    public string Mode { get; set; } = "dark";      // light or dark
    public string Screenshot { get; set; } = "";    // the picture its thumbnail was made from
    public string Thumb { get; set; } = "";         // thumbs/<name>.jpg, inside the index's folder
    public Dictionary<string, string> Palette { get; set; } = []; // the whole palette in Omarchy's names (ThemePalette.Resolve)
    public string[] Backgrounds { get; set; } = []; // its wallpapers, raw URLs at Commit
}

// Which themes there are: omarchy.org/themes' list, and the hand-kept extras.
public static class CommunityList
{
    public const string Page = "https://omarchy.org/themes/";

    // omarchy.org/themes is static HTML (Astro), one list item per theme: a link to its GitHub
    // repo around a 1200x675 screenshot the site hosts, its alt text "<Name> theme screenshot".
    static readonly Regex Card = new(
        @"<li>\s*<a\s+href=""(?<repo>https://github\.com/[^""]+)""[^>]*>\s*<img\s+src=""(?<img>/assets/themes/[^""]+)""[^>]*?\salt=""(?<name>[^""]+?) theme screenshot""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Every theme on the page, in its order: its title, owner/repo, and screenshot. Links that
    // aren't a repo (an org page, a gist) are left out.
    public static List<(string Title, string Repo, string Screenshot)> Parse(string html)
    {
        var found = new List<(string, string, string)>();
        foreach (Match m in Card.Matches(html))
        {
            if (RepoOf(WebUtility.HtmlDecode(m.Groups["repo"].Value)) is not { } repo) continue;
            var image = new Uri(new Uri(Page), WebUtility.HtmlDecode(m.Groups["img"].Value)).ToString();
            found.Add((WebUtility.HtmlDecode(m.Groups["name"].Value).Trim(), repo, image));
        }
        return found;
    }

    // themes/community-extra.txt: one repo per line, as owner/repo or its GitHub link; # comments.
    public static List<string> Extra(IEnumerable<string> lines) =>
        lines.Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0)
            .Select(RepoOf).OfType<string>().ToList();

    static readonly Regex Slug = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

    // "owner/repo" from a GitHub link (https://github.com/owner/repo, .git, a trailing /tree/main…)
    // or from owner/repo itself; null when it's neither, or a name no repo can have.
    public static string? RepoOf(string text)
    {
        var s = text.Trim();
        var at = s.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) s = s[(at + "github.com/".Length)..];
        else if (s.Contains("://")) return null;
        s = s.Split('?', '#')[0];
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        var slug = $"{parts[0]}/{repo}";
        return Slug.IsMatch(slug) && repo is not ("." or "..") ? slug : null;
    }

    // The theme's name from its repo, as themes/theme.ps1's Get-ThemeName names an installed one
    // (so `theme add <name>` installs under the name the index shows): without "omarchy-" and
    // "-theme", lower case, held to what a folder and an argument can carry; null when nothing's left.
    public static string? ThemeName(string repo)
    {
        var name = repo.TrimEnd('/');
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.StartsWith("omarchy-", StringComparison.OrdinalIgnoreCase)) name = name[8..];
        if (name.EndsWith("-theme", StringComparison.OrdinalIgnoreCase)) name = name[..^6];
        name = name.ToLowerInvariant();
        return Regex.IsMatch(name, "^[a-z0-9_][a-z0-9._+-]*$") && !name.Contains("..") ? name : null;
    }

    // A search of the index (the picker's typing, `omawin theme search`): every word in its name,
    // title, author or repo, or "light" / "dark" for its mode.
    public static bool Matches(CommunityTheme t, string query)
    {
        var text = $"{t.Name} {t.Title} {t.Author} {t.Repo}";
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w =>
            text.Contains(w, StringComparison.OrdinalIgnoreCase) || t.Mode.Equals(w, StringComparison.OrdinalIgnoreCase));
    }

    // The picker's and the search's order: most stars first, then by title.
    public static IEnumerable<CommunityTheme> Ordered(IEnumerable<CommunityTheme> themes) =>
        themes.OrderByDescending(t => t.Stars).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);
}

// A theme's palette, read the way themes/convert-theme.ps1 reads one (which reads it the way
// Omarchy's omarchy-theme-color does): colors.toml, or alacritty.toml for older themes, missing
// colors filled in with the same fallbacks. CI stores the whole palette in the index, so the
// picker draws a community theme's swatch and `omawin theme preview` renders it without its repo.
public static class ThemePalette
{
    // A color as #rrggbb, or null: "#abc123", "abc123", "0xabc123", "#abc123ff" (alpha dropped).
    public static string? Hex(string value)
    {
        var m = Regex.Match(value, "^\\s*(?:#|0x)?([0-9a-fA-F]{6})(?:[0-9a-fA-F]{2})?\\s*$");
        return m.Success ? "#" + m.Groups[1].Value.ToLowerInvariant() : null;
    }

    // A line's value: what's inside its quotes, else up to a comment.
    static string Value(string raw)
    {
        var quoted = Regex.Match(raw, "^[\"']([^\"']*)[\"']");
        return quoted.Success ? quoted.Groups[1].Value : Regex.Split(raw, "\\s+#")[0].Trim();
    }

    // colors.toml: flat `key = "value"` lines. Only colors, and mode / theme_type as dark or
    // light, are kept: these are filled into configs, so nothing else in the file gets through.
    public static Dictionary<string, string> FromColorsToml(string text)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var m = Regex.Match(line.TrimEnd('\r'), "^\\s*([A-Za-z0-9_-]+)\\s*=\\s*(.*)$");
            if (!m.Success) continue;
            var (key, value) = (m.Groups[1].Value, Value(m.Groups[2].Value.Trim()));
            if (key is "mode" or "theme_type") { if (value is "dark" or "light") colors[key] = value; }
            else if (Hex(value) is { } hex) colors[key] = hex;
        }
        return colors;
    }

    static readonly string[] AnsiNames = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];

    // alacritty.toml: its [colors.*] tables, or dotted keys under [colors]; null if it doesn't
    // hold all eight normal colors (omarchy-theme-colors-from-alacritty).
    public static Dictionary<string, string>? FromAlacritty(string text)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var header = Regex.Match(line, "^\\[([^\\]]+)\\]\\s*$");
            if (header.Success) { section = header.Groups[1].Value; continue; }
            if (section.Length == 0 || line.StartsWith('#')) continue;
            var m = Regex.Match(line, "^([^=]+?)\\s*=\\s*(.+)$");
            if (!m.Success || Hex(Value(m.Groups[2].Value.Trim())) is not { } hex) continue;
            found.TryAdd($"{section}.{m.Groups[1].Value.Trim()}", hex);
        }
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 8; i++)
        {
            if (!found.TryGetValue($"colors.normal.{AnsiNames[i]}", out var normal)) return null;
            colors[$"color{i}"] = normal;
            colors[$"color{i + 8}"] = found.GetValueOrDefault($"colors.bright.{AnsiNames[i]}", normal);
        }
        colors["background"] = found.GetValueOrDefault("colors.primary.background", colors["color0"]);
        colors["foreground"] = found.GetValueOrDefault("colors.primary.foreground", colors["color7"]);
        colors["selection"] = found.GetValueOrDefault("colors.selection.background", colors["foreground"]);
        colors["accent"] = colors["color4"];
        return colors;
    }

    // a blended toward b by amount (0..1), rounded as convert-theme.ps1's Get-Mix rounds.
    public static string Mix(string a, string b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var hex = "#";
        for (int i = 1; i < 7; i += 2)
        {
            int x = Convert.ToInt32(a.Substring(i, 2), 16), y = Convert.ToInt32(b.Substring(i, 2), 16);
            hex += ((int)Math.Floor(x * (1 - amount) + y * amount + 0.5)).ToString("x2", CultureInfo.InvariantCulture);
        }
        return hex;
    }

    // Every name Omarchy's templates use, filled in from what the theme has, in the order and with
    // the fallbacks of convert-theme.ps1's Resolve-Palette (omarchy-theme-color's
    // resolve_theme_colors). lightMode: the theme has a light.mode file. Throws when there's no
    // background or foreground to start from.
    public static Dictionary<string, string> Resolve(IReadOnlyDictionary<string, string> raw, bool lightMode)
    {
        var c = new Dictionary<string, string>(raw.Count + 32, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in raw) c[k] = v;
        bool Has(string key) => c.TryGetValue(key, out var v) && v.Length > 0;
        void From(string key, string from) { if (!Has(key) && Has(from)) c[key] = c[from]; }

        foreach (var (key, from) in new[] { ("background", "bg"), ("dark_background", "dark_bg"), ("darker_background", "darker_bg"),
                     ("lighter_background", "lighter_bg"), ("foreground", "fg"), ("dark_foreground", "dark_fg"),
                     ("light_foreground", "light_fg"), ("bright_foreground", "bright_fg") })
            From(key, from);
        From("background", "color0");
        From("foreground", "color7");
        if (!Has("background") || !Has("foreground")) throw new InvalidDataException("The palette has no background or foreground color.");
        c["color0"] = c["background"];
        c["color7"] = c["foreground"];
        foreach (var (key, from) in new[] { ("red", "color1"), ("green", "color2"), ("yellow", "color3"), ("blue", "color4"),
                     ("magenta", "color5"), ("cyan", "color6"), ("bright_red", "color9"), ("bright_green", "color10"),
                     ("bright_yellow", "color11"), ("bright_blue", "color12"), ("bright_magenta", "color13"),
                     ("bright_cyan", "color14"), ("magenta", "purple"), ("bright_magenta", "bright_purple") })
            From(key, from);
        foreach (var key in new[] { "red", "green", "yellow", "blue", "magenta", "cyan" })
            if (!Has(key)) c[key] = c["foreground"];
        From("light_foreground", "color7");
        From("bright_foreground", "color15");
        From("bright_foreground", "foreground");
        From("lighter_background", "color0");
        From("dark_foreground", "color8");
        From("dark_foreground", "foreground");
        From("muted", "color8");
        From("muted", "dark_foreground");
        if (!Has("selection"))
            c["selection"] = new[] { "selection_background", "color8", "color0", "background" }.Where(Has).Select(k => c[k]).First();
        From("selection_background", "selection");
        From("selection_foreground", "bright_foreground");
        From("orange", "yellow");
        if (!Has("brown")) c["brown"] = Mix(c["orange"], "#000000", 0.5);
        if (!Has("dark_background")) c["dark_background"] = Mix(c["background"], "#000000", 0.25);
        if (!Has("darker_background")) c["darker_background"] = Mix(c["background"], "#000000", 0.5);
        foreach (var key in new[] { "red", "yellow", "green", "cyan", "blue", "magenta" })
            if (!Has("bright_" + key)) c["bright_" + key] = Mix(c[key], "#ffffff", 0.2);
        if (!Has("accent")) c["accent"] = c["blue"];
        From("mode", "theme_type");
        if (!Has("mode"))
        {
            var bg = c["background"];
            int lum = Convert.ToInt32(bg.Substring(1, 2), 16) + Convert.ToInt32(bg.Substring(3, 2), 16) + Convert.ToInt32(bg.Substring(5, 2), 16);
            c["mode"] = lightMode || lum > 382 ? "light" : "dark";
        }
        c["theme_type"] = c["mode"];
        return c;
    }

    // The palette as convert-theme.ps1 writes colors/<name>.toml: Omarchy's names, in its order.
    public static readonly string[] Order =
    [
        "mode", "accent", "selection", "muted",
        "background", "dark_background", "darker_background", "lighter_background",
        "foreground", "dark_foreground", "light_foreground", "bright_foreground",
        "red", "yellow", "orange", "green", "cyan", "blue", "magenta", "brown",
        "bright_red", "bright_yellow", "bright_green", "bright_cyan", "bright_blue", "bright_magenta",
        "selection_foreground", "selection_background",
    ];

    // Only the names in Order, in that order: what the index keeps of a resolved palette (the
    // color0..15 a theme started from are folded into these).
    public static Dictionary<string, string> Kept(IReadOnlyDictionary<string, string> resolved) =>
        Order.Where(resolved.ContainsKey).ToDictionary(k => k, k => resolved[k]);

    // omawin's theme colors (themes/<name>.json, what the menu app and the bar read) from a
    // resolved palette, as convert-theme.ps1 maps them.
    public static Dictionary<string, string> ThemeColors(IReadOnlyDictionary<string, string> p) => new()
    {
        ["mode"] = p["mode"], ["background"] = p["background"], ["surface"] = p["lighter_background"],
        ["selection"] = p["selection"], ["border"] = p["muted"], ["foreground"] = p["bright_foreground"],
        ["muted"] = p["foreground"], ["comment"] = p["dark_foreground"], ["accent"] = p["accent"],
        ["purple"] = p["bright_magenta"], ["cyan"] = p["bright_cyan"], ["teal"] = p["cyan"],
        ["green"] = p["green"], ["yellow"] = p["yellow"], ["orange"] = p["orange"], ["red"] = p["red"],
    };

    // The theme's gradient (the menu's hairline), as convert-theme.ps1 picks it.
    public static string[] Gradient(IReadOnlyDictionary<string, string> p) =>
        [p["bright_magenta"], p["accent"], p["bright_cyan"], p["cyan"], p["green"]];
}
