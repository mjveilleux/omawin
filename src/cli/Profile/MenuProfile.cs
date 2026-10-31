#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Omawin.Cli.Profile
{
    // `omawin profile menu`'s numbers: trace.log's marks (one "<who> <what>\t<ticks>" line each,
    // QueryPerformanceCounter ticks, the clock every process shares) turned into runs and tables.
    // bin/profile-menu.ps1 drives the menu and hands the log here (it compiles this file with
    // Add-Type until the C# omawin ships, roadmap 2.11); cli.Tests checks it on any machine.
    //
    // A run starts at the driver's "driver key" mark (it's about to ask GlazeWM to run the key's
    // command) and lasts until the next one. With -Hand there's no key to mark: "driver ready"
    // starts the run, and its zero is the first mark another process writes after it (omawin.exe
    // created, or the menu app getting GlazeWM's emit). The last "driver surface <name>" or
    // "driver type <text>" before a run says what it was.
    // Written for Add-Type too: plain classes, no newer syntax than PowerShell 7's compiler takes.
    public sealed class TraceMark
    {
        public TraceMark(string label, long at) { Label = label; At = at; }
        public string Label { get; }
        public long At { get; }
    }

    public sealed class TraceRun
    {
        public TraceRun(string group, Dictionary<string, double> ms) { Group = group; Ms = ms; }
        // The surface ("launcher") or the text typed so far ("visual s"); "" when unsaid.
        public string Group { get; }
        // Each wanted label's first mark in the run, in milliseconds since the run's zero.
        public Dictionary<string, double> Ms { get; }
        public double? At(string label) { double v; return Ms.TryGetValue(label, out v) ? v : (double?)null; }
    }

    // MenuProfile.Numbers' answer; properties, so PowerShell's ConvertTo-Json writes them all.
    public sealed class MenuNumbers
    {
        // Runs measured, and how many never reached their frame (3 s without it).
        public int Runs { get; set; }
        public int Missed { get; set; }
        // Surfaces: each one's median key to first frame, by its name ("launcher"); null when no
        // run of it got there.
        public Dictionary<string, double?> KeyToFrameMs { get; } = new Dictionary<string, double?>();
        // Typing: every key's median key to list on screen, and the prefix slowest by its median.
        public double? TypingMs { get; set; }
        public string? SlowestPrefix { get; set; }
        public double? SlowestPrefixMs { get; set; }
    }

    public static class MenuProfile
    {
        // The steps from a menu key to the menu on screen, in order, and what the table calls them.
        // "menu emit omawin" is the menu app getting the key straight from GlazeWM (roadmap 1.2),
        // in place of omawin.exe's request file and signal.
        public static readonly string[][] SurfaceSteps =
        {
            new[] { "omawin process-created", "omawin.exe created" },
            new[] { "omawin main", "omawin.exe running" },
            new[] { "omawin send", "omawin: writing the request" },
            new[] { "omawin signalled", "omawin: signalled" },
            new[] { "menu request", "menu app woke (signal)" },
            new[] { "menu emit omawin", "menu app got the emit" },
            new[] { "menu handle", "request handled" },
            new[] { "menu shown", "window shown" },
            new[] { "menu painted", "first frame" },
        };

        // The steps from a key typed in the launcher to its list on screen.
        public static readonly string[][] TypingSteps =
        {
            new[] { "menu request", "menu app woke" },
            new[] { "menu typed", "text changed" },
            new[] { "menu rendered", "list rebuilt" },
            new[] { "menu list-painted", "list on screen" },
        };

        // What the launcher's search must stay under per key (roadmap 1.7): one frame at 60 Hz.
        public const double TypingBudgetMs = 16;

        public static readonly Dictionary<string, string> SurfaceNames = new Dictionary<string, string>
        {
            { "launcher", "Launcher (Win+Space)" },
            { "menu", "System menu (Win+Alt+Space)" },
            { "files", "File finder (Win+F)" },
        };

        // trace.log's lines, oldest first by their stamp (processes append in nearly that order;
        // the stamp decides). Lines that aren't "<label>\t<ticks>" are skipped.
        public static List<TraceMark> Parse(IEnumerable<string> lines)
        {
            var marks = new List<TraceMark>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r', '\n');
                int tab = line.LastIndexOf('\t');
                long at;
                if (tab <= 0 || !long.TryParse(line.Substring(tab + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out at)) continue;
                marks.Add(new TraceMark(line.Substring(0, tab).Trim(), at));
            }
            // Stable: two marks with one stamp keep the order they were written in.
            return marks.Select((m, i) => new { m, i }).OrderBy(x => x.m.At).ThenBy(x => x.i).Select(x => x.m).ToList();
        }

        // A mark is a step's when it's that label, or that label and more words: "menu handle
        // --launcher" is "menu handle", "omawin main launcher" is "omawin main".
        public static bool Is(string label, string step) =>
            label == step || (label.StartsWith(step, StringComparison.Ordinal) && label.Length > step.Length && label[step.Length] == ' ');

        public static List<TraceRun> Runs(IReadOnlyList<TraceMark> marks, long frequency, IEnumerable<string> steps)
        {
            var wanted = steps.ToList();
            var runs = new List<TraceRun>();
            var starts = new List<int>();
            for (int i = 0; i < marks.Count; i++)
                if (marks[i].Label == "driver key" || marks[i].Label == "driver ready") starts.Add(i);
            for (int s = 0; s < starts.Count; s++)
            {
                int from = starts[s], to = s + 1 < starts.Count ? starts[s + 1] : marks.Count;
                long? zero = null;
                if (marks[from].Label == "driver key") zero = marks[from].At;
                else
                    for (int i = from + 1; i < to; i++)
                        if (!marks[i].Label.StartsWith("driver ", StringComparison.Ordinal)) { zero = marks[i].At; break; }
                string group = "";
                for (int i = from - 1; i >= 0; i--)
                {
                    var l = marks[i].Label;
                    if (l.StartsWith("driver surface ", StringComparison.Ordinal)) { group = l.Substring(15); break; }
                    if (l.StartsWith("driver type ", StringComparison.Ordinal)) { group = l.Substring(12); break; }
                }
                var ms = new Dictionary<string, double>();
                if (zero != null)
                    for (int i = from + 1; i < to; i++)
                        foreach (var step in wanted)
                            if (!ms.ContainsKey(step) && Is(marks[i].Label, step) && marks[i].At >= zero.Value)
                                ms[step] = (marks[i].At - zero.Value) * 1000.0 / frequency;
                runs.Add(new TraceRun(group, ms));
            }
            return runs;
        }

        // The middle value, the lower of the two middle ones for an even count (as
        // profile-terminal's Median); null when there's none.
        public static double? Median(IEnumerable<double?> values)
        {
            var v = values.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToList();
            return v.Count == 0 ? (double?)null : v[(v.Count - 1) / 2];
        }

        // The whole report: per surface a timeline (a row per step, a column per run, then the
        // median) and a summary; or, typing, a row per prefix typed.
        public static string Report(IEnumerable<string> lines, long frequency, bool typing)
        {
            var marks = Parse(lines);
            var steps = typing ? TypingSteps : SurfaceSteps;
            var runs = Runs(marks, frequency, steps.Select(s => s[0]));
            var text = new StringBuilder();
            if (runs.Count == 0) return "No runs in the trace (no \"driver key\" marks): nothing was measured.\n";
            if (typing) Typing(text, runs);
            else Surfaces(text, runs);
            return text.ToString();
        }

        // The report's headline numbers, for a machine to read (`omawin profile menu -Json`; CI's
        // profile workflow fails on them): per surface the median key to first frame, or, typing,
        // the median key to list on screen over every key and the slowest prefix's median.
        public static MenuNumbers Numbers(IEnumerable<string> lines, long frequency, bool typing)
        {
            var steps = typing ? TypingSteps : SurfaceSteps;
            var runs = Runs(Parse(lines), frequency, steps.Select(s => s[0]));
            var numbers = new MenuNumbers { Runs = runs.Count };
            string end = typing ? "menu list-painted" : "menu painted";
            numbers.Missed = runs.Count(r => r.At(end) == null);
            if (typing)
            {
                numbers.TypingMs = Median(runs.Select(r => r.At(end)));
                foreach (var p in runs.Select(r => r.Group).Distinct())
                {
                    var median = Median(runs.Where(r => r.Group == p).Select(r => r.At(end)));
                    if (median != null && (numbers.SlowestPrefixMs == null || median.Value > numbers.SlowestPrefixMs.Value))
                    {
                        numbers.SlowestPrefix = p;
                        numbers.SlowestPrefixMs = median;
                    }
                }
            }
            else
                foreach (var g in runs.Select(r => r.Group).Distinct())
                    numbers.KeyToFrameMs[g] = Median(runs.Where(r => r.Group == g).Select(r => r.At(end)));
            return numbers;
        }

        static void Surfaces(StringBuilder text, List<TraceRun> runs)
        {
            var groups = runs.Select(r => r.Group).Distinct().ToList();
            foreach (var g in groups)
            {
                var these = runs.Where(r => r.Group == g).ToList();
                var name = SurfaceName(g);
                text.Append('\n').Append(name).Append(": ms since the key\n\n");
                var head = new StringBuilder(Pad(""));
                for (int i = 1; i <= these.Count; i++) head.Append(Cell("run " + i.ToString(CultureInfo.InvariantCulture)));
                text.Append(head).Append(Cell("median", 8)).Append('\n');
                foreach (var step in SurfaceSteps)
                {
                    var values = these.Select(r => r.At(step[0])).ToList();
                    if (values.All(v => v == null)) continue; // a path this setup doesn't take (emit, or the request file)
                    var row = new StringBuilder(Pad(step[1]));
                    foreach (var v in values) row.Append(Cell(v));
                    text.Append(row).Append(Cell(Median(values), 8)).Append('\n');
                }
                int missing = these.Count(r => r.At("menu painted") == null);
                if (missing > 0) text.Append(missing).Append(missing == 1 ? " run" : " runs").Append(" never reached the first frame (the menu app didn't show it within 3 s).\n");
            }
            text.Append("\nKey to first frame (median, ms)\n\n");
            foreach (var g in groups)
            {
                var name = SurfaceName(g);
                var these = runs.Where(r => r.Group == g).ToList();
                text.Append(Pad(name)).Append(Cell(Median(these.Select(r => r.At("menu painted"))))).Append('\n');
            }
        }

        static void Typing(StringBuilder text, List<TraceRun> runs)
        {
            text.Append("\nTyping in the launcher: ms from each key to its list on screen (median over ")
                .Append(runs.Select(r => r.Group).GroupBy(g => g).Max(g => g.Count())).Append(" runs)\n\n");
            var head = new StringBuilder(Pad("typed"));
            foreach (var step in TypingSteps) head.Append(Cell(step[1], 16));
            text.Append(head).Append(Cell("worst", 8)).Append('\n');
            var prefixes = runs.Select(r => r.Group).Distinct().ToList();
            string? slowest = null;
            double slowestMs = 0;
            foreach (var p in prefixes)
            {
                var these = runs.Where(r => r.Group == p).ToList();
                var row = new StringBuilder(Pad("\"" + p + "\""));
                foreach (var step in TypingSteps) row.Append(Cell(Median(these.Select(r => r.At(step[0]))), 16));
                var painted = these.Select(r => r.At("menu list-painted")).ToList();
                var worst = painted.Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty().Max();
                text.Append(row).Append(Cell(painted.Any(v => v != null) ? worst : (double?)null, 8)).Append('\n');
                var median = Median(painted);
                if (median != null && (slowest == null || median.Value > slowestMs)) { slowest = p; slowestMs = median.Value; }
            }
            var all = runs.Select(r => r.At("menu list-painted")).ToList();
            var overall = Median(all);
            text.Append('\n').Append(Pad("every key, median")).Append(Cell(overall, 16)).Append('\n');
            if (slowest != null)
                text.Append(slowestMs < TypingBudgetMs
                    ? string.Format(CultureInfo.InvariantCulture, "Every prefix under {0:0} ms (the slowest, \"{1}\", {2:0.0} ms).\n", TypingBudgetMs, slowest, slowestMs)
                    : string.Format(CultureInfo.InvariantCulture, "Over the {0:0} ms budget: \"{1}\" takes {2:0.0} ms (median).\n", TypingBudgetMs, slowest, slowestMs));
            int lost = all.Count(v => v == null);
            if (lost > 0) text.Append(lost).Append(lost == 1 ? " key" : " keys").Append(" never reached the screen (no list-painted mark).\n");
        }

        static string SurfaceName(string group)
        {
            string? name;
            return SurfaceNames.TryGetValue(group, out name) ? name : group.Length > 0 ? group : "Runs";
        }

        static string Pad(string name) => name.Length >= 34 ? name.Substring(0, 33) + " " : name.PadRight(34);
        static string Cell(string s, int width = 7) => s.PadLeft(width);
        static string Cell(double? v, int width = 7) =>
            (v == null ? "-" : v.Value.ToString(v.Value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture)).PadLeft(width);
    }
}
