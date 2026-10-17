using System.Numerics;
using System.Runtime.InteropServices;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace SystemMenu;

// About (`omawin about`, System menu › About): not a page of the launcher but its own window, and
// that window is the one fastfetch draws in a terminal — Omarchy's symbol on the left, Hardware,
// Software and Age · Uptime · Update in gray boxes on the right (About.cs is the only source of
// the facts; this file only lays them out).
//
// Everything here is counted in terminal cells, not in pixels: noctty's font (JetBrainsMono Nerd
// Font) at noctty's size (11pt, noctty/config.ghostty), where a cell is 0.6 em wide and 1.32 em
// tall (JetBrains Mono's advance is 600/1000 em and its line box is ascent 1020 + descent 300 over
// a 1000-unit em). That is the grid fastfetch/logo.txt was drawn on, so the logo is 54x26 cells of
// it, the boxes are 54 cells wide, and a fact sits in the column fastfetch put it in.
//
// Colours come from the theme's palette (themes/colors/<name>.toml), the same file the terminal
// and GlazeWM's border take theirs from, so a theme switch repaints this with everything else, and
// nothing here is a hard-coded colour.
//
// The logo is geometry: the 54x26 block cells merged into rectangles and snapped to *device*
// pixels, so its edges land on pixels at 100%, 125% and 150%, and its glyph baselines are the
// terminal's own rows. The sheen that runs over it is Omarchy's, ported from
// bin/omarchy-branding-about-animation: a band leaning two columns per row (45° where a cell is
// twice as tall as it is wide), half-width two cells, one column a frame, 25 ms a frame, then four
// seconds of rest, forever. It is drawn as a gradient brush whose white band sweeps down the
// logo's diagonal (SheenBrush below) and is paused while the window is hidden.
public sealed partial class AboutWindow : Window
{
    // One terminal cell, in DIP at 96 dpi.
    const double FontSizeDip = 11.0 * 96 / 72; // font-size = 11pt
    const double CellW = 0.6 * FontSizeDip;    // JetBrains Mono's advance
    const double CellH = 1.32 * FontSizeDip;   // its line box

    // fastfetch/config.jsonc's logo block, and the fastfetch layout the reference render has:
    // two cells of padding left of the logo, two above it, six between it and the module column,
    // where the box starts at cell 62. Right of the content Omarchy keeps the left padding again
    // plus two cells of spare (bin/omarchy-launch-about: LOGO_PAD_LEFT, FIT_SPARE_COLUMNS), and a
    // row of spare below it; a fact that grows a cell widens the window instead of clipping.
    const int PadLeft = 2, PadTop = 2, Gap = 6, PadRight = 4, SpareRow = 1;
    const int LogoCols = 54, LogoRows = 26;
    // A box is ┌ + 22 ─ + "Hardware" + 22 ─ + ┐: the title sits in the middle of 54 cells.
    const int BoxWidth = 54;

    // The sheen (bin/omarchy-branding-about-animation): SHEEN_SLANT, SHEEN_HALF,
    // SHEEN_FRAME_SECONDS, SHEEN_REST_TICKS × POLL_SECONDS, and a frame for every column the band
    // crosses: centre from -half to width + rows*slant + half.
    const int Slant = 2, Half = 2;
    const double FrameSeconds = 0.025, RestSeconds = 4.0;
    const int Frames = LogoCols + LogoRows * Slant + 2 * Half + 1;
    const double TotalSeconds = Frames * FrameSeconds + RestSeconds;
    // The band crosses row r at column centre - r*Slant: the line x + K*y = p, where K is that
    // step's slope on screen (K = 2 * CellW / CellH = 10/11) and p is where it meets the logo's
    // first row, in DIP. The gradient axis below runs along that line's normal, and Dx is half of
    // it: the axis is 2*Dx along x, so it is 56 cells either side of the band's centre line, and
    // one cell of the plane is 1/112 of the axis (SheenBrush.Cell) once the slant is counted in
    // (a move along the normal covers sqrt(1 + K*K) as much plane as it does x).
    static readonly double K = Slant * CellW / CellH;
    static readonly double Dx = 56 * CellW;
    static readonly double Dy = Dx * K;
    // Where the box column starts, and where a line of the module column begins.
    const double ModuleX = (PadLeft + LogoCols + Gap) * CellW;

