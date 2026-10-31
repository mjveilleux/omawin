using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
// The overlay and the note are plain Win32 windows on a thread of their own, drawn with GDI+
// (System.Drawing), kept warm from start-up so the first snip is as quick as the rest. Not
// WinForms, so the menu app compiles as one native program (Native AOT, docs/native-bar.md).
public enum SnipMode { Picture, Text, Color, Qr }

public static class Snip
{
    static Thread? thread;
    static SnipWindow? window;
    // Manual reset: every caller that waits (the start-up warm-up and a snip that comes before
    // it's done) sees it, not just the first.
    static readonly ManualResetEvent ready = new(false);
    static readonly object warming = new();
    static readonly ConcurrentQueue<Action> work = new();

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
                    Win.SetThreadDpiAwarenessContext(-4); // per monitor: real pixels everywhere
                    try { window = new SnipWindow(); } catch (Exception ex) { App.Log(ex); }
                    ready.Set();
                    Win.Loop(() => { while (work.TryDequeue(out var a)) try { a(); } catch (Exception ex) { App.Log(ex); } });
                }) { IsBackground = true, Name = "snip" };
                thread.SetApartmentState(ApartmentState.STA); // the clipboard and OCR want it
                thread.Start();
            }
        }
        ready.WaitOne(5000);
    }

    // Runs on the overlay's thread (its windows, the clipboard, the note live there).
    internal static void Post(Action action)
    {
        work.Enqueue(action);
        if (window != null) Win.PostMessage(window.Hwnd, Win.WorkMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Start(SnipMode mode = SnipMode.Picture)
    {
        Warm();
        if (window == null) throw new InvalidOperationException("The screenshot overlay didn't start");
        var shot = Capture(out var bounds);
        Post(() => window.Begin(shot, bounds, mode));
    }

    // The note at the bottom of the screen, from any thread (stay awake uses it too). A `--qs`
    // that starts the app can answer before the overlay's thread is up: then it waits for it,
    // off the caller's thread, instead of being dropped.
    public static void Say(string message)
    {
        if (window != null) { Post(() => Note.Show(message)); return; }
        Task.Run(() => { Warm(); Post(() => Note.Show(message)); });
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
        try { Clip.SetImage(window!.Hwnd, cut); } catch (Exception ex) { App.Log(ex); }
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
            byte[] pixels;
            int w, h;
            using (var cut = new Bitmap((int)(area.Width * scale), (int)(area.Height * scale), PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(cut))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shot, new Rectangle(0, 0, cut.Width, cut.Height), area, GraphicsUnit.Pixel);
                }
                (w, h) = (cut.Width, cut.Height);
                pixels = Pixels(cut);
            }
            using var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pixels),
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join("\n", result.Lines.Select(l => l.Text)).Trim();
            int lines = result.Lines.Count;
            Post(() =>
            {
                if (text.Length == 0) { Note.Show("No text found in that region"); return; }
                Clip.SetText(window!.Hwnd, text);
                int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                Note.Show(words == 1 ? "Copied 1 word" : $"Copied {words} words · {lines} line{(lines == 1 ? "" : "s")}");
            });
        }
        catch (Exception ex) { App.Log(ex); Post(() => Note.Show("Couldn't read text")); }
        finally { shot.Dispose(); }
    }

    // Colour mode: the pixel's hex on the clipboard.
    internal static void KeepColor(Color c)
    {
        var hex = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        try { Clip.SetText(window!.Hwnd, hex); Note.Show($"Copied {hex}", swatch: c); }
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
                pixels = Pixels(padded);
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
            Post(() =>
            {
                if (texts.Count == 0) { Note.Show("No QR code found in that region"); return; }
                Clip.SetText(window!.Hwnd, string.Join(Environment.NewLine, texts));
                var shown = texts[0].ReplaceLineEndings(" ");
                if (shown.Length > 90) shown = shown[..89] + "…";
                Note.Show(texts.Count == 1 ? $"QR copied: {shown}" : $"{texts.Count} QR codes copied · {shown} …", seconds: 4);
            });
        }
        catch (Exception ex) { App.Log(ex); Post(() => Note.Show("Couldn't read a QR code")); }
        finally { shot.Dispose(); }
    }

    // `SystemMenu --snip-selftest`: what can be checked without you: its windows made, the
    // screen copied, the text at the top of it read (the bar's clock), the note shown for a
    // moment. Nothing goes to the clipboard or Pictures. Printed; run beside the menu app.
    public static async Task<string> SelfTest()
    {
        var report = new System.Text.StringBuilder();
        Warm();
        report.AppendLine($"overlay window: {(window != null ? "made" : "NOT made")}");
        using var shot = Capture(out var bounds);
        report.AppendLine($"screen copied: {bounds.Width}x{bounds.Height} at {bounds.X},{bounds.Y}");
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) report.AppendLine("OCR: no language installed");
        else
        {
            var top = new Rectangle(0, 0, shot.Width, Math.Min(shot.Height, 40));
            using var cut = new Bitmap(top.Width * 2, top.Height * 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cut)) g.DrawImage(shot, new Rectangle(0, 0, cut.Width, cut.Height), top, GraphicsUnit.Pixel);
            using var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(Pixels(cut)),
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, cut.Width, cut.Height, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            report.AppendLine($"OCR of the top of the screen: \"{string.Join(" / ", result.Lines.Select(l => l.Text))}\"");
        }
        var shown = new ManualResetEventSlim();
        Post(() => { Note.Show("omawin screenshot self test", seconds: 1); shown.Set(); });
        report.AppendLine($"note shown: {(shown.Wait(3000) ? "yes" : "NO")}");
        await Task.Delay(1200);
        return report.ToString();
    }

    // Top-down BGRA, rows packed.
    static byte[] Pixels(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[b.Width * b.Height * 4];
        for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * b.Width * 4, b.Width * 4);
        b.UnlockBits(data);
        return pixels;
    }
}

