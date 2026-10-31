using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SystemMenu;

// Audio (Win+A), also the lower half of Quick settings: where you hear what. Each output (speakers,
// headphones) is a box in its own color, and the sources playing on it (the music player, Chrome,
// Teams, ...) sit inside it as chips; the microphones follow. Each has its volume as a bar that
// lights up with the sound going through it, and an icon that swells with it. Up / down walk box,
// its chips, next box; left / right (or + / −) turn it down or up, Space mutes, Enter moves a source to another
// output's box, or makes a device the one Windows uses. The switches above all this — Wi-Fi,
// Bluetooth, brightness, power mode, night light, do not disturb, battery — are
// MainWindow.Quick.cs. Data: Audio.cs.
public sealed partial class MainWindow
{
    const double AudioTileDip = 76, ChipRowDip = 46;
    List<AudioDevice> audioDevices = [];
    AudioApp? routing;             // choosing where this app plays
    Microsoft.UI.Dispatching.DispatcherQueueTimer? audioTimer;

    // A device's name without its driver: "Headphones (Realtek(R) Audio)" -> "Headphones".
    static (string Name, string? Driver) SplitDriver(string name)
    {
        int at = name.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 && name.EndsWith(')') ? (name[..at], name[(at + 2)..^1]) : (name, null);
    }

    // The speakers, what's playing on them, and the microphones: added below Quick settings'
    // switches, in the page's layout. Returns the counts for the footer.
    (int Outputs, int Inputs, int Microphones) AudioSection(TileLayout layout, string query)
    {
        audioDevices = Audio.List();
        var apps = Audio.Apps();
        bool Match(string name) => query.Length == 0 || Fuzzy.Match(name, query) != Fuzzy.None;
        void Title(string text) => layout.Rows.Add((TitleRowDip, text));
        int NewRow() { layout.Rows.Add((AudioTileDip, null)); return layout.Rows.Count - 1; }
        void Place(TileSpec spec, object row, int at, int col, int cols, int rows = 1)
        {
            spec.Row = at; spec.Col = col; spec.ColSpan = cols; spec.RowSpan = rows;
            spec.Item = row;
            layout.Cells.Add(spec);
        }

        // Outputs: a box each, in its own color along the theme's gradient, with what's playing
        // on it inside as chips, four a row. An input not sent anywhere plays on the one in use.
        var outputs = audioDevices.Where(d => !d.Input).ToList();
        var inUse = outputs.FirstOrDefault(d => d.Default);
        string OutputOf(AudioApp a) => outputs.Any(d => d.Id == a.Output) ? a.Output! : inUse?.Id ?? "";
        bool titled = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            var d = outputs[i];
            var on = apps.Where(a => OutputOf(a) == d.Id && Match(a.Name)).ToList();
            if (!Match(d.Name) && on.Count == 0) continue;
            if (!titled) { Title("Outputs"); titled = true; }
            var color = d.Muted ? Hex(theme.Red) : GradientAt(outputs.Count == 1 ? 0 : i / (double)(outputs.Count - 1));
            int top = NewRow();
            int chipRows = (on.Count + 3) / 4;
            for (int k = 0; k < chipRows; k++) layout.Rows.Add((ChipRowDip, null));
            var (name, driver) = SplitDriver(d.Name);
            string sub = string.Join(" · ", new[] { d.Default ? "in use" : null, on.Count == 0 ? "nothing playing here" : null, driver }.Where(x => x != null));
            var box = DeviceTile(d, name, sub, color);
            box.Style = "audiobox";
            Place(box, d, top, 0, 4, 1 + chipRows);
            for (int k = 0; k < on.Count; k++) Place(ChipTile(on[k], color), on[k], top + 1 + k / 4, k % 4, 1);
        }
        // Inputs that can't be sent anywhere and play on no speaker found (system sounds with
        // nothing plugged in): still listed, for their volume.
        var homeless = apps.Where(a => OutputOf(a) == "" && Match(a.Name)).ToList();
        if (homeless.Count > 0)
        {
            Title("Inputs");
            int at = -1;
            for (int k = 0; k < homeless.Count; k++)
            {
                if (k % 4 == 0) { layout.Rows.Add((ChipRowDip, null)); at = layout.Rows.Count - 1; }
                Place(ChipTile(homeless[k], Hex(theme.Cyan)), homeless[k], at, k % 4, 1);
            }
        }
        // Microphones, two a row.
        var mics = audioDevices.Where(d => d.Input && Match(d.Name)).ToList();
        if (mics.Count > 0)
        {
            Title("Microphones");
            int at = -1;
            for (int k = 0; k < mics.Count; k++)
            {
                if (k % 2 == 0) at = NewRow();
                var (name, driver) = SplitDriver(mics[k].Name);
                string sub = string.Join(" · ", new[] { mics[k].Default ? "in use" : null, driver }.Where(x => x != null));
                Place(DeviceTile(mics[k], name, sub, Hex(mics[k].Muted ? theme.Red : theme.Purple)), mics[k], at, k % 2 * 2, 2);
            }
        }
        if (layout.Cells.Count == 0)
            layout.Flow([new TileSpec { Key = "none", Style = "wide", ColSpan = 4, Icon = Glyph(0xF0581), Label = "Nothing found", Color = Hex(theme.Comment) }]);