    const char Block = '█';

    readonly IntPtr hwnd;
    readonly AppWindow appWindow;
    readonly bool rendering;
    readonly List<string> notes = []; // what --render-about writes beside the PNG
    readonly Dictionary<uint, SolidColorBrush> brushes = [];
    static readonly FontFamily Mono = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas");

    AnsiPalette palette = new();
    List<SheetLine> lines = [];
    double sheetW, sheetH;                 // DIP
    int cols, rows;
    int frame = -1;                        // >= 0: the sheen's band stands at that frame (a render)
    SheenBrush? sheen;
    DateTime shownAt;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, ref RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, ref RECT rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    public AboutWindow(bool rendering = false)
    {
        this.rendering = rendering;
        InitializeComponent();
        hwnd = WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        appWindow.SetPresenter(presenter);
        // A tool window: not on the taskbar or in Alt+Tab.
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x80); // GWL_EXSTYLE |= WS_EX_TOOLWINDOW
        appWindow.Title = "About omawin";
        int round = 2; // DWMWCP_ROUND: Windows 11's rounded corners, over the glass
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        // The launcher's glass, unless this is a render: a PNG has no desktop behind it, so it
        // gets the palette's background instead (Layout).
        if (!rendering) SystemBackdrop = new DesktopAcrylicBackdrop();

