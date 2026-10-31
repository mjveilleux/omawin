namespace SystemMenu.Tests;

// The notification history (8.22): its files, searching, codes and links.
public sealed class NoticeLogTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "omawin-notices-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    static Notice N(string when, string app, string title, string body, uint id = 0) =>
        new(DateTimeOffset.Parse(when), app, title, body, id);

    [Fact]
    public void LinesRoundTripWithTabsAndNewlines()
    {
        var n = N("2026-10-31T09:15:00+01:00", "Mail", "Re:\tplan", "line one\r\nline two \\ done", 42);
        var line = NoticeLog.Line(n);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(5, line.Split('\t').Length);
        var back = NoticeLog.Parse(line)!;
        Assert.Equal(n with { Body = "line one\nline two \\ done" }, back);
        Assert.Null(NoticeLog.Parse("not a line"));
    }

    [Fact]
    public void MonthFilesNewestFirstAndTrimmed()
    {
        NoticeLog.Append(dir, N("2026-09-02T10:00:00Z", "Teams", "Standup", "in 5 minutes"));
        NoticeLog.Append(dir, N("2026-10-30T10:00:00Z", "Mail", "Invoice", "attached"));
        NoticeLog.Append(dir, N("2026-10-31T08:00:00Z", "Mail", "Hello", ""));
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not the history's");
        var all = NoticeLog.Load(dir);
        Assert.Equal(["Hello", "Invoice", "Standup"], all.Select(n => n.Title));
        Assert.DoesNotContain(NoticeLog.Load(dir, months: 1), n => n.Title == "Standup");
        var trimmed = NoticeLog.Trim(dir, keep: 1);
        Assert.Single(trimmed);
        Assert.EndsWith("2026-09.tsv", trimmed[0]);
        Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
    }

    [Fact]
    public void SearchAndApps()
    {
        var all = new List<Notice>
        {
            N("2026-10-31T08:00:00Z", "Outlook", "Quarterly report", "Please review"),
            N("2026-10-31T09:00:00Z", "Teams", "Mason", "the report is ready"),
            N("2026-10-30T09:00:00Z", "Teams", "Build", "failed on main"),
        };
        Assert.Equal(3, NoticeLog.Search(all, "").Count);
        Assert.Equal(2, NoticeLog.Search(all, "report").Count);
        Assert.Equal("Build", Assert.Single(NoticeLog.Search(all, "teams failed")).Title);
        var apps = NoticeLog.Apps(all);
        Assert.Equal(("Teams", 2), (apps[0].App, apps[0].Count));
    }

    [Theory]
    [InlineData("Your verification code is 482913", "482913")]
    [InlineData("Microsoft account security code: 123 456", "123456")]
    [InlineData("Use 7781 to sign in. Expires 2026", "7781")]
    [InlineData("Your OTP is 90210. Do not share.", "90210")]
    [InlineData("Meeting moved to 2026-11-02 at 14:30", null)]          // no word saying it's a code
    [InlineData("Your code: call 555-1234 for help", null)]             // a phone number isn't one
    [InlineData("Order 123456 shipped", null)]
    public void Codes(string text, string? code) => Assert.Equal(code, NoticeLog.Code(text));

    [Fact]
    public void LinksWithoutTrailingPunctuation()
    {
        Assert.Equal(["https://example.com/a?b=1", "http://x.dev"], NoticeLog.Links("See https://example.com/a?b=1. Or (http://x.dev), twice: http://x.dev"));
        Assert.Empty(NoticeLog.Links("no links here"));
    }
}
