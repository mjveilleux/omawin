using System.Text.Json;

namespace SystemMenu.Tests.Look;

// The community theme index (Community.cs): reading omarchy.org/themes' list, naming a theme as
// `omawin theme install` would, and turning its colors.toml or alacritty.toml into the palette
// and theme colors convert-theme.ps1 makes. All from fixtures; nothing here touches the network.
public class CommunityTests
{
    // Two of the page's cards as the site writes them (one line, Astro's attributes), a theme
    // whose repo isn't named omarchy-*-theme, an HTML entity in a name, and a link that isn't a repo.
    const string Page =
        """<ul class="grid"><li><a href="https://github.com/leaflessbranch/omarchy-abyssal-cartographer-theme" class="group block focus-visible:outline-2"><img src="/assets/themes/abyssal-cartographer.webp" alt="Abyssal Cartographer theme screenshot" width="1200" height="675" loading="lazy" decoding="async" class="img-outlined"/><span class="mt-2.5 block">Abyssal Cartographer</span></a></li>"""
        + """<li><a href="https://github.com/JJDizz1L/aetheria" class="group block"><img src="/assets/themes/aetheria.webp" alt="Aetheria theme screenshot" width="1200" height="675"/><span>Aetheria</span></a></li>"""
        + """<li><a href="https://github.com/someone/omarchy-rock-n-roll-theme.git" class="group"><img src="/assets/themes/rock-n-roll.webp" alt="Rock &amp; Roll theme screenshot"/></a></li>"""
        + """<li><a href="https://github.com/an-org" class="group"><img src="/assets/themes/org.webp" alt="Org theme screenshot"/></a></li></ul>""";

    [Fact]
    public void Page_lists_every_card_with_its_repo_and_screenshot()
    {
        var themes = CommunityList.Parse(Page);
        Assert.Equal(3, themes.Count);
        Assert.Equal(("Abyssal Cartographer", "leaflessbranch/omarchy-abyssal-cartographer-theme", "https://omarchy.org/assets/themes/abyssal-cartographer.webp"), themes[0]);
        Assert.Equal(("Aetheria", "JJDizz1L/aetheria", "https://omarchy.org/assets/themes/aetheria.webp"), themes[1]);
        Assert.Equal(("Rock & Roll", "someone/omarchy-rock-n-roll-theme", "https://omarchy.org/assets/themes/rock-n-roll.webp"), themes[2]);
    }

    [Theory]
    [InlineData("https://github.com/someone/omarchy-nord-theme", "someone/omarchy-nord-theme")]
    [InlineData("https://github.com/someone/omarchy-nord-theme/", "someone/omarchy-nord-theme")]
    [InlineData("https://github.com/someone/omarchy-nord-theme.git", "someone/omarchy-nord-theme")]
    [InlineData("https://github.com/someone/omarchy-nord-theme/tree/main", "someone/omarchy-nord-theme")]
    [InlineData("someone/omarchy-nord-theme", "someone/omarchy-nord-theme")]
    [InlineData("  someone/repo  ", "someone/repo")]
    [InlineData("https://gitlab.com/someone/omarchy-nord-theme", null)]
    [InlineData("https://github.com/someone", null)]
    [InlineData("-bad/repo", null)]
    [InlineData("someone/..", null)]
    [InlineData("some one/repo", null)]
    public void Repo_from_a_link_or_slug(string text, string? repo) => Assert.Equal(repo, CommunityList.RepoOf(text));

    [Fact]
    public void Extra_list_skips_comments_and_blank_lines() =>
        Assert.Equal(["a/omarchy-x-theme", "b/y"],
            CommunityList.Extra(["# hand-kept", "", "a/omarchy-x-theme  # a note", "https://github.com/b/y", "not a repo"]));

    // As themes/theme.ps1's Get-ThemeName names an installed theme's link.
    [Theory]
    [InlineData("someone/omarchy-everforest-dark-theme", "everforest-dark")]
    [InlineData("JJDizz1L/aetheria", "aetheria")]
    [InlineData("someone/Omarchy-Neon-Theme", "neon")]
    [InlineData("someone/omarchy-x.y_z+1-theme.git", "x.y_z+1")]
    [InlineData("someone/omarchy-a..b-theme", null)]
    [InlineData("someone/omarchy--theme", null)]
    public void Theme_name_from_the_repo(string repo, string? name) => Assert.Equal(name, CommunityList.ThemeName(repo));

