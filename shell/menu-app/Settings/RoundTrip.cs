// The settings round-trip CI runs on a Windows runner (roadmap 1B "Testing"): every reg, spi and
// powercfg entry is read, set to another value, read back, and restored. This is the planning
// half (which entries, which value to try), pure so it's tested anywhere; the test that does it
// is test/menu-app.Tests/Settings/RoundTripTests.cs, through the real handlers (Switches.cs).

namespace SystemMenu;

public static class RoundTrip
{
    public sealed record Step(SettingEntry Entry, string? Skip);

    // Each reg / spi / powercfg entry with a value, and why it's skipped if it is: not this
    // Windows build, or machine-wide while the runner isn't elevated. Lines marked "check" are
    // included: this is where they get checked.
    public static List<Step> Plan(IEnumerable<SettingEntry> entries, int build, bool elevated) => entries
        .Where(e => e.How.Verb is "reg" or "spi" or "powercfg" && e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider)
        .Select(e => new Step(e,
            !e.FitsBuild(build) ? $"for Windows builds {e.MinBuild}..{(e.MaxBuild == 0 ? "" : e.MaxBuild.ToString())}, this is {build}"
            : e.Admin && !elevated ? "needs admin; the runner isn't elevated"
            : null))
        .ToList();

    // A value other than the current one: a switch's opposite, a choice's next value, a slider's
    // far end (its minimum, or its maximum when it's at the minimum already). An unknown current
    // value tries "on" or the first choice.
    public static string Other(SettingEntry e, string? current) => e.Kind switch
    {
        SettingKind.Switch => current == "on" ? "off" : "on",
        SettingKind.Choice => e.Choices.FirstOrDefault(c => c.Value != current)?.Value
            ?? throw new InvalidOperationException($"{e.Id}: a choice with one value"),
        SettingKind.Slider when e.Range is var (lo, hi) => (current == lo.ToString() ? hi : lo).ToString(),
        _ => throw new InvalidOperationException($"{e.Id}: nothing to round-trip"),
    };
}
