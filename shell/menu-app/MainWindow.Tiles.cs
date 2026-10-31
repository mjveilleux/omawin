using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SystemMenu;

// The home screens are tiles, bento style: the launcher's (Win+Space) has a big Terminal tile,
// wide Projects and Clipboard tiles that show what's in them, the other pages, a Menu bar and
// what you use most; the system menu's (Win+Alt+Space) has a big Theme tile, wide Install and
// Update tiles and the rest of its submenus. The theme picker is a grid of swatches, the
// background picker one of a theme's pictures. Typing, submenus and pages are lists
// (MainWindow.xaml.cs). The selected tile lifts: it grows a little, rises, and its icon fills
// with its color.
public sealed partial class MainWindow
{
    const double TileRowDip = 72, TitleRowDip = 22, TileGap = 6;
    const double SwatchRowDip = 100; // the theme picker's: room for a wallpaper-shaped picture

    sealed class TileSpec
    {
        public string Key = "";
        public string? BuiltIn;          // a launcher page's tile (it fills in its own details later)
        public string Style = "tile";    // tile | hero | wide | swatch | picture | audio | audiobox | chip
        public int Col, Row, ColSpan = 1, RowSpan = 1;
        public string Icon = "", Label = "";
        public string? Sub, Hint, AppId;
        public bool SubMono;
        public Color Color;
        public Theme? Swatch;
        public string? SwatchFile;       // the theme's file name, for its picture (ThemeThumbs)
        public string? Picture;          // a background picker tile's background, as its theme lists it
        public bool Current;
        public string? MeterKey;         // an audio tile's device or app (Audio.Peak); not a switch's
        public int Volume;
        public bool Muted;
        public object? Item;             // AudioDevice, AudioApp or Quick settings' QsTile: what Space and + / − work on
        public Action Run = () => { };
    }

    // Rows of four columns; a row is tiles or a section title.
    sealed class TileLayout
    {
        public readonly List<(double Height, string? Title)> Rows = [];
        public readonly List<TileSpec> Cells = [];
        public double Height => Rows.Sum(r => r.Height) + TileGap * Math.Max(0, Rows.Count - 1);

        // Adds tiles left to right, starting a new row when the next one doesn't fit.
        public void Flow(IEnumerable<TileSpec> cells, string? title = null, double rowHeight = TileRowDip)
        {
            var list = cells.ToList();
            if (list.Count == 0) return;
            if (title != null) Rows.Add((TitleRowDip, title));
            int col = 4;
            foreach (var c in list)
            {
                if (col + c.ColSpan > 4) { Rows.Add((rowHeight, null)); col = 0; }
                c.Row = Rows.Count - 1;
                c.Col = col;
                col += c.ColSpan;
                Cells.Add(c);
            }
        }

        // The big tile (two by two) on the left, and up to two wide ones beside it.
        public void Top(TileSpec hero, IEnumerable<TileSpec> wides)
        {
            int row = Rows.Count;
            Rows.Add((TileRowDip, null));
            Rows.Add((TileRowDip, null));
            hero.Row = row; hero.ColSpan = hero.RowSpan = 2;
            Cells.Add(hero);
            foreach (var w in wides.Take(2))
            {
                w.Row = row++; w.Col = 2; w.ColSpan = 2;
                Cells.Add(w);
            }
        }
    }

    sealed class TileView
    {
        public TileSpec Spec = null!;
        public Border Root = null!, IconTile = null!, Badge = null!;
        public Grid Inner = null!;
        public TextBlock Glyph = null!, BadgeText = null!;
        public TextBlock? SubText;
        public bool HasImage;
        // An audio tile's (MainWindow.Audio.cs): its volume, its bar, how loud it is now.
        public TextBlock? Percent;
        public Grid? Meter;
        public Border? MeterLevel, MeterLive;
        public ScaleTransform? IconScale;
        public SolidColorBrush? IconGlow; // its icon tile's color, changed in place as the sound moves
        public double Loudness;
    }

    readonly List<TileView> tiles = [];
    bool tileMode;                 // showing tiles (a home screen, the theme picker), not rows
    string? selectKey;             // the tile to select when going back to a home screen
    bool findingProjects;
    DateTime serversAsked;