    static CommunityTheme Entry(string name, string author, string mode, int stars) =>
        new() { Name = name, Title = name, Author = author, Repo = $"{author}/omarchy-{name}-theme", Mode = mode, Stars = stars };

    [Theory]
    [InlineData("", true)]
    [InlineData("nord", true)]
    [InlineData("NORD frost", true)]
    [InlineData("someone", true)]
    [InlineData("light", true)]
    [InlineData("dark", false)]
    [InlineData("nord gruvbox", false)]
    public void Search_needs_every_word(string query, bool matches) =>
        Assert.Equal(matches, CommunityList.Matches(Entry("nord-frost", "someone", "light", 3), query));

    [Fact]
    public void Most_stars_first_then_title() =>
        Assert.Equal(["c", "a", "b"],
            CommunityList.Ordered([Entry("b", "x", "dark", 5), Entry("c", "x", "dark", 9), Entry("a", "x", "dark", 5)]).Select(t => t.Name));

    [Theory]
    [InlineData("#ABC123", "#abc123")]
    [InlineData("abc123", "#abc123")]
    [InlineData("0xabc123", "#abc123")]
    [InlineData("#abc123ff", "#abc123")]
    [InlineData("#abc", null)]
    [InlineData("rgb(1,2,3)", null)]
    [InlineData("#abc123; rm -rf", null)]
    public void Hex_colors_only(string value, string? hex) => Assert.Equal(hex, ThemePalette.Hex(value));

    [Fact]
    public void Colors_toml_keeps_only_colors_and_mode()
    {
        var c = ThemePalette.FromColorsToml("""
            # a comment
            mode = "light"
            accent = '#AABBCC'
            background = 112233 # unquoted, with a comment
            foreground = "#ddeeff"
            font = "JetBrains Mono"
            theme_type = "sepia"
            evil = "#123456\"; echo"
            """.Replace("\n", "\r\n"));
        Assert.Equal("light", c["mode"]);
        Assert.Equal("#aabbcc", c["accent"]);
        Assert.Equal("#112233", c["background"]);
        Assert.Equal("#ddeeff", c["foreground"]);
        Assert.False(c.ContainsKey("font"));
        Assert.False(c.ContainsKey("theme_type"));
        Assert.False(c.ContainsKey("evil"));
    }

    const string Alacritty = """
        [colors.primary]
        background = "#1d2021"
        foreground = "#d4be98"

        [colors.normal]
        black = "#32302f"
        red = "#ea6962"
        green = "#a9b665"
        yellow = "#d8a657"
        blue = "#7daea3"
        magenta = "#d3869b"
        cyan = "#89b482"
        white = "#d4be98"

        [colors.bright]
        red = "#ff7f7f"

        [colors.selection]
        background = "#504945"
        """;

    [Fact]
    public void Alacritty_maps_to_the_numbered_palette()
    {
        var c = ThemePalette.FromAlacritty(Alacritty)!;
        Assert.Equal("#1d2021", c["background"]);
        Assert.Equal("#d4be98", c["foreground"]);
        Assert.Equal("#ea6962", c["color1"]);
        Assert.Equal("#ff7f7f", c["color9"]);
        Assert.Equal("#a9b665", c["color10"]); // no bright green: the normal one
        Assert.Equal("#504945", c["selection"]);
        Assert.Equal("#7daea3", c["accent"]);
    }

    [Fact]
    public void Alacritty_without_all_eight_colors_is_no_palette() =>
        Assert.Null(ThemePalette.FromAlacritty("[colors.normal]\nblack = \"#000000\"\nred = \"#ff0000\"\n"));

    [Fact]
    public void Alacritty_dotted_keys_under_colors()
    {
        var c = ThemePalette.FromAlacritty("""
            [colors]
            normal.black = "#000000"
            normal.red = "#110000"
            normal.green = "#001100"
            normal.yellow = "#111100"
            normal.blue = "#000011"
            normal.magenta = "#110011"
            normal.cyan = "#001111"
            normal.white = "#eeeeee"
            """)!;
        Assert.Equal("#000011", c["color4"]);
        Assert.Equal("#000000", c["background"]); // color0
        Assert.Equal("#eeeeee", c["foreground"]); // color7
    }

