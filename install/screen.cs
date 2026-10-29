// omawin-screen: install.ps1's screen, drawn smoothly in place.
//
// install.ps1 (install/board.ps1) starts it with its stdin redirected and its stdout on the same
// console, and tells it what happens, one line per event, fields split by tabs:
//
//   init    <version> <what> <log path> <rich: 1|0>
//   step    <name> <state> <seconds> <expected seconds> <note>   a step, added or changed
//   order   <name,name,...>                                       the steps' order
//   enter   <name>                                                the step running now
//   line    <kind> <text> [seconds]                               the step's lines: head note ok warn fail done
//   item    <group> <id> <name> <state> [admin]                   something installed: wait run done have fail
//   note    <text>                                                a line under the items ("3 left in the admin step")
//   admin   <what|detail> ...                                     the admin prompt's screen
//   adminclose
//   warn    <text>  /  next <key> <text>  /  final <failed step> <seconds> <line> <error>
//   pause / resume                                                a question is being asked on the bottom line
//   quit                                                          the last frame, then the list of everything
//
// It keeps nothing of its own beyond what it's told, and ends when its stdin closes (the install
// ended, or was stopped). Built by install/board.ps1 with Windows' own csc.exe: C# 5, no
// dependencies. OMAWIN_SCREEN=<w>x<h> fixes its size (recording it into a file).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

