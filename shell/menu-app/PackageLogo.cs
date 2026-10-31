using System.Xml;
using System.Xml.Linq;

namespace SystemMenu;

// Store apps' own logos (roadmap 1.8 step 4). A packaged app ships its Start / taskbar logo as
// several files beside the one its manifest names (Assets\Square44x44Logo.png): one per size
// (Square44x44Logo.targetsize-24.png), per scale (.scale-200.png), and unplated, drawn to stand
// on their own rather than on the app's coloured plate (.targetsize-24_altform-unplated.png,
// _altform-lightunplated for a light background). The shell can hand back the plated one; this
// picks the right file instead. Pure (no WinRT, no files), so menu-app.Tests checks it; AppIcons
// finds the package, lists the files and decodes the one chosen.
public static class PackageLogo
{
    // Square44x44Logo is 44 pixels at 100% scale: scale-200 is 88.
    public const int BasePixels = 44;

    // The file chosen, as given (relative to the logo's folder), the pixels it's drawn for, and
    // whether it's an unplated one.
    public sealed record Choice(string File, int Pixels, bool Unplated);

    // What a package's manifest says about one of its apps' logo: its path in the package
    // (Assets\Square44x44Logo.png) and whether its plate is transparent (BackgroundColor).
    public sealed record Visual(string Logo, bool Plateless);

