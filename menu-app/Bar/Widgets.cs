using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SystemMenu;

// Everything a widget may show, gathered by Bar.cs and handed to every widget on every change.
public sealed record BarData(
    GlazeWm.State? Wm,
    DateTime Now,
    BarData.Battery? Power,
    WifiState? Wifi,
    bool Ethernet,
    AudioDevice? Speaker,
    bool Awake,
    int Listening, // 0: no; 1: voice typing; 2: omawin's agent
    int Updates)
{
    public sealed record Battery(int Percent, bool Charging);
}

// One of bar.json's widgets (zebar/tokyo-islands/widgets/<name>.js, the same names, behaviour and
// glyphs): its element in the bar, filled in by Update.
public abstract class BarWidget
{
    public abstract FrameworkElement Element { get; }
    protected BarWindow Bar = null!;
    protected Theme Theme = new();
    public virtual void Attach(BarWindow bar) => Bar = bar;
    public virtual void Update(BarData data) { }
    public virtual void Paint(Theme theme) => Theme = theme;

    public static BarWidget? Create(string name) => name switch
    {
        "menu" => new MenuWidget(),
        "workspaces" => new WorkspacesWidget(),
        "indicators" => new IndicatorsWidget(),
        "clock" => new ClockWidget(),
        "system-update" => new UpdateWidget(),
        "bluetooth" => new BluetoothWidget(),
        "network" => new NetworkWidget(),
        "audio" => new AudioWidget(),
        "monitor" => new MonitorWidget(),
        "power" => new PowerWidget(),
        "tray" => new TrayWidget(),
        _ => null, // media, agents: Zebar's for now (docs/native-bar.md)
    };

    public static readonly FontFamily Mono = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas");
    public static string Glyph(int code) => char.ConvertFromUtf32(code);
}

