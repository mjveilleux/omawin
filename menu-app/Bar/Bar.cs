using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace SystemMenu;

// The native bar (docs/native-bar.md): Zebar's bar, drawn by the menu app itself, one window per
// monitor. On while %LOCALAPPDATA%\launcher\bar says "native" (omawin bar native; omawin bar
// zebar goes back). Its layout is Zebar's bar.json; its data comes straight from GlazeWM's IPC
// and Windows, gathered here and handed to every widget (Widgets.cs).
public static class Bar
{
    static string SettingFile => Path.Combine(MenuModel.StateDir, "bar");
    static string LayoutFile => Path.Combine(MenuModel.Repo, @"zebar\tokyo-islands\bar.json");
    public static bool Native => File.Exists(SettingFile) && File.ReadAllText(SettingFile).Trim() == "native";

    public static readonly GlazeWm Wm = new();
    public static FontFamily LogoFont { get; } = new("ms-appx:///omarchy.ttf#omarchy");
    static readonly List<BarWindow> windows = [];
    static DispatcherQueue? ui;
    static Action<string[]> request = _ => { };
    static Theme theme = new();
    static string themeName = "";
    static JsonElement layout;
    static string clockFormat = "EEEE HH:mm", clockAltFormat = "d MMMM 'W'WW yyyy";

    // What the widgets show, kept current by the loops below.
    static AudioDevice? speaker;
    static WifiState? wifi;
    static bool ethernet;
    static int listening, updates;
    static bool started, arrived;

    // From App.OnLaunched: how widgets reach the menu app (a request, as SystemMenu.exe takes it).
    public static void Start(Action<string[]> handle)
    {
        request = handle;
        ui = DispatcherQueue.GetForCurrentThread();
        if (Native) Show();
    }

    // SystemMenu --bar-preview: the bar alone, whichever bar is picked; clicks only logged.
    public static void Preview()
    {
        request = args => App.Log(new Exception("bar preview: " + string.Join(' ', args)));
        ui = DispatcherQueue.GetForCurrentThread();
        try { Show(); } catch (Exception ex) { App.Log(ex); }
        // What it sees, in the log, 5 s on: to check it without moving the mouse.
        Task.Delay(5000).ContinueWith(_ => App.Log(new Exception($"bar preview: {Wm.Last?.Monitors.Count ?? 0} monitor(s) from GlazeWM; tray: " +
            string.Join(" | ", Tray.Icons.Select(i => $"{i.Tip} (v{i.Version})")))));
    }

    public static void Request(string[] args) => ui?.TryEnqueue(() => request(args));

