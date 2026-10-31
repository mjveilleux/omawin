using Xunit.Abstractions;

namespace SystemMenu.Tests.Settings;

public class RoundTripPlanTests
{
    [Fact]
    public void OnlyValueSettingsTheDeclarativeHandlersChange()
    {
        var plan = RoundTrip.Plan(Fixture.Entries(), build: 26100, elevated: false);
        Assert.Equal(["look.dark-apps", "explorer.open-to", "input.pointer-speed", "developer.long-paths"], plan.Select(s => s.Entry.Id));
        Assert.Contains("isn't elevated", plan.Single(s => s.Entry.Id == "developer.long-paths").Skip);
        Assert.Null(RoundTrip.Plan(Fixture.Entries(), 26100, elevated: true).Single(s => s.Entry.Id == "developer.long-paths").Skip);
    }

    [Fact]
    public void AnEntryForAnotherBuildIsSkippedSayingWhy()
    {
        var e = Catalog.ParseLine(@"explorer.end-task | - | End task | kill | switch | reg HKCU\A\TaskbarEndTask dword on=1 off=0 | minBuild=22631", "t:1");
        Assert.Contains("22631", RoundTrip.Plan([e], 22000, true)[0].Skip);
    }

    [Theory]
    [InlineData("look.dark-apps", "on", "off")]
    [InlineData("look.dark-apps", null, "on")]
    [InlineData("explorer.open-to", "1", "2")]
    [InlineData("input.pointer-speed", "10", "1")]
    [InlineData("input.pointer-speed", "1", "20")]
    public void TriesAnotherValue(string id, string? current, string other) =>
        Assert.Equal(other, RoundTrip.Other(Fixture.Entries().Single(e => e.Id == id), current));
}

// Runs only where it can mean something: on Windows with OMAWIN_ROUNDTRIP=1 (the settings-roundtrip
// job in .github/workflows/test.yml). Elsewhere it's reported as skipped, not passed.
public sealed class RoundTripFactAttribute : FactAttribute
{
    public RoundTripFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("OMAWIN_ROUNDTRIP") != "1")
            Skip = "Windows CI only (OMAWIN_ROUNDTRIP=1)";
    }
}

public class RoundTripTests(ITestOutputHelper output)
{
    // Every reg / spi / powercfg entry of the shipped catalog, "check" lines included: read, set
    // another value, read it back, put the first one back exactly. Machine-wide ones only when
    // elevated (GitHub's runners are). A SystemParametersInfo the runner's session refuses is
    // reported as skipped with the reason, never silently passed; a write that reads back wrong
    // fails.
    [RoundTripFact]
    public void EveryEntrySetsAndRestores()
    {
        var file = Path.Combine(CatalogTests.RepoRoot(), "default", "launcher", "settings.txt");
        var (entries, problems) = Catalog.Parse([(file, File.ReadAllLines(file))]);
        Assert.Empty(problems);
        int build = Environment.OSVersion.Version.Build;
        bool elevated = IsElevated();
        var handlers = new Dictionary<string, ISettingHandler> { ["reg"] = new RegHandler(), ["spi"] = new SpiHandler(), ["powercfg"] = new PowerHandler() };
        var failures = new List<string>();
        int done = 0;
        foreach (var step in RoundTrip.Plan(entries, build, elevated))
        {
            var e = step.Entry with { Admin = false }; // written directly here, never through the admin queue
            if (step.Skip != null) { output.WriteLine($"skip  {e.Id}: {step.Skip}"); continue; }
            var h = handlers[e.How.Verb];
            var snapshot = e.How.Verb == "reg" ? RegHandler.Take(e) : null;
            var before = h.Read(e);
            if (before == null && e.How.Verb != "reg") { output.WriteLine($"skip  {e.Id}: can't be read in this session"); continue; }
            var other = RoundTrip.Other(e, before);
            try
            {
                if (h.Write(e, other) is { } error)
                {
                    if (e.How.Verb == "spi") output.WriteLine($"skip  {e.Id}: this session refused it ({error})");
                    else failures.Add($"{e.Id}: writing {other}: {error}");
                    continue;
                }
                var after = h.Read(e);
                if (after != other) failures.Add($"{e.Id}: wrote {other}, read back {after ?? "nothing"}");
                else { done++; output.WriteLine($"ok    {e.Id}: {before ?? "(absent)"} -> {other} -> back"); }
            }
            catch (Exception ex) { failures.Add($"{e.Id}: {ex.GetType().Name}: {ex.Message}"); }
            finally
            {
                try
                {
                    if (snapshot != null) RegHandler.Restore(e, snapshot);
                    else if (before != null) h.Write(e, before);
                    if (h.Read(e) != before) failures.Add($"{e.Id}: not restored to {before}");
                }
                catch (Exception ex) { failures.Add($"{e.Id}: restoring: {ex.Message}"); }
            }
        }
        output.WriteLine($"{done} round-tripped on build {build}{(elevated ? ", elevated" : "")}");
        Assert.Empty(failures);
        Assert.True(done > 0, "nothing was round-tripped");
    }

    static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
