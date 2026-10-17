using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SystemMenu;

// Screenshots (Win+Shift+S), nearly instant: this app is always running, so a snip is the
// screen copied (BitBlt, a few ms) and a borderless window over every monitor showing that
// frozen picture, dimmed; drag a region (or click a window, or Enter for everything) and it's
// on the clipboard and saved to Pictures\Screenshots. Esc or right-click cancels. Windows'
// Snipping Tool, which starts up each time, isn't involved.
//
// Text (Win+Shift+T, or T in the overlay), like Omarchy's "capture text": the region's text,
// read by Windows' own OCR engine (Windows.Media.Ocr: offline, in the languages Windows has),
// goes to the clipboard instead. A small note says what was copied.
//
// Colour (`omawin capture color`): the frozen screen, undimmed, with a loupe under the mouse
// and the hex of the pixel in its middle; a click (or Enter) copies it as #rrggbb.
//
// QR (`omawin capture qr`): pick a region like a screenshot; the QR codes in it are read
// (ZXing.Net, offline) and their text copied, never opened.
//
// The overlay is a plain WinForms window on its own thread, kept warm from start-up so the
// first snip is as quick as the rest.
public enum SnipMode { Picture, Text, Color, Qr }

public static class Snip
{
    static Thread? thread;
    static SnipForm? form;
    // Manual reset: every caller that waits (the start-up warm-up and a snip that comes before
    // it's done) sees it, not just the first.
    static readonly ManualResetEvent ready = new(false);
    static readonly object warming = new();

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    // A UI thread of its own, with the overlay made (hidden) and its code loaded.
    public static void Warm()
    {
        lock (warming)
        {
            if (thread == null)
            {
                thread = new Thread(() =>
                {
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    form = new SnipForm();
                    var _ = form.Handle; // create it now, not on the first snip
                    ready.Set();
                    Application.Run();
                }) { IsBackground = true, Name = "snip" };
                thread.SetApartmentState(ApartmentState.STA); // the clipboard needs it
                thread.Start();
            }
        }
        ready.WaitOne(5000);
    }

    public static void Start(SnipMode mode = SnipMode.Picture)
    {
        Warm();
        if (form == null) throw new InvalidOperationException("The screenshot overlay didn't start");
        var shot = Capture(out var bounds);
        form.BeginInvoke(() => form.Begin(shot, bounds, mode));
    }

