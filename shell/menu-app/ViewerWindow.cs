using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace SystemMenu;

// omawin's viewer (roadmap 8.25): a picture, or a text file (Markdown, JSON laid out, a log from
// its end), in the theme, from the keyboard. Windows opens it for the types you gave omawin
// (`omawin open-with`): omawin-view.exe hands the file over (`--view`), so it opens as fast as the
// launcher does. One viewer: a second file replaces the first. A window like any other, so
// GlazeWM tiles it. What ViewerFile.cs decides is tested; this draws it.
//   ← →  the previous / next picture in the folder     + - 0  zoom in, out, fit
//   e    open in your editor (omawin launch editor)     o      Windows' Open with
//   f    show in its folder                             c      copy its path
//   esc  close
public sealed class ViewerWindow : Window
{
    static ViewerWindow? open;

    public static void Show(string path)
    {
        try
        {
            open ??= new ViewerWindow();
            open.Load(path);
            open.Activate();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    readonly Grid root = new();
    readonly TextBlock name = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock facts = new() { FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock keys = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly ScrollViewer scroller = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsTabStop = true, MinZoomFactor = 0.1f, MaxZoomFactor = 16f,
    };
    readonly FontFamily textFont = new("Segoe UI Variable Text, Segoe UI");
    readonly FontFamily monoFont = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas");
    Theme theme = new();
    string path = "";
    List<string> siblings = [];
    int loadToken;

    ViewerWindow()
    {
        Title = "omawin viewer";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 800));
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Padding = new Thickness(18, 12, 18, 12), ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(name);
        titles.Children.Add(facts);
        header.Children.Add(titles);
        Grid.SetColumn(keys, 1);
        header.Children.Add(keys);
        root.Children.Add(header);
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        Content = root;
        foreach (var t in new[] { name, facts, keys }) t.FontFamily = textFont;
        // Before the scroller sees them: it would take ← and → for itself.
        root.PreviewKeyDown += OnKey;
        scroller.SizeChanged += (_, _) => Fit();
        Closed += (_, _) => open = null;
    }

    void Paint()
    {
        theme = MenuModel.LoadTheme(MenuModel.CurrentThemeName());
        root.RequestedTheme = theme.Mode == "light" ? ElementTheme.Light : ElementTheme.Dark;
        root.Background = new SolidColorBrush(Bar.Hex(theme.Background));
        name.Foreground = new SolidColorBrush(Bar.Hex(theme.Foreground));
        facts.Foreground = new SolidColorBrush(Bar.Hex(theme.Muted));
        keys.Foreground = new SolidColorBrush(Bar.Hex(theme.Comment));
    }