    [Fact]
    public void Resolve_fills_in_what_an_alacritty_theme_lacks()
    {
        var p = ThemePalette.Resolve(ThemePalette.FromAlacritty(Alacritty)!, lightMode: false);
        Assert.Equal("dark", p["mode"]);
        Assert.Equal("#ea6962", p["red"]);
        Assert.Equal("#ff7f7f", p["bright_red"]);
        Assert.Equal("#d8a657", p["orange"]);              // yellow
        Assert.Equal(ThemePalette.Mix("#d8a657", "#000000", 0.5), p["brown"]);
        Assert.Equal(ThemePalette.Mix("#1d2021", "#000000", 0.25), p["dark_background"]);
        Assert.Equal("#d4be98", p["bright_foreground"]);   // color15 = white, no bright one
        Assert.Equal("#32302f", p["dark_foreground"]);     // color8 = black, no bright one
        Assert.Equal(p["dark_foreground"], p["muted"]);
        Assert.All(ThemePalette.Order, k => Assert.True(p.ContainsKey(k), k));
    }

    [Theory]
    [InlineData("#f0f0f0", false, "light")] // a light background
    [InlineData("#202020", true, "light")]  // light.mode wins
    [InlineData("#202020", false, "dark")]
    public void Mode_from_the_background_or_light_mode(string background, bool lightMode, string mode) =>
        Assert.Equal(mode, ThemePalette.Resolve(new Dictionary<string, string> { ["background"] = background, ["foreground"] = "#808080" }, lightMode)["mode"]);

    [Fact]
    public void A_palette_with_no_background_throws() =>
        Assert.Throws<InvalidDataException>(() => ThemePalette.Resolve(new Dictionary<string, string> { ["accent"] = "#ff0000" }, false));

    [Theory]
    [InlineData("#000000", "#ffffff", 0.2, "#333333")]
    [InlineData("#ff8000", "#000000", 0.5, "#804000")]
    [InlineData("#123456", "#ffffff", 0, "#123456")]
    public void Mix_rounds_like_convert_theme(string a, string b, double amount, string mixed) =>
        Assert.Equal(mixed, ThemePalette.Mix(a, b, amount));

    // The cross-check with convert-theme.ps1: every theme this repo ships was made by it, its
    // palette in colors/<name>.toml and its colors in <name>.json. Reading that palette again here
    // must give the same colors, so a community theme's swatch and preview are what installing it
    // shows. Left out: tokyo-night, whose JSON is made by hand (convert-theme.ps1 -KeepJson), and
    // solitude, whose files predate the orange fallback (its palette has no orange line).
    public static TheoryData<string> ShippedThemes()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(Settings.CatalogTests.RepoRoot(), "themes", "colors"), "*.toml").Order())
            if (Path.GetFileNameWithoutExtension(f) is not ("tokyo-night" or "solitude")) data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedThemes))]
    public void Theme_colors_match_convert_theme(string name)
    {
        var themes = Path.Combine(Settings.CatalogTests.RepoRoot(), "themes");
        var palette = ThemePalette.Resolve(ThemePalette.FromColorsToml(File.ReadAllText(Path.Combine(themes, "colors", name + ".toml"))), false);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(themes, name + ".json")));
        foreach (var (key, value) in ThemePalette.ThemeColors(palette))
            Assert.True(string.Equals(json.RootElement.GetProperty(key).GetString(), value, StringComparison.OrdinalIgnoreCase), $"{name}.{key}: {value}, convert-theme.ps1 made {json.RootElement.GetProperty(key).GetString()}");
        Assert.Equal(json.RootElement.GetProperty("gradient").EnumerateArray().Select(e => e.GetString()!.ToLowerInvariant()), ThemePalette.Gradient(palette));
    }

    [Fact]
    public void Index_round_trips_as_json()
    {
        var index = new CommunityIndex
        {
            Generated = "2026-10-31T03:17:00Z",
            Themes = [new CommunityTheme { Name = "nord-frost", Title = "Nord Frost", Repo = "a/omarchy-nord-frost-theme", Commit = new string('a', 40),
                Palette = new() { ["accent"] = "#81a1c1" }, Backgrounds = ["https://raw.githubusercontent.com/a/b/c/backgrounds/1.jpg"] }],
        };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<CommunityIndex>(JsonSerializer.Serialize(index, options), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("Nord Frost", back.Themes[0].Title);
        Assert.Equal("#81a1c1", back.Themes[0].Palette["accent"]);
        Assert.Single(back.Themes[0].Backgrounds);
    }
}