// The overlay: one borderless, topmost window over every monitor, the frozen screen in it.
sealed class SnipWindow : Win.Window
{
    Bitmap? shot, dimmed;
    Rectangle screen;
    Point? from;
    Rectangle selection;
    Rectangle hover; // the window under the mouse, for a single click
    SnipMode mode;   // a picture, its text (OCR), a colour, or QR codes
    Point? mouse;    // colour mode: the pixel under the mouse (overlay coordinates)
    bool visible;
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

    public SnipWindow() : base("omawin snip", 0x80 | 0x8, 0x80000000) { } // WS_EX_TOOLWINDOW (not in Alt+Tab, not tiled) | TOPMOST; WS_POPUP

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
        var cursor = Win.Cursor();
        mouse = mode == SnipMode.Color ? new Point(cursor.X - bounds.X, cursor.Y - bounds.Y) : null;
        visible = true;
        Win.SetWindowPos(Hwnd, (IntPtr)(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040); // TOPMOST, SHOWWINDOW
        Win.SetForegroundWindow(Hwnd);
        Win.SetFocus(Hwnd);
        Invalidate(null);
    }

    void Finish(Rectangle? area)
    {
        if (!visible) return;
        visible = false;
        Win.ReleaseCapture();
        Win.ShowWindow(Hwnd, 0); // SW_HIDE
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
    [DllImport("user32.dll")] static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Win.RECT rect, int size);