    void Load(string file)
    {
        Paint();
        path = file;
        int token = ++loadToken;
        var kind = ViewerFile.KindOf(file);
        Title = Path.GetFileName(file) + " · omawin";
        name.Text = Path.GetFileName(file);
        facts.Text = Path.GetDirectoryName(file) ?? "";
        keys.Text = kind == ViewerFile.Kind.Picture ? "← → pictures   + − 0 zoom   o open with   f folder   c copy path   esc close"
            : "e editor   o open with   f folder   c copy path   esc close";
        scroller.ZoomMode = kind == ViewerFile.Kind.Picture ? ZoomMode.Enabled : ZoomMode.Disabled;
        scroller.ChangeView(0, 0, 1f, disableAnimation: true);
        long length = 0;
        try { length = new FileInfo(file).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }

        if (kind == ViewerFile.Kind.Picture)
        {
            ImageSource source = file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(new Uri(file))
                : new BitmapImage(new Uri(file));
            var image = new Image { Source = source, Stretch = Stretch.Uniform };
            if (source is BitmapImage bitmap)
                bitmap.ImageOpened += (_, _) =>
                {
                    if (token == loadToken) facts.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} · {ViewerFile.Size(length)} · {Path.GetDirectoryName(file)}";
                };
            scroller.Content = image;
            Fit();
            // The folder's pictures, for ← and →, listed off the UI thread (a big folder takes a moment).
            string folder = Path.GetDirectoryName(file) ?? "";
            Task.Run(() => ViewerFile.Siblings(Directory.EnumerateFiles(folder), file)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                if (token == loadToken && t.IsCompletedSuccessfully) siblings = t.Result;
            }));
        }
        else if (kind == ViewerFile.Kind.Text)
        {
            facts.Text = $"{ViewerFile.Size(length)} · {Path.GetDirectoryName(file)}";
            var text = new TextBlock
            {
                FontFamily = monoFont, FontSize = 13, IsTextSelectionEnabled = true, Padding = new Thickness(18, 4, 18, 18),
                Foreground = new SolidColorBrush(Bar.Hex(theme.Foreground)), TextWrapping = file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
            scroller.Content = text;
            bool log = file.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
            Task.Run(() => ViewerFile.Shown(file, Read(file, length, fromEnd: log), length)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                if (token != loadToken) return;
                if (!t.IsCompletedSuccessfully) { text.Text = "Can't read this file: " + t.Exception?.InnerException?.Message; return; }
                text.Text = t.Result.Text;
                if (t.Result.Note != null) facts.Text = $"{t.Result.Note} · {Path.GetDirectoryName(file)}";
                // A log is read from its end: the latest lines are what you opened it for.
                if (log) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => scroller.ChangeView(0, scroller.ScrollableHeight, null, disableAnimation: true));
            }));
        }
        else
        {
            facts.Text = $"{ViewerFile.Size(length)} · {Path.GetDirectoryName(file)}";
            scroller.Content = new TextBlock
            {
                Text = "omawin's viewer shows pictures and text. o opens it with another app.", FontFamily = textFont, FontSize = 14,
                Foreground = new SolidColorBrush(Bar.Hex(theme.Muted)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        DispatcherQueue.TryEnqueue(() => scroller.Focus(FocusState.Programmatic));
    }

    // At most ViewerFile.MaxText of it: the start, or a log's end.
    static string Read(string file, long length, bool fromEnd)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (length > ViewerFile.MaxText && fromEnd) stream.Seek(-ViewerFile.MaxText, SeekOrigin.End);
        var buffer = new byte[Math.Min(length, ViewerFile.MaxText)];
        int got = 0;
        while (got < buffer.Length)
        {
            int k = stream.Read(buffer, got, buffer.Length - got);
            if (k == 0) break;
            got += k;
        }
        using var reader = new StreamReader(new MemoryStream(buffer, 0, got), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // A picture fits the window at zoom 1; zooming goes from there.
    void Fit()
    {
        if (scroller.Content is not Image image) return;
        image.Width = Math.Max(1, scroller.ViewportWidth);
        image.Height = Math.Max(1, scroller.ViewportHeight);
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+C copies selected text, Ctrl+wheel zooms: those are the controls' own.
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        bool picture = ViewerFile.KindOf(path) == ViewerFile.Kind.Picture;
        switch (e.Key)
        {
            case VirtualKey.Escape: Close(); break;
            case VirtualKey.Left when picture && siblings.Count > 1: Load(ViewerFile.Step(siblings, path, -1)); break;
            case VirtualKey.Right when picture && siblings.Count > 1: Load(ViewerFile.Step(siblings, path, 1)); break;
            case (VirtualKey)187 or VirtualKey.Add when picture: Zoom(1.25f); break;      // + (= key and keypad)
            case (VirtualKey)189 or VirtualKey.Subtract when picture: Zoom(0.8f); break;  // −
            case VirtualKey.Number0 or VirtualKey.NumberPad0 when picture: scroller.ChangeView(0, 0, 1f); break;
            case VirtualKey.E or VirtualKey.Enter when !picture: MenuModel.Omawin("launch", "editor", path); break;
            case VirtualKey.O: OpenWith(); break;
            case VirtualKey.F: Start("explorer.exe", $"/select,\"{path}\""); break;
            case VirtualKey.C: ClipboardHistory.SetText(path); Snip.Say("Path copied"); break;
            default: return;
        }
        e.Handled = true;
    }

    void Zoom(float by)
    {
        float to = Math.Clamp(scroller.ZoomFactor * by, scroller.MinZoomFactor, scroller.MaxZoomFactor);
        scroller.ChangeView(null, null, to);
    }

    // Windows' own "Open with" chooser: the type stays omawin's unless you tick "Always".
    void OpenWith() => Start("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}");

    static void Start(string file, string arguments)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }
}