    // --- what the home screens hold ------------------------------------------------------------
    Color KindColor(LaunchItem item) => Hex(item.BuiltIn == "menu" ? theme.Purple : item.BuiltIn != null ? theme.Cyan : theme.Accent);

    TileLayout LauncherHome()
    {
        var layout = new TileLayout();
        var own = launchItems.Where(i => !i.IsApp).ToList();
        var placed = new HashSet<LaunchItem>();
        TileSpec Spec(LaunchItem item, string style = "tile")
        {
            placed.Add(item);
            return new TileSpec { Key = item.Key, BuiltIn = item.BuiltIn, Style = style, Icon = item.Icon, Label = item.Name, Color = KindColor(item), AppId = item.IconKey, Run = () => Launch(item) };
        }
        // Your first item (Terminal) big; Projects and Clipboard wide, with what's in them.
        var first = own.FirstOrDefault(i => i.BuiltIn == null);
        if (first != null)
        {
            var hero = Spec(first, "hero");
            if (first.Command.Trim() == "omawin.exe launch terminal") { hero.Hint = "Win+Enter"; hero.Sub = "in the folder you're working in"; }
            var wides = new List<TileSpec>();
            foreach (var name in new[] { "projects", "clipboard" })
                if (own.FirstOrDefault(i => i.BuiltIn == name) is { } item)
                {
                    var w = Spec(item, "wide");
                    w.Sub = name == "projects" ? ProjectsSub() : ClipSub();
                    w.SubMono = name == "clipboard";
                    wides.Add(w);
                }
            layout.Top(hero, wides);
        }
        // The next four of your items, then the menu as a bar with what's in it.
        var row = own.Where(i => !placed.Contains(i) && i.BuiltIn != "menu").Take(4).Select(i => Spec(i)).ToList();
        if (own.FirstOrDefault(i => i.BuiltIn == "menu") is { } menu)
        {
            var bar = Spec(menu, "wide");
            bar.ColSpan = 4;
            bar.Sub = string.Join(" · ", MenuModel.Children(entries, "").Select(e => e.Label).Take(6));
            row.Add(bar);
        }
        layout.Flow(row);
        // What you open most (launch-history.json); before there's any, the rest in order.
        var history = History.Load();
        layout.Flow(launchItems.Where(i => !placed.Contains(i)).OrderByDescending(i => history.Boost(i.Key, "")).Take(8).Select(i => Spec(i)).ToList(), "Frequent");
        return layout;
    }

    string ProjectsSub() => projects is { Count: > 0 } p ? string.Join(" · ", p.Take(3).Select(x => x.Name)) : "your git repos";

    static string ClipSub() => ClipboardHistory.Entries() is { Count: > 0 } e ? OneLine(e[0], 60) : "nothing copied yet";

    TileLayout MenuHome()
    {
        var layout = new TileLayout();
        var top = MenuModel.Children(entries, "");
        var used = new HashSet<string>();
        int n = 0, count = top.Count + 1;
        TileSpec Spec(MenuEntry e, string style = "tile")
        {
            used.Add(e.Id);
            // Along the theme's gradient, so the screen isn't one color.
            return new TileSpec { Key = e.Id, Style = style, Icon = e.Icon, Label = e.Label, Color = GradientAt(n++ / (double)Math.Max(1, count - 1)), Run = () => Pick(e) };
        }
        string Peek(string id) => string.Join(" · ", MenuModel.Children(entries, id).Select(c => c.Label).Take(4));

        // The theme picker big, showing the theme you have; Install and Update wide.
        if (entries.Values.FirstOrDefault(e => e.Kind == "themes") is { } themes)
        {
            var hero = Spec(themes, "hero");
            hero.Color = Hex(theme.Accent);
            hero.Sub = theme.Name;
            hero.Swatch = theme;
            var wides = new[] { "install", "update" }.Where(entries.ContainsKey).Select(id => { var w = Spec(entries[id], "wide"); w.Sub = Peek(id); return w; }).ToList();
            layout.Top(hero, wides);
        }
        // The other submenus, Lock (used most from System), and System as a bar.
        var rest = top.Where(e => !used.Contains(e.Id) && e.Id != "system").Select(e => Spec(e)).ToList();
        if (entries.TryGetValue("system.lock", out var lockEntry)) rest.Add(Spec(lockEntry));
        if (entries.TryGetValue("system", out var system))
        {
            var bar = Spec(system, "wide");
            bar.ColSpan = Math.Max(1, 4 - rest.Count % 4);
            if (bar.ColSpan == 1) bar.Style = "tile";
            bar.Sub = string.Join(" · ", MenuModel.Children(entries, "system").Where(c => c.Id != "system.lock").Select(c => c.Label));
            rest.Add(bar);
        }
        layout.Flow(rest);
        return layout;
    }