    // The window under a screen point (the overlay, disabled for a moment, is passed over).
    Rectangle WindowAt(Point p)
    {
        EnableWindow(Hwnd, false);
        var hwnd = GetAncestor(WindowFromPoint(p), 2); // GA_ROOT
        EnableWindow(Hwnd, true);
        if (hwnd == Hwnd || hwnd == IntPtr.Zero) return Rectangle.Empty;
        if (DwmGetWindowAttribute(hwnd, 9, out var r, Marshal.SizeOf<Win.RECT>()) != 0) return Rectangle.Empty; // DWMWA_EXTENDED_FRAME_BOUNDS
        var rect = Rectangle.FromLTRB(r.Left - screen.X, r.Top - screen.Y, r.Right - screen.X, r.Bottom - screen.Y);
        rect.Intersect(new Rectangle(Point.Empty, screen.Size));
        return rect.Width > 0 && rect.Height > 0 && rect.Size != screen.Size ? rect : Rectangle.Empty;
    }

    protected override IntPtr Message(uint message, IntPtr wParam, IntPtr lParam)
    {
        var at = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        switch (message)
        {
            case 0x0020: Win.SetCursor(Win.LoadCursor(IntPtr.Zero, (IntPtr)32515)); return (IntPtr)1; // WM_SETCURSOR: the crosshair
            case 0x0204: Finish(null); return IntPtr.Zero; // WM_RBUTTONDOWN
            case 0x0201: MouseDown(at); return IntPtr.Zero; // WM_LBUTTONDOWN
            case 0x0200: MouseMove(at); return IntPtr.Zero; // WM_MOUSEMOVE
            case 0x0202: MouseUp(); return IntPtr.Zero; // WM_LBUTTONUP
            case 0x0100: KeyDown((int)wParam); return IntPtr.Zero; // WM_KEYDOWN
            case 0x0006: if ((wParam.ToInt64() & 0xFFFF) == 0 && visible) Finish(null); break; // WM_ACTIVATE: inactive
            case 0x000F: Paint(); return IntPtr.Zero; // WM_PAINT
            case 0x0014: return (IntPtr)1; // WM_ERASEBKGND: all painted in WM_PAINT
        }
        return base.Message(message, wParam, lParam);
    }

    void MouseDown(Point p)
    {
        if (mode == SnipMode.Color) { PickColor(p); return; }
        Win.SetCapture(Hwnd);
        from = p;
        selection = Rectangle.Empty;
        Invalidate(null); // the hint goes away
    }

    void MouseMove(Point p)
    {
        if (!visible) return;
        if (mode == SnipMode.Color) { MoveLoupe(p); return; }
        var old = Rectangle.Union(selection, hover);
        if (from is { } f)
        {
            selection = Rectangle.FromLTRB(Math.Min(f.X, p.X), Math.Min(f.Y, p.Y), Math.Max(f.X, p.X), Math.Max(f.Y, p.Y));
            hover = Rectangle.Empty;
        }
        else hover = WindowAt(new Point(p.X + screen.X, p.Y + screen.Y));
        var now = Rectangle.Union(selection, hover);
        var dirty = Rectangle.Union(old, now);
        dirty.Inflate(40, 40);
        Invalidate(dirty);
    }

    void MouseUp()
    {
        if (from == null) return;
        from = null;
        Win.ReleaseCapture();
        // A click (no drag): the window under the mouse.
        Finish(selection.Width >= 4 && selection.Height >= 4 ? selection : hover.IsEmpty ? null : hover);
    }

