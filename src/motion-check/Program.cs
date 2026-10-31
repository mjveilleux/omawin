using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Omawin.MotionCheck;

// The menu app's motion, measured on a real Windows desktop (CI: .github/workflows/animations.yml,
// roadmap 1.4). It starts its own copy of the menu app with animations on or off, opens the
// launcher the way Win+Space does (the request file and the signal, as bin/omawin.cs Send), closes
// it with Esc, records the menu's place on screen at ~60 frames a second throughout, and checks
// what Motion.cs promises: on, the launcher fades in within Enter (plus its cascade) and out within
// Exit, each plus a budget for a slow runner; off, it appears and goes in one jump.
//
//   motion-check --menu <SystemMenu.exe> --repo <omawin checkout> --out <folder> --animations on|off [--budget-ms 100]
//
// Exit code 0 passed, 1 a check failed, 2 it couldn't run. It refuses to run beside a menu app
// that's already running (yours): it's for a desktop it can have to itself.
public static class Program
{
    public static int Main(string[] args)
    {
        var o = Options.Parse(args);
        if (o == null)
        {
            Console.Error.WriteLine("usage: motion-check --menu <SystemMenu.exe> --repo <omawin checkout> --out <folder> --animations on|off [--budget-ms 100]");
            return 2;
        }
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("motion-check records a Windows desktop: run it on Windows"); return 2; }
        try { return Run(o); }
        catch (Exception ex) { Console.Error.WriteLine("motion-check: " + ex.Message); return 2; }
    }

    sealed record Options(string Menu, string Repo, string Out, bool Animated, double BudgetMs)
    {
        public static Options? Parse(string[] args)
        {
            string? menu = null, repo = null, outDir = null, animations = null;
            double budget = 100;
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--menu": menu = args[i + 1]; break;
                    case "--repo": repo = args[i + 1]; break;
                    case "--out": outDir = args[i + 1]; break;
                    case "--animations": animations = args[i + 1]; break;
                    case "--budget-ms" when double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b >= 0: budget = b; break;
                    default: return null;
                }
            }
            if (args.Length % 2 != 0 || menu == null || repo == null || outDir == null || animations is not ("on" or "off")) return null;
            return new Options(Path.GetFullPath(menu), Path.GetFullPath(repo), Path.GetFullPath(outDir), animations == "on", budget);
        }
    }

    // The menu app's request path and setting (App.xaml.cs, Motion.cs, bin/omawin.cs).
    const string SignalName = @"Local\dotfiles-system-menu-signal";
    const string WindowTitle = "System Menu";
    static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
    static readonly string RequestFile = Path.Combine(StateDir, "system-menu-request");
    static readonly string SettingFile = Path.Combine(StateDir, "animations");

    [SupportedOSPlatform("windows")]
    static int Run(Options o)
    {
        if (!File.Exists(o.Menu)) throw new FileNotFoundException("no menu app at " + o.Menu);
        if (!Directory.Exists(Path.Combine(o.Repo, "default", "launcher"))) throw new DirectoryNotFoundException("not an omawin checkout: " + o.Repo);
        if (Process.GetProcessesByName("SystemMenu").Length > 0)
            throw new InvalidOperationException("a menu app is already running; this check starts its own, on a desktop it has to itself");
        Directory.CreateDirectory(o.Out);
        Win32.DpiAware();
        var expect = Expectation.For(o.Animated, o.BudgetMs);
        var log = new StringBuilder();
        void Say(string line) { Console.WriteLine(line); log.AppendLine(line); }

        // The two switches the menu app reads at its start, as asked; both put back afterwards.
        // Windows' own is on in both runs: off is omawin's toggle alone, as a PC would have it.
        Directory.CreateDirectory(StateDir);
        string? settingWas = File.Exists(SettingFile) ? File.ReadAllText(SettingFile) : null;
        bool clientWas = Win32.ClientAnimation;
        File.WriteAllText(SettingFile, o.Animated ? "on" : "off");
        Win32.ClientAnimation = true;
        Say($"animations {(o.Animated ? "on" : "off")} (Windows' animation effects were {(clientWas ? "on" : "off")}, on for the run)");

        Process? menu = null;
        try
        {
            using var backdrop = new Win32.Backdrop(40, 150, 90);
            var (sw, sh) = Win32.Screen;
            // The pointer off to the side, where the menu never is: no tile lit up under it.
            Win32.SetCursorPos(sw - 2, sh / 2);

            var start = new ProcessStartInfo(o.Menu, "--start-hidden") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(o.Menu)! };
            start.Environment["DOTFILES"] = o.Repo; // MenuModel.Repo: menus and themes from this checkout
            menu = Process.Start(start) ?? throw new InvalidOperationException("the menu app didn't start");
            IntPtr hwnd = IntPtr.Zero;
            if (!Until(TimeSpan.FromSeconds(90), () => EventWaitHandle.TryOpenExisting(SignalName, out var s) && Dispose(s) && (hwnd = Win32.Find(menu.Id, WindowTitle)) != IntPtr.Zero))
                throw new InvalidOperationException("the menu app's window never came up" + LogTail());
            Say($"menu app up (pid {menu.Id})");
            Thread.Sleep(3000); // its background work at start (indexes, icons) settles

            // Twice through first: the first showing pays for loading and compiling, which isn't motion.
            Win32.RECT place = default;
            for (int i = 0; i < 2; i++)
            {
                Send("--search");
                if (!Until(TimeSpan.FromSeconds(20), () => Win32.IsWindowVisible(hwnd))) throw new InvalidOperationException("the launcher never showed" + LogTail());
                Thread.Sleep(1500);
                place = Win32.Frame(hwnd) ?? throw new InvalidOperationException("no frame bounds for the menu");
                Close(hwnd);
                if (!Until(TimeSpan.FromSeconds(5), () => !Win32.IsWindowVisible(hwnd))) throw new InvalidOperationException("the launcher never hid" + LogTail());
                Thread.Sleep(1000);
            }
            int x = Math.Max(0, place.Left), y = Math.Max(0, place.Top);
            int w = Math.Min(sw, place.Right) - x, h = Math.Min(sh, place.Bottom) - y;
            if (w < 16 || h < 16) throw new InvalidOperationException($"the menu's place ({place.Left},{place.Top})-({place.Right},{place.Bottom}) is off screen");
            Say($"menu at ({x},{y}), {w}x{h}");

            var clock = Stopwatch.StartNew();
            // Opening: a little of the screen before, the request, then long enough for the whole cascade.
            List<Frame> opening;
            using (var rec = new Win32.Recorder(x, y, w, h, clock))
            {
                Thread.Sleep(300);
                double t0 = clock.Elapsed.TotalMilliseconds;
                Send("--search");
                Thread.Sleep(TimeSpan.FromMilliseconds(expect.InWithinMs + 1000));
                opening = rec.Stop(t0);
            }
            bool shown = Win32.IsWindowVisible(hwnd);
            var after = Win32.Frame(hwnd);
            bool moved = after is { } a && (a.Left != place.Left || a.Top != place.Top || a.Right != place.Right || a.Bottom != place.Bottom);
            Thread.Sleep(300);

            List<Frame> closing;
            string how;
            using (var rec = new Win32.Recorder(x, y, w, h, clock))
            {
                Thread.Sleep(300);
                double t0 = clock.Elapsed.TotalMilliseconds;
                how = Close(hwnd);
                Thread.Sleep(TimeSpan.FromMilliseconds(expect.OutWithinMs + 800));
                closing = rec.Stop(t0);
            }
            bool hidden = !Win32.IsWindowVisible(hwnd);

            var problems = new List<string>();
            if (!shown) problems.Add("opening: the window wasn't visible once it should have arrived");
            if (moved) problems.Add("opening: the menu opened somewhere other than where it was measured");
            if (!hidden) problems.Add("closing: the window was still visible once it should have gone");
            return Report(o, expect, opening, closing, how, problems, log);
        }
        finally
        {
            if (menu != null)
            {
                try { Send("--quit"); } catch (Exception) { } // gone already: nothing to ask
                if (!menu.WaitForExit(10_000)) menu.Kill();
                menu.Dispose();
            }
            if (settingWas != null) File.WriteAllText(SettingFile, settingWas);
            else File.Delete(SettingFile); // it made this one
            Win32.ClientAnimation = clientWas;
        }
    }

    // What `omawin` does for a menu key (bin/omawin.cs Send): the request in the file, then the signal.
    [SupportedOSPlatform("windows")]
    static void Send(string request)
    {
        File.WriteAllText(RequestFile, request);
        Win32.AllowSetForegroundWindow(-1); // let it take focus, as omawin.exe does
        using var signal = EventWaitHandle.OpenExisting(SignalName);
        signal.Set();
    }

    // Esc, as you'd close it, when it has the keyboard; else the window's close (the same Dismiss).
    [SupportedOSPlatform("windows")]
    static string Close(IntPtr hwnd)
    {
        if (Win32.GetForegroundWindow() == hwnd) { Win32.PressEscape(); return "Esc"; }
        Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return "WM_CLOSE (the menu didn't have the keyboard)";
    }

    static bool Dispose(IDisposable? d) { d?.Dispose(); return true; }

    static bool Until(TimeSpan limit, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (done()) return true;
            Thread.Sleep(20);
        }
        return done();
    }

    static string LogTail()
    {
        var file = Path.Combine(StateDir, "system-menu.log");
        if (!File.Exists(file)) return "";
        var lines = File.ReadAllLines(file);
        return "\n--- system-menu.log (end)\n" + string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 30)));
    }

    // Levels, the checks, the frames and the summary; the exit code.
    static int Report(Options o, Expectation expect, List<Frame> opening, List<Frame> closing, string how, List<string> problems, StringBuilder log)
    {
        string mode = o.Animated ? "on" : "off";
        var openBefore = opening.LastOrDefault(f => f.Ms < 0) ?? throw new InvalidOperationException("no frame before the request");
        var openAfter = opening[^1];
        var closeBefore = closing.LastOrDefault(f => f.Ms < 0) ?? throw new InvalidOperationException("no frame before Esc");
        var closeAfter = closing[^1];

        // 1 is the menu settled, 0 the screen without it, in both.
        var openLevels = opening.Select(f => FrameMath.Level(f, openBefore, openAfter)).ToList();
        var closeLevels = closing.Select(f => FrameMath.Level(f, closeAfter, closeBefore)).ToList();
        if (double.IsNaN(FrameMath.Level(openAfter, openBefore, openAfter)))
            problems.Add("the menu and the background look alike: nothing to measure");
        var open = Analysis.Find(Samples(opening, openLevels), Way.In);
        var close = Analysis.Find(Samples(closing, closeLevels), Way.Out);
        problems.InsertRange(0, Analysis.Problems(open, close, expect));
        // Closed means the screen is as it was before it opened.
        double back = FrameMath.Level(closeAfter, openBefore, openAfter);
        if (!(back <= 0.1)) problems.Add($"closing: the screen afterwards isn't the one from before it opened (level {back:0.00})");
        double openFps = Analysis.Fps(opening.Select(f => f.Ms).ToList()), closeFps = Analysis.Fps(closing.Select(f => f.Ms).ToList());
        if (Math.Min(openFps, closeFps) < Analysis.MinFps) problems.Add($"the recording ran at {Math.Min(openFps, closeFps):0} frames a second: too few to judge (at least {Analysis.MinFps:0})");

        var dir = o.Out;
        Directory.CreateDirectory(Path.Combine(dir, "frames"));
        var csv = new StringBuilder("phase,ms,level\n");
        for (int i = 0; i < opening.Count; i++) csv.Append(CultureInfo.InvariantCulture, $"open,{opening[i].Ms:0.0},{openLevels[i]:0.0000}\n");
        for (int i = 0; i < closing.Count; i++) csv.Append(CultureInfo.InvariantCulture, $"close,{closing[i].Ms:0.0},{closeLevels[i]:0.0000}\n");
        File.WriteAllText(Path.Combine(dir, "levels.csv"), csv.ToString());
        SaveFrames(dir, "open", opening, openLevels, open);
        SaveFrames(dir, "close", closing, closeLevels, close);

        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"### Menu motion, animations {mode}: {(problems.Count == 0 ? "passed" : "FAILED")}");
        md.AppendLine();
        md.AppendLine("| | first change | settled | took | allowed | frames in between |");
        md.AppendLine("|---|---|---|---|---|---|");
        md.AppendLine(Row("opening (Motion.Enter + cascade)", open, o.Animated ? $"{expect.InWithinMs:0} ms" : $"≤ {Analysis.MaxBetweenWhenOff} frame between"));
        md.AppendLine(Row("closing (Motion.Exit)", close, o.Animated ? $"{expect.OutWithinMs:0} ms" : $"≤ {Analysis.MaxBetweenWhenOff} frame between"));
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Recorded at {openFps:0} / {closeFps:0} frames a second (opening / closing); closed with {how}; budget {expect.BudgetMs:0} ms for a slow runner. Times are from the request (Win+Space's) and from the Esc. Contact sheets: `open-sheet.png`, `close-sheet.png` (the bar under each frame is its level: green arrived, red gone).");
        if (problems.Count > 0)
        {
            md.AppendLine();
            foreach (var p in problems) md.AppendLine("- " + p);
        }
        File.WriteAllText(Path.Combine(dir, "summary.md"), md.ToString());
        File.WriteAllText(Path.Combine(dir, "run.log"), log.ToString());
        Console.WriteLine(md.ToString());
        return problems.Count == 0 ? 0 : 1;
    }

    static List<Sample> Samples(List<Frame> frames, List<double> levels) =>
        frames.Select((f, i) => new Sample(f.Ms, levels[i])).Where(s => s.Ms >= 0).ToList();

    static string Row(string name, Transition t, string allowed) => string.Create(CultureInfo.InvariantCulture,
        $"| {name} | {Ms(t.StartMs)} | {Ms(t.EndMs)} | {Ms(t.DurationMs)} | {allowed} | {t.Between} |");

    static string Ms(double? ms) => ms is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v:0} ms") : "never";

    // The frames around the change as PNGs (a few before it to a few after), and a contact sheet
    // of the phase from just before the change to a little after it settled.
    static void SaveFrames(string dir, string phase, List<Frame> frames, List<double> levels, Transition t)
    {
        int first = t.StartMs is { } s ? frames.FindIndex(f => f.Ms >= s) : -1;
        int last = t.EndMs is { } e ? frames.FindIndex(f => f.Ms >= e) : -1;
        if (first < 0) first = frames.FindIndex(f => f.Ms >= 0);
        if (first < 0) first = 0;
        if (last < 0) last = Math.Min(frames.Count - 1, first + 40);
        int from = Math.Max(0, first - 3), to = Math.Min(frames.Count - 1, Math.Min(last + 3, from + 59));
        for (int i = from; i <= to; i++)
            File.WriteAllBytes(Path.Combine(dir, "frames", string.Create(CultureInfo.InvariantCulture, $"{phase}-{i:000}-{frames[i].Ms:0}ms.png")), Png.Encode(frames[i]));

        double sheetFrom = frames[from].Ms - 100, sheetTo = frames[to].Ms + 300;
        var tiles = frames.Select((f, i) => (Frame: f, Level: levels[i])).Where(p => p.Frame.Ms >= sheetFrom && p.Frame.Ms <= sheetTo).Take(96).ToList();
        if (tiles.Count > 0) File.WriteAllBytes(Path.Combine(dir, $"{phase}-sheet.png"), Png.Encode(ContactSheet.Make(tiles)));
    }
}