        // Any key closes it (there is nothing to do in here), and so does being clicked away from.
        Input.PreviewKeyDown += (_, e) => { HideAbout(); e.Handled = true; };
        Content.KeyDown += (_, _) => HideAbout();
        Root.PointerPressed += (_, _) => Input.Focus(FocusState.Programmatic); // a click inside takes the keys back
        Activated += (_, e) =>
        {
            // Clicking elsewhere hides it (but not the focus shuffle right as it appears).
            if (e.WindowActivationState == WindowActivationState.Deactivated && DateTime.Now - shownAt > TimeSpan.FromMilliseconds(400))
                HideAbout();
        };
        appWindow.Closing += (_, e) => { e.Cancel = true; HideAbout(); };
    }

    // --- showing and hiding -------------------------------------------------------------------
    public void ShowAbout()
    {
        try
        {
            Layout();
            Place();
            appWindow.Show();
            shownAt = DateTime.Now;
            SetForegroundWindow(hwnd);
            Input.Focus(FocusState.Programmatic);
            sheen?.Play();
            Read();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void HideAbout()
    {
        appWindow.Hide();
        sheen?.Stop();
    }

    // The facts are read off the UI thread (About.cs: git and a few process lookups); what was
    // read last shows at once, and the sheet is laid out again only if the answer changed.
    void Read()
    {
        About.LoadAsync().ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (!appWindow.IsVisible) return;
            var fresh = SheetLines();
            if (string.Join('\n', fresh.Select(l => l.Text)) == string.Join('\n', lines.Select(l => l.Text))) return;
            Layout();
            Place();
            Input.Focus(FocusState.Programmatic);
            sheen?.Play();
        }));
    }

    // --- the sheet ----------------------------------------------------------------------------
    void Layout()
    {
        notes.Clear();
        palette = AnsiPalette.Load(MenuModel.CurrentThemeName());
        lines = SheetLines();
        int text = lines.Count == 0 ? 0 : lines.Max(l => l.Cells);
        cols = PadLeft + LogoCols + Gap + text + PadRight;
        rows = PadTop + Math.Max(LogoRows, lines.Count) + SpareRow;
        sheetW = cols * CellW;
        sheetH = rows * CellH;

        Root.RequestedTheme = palette.Mode == "light" ? ElementTheme.Light : ElementTheme.Dark;
        Root.Background = rendering
            ? BrushFor(Hex(palette.Background))
            : new SolidColorBrush(Hex(palette.Background, palette.Mode == "light" ? (byte)0xCC : (byte)0xB4));
        // The window's own 1px border: the accent GlazeWM paints a focused window's with.
        var accent = Hex(palette.Accent);
        int colorref = accent.R | (accent.G << 8) | (accent.B << 16);
        DwmSetWindowAttribute(hwnd, 34, ref colorref, sizeof(int));

        notes.Add($"{MenuModel.CurrentThemeName()}: background {palette.Background}, foreground {palette.Foreground}, "
            + $"green {palette.Green}, blue {palette.Blue}, magenta {palette.Magenta}, gray {palette.Gray}, "
            + $"band {palette.BrightWhite}, accent {palette.Accent}");
        notes.Add($"cell {CellW:0.###} x {CellH:0.###} dip (JetBrains Mono, {FontSizeDip:0.####} dip = 11pt)");
        notes.Add($"sheet {cols} x {rows} cells = {sheetW:0.#} x {sheetH:0.#} dip ({lines.Count} module lines)");
    }

    // Where the window goes: centred on the active monitor (the one the focused window is on,
    // which under GlazeWM is the monitor in use), sized to the sheet. Created once and reused, and
    // floated by GlazeWM (glazewm/config.yaml ignores window_process: SystemMenu).
    void Place()
    {
        var monitor = MonitorFromWindow(GetForegroundWindow(), 2); // MONITOR_DEFAULTTONEAREST
        if (monitor == IntPtr.Zero) monitor = MonitorFromWindow(hwnd, 2);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) info.Work = new RECT { Right = 1920, Bottom = 1080 };
        double scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        int w = (int)Math.Round(sheetW * scale), h = (int)Math.Round(sheetH * scale);
        int x = info.Work.Left + (info.Work.Right - info.Work.Left - w) / 2;
        int y = info.Work.Top + (info.Work.Bottom - info.Work.Top - h) / 2;
        if (rendering)
        {
            // Off the bottom of the screen: RenderTargetBitmap reads a window that has been drawn,
            // and this one is a screenshot, not something to look at. The same monitor, so the
            // PNG is at the scale the sheet would be shown at.
            x = info.Work.Left;
            y = info.Work.Bottom + 8;
        }
        appWindow.MoveAndResize(new RectInt32(x, y, w, h));
        // AppWindow sizes the whole window, resize frame included, and the sheet is drawn in the
        // client area: ask what we got and hand the frame the difference, so the sheet is the
        // window's content and not something the frame is eating into.
        var whole = new RECT();
        var client = new RECT();
        if (GetWindowRect(hwnd, ref whole) && GetClientRect(hwnd, ref client))
        {
            int addW = w - (client.Right - client.Left), addH = h - (client.Bottom - client.Top);
            if (addW != 0 || addH != 0)
            {
                x -= addW / 2;
                y -= addH / 2;
                appWindow.MoveAndResize(new RectInt32(x, y, w + addW, h + addH));
            }
        }
        Draw(scale);
        notes.Add($"window {w} x {h} px content at {x},{y} ({scale:0.###}x scale)");
    }

    // Everything the sheet holds, in one pass: the logo as one Path of merged rectangles, then a
    // TextBlock a row (a terminal row is one line of runs in its own colours).
    void Draw(double scale)
    {
        Sheet.Children.Clear();
        sheen = null;
        var compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;

        var logo = LogoGeometry(scale);
        if (logo != null)
        {
            var path = new Microsoft.UI.Xaml.Shapes.Path { Data = logo, Stretch = Stretch.None };
            if (frame >= 0 || !rendering)
            {
                sheen = new SheenBrush(compositor, Hex(palette.Green), Hex(palette.BrightWhite), Dx, Dy, frame, notes.Add);
                path.Fill = sheen;
            }
            else path.Fill = BrushFor(Hex(palette.Green)); // a render's plain logo: the terminal's own still one
            Canvas.SetLeft(path, PadLeft * CellW);
            Canvas.SetTop(path, PadTop * CellH);
            Sheet.Children.Add(path);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Runs.Count == 0) continue; // a blank row (fastfetch's "break")
            var block = new TextBlock
            {
                FontFamily = Mono, FontSize = FontSizeDip, LineHeight = CellH,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.NoWrap, IsTextScaleFactorEnabled = false,
            };
            foreach (var (text, colour) in lines[i].Runs) block.Inlines.Add(new Run { Text = text, Foreground = BrushFor(colour) });
            Canvas.SetLeft(block, ModuleX);
            Canvas.SetTop(block, (PadTop + i) * CellH);
            Sheet.Children.Add(block);
        }
    }

    // The module column: a box a section, its facts in the tree fastfetch draws (no prefix on the
    // first, │ ├ on the middle ones, └ └ on the last), then a blank row. About.cs's own section
    // names and order; the colours are the reference's (Hardware green, Software blue, the Age
    // keys magenta), and a value is the terminal's default foreground.
    List<SheetLine> SheetLines()
    {
        var grey = Hex(palette.Gray);
        var foreground = Hex(palette.Foreground);
        var lines = new List<SheetLine>();
        var facts = About.Last;
        if (facts == null)
        {
            lines.Add(new([("Reading this PC…", foreground)]));
            return lines;
        }
        foreach (var section in facts.Select(f => f.Section).Distinct())
        {
            var rows = facts.Where(f => f.Section == section).ToList();
            var colour = section == About.Hardware ? Hex(palette.Green) : section == About.Software ? Hex(palette.Blue) : Hex(palette.Magenta);
            // The key column: │ ├ (or └ └, or nothing on the first row) + the Nerd Font glyph + a
            // space + the key, all of it padded to this section's widest, so its values line up.
            int keyField = 0;
            for (int i = 0; i < rows.Count; i++) keyField = Math.Max(keyField, Cells(Prefix(i, rows.Count) + Glyph(rows[i].Glyph) + " " + rows[i].Key));
            lines.Add(new([(BoxLine(section, '┌', '┐'), grey)]));
            for (int i = 0; i < rows.Count; i++)
            {
                var runs = new List<(string Text, Color Color)>
                {
                    (Pad(Prefix(i, rows.Count) + Glyph(rows[i].Glyph) + " " + rows[i].Key, keyField), colour),
                    (": ", foreground),
                    (rows[i].Value + (rows[i].Dots ? " " : ""), foreground),
                };
                // The theme's row carries the terminal's eight colours, as fastfetch's does
                // (39, 37, 36, 35, 34, 33, 32, 31).
                if (rows[i].Dots) runs.AddRange(palette.Dots.Select(c => ("●", Hex(c))));
                lines.Add(new(runs));
            }
            lines.Add(new([(BoxLine("", '└', '┘'), grey)]));
            lines.Add(new([]));
        }
        return lines;
    }

    static string Prefix(int row, int count) => row == 0 ? "" : row == count - 1 ? "└ └" : "│ ├";
    static string Glyph(int code) => char.ConvertFromUtf32(code);

    // ┌ + n ─ + the title + m ─ + ┐ over 54 cells, split as fastfetch splits it: 22 and 22 for
    // "Hardware", 16 and 15 for "Age / Uptime / Update" (the longer half first).
    static string BoxLine(string title, char open, char close)
    {
        int room = BoxWidth - 2 - Cells(title);
        int before = (room + 1) / 2;
        return open + new string('─', before) + title + new string('─', room - before) + close;
    }

    // The 54x26 cells of fastfetch/logo.txt as rectangles: a run of blocks in a row is one
    // rectangle, and its edges are snapped to whole device pixels at this window's scale, so the
    // grid stays an unbroken grid at 125% and 150% (a half-pixel edge would show as a seam).
    Geometry? LogoGeometry(double scale)
    {
        string[] logo;
        try { logo = File.ReadAllLines(Path.Combine(MenuModel.Repo, "fastfetch", "logo.txt")); }
        catch (Exception ex) { App.Log(ex); return null; }
        double Snap(double dip) => Math.Round(dip * scale) / scale;
        var group = new GeometryGroup();
        int rects = 0;
        for (int row = 0; row < logo.Length; row++)
        {
            var line = logo[row];
            for (int c = 0; c < line.Length;)
            {
                if (line[c] != Block) { c++; continue; }
                int start = c;
                while (c < line.Length && line[c] == Block) c++;
                double x0 = Snap(start * CellW), x1 = Snap(c * CellW), y0 = Snap(row * CellH), y1 = Snap((row + 1) * CellH);
                group.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x0, y0, x1 - x0, y1 - y0) });
                rects++;
            }
        }
        notes.Add($"logo {logo.Max(l => l.Length)} x {logo.Length} cells, {rects} rectangles, snapped at {scale:0.###}x "
            + $"= {Snap(LogoCols * CellW):0.#} x {Snap(LogoRows * CellH):0.#} dip");
        if (frame >= 0) notes.Add($"sheen frame {frame}/{Frames - 1}: the band crosses the top row at cell {frame - Half}");
        return group;
    }

    // A cell per character: this font is monospace, the box strokes and Nerd Font glyphs in it are
    // one cell wide, and About.cs's facts are Latin. A glyph is one cell even though .NET holds it
    // as a surrogate pair, which is why this isn't string.Length.
    static int Cells(string text)
    {
        int cells = 0;
        for (int i = 0; i < text.Length; i++, cells++)
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
        return cells;
    }

    static string Pad(string text, int cells) => text + new string(' ', Math.Max(0, cells - Cells(text)));

    SolidColorBrush BrushFor(Color colour)
    {
        uint key = (uint)(colour.A << 24 | colour.R << 16 | colour.G << 8 | colour.B);
        if (!brushes.TryGetValue(key, out var brush)) brushes[key] = brush = new SolidColorBrush(colour);
        return brush;
    }

    static Color Hex(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    // --- the render switch --------------------------------------------------------------------
    // `--render-about <png> [frame]`: the same sheet, off screen, into a PNG (and what it measured
    // beside it), for a report's side-by-side with the terminal's fastfetch. A frame >= 0 puts the
    // sheen's band there and holds it; without one the logo is drawn plainly, as fastfetch draws it.
    public async Task SaveAsync(string path, int atFrame)
    {
        frame = atFrame;
        await About.LoadAsync(); // the real facts: this is a screenshot of what the window shows
        Layout();
        Place();
        appWindow.Show(activateWindow: false);
        await Task.Delay(500); // let it draw: RenderTargetBitmap reads what has been painted
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var image = SoftwareBitmap.CreateCopyFromBuffer(pixels, BitmapPixelFormat.Bgra8, bitmap.PixelWidth, bitmap.PixelHeight, BitmapAlphaMode.Premultiplied);
        using (var stream = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(image);
            await encoder.FlushAsync();
            stream.Seek(0);
            using var file = File.Create(path);
            await stream.AsStreamForRead().CopyToAsync(file);
        }
        notes.Add($"png {bitmap.PixelWidth} x {bitmap.PixelHeight} px -> {path}");
        // A RenderTargetBitmap reads the XAML tree, and composition content is not part of it:
        // Microsoft's own note is that what it can't capture comes out blank, and a
        // XamlCompositionBrushBase fills in with its fallback colour instead — so the PNG above
        // shows the logo plain whatever the sheen is doing. PrintWindow with PW_RENDERFULLCONTENT
        // asks for the window as DWM has composited it (what Chrome and WebRTC capture their own
        // windows with), which is the one way a PNG can hold the sheen. It goes beside the first.
        CaptureComposited(path + ".dwm.png");
        File.WriteAllLines(path + ".txt", notes);
        Application.Current.Exit();
    }

    // The whole window, frame and rounded corners included, as it is on the screen.
    void CaptureComposited(string path)
    {
        var whole = new RECT();
        if (!GetWindowRect(hwnd, ref whole)) return;
        int w = whole.Right - whole.Left, h = whole.Bottom - whole.Top;
        if (w < 2 || h < 2) return;
        using var shot = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(shot);
        var dc = graphics.GetHdc();
        bool ok = PrintWindow(hwnd, dc, 2); // PW_RENDERFULLCONTENT
        graphics.ReleaseHdc(dc);
        notes.Add($"composited capture {w} x {h} px ({(ok ? "ok" : "failed")}) -> {path}");
        shot.Save(path, ImageFormat.Png);
    }

    // --- the sheen ----------------------------------------------------------------------------
    // The logo's own geometry, filled with a band of light that leans across it. The band is the
    // stripe x + K*y = p; a linear gradient whose axis runs along the stripe's normal draws it, and
    // sweeping is sliding that axis along its own normal: the stops never move, so the band is the
    // same five cells with the same feather at every step, and a whole frame is two numbers.
    //
    // The white is 112ths of the axis: the axis spans 112 cells of the plane (Dx/Dy), so one cell
    // is 1/112 of it and the five cells the terminal lights — two before the band's column, three
    // after (the slice runs from at-HALF to at+HALF) — are 2/112 and 3/112 either side of the
    // middle. Half a cell of feather either side keeps the diagonal from stair-stepping.
    sealed class SheenBrush : XamlCompositionBrushBase
    {
        const double Cell = 1.0 / 112;
        readonly Compositor compositor;
        readonly Color logo, band;
        readonly double dx, dy;
        readonly int still;      // >= 0: hold this frame and never animate
        readonly Action<string> note;
        CompositionLinearGradientBrush? brush;
        bool playing;

        public SheenBrush(Compositor compositor, Color logo, Color band, double dx, double dy, int still, Action<string> note)
        {
            this.compositor = compositor;
            this.logo = logo;
            this.band = band;
            this.dx = dx;
            this.dy = dy;
            this.still = still;
            this.note = note;
            FallbackColor = logo;
        }

        protected override void OnConnected()
        {
            note($"sheen connected, axis {2 * dx:0.#} x {2 * dy:0.#} dip, one cell {1 / Cell:0.#}th of it");
            var gradient = compositor.CreateLinearGradientBrush();
            gradient.ExtendMode = CompositionGradientExtendMode.Clamp; // past either end the logo's colour, not the band's
            // StartPoint / EndPoint below are in DIP of the logo, not the default 0..1 of its box.
            gradient.MappingMode = CompositionMappingMode.Absolute;
            gradient.ColorStops.Add(Stop(compositor, logo, 0));
            gradient.ColorStops.Add(Stop(compositor, logo, 0.5 - 2.5 * Cell));
            gradient.ColorStops.Add(Stop(compositor, band, 0.5 - 2 * Cell));
            gradient.ColorStops.Add(Stop(compositor, band, 0.5 + 3 * Cell));
            gradient.ColorStops.Add(Stop(compositor, logo, 0.5 + 3.5 * Cell));
            gradient.ColorStops.Add(Stop(compositor, logo, 1));
            brush = gradient;
            CompositionBrush = gradient;
            At(still >= 0 ? still : 0);
            if (playing) Animate();
        }

        protected override void OnDisconnected()
        {
            base.OnDisconnected();
            note("sheen disconnected");
            CompositionBrush?.Dispose();
            CompositionBrush = null;
            brush = null;
        }

        static CompositionColorGradientStop Stop(Compositor compositor, Color colour, double at) => compositor.CreateColorGradientStop((float)at, colour);

        // Where the band is: p is the column it crosses the logo's first row at, so StartPoint and
        // EndPoint both slide with it (their midpoint is the band's centre line).
        void At(int at)
        {
            if (brush == null) return;
            double p = (at - Half) * CellW;
            brush.StartPoint = new Vector2((float)(p - dx), (float)-dy);
            brush.EndPoint = new Vector2((float)(p + dx), (float)dy);
        }

        void Animate()
        {
            if (brush == null) return;
            var start = compositor.CreateVector2KeyFrameAnimation();
            var end = compositor.CreateVector2KeyFrameAnimation();
            for (int i = 0; i < Frames; i++)
            {
                double p = (i - Half) * CellW;
                float at = (float)(i * FrameSeconds / TotalSeconds);
                start.InsertKeyFrame(at, new Vector2((float)(p - dx), (float)-dy));
                end.InsertKeyFrame(at, new Vector2((float)(p + dx), (float)dy));
            }
            // The last frame again at the end: that hold is the rest between sweeps.
            double last = (Frames - 1 - Half) * CellW;
            start.InsertKeyFrame(1, new Vector2((float)(last - dx), (float)-dy));
            end.InsertKeyFrame(1, new Vector2((float)(last + dx), (float)dy));
            foreach (var animation in new Vector2KeyFrameAnimation[] { start, end })
            {
                animation.Duration = TimeSpan.FromSeconds(TotalSeconds);
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
            }
            brush.StartAnimation("StartPoint", start);
            brush.StartAnimation("EndPoint", end);
        }

        public void Play()
        {
            playing = true;
            if (brush != null && still < 0) Animate();
        }

        public void Stop()
        {
            playing = false;
            brush?.StopAnimation("StartPoint");
            brush?.StopAnimation("EndPoint");
        }
    }

    // --- the sheet's lines --------------------------------------------------------------------
    // One row of the sheet, as the runs a terminal would draw it with.
    sealed record SheetLine(List<(string Text, Color Color)> Runs)
    {
        public int Cells => Runs.Sum(r => AboutWindow.Cells(r.Text));
        public string Text => string.Concat(Runs.Select(r => r.Text));
    }
}
