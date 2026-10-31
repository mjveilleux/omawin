using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using WinRT.Interop;

namespace SystemMenu;

// Quick Look's window (roadmap 8.4, QuickLook.cs): borderless, translucent and rounded like the
// launcher, centred on Explorer's monitor, in the theme. The file's name and facts on top; under
// them the file, drawn the way the finder's preview draws it (FilePreview.cs decides: a picture,
// text, a folder's contents) and, for what omawin can't draw (Word, PDF, …), by Windows' own
// preview handler (PreviewHandlerHost.cs), else its thumbnail. Space or Esc closes it and
// Explorer has the keys again; ← and → step through the selection; Enter opens the file in its
// app. Clicking elsewhere closes it.
//
// One window, made the first time and hidden in between (as the launcher is), so every Space
// after the first answers at once.
sealed partial class QuickLookWindow : Window
{
    static QuickLookWindow? instance;

    readonly nint hwnd;
    readonly AppWindow appWindow;
    nint explorer;
    List<string> paths = [];
    Theme theme = new();
    string themeName = "";
    readonly FontFamily textFont = new("Segoe UI Variable Text, Segoe UI");
    readonly FontFamily monoFont = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas");
    readonly FontFamily iconFont = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Segoe Fluent Icons");
    readonly ContentControl keys = new() { IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly Grid root = new() { Padding = new Thickness(18, 14, 18, 12), RowSpacing = 10 };
    readonly TextBlock name = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock counter = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel facts = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly Border body = new() { CornerRadius = new CornerRadius(10) };
    readonly TextBlock footer = new() { FontSize = 12 };
    PreviewHandlerHost? handler;
    int index, token;
    bool showing;
    DateTime shownAt;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    // Shows the selection of the Explorer window `explorer` (on the UI thread).
    public static void Open(List<string> paths, nint explorer)
    {
        instance ??= new QuickLookWindow();
        instance.Arrive(paths, explorer);
    }

    QuickLookWindow()
    {
        hwnd = WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        appWindow.SetPresenter(presenter);
        appWindow.Title = "omawin quick look";
        // A tool window: not on the taskbar or in Alt+Tab; GlazeWM leaves SystemMenu's windows be.
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x80);
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        SystemBackdrop = new DesktopAcrylicBackdrop();

        name.FontFamily = textFont;
        counter.FontFamily = textFont;
        footer.FontFamily = textFont;

        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(name);
        Grid.SetColumn(counter, 1);
        top.Children.Add(counter);

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(top);
        Grid.SetRow(facts, 1);
        root.Children.Add(facts);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        keys.Content = root;
        Content = keys;

        // The keys, before any control inside (a text preview's scroller) takes them.
        keys.PreviewKeyDown += OnKey;
        body.SizeChanged += (_, _) => handler?.Move(BodyPixels());
        Activated += (_, e) =>
        {
            // Clicking elsewhere closes it (but not the focus shuffle right as it appears).
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (DateTime.Now - shownAt > TimeSpan.FromMilliseconds(400)) CloseNow(backToExplorer: false);
            }
            else keys.Focus(FocusState.Programmatic);
        };
        appWindow.Closing += (_, e) => { e.Cancel = true; CloseNow(backToExplorer: true); };
    }

    // The theme's colours, read again only when the theme has changed since the last showing.
    void Paint()
    {
        var now = MenuModel.CurrentThemeName();
        if (now == themeName) return;
        themeName = now;
        theme = MenuModel.LoadTheme(now);
        name.Foreground = Brush(theme.Foreground);
        counter.Foreground = Brush(theme.Comment);
        footer.Foreground = Brush(theme.Comment);
        body.Background = Brush(theme.Background, 0x90);
        root.Background = Brush(theme.Background, 0x60);
    }

    void Arrive(List<string> selection, nint from)
    {
        if (showing) CloseNow(backToExplorer: false);
        paths = selection;
        explorer = from;
        Paint();
        // Centred on Explorer's monitor, at its scale.
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(explorer), DisplayAreaFallback.Nearest).WorkArea;
        double scale = Math.Max(96u, GetDpiForWindow(explorer)) / 96.0;
        var at = FilePreview.Place(new PixelBox(area.X, area.Y, area.X + area.Width, area.Y + area.Height), scale);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(at.Left, at.Top, at.Width, at.Height));
        showing = true;
        shownAt = DateTime.Now;
        ShowFile(0);
        appWindow.Show();
        // Windows gives the foreground only to the program that had the last input: a key of our
        // own (unassigned, tagged, so nothing acts on it) makes that us. (check)
        KeyHook.Press(CopilotKey.VkMask);
        SetForegroundWindow(hwnd);
        keys.Focus(FocusState.Programmatic);
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            // The Space that opened it may still be repeating: only a fresh press closes it.
            case VirtualKey.Space when !e.KeyStatus.WasKeyDown:
            case VirtualKey.Escape:
                CloseNow(backToExplorer: true);
                break;
            case VirtualKey.Space:
                break;
            case VirtualKey.Left when paths.Count > 1:
                ShowFile(FilePreview.Step(index, paths.Count, -1));
                break;
            case VirtualKey.Right when paths.Count > 1:
                ShowFile(FilePreview.Step(index, paths.Count, +1));
                break;
            case VirtualKey.Enter:
                var path = paths[index];
                CloseNow(backToExplorer: false);
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { App.Log(ex); }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Hidden, the preview let go (a handler's process ends with it).
    void CloseNow(bool backToExplorer)
    {
        if (!showing) return;
        showing = false;
        ++token;
        handler?.Dispose();
        handler = null;
        body.Child = null;
        // Focus first, so the next key goes to Explorer at once.
        if (backToExplorer) SetForegroundWindow(explorer);
        appWindow.Hide();
    }

    // --- one file ---------------------------------------------------------------------------------
    void ShowFile(int at)
    {
        index = at;
        int mine = ++token;
        handler?.Dispose();
        handler = null;
        var path = paths[at];
        name.Text = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
        counter.Text = paths.Count > 1 ? $"{at + 1} of {paths.Count}" : "";
        footer.Text = paths.Count > 1 ? "space close   ← → the others   ↵ open" : "space close   ↵ open";
        facts.Children.Clear();
        body.Child = Glyph(0xF0214, theme.Muted);

        // What it is, off the UI thread: a file on a slow share must not hold the window up.
        Task.Run(() => Look(path)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (mine != token || !showing || !t.IsCompletedSuccessfully) return;
            Draw(path, t.Result, mine);
        }));
    }

    sealed record Facts(PreviewKind Kind, bool IsDir, long Size, DateTime Modified, Guid? Handler, string Text);

    static Facts Look(string path)
    {
        bool isDir = Directory.Exists(path);
        if (isDir)
        {
            string listing;
            try
            {
                listing = string.Join("\n", Directory.EnumerateFileSystemEntries(path).Take(400)
                    .Select(e => Directory.Exists(e) ? Path.GetFileName(e) + "\\" : Path.GetFileName(e)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { listing = ""; }
            return new Facts(PreviewKind.Folder, true, 0, Directory.GetLastWriteTime(path), null, listing);
        }
        var info = new FileInfo(path);
        long size = info.Exists ? info.Length : 0;
        var head = new byte[8000];
        int read = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { head[0] = 0; read = 1; } // unreadable: not text
        var clsid = PreviewHandlerHost.For(path);
        var kind = FilePreview.Kind(path, false, size, clsid != null, head.AsSpan(0, read));
        string text = "";
        if (kind == PreviewKind.Text)
        {
            try { text = string.Join("\n", FilePreview.Lines(File.ReadLines(path), 2000, 400)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { kind = clsid != null ? PreviewKind.Handler : PreviewKind.Icon; }
        }
        return new Facts(kind, false, size, info.Exists ? info.LastWriteTime : default, clsid, text);
    }

    void Draw(string path, Facts f, int mine)
    {
        facts.Children.Add(Pill(f.IsDir ? "Folder" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "File", theme.Accent));
        if (!f.IsDir) facts.Children.Add(Pill(Size(f.Size), theme.Muted));
        if (f.Modified != default) facts.Children.Add(Pill(f.Modified.ToString("d MMM yyyy, HH:mm"), theme.Muted));
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1;

        switch (f.Kind)
        {
            case PreviewKind.Folder:
            case PreviewKind.Text:
                body.Child = new ScrollViewer
                {
                    Padding = new Thickness(14, 12, 14, 12),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = f.Text.Length > 0 ? f.Text : f.IsDir ? "(empty)" : "",
                        FontFamily = monoFont, FontSize = 12.5, Foreground = Brush(theme.Foreground), IsTextSelectionEnabled = true,
                    },
                };
                break;
            case PreviewKind.Picture:
                ImageSource source;
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) source = new SvgImageSource(new Uri(path));
                else
                {
                    // Decoded at the body's width, not the photo's own (a 50-megapixel photo stays light).
                    var bitmap = new BitmapImage();
                    int pixels = (int)(body.ActualWidth * scale);
                    if (pixels > 0) { bitmap.DecodePixelType = DecodePixelType.Physical; bitmap.DecodePixelWidth = pixels; }
                    bitmap.UriSource = new Uri(path);
                    source = bitmap;
                }
                body.Child = new Image { Source = source, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
                break;
            case PreviewKind.Handler when f.Handler is { } clsid:
                body.Child = null;
                // Once the body's laid out, so the handler's window lands on it.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (mine != token || !showing) return;
                    handler = PreviewHandlerHost.Show(clsid, path, hwnd, BodyPixels(), LightColor.Parse(theme.Background), LightColor.Parse(theme.Foreground));
                    if (handler == null) Thumbnail(path, mine);
                });
                break;
            default:
                Thumbnail(path, mine);
                break;
        }
    }

    // Windows' thumbnail (a video's frame, a document's first page where it has one) or the icon.
    void Thumbnail(string path, int mine)
    {
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1;
        AppIcons.GetPreview(path, 256, scale, image =>
        {
            if (mine != token || !showing) return;
            body.Child = new Image { Source = image, Stretch = Stretch.Uniform, MaxHeight = 256, MaxWidth = 256 };
        });
    }

    // The body in the window's client pixels (where a preview handler's window goes).
    PixelBox BodyPixels()
    {
        if (Content?.XamlRoot is not { } root || body.ActualWidth <= 0) return default;
        var at = body.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, body.ActualWidth, body.ActualHeight));
        return WindowPicture.ToPixels(at.X, at.Y, at.Width, at.Height, root.RasterizationScale, inset: 4);
    }

    // --- looks ------------------------------------------------------------------------------------
    TextBlock Glyph(int code, string color) => new()
    {
        Text = char.ConvertFromUtf32(code), FontFamily = iconFont, FontSize = 72, Foreground = Brush(color),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    Border Pill(string text, string color) => new()
    {
        CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center,
        Background = Brush(color, 0x2A),
        Child = new TextBlock { Text = text, FontFamily = textFont, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) },
    };

    static SolidColorBrush Brush(string hex, byte alpha = 255)
    {
        var c = Bar.Hex(hex);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1 << 20 ? $"{bytes / 1024.0:F0} KB" : bytes < 1 << 30 ? $"{bytes / 1048576.0:F1} MB" : $"{bytes / 1073741824.0:F1} GB";
}