    void KeyDown(int key)
    {
        if (key == 0x1B) Finish(null); // Esc
        else if (mode == SnipMode.Color)
        {
            // The arrows nudge the mouse a pixel at a time, for the exact one.
            int dx = key == 0x25 ? -1 : key == 0x27 ? 1 : 0, dy = key == 0x26 ? -1 : key == 0x28 ? 1 : 0;
            if (dx != 0 || dy != 0) { var c = Win.Cursor(); Win.SetCursorPos(c.X + dx, c.Y + dy); }
            else if (key == 0x0D && mouse is { } m) PickColor(m);
        }
        else if (key == 0x54 && mode is SnipMode.Picture or SnipMode.Text) // T: picture <-> text
        {
            mode = mode == SnipMode.Text ? SnipMode.Picture : SnipMode.Text;
            Invalidate(null);
        }
        else if (key == 0x0D) Finish(new Rectangle(Point.Empty, screen.Size)); // Enter: everything
    }

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
        Invalidate(old);
        Invalidate(LoupeBounds());
    }

    // Down and right of the mouse, or flipped where that runs off its monitor.
    Rectangle LoupeBounds()
    {
        if (mouse is not { } m) return Rectangle.Empty;
        var mon = Win.MonitorAt(new Point(m.X + screen.X, m.Y + screen.Y), work: false);
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
        using var font = Win.Font("Cascadia Mono", 10.5f, FontStyle.Bold, Scale());
        g.DrawString($"#{c.R:x2}{c.G:x2}{c.B:x2}", font, Brushes.White, zoom.X + 22, zoom.Bottom + 6);
    }

    // The scale of the monitor under the mouse (the overlay spans them all).
    static float Scale() => Win.DpiAt(Win.Cursor()) / 96f;

    // What changed is drawn into a picture of its own, then put on the screen at once: no flicker.
    void Paint()
    {
        var ps = new Win.PAINTSTRUCT();
        var dc = Win.BeginPaint(Hwnd, ref ps);
        try
        {
            var clip = Rectangle.FromLTRB(ps.rcPaint.Left, ps.rcPaint.Top, ps.rcPaint.Right, ps.rcPaint.Bottom);
            if (dimmed == null || shot == null || clip.Width <= 0 || clip.Height <= 0) return;
            using var buffer = new Bitmap(clip.Width, clip.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(buffer))
            {
                g.TranslateTransform(-clip.X, -clip.Y);
                g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);
                Draw(g);
            }
            using var screenG = Graphics.FromHdc(dc);
            screenG.DrawImageUnscaled(buffer, clip.X, clip.Y);
        }
        catch (Exception ex) { App.Log(ex); }
        finally { Win.EndPaint(Hwnd, ref ps); }
    }

    void Draw(Graphics g)
    {
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
        g.DrawImage(shot!, area, area, GraphicsUnit.Pixel); // the picked part, undimmed
        using var pen = new Pen(Accent, 2);
        g.DrawRectangle(pen, area);
        // Its size, under it.
        var label = mode switch { SnipMode.Text => "copy the text in here", SnipMode.Qr => "read the QR code in here", _ => $"{area.Width} × {area.Height}" };
        using var font = Win.Font("Segoe UI Semibold", 10f, FontStyle.Regular, Scale());
        var size = g.MeasureString(label, font);
        var at = new PointF(area.Left, area.Bottom + 6 + size.Height > screen.Height ? area.Top - size.Height - 6 : area.Bottom + 6);
        using var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        g.FillRectangle(back, at.X, at.Y, size.Width + 8, size.Height + 2);
        g.DrawString(label, font, Brushes.White, at.X + 4, at.Y + 1);
    }

    void DrawHint(Graphics g, string text)
    {
        using var font = Win.Font("Segoe UI", 11f, FontStyle.Regular, Scale());
        var size = g.MeasureString(text, font);
        var bounds = Win.MonitorAt(Win.Cursor(), work: false);
        float x = bounds.X - screen.X + (bounds.Width - size.Width) / 2, y = bounds.Y - screen.Y + 80 * Scale();
        using var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        g.FillRectangle(back, x - 14, y - 8, size.Width + 28, size.Height + 16);
        g.DrawString(text, font, Brushes.White, x, y);
    }
}

// A small note at the bottom of the screen, for a moment: what a snip copied. On the overlay's
// thread (Snip.Post).
sealed class Note : Win.Window
{
    static Note? shown;
    readonly string message;
    readonly Color? swatch;
    readonly float scale;

    Note(string message, Color? swatch) : base("omawin note", 0x80 | 0x8 | 0x80000 | 0x08000000, 0x80000000) // tool, topmost, layered, no activate; popup
    {
        this.message = message;
        this.swatch = swatch;
        scale = Win.DpiAt(Win.Cursor()) / 96f;
    }

