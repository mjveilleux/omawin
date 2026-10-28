namespace SystemMenu;

// The terminal's ANSI palette, read from themes/colors/<name>.toml (Omarchy's own colors.toml).
// The About window draws what fastfetch draws, and fastfetch is a terminal program: its logo is
// ANSI 2 (green), the Hardware keys 2, the Software keys 4 (blue), the Age keys 5 (magenta), the
// box lines 90 (bright black, the palette's dark_foreground) and the sheen's band bright white
// (ANSI 15, bright_foreground). So the palette has to come from the file the terminal reads, not
// from themes/<name>.json: the two disagree on some themes (Tokyo Night's magenta is #ad8ee6 in
// the terminal and #bb9af7 in the JSON), and the fastfetch render is the reference.
//
// The window's own colours come from here too: its glass is the palette's background, and its
// 1px border is the palette's accent, which is what GlazeWM paints a focused window's border in
// (glazewm/config.yaml is rendered from the same file).
public sealed class AnsiPalette
{
    public string Mode { get; private set; } = "dark";
    public string Background { get; private set; } = "#282828";
    public string Foreground { get; private set; } = "#d4be98";    // ANSI 39, the default
    public string Accent { get; private set; } = "#7daea3";
    public string Green { get; private set; } = "#a9b665";         // ANSI 2
    public string Blue { get; private set; } = "#7daea3";          // ANSI 4
    public string Magenta { get; private set; } = "#d3869b";       // ANSI 5
    public string Red { get; private set; } = "#ea6962";           // ANSI 1
    public string Yellow { get; private set; } = "#d8a657";        // ANSI 3
    public string Cyan { get; private set; } = "#89b482";          // ANSI 6
    public string White { get; private set; } = "#bdae93";         // ANSI 7
    public string Gray { get; private set; } = "#7c6f64";          // ANSI 8, the boxes
    public string BrightWhite { get; private set; } = "#d4be98";   // ANSI 15, the sheen

    // The order fastfetch puts the theme's eight colours in, after the theme's name
    // (fastfetch/config.jsonc: 39, 37, 36, 35, 34, 33, 32, 31).
    public string[] Dots => [Foreground, White, Cyan, Magenta, Blue, Yellow, Green, Red];

    public static AnsiPalette Load(string theme)
    {
        var p = new AnsiPalette();
        var file = MenuModel.ThemeFile(Path.Combine("colors", theme + ".toml"));
        if (!File.Exists(file)) file = Path.Combine(MenuModel.ThemesDir, "colors", "starship.toml"); // omawin's default
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim().Trim('"');
                if (value.Length == 0) continue;
                switch (key)
                {
                    case "mode": p.Mode = value; break;
                    case "accent": if (value[0] == '#') p.Accent = value; break;
                    case "background": if (value[0] == '#') p.Background = value; break;
                    case "foreground": if (value[0] == '#') p.Foreground = value; break;
                    case "green": if (value[0] == '#') p.Green = value; break;
                    case "blue": if (value[0] == '#') p.Blue = value; break;
                    case "magenta": if (value[0] == '#') p.Magenta = value; break;
                    case "red": if (value[0] == '#') p.Red = value; break;
                    case "yellow": if (value[0] == '#') p.Yellow = value; break;
                    case "cyan": if (value[0] == '#') p.Cyan = value; break;
                    case "light_foreground": if (value[0] == '#') p.White = value; break;
                    case "dark_foreground": if (value[0] == '#') p.Gray = value; break;        // palette 8: "bright black"
                    case "bright_foreground": if (value[0] == '#') p.BrightWhite = value; break; // palette 15
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return p;
    }
}