// A button-sized cell, as bar.css's: 26 px high, at least 27 wide (Omarchy's icon slot), the
// theme's text, a faint highlight under the mouse. Click, right click and the wheel are its own.
public sealed class Cell : Grid
{
    public readonly TextBlock Icon = new() { FontFamily = BarWidget.Mono, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    public readonly TextBlock Text = new() { FontFamily = BarWidget.Mono, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    public Action? Click, RightClick;
    public Action<int>? Wheel; // +1 up, -1 down
    Theme theme = new();
    bool hot, red;

    public Cell(string? tip = null)
    {
        Height = BarWindow.Height;
        MinWidth = 27;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // so the whole cell takes the mouse
        row.Children.Add(Icon);
        row.Children.Add(Text);
        Icon.Visibility = Text.Visibility = Visibility.Collapsed;
        Children.Add(row);
        if (tip != null) Tip = tip;
        PointerEntered += (_, _) => { hot = true; Repaint(); };
        PointerExited += (_, _) => { hot = false; Repaint(); };
        PointerReleased += (_, e) =>
        {
            var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            if (kind == PointerUpdateKind.LeftButtonReleased) Click?.Invoke();
            else if (kind == PointerUpdateKind.RightButtonReleased) RightClick?.Invoke();
        };
        PointerWheelChanged += (_, e) =>
        {
            int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta != 0) Wheel?.Invoke(delta > 0 ? 1 : -1);
            e.Handled = true;
        };
    }

    public string Tip { set => ToolTipService.SetToolTip(this, string.IsNullOrEmpty(value) ? null : value); }

    public void SetIcon(string? glyph)
    {
        if (Icon.Text != glyph) Icon.Text = glyph ?? "";
        Icon.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetText(string? text)
    {
        if (Text.Text != text) Text.Text = text ?? "";
        Text.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Red: bar.css's .active (an update waiting, a low battery, listening).
    public bool Red { set { if (red != value) { red = value; Repaint(); } } }

    public void Paint(Theme t) { theme = t; Repaint(); }

    void Repaint()
    {
        var fg = SystemMenu.Bar.Hex(red ? theme.Red : theme.Foreground);
        Icon.Foreground = Text.Foreground = new SolidColorBrush(fg);
        var hover = SystemMenu.Bar.Hex(theme.Foreground);
        hover.A = hot ? (byte)20 : (byte)0; // 8% of the text's colour, as bar.css
        Background = new SolidColorBrush(hover);
    }
}

// --- the widgets, each after its zebar/tokyo-islands/widgets/<name>.js --------------------------

// Omarchy's logo (omarchy.ttf, U+E900). Click: the system menu; right click: a terminal.
sealed class MenuWidget : BarWidget
{
    readonly Cell cell = new("Menu (right click: a terminal)");
    public override FrameworkElement Element => cell;
    public MenuWidget()
    {
        cell.Icon.FontFamily = SystemMenu.Bar.LogoFont;
        cell.Icon.FontSize = 14;
        cell.SetIcon(Glyph(0xE900));
        cell.Click = () => SystemMenu.Bar.Request([]);
        cell.RightClick = () => SystemMenu.Bar.Omawin("launch terminal");
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// 1 to 5 always, and any others in use (10 shows as 0); the one on screen shows its layout (a
// square tiling, a triangle when its focused window floats); empty ones at half. Click: go there
// (on the one on screen: turn its tiling direction); the wheel moves along, stopping at the ends.
sealed class WorkspacesWidget : BarWidget
{
    const int Tiling = 0xF14FB, Floating = 0xF0536;
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    readonly Dictionary<int, Cell> cells = [];
    List<int> ids = [];
    int shown = -1;
    string key = "";
    public override FrameworkElement Element => row;

    public WorkspacesWidget()
    {
        row.PointerWheelChanged += (_, e) =>
        {
            int delta = e.GetCurrentPoint(row).Properties.MouseWheelDelta;
            int i = ids.IndexOf(shown);
            if (delta == 0 || i < 0) return;
            int next = ids[Math.Clamp(i + (delta < 0 ? 1 : -1), 0, ids.Count - 1)];
            if (next != shown) SystemMenu.Bar.Wm.Command($"focus --workspace {next}");
            e.Handled = true;
        };
    }

    public override void Update(BarData data)
    {
        if (data.Wm == null || Bar.Monitor == null) return;
        var mine = Bar.Monitor.Workspaces.Where(w => int.TryParse(w.Name, out _)).ToDictionary(w => int.Parse(w.Name));
        ids = [.. new SortedSet<int>([1, 2, 3, 4, 5, .. mine.Keys])];
        shown = mine.Values.FirstOrDefault(w => w.Displayed) is { } s ? int.Parse(s.Name) : -1;
        var k = string.Join(",", ids.Select(id => $"{id}:{(mine.TryGetValue(id, out var w) && w.Occupied ? 1 : 0)}")) + $"|{shown}|{data.Wm.FocusedFloats}";
        if (k == key) return;
        key = k;
        row.Children.Clear();
        foreach (var id in ids)
        {
            if (!cells.TryGetValue(id, out var cell))
            {
                int target = id;
                cells[id] = cell = new Cell($"Workspace {id}") { MinWidth = 20, Padding = new Thickness(4, 0, 4, 0) };
                cell.Click = () => SystemMenu.Bar.Wm.Command(target == shown ? "toggle-tiling-direction" : $"focus --workspace {target}");
                cell.Paint(Theme);
            }
            bool isShown = id == shown, occupied = mine.TryGetValue(id, out var w) && w.Occupied;
            if (isShown) { cell.SetText(null); cell.SetIcon(Glyph(data.Wm.FocusedFloats ? Floating : Tiling)); }
            else { cell.SetIcon(null); cell.SetText(id == 10 ? "0" : id.ToString()); }
            cell.Opacity = occupied || isShown ? 1 : 0.5;
            row.Children.Add(cell);
        }
    }

    public override void Paint(Theme theme) { base.Paint(theme); foreach (var c in cells.Values) c.Paint(theme); }
}

// "Thursday 22:58" (bar.json's clock.format); right click: the date and week; click: Windows'
// calendar (the notification center).
sealed class ClockWidget : BarWidget
{
    readonly Cell cell = new("Click: calendar · right click: the date") { Padding = new Thickness(6, 0, 6, 0) };
    bool alt;
    DateTime last;
    public override FrameworkElement Element => cell;
    public ClockWidget()
    {
        cell.RightClick = () => { alt = !alt; Show(last); };
        cell.Click = () => SystemMenu.Bar.Wm.Command("shell-exec explorer.exe ms-actioncenter:");
    }
    public override void Update(BarData data) { last = data.Now; Show(data.Now); }
    void Show(DateTime now) => cell.SetText(alt ? SystemMenu.Bar.ClockAlt(now) : SystemMenu.Bar.ClockMain(now));
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// Dictation (the mic, red and pulsing while voice typing or omawin's agent listens) and stay
// awake (a mug; click to stop): shown only while on. The faint reveal of an off mug under the
// mouse is left for later.
sealed class IndicatorsWidget : BarWidget
{
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    readonly Cell mic = new("Listening"), mug = new("Stay awake");
    public override FrameworkElement Element => row;
    public IndicatorsWidget()
    {
        mic.SetIcon(Glyph(0xF036C));
        mic.Red = true;
        mug.SetIcon(Glyph(0xEF59));
        mug.Click = () => SystemMenu.Bar.Request(["--toggle", "stay-awake"]);
        row.Children.Add(mic);
        row.Children.Add(mug);
        var pulse = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 1, To = 0.45, Duration = TimeSpan.FromMilliseconds(600), AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.SineEase(),
        };
        var board = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(pulse, mic.Icon);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(pulse, "Opacity");
        board.Children.Add(pulse);
        mic.Loaded += (_, _) => board.Begin();
    }
    public override void Update(BarData data)
    {
        mic.Visibility = data.Listening > 0 ? Visibility.Visible : Visibility.Collapsed;
        mic.Tip = data.Listening == 2 ? "omawin is listening" : "Listening";
        mug.Visibility = data.Awake ? Visibility.Visible : Visibility.Collapsed;
    }
    public override void Paint(Theme theme) { base.Paint(theme); mic.Paint(theme); mug.Paint(theme); }
}

// An arrow in red while omawin updates are waiting (bin/update-available.ps1); click: update.
sealed class UpdateWidget : BarWidget
{
    readonly Cell cell = new();
    public override FrameworkElement Element => cell;
    public UpdateWidget()
    {
        cell.SetIcon(Glyph(0xF021));
        cell.Red = true;
        cell.Visibility = Visibility.Collapsed;
        cell.Click = () =>
        {
            cell.Visibility = Visibility.Collapsed;
            SystemMenu.Bar.Omawin("launch terminal --title menu-task -- pwsh -NoLogo -Command omawin update");
        };
    }
    public override void Update(BarData data)
    {
        cell.Visibility = data.Updates > 0 ? Visibility.Visible : Visibility.Collapsed;
        cell.Tip = $"{data.Updates} omawin update{(data.Updates == 1 ? "" : "s")} waiting: click to update";
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// The tray: a drawer of the apps' icons (Tray.cs) that opens while the mouse is on it, after the
// arrow that points the way it opens. Click an icon: the app's own click; right click: its menu.
sealed class TrayWidget : BarWidget
{
    readonly StackPanel root = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel drawer = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly Cell chevron = new("Tray");
    readonly Dictionary<string, (Cell Cell, Image Image, int Stamp)> cells = [];
    string key = "";
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer closing;
    public override FrameworkElement Element => root;

    public TrayWidget()
    {
        chevron.Icon.FontSize = 11;
        chevron.Icon.Opacity = 0.7;
        chevron.SetIcon(Glyph(0xF0141));
        root.Children.Add(drawer);
        root.Children.Add(chevron);
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        closing = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        closing.Interval = TimeSpan.FromMilliseconds(600);
        closing.IsRepeating = false;
        closing.Tick += (_, _) => Open(false);
        root.PointerEntered += (_, _) => { closing.Stop(); Open(true); };
        root.PointerExited += (_, _) => closing.Start();
        Tray.Changed += () => SystemMenu.Bar.Refresh();
        Tray.Start();
    }

    void Open(bool open)
    {
        if (open == (drawer.Visibility == Visibility.Visible)) return;
        drawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;
        drawer.Opacity = 0;
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        var board = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, drawer);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        board.Children.Add(fade);
        board.Begin();
    }

    public override void Update(BarData data)
    {
        var icons = Tray.Icons;
        var k = string.Join(",", icons.Select(i => $"{i.Key}:{i.Stamp}:{i.Tip}"));
        if (k == key) return;
        key = k;
        drawer.Children.Clear();
        foreach (var icon in icons)
        {
            if (!cells.TryGetValue(icon.Key, out var c))
            {
                var image = new Image { Width = 15, Height = 15 };
                var cell = new Cell { MinWidth = 24 };
                cell.Children.Add(image);
                cell.Paint(Theme);
                c = (cell, image, -1);
            }
            var current = icon;
            c.Cell.Click = () => ClickAt(current, false);
            c.Cell.RightClick = () => ClickAt(current, true);
            c.Cell.Tip = icon.Tip;
            if (c.Stamp != icon.Stamp && icon.Pixels != null)
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(32, 32);
                using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(bitmap.PixelBuffer)) stream.Write(icon.Pixels, 0, icon.Pixels.Length);
                c.Image.Source = bitmap;
                c.Stamp = icon.Stamp;
            }
            cells[icon.Key] = c;
            drawer.Children.Add(c.Cell);
        }
    }

    // Where the mouse is, on the screen: what the app's menu opens at.
    static void ClickAt(Tray.Icon icon, bool right)
    {
        GetCursorPos(out var at);
        Tray.Click(icon, right, at.X, at.Y);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct Point { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);

    public override void Paint(Theme theme) { base.Paint(theme); chevron.Paint(theme); foreach (var c in cells.Values) c.Cell.Paint(theme); }
}

// The Bluetooth glyph; click: Quick settings (its radio and devices).
sealed class BluetoothWidget : BarWidget
{
    readonly Cell cell = new("Bluetooth");
    public override FrameworkElement Element => cell;
    public BluetoothWidget()
    {
        cell.SetIcon(Glyph(0xF00AF));
        cell.Click = () => SystemMenu.Bar.Request(["--launcher", "quick"]);
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// Wi-Fi by signal, ethernet, or disconnected; click: Quick settings.
sealed class NetworkWidget : BarWidget
{
    static readonly int[] Wifi = [0xF092F, 0xF091F, 0xF0922, 0xF0925, 0xF0928];
    readonly Cell cell = new();
    public override FrameworkElement Element => cell;
    public NetworkWidget() => cell.Click = () => SystemMenu.Bar.Request(["--launcher", "quick"]);
    public override void Update(BarData data)
    {
        if (data.Ethernet) { cell.SetIcon(Glyph(0xF0200)); cell.Tip = "Ethernet"; }
        else if (data.Wifi is { Connected: true } w)
        {
            int s = w.Signal;
            cell.SetIcon(Glyph(Wifi[s >= 80 ? 4 : s >= 60 ? 3 : s >= 40 ? 2 : s >= 20 ? 1 : 0]));
            cell.Tip = $"{w.Ssid} · {s}%";
        }
        else { cell.SetIcon(Glyph(0xF092E)); cell.Tip = "Disconnected"; }
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// The speaker by volume (headphones for headphones, crossed out when muted). Wheel: volume;
// right click: mute; click: the sound page.
sealed class AudioWidget : BarWidget
{
    readonly Cell cell = new();
    AudioDevice? device;
    public override FrameworkElement Element => cell;
    public AudioWidget()
    {
        cell.Click = () => SystemMenu.Bar.Request(["--launcher", "audio"]);
        cell.RightClick = () => { if (device != null) { Audio.ToggleMute(device.Id); SystemMenu.Bar.Refresh(); } };
        cell.Wheel = up => { if (device != null) { Audio.Change(device.Id, up * 5); SystemMenu.Bar.Refresh(); } };
    }
    public override void Update(BarData data)
    {
        device = data.Speaker;
        if (device == null) { cell.SetIcon(null); return; }
        bool muted = device.Muted || device.Volume == 0;
        var headphones = System.Text.RegularExpressions.Regex.IsMatch(device.Name, "headphone|headset|earbud|airpods|buds", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        int v = device.Volume;
        cell.SetIcon(Glyph(muted ? 0xF075F : headphones ? 0xF02CB : v < 34 ? 0xF057F : v < 67 ? 0xF0580 : 0xF057E));
        cell.Tip = $"{device.Name} · {(muted ? "muted" : v + "%")}";
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// A screen (two with more than one); wheel: brightness; click: Quick settings.
sealed class MonitorWidget : BarWidget
{
    readonly Cell cell = new("Wheel: brightness");
    DateTime wheeled;
    public override FrameworkElement Element => cell;
    public MonitorWidget()
    {
        cell.SetIcon(Glyph(0xF0379));
        cell.Click = () => SystemMenu.Bar.Request(["--launcher", "quick"]);
        cell.Wheel = up =>
        {
            if (DateTime.Now - wheeled < TimeSpan.FromMilliseconds(150)) return;
            wheeled = DateTime.Now;
            Task.Run(() => QuickSettings.Command("brightness", [up > 0 ? "up" : "down"]));
        };
    }
    public override void Update(BarData data) => cell.SetIcon(Glyph((data.Wm?.Monitors.Count ?? 1) > 1 ? 0xF037A : 0xF0379));
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}

// The battery by charge (a plug while charging, a power symbol without one), red under 15%.
// Right click: its percentage beside it; click: the system menu's power options.
sealed class PowerWidget : BarWidget
{
    static readonly int[] Levels = [0xF007A, 0xF007B, 0xF007C, 0xF007D, 0xF007E, 0xF007F, 0xF0080, 0xF0081, 0xF0082, 0xF0079];
    readonly Cell cell = new("Power");
    bool showPercent;
    BarData? last;
    public override FrameworkElement Element => cell;
    public PowerWidget()
    {
        cell.Text.FontSize = 11;
        cell.Click = () => SystemMenu.Bar.Request(["--open", "system"]);
        cell.RightClick = () => { showPercent = !showPercent; if (last != null) Update(last); };
    }
    public override void Update(BarData data)
    {
        last = data;
        if (data.Power is not { } b) { cell.SetIcon(Glyph(0xF0425)); cell.SetText(null); cell.Red = false; cell.Tip = "Power"; return; }
        cell.SetIcon(Glyph(b.Charging ? 0xF0084 : Levels[Math.Min(9, b.Percent / 10)]));
        cell.Red = b.Percent < 15 && !b.Charging;
        cell.SetText(showPercent ? $"{b.Percent}%" : null);
        cell.Tip = $"{b.Percent}%{(b.Charging ? ", charging" : "")}";
    }
    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}
