using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace SystemMenu;

// The menu window: borderless, rounded, translucent (acrylic tinted with the theme's
// background), centered on the screen with the mouse, always on top, as tall as its rows. It
// hides instead of closing: when you pick something, press Esc at the top, or click elsewhere.
public sealed partial class MainWindow : Window
{
    // Width; each row's height (with the gap under it); and the search line, hairline and footer
    // around the body (MainWindow.Tiles.cs, HomeBodyDip). How many rows cascade in: Motion.CascadeRows.
    const double WidthDip = 560, RowDip = 44, ChromeDip = 56 + 1 + 38;
    // The file finder is wider (a preview beside the list) and a fixed height.
    const double FinderWidthDip = 980, FinderHeightDip = 620, FileRowDip = 54;

    readonly IntPtr hwnd;
    readonly AppWindow appWindow;
    Theme theme = new();
    string themeName = "";
    Dictionary<string, MenuEntry> entries = [];
    HashSet<string> installed = [];
    string current = "";           // the submenu on screen ("" = top)
    string start = "";             // where this showing started (Esc there hides)
    MenuEntry? confirming;         // asking "Yes, restart?" for this row
    bool pickingTheme;             // showing the theme list
    string? backgroundsOf;         // showing this theme's backgrounds (the background picker, MainWindow.Tiles.cs)
    (string Theme, string Source)? pickedBackground; // the one just picked here, checked before set-theme.ps1 has written it down
    bool launching;                // the launcher (Win+Space): items + apps, not the menu
    bool menuFromLauncher;         // the menu was opened from the launcher (Esc goes back to it)
    List<LaunchItem> launchItems = [];
    readonly List<(Border? Row, Action Run)> rows = []; // Row: null for the launcher's rows not drawn yet (MainWindow.Virtual.cs)
    readonly List<RowView> pool = [];  // every row ever drawn, reused: a search changes their text
    int used;                          // how many of the pool this drawing uses
    int selected;
    DateTime shownAt;
    int left, top, maxHeight;      // the window's top left corner, and how tall it may get, while it's showing
    double bodyDip = 400;          // how tall the rows or tiles may be, while it's showing
    double scrollY;                // how far the rows or tiles are scrolled
    RowView? liftedRow;            // the selected row, drawn lifted
    DateTime keyMovedAt;           // the last arrow key (rows moving under a still mouse don't select)
    string lastQuery = "";
    int areaX, areaWidth;          // the screen it's on (to center it as it changes width)
    bool finding;                  // the file finder (files and folders, with a preview)
    bool findFromLauncher;         // opened from the launcher's Files item (Esc goes back to it)
    string? findScope;             // the folder the finder searches (null: all of them; FoldersOnly: folders, in all of them)
    const string FoldersOnly = "*folders*"; // not a path
    const string ImagesOnly = "*images*";   // not a path either: pictures, in all the folders

    // What the Images chip finds (fff's *.ext constraints, any of them) and shows as pictures.
    static readonly string[] ImageExtensions = ["png", "jpg", "jpeg", "gif", "webp", "bmp", "ico", "tif", "tiff", "heic", "avif", "svg"];
    static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
    List<FileSearch.Hit> findHits = [];
    int previewToken;              // drops a slow preview that's no longer the selected one
    IntPtr previous;               // the window that had focus before it opened (Dismiss returns there)
    FontFamily iconFont = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Segoe Fluent Icons");
    FontFamily textFont = new("Segoe UI Variable Text, Segoe UI");
    FontFamily monoFont = new("JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas");

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    // The monitor's scale (150% is 1.5) where this window is: icons are fetched for the pixels
    // they're drawn at (AppIcons).
    double IconScaleNow => Content?.XamlRoot?.RasterizationScale ?? 1;

    public MainWindow()
    {
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
        appWindow.Title = "System Menu";
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        SystemBackdrop = new DesktopAcrylicBackdrop();

        foreach (var t in new[] { QueryText, Placeholder, FooterLeft }) t.FontFamily = textFont;
        SearchIcon.FontFamily = iconFont;
        SearchIcon.Text = char.ConvertFromUtf32(0xF0349); // magnify

        SetUpMotion();
        Input.TextChanged += (_, _) =>
        {
            // Letters typed while it fades out are dropped (Show clears the search anyway).
            if (leaving != null) return;
            // omawin profile menu -Typing: a key in the search to its list on screen.
            SystemMenu.Program.Trace("typed");
            selected = 0;
            Render();
            SystemMenu.Program.Trace("rendered");
            TraceNextFrame("list-painted");
        };
        Input.PreviewKeyDown += OnKey;
        Activated += (_, e) =>
        {
            // Clicking elsewhere hides it (but not the focus shuffle right as it appears).
            if (e.WindowActivationState == WindowActivationState.Deactivated && DateTime.Now - shownAt > TimeSpan.FromMilliseconds(400))
                HideMenu();
        };
        appWindow.Closing += (_, e) => { e.Cancel = true; Dismiss(); };
    }

    // --- showing and hiding -----------------------------------------------------------------
    public void Toggle()
    {
        bool menu = !launching && !finding;
        if (Showing && GetForegroundWindow() == hwnd && menu) Dismiss();
        else if (Showing && menu) { shownAt = DateTime.Now; SetForegroundWindow(hwnd); Input.Focus(FocusState.Programmatic); }
        else ShowMenu(""); // hidden, or showing the launcher / finder: the menu
    }

    // `with`: what follows the row id (--open style.backgrounds gruvbox: that theme's backgrounds).
    public void ShowMenu(string at, string? with = null)
    {
        try { Show(at, with: with); } catch (Exception ex) { App.Log(ex); }
    }

