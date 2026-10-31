using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SystemMenu;

// One copy runs all the time, hidden, so the menu shows instantly. Starting SystemMenu.exe
// again just passes its arguments to that copy and exits; so does `omawin` (bin/omawin.cs,
// the `app` rows of bin/commands.txt), more quickly:
//   SystemMenu                 One Search on Commands, browsing (what the system menu was)
//   SystemMenu --search [filter|page] [text]  One Search (Win+Space; MainWindow.Search.cs): on a
//                              filter (file: Win+F, clipboard: Win+Shift+V, settings, …) or a page;
//                              --browse [section]: Commands, browsing the menu (omawin menu);
//                              --retired-key: Win+Alt+Space, with its note (Search/Request.cs)
//   SystemMenu --launcher [page]  the same as --search (older omawin.exe copies, the bar)
//   SystemMenu --files         the same as --search file
//   SystemMenu --snip [text]   a screenshot (Win+Shift+S), or its text (Win+Shift+T): drag a region (Snip.cs)
//   SystemMenu --snip color    the colour picker; --snip qr: read QR codes in a region
//   SystemMenu --toggle stay-awake  keep the PC awake, or stop (StayAwake.cs)
//   SystemMenu --toggle animations [on|off|status]  every animation on or off (Motion.cs)
//   SystemMenu --qs <section> [action]  one Quick settings toggle without showing anything:
//                              wifi|bluetooth on|off|toggle|connect|disconnect,
//                              brightness up|down|set <n>, power <mode|cycle>,
//                              night-light | dnd (both open Settings: no public API)
//   SystemMenu --icon-selftest   print each app icon's drawn, asked and received pixel sizes
//   SystemMenu --qs-selftest   print what each Quick settings section sees (read only), for
//                              comparison with netsh / Get-PnpDevice / powercfg; handled before
//                              the single-copy check, so it works while the menu app runs
//   SystemMenu --about         About omawin and this PC, in its own window (AboutWindow.xaml.cs)
//   SystemMenu --mic [toggle|mute|unmute|status]  every microphone at once (Microphones.cs)
//   SystemMenu --page <name>   the launcher on one of its pages: projects, clipboard (Win+Shift+V),
//                              localhost, reports, keys, packages, vpn <connection>
//   SystemMenu --open <id>     show it on a submenu, e.g. --open install
//   SystemMenu --select <id>   show the submenu a row is in, with that row picked out
//                              (--select theme:<name> in the theme list); for walkthroughs
//   SystemMenu --type <text>   show it with that typed into the search; for walkthroughs too
//   SystemMenu --key <key>     down / up / tab / shift-tab / pick, as if pressed; walkthroughs too
//   SystemMenu --hide          hide it at once, no animation (omawin profile menu, between runs)
//   SystemMenu --start-hidden  start in the background (GlazeWM runs this at login)
//   SystemMenu --reply <pid> --setting|--settings|--tune-up …  `omawin setting` / `settings` / `tune-up`: the
//                              answer is written for the waiting omawin.exe (Reply.cs)
//   SystemMenu --version       the release this exe is from ((dev) built off a checkout)
//   SystemMenu --render-about <png> [frame]  draw the About window into a PNG and exit; for a
//                              report's screenshots, never in normal use
public static class Program
{
    const string MutexName = @"Local\dotfiles-system-menu";
    const string SignalName = @"Local\dotfiles-system-menu-signal";
    static readonly string RequestFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "system-menu-request");

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    static Mutex? mutex;

    // Timing marks for `omawin profile login` and `omawin profile menu`, as omawin.exe writes
    // them: only while %LOCALAPPDATA%\launcher\trace-on exists. The stamp is taken first, so the
    // file check doesn't count against the step being timed.
    static readonly string TraceLog = Path.Combine(Path.GetDirectoryName(RequestFile)!, "trace.log");
    static readonly string TraceOn = Path.Combine(Path.GetDirectoryName(RequestFile)!, "trace-on");
    internal static bool Tracing => File.Exists(TraceOn);
    internal static void Trace(string label)
    {
        long at = Stopwatch.GetTimestamp();
        if (!Tracing) return;
        try { File.AppendAllText(TraceLog, "menu " + label + "\t" + at + "\n"); } catch (IOException) { }
    }
    // What a `--render-about` run is drawing (App.OnLaunched reads it and exits when it's done).
    internal static (string Path, int Frame)? Render;
    internal static bool BarPreview;

    [STAThread]
    static int Main(string[] args)
    {
        // First, before the mutex and the request file: --version must never poke a running copy
        // (that would toggle the system menu) nor start WinUI.
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Console.WriteLine("SystemMenu " + (typeof(Program).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "(dev)"));
            return 0;
        }
        // The render switch draws the About window into a PNG and exits. It is answered before the
        // hand-off below: the copy that is already running has no way to draw this one, and a
        // screenshot must not depend on one being up.
        // --bar-preview: only the native bar, in a copy of its own beside the running menu app (to
        // work on the bar without stopping the launcher). Its clicks are logged, not acted on.
        if (args.Contains("--bar-preview")) { BarPreview = true; return Start(args, null); }
        int render = Array.IndexOf(args, "--render-about");
        if (render >= 0 && render + 1 < args.Length)
        {
            Render = (args[render + 1], render + 2 < args.Length && int.TryParse(args[render + 2], out var frame) ? frame : -1);
            return Start(args, null);
        }
        // The self test is read only and must run whatever else is running: it is done before the
        // single-copy check, so it is never handed to a running copy (which couldn't print).
        if (args.Contains("--qs-selftest")) return QuickSettingsSelfTest.Run();
        if (args.Contains("--snip-selftest")) return QuickSettingsSelfTest.RunSnip();
        if (args.Contains("--icon-selftest")) return QuickSettingsSelfTest.RunIcons();

        Trace("main");
        mutex = new Mutex(true, MutexName, out bool first);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        // A copy that was stopped can hang in Windows for minutes, exited but still holding the
        // mutex; then this one takes over (requests reach it through the same signal).
        if (!first && AnotherCopyRuns())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RequestFile)!);
            File.WriteAllText(RequestFile, string.Join(' ', args));
            AllowSetForegroundWindow(-1); // let the running copy take focus
            signal.Set();
            return 0;
        }
        return Start(args, signal);
    }

    static int Start(string[] args, EventWaitHandle? signal)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new App(args);
            if (signal == null) return;
            new Thread(() =>
            {
                while (signal.WaitOne())
                {
                    Trace("request");
                    // An empty request is a real one (plain `SystemMenu`: toggle the system menu),
                    // but no file, or one that couldn't be read (a second signal for a request
                    // already handled, or one another copy is still writing; its own signal
                    // follows), is nothing to do.
                    string request;
                    try { request = File.ReadAllText(RequestFile).Trim(); }
                    catch { continue; }
                    try { File.Delete(RequestFile); } catch { }
                    queue.TryEnqueue(() => app.HandleRequest(request.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                }
            }) { IsBackground = true }.Start();
        });
        return 0;
    }

    static bool AnotherCopyRuns()
    {
        using var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
            using (p)
            {
                try { if (p.Id != me.Id && !p.HasExited) return true; }
                catch { return true; } // can't tell: assume it's alive
            }
        return false;
    }
}