        return (outputs.Count, apps.Count, mics.Count);
    }

    // A microphone, or a speaker by how loud it's set (low, medium, high).
    static int DeviceGlyph(bool input, bool muted, int volume) =>
        input ? (muted ? 0xF036D : 0xF036C) : muted ? 0xF075F : volume < 34 ? 0xF057F : volume < 67 ? 0xF0580 : 0xF057E;

    TileSpec DeviceTile(AudioDevice d, string name, string sub, Color color)
    {
        return new TileSpec
        {
            Key = d.Id, Style = "audio", Icon = Glyph(DeviceGlyph(d.Input, d.Muted, d.Volume)), Label = name, Sub = sub, Current = d.Default, Color = color,
            MeterKey = d.Id, Volume = d.Volume, Muted = d.Muted, Run = () => TileUse(),
        };
    }

    // A source playing on a box's output: its icon and name, and a thin bar that moves with it.
    TileSpec ChipTile(AudioApp a, Color color)
    {
        bool ours = a.Name == Audio.MusicName; // omawin's player has no icon of its own
        int glyph = a.Muted ? 0xF075F : a.Key == "system" ? 0xF009A : 0xF075A;
        return new TileSpec
        {
            Key = a.Key, Style = "chip", Icon = Glyph(glyph), Label = ours ? "Music" : a.Key == "system" ? "System" : a.Name,
            Color = a.Muted ? Hex(theme.Red) : color,
            AppId = a.Exe != null && !a.Muted && !ours ? "path:" + a.Exe : null,
            MeterKey = a.Key, Volume = a.Volume, Muted = a.Muted, Run = () => TileUse(),
        };
    }

    // Enter on an input: which output it plays on (Windows remembers it for that app). Typing
    // narrows the speakers, as on the tiles.
    void RouteList(string query)
    {
        var app = routing!;
        SetCrumbs("", $"Send {app.Name} to");
        Placeholder.Text = "Search speakers…";
        var def = audioDevices.FirstOrDefault(d => !d.Input && d.Default);
        const string Follow = "Whatever Windows is using";
        foreach (var (_, hits) in Filter(new[] { Follow }, query, x => x))
            AddRow(Glyph(0xF0493), Follow, Hex(theme.Accent), () => Route(app, null),
                app.Output == null ? Tag.Current : Tag.Text, def != null ? SplitDriver(def.Name).Name : "", sub: "follows the default", highlight: hits);
        foreach (var (d, hits) in Filter(audioDevices.Where(d => !d.Input), query, d => SplitDriver(d.Name).Name))
        {
            var device = d;
            var (name, driver) = SplitDriver(d.Name);
            AddRow(Glyph(0xF057E), name, Hex(theme.Cyan), () => Route(app, device.Id), app.Output == d.Id ? Tag.Current : Tag.None, sub: driver, highlight: hits);
        }
        if (rows.Count == 0) AddRow(Glyph(0xF0156), "No matches", Hex(theme.Comment), () => { }, dim: true);
        FooterLeft.Text = app.Name;
        SetKeys(("↑↓", "move"), ("↵", "play it here"), ("esc", "back"));
    }

    void Route(AudioApp app, string? deviceId)
    {
        Audio.SendTo(app, deviceId);
        routing = null;
        ReselectAudio(app.Key);
    }

    // Space, + and − work the tiles only while nothing is typed; after that they're search text.
    // (Tiles are the switches above and the sound below, so this is not just audio.)
    bool TileKeys => launching && page == "audio" && Input.Text.Length == 0;

    // What the selected tile is: an AudioDevice, an AudioApp or one of Quick settings' switches.
    object? TileItem => routing == null && tileMode && selected < tiles.Count ? tiles[selected].Spec.Item : null;

    // A tile with a level that + / − and left / right turn.
    bool Adjustable => TileItem is AudioDevice or AudioApp or QsTile { Kind: "brightness" };

    // + / −: 5% a press, on a speaker's or an app's volume, or a monitor's brightness.
    void Adjust(int by)
    {
        switch (TileItem)
        {
            case AudioApp app: ShowVolume(Audio.ChangeApp(app.Key, by), muted: false); break;
            case AudioDevice d: ShowVolume(Audio.Change(d.Id, by), muted: false); break;
            case QsTile t: QuickAdjust(t, by); break;
        }
    }

    // Space: a speaker's or an app's mute, or a switch on or off.
    void TileAction()
    {
        switch (TileItem)
        {
            case AudioApp app: Audio.ToggleAppMute(app.Key); ReselectAudio(app.Key); break;
            case AudioDevice d: Audio.ToggleMute(d.Id); ReselectAudio(d.Id); break;
            case QsTile t: QuickAction(t); break;
        }
    }

    // Enter (or a click): an input, pick the output it plays on; a device, make it the one
    // Windows uses; a switch, open what it holds (networks, devices, power modes).
    void TileUse()
    {
        switch (TileItem)
        {
            case AudioApp app when app.Pids.Length > 0: routing = app; selected = 0; Render(); Slide(1); break;
            case AudioDevice d: Audio.MakeDefault(d.Id); ReselectAudio(d.Id); break;
            case QsTile t: QuickOpen(t); break;
        }
    }

    // Esc while choosing a speaker: back to the tiles, on that app.
    bool LeaveRouting()
    {
        if (routing == null) return false;
        var key = routing.Key;
        routing = null;
        ReselectAudio(key);
        Slide(-1);
        return true;
    }

    // Drawn again, still on that tile.
    void ReselectAudio(string key)
    {
        selectKey = key;
        Render();
    }

    // The selected tile's new volume, in place (a redraw would restart its meter). A change
    // that unmutes it redraws it, for its icon and color.
    void ShowVolume(int? volume, bool muted)
    {
        if (volume == null || selected >= tiles.Count) return;
        var v = tiles[selected];
        if (v.Spec.Muted != muted) { ReselectAudio(v.Spec.Key); return; }
        v.Spec.Volume = volume.Value;
        SetLevel(v);
        // A speaker's icon shows how loud it's set.
        if (TileItem is AudioDevice d) v.Glyph.Text = Glyph(DeviceGlyph(d.Input, muted, volume.Value));
    }

    void SetLevel(TileView v)
    {
        if (v.Meter == null || v.Percent == null) return;
        int level = v.Spec.Muted ? 0 : v.Spec.Volume;
        v.Meter.ColumnDefinitions[0].Width = new GridLength(level, GridUnitType.Star);
        v.Meter.ColumnDefinitions[1].Width = new GridLength(100 - level, GridUnitType.Star);
        v.Percent.Text = v.Spec.Hint ?? (v.Spec.Muted ? "muted" : $"{v.Spec.Volume}%");
        v.Percent.Foreground = Brush(v.Spec.Muted ? theme.Red : theme.Foreground);
    }

    // An audio tile (a microphone, or an output's box: this along its top, its chips below): its
    // icon (swelling with the sound), name, what it is, its volume, and the bar under them.
    void BuildAudioTile(TileView v, TileSpec s, TextBlock label)
    {
        // A glow of its color from the top right; brighter on the device in use.
        var glow = new LinearGradientBrush { StartPoint = new(1, 0), EndPoint = new(0.3, 1) };
        glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(s.Current ? (byte)0x40 : (byte)0x1C, s.Color.R, s.Color.G, s.Color.B), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, s.Color.R, s.Color.G, s.Color.B), Offset = 1 });
        v.Inner.Children.Add(new Border { Background = glow, CornerRadius = new CornerRadius(11) });

        var body = new Grid { Padding = new Thickness(14, 10, 14, 11), ColumnSpacing = 12, RowSpacing = 8, VerticalAlignment = VerticalAlignment.Top, Height = AudioTileDip };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        v.IconScale = new ScaleTransform { CenterX = 18, CenterY = 18 };
        v.IconTile.RenderTransform = v.IconScale;
        v.IconTile.VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(v.IconTile);

        label.FontSize = 13.5;
        label.FontWeight = FontWeights.SemiBold;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(label);
        if (!string.IsNullOrEmpty(s.Sub))
            text.Children.Add(v.SubText = new TextBlock
            {
                Text = s.Sub, FontFamily = textFont, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
                Foreground = Brush(s.Current ? theme.Accent : theme.Comment),
            });
        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        v.Percent = new TextBlock { FontFamily = monoFont, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v.Percent, 2);
        body.Children.Add(v.Percent);

        // The bar: a light track, the volume set, and inside it the sound right now along the
        // theme's gradient (mapped to the whole track, so louder reaches further along it).
        v.Meter = new Grid { Height = 6 };
        v.Meter.ColumnDefinitions.Add(new ColumnDefinition());
        v.Meter.ColumnDefinitions.Add(new ColumnDefinition());
        var track = new Border { CornerRadius = new CornerRadius(3), Background = Brush(Hex(theme.Foreground), 0x14) };
        Grid.SetColumnSpan(track, 2);
        v.MeterLevel = new Border { CornerRadius = new CornerRadius(3), Background = s.Muted ? Brush(Hex(theme.Comment), 0x60) : Brush(Hex(theme.Foreground), 0x38) };
        var gradient = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new(0, 0), EndPoint = new(300, 0) };
        for (int i = 0; i <= 4; i++) gradient.GradientStops.Add(new GradientStop { Color = GradientAt(i / 4.0), Offset = i / 4.0 });
        track.SizeChanged += (_, e) => gradient.EndPoint = new(Math.Max(1, e.NewSize.Width), 0);
        v.MeterLive = new Border { CornerRadius = new CornerRadius(3), Background = gradient, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        v.Meter.Children.Add(track);
        v.Meter.Children.Add(v.MeterLevel);
        v.Meter.Children.Add(v.MeterLive);
        Grid.SetRow(v.Meter, 1);
        Grid.SetColumnSpan(v.Meter, 3);
        body.Children.Add(v.Meter);
        v.Inner.Children.Add(body);
        SetLevel(v);
    }

    // A chip: icon, name, and a thin bar along its bottom (the sound, inside its volume).
    void BuildChip(TileView v, TileSpec s, TextBlock label)
    {
        v.Inner.Children.Add(new Border { Background = Brush(s.Color, 0x1A), CornerRadius = new CornerRadius(11) });
        var body = new Grid { Padding = new Thickness(10, 0, 10, 0), ColumnSpacing = 9 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        v.IconTile.CornerRadius = new CornerRadius(7);
        v.Glyph.FontSize = 14;
        v.IconScale = new ScaleTransform { CenterX = 13, CenterY = 13 };
        v.IconTile.RenderTransform = v.IconScale;
        v.IconTile.VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(v.IconTile);
        label.FontSize = 12.5;
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        if (s.Muted) label.Foreground = Brush(theme.Comment);
        Grid.SetColumn(label, 1);
        body.Children.Add(label);
        v.Inner.Children.Add(body);

        v.Meter = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 6) };
        v.Meter.ColumnDefinitions.Add(new ColumnDefinition());
        v.Meter.ColumnDefinitions.Add(new ColumnDefinition());
        v.MeterLevel = new Border { CornerRadius = new CornerRadius(1.5), Background = Brush(Hex(theme.Foreground), 0x22) };
        v.MeterLive = new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(s.Color), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        v.Meter.Children.Add(v.MeterLevel);
        v.Meter.Children.Add(v.MeterLive);
        v.Inner.Children.Add(v.Meter);
        v.Percent = new TextBlock(); // kept for SetLevel; a chip doesn't show it
        SetLevel(v);
    }

    Microsoft.UI.Dispatching.DispatcherQueueTimer MakeAudioTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) => AudioTick();
        return timer;
    }

    // ~30 times a second while the tiles are up: each bar follows its sound, jumping up and
    // falling back slowly like a VU meter, and the icon swells and glows with it.
    void AudioTick()
    {
        if (!(launching && page == "audio" && routing == null && tileMode && appWindow.IsVisible)) { audioTimer?.Stop(); Audio.StopWatchingSessions(); return; }
        for (int i = 0; i < tiles.Count; i++)
        {
            var v = tiles[i];
            if (v.Spec.MeterKey == null || v.Spec.Muted || v.MeterLive == null || v.IconScale == null) continue;
            double peak = Math.Sqrt(Audio.Peak(v.Spec.MeterKey)); // closer to how loud it sounds
            v.Loudness = Math.Max(peak, v.Loudness * 0.86);
            v.MeterLive.Width = v.MeterLevel!.ActualWidth * v.Loudness;
            v.IconScale.ScaleX = v.IconScale.ScaleY = 1 + 0.16 * v.Loudness;
            if (i != selected && !v.HasImage)
            {
                // One brush per tile, recolored: not a new one per tile 30 times a second.
                var c = v.Spec.Color;
                v.IconGlow ??= new SolidColorBrush();
                v.IconGlow.Color = Color.FromArgb((byte)(0x29 + v.Loudness * 0x90), c.R, c.G, c.B);
                if (v.IconTile.Background != v.IconGlow) v.IconTile.Background = v.IconGlow;
            }
        }
    }
}