    TileLayout ThemePicker(string query)
    {
        var layout = new TileLayout();
        layout.Flow(MenuModel.Themes().Where(t => query.Length == 0 || t.Theme.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(t => new TileSpec
            {
                Key = "theme:" + t.File, Style = "swatch", Label = t.Theme.Name, Swatch = t.Theme, SwatchFile = t.File, Current = t.File == themeName,
                Color = Hex(t.Theme.Accent), Run = () => SetTheme(t.File),
            }), rowHeight: SwatchRowDip);
        return layout;
    }

    // The background picker (Style › Backgrounds, omawin theme backgrounds): every background of
    // one theme, yours unless Tab moved on, as a grid of its pictures (ThemeThumbs: the release
    // brings them all), the one on your desktop checked. Typing narrows it by the picture's name.
    TileLayout BackgroundPicker(string file, Theme shown, string query)
    {
        var layout = new TileLayout();
        int on = CurrentBackground(file, shown);
        layout.Flow(shown.Backgrounds.Select((source, i) => (Source: source, Index: i))
            .Where(b => query.Length == 0 || ThemePictures.Matches(b.Source, query))
            .Select(b => new TileSpec
            {
                Key = BackgroundKey(file, b.Source), Style = "picture", Label = ThemePictures.Label(b.Source),
                Swatch = shown, SwatchFile = file, Picture = b.Source, Current = b.Index == on,
                Color = Hex(shown.Accent), Run = () => SetBackground(file, b.Source),
            }), rowHeight: SwatchRowDip);
        return layout;
    }

    static string BackgroundKey(string file, string source) => $"background:{file}:{ThemePictures.Name(source)}";

    // Which of a theme's backgrounds is on the desktop, -1 for none: the one just picked here,
    // else, for your theme, the one set-theme.ps1 wrote down (launcher\background).
    int CurrentBackground(string file, Theme shown)
    {
        if (pickedBackground is { } picked) return picked.Theme == file ? Array.IndexOf(shown.Backgrounds, picked.Source) : -1;
        if (file != themeName) return -1;
        string? saved = null;
        try { saved = File.ReadAllText(Path.Combine(MenuModel.StateDir, "background")); } catch { }
        return ThemePictures.Current(shown.Backgrounds, saved);
    }

    // Opens the background picker on a theme's backgrounds, on the one on the desktop.
    void OpenBackgrounds(string file)
    {
        backgroundsOf = file;
        selected = 0;
        var shown = file == themeName ? theme : MenuModel.LoadTheme(file);
        int on = CurrentBackground(file, shown);
        selectKey = on >= 0 ? BackgroundKey(file, shown.Backgrounds[on]) : null;
    }

    // Tab / Shift+Tab in the background picker: the next / previous theme's backgrounds, in the
    // theme picker's order, skipping themes without any.
    void CycleBackgroundTheme(int by)
    {
        if (backgroundsOf == null) return;
        var files = MenuModel.Themes().Where(t => t.Theme.Backgrounds.Length > 0).Select(t => t.File).ToList();
        if (files.Count == 0) return;
        int at = files.FindIndex(f => f.Equals(backgroundsOf, StringComparison.OrdinalIgnoreCase));
        OpenBackgrounds(files[at < 0 ? 0 : ((at + by) % files.Count + files.Count) % files.Count]);
        if (Input.Text.Length > 0) Input.Text = ""; // a search of one theme's names means nothing in the next
        Render();
    }

    // A theme by its file name or its own name ("gruvbox", "Tokyo Night"), for
    // `omawin theme backgrounds <theme>`; null when it's none of them.
    static string? ThemeFileNamed(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : MenuModel.Themes().Where(t => t.File.Equals(name, StringComparison.OrdinalIgnoreCase) || t.Theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(t => (string?)t.File).FirstOrDefault();

    // How tall the window's body is while it's showing: the home screen it opens on (lists get
    // the same room), so it doesn't resize as you move around.
    // The layout is kept for the Render that follows, so it's built once per showing (the
    // launcher's reads the clipboard and launch history).
    double HomeBodyDip(bool launcher)
    {
        var layout = launcher ? LauncherHome() : MenuHome();
        builtHome = (launcher, layout);
        return layout.Height + 16 + 4; // + the window's frame
    }

    (bool Launcher, TileLayout Layout)? builtHome;

    // The home layout HomeBodyDip just built, if it's that one; else a fresh one.
    TileLayout Home(bool launcher, (bool Launcher, TileLayout Layout)? built) =>
        built is { } b && b.Launcher == launcher ? b.Layout : launcher ? LauncherHome() : MenuHome();

    // The launcher home's live details: projects (the background index's latest, kept current
    // as repos come and go) and how many servers are running (asked at most every 10 s).
    void LoadHomeDetails()
    {
        if (Projects.Latest is { } latest && !ReferenceEquals(latest, projects))
        {
            projects = latest;
            if (tiles.FirstOrDefault(v => v.Spec.BuiltIn == "projects")?.SubText is { } sub) sub.Text = ProjectsSub();
        }
        else if (projects == null && !findingProjects)
        {
            findingProjects = true;
            Projects.Find().ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                findingProjects = false;
                if (t.IsCompletedSuccessfully && projects == null) projects = t.Result;
                if (tiles.FirstOrDefault(v => v.Spec.BuiltIn == "projects")?.SubText is { } sub) sub.Text = ProjectsSub();
            }));
        }
        if (DateTime.Now - serversAsked > TimeSpan.FromSeconds(10))
        {
            serversAsked = DateTime.Now;
            Task.Run(Localhost.FindAsync).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                if (t.IsCompletedSuccessfully) servers ??= t.Result;
                ShowServerCount();
            }));
        }
        ShowServerCount();
    }

    void ShowServerCount()
    {
        if (tiles.FirstOrDefault(v => v.Spec.BuiltIn == "localhost") is not { } v) return;
        int count = servers?.Count ?? 0;
        v.BadgeText.Text = count.ToString();
        v.Badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- drawing them ----------------------------------------------------------------------------
    void RenderTiles(TileLayout layout)
    {
        tiles.Clear();
        Tiles.Children.Clear();
        Tiles.RowDefinitions.Clear();
        if (Tiles.ColumnDefinitions.Count == 0)
            for (int c = 0; c < 4; c++) Tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < layout.Rows.Count; r++)
        {
            var (height, title) = layout.Rows[r];
            Tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            if (title == null) continue;
            var t = new TextBlock
            {
                Text = title.ToUpperInvariant(), FontFamily = textFont, FontSize = 10.5, FontWeight = FontWeights.SemiBold, CharacterSpacing = 80,
                Foreground = Brush(theme.Comment), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0),
            };
            Grid.SetRow(t, r);
            Grid.SetColumnSpan(t, 4);
            Tiles.Children.Add(t);
        }
        foreach (var spec in layout.Cells)
        {
            var v = MakeTile(spec);
            Grid.SetRow(v.Root, spec.Row);
            Grid.SetColumn(v.Root, spec.Col);
            Grid.SetRowSpan(v.Root, spec.RowSpan);
            Grid.SetColumnSpan(v.Root, spec.ColSpan);
            if (spec.Style == "chip") v.Root.Margin = new Thickness(spec.Col == 0 ? 10 : 3, 0, spec.Col == 3 ? 10 : 3, 8);
            Tiles.Children.Add(v.Root);
            tiles.Add(v);
        }
    }

    TileView MakeTile(TileSpec s)
    {
        var v = new TileView { Spec = s };
        double size = s.Style switch { "hero" => 50, "wide" => 38, "chip" => 26, _ => 36 };
        v.Glyph = new TextBlock { Text = s.Icon, FontFamily = iconFont, FontSize = size * 0.5, Foreground = new SolidColorBrush(s.Color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var image = new Image { Width = size - 4, Height = size - 4, Visibility = Visibility.Collapsed };
        var inIcon = new Grid();
        inIcon.Children.Add(v.Glyph);
        inIcon.Children.Add(image);
        v.IconTile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = Brush(s.Color, 0x29), Child = inIcon };
        if (s.AppId != null)
            AppIcons.Get(s.AppId, size - 4, IconScaleNow, (img, units) =>
            {
                v.HasImage = true;
                v.IconTile.Background = new SolidColorBrush(Colors.Transparent);
                image.Source = img;
                image.Width = image.Height = units;   // one icon pixel on one screen pixel
                image.Visibility = Visibility.Visible;
                v.Glyph.Visibility = Visibility.Collapsed;
            });

        var label = new TextBlock { Text = s.Label, FontFamily = textFont, Foreground = Brush(theme.Foreground), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        TextBlock Sub(string text) => v.SubText = new TextBlock
        {
            Text = text, FontFamily = s.SubMono ? monoFont : textFont, FontSize = s.SubMono ? 11.5 : 12, Foreground = Brush(theme.Comment),
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
        };
        v.Inner = new Grid();
        ElementCompositionPreview.SetIsTranslationEnabled(v.Inner, true);
        switch (s.Style)
        {
            case "hero":
            {
                // A glow of its color from the top right corner.
                var glow = new LinearGradientBrush { StartPoint = new(1, 0), EndPoint = new(0.35, 0.9) };
                glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x3A, s.Color.R, s.Color.G, s.Color.B), Offset = 0 });
                glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, s.Color.R, s.Color.G, s.Color.B), Offset = 1 });
                v.Inner.Children.Add(new Border { Background = glow, CornerRadius = new CornerRadius(11) });
                var body = new Grid { Padding = new Thickness(16) };
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                v.IconTile.HorizontalAlignment = HorizontalAlignment.Left;
                v.IconTile.VerticalAlignment = VerticalAlignment.Top;
                body.Children.Add(v.IconTile);
                if (s.Hint != null)
                    body.Children.Add(new TextBlock { Text = s.Hint, FontFamily = monoFont, FontSize = 11, Foreground = Brush(theme.Comment), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
                label.FontSize = 19;
                label.FontWeight = FontWeights.SemiBold;
                var text = new StackPanel { Spacing = 2 };
                text.Children.Add(label);
                if (s.Sub != null) text.Children.Add(Sub(s.Sub));
                if (s.Swatch is { } sw)
                {
                    // The theme's colors, as it is now.
                    var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
                    foreach (var c in (sw.Gradient.Length > 0 ? sw.Gradient : [sw.Accent]).Take(5))
                        bars.Children.Add(new Border { Width = 22, Height = 6, CornerRadius = new CornerRadius(3), Background = Brush(c) });
                    text.Children.Add(bars);
                }
                Grid.SetRow(text, 1);
                body.Children.Add(text);
                v.Inner.Children.Add(body);
                break;
            }
            case "wide":
            {
                var glow = new LinearGradientBrush { StartPoint = new(1, 0), EndPoint = new(0.4, 1) };
                glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x22, s.Color.R, s.Color.G, s.Color.B), Offset = 0 });
                glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, s.Color.R, s.Color.G, s.Color.B), Offset = 1 });
                v.Inner.Children.Add(new Border { Background = glow, CornerRadius = new CornerRadius(11) });
                var body = new Grid { Padding = new Thickness(14, 0, 14, 0), ColumnSpacing = 12 };
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                v.IconTile.VerticalAlignment = VerticalAlignment.Center;
                body.Children.Add(v.IconTile);
                label.FontSize = 14;
                label.FontWeight = FontWeights.SemiBold;
                var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(label);
                if (s.Sub != null) text.Children.Add(Sub(s.Sub));
                Grid.SetColumn(text, 1);
                body.Children.Add(text);
                v.Inner.Children.Add(body);
                break;
            }
            case "swatch":
            {
                // The theme: its first wallpaper (until that's loaded, its background with "Aa"
                // in its text color), four of its colors along the bottom, and a check on the
                // one you have.
                var t = s.Swatch!;
                var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
                var under = Hex(t.Background);
                shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, under.R, under.G, under.B), Offset = 0.45 });
                shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xD0, under.R, under.G, under.B), Offset = 1 });
                var box = new Grid { Padding = new Thickness(7, 5, 7, 6) };
                var aa = new TextBlock { Text = "Aa", FontFamily = textFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(t.Foreground) };
                box.Children.Add(aa);
                if (s.Current)
                    box.Children.Add(new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brush(t.Background), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock { Text = char.ConvertFromUtf32(0xF05E0), FontFamily = iconFont, FontSize = 15, Foreground = Brush(t.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    });
                var bars = new Grid { ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
                string[] colors = [t.Accent, t.Purple, t.Cyan, t.Yellow];
                for (int i = 0; i < colors.Length; i++)
                {
                    bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush(colors[i]) };
                    Grid.SetColumn(bar, i);
                    bars.Children.Add(bar);
                }
                box.Children.Add(bars);
                var shaded = new Border { Background = shade, CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed };
                var inFrame = new Grid();
                inFrame.Children.Add(shaded);
                inFrame.Children.Add(box);
                var frame = new Border { Background = Brush(t.Background), BorderBrush = Brush(t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = inFrame };
                if (s.SwatchFile != null)
                    ThemeThumbs.Get(s.SwatchFile, t, picture =>
                    {
                        frame.Background = new ImageBrush { ImageSource = picture, Stretch = Stretch.UniformToFill };
                        shaded.Visibility = Visibility.Visible;
                        aa.Visibility = Visibility.Collapsed;
                    });
                var body = new Grid { Padding = new Thickness(6, 6, 6, 4), RowSpacing = 4 };
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Children.Add(frame);
                label.FontSize = 12;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                if (s.Current) { label.Foreground = Brush(theme.Accent); label.FontWeight = FontWeights.SemiBold; }
                Grid.SetRow(label, 1);
                body.Children.Add(label);
                v.Inner.Children.Add(body);
                break;
            }
            case "picture":
            {
                // One of a theme's backgrounds: its picture (until that's loaded, the theme's
                // background with a picture glyph), a check and an accent ring on the one on your
                // desktop, its name under it.
                var t = s.Swatch!;
                var placeholder = new TextBlock
                {
                    Text = char.ConvertFromUtf32(0xF02E9), FontFamily = iconFont, FontSize = 20, Foreground = Brush(t.Comment),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                var inFrame = new Grid();
                inFrame.Children.Add(placeholder);
                if (s.Current)
                    inFrame.Children.Add(new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brush(t.Background), Margin = new Thickness(0, 5, 5, 0),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock { Text = char.ConvertFromUtf32(0xF05E0), FontFamily = iconFont, FontSize = 15, Foreground = Brush(t.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    });
                var frame = new Border
                {
                    Background = Brush(t.Background), BorderBrush = Brush(s.Current ? t.Accent : t.Border), BorderThickness = new Thickness(s.Current ? 2 : 1),
                    CornerRadius = new CornerRadius(8), Child = inFrame,
                };
                if (s.SwatchFile != null && s.Picture != null)
                    ThemeThumbs.Get(s.SwatchFile, t, s.Picture, picture =>
                    {
                        frame.Background = new ImageBrush { ImageSource = picture, Stretch = Stretch.UniformToFill };
                        placeholder.Visibility = Visibility.Collapsed;
                    });
                var body = new Grid { Padding = new Thickness(6, 6, 6, 4), RowSpacing = 4 };
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Children.Add(frame);
                label.FontSize = 12;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                if (s.Current) { label.Foreground = Brush(theme.Accent); label.FontWeight = FontWeights.SemiBold; }
                Grid.SetRow(label, 1);
                body.Children.Add(label);
                v.Inner.Children.Add(body);
                break;
            }
            case "audio":
            case "audiobox":
                BuildAudioTile(v, s, label);
                break;
            case "chip":
                BuildChip(v, s, label);
                break;
            default:
            {
                var body = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
                v.IconTile.HorizontalAlignment = HorizontalAlignment.Center;
                body.Children.Add(v.IconTile);
                label.FontSize = 12.5;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                body.Children.Add(label);
                v.Inner.Children.Add(body);
                break;
            }
        }
        // A count in the corner (Localhost: servers running).
        v.BadgeText = new TextBlock { FontFamily = monoFont, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(theme.Background) };
        v.Badge = new Border
        {
            Background = Brush(theme.Accent), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 7, 7, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Child = v.BadgeText,
        };
        v.Inner.Children.Add(v.Badge);

        v.Root = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = v.Inner,
            Background = Brush(Hex(theme.Foreground), 0x0A), BorderBrush = new SolidColorBrush(Colors.Transparent),
            BackgroundTransition = new BrushTransition { Duration = Motion.Or0(Motion.Quick) },
            ScaleTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) },
            TranslationTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) },
        };
        v.Root.SizeChanged += (_, e) => v.Root.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
        int index = tiles.Count;
        v.Root.PointerEntered += (_, _) => { if (tileMode && !RecentlyMovedByKey && index < tiles.Count && selected != index) { selected = index; Highlight(); } };
        v.Root.Tapped += (_, _) => { if (tileMode && index < tiles.Count) { selected = index; Highlight(); s.Run(); } };
        return v;
    }

    // The selected tile lifts: lighter, ringed in the accent, a little bigger and higher, its
    // icon filled with its color.
    void LiftTile(TileView v, bool on)
    {
        bool box = v.Spec.Style == "audiobox", chip = v.Spec.Style == "chip";
        v.Root.Background = Brush(Hex(theme.Foreground), on ? (byte)0x1C : (byte)0x0A);
        // An output's box keeps an outline in its color, so what's inside it reads as its.
        v.Root.BorderBrush = on ? Brush(Hex(theme.Accent), 0x80) : box ? Brush(v.Spec.Color, 0x55) : new SolidColorBrush(Colors.Transparent);
        float grow = box ? 1 : v.Spec.RowSpan > 1 || v.Spec.ColSpan > 2 ? 1.015f : 1.04f; // big tiles grow less, a box not at all
        v.Root.Scale = on ? new Vector3(grow, grow, 1) : Vector3.One;
        v.Root.Translation = on && !box ? new Vector3(0, -2, 0) : Vector3.Zero;
        Canvas.SetZIndex(v.Root, (chip ? 2 : 0) + (on ? 1 : 0)); // a chip sits over its box
        if (v.HasImage || v.Spec.Style is "swatch" or "picture") return;
        v.IconTile.Background = on ? new SolidColorBrush(v.Spec.Color) : Brush(v.Spec.Color, 0x29);
        v.Glyph.Foreground = on ? Brush(theme.Background) : new SolidColorBrush(v.Spec.Color);
    }

    // Arrows move to the nearest tile that way, whatever the sizes: the one closest along the
    // arrow, then most in line with this one.
    void NavTile(int dx, int dy)
    {
        keyMovedAt = DateTime.Now;
        if (selected >= tiles.Count) return;
        var r = RectOf(tiles[selected].Root);
        int best = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (i == selected) continue;
            var o = RectOf(tiles[i].Root);
            double along, gap, off;
            if (dy != 0)
            {
                along = dy > 0 ? o.Y - (r.Y + r.Height) : r.Y - (o.Y + o.Height);
                gap = Math.Max(0, Math.Max(o.X - (r.X + r.Width), r.X - (o.X + o.Width)));
                off = Math.Abs(o.X + o.Width / 2 - (r.X + r.Width / 2));
            }
            else
            {
                along = dx > 0 ? o.X - (r.X + r.Width) : r.X - (o.X + o.Width);
                gap = Math.Max(0, Math.Max(o.Y - (r.Y + r.Height), r.Y - (o.Y + o.Height)));
                off = Math.Abs(o.Y + o.Height / 2 - (r.Y + r.Height / 2));
            }
            if (along < -1) continue;
            double score = along + gap * 3 + off * 0.05;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) return;
        selected = best;
        Highlight();
    }

    static Windows.Foundation.Rect RectOf(FrameworkElement e) => new(e.ActualOffset.X, e.ActualOffset.Y, e.ActualWidth, e.ActualHeight);
}