public partial class App : Application
{
    readonly string[] startArgs;
    MainWindow? window;
    AboutWindow? about;

    public App(string[] args)
    {
        startArgs = args;
        InitializeComponent();
        // Keep running on a bad moment, and leave a note in %LOCALAPPDATA%\launcher\system-menu.log.
        UnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        // `--render-about`: draw the sheet off screen, write the PNG, exit. Nothing else starts:
        // this is a screenshot, not the menu.
        if (Program.Render is { } render)
        {
            _ = new AboutWindow(rendering: true).SaveAsync(render.Path, render.Frame);
            return;
        }
        if (Program.BarPreview) { Bar.Preview(); return; }
        Program.Trace("launched");
        // Before anything moves: every animation asks it whether to (Motion.cs).
        Motion.Start(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        // The bar first (when it's the one picked, Bar/Bar.cs): at sign-in it's what you see. Its
        // windows draw once this returns, so the rest comes after, queued behind that first frame.
        Bar.Start(HandleRequest);
        Program.Trace("bar");
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            window = new MainWindow();
            Program.Trace("window");
            // Keys GlazeWM sends straight here (`emit omawin <command>`, roadmap 1.2): no omawin.exe,
            // no request file, the same requests as the signal's (Emit.cs).
            var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Bar.Wm.Custom += (name, words) => Emit.Handle(name, words, ui, HandleRequest);
            Bar.Wm.Start(); // already started when the bar is up; once either way
            FileSearch.Start(); // the launcher's file indexes (fff), built in the background
            Background.Start(); // clipboard history, installed apps, reports (Background.cs)
            WinSettings.Start(); // the settings history trimmed, Windows' settings index read if it changed (Settings/)
            Tiling.Start(); // Omarchy's window placement: each new window halves the focused one (Tiling.cs)
            // The keyboard hook, only while Caps Lock's remap, the Copilot key or snippets are on (KeyHook.cs).
            try { KeyHook.Start(() => ui.TryEnqueue(() => HandleRequest(["--launcher"]))); } catch (Exception ex) { Log(ex); }
            StayAwake.Reset(); // a fresh start is never keeping the PC awake
            new Thread(Snip.Warm) { IsBackground = true }.Start(); // the screenshot overlay, ready
            Program.Trace("ready");
            // The theme picker's pictures, decoded while nothing else is going on (ThemeThumbs.cs).
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ThemeThumbs.Preload);
            ThemeThumbs.FetchBackgrounds(); // and every theme's backgrounds, on an unmetered connection
            CommunityThemes.Start(); // the theme picker's Community chip: its index, fetched in the background
            if (!startArgs.Contains("--start-hidden")) HandleRequest(startArgs);
        });
    }

    public static void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(MenuModel.StateDir, "system-menu.log"), $"{DateTime.Now:s} {ex}\n\n"); } catch { }
    }

    public void HandleRequest(string[] args)
    {
        Program.Trace("handle " + (args.Length > 0 ? args[0] : "toggle"));
        // `omawin setting …` and other requests that print an answer (Reply.cs): they need no
        // window, so one that comes early at sign-in is answered too.
        if (args is ["--reply", var pid, _, ..]) { Reply.Answer(pid, args[2..]); return; }
        if (window == null) return;
        // `omawin profile menu` hides it between runs: at once, with no exit animation and no
        // focus handed back, so the next run starts from the same place.
        if (args is ["--hide"]) { window.HideForProfile(); return; }
        if (args.Length > 0 && args[0] == "--search")
        {
            var search = Search.SearchRequest.Parse(args);
            if (search.Page == "about") (about ??= new AboutWindow()).ShowAbout();
            else if (search.Page != null) window.OpenPage((search.Page + " " + search.PageArg).Trim());
            else window.OpenSearch(search.Filter, search.Text, search.Section, search.RetiredKey);
            return;
        }
        int open = Array.IndexOf(args, "--open");
        int select = Array.IndexOf(args, "--select");
        if (open >= 0 && open + 1 < args.Length) window.ShowMenu(args[open + 1], open + 2 < args.Length ? args[open + 2] : null);
        else if (select >= 0 && select + 1 < args.Length) window.SelectItem(args[select + 1]);
        else if (args.Length > 0 && args[0] == "--type") window.TypeText(string.Join(' ', args[1..]));
        else if (args.Length > 1 && args[0] == "--key") window.PressKey(args[1]);
        else if (args.Length > 1 && args[0] is "--page" or "--launcher") window.OpenPage(string.Join(' ', args[1..]).TrimStart('@'));
        else if (args.Contains("--snip"))
        {
            var mode = args.Contains("text") ? SnipMode.Text : args.Contains("color") ? SnipMode.Color : args.Contains("qr") ? SnipMode.Qr : SnipMode.Picture;
            window.FinishHiding(); // a menu still fading out isn't in the picture
            try { Snip.Start(mode); } catch (Exception ex) { Log(ex); }
        }
        else if (args.Length > 1 && args[0] == "--toggle" && args[1] == "stay-awake") StayAwake.Toggle();
        else if (args.Length > 1 && args[0] == "--toggle" && args[1] == "animations") Motion.Toggle(args.Length > 2 ? args[2] : null);
        else if (args.Length > 1 && args[0] == "--bar") Bar.Set(args[1]);
        else if (args.Length > 1 && args[0] == "--say") Snip.Say(string.Join(' ', args[1..])); // the note, for other programs (the music player)
        else if (args.Length > 1 && args[0] == "--qs")
        {
            // One Quick settings toggle, off the UI thread (the radios, WLAN and WMI calls block),
            // answered with the note at the bottom of the screen — no window.
            var section = args[1];
            var rest = args[2..];
            Task.Run(() => QuickSettings.Command(section, rest)).ContinueWith(t => Snip.Say(t.Result));
        }
        // Every microphone muted or not (Microphones.cs), answered with the note; the bar's red mic follows.
        else if (args.Length > 0 && args[0] == "--mic")
        {
            var action = args.Length > 1 ? args[1] : "toggle";
            Task.Run(() => Audio.SetMics(action)).ContinueWith(t => { Snip.Say(t.Result); Bar.Refresh(); });
        }
        else if (args.Contains("--about")) (about ??= new AboutWindow()).ShowAbout();
        else if (args.Contains("--launcher")) window.ToggleLauncher();
        else if (args.Contains("--files")) window.ToggleFinder();
        else if (args.Contains("--start-hidden")) { }
        // Asked to go (menu-app\stop-menu.ps1, before new files go in): started by GlazeWM, this
        // app inherits its uiAccess, and a plain shell isn't allowed to stop it by force.
        else if (args.Contains("--quit")) Environment.Exit(0);
        else window.Toggle();
    }
}
