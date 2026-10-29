using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace SystemMenu;

// The bar on one monitor: a borderless strip across its top, 26 px (scaled with the monitor),
// in the theme's background, never activated and never in Alt+Tab or the taskbar (GlazeWM
// leaves SystemMenu's windows alone: glazewm/config.yaml). Its widgets sit in bar.json's five
// places as Zebar's bar.html lays them out: left, then the clock's three in the middle (the clock
// kept centred however wide what's beside it is), then right.
public sealed unsafe class BarWindow : Window
{
    public const int Height = 26;
    public const string WindowTitle = "omawin bar"; // omawin.exe's strip waits for a window of this name

    public readonly IntPtr Hwnd;
    readonly AppWindow appWindow;
    readonly Grid root = new();
    public readonly StackPanel Left = Row(), CenterBegin = Row(), Center = Row(), CenterEnd = Row(), Right = Row();
    public readonly List<BarWidget> Widgets = [];
    public GlazeWm.Monitor? Monitor; // GlazeWM's monitor this bar is on, once it has said
    public (int X, int Y, int Width, int Height) Screen;

    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> proc, nuint id, IntPtr data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    bool subclassed;

    // WM_NCCALCSIZE answered with 0: the whole window is its client area, so whatever frame style
    // WinUI sets, no frame is drawn (the usual way to make a window frameless).
    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    static IntPtr NoFrame(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data) =>
        message == 0x0083 && wParam != IntPtr.Zero ? IntPtr.Zero : DefSubclassProc(hwnd, message, wParam, lParam);

    static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

    public BarWindow()
    {
        Title = WindowTitle;
        Hwnd = WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Hwnd));
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        // A tool window (not in Alt+Tab or the taskbar; AppWindow.IsShownInSwitchers isn't there for
        // an unpackaged app) that's never activated: clicks go to it, focus stays where it was.
        SetWindowLongPtr(Hwnd, -20, GetWindowLongPtr(Hwnd, -20) | 0x80 | 0x08000000); // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        // Square, with no frame: Windows 11 rounds a window's corners and draws a hairline round it.
        int square = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(Hwnd, 33, ref square, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(Hwnd, 34, ref none, sizeof(int)); // DWMWA_BORDER_COLOR

        // Outer: left | middle | right, the two sides equal so the middle is the screen's middle.
        root.Padding = new Thickness(6, 0, 6, 0);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Left.HorizontalAlignment = HorizontalAlignment.Left;
        Right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(Right, 2);

        // Middle: what's beside the clock takes two equal columns, so the clock stays put.
        var middle = new Grid { ColumnSpacing = 10 };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        CenterBegin.HorizontalAlignment = HorizontalAlignment.Right;
        CenterEnd.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(Center, 1);
        Grid.SetColumn(CenterEnd, 2);
        middle.Children.Add(CenterBegin);
        middle.Children.Add(Center);
        middle.Children.Add(CenterEnd);
        Grid.SetColumn(middle, 1);

        root.Children.Add(Left);
        root.Children.Add(middle);
        root.Children.Add(Right);
        Content = root;
    }

    public StackPanel Place(string place) => place switch
    {
        "left" => Left,
        "centerBegin" => CenterBegin,
        "center" => Center,
        "centerEnd" => CenterEnd,
        _ => Right,
    };

    // Across the top of this monitor (its pixels), Height scaled by its DPI.
    public void PlaceOn((int X, int Y, int Width, int Height) screen, double scale)
    {
        Screen = screen;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(screen.X, screen.Y, screen.Width, (int)Math.Round(Height * scale)));
        Unframe();
    }

    public void Paint(Theme theme)
    {
        root.Background = new SolidColorBrush(Bar.Hex(theme.Background));
        root.RequestedTheme = theme.Mode == "light" ? ElementTheme.Light : ElementTheme.Dark;
        foreach (var w in Widgets) w.Paint(theme);
    }

    public void ShowBar()
    {
        appWindow.Show(false); // shown without being activated
        Unframe();
    }

    // A plain popup: without a caption or a sizing frame, nothing is drawn round the strip. WinUI
    // puts its frame styles back when the window is shown or moved, so this runs after each.
    void Unframe()
    {
        if (!subclassed) subclassed = SetWindowSubclass(Hwnd, &NoFrame, 1, IntPtr.Zero);
        long style = GetWindowLongPtr(Hwnd, -16).ToInt64(); // GWL_STYLE
        style &= ~(0x00C00000L | 0x00040000L | 0x00080000L | 0x00020000L | 0x00010000L); // caption, sizing frame, menu, min, max
        SetWindowLongPtr(Hwnd, -16, new IntPtr(style | 0x80000000L)); // WS_POPUP
        long ex = GetWindowLongPtr(Hwnd, -20).ToInt64();
        SetWindowLongPtr(Hwnd, -20, new IntPtr(ex & ~(0x1L | 0x100L | 0x200L | 0x20000L))); // no dialog, window, client or static edge
        // Styles only take effect once the frame is worked out again.
        SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0010); // FRAMECHANGED, NOSIZE, NOMOVE, NOZORDER, NOACTIVATE
    }
}