    // swatch: a square of that colour before the message (the colour picker's).
    public static void Show(string message, Color? swatch = null, double seconds = 1.8)
    {
        shown?.Close();
        var note = shown = new Note(message, swatch);
        var area = Win.MonitorAt(Win.Cursor(), work: true);
        using var font = Win.Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, note.scale);
        Size text;
        using (var probe = new Bitmap(1, 1)) using (var g = Graphics.FromImage(probe)) text = Size.Ceiling(g.MeasureString(message, font));
        int pad = (int)(16 * note.scale), padY = (int)(10 * note.scale), chip = swatch != null ? (int)(26 * note.scale) : 0;
        int w = text.Width + 2 * pad + chip, h = text.Height + 2 * padY;
        Win.SetLayeredWindowAttributes(note.Hwnd, 0, 242, 2); // 95% opaque (LWA_ALPHA)
        Win.SetWindowPos(note.Hwnd, (IntPtr)(-1), area.X + (area.Width - w) / 2, area.Bottom - h - (int)(90 * note.scale), w, h, 0x0040 | 0x0010); // SHOWWINDOW, NOACTIVATE
        Win.SetTimer(note.Hwnd, 1, (uint)(seconds * 1000), IntPtr.Zero);
    }

    void Close()
    {
        if (shown == this) shown = null;
        Destroy();
    }

    protected override IntPtr Message(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case 0x0113: Close(); return IntPtr.Zero; // WM_TIMER: its time is up
            case 0x0014: return (IntPtr)1;
            case 0x000F:
                var ps = new Win.PAINTSTRUCT();
                var dc = Win.BeginPaint(Hwnd, ref ps);
                try
                {
                    Win.GetClientRect(Hwnd, out var r);
                    using var g = Graphics.FromHdc(dc);
                    g.Clear(Color.FromArgb(40, 40, 40));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    int pad = (int)(16 * scale), x = pad;
                    if (swatch is { } c)
                    {
                        int s = (int)(18 * scale);
                        using var b = new SolidBrush(c);
                        g.FillRectangle(b, pad, (r.Bottom - s) / 2, s, s);
                        g.DrawRectangle(Pens.Gray, pad, (r.Bottom - s) / 2, s, s);
                        x += (int)(26 * scale);
                    }
                    using var font = Win.Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, scale);
                    g.DrawString(this.message, font, Brushes.White, x, (int)(10 * scale));
                }
                finally { Win.EndPaint(Hwnd, ref ps); }
                return IntPtr.Zero;
        }
        return base.Message(message, wParam, lParam);
    }
}

// --- plain Win32 windows, for the overlay and the note ---------------------------------------
static unsafe class Win
{
    public const uint WorkMessage = 0x8001; // WM_APP + 1: Snip.Post's queue has something

    // A window of its own class; its messages come to Message (on the thread that made it).
    public abstract class Window
    {
        static readonly Dictionary<IntPtr, Window> windows = [];
        static readonly HashSet<string> registered = [];
        public readonly IntPtr Hwnd;