    // Win+Space: the launcher, or hide it if it's in front.
    public void ToggleLauncher()
    {
        try
        {
            if (Showing && GetForegroundWindow() == hwnd && launching) Dismiss();
            else Show("", asLauncher: true);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // Shows the submenu a row is in, with that row picked out (--select install.development,
    // or --select theme:gruvbox in the theme list). For scripted walkthroughs.
    public void SelectItem(string id)
    {
        try
        {
            if (Showing && ApplyTheme(MenuModel.CurrentThemeName())) Render();
            if (id.StartsWith("theme:"))
            {
                if (!pickingTheme || !Showing) Show("style.theme");
                selected = Math.Max(0, MenuModel.Themes().FindIndex(t => t.File == id[6..]));
                Highlight();
                return;
            }
            if (!Showing) Show("");
            var parent = entries.TryGetValue(id, out var e) ? e.Parent : "";
            if (pickingTheme || backgroundsOf != null || confirming != null || current != parent || Input.Text.Length > 0) Show(parent);
            selected = Math.Max(0, MenuModel.Children(entries, current).FindIndex(c => c.Id == id));
            Highlight();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // Types into the search (--type <text>); for scripted walkthroughs.
    public void TypeText(string text)
    {
        if (!Showing) Show("");
        Input.Text = text;
        Input.SelectionStart = text.Length;
    }

    // --key down / up / left / right / tab / shift-tab / enter / esc / pick (Ctrl+Space on the
    // Packages page): as if pressed; for scripted walkthroughs.
    public void PressKey(string key)
    {
        if (!Showing) return;
        switch (key)
        {
            case "down": if (tileMode) NavTile(0, 1); else Move(1); break;
            case "up": if (tileMode) NavTile(0, -1); else Move(-1); break;
            case "left": if (tileMode) NavTile(-1, 0); else Back(hideAtTop: false); break;
            case "right": if (tileMode) NavTile(1, 0); else RunSelected(); break;
            case "enter": RunSelected(); break;
            case "esc": if (Input.Text.Length > 0) Input.Text = ""; else Back(hideAtTop: true); break;
            case "tab": if (finding) CycleScope(1); else if (launching && page == "packages") CyclePackageSource(1); else if (backgroundsOf != null) CycleBackgroundTheme(1); else Move(1); break;
            case "shift-tab": if (finding) CycleScope(-1); else if (launching && page == "packages") CyclePackageSource(-1); else if (backgroundsOf != null) CycleBackgroundTheme(-1); else Move(-1); break;
            case "pick": if (launching && page == "packages") TogglePackagePick(); break; // Ctrl+Space
        }
    }

    // Shows the menu at a submenu; when it's already showing, just goes there.
    void Show(string at, bool asLauncher = false, bool asFinder = false, string? with = null)
    {
        // A key during the exit animation ends it and shows afresh: nothing waits on an animation.
        if (leaving != null) HideNow();
        bool showing = appWindow.IsVisible;
        if (!showing)
        {
            installed = MenuModel.Installed();
            entries = MenuModel.Load(installed);
            RefreshInstalledIfOld();
            pickedBackground = null; // by now set-theme.ps1 has written down the one picked last time
        }
        launching = asLauncher;
        page = null;
        finding = asFinder;
        findFromLauncher = false;
        menuFromLauncher = false;
        if (asLauncher) launchItems = Launcher.Load();
        ApplyTheme(MenuModel.CurrentThemeName());
        current = entries.TryGetValue(at, out var e) && e.IsSubmenu ? at : "";
        confirming = null;
        pickingTheme = false;
        backgroundsOf = null;
        Input.Text = "";
        selected = 0;
        // --open style.theme: straight to the theme list; --open style.backgrounds [theme]: to
        // that theme's backgrounds (yours when it's left out or isn't one).
        if (entries.TryGetValue(at, out var picker) && picker.Kind == "themes")
        {
            current = picker.Parent;
            pickingTheme = true;
            selected = Math.Max(0, MenuModel.Themes().FindIndex(t => t.File == themeName));
        }
        else if (picker is { Kind: "backgrounds" })
        {
            current = picker.Parent;
            OpenBackgrounds(ThemeFileNamed(with) ?? themeName);
        }
        if (!showing)
        {
            start = current;
            bodyDip = HomeBodyDip(launcher: asLauncher);
            // Centered across the screen the mouse is on, a little above the middle; it grows
            // downward with its rows.
            GetCursorPos(out var p);
            var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(p.X, p.Y), DisplayAreaFallback.Nearest).WorkArea;
            areaX = area.X;
            areaWidth = area.Width;
            top = area.Y + (int)(area.Height * 0.15);
            maxHeight = area.Y + area.Height - top - (int)(area.Height * 0.08);
        }
        Render();
        if (showing) { shownAt = DateTime.Now; SetForegroundWindow(hwnd); Input.Focus(FocusState.Programmatic); return; }

        shownAt = DateTime.Now;
        previous = GetForegroundWindow();
        appWindow.Show();
        SystemMenu.Program.Trace("shown");
        TraceNextFrame("painted");
        SetForegroundWindow(hwnd);
        Input.Focus(FocusState.Programmatic);
        Animate();
        Cascade();
    }

    // On screen and staying: not hidden, and not on its way out.
    bool Showing => appWindow.IsVisible && leaving == null;

    Motion.Leaving? leaving; // the exit animation, while it runs

    // Hides it: a short fade and sink (Motion.Exit) unless `instant` or animations are off.
    // Whatever captures the screen next (a screenshot, the colour picker, text from the screen)
    // passes instant, so the fading menu isn't in the picture.
    void HideMenu(bool instant = false)
    {
        if (!appWindow.IsVisible) return;
        if (leaving != null) { if (instant) HideNow(); return; }
        if (instant || !Motion.Enabled) { HideNow(); return; }
        Root.IsHitTestVisible = false; // clicks during the fade go nowhere
        var exit = Motion.Leave(ElementCompositionPreview.GetElementVisual(Root), Motion.DropPx, HideNow);
        if (!exit.Over) leaving = exit;
    }

    // Hidden now, the exit animation (if any) cut short and undone for the next showing.
    void HideNow()
    {
        var exit = leaving;
        leaving = null;
        exit?.Cancel();
        Root.IsHitTestVisible = true;
        appWindow.Hide();
    }

    // A screen capture is about to start (SystemMenu --snip): a menu still fading out goes now.
    public void FinishHiding()
    {
        if (leaving != null) HideNow();
    }

    // For `omawin profile menu` (SystemMenu --hide): hidden now, no animation, no focus shuffle.
    public void HideForProfile() => HideMenu(instant: true);

    // Marks the next frame the compositor draws: what `omawin profile menu` calls "painted".
    // CompositionTarget.Rendering is raised on the UI thread once per frame, just before it's
    // composed, so this is the frame that has the change in it (to within that frame's own
    // drawing, well under a refresh).
    void TraceNextFrame(string label)
    {
        if (!SystemMenu.Program.Tracing) return;
        EventHandler<object>? once = null;
        once = (_, _) => { CompositionTarget.Rendering -= once; SystemMenu.Program.Trace(label); };
        CompositionTarget.Rendering += once;
    }

    // Closed without opening anything (Esc, the shortcut again): focus goes back to the window
    // you were in, or to the desktop. Left alone, Windows gives it to the taskbar, and a
    // focused taskbar doesn't auto-hide: it would pop up over the dock.
    void Dismiss()
    {
        var target = previous != IntPtr.Zero && IsWindowVisible(previous) && ClassOf(previous) != "Shell_TrayWnd"
            ? previous : FindWindow("Progman", null);
        // Focus first, then the fade: your next key goes to that window at once.
        if (target != IntPtr.Zero) SetForegroundWindow(target);
        HideMenu();
    }

    static string ClassOf(IntPtr window)
    {
        var name = new System.Text.StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    // One size while it's showing (its home screen's, or what fits on the screen), so it never
    // resizes under you: a resized window shows a black band for a frame before it's painted.
    // The file finder (and the clipboard page, with its preview) is wider.
    void FitToRows()
    {
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int w, h;
        if (Wide)
        {
            w = (int)(FinderWidthDip * scale);
            h = Math.Min(maxHeight, (int)(FinderHeightDip * scale));
        }
        else
        {
            w = (int)(WidthDip * scale);
            h = Math.Min(maxHeight, (int)((ChromeDip + bodyDip) * scale));
        }
        left = areaX + (areaWidth - w) / 2;
        if (appWindow.Size.Width != w || appWindow.Size.Height != h || appWindow.Position.X != left || appWindow.Position.Y != top)
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, w, h));
    }

    // A small fade and rise as it appears.
    void Animate()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        Motion.Arrive(ElementCompositionPreview.GetElementVisual(Root));
    }

    // --- motion -------------------------------------------------------------------------------
    // All of it runs on the compositor (off the UI thread), so it stays smooth while the list
    // is being filled: scrolling, the selected row or tile lifting, the light drifting along the
    // hairline, rows arriving, levels sliding. The numbers are Motion's (Motion.cs), and with
    // animations off every one of these is a jump to its end.
    void SetUpMotion()
    {
        // Scrolling: setting ListLayer's Translation glides it there, and a new target set while
        // it's moving (a held key, the wheel) carries on from where it is.
        ListLayer.TranslationTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) };
        ElementCompositionPreview.SetIsTranslationEnabled(Pane, true);
        Viewport.SizeChanged += (_, e) =>
        {
            ListLayer.Width = Math.Max(0, e.NewSize.Width - 16);
            Viewport.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            // Opening on a selection further down (the theme list on the current theme): the
            // first scroll ran before the window had its size, so redo it now that it has.
            if (e.NewSize.Height != e.PreviousSize.Height) { ListLayer.UpdateLayout(); ScrollToSelected(animate: false); }
        };
        Viewport.PointerWheelChanged += (_, e) =>
        {
            ScrollTo(scrollY - e.GetCurrentPoint(Viewport).Properties.MouseWheelDelta, animate: true);
            e.Handled = true;
        };

        // A soft light drifting along the hairline under the search, every few seconds.
        ElementCompositionPreview.SetIsTranslationEnabled(Sheen, true);
        DividerLine.SizeChanged += (_, e) =>
        {
            DividerLine.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Drift();
        };

