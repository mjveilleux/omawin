using SystemMenu;

namespace Omawin.MotionCheck;

// What the frames say about one opening or closing of the menu, and whether that's what
// Motion.cs asks for. Pure (no Windows), so it's tested on Linux with made-up frames.
//
// Each frame gets a level: 0 is the screen without the menu, 1 the menu settled (FrameMath.Level),
// so a fade is a climb from 0 to 1 (or a fall), however the menu and the desktop look.

// One frame's level, at its time from the request.
public readonly record struct Sample(double Ms, double Level);

public enum Way { In, Out }

// A climb (In) or a fall (Out): where it started (the first frame off its first side), where it
// ended (the first frame on the far side), how many frames were seen in between, the furthest it
// got (for one that never ended), and whether it stayed once there (no flash back).
public sealed record Transition(Way Way, double? StartMs, double? EndMs, int Between, double Furthest, bool Stayed)
{
    public double? DurationMs => StartMs is { } s && EndMs is { } e ? e - s : null;
}

// What to expect: animated (Motion's numbers plus a budget for frames the runner drops) or not
// (a jump: at most one frame caught half drawn).
public sealed record Expectation(bool Animated, double InWithinMs, double OutWithinMs, double BudgetMs)
{
    // The latest the opening may finish: the window's own fade (Enter), and the cascade's last
    // row or tile, which starts Lead + Stagger * its place later and fades for Enter too
    // (MainWindow.xaml.cs, Cascade). The closing: Exit, one fade and sink of the whole window.
    public static Expectation For(bool animated, double budgetMs)
    {
        double lastStep = Math.Max(Motion.CascadeRows, Motion.CascadeTiles);
        double cascade = (Motion.Lead + Motion.Stagger * lastStep).TotalMilliseconds;
        return new(animated, Motion.Enter.TotalMilliseconds + cascade + budgetMs, Motion.Exit.TotalMilliseconds + budgetMs, budgetMs);
    }
}

public static class Analysis
{
    // Gone below this, arrived above that: a little room for the caret's blink and the acrylic's grain.
    public const double Low = 0.03, High = 0.97;

    // With animations off a jump may still be caught mid-draw in one frame; more is a fade.
    public const int MaxBetweenWhenOff = 1;

    // Below this many frames a second the recording can't tell a 130 ms fade from a jump.
    public const double MinFps = 20;

    // The climb or fall in `samples` (time order, from the request on).
    public static Transition Find(IReadOnlyList<Sample> samples, Way way, double low = Low, double high = High)
    {
        bool Left(double level) => way == Way.In ? level > low : level < high;          // left the first side
        bool Done(double level) => way == Way.In ? level >= high : level <= low;      // on the far side
        bool Back(double level) => way == Way.In ? level < 0.85 : level > 0.15;        // well off it again

        int start = -1, end = -1;
        for (int i = 0; i < samples.Count; i++)
            if (!double.IsNaN(samples[i].Level) && Left(samples[i].Level)) { start = i; break; }
        if (start >= 0)
            for (int i = start; i < samples.Count; i++)
                if (Done(samples[i].Level)) { end = i; break; }

        int between = 0;
        double furthest = way == Way.In ? double.NegativeInfinity : double.PositiveInfinity;
        int last = end >= 0 ? end : samples.Count;
        for (int i = Math.Max(0, start); start >= 0 && i < last; i++)
        {
            var l = samples[i].Level;
            if (l > low && l < high) between++;
        }
        foreach (var s in samples)
            if (!double.IsNaN(s.Level)) furthest = way == Way.In ? Math.Max(furthest, s.Level) : Math.Min(furthest, s.Level);
        bool stayed = true;
        for (int i = end + 1; end >= 0 && i < samples.Count; i++)
            if (Back(samples[i].Level)) { stayed = false; break; }

        return new Transition(way, start >= 0 ? samples[start].Ms : null, end >= 0 ? samples[end].Ms : null, between,
            double.IsInfinity(furthest) ? double.NaN : furthest, stayed);
    }

    // What's wrong with an opening and a closing, in words; none is a pass.
    public static List<string> Problems(Transition open, Transition close, Expectation expect)
    {
        var problems = new List<string>();
        Check(open, "opening", "arrive", expect.InWithinMs);
        Check(close, "closing", "leave", expect.OutWithinMs);
        return problems;

        void Check(Transition t, string name, string verb, double withinMs)
        {
            if (t.StartMs is null) { problems.Add($"{name}: nothing changed on screen"); return; }
            if (t.EndMs is null)
            {
                problems.Add(t.Way == Way.In
                    ? $"{name}: never reached full opacity (at most {t.Furthest:0.00})"
                    : $"{name}: never hidden (down to {t.Furthest:0.00} at least)");
                return;
            }
            if (!t.Stayed) problems.Add($"{name}: flashed back after it {(t.Way == Way.In ? "arrived" : "left")}");
            if (expect.Animated)
            {
                if (t.DurationMs > withinMs) problems.Add($"{name}: took {t.DurationMs:0} ms to {verb}, allowed {withinMs:0} ms");
                if (t.Between == 0) problems.Add($"{name}: one jump, no frame in between: it didn't animate");
            }
            else if (t.Between > MaxBetweenWhenOff)
                problems.Add($"{name}: {t.Between} frames in between with animations off ({t.DurationMs:0} ms): it should be one jump");
        }
    }

    // Frames a second, from the median gap between frames (one slow frame doesn't count).
    public static double Fps(IReadOnlyList<double> times)
    {
        if (times.Count < 2) return 0;
        var gaps = new List<double>();
        for (int i = 1; i < times.Count; i++) gaps.Add(times[i] - times[i - 1]);
        gaps.Sort();
        double median = gaps[gaps.Count / 2];
        return median <= 0 ? 0 : 1000 / median;
    }
}