    // The note at the bottom of the screen, from any thread (stay awake uses it too).
    // A `--qs` that starts the app answers before the overlay's thread (started alongside it in
    // OnLaunched) has made its window; then the note waits for it, off the caller's thread,
    // instead of being dropped.
    public static void Say(string message)
    {
        if (form is { IsHandleCreated: true } f) { f.BeginInvoke(() => Note.Show(message)); return; }
        Task.Run(() =>
        {
            Warm();
            if (form is { IsHandleCreated: true } made) made.BeginInvoke(() => Note.Show(message));
        });
    }

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    // Every monitor, in physical pixels.
    static Bitmap Capture(out Rectangle bounds)
    {
        bounds = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); // SM_X/Y/CX/CYVIRTUALSCREEN
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bitmap);
        var dst = g.GetHdc();
        var src = GetDC(IntPtr.Zero);
        BitBlt(dst, 0, 0, bounds.Width, bounds.Height, src, bounds.X, bounds.Y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
        ReleaseDC(IntPtr.Zero, src);
        g.ReleaseHdc(dst);
        return bitmap;
    }

    // What's picked: on the clipboard, and saved as a PNG.
    internal static void Keep(Bitmap shot, Rectangle area)
    {
        if (area.Width < 2 || area.Height < 2) return;
        using var cut = shot.Clone(area, PixelFormat.Format32bppRgb);
        try { Clipboard.SetImage(cut); } catch (Exception ex) { App.Log(ex); }
        try
        {
            Directory.CreateDirectory(Folder);
            cut.Save(Path.Combine(Folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png"), ImageFormat.Png);
            Note.Show("Screenshot copied · saved to Pictures\\Screenshots");
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // Text mode: the region's text on the clipboard. Small text reads better bigger, so short
    // regions are scaled up first (Windows' OCR wants letters at least ~20 px tall). It owns the
    // screenshot and disposes it, however it ends.
    internal static async void KeepText(Bitmap shot, Rectangle area)
    {
        try
        {
            if (area.Width < 2 || area.Height < 2) return;
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Note.Show("No OCR language installed (Settings > Time & language > Language)"); return; }
            double scale = Math.Clamp(1200.0 / Math.Max(area.Width, area.Height), 1, 3);
            using var cut = new Bitmap((int)(area.Width * scale), (int)(area.Height * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cut))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(shot, new Rectangle(0, 0, cut.Width, cut.Height), area, GraphicsUnit.Pixel);
            }
            var data = cut.LockBits(new Rectangle(0, 0, cut.Width, cut.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[data.Stride * cut.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            cut.UnlockBits(data);
            using var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pixels),
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, cut.Width, cut.Height, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join("\n", result.Lines.Select(l => l.Text)).Trim();
            if (text.Length == 0) { Note.Show("No text found in that region"); return; }
            Clipboard.SetText(text);
            int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            Note.Show(words == 1 ? "Copied 1 word" : $"Copied {words} words · {result.Lines.Count} line{(result.Lines.Count == 1 ? "" : "s")}");
        }
        catch (Exception ex) { App.Log(ex); Note.Show("Couldn't read text"); }
        finally { shot.Dispose(); }
    }

    // Colour mode: the pixel's hex on the clipboard.
    internal static void KeepColor(Color c)
    {
        var hex = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        try { Clipboard.SetText(hex); Note.Show($"Copied {hex}", swatch: c); }
        catch (Exception ex) { App.Log(ex); }
    }

    // QR mode: every QR code in the region, read off the UI thread, its text on the clipboard.
    // A white margin goes around the region, since a tight drag cuts the code's quiet zone,
    // which the reader needs. It owns the screenshot and disposes it, however it ends.
    internal static async void KeepQr(Bitmap shot, Rectangle area)
    {
        try
        {
            if (area.Width < 8 || area.Height < 8) return;
            const int margin = 24;
            int w = area.Width + 2 * margin, h = area.Height + 2 * margin;
            byte[] pixels;
            using (var padded = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(padded))
                {
                    g.Clear(Color.White);
                    g.DrawImage(shot, new Rectangle(margin, margin, area.Width, area.Height), area, GraphicsUnit.Pixel);
                }
                var data = padded.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                pixels = new byte[w * h * 4];
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * w * 4, w * 4);
                padded.UnlockBits(data);
            }
            shot.Dispose();
            var texts = await Task.Run(() =>
            {
                var reader = new ZXing.BarcodeReaderGeneric { AutoRotate = true };
                reader.Options.PossibleFormats = [ZXing.BarcodeFormat.QR_CODE];
                reader.Options.TryHarder = true;
                var source = new ZXing.RGBLuminanceSource(pixels, w, h, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
                var found = reader.DecodeMultiple(source) ?? (reader.Decode(source) is { } one ? [one] : []);
                return found.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            });
            if (texts.Count == 0) { Note.Show("No QR code found in that region"); return; }
            Clipboard.SetText(string.Join(Environment.NewLine, texts));
            var shown = texts[0].ReplaceLineEndings(" ");
            if (shown.Length > 90) shown = shown[..89] + "…";
            Note.Show(texts.Count == 1 ? $"QR copied: {shown}" : $"{texts.Count} QR codes copied · {shown} …", seconds: 4);
        }
        catch (Exception ex) { App.Log(ex); Note.Show("Couldn't read a QR code"); }
        finally { shot.Dispose(); }
    }
}

sealed class SnipForm : Form
{
    Bitmap? shot, dimmed;
    Rectangle screen;
    Point? from;
    Rectangle selection;
    Rectangle hover; // the window under the mouse, for a single click
    SnipMode mode;   // a picture, its text (OCR), a colour, or QR codes
    Point? mouse;    // colour mode: the pixel under the mouse (overlay coordinates)
    // The selection's color: the current theme's accent (themes/<name>.json).
    static string? accentTheme;
    static Color accent;
    static Color Accent
    {
        get
        {
            var name = MenuModel.CurrentThemeName();
            if (name != accentTheme)
            {
                var hex = MenuModel.LoadTheme(name).Accent.TrimStart('#');
                try { accent = Color.FromArgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)); }
                catch { accent = Color.FromArgb(0x7d, 0xae, 0xa3); }
                accentTheme = name;
            }
            return accent;
        }
    }

    public SnipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x80; return p; } // WS_EX_TOOLWINDOW: not in Alt+Tab, not tiled
    }

    public void Begin(Bitmap image, Rectangle bounds, SnipMode snipMode = SnipMode.Picture)
    {
        mode = snipMode;
        shot?.Dispose(); dimmed?.Dispose();
        shot = image;
        screen = bounds;
        dimmed = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(dimmed))
        {
            g.DrawImageUnscaled(image, 0, 0);
            // Colour mode shows the screen as it is: dimmed, every colour would be wrong.
            using var veil = new SolidBrush(Color.FromArgb(mode == SnipMode.Color ? 0 : 150, 0, 0, 0));
            g.FillRectangle(veil, 0, 0, image.Width, image.Height);
        }
        from = null;
        selection = hover = Rectangle.Empty;
        // Colour mode: the loupe starts where the mouse is.
        mouse = mode == SnipMode.Color ? new Point(Cursor.Position.X - bounds.X, Cursor.Position.Y - bounds.Y) : null;
        Bounds = bounds;
        Show();
        Activate();
        Bounds = bounds; // again: Show can move it to the primary monitor's DPI
        Invalidate();
    }

    void Finish(Rectangle? area)
    {
        Hide();
        var picture = shot;
        shot = null;
        dimmed?.Dispose();
        dimmed = null;
        if (area is { } a && picture != null)
        {
            if (mode == SnipMode.Text) Snip.KeepText(picture, a); // reads it, then it's done with the picture
            else if (mode == SnipMode.Qr) Snip.KeepQr(picture, a);
            else if (mode == SnipMode.Color) { Snip.KeepColor(picture.GetPixel(a.X, a.Y)); picture.Dispose(); }
            else { Snip.Keep(picture, a); picture.Dispose(); }
        }
        else picture?.Dispose();
    }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    // The window under a screen point (this overlay is click-through for the question).
    Rectangle WindowAt(Point p)
    {
        Enabled = false;
        var hwnd = GetAncestor(WindowFromPoint(p), 2); // GA_ROOT
        Enabled = true;
        if (hwnd == Handle || hwnd == IntPtr.Zero) return Rectangle.Empty;
        if (DwmGetWindowAttribute(hwnd, 9, out var r, Marshal.SizeOf<RECT>()) != 0) return Rectangle.Empty; // DWMWA_EXTENDED_FRAME_BOUNDS
        var rect = Rectangle.FromLTRB(r.Left - screen.X, r.Top - screen.Y, r.Right - screen.X, r.Bottom - screen.Y);
        rect.Intersect(new Rectangle(Point.Empty, screen.Size));
        return rect.Width > 0 && rect.Height > 0 && rect.Size != screen.Size ? rect : Rectangle.Empty;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Finish(null); return; }
        if (mode == SnipMode.Color) { PickColor(e.Location); return; }
        from = e.Location;
        selection = Rectangle.Empty;
        Invalidate(); // the hint goes away
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (mode == SnipMode.Color) { MoveLoupe(e.Location); return; }
        var old = Rectangle.Union(selection, hover);
        if (from is { } f)
        {
            selection = Rectangle.FromLTRB(Math.Min(f.X, e.X), Math.Min(f.Y, e.Y), Math.Max(f.X, e.X), Math.Max(f.Y, e.Y));
            hover = Rectangle.Empty;
        }
        else hover = WindowAt(new Point(e.X + screen.X, e.Y + screen.Y));
        var now = Rectangle.Union(selection, hover);
        var dirty = Rectangle.Union(old, now);
        dirty.Inflate(40, 40);
        Invalidate(dirty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (from == null || e.Button != MouseButtons.Left) return;
        from = null;
        // A click (no drag): the window under the mouse.
        Finish(selection.Width >= 4 && selection.Height >= 4 ? selection : hover.IsEmpty ? null : hover);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Finish(null);
        else if (mode == SnipMode.Color)
        {
            // The arrows nudge the mouse a pixel at a time, for the exact one.
            int dx = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0, dy = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0;
            if (dx != 0 || dy != 0) Cursor.Position = new Point(Cursor.Position.X + dx, Cursor.Position.Y + dy);
            else if (e.KeyCode == Keys.Enter && mouse is { } m) PickColor(m);
        }
        else if (e.KeyCode == Keys.T && mode is SnipMode.Picture or SnipMode.Text) // picture <-> text
        {
            mode = mode == SnipMode.Text ? SnipMode.Picture : SnipMode.Text;
            Invalidate();
        }
        else if (e.KeyCode == Keys.Enter) Finish(new Rectangle(Point.Empty, screen.Size)); // everything
    }

    // Arrow keys reach OnKeyDown instead of moving focus.
    protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(key);

    // --- colour mode ------------------------------------------------------------------------
    // The loupe: 11 × 11 pixels around the mouse, 12× bigger, the middle one ringed, and its hex.
    const int LoupeCells = 11, LoupeZoom = 12, LoupeSize = LoupeCells * LoupeZoom, LoupeLabel = 30;

    void PickColor(Point p)
    {
        if (shot == null || !new Rectangle(Point.Empty, shot.Size).Contains(p)) return;
        Finish(new Rectangle(p, new Size(1, 1)));
    }

    void MoveLoupe(Point p)
    {
        var old = LoupeBounds();
        mouse = p;
        var now = LoupeBounds();
        Invalidate(old);
        Invalidate(now);
    }

    // Down and right of the mouse, or flipped where that runs off its monitor.
    Rectangle LoupeBounds()
    {
        if (mouse is not { } m) return Rectangle.Empty;
        var mon = Screen.FromPoint(new Point(m.X + screen.X, m.Y + screen.Y)).Bounds;
        mon.Offset(-screen.X, -screen.Y);
        int w = LoupeSize + 4, h = LoupeSize + LoupeLabel + 4;
        int x = m.X + 24 + w > mon.Right ? m.X - 24 - w : m.X + 24;
        int y = m.Y + 24 + h > mon.Bottom ? m.Y - 24 - h : m.Y + 24;
        return new Rectangle(x, y, w + 1, h + 1);
    }

    void DrawLoupe(Graphics g)
    {
        if (mouse is not { } m || shot == null) return;
        var box = LoupeBounds();
        var zoom = new Rectangle(box.X + 2, box.Y + 2, LoupeSize, LoupeSize);
        using var back = new SolidBrush(Color.FromArgb(235, 30, 30, 30));
        g.FillRectangle(back, box.X, box.Y, box.Width - 1, box.Height - 1);
        var state = g.Save();
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(shot, zoom, new Rectangle(m.X - LoupeCells / 2, m.Y - LoupeCells / 2, LoupeCells, LoupeCells), GraphicsUnit.Pixel);
        g.Restore(state);
        var middle = new Rectangle(zoom.X + LoupeCells / 2 * LoupeZoom, zoom.Y + LoupeCells / 2 * LoupeZoom, LoupeZoom, LoupeZoom);
        g.DrawRectangle(Pens.Black, middle);
        middle.Inflate(1, 1);
        g.DrawRectangle(Pens.White, middle);
        using var pen = new Pen(Accent, 2);
        g.DrawRectangle(pen, zoom);
        var c = shot.GetPixel(Math.Clamp(m.X, 0, shot.Width - 1), Math.Clamp(m.Y, 0, shot.Height - 1));
        using var chip = new SolidBrush(c);
        g.FillRectangle(chip, zoom.X, zoom.Bottom + 7, 16, 16);
        g.DrawRectangle(Pens.Gray, zoom.X, zoom.Bottom + 7, 16, 16);
        using var font = new Font("Cascadia Mono, Consolas", 10.5f, FontStyle.Bold);
        g.DrawString($"#{c.R:x2}{c.G:x2}{c.B:x2}", font, Brushes.White, zoom.X + 22, zoom.Bottom + 6);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (Visible) Finish(null);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (dimmed == null || shot == null) return;
        var g = e.Graphics;
        g.DrawImage(dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
        if (mode == SnipMode.Color)
        {
            DrawHint(g, "Colour: click to copy its hex · arrows: one pixel · Esc: cancel");
            DrawLoupe(g);
            return;
        }
        var area = !selection.IsEmpty ? selection : hover;
        if (area.IsEmpty)
        {
            DrawHint(g, mode switch
            {
                SnipMode.Text => "Text: drag over what to read · T: picture instead · Esc: cancel",
                SnipMode.Qr => "QR: drag over the code · click a window · Enter: everything · Esc: cancel",
                _ => "Drag a region · click a window · Enter: everything · T: text · Esc: cancel",
            });
            return;
        }
        g.DrawImage(shot, area, area, GraphicsUnit.Pixel); // the picked part, undimmed
        using var pen = new Pen(Accent, 2);
        g.DrawRectangle(pen, area);
        // Its size, under it.
        var label = mode switch { SnipMode.Text => "copy the text in here", SnipMode.Qr => "read the QR code in here", _ => $"{area.Width} × {area.Height}" };
        using var font = new Font("Segoe UI Semibold", 10f);
        var size = g.MeasureString(label, font);
        var at = new PointF(area.Left, area.Bottom + 6 + size.Height > Height ? area.Top - size.Height - 6 : area.Bottom + 6);
        using var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        g.FillRectangle(back, at.X, at.Y, size.Width + 8, size.Height + 2);
        g.DrawString(label, font, Brushes.White, at.X + 4, at.Y + 1);
    }

    void DrawHint(Graphics g, string text)
    {
        using var font = new Font("Segoe UI", 11f);
        var size = g.MeasureString(text, font);
        var bounds = Screen.FromPoint(MousePosition).Bounds;
        float x = bounds.X - screen.X + (bounds.Width - size.Width) / 2, y = bounds.Y - screen.Y + 80;
        using var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        g.FillRectangle(back, x - 14, y - 8, size.Width + 28, size.Height + 16);
        g.DrawString(text, font, Brushes.White, x, y);
    }
}

// A small note at the bottom of the screen, for a moment: what a snip copied.
static class Note
{
    static Form? shown;

    // swatch: a square of that colour before the message (the colour picker's).
    public static void Show(string message, Color? swatch = null, double seconds = 1.8)
    {
        shown?.Close();
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        var label = new Label
        {
            Text = message, AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f),
            Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(40, 40, 40),
        };
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(40, 40, 40), Opacity = 0.95,
        };
        if (swatch is { } c)
        {
            label.Left = 34;
            form.Controls.Add(new Panel { BackColor = c, Size = new Size(18, 18), Location = new Point(16, (label.PreferredHeight - 18) / 2), BorderStyle = BorderStyle.FixedSingle });
        }
        form.Controls.Add(label);
        form.Load += (_, _) => form.Location = new Point(screen.X + (screen.Width - form.Width) / 2, screen.Bottom - form.Height - 90);
        shown = form;
        form.Show();
        var timer = new System.Windows.Forms.Timer { Interval = (int)(seconds * 1000) };
        timer.Tick += (_, _) => { timer.Dispose(); form.Close(); if (shown == form) shown = null; };
        timer.Start();
    }
}