        // Turned on or off (omawin toggle animations, or Windows' own switch) while running.
        Motion.Changed += () =>
        {
            ListLayer.TranslationTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) };
            foreach (var v in pool) v.Row.BackgroundTransition = new BrushTransition { Duration = Motion.Or0(Motion.Quick) };
            Drift();
        };
    }

    // The hairline's light: decoration, so it doesn't move at all while animations are off.
    void Drift()
    {
        var sheen = ElementCompositionPreview.GetElementVisual(Sheen);
        sheen.StopAnimation("Translation.X");
        float width = (float)DividerLine.ActualWidth;
        if (!Motion.Enabled || width <= 0) { sheen.Properties.InsertVector3("Translation", new Vector3(-180, 0, 0)); return; }
        var compositor = sheen.Compositor;
        var drift = compositor.CreateScalarKeyFrameAnimation();
        drift.InsertKeyFrame(0f, -180f);
        // Across in the first half of the pass, then a rest.
        drift.InsertKeyFrame(0.55f, width, Motion.EaseInOut(compositor));
        drift.InsertKeyFrame(1f, width);
        drift.Duration = Motion.Drift;
        drift.IterationBehavior = AnimationIterationBehavior.Forever;
        sheen.StartAnimation("Translation.X", drift);
    }

    // The first screenful of rows or tiles fades and rises in, Motion.Stagger apart.
    void Cascade()
    {
        int first = tileMode ? 0 : (int)Math.Round(scrollY / Pitch);
        int count = tileMode ? tiles.Count : used;
        for (int i = 0; i < count; i++)
        {
            // A tile's inside moves, not the tile: the tile's own Translation is its lift.
            var visual = ElementCompositionPreview.GetElementVisual(tileMode ? tiles[i].Inner : pool[i].Slot);
            // A row's place in the list (pool[i].Index: the launcher's search binds only the rows
            // in view, so the pool's first row may stand for one further down).
            int step = (tileMode ? i : pool[i].Index) - first;
            if (Motion.CascadeDelay(step, tileMode ? Motion.CascadeTiles : Motion.CascadeRows) is { } delay) Motion.Arrive(visual, delay);
            else
            {
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Translation");
                visual.Opacity = 1;
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
            }
        }
    }

    // Into a submenu or page the new level slides in from the right; back out, from the left.
    void Slide(int direction) => SlideIn(new Vector3(Motion.SlidePx * direction, 0, 0));

    // Typing on a home screen turns its tiles into a list (and clearing it, back): a quick fade.
    void Swap() => SlideIn(new Vector3(0, Motion.DropPx, 0));

    void SlideIn(Vector3 from)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Pane);
        Motion.MoveIn(visual, from);
        Motion.FadeIn(visual, duration: Motion.Enter * 0.8);
    }

    // --- scrolling ------------------------------------------------------------------------------
    double ViewHeight => Math.Max(0, Viewport.ActualHeight - 16);

    void ScrollTo(double y, bool animate)
    {
        y = Math.Clamp(y, 0, Math.Max(0, ListLayer.ActualHeight - ViewHeight));
        animate = EnsureBound(scrollY, y, animate); // the launcher's long list: its rows on the way, drawn first
        scrollY = y;
        var at = new Vector3(0, (float)-y, 0);
        if (animate) { ListLayer.Translation = at; return; }
        var glide = ListLayer.TranslationTransition;
        ListLayer.TranslationTransition = null;
        ListLayer.Translation = at;
        ListLayer.TranslationTransition = glide;
    }

    // Scrolls just enough to show this part of the list, with some room around it.
    void ScrollIntoView(double y, double height, double room, bool animate)
    {
        double to = scrollY;
        if (y - room < to) to = y - room;
        else if (y + height + room > to + ViewHeight) to = y + height + room - ViewHeight;
        ScrollTo(to, animate);
    }

    void ScrollToSelected(bool animate)
    {
        if (tileMode && selected < tiles.Count)
        {
            var r = RectOf(tiles[selected].Root);
            ScrollIntoView(r.Y, r.Height, 20, animate);
        }
        else if (!tileMode && selected < rows.Count)
        {
            // The row's own place, with its section title if it has one (a page's titles make
            // rows sit lower than their pitch); the first row shows the top of the list.
            // The launcher's long list: a row's place is its pitch (most rows aren't drawn to measure).
            if (selected == 0) ScrollTo(0, animate);
            else if (drawRow != null) ScrollIntoView(selected * Pitch, Pitch - 2, Pitch, animate);
            else ScrollIntoView(pool[selected].Slot.ActualOffset.Y, pool[selected].Slot.ActualHeight - 2, Pitch, animate);
        }
        else ScrollTo(0, animate: false);
    }

    bool RecentlyMovedByKey => DateTime.Now - keyMovedAt < TimeSpan.FromMilliseconds(350);

    // installed.txt is rebuilt by `winget export` (~20 s), in the background, at most every 10 min.
    static DateTime lastRefresh;
    void RefreshInstalledIfOld()
    {
        var age = File.Exists(MenuModel.InstalledFile) ? DateTime.Now - File.GetLastWriteTime(MenuModel.InstalledFile) : TimeSpan.MaxValue;
        if (age < TimeSpan.FromMinutes(10) || DateTime.Now - lastRefresh < TimeSpan.FromMinutes(2)) return;
        lastRefresh = DateTime.Now;
        RunPowerShell("-RefreshInstalled");
    }

    // --- theme ------------------------------------------------------------------------------
    // Returns whether anything changed.
    bool ApplyTheme(string name)
    {
        if (name == themeName) return false;
        themeName = name;
        theme = MenuModel.LoadTheme(name);
        AppIcons.Light = theme.Mode == "light"; // Store apps' light unplated logos
        Root.RequestedTheme = theme.Mode == "light" ? ElementTheme.Light : ElementTheme.Dark;
        // Tinted glass: the acrylic blur of what's behind, in the theme's background.
        Root.Background = Brush(theme.Background, theme.Mode == "light" ? (byte)0xCC : (byte)0xB4);
        FooterBar.Background = Brush(theme.Surface, 0x99);
        SearchIcon.Foreground = Brush(theme.Accent);
        QueryText.Foreground = Brush(theme.Foreground);
        Placeholder.Foreground = Brush(theme.Comment);
        Caret.Background = Brush(theme.Accent);
        FooterLeft.Foreground = Brush(theme.Comment);

        // The hairline under the search: the theme's gradient, fading out at both ends.
        var stops = theme.Gradient.Length > 0 ? theme.Gradient : [theme.Accent];
        var line = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        line.GradientStops.Add(new GradientStop { Color = Hex(stops[0], 0), Offset = 0 });
        for (int i = 0; i < stops.Length; i++)
            line.GradientStops.Add(new GradientStop { Color = Hex(stops[i]), Offset = 0.1 + 0.8 * i / Math.Max(1, stops.Length - 1) });
        line.GradientStops.Add(new GradientStop { Color = Hex(stops[^1], 0), Offset = 1 });
        Divider.Background = line;

        // The light that drifts along the hairline.
        var sheen = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        sheen.GradientStops.Add(new GradientStop { Color = Hex(theme.Foreground, 0), Offset = 0 });
        sheen.GradientStops.Add(new GradientStop { Color = Hex(theme.Foreground, 0xE0), Offset = 0.5 });
        sheen.GradientStops.Add(new GradientStop { Color = Hex(theme.Foreground, 0), Offset = 1 });
        Sheen.Background = sheen;

        // The window's own 1px border, in the theme's border color.
        var b = Hex(theme.Border);
        int colorref = b.R | (b.G << 8) | (b.B << 16);
        DwmSetWindowAttribute(hwnd, 34, ref colorref, sizeof(int));
        return true;
    }

    static Color Hex(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
    static SolidColorBrush Brush(string hex, byte alpha = 255) => new(Hex(hex, alpha));
    static SolidColorBrush Brush(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    Color GradientAt(double t)
    {
        var stops = theme.Gradient.Length > 1 ? theme.Gradient : [theme.Accent, theme.Accent];
        t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)t, stops.Length - 2);
        Color a = Hex(stops[i]), b = Hex(stops[i + 1]);
        double f = t - i;
        return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
    }

    // --- drawing the rows -------------------------------------------------------------------
    // What a row shows on its right.
    enum Tag { None, Chevron, Text, Installed, Remove, Current }

    void Render()
    {
        rows.Clear();
        EndVirtual();
        used = 0;
        liftedRow = null;
        TileLayout? home = null; // set: this view is tiles
        var built = builtHome;   // good for this drawing only
        builtHome = null;
        string query = Input.Text.Trim();
        QueryText.Text = Input.Text;
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterLeft.Text = theme.Name;
        pageRows.Clear();
        // Folder chips are the file finder's (and source chips the Packages page's); the preview
        // theirs and the clipboard page's.
        Scopes.Visibility = finding || (launching && page == "packages") ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = Wide ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = new GridLength(Wide ? 400 : 0);

        if (finding)
        {
            RenderFinder(query);
        }
        else if (confirming is { } c)
        {
            SetCrumbs(c.Parent, c.Label);
            Placeholder.Text = "Are you sure?";
            AddRow(c.Icon, $"Yes, {c.Label.ToLowerInvariant()}", Hex(theme.Red), () => { var e = confirming; confirming = null; Run(e!); });
            AddRow(char.ConvertFromUtf32(0xF0156), "No, go back", Hex(theme.Comment), () => { confirming = null; Render(); });
            SetKeys(("↵", "choose"), ("esc", "back"));
        }
        else if (pickingTheme)
        {
            SetCrumbs("style", "Theme");
            Placeholder.Text = "Search themes…";
            home = ThemePicker(query);
            FooterLeft.Text = $"{home.Cells.Count} themes";
            SetKeys(("←↑↓→", "move"), ("↵", "apply"), ("esc", "back"));
        }
        else if (backgroundsOf is { } of)
        {
            SetCrumbs("style", "Backgrounds");
            var shown = of == themeName ? theme : MenuModel.LoadTheme(of);
            Placeholder.Text = $"Search {shown.Name}'s backgrounds…";
            var grid = BackgroundPicker(of, shown, query);
            if (grid.Cells.Count > 0) home = grid;
            else AddRow(char.ConvertFromUtf32(0xF0156), shown.Backgrounds.Length == 0 ? $"{shown.Name} has no backgrounds" : "No matches", Hex(theme.Comment), () => { }, dim: true);
            FooterLeft.Text = $"{shown.Name} · {grid.Cells.Count} backgrounds";
            SetKeys(("←↑↓→", "move"), ("↵", "set"), ("tab", "another theme"), ("esc", "back"));
        }
        else if (launching && page != null)
        {
            home = RenderPage(query);
        }
        else if (launching && query.Length == 0)
        {
            SetCrumbs("", null);
            Placeholder.Text = "Search apps…";
            home = Home(launcher: true, built);
            FooterLeft.Text = $"{launchItems.Count(i => i.IsApp)} apps · {theme.Name}";
            SetKeys(("←↑↓→", "move"), ("↵", "open"), ("esc", "close"));
        }
        else if (!launching && query.Length == 0 && current.Length == 0)
        {
            SetCrumbs("", null);
            Placeholder.Text = "Search the menu…";
            home = Home(launcher: false, built);
            SetKeys(("←↑↓→", "move"), ("↵", "open"), ("esc", menuFromLauncher ? "back" : "close"));
        }
        else if (launching)
        {
            // Your own items (items.txt: Projects, Reports, Files, ...; each its own color along
            // the theme's gradient), then Windows' apps with their real icons. Files have their
            // own finder (the Files item). Every match is a row, but only the ones in view are
            // drawn, as they scroll in (MainWindow.Virtual.cs): typing never waits on the list.
            SetCrumbs("", null);
            Placeholder.Text = "Search apps…";
            // A sum ("12*4.5", "sqrt(2)"): its value first; Enter copies it (Calc.cs).
            bool sum = Calc.TryEvaluate(query, out var value);
            string result = sum ? Calc.Format(value) : "";
            if (sum) rows.Add((null, () => { ClipboardHistory.SetText(result); Dismiss(); }));
            var list = Launcher.Search(launchItems, query);
            foreach (var match in list)
            {
                var item = match.Item;
                rows.Add((null, () => Launch(item, query)));
            }
            int skip = sum ? 1 : 0;
            if (rows.Count == 0) AddRow(char.ConvertFromUtf32(0xF0156), "No matches", Hex(theme.Comment), () => { }, dim: true);
            else Virtualize((v, i) =>
            {
                if (i < skip) { DrawRow(v, char.ConvertFromUtf32(0xF00EC), "= " + result, Hex(theme.Green), Tag.Text, "↵ copy", sub: query); return; }
                var item = list[i - skip].Item;
                DrawRow(v, item.Icon, item.Name, KindColor(item), Tag.Text, item.Hint, appId: item.IconKey, highlight: list[i - skip].Positions);
            });
            FooterLeft.Text = $"{launchItems.Count(i => i.IsApp)} apps";
            SetKeys(("↑↓", "move"), ("↵", "open"), ("esc", query.Length > 0 ? "clear" : "close"));
        }
        else
        {
            var list = query.Length > 0 ? MenuModel.Search(entries, query) : MenuModel.Children(entries, current);
            SetCrumbs(current, null);
            Placeholder.Text = current.Length > 0 ? $"Search {entries[current].Label}…" : "Search the menu…";
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                bool have = e.Package != null && installed.Contains(e.Package);
                // By kind: submenus purple, pages and the pickers cyan, actions yellow,
                // ones that ask first (restart, shut down) red, what's installed gray.
                var color = Hex(have ? theme.Comment : e.IsSubmenu ? theme.Purple : e.Confirm ? theme.Red
                    : e.Kind is "themes" or "backgrounds" || e.Kind.StartsWith('@') ? theme.Cyan : theme.Yellow);
                var (tag, text) = TagFor(e, have);
                string? sub = query.Length > 0 && e.Parent.Length > 0 ? MenuModel.PathLabel(entries, e.Parent) : null;
                AddRow(e.Icon, e.Label, color, () => Pick(e), tag, text, dim: have, sub: sub);
            }
            if (list.Count == 0)
                AddRow(char.ConvertFromUtf32(0xF0156), current == "remove" ? "Nothing from Install is on this PC" : "No matches", Hex(theme.Comment), () => { }, dim: true);
            SetKeys(("↑↓", "move"), ("↵", "open"), ("esc", query.Length > 0 ? "clear" : current.Length > 0 ? "back" : "close"));
        }
        // Rows this drawing didn't need stay in the pool, hidden.
        for (int i = used; i < pool.Count; i++) pool[i].Slot.Visibility = Visibility.Collapsed;
        bool wasTiles = tileMode;
        tileMode = home != null;
        Rows.Visibility = tileMode ? Visibility.Collapsed : Visibility.Visible;
        Tiles.Visibility = tileMode ? Visibility.Visible : Visibility.Collapsed;
        if (home != null)
        {
            RenderTiles(home);
            if (selectKey != null && tiles.FindIndex(t => t.Spec.Key == selectKey) is var at and >= 0) selected = at;
            if (launching && page == null) LoadHomeDetails();
        }
        else tiles.Clear();
        selectKey = null;
        selected = Math.Clamp(selected, 0, Math.Max(0, (tileMode ? tiles.Count : rows.Count) - 1));
        FitToRows();
        Highlight(jump: true);
        if (wasTiles != tileMode && query != lastQuery) Swap();
        lastQuery = query;
    }

    void RunSelected()
    {
        if (tileMode) { if (selected < tiles.Count) tiles[selected].Spec.Run(); }
        else if (selected < rows.Count) rows[selected].Run();
    }

    // --- the file finder -------------------------------------------------------------------
    // Files and folders from fff: folder chips to narrow it (Tab), the list (name, and where
    // it is), and a preview of the selected one beside it.
    void RenderFinder(string query)
    {
        SetCrumbs("", "Files");
        Placeholder.Text = findScope switch
        {
            null => "Search files and folders…",
            FoldersOnly => "Search folders…",
            ImagesOnly => "Search pictures…",
            _ => $"Search {Path.GetFileName(findScope)}…",
        };

        Scopes.Children.Clear();
        var scopes = FileSearch.Scopes();
        if (findScope is not (null or FoldersOnly or ImagesOnly) && !scopes.Any(s => s.Root == findScope)) findScope = null;
        void Chip(string label, string? root, string glyph)
        {
            bool on = findScope == root;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 5),
                Background = on ? Brush(Hex(theme.Accent), 0x33) : Brush(Hex(theme.Selection), 0x90),
                BorderBrush = on ? Brush(Hex(theme.Accent), 0x99) : new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = glyph, FontFamily = iconFont, FontSize = 13, Foreground = Brush(on ? theme.Accent : theme.Muted), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = label, FontFamily = textFont, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(on ? theme.Accent : theme.Muted), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            chip.Tapped += (_, _) => { findScope = root; selected = 0; Render(); Input.Focus(FocusState.Programmatic); };
            Scopes.Children.Add(chip);
        }
        Chip("All", null, char.ConvertFromUtf32(0xF0C4B)); // all-inclusive
        Chip("Folders", FoldersOnly, char.ConvertFromUtf32(0xF0770)); // folder-search
        Chip("Images", ImagesOnly, char.ConvertFromUtf32(0xF02E9)); // image
        foreach (var (root, label) in scopes)
            Chip(label, root, char.ConvertFromUtf32(label switch
            {
                "Desktop" or "OneDrive Desktop" => 0xF01C4, "Documents" or "OneDrive Documents" => 0xF0219, "Downloads" => 0xF0B4F, _ => 0xF024B,
            }));

        // Folders only: the Folders chip, or a query ending in / or \. Images: the Images chip,
        // fff told to keep only those extensions (its *.png constraints, any of them).
        bool imagesOnly = findScope == ImagesOnly;
        bool foldersOnly = !imagesOnly && (findScope == FoldersOnly || query.EndsWith('/') || query.EndsWith('\\'));
        string bare = query.TrimEnd('/', '\\'); // the name part, for searching and highlighting
        string fffQuery = imagesOnly ? $"{bare} {string.Join(" ", ImageExtensions.Select(e => "*." + e))}".Trim() : bare;
        findHits = FileSearch.Search(fffQuery, findScope is FoldersOnly or ImagesOnly ? null : findScope, foldersOnly: foldersOnly);
        if (imagesOnly) findHits = [.. findHits.Where(h => !h.IsDir && IsImage(h.Name))];
        foreach (var hit in findHits) AddFileRow(hit, bare);
        if (findHits.Count == 0)
            AddRow(char.ConvertFromUtf32(scopes.Count == 0 ? 0xF0996 : 0xF0156),
                scopes.Count == 0 ? "Indexing your folders…" : "No matches", Hex(theme.Comment), () => { }, dim: true);
        FooterLeft.Text = query.Length == 0 ? "most used" : $"{findHits.Count} found";
        SetKeys(("tab", "where · folders"), ("↵", "open"), ("ctrl ↵", "show in folder"), ("esc", query.Length > 0 ? "clear" : findFromLauncher ? "back" : "close"));
    }

    // A file or folder: its icon (a thumbnail for pictures), name with the matched letters,
    // and where it is under it; on the right its git status or when it changed.
    void AddFileRow(FileSearch.Hit hit, string query)
    {
        var v = NextRow(FileRowDip - 2, 32);
        var color = Hex(hit.IsDir ? theme.Accent : theme.Muted);
        SetTile(v, char.ConvertFromUtf32(hit.IsDir ? 0xF024B : 0xF0214), color, 16, hit.IsDir ? null : "path:" + hit.Path);
        // Which letters the query matched (fff doesn't say), for highlighting.
        var hits = new List<int>();
        Fuzzy.Match(hit.Name, query, hits);
        SetLabel(v, hit.Name, hits, dim: false);
        v.Sub.Visibility = Visibility.Collapsed;
        v.Below.Text = hit.RelativeDir.Length > 0 ? $"{hit.RootLabel} › {hit.RelativeDir.Replace("\\", " › ")}" : hit.RootLabel;
        v.Below.Foreground = Brush(theme.Comment);
        v.Below.Visibility = Visibility.Visible;
        if (hit.Git != null) SetRight(v, Tag.Text, "", pill: hit.Git, pillColor: hit.Git.Contains("untracked") ? theme.Green : theme.Accent);
        else SetRight(v, Tag.Text, hit.Modified is { } m ? Ago(m) : "");
        rows.Add((v.Row, () => OpenFile(hit, query, reveal: false)));
    }

    static string Ago(DateTime when)
    {
        var age = DateTime.Now - when;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago"
            : age.TotalDays < 30 ? $"{(int)age.TotalDays}d ago"
            : when.ToString(age.TotalDays < 365 ? "MMM d" : "MMM yyyy");
    }

    static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1 << 20 ? $"{bytes / 1024.0:F0} KB" : bytes < 1 << 30 ? $"{bytes / 1048576.0:F1} MB" : $"{bytes / 1073741824.0:F1} GB";

    // The preview beside the list: a big icon or the picture itself, the name, the folders it's
    // in as a path of chips, facts (kind, size, changed, git), and for text files their first
    // lines, for folders what's in them.
    void ShowPreview()
    {
        int token = ++previewToken;
        if (selected >= findHits.Count) { Preview.Visibility = Visibility.Collapsed; return; }
        Preview.Visibility = Visibility.Visible;
        var hit = findHits[selected];
        Preview.Background = Brush(Hex(theme.Surface), 0xB0);
        Preview.BorderBrush = Brush(Hex(theme.Border), 0x80);

        PreviewArt.Visibility = Visibility.Visible;
        PreviewText.TextWrapping = TextWrapping.NoWrap;
        PreviewArt.Background = Brush(Hex(theme.Background), 0x90);
        bool picture = !hit.IsDir && IsImage(hit.Name);
        PreviewArt.Height = picture ? 300 : 150;
        PreviewArt.Child = new TextBlock
        {
            Text = char.ConvertFromUtf32(hit.IsDir ? 0xF024B : picture ? 0xF02E9 : 0xF0214), FontFamily = iconFont, FontSize = 64,
            Foreground = Brush(hit.IsDir ? theme.Accent : theme.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (picture)
        {
            // The picture itself, decoded at the preview's size (Windows' own decoders, WebP and
            // HEIC included where Windows has them), and how big it really is.
            try
            {
                ImageSource source;
                var uri = new Uri(hit.Path);
                if (hit.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(uri);
                else
                {
                    source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri)
                    {
                        DecodePixelWidth = 380, DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical,
                    };
                }
                PreviewArt.Child = new Image { Source = source, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
                // Its real size (the picture above is decoded small), next to its kind.
                string file = hit.Path;
                Task.Run(async () =>
                {
                    var props = await (await Windows.Storage.StorageFile.GetFileFromPathAsync(file)).Properties.GetImagePropertiesAsync();
                    return (props.Width, props.Height);
                }).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
                {
                    if (token != previewToken || !t.IsCompletedSuccessfully || t.Result.Width == 0) return;
                    PreviewFacts.Children.Insert(Math.Min(1, PreviewFacts.Children.Count), Pill($"{t.Result.Width} × {t.Result.Height}", theme.Accent));
                }));
            }
            catch (Exception ex) { App.Log(ex); }
        }
        else if (!hit.IsDir)
            AppIcons.GetPreview(hit.Path, 140, IconScaleNow, image =>
            {
                if (token != previewToken) return;
                PreviewArt.Child = new Image { Source = image, Stretch = Stretch.Uniform, MaxHeight = 140, Margin = new Thickness(6) };
            });

        PreviewName.Text = hit.Name;
        PreviewName.Foreground = Brush(theme.Foreground);
        PreviewPath.Children.Clear();
        var parts = new List<string> { hit.RootLabel };
        if (hit.RelativeDir.Length > 0) parts.AddRange(hit.RelativeDir.Split('\\'));
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) PreviewPath.Children.Add(new TextBlock { Text = "›", FontSize = 12, Foreground = Brush(theme.Comment) });
            PreviewPath.Children.Add(new TextBlock { Text = parts[i], FontFamily = textFont, FontSize = 12, Foreground = Brush(i == 0 ? theme.Accent : theme.Muted) });
        }

        PreviewFacts.Children.Clear();
        string kind = hit.IsDir ? "Folder" : Path.GetExtension(hit.Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "File";
        PreviewFacts.Children.Add(Pill(kind, theme.Accent));
        if (!hit.IsDir) PreviewFacts.Children.Add(Pill(Size(hit.Size), theme.Muted));
        if (hit.Modified is { } changed) PreviewFacts.Children.Add(Pill(Ago(changed), theme.Muted));
        if (hit.Git != null) PreviewFacts.Children.Add(Pill(hit.Git, theme.Green));

        PreviewText.FontFamily = monoFont;
        PreviewText.Foreground = Brush(theme.Muted);
        PreviewTextBox.Background = Brush(Hex(theme.Background), 0x90);
        PreviewText.Text = "";
        PreviewTextBox.Visibility = Visibility.Collapsed;
        string path = hit.Path;
        bool isDir = hit.IsDir, textual = !hit.IsDir && !hit.Binary && hit.Size < 4 << 20;
        if (!isDir && !textual) return;
        Task.Run(() =>
        {
            string text;
            try
            {
                text = isDir
                    ? string.Join("\n", Directory.EnumerateFileSystemEntries(path).Take(18)
                        .Select(e => Directory.Exists(e) ? Path.GetFileName(e) + "\\" : Path.GetFileName(e)))
                    : string.Join("\n", File.ReadLines(path).Take(22).Select(l => l.Replace("\t", "    ") is var s && s.Length > 90 ? s[..90] + "…" : s));
            }
            catch { text = ""; }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != previewToken || text.Length == 0) return;
                PreviewText.Text = text;
                PreviewTextBox.Visibility = Visibility.Visible;
            });
        });
    }

    (Tag, string) TagFor(MenuEntry e, bool have)
    {
        if (e.IsSubmenu || e.Kind is "themes" or "backgrounds" || e.Kind.StartsWith('@')) return (Tag.Chevron, "");
        if (have) return (Tag.Installed, "Installed");
        if (e.Kind == "omawin" && e.Argument == "toggle stay-awake") return StayAwake.On ? (Tag.Installed, "On") : (Tag.Text, "off");
        if (e.Kind == "omawin" && e.Argument == "toggle animations") return Motion.On ? (Tag.Installed, "On") : (Tag.Text, "off");
        if (e.Package != null) return (Tag.Text, e.Kind);
        return e.Kind switch
        {
            "unwinget" or "unchoco" => (Tag.Remove, "Remove"),
            "edit" => (Tag.Text, "edit"),
            "task" => (Tag.Text, "run"),
            "run" when e.Argument.StartsWith("http") => (Tag.Text, "web"),
            _ => (Tag.None, ""),
        };
    }

    // Chips before the search text for where you are: Install › Development.
    void SetCrumbs(string id, string? extra)
    {
        Crumbs.Children.Clear();
        var labels = new List<string>();
        var parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i <= parts.Length; i++)
            if (entries.TryGetValue(string.Join('.', parts[..i]), out var e)) labels.Add(e.Label);
        if (extra != null) labels.Add(extra);
        for (int i = 0; i < labels.Count; i++)
        {
            bool last = i == labels.Count - 1;
            Crumbs.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 3, 9, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = last ? Brush(Hex(theme.Accent), 0x2E) : Brush(Hex(theme.Selection), 0xC0),
                Child = new TextBlock
                {
                    Text = labels[i], FontFamily = textFont, FontSize = 13,
                    FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Brush(last ? theme.Accent : theme.Muted),
                },
            });
        }
        Crumbs.Visibility = labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // The footer's keys: ↵ open   esc back.
    void SetKeys(params (string Key, string Label)[] keys)
    {
        Keys.Children.Clear();
        foreach (var (key, label) in keys)
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            pair.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 2), MinWidth = 20, VerticalAlignment = VerticalAlignment.Center,
                Background = Brush(theme.Selection), BorderBrush = Brush(theme.Border), BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock { Text = key, FontFamily = monoFont, FontSize = 11, Foreground = Brush(theme.Muted), HorizontalAlignment = HorizontalAlignment.Center },
            });
            pair.Children.Add(new TextBlock { Text = label, FontFamily = textFont, FontSize = 12, Foreground = Brush(theme.Comment), VerticalAlignment = VerticalAlignment.Center });
            Keys.Children.Add(pair);
        }
    }

    // A row: its icon on a tile tinted with the icon's color, the label (and, when searching,
    // the submenu it's in), and a tag on the right.
    void AddRow(string icon, string label, Color color, Action run, Tag tag = Tag.None, string tagText = "",
        bool dim = false, string? sub = null, Theme? swatch = null, string? appId = null, List<int>? highlight = null)
    {
        var v = NextRow(RowDip - 2, 28);
        DrawRow(v, icon, label, color, tag, tagText, dim, sub, swatch, appId, highlight);
        rows.Add((v.Row, run));
    }

    // What AddRow shows, into a pooled view (the launcher's long list draws its rows this way as
    // they scroll into view: MainWindow.Virtual.cs).
    void DrawRow(RowView v, string icon, string label, Color color, Tag tag = Tag.None, string tagText = "",
        bool dim = false, string? sub = null, Theme? swatch = null, string? appId = null, List<int>? highlight = null)
    {
        if (swatch != null)
        {
            // A theme: its background with three of its colors on it.
            var g = swatch.Gradient.Length >= 4 ? swatch.Gradient : [swatch.Accent, swatch.Accent, swatch.Accent, swatch.Accent];
            string[] bars = [g[0], swatch.Accent, g[3]];
            for (int i = 0; i < 3; i++) ((Border)v.Bars.Children[i]).Background = Brush(bars[i]);
            v.Tile.Background = Brush(swatch.Background);
            v.Tile.BorderBrush = Brush(swatch.Border);
            v.Glyph.Visibility = Visibility.Collapsed;
            v.Icon.Visibility = Visibility.Collapsed;
            v.Bars.Visibility = Visibility.Visible;
            v.IconKey = null;
        }
        else SetTile(v, icon, color, 15, appId);
        SetLabel(v, label, highlight, dim);
        v.Sub.Text = sub ?? "";
        v.Sub.Foreground = Brush(theme.Comment);
        v.Sub.Visibility = sub != null ? Visibility.Visible : Visibility.Collapsed;
        v.Below.Visibility = Visibility.Collapsed;
        switch (tag)
        {
            case Tag.Installed: SetRight(v, tag, "", pill: tagText, pillColor: theme.Green); break;
            case Tag.Remove: SetRight(v, tag, "", pill: tagText, pillColor: theme.Red); break;
            case Tag.Current: SetRight(v, tag, "", pill: "Current", pillColor: theme.Accent); break;
            default: SetRight(v, tag, tagText); break;
        }
    }

    // One row of the list, made once and reused: a search or another submenu only changes what
    // its parts say, so nothing is built or thrown away while you type or move.
    sealed class RowView
    {
        public Border Row = null!, Tile = null!, Pill = null!;
        public TextBlock Glyph = null!, Label = null!, Sub = null!, Below = null!, RightText = null!, PillText = null!;
        public Image Icon = null!;
        public StackPanel Bars = null!;
        public StackPanel Slot = null!; // the row, with room above it for a title
        public ColumnDefinition TileColumn = null!;
        public int Index;       // the list row it shows (the launcher's long list: not its place in the pool)
        public string? IconKey; // the icon this row wants now (a late one for an old row is dropped)
        public Color Color;     // its icon's color (the lifted row's icon tile fills with it)
    }

    RowView NextRow(double height, double tile)
    {
        if (used == pool.Count) pool.Add(MakeRow());
        var v = pool[used];
        v.Index = used++;
        v.Row.Height = height;
        v.Tile.Width = v.Tile.Height = tile;
        v.TileColumn.Width = new GridLength(tile);
        v.Slot.Visibility = Visibility.Visible;
        v.Row.Background = new SolidColorBrush(Colors.Transparent);
        v.Row.BorderBrush = new SolidColorBrush(Colors.Transparent);
        return v;
    }

    RowView MakeRow()
    {
        var v = new RowView();
        v.Glyph = new TextBlock { FontFamily = iconFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        v.Icon = new Image { Width = 26, Height = 26, Visibility = Visibility.Collapsed };
        v.Bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        for (int i = 0; i < 3; i++) v.Bars.Children.Add(new Border { Width = 4, Height = 12, CornerRadius = new CornerRadius(2) });
        var inTile = new Grid();
        inTile.Children.Add(v.Glyph);
        inTile.Children.Add(v.Icon);
        inTile.Children.Add(v.Bars);
        v.Tile = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0), Child = inTile, VerticalAlignment = VerticalAlignment.Center };

        v.Label = new TextBlock { FontFamily = textFont, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        v.Sub = new TextBlock { FontFamily = textFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(v.Label);
        line.Children.Add(v.Sub);
        v.Below = new TextBlock { FontFamily = textFont, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(line);
        text.Children.Add(v.Below);

        v.RightText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        v.PillText = new TextBlock { FontFamily = textFont, FontSize = 11, FontWeight = FontWeights.SemiBold };
        v.Pill = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center, Child = v.PillText };
        var right = new Grid { Margin = new Thickness(0, 0, 2, 0) };
        right.Children.Add(v.RightText);
        right.Children.Add(v.Pill);

        var grid = new Grid { ColumnSpacing = 12 };
        v.TileColumn = new ColumnDefinition { Width = new GridLength(28) };
        grid.ColumnDefinitions.Add(v.TileColumn);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(v.Tile);
        grid.Children.Add(text);
        grid.Children.Add(right);

        v.Row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 0, 9, 0), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Colors.Transparent), BorderBrush = new SolidColorBrush(Colors.Transparent),
            BackgroundTransition = new BrushTransition { Duration = Motion.Or0(Motion.Quick) },
        };
        v.Row.PointerEntered += (_, _) => { if (!tileMode && !RecentlyMovedByKey && v.Index < rows.Count && selected != v.Index) { selected = v.Index; Highlight(); } };
        v.Row.Tapped += (_, _) => { if (v.Index < rows.Count) { selected = v.Index; Highlight(); rows[v.Index].Run(); } };
        ElementCompositionPreview.SetIsTranslationEnabled(v.Row, true);
        v.Slot = new StackPanel { Children = { v.Row } };
        ElementCompositionPreview.SetIsTranslationEnabled(v.Slot, true);
        Rows.Children.Add(v.Slot);
        return v;
    }

    // The icon: a glyph on a tile tinted with its color; an app's or file's real icon replaces it
    // as soon as it's loaded (at once when it's been seen before).
    void SetTile(RowView v, string glyph, Color color, double size, string? iconKey)
    {
        v.Bars.Visibility = Visibility.Collapsed;
        v.Color = color;
        v.Tile.BorderBrush = null;
        v.Tile.Background = Brush(color, 0x26);
        v.Glyph.Text = glyph;
        v.Glyph.FontSize = size;
        v.Glyph.Foreground = new SolidColorBrush(color);
        v.Glyph.Visibility = Visibility.Visible;
        v.Icon.Visibility = Visibility.Collapsed;
        v.IconKey = iconKey;
        if (iconKey == null) return;
        AppIcons.Get(iconKey, v.Tile.Width - 2, IconScaleNow, (image, units) =>
        {
            if (v.IconKey != iconKey) return;
            v.Tile.Background = new SolidColorBrush(Colors.Transparent);
            v.Icon.Source = image;
            v.Icon.Width = v.Icon.Height = units;   // one icon pixel on one screen pixel
            v.Icon.Visibility = Visibility.Visible;
            v.Glyph.Visibility = Visibility.Collapsed;
        });
    }

    void SetLabel(RowView v, string label, List<int>? highlight, bool dim)
    {
        v.Label.Foreground = Brush(dim ? theme.Comment : theme.Foreground);
        v.Label.Inlines.Clear();
        if (highlight is { Count: > 0 }) SetHighlighted(v.Label, label, highlight);
        else v.Label.Text = label;
    }

    void SetRight(RowView v, Tag tag, string text, string? pill = null, string? pillColor = null)
    {
        if (pill != null)
        {
            v.PillText.Text = pill;
            v.PillText.Foreground = Brush(pillColor!);
            v.Pill.Background = Brush(Hex(pillColor!), 0x2A);
            v.Pill.Visibility = Visibility.Visible;
            v.RightText.Visibility = Visibility.Collapsed;
            return;
        }
        v.Pill.Visibility = Visibility.Collapsed;
        v.RightText.Visibility = tag is Tag.Chevron or Tag.Text ? Visibility.Visible : Visibility.Collapsed;
        v.RightText.Foreground = Brush(theme.Comment);
        if (tag == Tag.Chevron) { v.RightText.Text = char.ConvertFromUtf32(0xF0142); v.RightText.FontFamily = iconFont; v.RightText.FontSize = 14; }
        else { v.RightText.Text = text; v.RightText.FontFamily = monoFont; v.RightText.FontSize = 11; }
    }

    // The text, with the letters the search matched in the accent color.
    void SetHighlighted(TextBlock block, string text, List<int> matched)
    {
        var on = new HashSet<int>(matched);
        for (int k = 0; k < text.Length;)
        {
            bool hit = on.Contains(k);
            int end = k;
            while (end < text.Length && on.Contains(end) == hit) end++;
            var piece = new Microsoft.UI.Xaml.Documents.Run { Text = text[k..end] };
            if (hit) { piece.Foreground = Brush(theme.Accent); piece.FontWeight = FontWeights.SemiBold; }
            block.Inlines.Add(piece);
            k = end;
        }
    }

    Border Pill(string text, string color) => new()
    {
        CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center,
        Background = Brush(Hex(color), 0x2A),
        Child = new TextBlock { Text = text, FontFamily = textFont, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) },
    };

    // The selected row or tile lifts (LiftRow, LiftTile), and the view scrolls to keep it in
    // sight with a row's room around it.
    // jump: new results, so no gliding from where the old ones were.
    void Highlight(bool jump = false)
    {
        // New rows or tiles: let them be measured first, or the scroll would stop at the old length.
        if (jump) ListLayer.UpdateLayout();
        if (tileMode)
        {
            for (int i = 0; i < tiles.Count; i++) LiftTile(tiles[i], i == selected);
            ScrollToSelected(animate: !jump);
            return;
        }
        if (liftedRow != null && liftedRow.Index != selected) LiftRow(liftedRow, false);
        liftedRow = null;
        // The launcher's long list scrolls first, so the selected row is drawn to be lifted.
        if (drawRow != null) ScrollToSelected(animate: !jump);
        if (ViewAt(selected) is { } row)
        {
            liftedRow = row;
            LiftRow(row, true);
        }
        if (drawRow == null) ScrollToSelected(animate: !jump);
        if (finding) ShowPreview();
        else if (launching && page == "clipboard") ShowClipPreview();
        else if (launching && page == "packages") ShowPackagePreview();
    }

    // The selected row: lighter, ringed in the accent, its icon tile filled with its color.
    void LiftRow(RowView v, bool on)
    {
        v.Row.Background = on ? Brush(Hex(theme.Foreground), 0x1A) : new SolidColorBrush(Colors.Transparent);
        v.Row.BorderBrush = on ? Brush(Hex(theme.Accent), 0x70) : new SolidColorBrush(Colors.Transparent);
        if (v.Glyph.Visibility != Visibility.Visible) return; // a real icon or a swatch
        v.Tile.Background = on ? new SolidColorBrush(v.Color) : Brush(v.Color, 0x26);
        v.Glyph.Foreground = on ? Brush(theme.Background) : new SolidColorBrush(v.Color);
    }

    // A row and the gap under it.
    double Pitch => finding ? FileRowDip : RowDip;

    // Tab / Shift+Tab in the file finder: the next / previous folder chip.
    void CycleScope(int by)
    {
        var scopes = new List<string?> { null, FoldersOnly, ImagesOnly };
        scopes.AddRange(FileSearch.Scopes().Select(s => (string?)s.Root));
        int at = Math.Max(0, scopes.IndexOf(findScope));
        findScope = scopes[(at + by + scopes.Count) % scopes.Count];
        selected = 0;
        Render();
    }

    // --- keys -------------------------------------------------------------------------------
    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        // On its way out (the exit animation): keys do nothing here, so a second Enter can't
        // start a thing twice.
        if (leaving != null) { e.Handled = true; return; }
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        // Quick settings asked "Press again to turn Wi-Fi off": only Space or Enter (the press
        // that asked, repeated) can confirm it; any other key drops it.
        if (e.Key is not (VirtualKey.Space or VirtualKey.Enter)) ForgetOff();
        switch (e.Key)
        {
            case VirtualKey.X when ctrl && launching && page is "clipboard" or "localhost":
                PageDelete(); break;
            // The Packages page: Tab moves between the sources; Ctrl+Space picks several.
            case VirtualKey.Tab when launching && page == "packages":
                CyclePackageSource(shift ? -1 : 1); break;
            case VirtualKey.Space when ctrl && launching && page == "packages":
                TogglePackagePick(); break;
            // The tiles (Quick settings' switches and the sound under them): Space mutes a speaker
            // or works a switch, + / − turn a volume or a brightness up or down. Only while the
            // search is empty: once you've typed something, they're letters of the search.
            case VirtualKey.Space when TileKeys && routing == null: TileAction(); break;
            // Left / right turn the selected speaker, app or monitor down / up, like + / −; on a
            // tile that has no level (Wi-Fi), and once something's typed, they move like up / down.
            case VirtualKey.Right when TileKeys && Adjustable:
                Adjust(shift ? 1 : 5); break;
            case VirtualKey.Left when TileKeys && Adjustable:
                Adjust(shift ? -1 : -5); break;
            case VirtualKey.Down or VirtualKey.Right when launching && page == "audio" && tileMode: Move(1); break;
            case VirtualKey.Up or VirtualKey.Left when launching && page == "audio" && tileMode: Move(-1); break;
            case (VirtualKey)187 when TileKeys:
            case VirtualKey.Add when TileKeys:
                Adjust(shift ? 1 : 5); break;
            case (VirtualKey)189 when TileKeys:
            case VirtualKey.Subtract when TileKeys:
                Adjust(shift ? -1 : -5); break;
            // The file finder: Tab moves between folder chips; Ctrl+Enter shows in its folder.
            case VirtualKey.Tab when finding:
                CycleScope(shift ? -1 : 1); break;
            case VirtualKey.Enter when finding && ctrl:
                if (selected < findHits.Count) OpenFile(findHits[selected], Input.Text.Trim(), reveal: true);
                break;
            // The background picker: Tab shows the next theme's backgrounds.
            case VirtualKey.Tab when backgroundsOf != null:
                CycleBackgroundTheme(shift ? -1 : 1); break;
            // Tiles: the arrows move in two dimensions.
            case VirtualKey.Down when tileMode: NavTile(0, 1); break;
            case VirtualKey.Up when tileMode: NavTile(0, -1); break;
            case VirtualKey.Right when tileMode: NavTile(1, 0); break;
            case VirtualKey.Left when tileMode: NavTile(-1, 0); break;
            case VirtualKey.Down:
            case VirtualKey.Tab when !shift:
            case VirtualKey.J when ctrl:
            case VirtualKey.N when ctrl:
                Move(1); break;
            case VirtualKey.Up:
            case VirtualKey.Tab when shift:
            case VirtualKey.K when ctrl:
            case VirtualKey.P when ctrl:
                Move(-1); break;
            case VirtualKey.PageDown: Move(8); break;
            case VirtualKey.PageUp: Move(-8); break;
            case VirtualKey.Enter:
            case VirtualKey.Right when Input.Text.Length == 0:
                RunSelected();
                break;
            case VirtualKey.Escape:
                if (Input.Text.Length > 0) Input.Text = "";
                else Back(hideAtTop: true);
                break;
            case VirtualKey.Back when Input.Text.Length == 0:
            case VirtualKey.Left when Input.Text.Length == 0:
                Back(hideAtTop: false);
                break;
            default: return;
        }
        e.Handled = true;
    }

    void Move(int by)
    {
        int count = tileMode ? tiles.Count : rows.Count;
        if (count == 0) return;
        keyMovedAt = DateTime.Now;
        selected = Math.Clamp(selected + by, 0, count - 1);
        Highlight();
    }

    void Back(bool hideAtTop)
    {
        if (LeavePage()) return;
        string from = current;
        if (finding)
        {
            // The finder, opened from the launcher's Files item: back to the launcher, on Files.
            if (!findFromLauncher) { if (hideAtTop) Dismiss(); return; }
            finding = false;
            launching = true;
            selectKey = launchItems.FirstOrDefault(i => i.BuiltIn == "files")?.Key;
            Render();
            Slide(-1);
            return;
        }
        if (confirming != null) confirming = null;
        else if (pickingTheme) { pickingTheme = false; from = entries.Values.FirstOrDefault(e => e.Kind == "themes")?.Id ?? ""; }
        else if (backgroundsOf != null) { backgroundsOf = null; from = entries.Values.FirstOrDefault(e => e.Kind == "backgrounds")?.Id ?? ""; }
        else if (current.Length > 0 && current != start) current = entries.TryGetValue(current, out var e) ? e.Parent : "";
        else if (menuFromLauncher)
        {
            // The menu's top, opened from the launcher: back to the launcher, on Menu.
            menuFromLauncher = false;
            launching = true;
            selectKey = launchItems.FirstOrDefault(i => i.BuiltIn == "menu")?.Key;
            Render();
            Slide(-1);
            return;
        }
        else { if (hideAtTop) Dismiss(); return; }
        // Back on the row (or tile) you came from.
        selected = Math.Max(0, MenuModel.Children(entries, current).FindIndex(c => c.Id == from));
        selectKey = from;
        Render();
        Slide(-1);
    }

    // --- doing things -----------------------------------------------------------------------
    void Pick(MenuEntry e)
    {
        Input.Text = "";
        if (e.IsSubmenu) { current = e.Id; selected = 0; Render(); Slide(1); return; }
        if (e.Package != null && installed.Contains(e.Package)) return; // already installed
        if (e.Kind == "themes") { pickingTheme = true; selected = Math.Max(0, MenuModel.Themes().FindIndex(t => t.File == themeName)); Render(); Slide(1); return; }
        if (e.Kind == "backgrounds") { OpenBackgrounds(themeName); Render(); Slide(1); return; }
        if (e.Confirm) { confirming = e; selected = 1; Render(); return; }
        Run(e);
    }

    // Open a file (its program) or folder (Explorer), or show it selected in its folder.
    void OpenFile(FileSearch.Hit hit, string query, bool reveal)
    {
        HideMenu();
        if (!hit.IsDir) FileSearch.Opened(hit.Path, query);
        try
        {
            if (reveal) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{hit.Path}\"") { UseShellExecute = true });
            else if (hit.IsDir) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{hit.Path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(hit.Path) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // Win+F (omawin files): the file finder, or hide it if it's in front.
    public void ToggleFinder()
    {
        try
        {
            if (Showing && GetForegroundWindow() == hwnd && finding) Dismiss();
            else Show("", asFinder: true);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // A launcher row: Menu turns this window into the system menu, Files into the file finder,
    // Projects, Clipboard, Localhost, Reports and Shortcuts into their page (MainWindow.Pages.cs);
    // apps start through shell:AppsFolder; anything else is a program, URL or settings page.
    void Launch(LaunchItem item, string query = "")
    {
        Launcher.Remember(item, query); // so it ranks higher next time, most of all for this query
        Input.Text = "";
        if (item.BuiltIn == "files")
        {
            launching = false;
            finding = true;
            findFromLauncher = true;
            selected = 0;
            Render();
            Slide(1);
            return;
        }
        if (item.BuiltIn == "menu")
        {
            launching = false;
            menuFromLauncher = true;
            start = current = "";
            selected = 0;
            Render();
            Slide(1);
            return;
        }
        if (IsPage(item.BuiltIn))
        {
            var rest = item.Command.Split(' ', 2);
            EnterPage(item.BuiltIn!, rest.Length > 1 ? rest[1].Trim() : "");
            if (page != null) Slide(1);
            return;
        }
        HideMenu(instant: !item.IsApp && CapturesScreen(item.Command));
        try
        {
            if (item.IsApp)
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.AppId}") { UseShellExecute = true });
            else
                StartCommand(item.Command);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // A command that captures the screen (omawin capture ..., SystemMenu --snip): the menu goes at
    // once instead of fading, so it isn't in the picture.
    static bool CapturesScreen(string command) => Regex.IsMatch(command, @"(^|\s)(capture\s|--snip\b)", RegexOptions.IgnoreCase);

    void Run(MenuEntry e)
    {
        HideMenu(instant: CapturesScreen(e.Argument));
        try
        {
            switch (e.Kind)
            {
                case "run": StartCommand(e.Argument); break;
                case "omawin": // one of omawin's commands (bin/commands.txt), hidden
                    StartCommand("omawin.exe " + e.Argument);
                    break;
                case "edit": // in the default editor (omawin launch editor)
                    MenuModel.Omawin("launch", "editor", Regex.Replace(e.Argument, "^~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                    break;
                case var k when k.StartsWith('@'):
                    // A launcher page (Shortcuts, Clipboard, Localhost, ...): this window, on it.
                    OpenPage(k[1..] + (e.Argument.Length > 0 ? " " + e.Argument : ""));
                    break;
                default:
                    // Installs, removals and other jobs: menu.ps1 runs them in a "menu-task" terminal.
                    RunPowerShell($"-Run \"{e.Id}\"");
                    break;
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void SetTheme(string file)
    {
        RunPowerShell($"-File \"{Path.Combine(MenuModel.ThemesDir, "set-theme.ps1")}\" {file}", script: false);
        // Repaint right away; set-theme.ps1 takes care of everything else.
        ApplyTheme(file);
        pickingTheme = true;
        Render();
    }

    // The background picker's Enter: one of your theme's backgrounds changes only the wallpaper
    // (set-theme.ps1 -Background, as quick as Win+Ctrl+Space); another theme's switches to that
    // theme, on that background. The picker stays, the new one checked, to try another.
    void SetBackground(string file, string source)
    {
        var name = ThemePictures.Name(source);
        var script = Path.Combine(MenuModel.ThemesDir, "set-theme.ps1");
        RunPowerShell(file == themeName ? $"-File \"{script}\" -Background \"{name}\"" : $"-File \"{script}\" {file} -Background \"{name}\"", script: false);
        pickedBackground = (file, source);
        ApplyTheme(file); // another theme's: repaint right away, as SetTheme does
        selectKey = BackgroundKey(file, source);
        Render();
    }

    static void RunPowerShell(string args, bool script = true)
    {
        var target = script ? $"-File \"{Path.Combine(MenuModel.Repo, "default", "launcher", "menu.ps1")}\" {args}" : args;
        Process.Start(new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -WindowStyle Hidden {target}") { UseShellExecute = false, CreateNoWindow = true });
    }

    // Like the launcher's items.txt: a URL or protocol link, or a program and its arguments.
    static void StartCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (Launcher.Link.IsMatch(command))
        {
            Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
            return;
        }
        var m = Regex.Match(command, "^\"([^\"]+)\"\\s*(.*)$");
        if (!m.Success) m = Regex.Match(command, @"^(\S+)\s*(.*)$");
        if (!m.Success) return;
        // omawin is a console program: run it without a console window flashing up.
        bool omawin = Regex.IsMatch(m.Groups[1].Value, @"^omawin(\.exe)?$", RegexOptions.IgnoreCase);
        Process.Start(new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value) { UseShellExecute = !omawin, CreateNoWindow = omawin });
    }
}