static class Screen
{
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr h, out uint m);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr h, uint m);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);

    // --- what it's been told ------------------------------------------------------------------
    class Step { public string Name, State = "pending", Note = ""; public double Seconds, Expected = 5; public DateTime StartAt, ChangedAt; }
    class Item { public string Group, Id, Name, State, StepName; public bool Admin; public DateTime Appeared, ChangedAt, RunAt; }
    class Line { public string Kind, Text; public double Seconds; public DateTime At; }

    static readonly object Gate = new object();
    static string Version = "", What = "", LogPath = "";
    static bool Rich = true;
    static readonly List<Step> Steps = new List<Step>();
    static string Current = "";
    static string Mode = "log";
    static readonly List<Line> Lines = new List<Line>();
    static readonly List<string> GroupOrder = new List<string>();
    static readonly List<Item> Items = new List<Item>();
    static readonly Dictionary<string, Item> ItemById = new Dictionary<string, Item>();
    static string Note = "";
    static string[] AdminWhat = new string[0];
    static readonly List<string> Warnings = new List<string>();
    static readonly List<string[]> Next = new List<string[]>();
    static bool Final, Failed;
    static string FailedStep = "", FinalLine = "", FinalError = "";
    static string Title = "omawin is installed", Section = "Next";   // the finale's (omawin update: its own)
    static double FinalSeconds;
    static DateTime FinalAt;
    static volatile bool Paused, Quit, InputEnded, Repaint;
    static readonly DateTime Started = DateTime.Now;
    // Every time below is the start plus a stopwatch: a clock that a time sync or a time zone
    // change can't move back or forward mid-install.
    static readonly Stopwatch Mono = Stopwatch.StartNew();
    static DateTime Now { get { return Started + Mono.Elapsed; } }

    static Step GetStep(string name)
    {
        foreach (var s in Steps) if (s.Name == name) return s;
        var n = new Step { Name = name };
        Steps.Add(n);
        return n;
    }

    static readonly Regex Escapes = new Regex("\x1b\\[[0-9;?]*[ -/]*[@-~]|\x1b[@-_]|[\x00-\x08\x0a-\x1f\x7f]");
    static void Handle(string raw)
    {
        raw = Escapes.Replace(raw, " ");
        var f = raw.Split('\t');
        if (f.Length == 0) return;
        var now = Now;
        lock (Gate)
        {
            switch (f[0])
            {
                case "init":
                    if (f.Length > 1) Version = f[1];
                    if (f.Length > 2) What = f[2];
                    if (f.Length > 3) LogPath = f[3];
                    if (f.Length > 4) Rich = f[4] != "0";
                    break;
                case "step":
                {
                    if (f.Length < 3) break;
                    var s = GetStep(f[1]);
                    if (s.State != f[2]) { if (f[2] == "running") s.StartAt = now; s.ChangedAt = now; }
                    s.State = f[2];
                    double d;
                    if (f.Length > 3 && double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.Seconds = d;
                    if (f.Length > 4 && double.TryParse(f[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && d > 0) s.Expected = d;
                    s.Note = f.Length > 5 ? f[5] : "";
                    break;
                }
                case "order":
                    if (f.Length > 1)
                    {
                        var names = f[1].Split(',');
                        var sorted = new List<Step>();
                        foreach (var n in names) sorted.Add(GetStep(n));
                        foreach (var s in Steps) if (!sorted.Contains(s)) sorted.Add(s);
                        Steps.Clear(); Steps.AddRange(sorted);
                    }
                    break;
                case "enter":
                    if (f.Length > 1) { Current = f[1]; Lines.Clear(); Note = ""; if (Mode != "done") Mode = "log"; }
                    break;
                case "line":
                    if (f.Length > 2)
                    {
                        double d = 0;
                        if (f.Length > 3) double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                        Lines.Add(new Line { Kind = f[1], Text = f[2], Seconds = d, At = now });
                        if (Lines.Count > 200) Lines.RemoveRange(0, 50);
                    }
                    break;
                case "item":
                    if (f.Length > 4)
                    {
                        Item it;
                        if (!ItemById.TryGetValue(f[2], out it))
                        {
                            it = new Item { Group = f[1], Id = f[2], Appeared = now, ChangedAt = now, StepName = Current };
                            ItemById[f[2]] = it;
                            Items.Add(it);
                            if (!GroupOrder.Contains(f[1])) GroupOrder.Add(f[1]);
                        }
                        it.Name = f[3];
                        if (it.State != f[4]) { it.ChangedAt = now; if (f[4] == "run") it.RunAt = now; }
                        it.State = f[4];
                        if (f.Length > 5 && f[5] == "admin") it.Admin = true;
                    }
                    break;
                case "note": Note = f.Length > 1 ? f[1] : ""; break;
                case "admin":
                    var what = new List<string>();
                    for (int i = 1; i < f.Length; i++) if (f[i].Length > 0) what.Add(f[i]);
                    AdminWhat = what.ToArray();
                    Mode = "admin";
                    break;
                case "adminclose": if (Mode == "admin") Mode = "log"; break;
                case "warn": if (f.Length > 1) Warnings.Add(f[1]); break;
                case "title": if (f.Length > 1) Title = f[1]; break;
                case "section": Section = f.Length > 1 ? f[1] : ""; break;
                case "next": if (f.Length > 2) Next.Add(new[] { f[1], f[2] }); break;
                case "final":
                {
                    FailedStep = f.Length > 1 ? f[1] : "";
                    Failed = FailedStep.Length > 0;
                    double d;
                    if (f.Length > 2 && double.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) FinalSeconds = d;
                    FinalLine = f.Length > 3 ? f[3] : "";
                    FinalError = f.Length > 4 ? f[4] : "";
                    Final = true; FinalAt = now; Mode = "done";
                    break;
                }
                case "pause": Paused = true; break;
                case "resume": Paused = false; Repaint = true; break;
                case "quit": Quit = true; break;
            }
        }
    }

    // --- colors and glyphs ------------------------------------------------------------------------
    const int Default = -1;
    static int Rgb(int r, int g, int b) { return (r << 16) | (g << 8) | b; }
    static readonly int Fg = Rgb(217, 217, 224), Dim = Rgb(128, 128, 140), Faint = Rgb(70, 70, 82), Acc = Rgb(255, 159, 10),
        AccDim = Rgb(150, 96, 18), Ok = Rgb(61, 214, 140), Warn = Rgb(242, 201, 76), Red = Rgb(255, 92, 80), White = Rgb(255, 255, 255),
        Border = Rgb(52, 52, 64), Hot = Rgb(120, 78, 20), Unlit = Rgb(34, 34, 44), Track = Rgb(44, 44, 56);
    static int Mix(int a, int b, double t)
    {
        if (t <= 0) return a; if (t >= 1) return b;
        int r = (int)(((a >> 16) & 255) * (1 - t) + ((b >> 16) & 255) * t);
        int g = (int)(((a >> 8) & 255) * (1 - t) + ((b >> 8) & 255) * t);
        int bl = (int)((a & 255) * (1 - t) + (b & 255) * t);
        return Rgb(r, g, bl);
    }
    static readonly int[] Shade = { Rgb(255, 196, 0), Rgb(255, 168, 10), Rgb(255, 140, 30), Rgb(255, 112, 50), Rgb(255, 84, 70), Rgb(255, 58, 92) };
    static string[] Spin;
    static string Done, Fail, Dot, Sub, Ell, H, V, Upper, Lower, Seg, TrackG;
    static char[] Corners;
    static string[] Eighths;
    static void Glyphs()
    {
        if (Rich)
        {
            Spin = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            Done = "✓"; Fail = "✗"; Sub = "▸"; Ell = "…"; Corners = new[] { '╭', '╮', '╰', '╯' };
            Seg = "━"; TrackG = "─";
            Eighths = new[] { "", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };
        }
        else
        {
            Spin = new[] { "|", "/", "-", "\\" };
            Done = "√"; Fail = "×"; Sub = "►"; Ell = "~"; Corners = new[] { '┌', '┐', '└', '┘' };
            Seg = "="; TrackG = "-";
            Eighths = new[] { "", "", "", "", "▌", "▌", "▌", "▌" };
        }
        Dot = "·"; H = "─"; V = "│"; Upper = "▀"; Lower = "▄";
    }

    // --- the frame: a grid of cells, written out only where it changed ------------------------------
    static int W, Hgt;
    static string[] Ch; static int[] CFg, CBg; static bool[] CBold;
    static string[] PCh; static int[] PFg, PBg; static bool[] PBold;
    static void Alloc()
    {
        int n = W * Hgt;
        Ch = new string[n]; CFg = new int[n]; CBg = new int[n]; CBold = new bool[n];
        PCh = new string[n]; PFg = new int[n]; PBg = new int[n]; PBold = new bool[n];
    }
    static void Clear()
    {
        for (int i = 0; i < Ch.Length; i++) { Ch[i] = " "; CFg[i] = Default; CBg[i] = Default; CBold[i] = false; }
    }
    static void Set(int x, int y, string c, int fg, int bg, bool bold)
    {
        if (x < 0 || y < 0 || x >= W || y >= Hgt) return;
        int i = y * W + x;
        Ch[i] = c; CFg[i] = fg; CBg[i] = bg; CBold[i] = bold;
    }
    // Text from (x, y), at most max columns; cut with an ellipsis. Returns the columns used.
    static int Put(int x, int y, string text, int fg, int max, bool bold)
    {
        if (text == null) return 0;
        int n = 0;
        for (int i = 0; i < text.Length && n < max; i++)
        {
            string c = text[i].ToString();
            if (n == max - 1 && i < text.Length - 1) { Set(x + n, y, Ell, fg, Default, bold); n++; break; }
            Set(x + n, y, c, fg, Default, bold);
            n++;
        }
        return n;
    }
    static int Put(int x, int y, string text, int fg, int max) { return Put(x, y, text, fg, max, false); }
    static Stream Out;
    static readonly StringBuilder Sb = new StringBuilder(1 << 16);
    static void Flush(bool full)
    {
        Sb.Length = 0;
        int lastFg = -2, lastBg = -2; bool lastBold = false;
        int cx = -1, cy = -1;
        for (int y = 0; y < Hgt; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (!full && Ch[i] == PCh[i] && CFg[i] == PFg[i] && CBg[i] == PBg[i] && CBold[i] == PBold[i]) continue;
                if (cy != y || cx != x) { Sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H'); }
                if (CBold[i] != lastBold) { Sb.Append(CBold[i] ? "\x1b[1m" : "\x1b[22m"); lastBold = CBold[i]; }
                if (CFg[i] != lastFg) { if (CFg[i] == Default) Sb.Append("\x1b[39m"); else Sb.Append("\x1b[38;2;").Append((CFg[i] >> 16) & 255).Append(';').Append((CFg[i] >> 8) & 255).Append(';').Append(CFg[i] & 255).Append('m'); lastFg = CFg[i]; }
                if (CBg[i] != lastBg) { if (CBg[i] == Default) Sb.Append("\x1b[49m"); else Sb.Append("\x1b[48;2;").Append((CBg[i] >> 16) & 255).Append(';').Append((CBg[i] >> 8) & 255).Append(';').Append(CBg[i] & 255).Append('m'); lastBg = CBg[i]; }
                Sb.Append(Ch[i]);
                cx = x + 1; cy = y;
                PCh[i] = Ch[i]; PFg[i] = CFg[i]; PBg[i] = CBg[i]; PBold[i] = CBold[i];
            }
        }
        if (Sb.Length == 0) return;
        Sb.Append("\x1b[0m\x1b[1;1H");
        Pending = Encoding.UTF8.GetBytes(Sb.ToString());
    }
    static byte[] Pending;   // a frame to write, once the lock is let go

    // --- the logo and its snake -----------------------------------------------------------------------
    static readonly string[] Rows = {
        "###########################","###########################","##...........##..........##","##...........##..........##",
        "##..###########....####..##","##..###########....####..##","##..##...............##..##","##..##...............##..##",
        "##..##...............##..##","##..##...............##..##","##..##...............##..##","##..##...............##..##",
        "######...............##..##","######...............##..##","##..##...............##..##","##..##...............##..##",
        "##..##...............##..##","##..##...............##..##","##..##...............##..##","##..##...............##..##",
        "##..###################..##","##..###################..##","##...........##..........##","##...........##..........##",
        "###############..##########","###############..##########" };
    const int LW = 27, LH = 26;
    static bool On(int x, int y) { return x >= 0 && y >= 0 && x < LW && y < LH && Rows[y][x] == '#'; }
    static readonly List<int> Walk = new List<int>();
    static readonly double[] LitAt = new double[LW * LH];   // seconds since start when the snake first passed
    static void BuildWalk()
    {
        // Depth first along the strokes from the bottom-left corner, straight on where it can, stepping
        // back the way it came at a dead end: every move is one square.
        var seen = new bool[LW * LH];
        int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
        var stack = new Stack<int[]>();
        seen[25 * LW] = true; Walk.Add(25 * LW);
        stack.Push(new[] { 0, 25, 3, 0 });
        while (stack.Count > 0)
        {
            var t = stack.Peek();
            if (t[3] < 4)
            {
                int d = t[2], k = new[] { d, (d + 1) % 4, (d + 3) % 4, (d + 2) % 4 }[t[3]];
                t[3]++;
                int nx = t[0] + dx[k], ny = t[1] + dy[k];
                if (On(nx, ny) && !seen[ny * LW + nx]) { seen[ny * LW + nx] = true; Walk.Add(ny * LW + nx); stack.Push(new[] { nx, ny, k, 0 }); }
            }
            else
            {
                stack.Pop();
                if (stack.Count > 0) { var b = stack.Peek(); Walk.Add(b[1] * LW + b[0]); }
            }
        }
        for (int i = 0; i < LitAt.Length; i++) LitAt[i] = -1;
    }
    // The snake doesn't follow the progress as it comes (a package finishing jumps it, a long one
    // stalls it): it follows a smoothed copy that never goes back, at a speed that changes gently,
    // so the logo builds at a steady pace. In a pause it keeps creeping into a little slack ahead of
    // the progress, then holds still with a glow rippling down its body.
    static double HeadF;      // where the snake's head is along its road
    static double Vel;        // its speed, squares a second
    static double SmoothP;    // the progress it follows
    static double ShownPct;   // the percentage shown, eased and never going back
    static double FinalHead0 = -1;   // where the head was when the install finished
    static void MoveSnake(double p, double dt)
    {
        SmoothP = Math.Max(SmoothP, SmoothP + (p - SmoothP) * Math.Min(1, dt / 2.5));
        double end = Walk.Count - 1;
        double ahead = Math.Min(end, SmoothP * end + end * 0.025);
        double want = HeadF < ahead ? Math.Max(1.5, Math.Min(26, (ahead - HeadF) * 0.6)) : 0;
        Vel += (want - Vel) * Math.Min(1, dt * 1.8);
        HeadF = Math.Min(ahead, HeadF + Math.Max(0, Vel) * dt);
    }
    static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2; }

    static void DrawLogo(int ox, int oy, double t, bool snakeOn, double wave)
    {
        int head = (int)HeadF;
        for (int i = 0; i <= head && i < Walk.Count; i++) if (LitAt[Walk[i]] < 0) LitAt[Walk[i]] = t;
        var snake = new Dictionary<int, double>();
        if (snakeOn)
        {
            bool idle = Vel < 0.6;
            for (int k = 0; k < 12 && head - k >= 0; k++)
            {
                int key = Walk[head - k];
                double s = k == 0 ? 1.0 : Math.Max(0.15, 1 - (k - (HeadF - head)) / 12.0);
                // Holding still: a glow runs down its body, so it never looks stuck.
                if (idle && k > 0) s *= 0.7 + 0.3 * (0.5 + 0.5 * Math.Sin(t * 5 - k * 0.7));
                if (!snake.ContainsKey(key)) snake[key] = s;
            }
        }
        for (int r = 0; r < 13; r++)
        {
            for (int x = 0; x < LW; x++)
            {
                int top = Color(x, 2 * r, t, snake, wave), bot = Color(x, 2 * r + 1, t, snake, wave);
                if (top == Default && bot == Default) continue;
                if (top != Default && bot != Default) Set(ox + x, oy + r, Upper, top, bot, false);
                else if (top != Default) Set(ox + x, oy + r, Upper, top, Default, false);
                else Set(ox + x, oy + r, Lower, bot, Default, false);
            }
        }
    }
    static int Color(int x, int y, double t, Dictionary<int, double> snake, double wave)
    {
        if (!On(x, y)) return Default;
        int key = y * LW + x;
        int shade = Shade[Math.Min(5, y * 6 / LH)];
        double s;
        if (snake.TryGetValue(key, out s)) return Mix(Rgb(120, 96, 30), Rgb(255, 246, 205), s);
        double lit = LitAt[key];
        int c = lit < 0 ? Unlit : Mix(Rgb(255, 236, 170), shade, Math.Min(1, (t - lit) / 0.9));  // a just-lit square glows, then settles
        if (wave > -50) { double d = Math.Abs(x + y - wave); if (d < 4) c = Mix(c, Rgb(255, 244, 210), (1 - d / 4) * 0.85); }
        return c;
    }

    // --- layout helpers -----------------------------------------------------------------------------------
    static void Box(int x0, int y0, int w, int h, int color, string title, int titleColor)
    {
        Set(x0, y0, Corners[0].ToString(), color, Default, false);
        Set(x0 + w - 1, y0, Corners[1].ToString(), color, Default, false);
        Set(x0, y0 + h - 1, Corners[2].ToString(), color, Default, false);
        Set(x0 + w - 1, y0 + h - 1, Corners[3].ToString(), color, Default, false);
        for (int x = 1; x < w - 1; x++) { Set(x0 + x, y0, H, color, Default, false); Set(x0 + x, y0 + h - 1, H, color, Default, false); }
        for (int y = 1; y < h - 1; y++) { Set(x0, y0 + y, V, color, Default, false); Set(x0 + w - 1, y0 + y, V, color, Default, false); }
        if (!string.IsNullOrEmpty(title)) { Set(x0 + 2, y0, " ", color, Default, false); int n = Put(x0 + 3, y0, title, titleColor, w - 8, true); Set(x0 + 3 + n, y0, " ", color, Default, false); }
    }
    static string Dur(double s)
    {
        if (s < 1) return "<1s";
        if (s < 60) return ((int)s) + "s";
        return ((int)(s / 60)) + "m " + ((int)(s % 60)).ToString("00") + "s";
    }
    static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>(); var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line); line = word; }
            else line = line.Length > 0 ? line + " " + word : word;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
    static bool Finished(string state) { return state == "done" || state == "have" || state == "fail"; }

    // --- one frame ------------------------------------------------------------------------------------
    static double LastT;
    static void Frame(double t)
    {
        double dt = Math.Max(0.001, Math.Min(0.1, t - LastT)); LastT = t;
        Clear();
        if (W < 79 || Hgt < 24)
        {
            string msg = "make this window bigger (80 x 24) to see the install";
            Put(Math.Max(0, (W - msg.Length) / 2), Hgt / 2, msg, Dim, W);
            Flush(false);
            return;
        }
        var now = Now;
        string spin = Spin[(int)(t * 12) % Spin.Length];
        double elapsed = (now - Started).TotalSeconds;

        // How far along: each step by its expected seconds; the running one by its time so far, or by its
        // items when it has some.
        double total = 0, got = 0;
        foreach (var s in Steps)
        {
            if (s.Name == "admin" || s.State == "skipped") continue;
            double w = s.Expected > 0 ? s.Expected : 5;
            total += w;
            if (s.State == "done" || s.State == "warned" || s.State == "failed") got += w;
            else if (s.State == "running")
            {
                double f = Math.Min(0.95, (now - s.StartAt).TotalSeconds / w);
                int all = 0, fin = 0;
                foreach (var it in Items) if (it.StepName == s.Name) { all++; if (Finished(it.State)) fin++; }
                if (all > 0) f = Math.Max(f, 0.92 * fin / all);
                got += w * f;
            }
        }
        double p = total > 0 ? got / total : 0;
        // Finished well: the snake hurries to close the logo (the first second), the panes fade (to
        // 1.4 s), then the finale (DrawFinale).
        bool finale = Final && !Failed;
        double ft = Final ? (now - FinalAt).TotalSeconds : 0;
        if (finale && ft >= 1.4) { DrawFinale(t, ft); Flush(false); return; }
        if (finale)
        {
            if (FinalHead0 < 0) FinalHead0 = HeadF;
            HeadF = FinalHead0 + (Walk.Count - 1 - FinalHead0) * Ease(ft / 1.0);
            ShownPct += (100 - ShownPct) * Math.Min(1, dt * 5);
        }
        else if (!Final)
        {
            MoveSnake(p, dt);
            ShownPct = Math.Max(ShownPct, ShownPct + (SmoothP * 100 - ShownPct) * Math.Min(1, dt * 4));
        }

        // Header and footer.
        int x0 = 2;
        Put(x0, 0, "omawin", White, 6, true);
        Put(x0 + 8, 0, Version + " " + Dot + " " + What, Dim, W - 30);
        string clock = Final ? "done in " + Dur(FinalSeconds) : string.Format("{0:00}:{1:00}", (int)(elapsed / 60), (int)(elapsed % 60));
        Put(W - clock.Length - 2, 0, clock, Dim, clock.Length);
        string foot = Final && !Failed ? "sign out and back in: the desktop starts at sign-in" : "log  " + LogPath;
        Put(x0, Hgt - 2, foot, Dim, W - 4);

        int top = 2, boxH = Hgt - 4;
        int rightW = Math.Max(33, (int)((W - 5) * 0.40));
        int leftW = W - 5 - rightW;
        int rx = x0 + leftW + 2;
        int inner = boxH - 2;

        // --- the right side: the logo, the percentage, the steps
        Box(rx, top, rightW, boxH, Border, null, Border);
        var stepRows = new List<Step>(Steps);
        int fixedRows = 13 + 1 + 1;
        bool gap = inner >= fixedRows + 2 + stepRows.Count;
        int room = inner - fixedRows - (gap ? 2 : 1);
        if (room < stepRows.Count && room > 0)
        {
            int at = 0; for (int i = 0; i < stepRows.Count; i++) if (stepRows[i].State == "running") at = i;
            int from = Math.Max(0, Math.Min(at - room / 2, stepRows.Count - room));
            stepRows = stepRows.GetRange(from, room);
        }
        if (room <= 0) stepRows.Clear();
        int blockH = 13 + 1 + (gap ? 2 : 1) + stepRows.Count;
        int ry = top + 1 + Math.Max(0, (inner - blockH) / 2);
        int lx = rx + (rightW - LW) / 2;
        DrawLogo(lx, ry, t, !Final || (finale && ft < 1.0), -99);
        LogoX = lx; LogoY = ry;
        ry += 13 + (gap ? 1 : 0);
        string pct = Final && Failed ? "stopped" : ((int)Math.Floor(ShownPct + 0.0001)) + "%";
        string eta = "";
        bool adminWaiting = false;
        foreach (var s in Steps) if (s.Name == "admin" && s.State == "running") adminWaiting = true;
        if (!Final)
        {
            if (adminWaiting) eta = "  " + Dot + " waiting for you";
            else if (p > 0.04) { double left = elapsed / p * (1 - p); eta = "  " + Dot + (left < 60 ? " under a minute left" : " about " + (int)Math.Round(left / 60) + "m left"); }
        }
        int pw = pct.Length + eta.Length;
        int px = rx + Math.Max(1, (rightW - pw) / 2);
        Put(px, ry, pct, White, pct.Length, true);
        Put(px + pct.Length, ry, eta, Dim, rightW - 4);
        ry += 2;
        int sx = rx + Math.Max(2, (rightW - 23) / 2);
        foreach (var s in stepRows)
        {
            double since = (now - s.ChangedAt).TotalSeconds;
            string name = s.Name.PadRight(12);
            switch (s.State)
            {
                case "running":
                {
                    string tm = s.Note.Length > 0 ? s.Note : Dur((now - s.StartAt).TotalSeconds);
                    Put(sx, ry, spin, Acc, 1); Put(sx + 2, ry, name, White, 12, true); Put(sx + 14, ry, tm.PadLeft(9), Acc, 9);
                    break;
                }
                case "background":
                    Put(sx, ry, spin, AccDim, 1); Put(sx + 2, ry, name, Dim, 12); Put(sx + 14, ry, s.Note.PadLeft(9), AccDim, 9);
                    break;
                case "done":
                case "warned":
                case "failed":
                {
                    int mark = s.State == "done" ? Ok : s.State == "warned" ? Warn : Red;
                    double fade = Math.Min(1, since / 0.6);
                    Put(sx, ry, s.State == "done" ? Done : s.State == "warned" ? "!" : Fail, Mix(Acc, mark, fade), 1);
                    Put(sx + 2, ry, name, Mix(White, s.State == "failed" ? Red : Dim, fade), 12);
                    Put(sx + 14, ry, Dur(s.Seconds).PadLeft(9), Mix(Acc, Dim, fade), 9);
                    break;
                }
                case "skipped":
                    Put(sx, ry, "-", Faint, 1); Put(sx + 2, ry, name, Faint, 12); Put(sx + 14, ry, (s.Note.Length > 0 ? s.Note : "skipped").PadLeft(9), Faint, 9);
                    break;
                default:
                    Put(sx, ry, Dot, Faint, 1); Put(sx + 2, ry, name, Faint, 12);
                    break;
            }
            ry++;
        }

        // --- the left side
        string legend; int legendColor;
        if (Final) { legend = Failed ? Fail + " stopped" : Done + " installed"; legendColor = Failed ? Red : Ok; }
        else if (Mode == "admin") { legend = "admin"; legendColor = Acc; }
        else { legend = Current; legendColor = Acc; }
        double pulse = 0.5 + 0.5 * Math.Sin(t * 2.2);
        Box(x0, top, leftW, boxH, Final ? Border : Mix(Border, Hot, 0.55 + 0.45 * pulse), legend, legendColor);
        int iw = leftW - 4, ix = x0 + 2, iy = top + 1;
        if (Final && Failed) DrawDone(ix, iy, iw, inner, t);
        else if (Mode == "admin") DrawAdmin(ix, iy, iw, inner, spin);
        else DrawWork(ix, iy, iw, inner, spin, t, now);

        // The panes fade away around the finished logo.
        if (finale && ft > 1.0)
        {
            double fade = Math.Min(1, (ft - 1.0) / 0.4);
            for (int y = 0; y < Hgt; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x >= LogoX && x < LogoX + LW && y >= LogoY && y < LogoY + 13) continue;
                    int i = y * W + x;
                    CFg[i] = Mix(CFg[i] == Default ? Fg : CFg[i], Ground, fade);
                    if (CBg[i] != Default) CBg[i] = Mix(CBg[i], Ground, fade);
                }
        }
        Flush(false);
    }

    // --- the finale ---------------------------------------------------------------------------------------
    static int LogoX, LogoY;
    static readonly int Ground = Rgb(14, 14, 18);   // near enough the terminal's own dark background to fade into
    // How many of each group, "Programs 25  ·  Tools 12 ...", each number counted up to by f (0..1).
    static string Counts(double f)
    {
        var sb = new StringBuilder();
        foreach (var g in GroupOrder)
        {
            int n = 0; foreach (var it in Items) if (it.Group == g && it.State != "wait" && it.State != "run") n++;
            if (sb.Length > 0) sb.Append("  " + Dot + "  ");
            sb.Append(g).Append(' ').Append((int)Math.Round(n * f));
        }
        return sb.ToString();
    }
    // The finale's text: one line per logo row (13), for the screen and for the summary it leaves.
    // Each: text, color, bold, when it appears (seconds into the finale), and how it appears.
    class Row { public string Text = ""; public int Color; public bool Bold; public double At; public bool Typed, Slide; public string Lead = ""; public int LeadColor; }
    static Row[] FinaleRows()
    {
        var rows = new Row[13];
        for (int i = 0; i < 13; i++) rows[i] = new Row();
        int wn = Warnings.Count;
        rows[1] = new Row { Text = Done + " " + Title + (wn > 0 ? ", with " + wn + " warning" + (wn != 1 ? "s" : "") : ""), Color = wn > 0 ? Warn : Ok, Bold = true, At = 1.9, Typed = true };
        rows[2] = new Row { Text = FinalLine, Color = Dim, At = 2.3 };
        rows[4] = new Row { Text = Counts(1), Color = Fg, At = 2.5 };
        if (Next.Count > 0 && Section.Length > 0) rows[6] = new Row { Text = Section, Color = White, Bold = true, At = 2.9 };
        for (int i = 0; i < Next.Count && 7 + i < 13; i++)
        {
            // A row with no key (omawin update's "What's new"): a dot and its text.
            if (Next[i][0].Length == 0) rows[7 + i] = new Row { Text = Dot + " " + Next[i][1], Color = Fg, At = 3.05 + 0.14 * i, Slide = true };
            else rows[7 + i] = new Row { Lead = (i + 1) + "  " + Next[i][0].PadRight(22), LeadColor = White, Text = Next[i][1], Color = Dim, At = 3.05 + 0.14 * i, Slide = true };
        }
        return rows;
    }
    static void DrawFinale(double t, double ft)
    {
        double mv = Ease((ft - 1.4) / 0.6);
        int lx = (int)Math.Round(LogoX + (2 - LogoX) * mv), ly = (int)Math.Round(LogoY + (1 - LogoY) * mv);
        HeadF = Walk.Count - 1;
        double wave = ft > 1.7 ? ((ft - 1.7) * 30) % 130 - 14 : -99;
        DrawLogo(lx, ly, t, false, wave);
        if (ft < 1.9) return;
        bool side = W >= 2 + LW + 4 + 40;
        int bx = side ? 2 + LW + 4 : 2, by = side ? 1 : 15, bw = W - bx - 1;
        var rows = FinaleRows();
        for (int r = 0; r < 13; r++)
        {
            var row = rows[r];
            if (row.Text.Length == 0 && row.Lead.Length == 0) continue;
            double a = ft - row.At;
            if (a < 0) continue;
            double e = Ease(a / 0.45);
            int y = by + r;
            if (row.Typed)
            {
                int n = Math.Min(row.Text.Length, (int)(a * 48));
                Put(bx, y, row.Text.Substring(0, n), row.Color, bw, row.Bold);
                if (n < row.Text.Length && ((int)(t * 6)) % 2 == 0) Set(bx + n, y, "█", Acc, Default, false);
                continue;
            }
            int off = row.Slide ? (int)Math.Round((1 - e) * 14) : 0;
            string text = r == 4 ? Counts(Ease(a / 0.8)) : row.Text;
            int x = bx + off;
            if (row.Lead.Length > 0)
            {
                Put(x, y, row.Lead.Substring(0, 1), Mix(Ground, Acc, e), 1);
                x += Put(x + 3, y, row.Lead.Substring(3), Mix(Ground, row.LeadColor, e), bw - 3, true) + 4;
            }
            Put(x, y, text, Mix(Ground, row.Color, e), bw - (x - bx), row.Bold);
        }
        // The warnings, under it all.
        int wy = Math.Max(by + 13, 15) + (side ? 1 : 0);
        double wa = ft - 3.6;
        if (wa > 0)
            foreach (var w in Warnings)
                foreach (var l in Wrap(w, W - 6))
                {
                    if (wy >= Hgt - 1) break;
                    Put(2, wy, l == Wrap(w, W - 6)[0] ? "!" : " ", Mix(Ground, Warn, Ease(wa / 0.5)), 1);
                    Put(4, wy++, l, Mix(Ground, Warn, Ease(wa / 0.5)), W - 6);
                }
    }
    static bool FinaleRunning() { return Final && !Failed && (Now - FinalAt).TotalSeconds < 4.6; }

    static void DrawAdmin(int ix, int iy, int iw, int inner, string spin)
    {
        int y = iy;
        Put(ix, y++, "Windows asks for admin once, now.", White, iw, true);
        Put(ix, y++, "For what every account on this PC shares:", Dim, iw);
        y++;
        foreach (var w in AdminWhat)
        {
            var parts = w.Split(new[] { '|' }, 2);
            Put(ix, y, Dot, Acc, 1);
            int n = Put(ix + 2, y, parts[0], Fg, iw - 2);
            if (parts.Length > 1) Put(ix + 4 + n, y, parts[1], Dim, iw - 4 - n);
            y++;
        }
        y++;
        foreach (var l in Wrap("Nothing asks again after this. Say no, and omawin installs what it can for this account alone.", iw)) Put(ix, y++, l, Dim, iw);
        y++;
        Put(ix, y, spin, Acc, 1); Put(ix + 2, y, "waiting for the Windows prompt", Fg, iw - 2);
    }

    // Everything the install has touched, by group; then what's running now and the step's last lines.
    static void DrawWork(int ix, int iy, int iw, int inner, string spin, double t, DateTime now)
    {
        // The bottom block: running items with a sliding bar, then the step's last lines.
        var runningItems = new List<Item>();
        foreach (var it in Items) if (it.State == "run") runningItems.Add(it);
        int nowRows = Math.Min(5, runningItems.Count);
        int logRows = Math.Max(3, inner / 4);
        int noteRows = Note.Length > 0 ? 1 : 0;
        int bottom = nowRows + (nowRows > 0 ? 1 : 0) + logRows + noteRows;
        int roomTop = inner - bottom - 1;

        // Groups: open (a grid of its items) while it has work, or just changed; else one line.
        int cols = iw >= 66 ? 3 : iw >= 34 ? 2 : 1;
        int cw = iw / cols;
        var openG = new List<string>();
        foreach (var g in GroupOrder)
        {
            bool busy = false; DateTime last = DateTime.MinValue;
            foreach (var it in Items) if (it.Group == g) { if (!Finished(it.State)) busy = true; if (it.ChangedAt > last) last = it.ChangedAt; }
            if (busy || (now - last).TotalSeconds < 2.5) openG.Add(g);
        }
        Func<string, int> heightOf = g => { int n = 0; foreach (var it in Items) if (it.Group == g) n++; return 1 + (openG.Contains(g) ? (n + cols - 1) / cols : 0); };
        int need = 0; foreach (var g in GroupOrder) need += heightOf(g);
        // Too tall: close the open groups with the least going on, oldest first.
        while (need > roomTop && openG.Count > 0)
        {
            string drop = openG[0];
            foreach (var g in openG) { bool busy = false; foreach (var it in Items) if (it.Group == g && it.State == "run") busy = true; if (!busy) { drop = g; break; } }
            openG.Remove(drop);
            need = 0; foreach (var g in GroupOrder) need += heightOf(g);
        }
        int y = iy;
        foreach (var g in GroupOrder)
        {
            if (y >= iy + roomTop) break;
            var list = new List<Item>(); int fin = 0, fresh = 0, failed = 0;
            foreach (var it in Items) if (it.Group == g) { list.Add(it); if (Finished(it.State)) fin++; if (it.State == "done") fresh++; if (it.State == "fail") failed++; }
            bool open = openG.Contains(g);
            // The header: name, a small bar, the counts.
            bool complete = fin == list.Count;
            Put(ix, y, complete ? (failed > 0 ? "!" : Done) : spin, complete ? (failed > 0 ? Warn : Ok) : Acc, 1);
            Put(ix + 2, y, g, White, 12, true);
            int barW = Math.Min(14, Math.Max(6, iw - 40));
            DrawBar(ix + 15, y, barW, list.Count > 0 ? (double)fin / list.Count : 1, complete ? Ok : Acc, t);
            string counts = fin + "/" + list.Count + (fresh > 0 ? "  " + fresh + " new" : "") + (failed > 0 ? "  " + failed + " failed" : "");
            int cx = ix + 16 + barW;
            int n = Put(cx, y, counts, complete ? Dim : Fg, iw - (cx - ix));
            if (!open)
            {
                // Closed: its names, as many as fit.
                var names = new StringBuilder();
                foreach (var it in list) { if (names.Length > 0) names.Append(", "); names.Append(it.Name); }
                Put(cx + n + 2, y, names.ToString(), Faint, iw - (cx - ix) - n - 2);
                y++;
                continue;
            }
            y++;
            int rows = (list.Count + cols - 1) / cols;
            for (int r = 0; r < rows && y < iy + roomTop; r++, y++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = c * rows + r;
                    if (i >= list.Count) break;
                    DrawItem(ix + 2 + c * cw, y, cw - 3, list[i], spin, now);
                }
            }
        }

        // The bottom block.
        int by = iy + inner - bottom;
        if (nowRows > 0)
        {
            int track = Math.Max(8, Math.Min(24, iw - 36));
            for (int i = 0; i < nowRows; i++, by++)
            {
                var it = runningItems[i];
                Put(ix, by, spin, Acc, 1);
                Put(ix + 2, by, it.Name, Fg, 16);
                // A light that slides back and forth along a track, a fraction of a column at a time.
                double ph = (t * 0.9 + i * 0.37) % 2; double pos = ph < 1 ? ph : 2 - ph;
                double center = pos * (track - 1);
                for (int k = 0; k < track; k++)
                {
                    double d = Math.Abs(k - center);
                    int col = d < 3 ? Mix(Acc, Track, d / 3) : Track;
                    Set(ix + 19 + k, by, d < 2.2 ? Seg : TrackG, col, Default, false);
                }
                string tag = (it.Admin ? "admin " + Dot + " " : "") + Dur((now - it.RunAt).TotalSeconds);
                Put(ix + 20 + track, by, tag, Dim, iw - 20 - track);
            }
            by++;
        }
        var shown = new List<Line>();
        for (int i = Lines.Count - 1; i >= 0 && shown.Count < logRows; i--) shown.Insert(0, Lines[i]);
        for (int i = 0; i < shown.Count; i++, by++)
        {
            var l = shown[i];
            bool last = i == shown.Count - 1;
            double age = (now - l.At).TotalSeconds;
            int appear = Mix(White, Fg, Math.Min(1, age / 0.5));
            switch (l.Kind)
            {
                case "head": Put(ix, by, last ? spin : Sub, last ? Acc : AccDim, 1); Put(ix + 2, by, l.Text, last ? White : Fg, iw - 2, last); break;
                case "ok": Put(ix + 2, by, Done, Ok, 1); Put(ix + 4, by, l.Text, Mix(Fg, Dim, Math.Min(1, age / 0.8)), iw - 4); break;
                case "warn": Put(ix + 2, by, "!", Warn, 1); Put(ix + 4, by, l.Text, Warn, iw - 4); break;
                case "fail": Put(ix + 2, by, Fail, Red, 1); Put(ix + 4, by, l.Text, Red, iw - 4); break;
                case "done": { string d = Dur(l.Seconds); Put(ix + 2, by, Done, Ok, 1); int n = Put(ix + 4, by, l.Text, appear, iw - 6 - d.Length); Put(ix + 6 + n, by, d, Dim, d.Length); break; }
                default: Put(ix + 4, by, l.Text, Mix(appear, Dim, Math.Min(1, age / 0.8)), iw - 4); break;
            }
        }
        if (noteRows > 0) Put(ix, iy + inner - 1, Note, Dim, iw);
    }

    static void DrawItem(int x, int y, int w, Item it, string spin, DateTime now)
    {
        double since = (now - it.ChangedAt).TotalSeconds;
        double fade = Math.Min(1, since / 0.7);
        double born = Math.Min(1, (now - it.Appeared).TotalSeconds / 0.4);
        switch (it.State)
        {
            case "run": Put(x, y, spin, Acc, 1); Put(x + 2, y, it.Name, Acc, w - 2); break;
            case "done": Put(x, y, Done, Mix(Acc, Ok, fade), 1); Put(x + 2, y, it.Name, Mix(White, Ok, fade), w - 2, fade < 0.5); break;
            case "have": Put(x, y, Done, Mix(Faint, Dim, born), 1); Put(x + 2, y, it.Name, Mix(Faint, Dim, born), w - 2); break;
            case "fail": Put(x, y, Fail, Red, 1); Put(x + 2, y, it.Name, Red, w - 2); break;
            default: Put(x, y, Dot, Faint, 1); Put(x + 2, y, it.Name, Faint, w - 2); break;
        }
    }

    // A bar filled to f, an eighth of a column at a time.
    static void DrawBar(int x, int y, int w, double f, int color, double t)
    {
        double cells = Math.Max(0, Math.Min(1, f)) * w;
        int full = (int)cells;
        for (int i = 0; i < w; i++)
        {
            if (i < full) Set(x + i, y, "█", color, Default, false);
            else if (i == full && Eighths[(int)((cells - full) * 8)].Length > 0) Set(x + i, y, Eighths[(int)((cells - full) * 8)], color, Track, false);
            else Set(x + i, y, " ", Default, Track, false);
        }
    }

    static void DrawDone(int ix, int iy, int iw, int inner, double t)
    {
        int y = iy;
        int wn = Warnings.Count;
        if (Failed) Put(ix, y++, Fail + " install stopped at " + FailedStep, Red, iw, true);
        else if (wn > 0) Put(ix, y++, Done + " omawin is installed, with " + wn + " warning" + (wn != 1 ? "s" : ""), Warn, iw, true);
        else Put(ix, y++, Done + " " + Title, Ok, iw, true);
        Put(ix, y++, FinalLine, Dim, iw);
        y++;
        if (FinalError.Length > 0) { foreach (var l in Wrap(FinalError, iw - 2)) Put(ix + 2, y++, l, Red, iw - 2); y++; }
        // Everything, by group: how many, how many new.
        foreach (var g in GroupOrder)
        {
            int all = 0, fresh = 0, failed = 0;
            foreach (var it in Items) if (it.Group == g) { all++; if (it.State == "done") fresh++; if (it.State == "fail") failed++; }
            Put(ix, y, failed > 0 ? "!" : Done, failed > 0 ? Warn : Ok, 1);
            Put(ix + 2, y, g, White, 12, true);
            string c = all + " in" + (fresh > 0 ? ", " + fresh + " new" : "") + (failed > 0 ? ", " + failed + " failed" : "");
            Put(ix + 15, y, c, Dim, iw - 15);
            y++;
        }
        if (GroupOrder.Count > 0) y++;
        int budget = iy + inner - y - 1 - Next.Count;
        foreach (var w in Warnings)
        {
            var wl = Wrap(w, iw - 2);
            if (budget < wl.Count) break;
            for (int i = 0; i < wl.Count; i++) { if (i == 0) Put(ix, y, "!", Warn, 1); Put(ix + 2, y++, wl[i], Warn, iw - 2); }
            budget -= wl.Count;
        }
        if (wn > 0) y++;
        if (y < iy + inner) Put(ix, y++, "Next", White, 4, true);
        int k = 0;
        foreach (var nx in Next)
        {
            if (y >= iy + inner) break;
            k++;
            Put(ix, y, k.ToString(), Acc, 1);
            Put(ix + 3, y, nx[0], White, 21, true);
            Put(ix + 25, y++, nx[1], Dim, iw - 25);
        }
    }

    // After the last frame: the screen gives way to a plain summary that stays readable in the
    // scrollback: the result, everything by group (new, failed, already there), warnings, what's next.
    static string Sgr(int c) { return "\x1b[38;2;" + ((c >> 16) & 255) + ";" + ((c >> 8) & 255) + ";" + (c & 255) + "m"; }
    static void Wrapped(StringBuilder sb, string label, int labelColor, string text, int color, int width)
    {
        const int indent = 14;
        sb.Append("  ").Append(Sgr(labelColor)).Append(label.PadRight(indent - 2)).Append("\x1b[0m");
        int col = indent;
        foreach (var word in text.Split(' '))
        {
            if (col > indent && col + 1 + word.Length > width) { sb.Append("\r\n").Append(' ', indent); col = indent; }
            else if (col > indent) { sb.Append(' '); col++; }
            sb.Append(Sgr(color)).Append(word).Append("\x1b[0m");
            col += word.Length;
        }
        sb.Append("\r\n");
    }
    // One row of the logo in its finished colors, as text with color codes.
    static string LogoRow(int r)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < LW; x++)
        {
            int up = On(x, 2 * r) ? Shade[Math.Min(5, 2 * r * 6 / LH)] : Default;
            int dn = On(x, 2 * r + 1) ? Shade[Math.Min(5, (2 * r + 1) * 6 / LH)] : Default;
            if (up != Default && dn != Default) sb.Append(Sgr(up)).Append("\x1b[48;2;").Append((dn >> 16) & 255).Append(';').Append((dn >> 8) & 255).Append(';').Append(dn & 255).Append('m').Append(Upper);
            else if (up != Default) sb.Append("\x1b[49m").Append(Sgr(up)).Append(Upper);
            else if (dn != Default) sb.Append("\x1b[49m").Append(Sgr(dn)).Append(Lower);
            else sb.Append("\x1b[0m ");
        }
        return sb.Append("\x1b[0m").ToString();
    }
    static void AppendRow(StringBuilder sb, Row row, int width)
    {
        string text = row.Text;
        if (row.Lead.Length > 0)
        {
            sb.Append(Sgr(Acc)).Append(row.Lead.Substring(0, 1)).Append("  ").Append("\x1b[1m").Append(Sgr(row.LeadColor)).Append(row.Lead.Substring(3)).Append("\x1b[22m  ");
            width -= row.Lead.Length + 2;
        }
        if (text.Length > width) text = text.Substring(0, Math.Max(0, width - 1)) + Ell;
        if (row.Bold) sb.Append("\x1b[1m");
        sb.Append(Sgr(row.Color)).Append(text).Append("\x1b[0m");
    }
    static void PrintSummary()
    {
        int width = Math.Max(40, W - 2);
        var sb = new StringBuilder();
        sb.Append("\x1b[0m\x1b[2J\x1b[H\r\n");
        int wn = Warnings.Count;
        bool finale = Final && !Failed;
        if (finale)
        {
            // The finale's last frame, as lines: the logo with its text beside it (or under it).
            bool side = W >= 2 + LW + 4 + 40;
            var rows = FinaleRows();
            for (int r = 0; r < 13; r++)
            {
                sb.Append("  ").Append(LogoRow(r));
                if (side) { sb.Append("    "); AppendRow(sb, rows[r], W - LW - 7); }
                sb.Append("\x1b[0m\r\n");
            }
            if (!side) for (int r = 0; r < 13; r++) { if (rows[r].Text.Length + rows[r].Lead.Length == 0) continue; sb.Append("  "); AppendRow(sb, rows[r], W - 4); sb.Append("\x1b[0m\r\n"); }
            sb.Append("\r\n");
        }
        else sb.Append("  ").Append("\x1b[1m").Append(Sgr(White)).Append("omawin").Append("\x1b[22m").Append(Sgr(Dim)).Append("  ").Append(Version).Append(' ').Append(Dot).Append(' ').Append(What).Append("\x1b[0m\r\n\r\n");
        if (finale) { }
        else if (!Final) sb.Append("  ").Append(Sgr(Warn)).Append("! the install stopped before it finished").Append("\x1b[0m\r\n");
        else if (Failed) sb.Append("  ").Append(Sgr(Red)).Append("\x1b[1m").Append(Fail).Append(" install stopped at ").Append(FailedStep).Append("\x1b[0m\r\n");
        if (!finale && FinalLine.Length > 0) sb.Append("  ").Append(Sgr(Dim)).Append(FinalLine).Append("\x1b[0m\r\n");
        if (FinalError.Length > 0) sb.Append("\r\n  ").Append(Sgr(Red)).Append(FinalError).Append("\x1b[0m\r\n");
        if (!finale) sb.Append("\r\n");
        foreach (var g in GroupOrder)
        {
            var fresh = new List<string>(); var have = new List<string>(); var failed = new List<string>(); var open = new List<string>();
            foreach (var it in Items) if (it.Group == g) { if (it.State == "done") fresh.Add(it.Name); else if (it.State == "fail") failed.Add(it.Name); else if (it.State == "have") have.Add(it.Name); else open.Add(it.Name); }
            string head = g;
            // Changes (omawin update's): their titles have commas of their own, so one a line.
            if (g == "Changes") { for (int i = 0; i < fresh.Count; i++) Wrapped(sb, i == 0 ? head : "", White, Dot + " " + fresh[i], Fg, width); continue; }
            if (fresh.Count > 0) Wrapped(sb, head, White, Done + " " + string.Join(", ", fresh.ToArray()), Ok, width);
            if (failed.Count > 0) Wrapped(sb, fresh.Count > 0 ? "" : head, White, Fail + " " + string.Join(", ", failed.ToArray()), Red, width);
            if (have.Count > 0) Wrapped(sb, fresh.Count + failed.Count > 0 ? "" : head, White, "already there: " + string.Join(", ", have.ToArray()), Dim, width);
            if (open.Count > 0) Wrapped(sb, fresh.Count + failed.Count + have.Count > 0 ? "" : head, White, "not finished: " + string.Join(", ", open.ToArray()), Warn, width);
        }
        if (wn > 0)
        {
            sb.Append("\r\n");
            foreach (var w in Warnings) Wrapped(sb, "!", Warn, w, Warn, width);
        }
        if (Next.Count > 0 && !finale)
        {
            sb.Append("\r\n  \x1b[1m").Append(Sgr(White)).Append("Next").Append("\x1b[0m\r\n");
            int k = 0;
            foreach (var nx in Next)
            {
                k++;
                sb.Append("    ").Append(Sgr(Acc)).Append(k).Append("  ").Append("\x1b[1m").Append(Sgr(White)).Append(nx[0].PadRight(22)).Append("\x1b[0m").Append(Sgr(Dim)).Append(nx[1].Length > width - 31 ? nx[1].Substring(0, Math.Max(0, width - 32)) + Ell : nx[1]).Append("\x1b[0m\r\n");
            }
        }
        sb.Append("\r\n  ").Append(Sgr(Dim)).Append("log  ").Append(LogPath).Append("\x1b[0m\r\n\x1b[?25h");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        Out.Write(bytes, 0, bytes.Length);
        Out.Flush();
    }

    static bool Size()
    {
        int w, h;
        string fixedSize = Environment.GetEnvironmentVariable("OMAWIN_SCREEN");
        if (fixedSize != null && fixedSize.Contains("x"))
        {
            var p = fixedSize.Split('x');
            w = int.Parse(p[0]); h = int.Parse(p[1]);
        }
        else
        {
            try { w = Console.WindowWidth; h = Console.WindowHeight; } catch { w = 120; h = 36; }
        }
        w = Math.Max(20, w - 1);   // the last column left empty: a write there can wrap the line
        h = Math.Max(10, h);
        if (w == W && h == Hgt) return false;
        W = w; Hgt = h;
        Alloc();
        return true;
    }

    static int Main()
    {
        try
        {
            IntPtr hOut = GetStdHandle(-11); uint mode;
            if (GetConsoleMode(hOut, out mode)) SetConsoleMode(hOut, mode | 4);
            timeBeginPeriod(1);
        }
        catch { }
        Out = Console.OpenStandardOutput();
        // Ctrl+C reaches every program on the console: this one leaves it to the install, which
        // closes it (stdin ends) so it prints its summary instead of dying mid-frame.
        Console.CancelKeyPress += (sender, e) => { e.Cancel = true; };
        Glyphs();
        BuildWalk();
        var input = new Thread(() =>
        {
            try
            {
                var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                string l;
                while ((l = reader.ReadLine()) != null) { Handle(l); if (Quit) break; }
            }
            catch { }
            InputEnded = true;
        });
        input.IsBackground = true;
        input.Start();

        var clock = Stopwatch.StartNew();
        bool rich = Rich;
        Size();
        var hello = Encoding.UTF8.GetBytes("\x1b[?25l\x1b[2J\x1b[H");
        Out.Write(hello, 0, hello.Length);
        bool wasPaused = false;
        while (true)
        {
            double t = clock.Elapsed.TotalSeconds;
            if (Rich != rich) { rich = Rich; Glyphs(); }
            bool done = Quit || InputEnded;
            if (Paused && !done) { wasPaused = true; Thread.Sleep(20); continue; }
            bool full = Size() || Repaint || wasPaused;
            if (full) { var clr = Encoding.UTF8.GetBytes("\x1b[2J"); Out.Write(clr, 0, clr.Length); for (int i = 0; i < PCh.Length; i++) PCh[i] = null; Repaint = false; wasPaused = false; }
            try
            {
                lock (Gate) Frame(t);
                var frame = Pending; Pending = null;
                if (frame != null) { Out.Write(frame, 0, frame.Length); Out.Flush(); }
            }
            catch (Exception e) { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "omawin-screen.err"), e + "\r\n"); } catch { } }
            // Done, unless the finale is still playing (the install waits for it).
            bool playing; lock (Gate) playing = FinaleRunning();
            if (done && !playing) break;
            double spent = clock.Elapsed.TotalSeconds - t;
            int wait = (int)Math.Max(1, 16 - spent * 1000);
            Thread.Sleep(wait);
        }
        lock (Gate) PrintSummary();
        return 0;
    }
}