    // A Store app's id (AUMID) split into its package family name and the app's id in the
    // package: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App". Null for anything else (a path,
    // a desktop program's own AUMID such as "MSEdge").
    public static (string Family, string App)? SplitAumid(string id)
    {
        int bang = id.IndexOf('!');
        if (bang <= 0 || bang == id.Length - 1 || id.IndexOf('!', bang + 1) >= 0) return null;
        string family = id[..bang], app = id[(bang + 1)..];
        // <name>_<publisher id>: the name is letters, digits, dots and dashes; the publisher id is
        // 13 lowercase letters and digits.
        int under = family.LastIndexOf('_');
        if (under <= 0 || family.Length - under - 1 != 13) return null;
        foreach (char c in family[..under]) if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-') return null;
        foreach (char c in family[(under + 1)..]) if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterLower(c)) return null;
        foreach (char c in app) if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return null;
        return (family, app);
    }

    // The logo of the app `appId` in an AppxManifest.xml, or null when it names none (or the
    // manifest can't be read). Namespaces are ignored: VisualElements is uap's on Windows 10 and
    // 11, but the attribute has no namespace either way.
    public static Visual? Manifest(string xml, string appId)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var doc = XDocument.Load(reader);
            foreach (var app in doc.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                if (!string.Equals((string?)app.Attribute("Id"), appId, StringComparison.OrdinalIgnoreCase)) continue;
                var visual = app.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
                var logo = (string?)visual?.Attribute("Square44x44Logo");
                // A plain path inside the package only: not a resource reference or a way out.
                if (string.IsNullOrWhiteSpace(logo) || logo.Contains(':') || logo.StartsWith('\\') || logo.StartsWith('/')
                    || logo.Split('\\', '/').Any(part => part == "..")) return null;
                var background = (string?)visual!.Attribute("BackgroundColor");
                return new Visual(logo, string.Equals(background, "transparent", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (XmlException) { }
        return null;
    }

    // The qualifiers a resource file's name or folder may carry (MRT's list). Anything else in
    // the name (Square44x44Logo.old.png) means it isn't a variant of the logo.
    static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "scale", "targetsize", "altform", "contrast", "theme", "lang", "language", "layoutdir",
        "devicefamily", "homeregion", "configuration", "dxfeaturelevel",
    };

    // Whether a folder's name is only qualifiers (scale-200, contrast-standard,
    // lang-en-us_scale-100): one where variants of the logo can also live.
    public static bool IsQualifierFolder(string name) => Qualifiers(name) != null;

    // "targetsize-24_altform-unplated" → its qualifiers, names lowercased; null when a part isn't
    // name-value with a known name.
    static List<(string Name, string Value)>? Qualifiers(string text)
    {
        var list = new List<(string, string)>();
        foreach (var part in text.Split('_'))
        {
            int dash = part.IndexOf('-');
            if (dash <= 0 || dash == part.Length - 1 || !Known.Contains(part[..dash])) return null;
            list.Add((part[..dash].ToLowerInvariant(), part[(dash + 1)..].ToLowerInvariant()));
        }
        return list;
    }

    // The best of a logo's files for drawing `want` pixels wide, on a light or dark background.
    // `files` are relative to the logo's folder ("Square44x44Logo.targetsize-48.png",
    // "scale-200\Square44x44Logo.png"); `logo` is the manifest's file name (Square44x44Logo.png).
    //
    // In order: the unplated one for this background (light: altform-lightunplated, then
    // altform-unplated; dark: altform-unplated), then, only when the app's plate is transparent
    // (`plateless`), the plain ones, which otherwise expect their plate behind them (often a white
    // glyph) and are better left to the shell, which draws it. Within each, targetsize-N before
    // scale-S. Of the first kind that has any, the smallest at or above `want`, else the largest.
    // High-contrast variants, the other theme's, and other altforms are never chosen.
    // Null when nothing fits: the shell's icon then.
    public static Choice? Best(IEnumerable<string> files, string logo, int want, bool light, bool plateless)
    {
        string stem = Path.GetFileNameWithoutExtension(logo), ext = Path.GetExtension(logo);
        var candidates = new List<(int Kind, int Pixels, int Extra, string File, bool Unplated)>();
        foreach (var file in files)
        {
            var parts = file.Split('\\', '/');
            var name = parts[^1];
            if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || !name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
            var middle = name[stem.Length..^ext.Length];
            var qualifiers = new List<(string Name, string Value)>();
            bool ok = true;
            foreach (var folder in parts[..^1])
                if (Qualifiers(folder) is { } q) qualifiers.AddRange(q); else ok = false;
            if (middle.Length > 0)
            {
                if (middle[0] != '.' || Qualifiers(middle[1..]) is not { } q) ok = false;
                else qualifiers.AddRange(q);
            }
            if (!ok) continue;

            int? target = null, scale = null;
            string altform = "";
            int extra = 0; // qualifiers that say nothing we choose by (a language): fewer is better
            foreach (var (n, v) in qualifiers)
                switch (n)
                {
                    case "targetsize": target = int.TryParse(v, out var t) && t > 0 ? t : -1; break;
                    case "scale": scale = int.TryParse(v, out var s) && s > 0 ? s : -1; break;
                    case "altform": altform = v; break;
                    case "contrast": if (v != "standard") ok = false; break;
                    case "theme": if (v != (light ? "light" : "dark")) ok = false; break;
                    default: extra++; break;
                }
            if (!ok || target == -1 || scale == -1) continue;

            // Kinds, best first; -1 is never chosen.
            int look = altform switch
            {
                "lightunplated" => light ? 0 : -1,
                "unplated" => light ? 1 : 0,
                "" => plateless ? 2 : -1,
                _ => -1,
            };
            if (look < 0) continue;
            int pixels = target ?? (int)Math.Round(BasePixels * (scale ?? 100) / 100.0);
            candidates.Add((look * 2 + (target != null ? 0 : 1), pixels, extra, file, altform != ""));
        }
        if (candidates.Count == 0) return null;

        int kind = candidates.Min(c => c.Kind);
        var same = candidates.Where(c => c.Kind == kind).ToList();
        var big = same.Where(c => c.Pixels >= want).ToList();
        var best = big.Count > 0
            ? big.OrderBy(c => c.Pixels).ThenBy(c => c.Extra).ThenBy(c => c.File, StringComparer.Ordinal).First()
            : same.OrderByDescending(c => c.Pixels).ThenBy(c => c.Extra).ThenBy(c => c.File, StringComparer.Ordinal).First();
        return new Choice(best.File, best.Pixels, best.Unplated);
    }
}