        protected Window(string className, uint exStyle, uint style)
        {
            var instance = GetModuleHandle(IntPtr.Zero);
            lock (registered)
                if (registered.Add(className))
                {
                    var name = Marshal.StringToHGlobalUni(className); // kept: the class holds it
                    var cls = new WNDCLASSEX { cbSize = (uint)sizeof(WNDCLASSEX), lpfnWndProc = &Proc, hInstance = instance, lpszClassName = name };
                    if (RegisterClassEx(ref cls) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx " + className);
                }
            Hwnd = CreateWindowEx(exStyle, className, className, style, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (Hwnd == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx " + className);
            lock (windows) windows[Hwnd] = this;
        }

        protected virtual IntPtr Message(uint message, IntPtr wParam, IntPtr lParam) => DefWindowProc(Hwnd, message, wParam, lParam);

        protected void Invalidate(Rectangle? area)
        {
            if (area is not { } a) { InvalidateRect(Hwnd, null, false); return; }
            var r = new RECT { Left = a.Left, Top = a.Top, Right = a.Right, Bottom = a.Bottom };
            InvalidateRect(Hwnd, &r, false);
        }

        protected void Destroy()
        {
            lock (windows) windows.Remove(Hwnd);
            DestroyWindow(Hwnd);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static IntPtr Proc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (message == WorkMessage) { loopWork?.Invoke(); return IntPtr.Zero; }
                Window? w;
                lock (windows) windows.TryGetValue(hwnd, out w);
                if (w != null) return w.Message(message, wParam, lParam);
            }
            catch (Exception ex) { App.Log(ex); }
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    static Action? loopWork;

    // The thread's message loop; `work` runs whenever Snip.Post wakes it.
    public static void Loop(Action work)
    {
        loopWork = work;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    public static Point Cursor() { GetCursorPos(out var p); return p; }

    // The monitor at a point (its whole area, or its work area: without the taskbar).
    public static Rectangle MonitorAt(Point p, bool work)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfo(MonitorFromPoint(p, 2), ref info);
        var r = work ? info.rcWork : info.rcMonitor;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static uint DpiAt(Point p)
    {
        try { GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out uint dpi, out _); return dpi; }
        catch { return 96; }
    }

    // A font of `points` at this scale, in pixels (these windows draw in real pixels).
    public static Font Font(string family, float points, FontStyle style, float scale) => new(family, points * scale * 96f / 72f, style, GraphicsUnit.Pixel);

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public fixed byte rgbReserved[32]; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hwnd, RECT* rect, bool erase);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int show);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
}

// The clipboard, straight through Win32 (WinForms' Clipboard was all this needed of it): text,
// and a picture as a DIB (Windows makes the other picture formats from it for whoever asks).
static class Clip
{
    public static void SetText(IntPtr owner, string text)
    {
        var bytes = (text.Length + 1) * 2;
        var memory = GlobalAlloc(0x0002, (nuint)bytes); // GMEM_MOVEABLE
        var p = GlobalLock(memory);
        Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
        Marshal.WriteInt16(p, text.Length * 2, 0);
        GlobalUnlock(memory);
        Put(owner, 13, memory); // CF_UNICODETEXT
    }

    public static void SetImage(IntPtr owner, Bitmap picture)
    {
        int w = picture.Width, h = picture.Height, stride = w * 4;
        var memory = GlobalAlloc(0x0002, (nuint)(40 + stride * h));
        var p = GlobalLock(memory);
        // BITMAPINFOHEADER: 32 bits a pixel, bottom-up rows (a positive height), no compression.
        Marshal.WriteInt32(p, 0, 40);
        Marshal.WriteInt32(p, 4, w);
        Marshal.WriteInt32(p, 8, h);
        Marshal.WriteInt16(p, 12, 1);
        Marshal.WriteInt16(p, 14, 32);
        for (int i = 16; i < 40; i += 4) Marshal.WriteInt32(p, i, 0);
        var data = picture.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var row = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, stride);
            Marshal.Copy(row, 0, p + 40 + (h - 1 - y) * stride, stride);
        }
        picture.UnlockBits(data);
        GlobalUnlock(memory);
        Put(owner, 8, memory); // CF_DIB
    }

    // Another program can have the clipboard open for a moment: tried a few times.
    static void Put(IntPtr owner, uint format, IntPtr memory)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner))
            {
                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(format, memory) != IntPtr.Zero) return; // the clipboard owns it now
                }
                finally { CloseClipboard(); }
                break;
            }
            Thread.Sleep(20);
        }
        GlobalFree(memory);
        throw new InvalidOperationException("the clipboard is busy");
    }

    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
}
