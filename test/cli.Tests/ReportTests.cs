using Omawin.Cli.Release;
using Omawin.Cli.Report;

namespace Omawin.Cli.Tests;

public class ReportTests
{
    // A report as `omawin report` would collect it, full of what must not leave the PC.
    const string Log = """
        omawin check: 2 failed
        repo: C:\Users\ana\omawin  account: C:\Users\ana\omawin-account\work\acme-reports
        git config user.email ana.lopez@example.com
        GH_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz0123456789
        Authorization: Bearer sk-ant-api03-abcdefghijklmnopqrstuvwxyz
        password = "hunter2 with spaces"
        proxy 10.20.30.40:8080, local server on 127.0.0.1:3000
        PC ANA-LAPTOP7 signed in as ana; /mnt/c/Users/ana/omawin/bin
        anagram and banana stay
        """;

    [Fact]
    public void NothingIdentifyingSurvives()
    {
        var r = Redact.Text(Log, user: "ana", machine: "ANA-LAPTOP7", home: @"C:\Users\ana", account: @"C:\Users\ana\omawin-account");
        foreach (var secret in new[] { "ana.lopez", "example.com", "ghp_", "sk-ant", "hunter2", "10.20.30.40", "ANA-LAPTOP7", "acme", @"Users\ana", "Users/ana" })
            Assert.DoesNotContain(secret, r, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"~\omawin", r);
        Assert.Contains("<account>", r);
        Assert.Contains("127.0.0.1:3000", r);       // localhost is useful and not personal
        Assert.Contains("signed in as <user>", r);
        Assert.Contains("anagram and banana stay", r); // whole words only
    }

    [Fact]
    public void NotesGroupByAreaAndListWhatChangesOnThePC()
    {
        var notes = Notes.Make(
            ["Plugins: manifest model and protocol", "Settings catalog: 56 settings", "fix a typo", "plugins: the registry", "Merge branch 'x'"],
            [("2026-10-30-zebar-out.ps1", "Removes Zebar; the native bar takes its place")]);
        Assert.Equal("""
            ### Plugins
            - Manifest model and protocol
            - The registry

            ### Settings catalog
            - 56 settings

            ### Other
            - Fix a typo

            ### What changes on your PC
            - Removes Zebar; the native bar takes its place (`2026-10-30-zebar-out.ps1`)

            """.Replace("\r\n", "\n"), notes);
    }
}