    // omawin.exe <command>, hidden (a terminal, an update): what Zebar's ctx.omawin did.
    public static void Omawin(string args)
    {
        try
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin\omawin.exe");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // `omawin bar native|zebar|toggle` (SystemMenu --bar ...): which bar, remembered.
    public static void Set(string which)
    {
        bool native = which == "toggle" ? !Native : which == "native";
        try { Directory.CreateDirectory(MenuModel.StateDir); File.WriteAllText(SettingFile, native ? "native" : "zebar"); } catch (Exception ex) { App.Log(ex); }
        if (native)
        {
            try { Show(); } catch (Exception ex) { App.Log(ex); Hide(); return; } // Zebar stays if it didn't come up
            StopZebar();
        }
        else { Hide(); Omawin("start zebar"); }
    }

    static void StopZebar()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("zebar"))
            using (p) try { if (p.SessionId == session) p.Kill(); } catch { }
    }

    static void Show()
    {
        if (windows.Count > 0) return;
        ReadLayout();
        ApplyTheme(force: true);
        foreach (var screen in Screens()) Open(screen);
        if (!started)
        {
            started = true;
            Wm.Changed += _ => ui?.TryEnqueue(Push);
            Wm.Start();
            StartLoops();
            WatchTheme();
        }
        // The widgets fade in once GlazeWM has said where things are, or after 0.6 s anyway.
        Task.Delay(600).ContinueWith(_ => ui?.TryEnqueue(Arrive));
        Push();
    }

    static void Hide()
    {
        foreach (var w in windows) w.Close();
        windows.Clear();
        arrived = false;
    }

    // Every monitor's pixels, primary first.
    // (DisplayArea.FindAll's list only works by index: enumerating it throws, a Windows App SDK bug.)
    static List<(int X, int Y, int Width, int Height)> Screens()
    {
        var all = DisplayArea.FindAll();
        var screens = new List<(int, int, int, int)>();
        for (int i = 0; i < all.Count; i++) screens.Add((all[i].OuterBounds.X, all[i].OuterBounds.Y, all[i].OuterBounds.Width, all[i].OuterBounds.Height));
        return [.. screens.OrderBy(s => s.Item1 != 0 || s.Item2 != 0)];
    }

    static void Open((int X, int Y, int Width, int Height) screen)
    {
        var bar = new BarWindow();
        foreach (var place in new[] { "left", "centerBegin", "center", "centerEnd", "right" })
        {
            if (!layout.TryGetProperty(place, out var names)) continue;
            foreach (var name in names.EnumerateArray())
                if (BarWidget.Create(name.GetString() ?? "") is { } widget)
                {
                    widget.Attach(bar);
                    widget.Element.Opacity = 0;
                    bar.Place(place).Children.Add(widget.Element);
                    bar.Widgets.Add(widget);
                }
        }
        bar.Paint(theme);
        bar.PlaceOn(screen, Scale(screen));
        bar.ShowBar();
        windows.Add(bar);
    }

    static void ReadLayout()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LayoutFile));
            layout = doc.RootElement.Clone();
            if (layout.TryGetProperty("clock", out var c))
            {
                if (c.TryGetProperty("format", out var f)) clockFormat = f.GetString() ?? clockFormat;
                if (c.TryGetProperty("formatAlt", out var a)) clockAltFormat = a.GetString() ?? clockAltFormat;
            }
        }
        catch (Exception ex) { App.Log(ex); layout = JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    // --- the data -------------------------------------------------------------------------------
    public static void Refresh() => Task.Run(() => { speaker = Audio.DefaultSpeaker(); ui?.TryEnqueue(Push); });

    static void StartLoops()
    {
        // Each second: the clock, the sound, the battery, stay awake (all cheap reads).
        new Thread(() =>
        {
            while (true)
            {
                speaker = Audio.DefaultSpeaker();
                ui?.TryEnqueue(Push);
                Thread.Sleep(1000 - DateTime.Now.Millisecond); // on the second, so the minute turns on time
            }
        }) { IsBackground = true, Name = "bar" }.Start();
        // Every 3 s: the monitors. One plugged in or out, or moved: the bars are made again for them.
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(3000);
                ui?.TryEnqueue(() =>
                {
                    if (windows.Count == 0) return;
                    var now = Screens();
                    var had = windows.Select(w => w.Screen).ToList();
                    if (now.Count == had.Count && now.All(had.Contains)) return;
                    Hide();
                    try { Show(); } catch (Exception ex) { App.Log(ex); }
                });
            }
        }) { IsBackground = true, Name = "bar monitors" }.Start();
        // Every 10 s: the network (Wi-Fi's signal moves slowly).
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                    ethernet = profile != null && !profile.IsWlanConnectionProfile && profile.NetworkAdapter?.IanaInterfaceType == 6;
                    wifi = QuickSettings.Wifi();
                }
                catch (Exception ex) { App.Log(ex); }
                ui?.TryEnqueue(Push);
                Thread.Sleep(10000);
            }
        }) { IsBackground = true, Name = "bar network" }.Start();
        // Updates waiting: 20 s on (not in the rush of signing in), then every 6 hours, as Omarchy.
        new Thread(() =>
        {
            Thread.Sleep(20000);
            while (true)
            {
                updates = UpdatesWaiting();
                ui?.TryEnqueue(Push);
                Thread.Sleep(TimeSpan.FromHours(6));
            }
        }) { IsBackground = true, Name = "bar updates" }.Start();
        new Thread(VoiceMeter) { IsBackground = true, Name = "bar voice" }.Start();
    }

    static int UpdatesWaiting()
    {
        try
        {
            var script = Path.Combine(MenuModel.Repo, @"bin\update-available.ps1");
            var p = Process.Start(new ProcessStartInfo("pwsh", $"-NoProfile -NonInteractive -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return int.TryParse(output.Trim(), out var n) ? n : 0;
        }
        catch (Exception ex) { App.Log(ex); return 0; }
    }

    // voice-meter (~/.local/bin) prints "1 <level>" while voice typing listens, "2 ..." for
    // omawin's agent, "0" otherwise; started again if it stops.
    static void VoiceMeter()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin\voice-meter.exe");
        while (true)
        {
            try
            {
                if (!File.Exists(exe)) return;
                using var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                string? line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var on = line.Trim().Split(' ')[0];
                    int now = on == "1" ? 1 : on == "2" ? 2 : 0;
                    if (now != listening) { listening = now; ui?.TryEnqueue(Push); }
                }
            }
            catch (Exception ex) { App.Log(ex); }
            listening = 0;
            Thread.Sleep(3000);
        }
    }

    static BarData.Battery? Battery()
    {
        try
        {
            var status = Windows.System.Power.PowerManager.BatteryStatus;
            if (status == Windows.System.Power.BatteryStatus.NotPresent) return null;
            return new BarData.Battery(Windows.System.Power.PowerManager.RemainingChargePercent, status == Windows.System.Power.BatteryStatus.Charging);
        }
        catch { return null; }
    }

    // Everything to every widget, on the UI thread.
    static void Push()
    {
        if (windows.Count == 0) return;
        var wm = Wm.Last;
        var data = new BarData(wm, DateTime.Now, Battery(), wifi, ethernet, speaker, StayAwake.On, listening, updates);
        foreach (var bar in windows)
        {
            bar.Monitor = wm?.Monitors.FirstOrDefault(m => m.X == bar.Screen.X && m.Y == bar.Screen.Y);
            foreach (var w in bar.Widgets)
                try { w.Update(data); } catch (Exception ex) { App.Log(ex); }
        }
        if (wm != null) Arrive();
    }

    // Each widget fades down into place, left to right, the first time (Zebar's bar did the same).
    static void Arrive()
    {
        if (arrived || windows.Count == 0) return;
        arrived = true;
        foreach (var bar in windows)
        {
            int i = 0;
            foreach (var w in bar.Widgets)
            {
                var el = w.Element;
                var move = new TranslateTransform { Y = -4 };
                el.RenderTransform = move;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var begin = TimeSpan.FromMilliseconds(25 * i++);
                var board = new Storyboard();
                var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(280), BeginTime = begin, EasingFunction = ease };
                var drop = new DoubleAnimation { From = -4, To = 0, Duration = TimeSpan.FromMilliseconds(280), BeginTime = begin, EasingFunction = ease };
                Storyboard.SetTarget(fade, el);
                Storyboard.SetTargetProperty(fade, "Opacity");
                Storyboard.SetTarget(drop, move);
                Storyboard.SetTargetProperty(drop, "Y");
                board.Children.Add(fade);
                board.Children.Add(drop);
                board.Begin();
            }
        }
    }

    // --- the theme --------------------------------------------------------------------------------
    static FileSystemWatcher? themeWatcher;

    static void WatchTheme()
    {
        try
        {
            themeWatcher = new FileSystemWatcher(MenuModel.StateDir, "theme") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            themeWatcher.Changed += (_, _) => ui?.TryEnqueue(() => ApplyTheme(force: false));
            themeWatcher.Created += (_, _) => ui?.TryEnqueue(() => ApplyTheme(force: false));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void ApplyTheme(bool force)
    {
        var name = MenuModel.CurrentThemeName();
        if (!force && name == themeName) return;
        themeName = name;
        theme = MenuModel.LoadTheme(name);
        foreach (var w in windows) w.Paint(theme);
    }

    public static Windows.UI.Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte P(int i) => Convert.ToByte(hex.Substring(i, 2), 16);
        return hex.Length >= 6 ? Windows.UI.Color.FromArgb(255, P(0), P(2), P(4)) : Windows.UI.Color.FromArgb(255, 0, 0, 0);
    }

    // --- monitors ---------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    static double Scale((int X, int Y, int Width, int Height) s)
    {
        try { GetDpiForMonitor(MonitorFromPoint(new Point { X = s.X + 1, Y = s.Y + 1 }, 2), 0, out uint dpi, out _); return dpi / 96.0; }
        catch { return 1; }
    }

    // --- the clock's formats (bar.json's, in Luxon's tokens as Zebar took them) --------------------
    public static string ClockMain(DateTime now) => Format(now, clockFormat);
    public static string ClockAlt(DateTime now) => Format(now, clockAltFormat);

    static string Format(DateTime t, string format)
    {
        var s = new StringBuilder();
        for (int i = 0; i < format.Length;)
        {
            if (format[i] == '\'')
            {
                int end = format.IndexOf('\'', i + 1);
                if (end < 0) end = format.Length;
                s.Append(format, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }
            int n = 1;
            while (i + n < format.Length && format[i + n] == format[i]) n++;
            var token = format.Substring(i, n);
            s.Append(token switch
            {
                "EEEE" => t.ToString("dddd"),
                "EEE" => t.ToString("ddd"),
                "HH" => t.ToString("HH"),
                "H" => t.Hour.ToString(),
                "hh" => t.ToString("hh"),
                "h" => (t.Hour % 12 == 0 ? 12 : t.Hour % 12).ToString(),
                "mm" => t.ToString("mm"),
                "ss" => t.ToString("ss"),
                "a" => t.ToString("tt"),
                "d" => t.Day.ToString(),
                "dd" => t.ToString("dd"),
                "MMMM" => t.ToString("MMMM"),
                "MMM" => t.ToString("MMM"),
                "MM" => t.ToString("MM"),
                "M" => t.Month.ToString(),
                "yyyy" => t.ToString("yyyy"),
                "yy" => t.ToString("yy"),
                "WW" => System.Globalization.ISOWeek.GetWeekOfYear(t).ToString("00"),
                "W" => System.Globalization.ISOWeek.GetWeekOfYear(t).ToString(),
                _ => token,
            });
            i += n;
        }
        return s.ToString();
    }
}
