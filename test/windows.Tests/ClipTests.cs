namespace Omawin.Windows.Tests;

public class ClipTests
{
    static string? Run(string name, string text) => Transforms.Find(name)!.Apply(text);

    [Fact]
    public void JsonPrettyAndOneLine()
    {
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ],\n  \"é\": \"<b>\"\n}", Run("json", "{\"a\":[1,2],\"é\":\"<b>\"}")!.Replace("\r\n", "\n"));
        Assert.Equal("{\"a\":1}", Run("json-line", "{\n  // a comment\n  \"a\": 1,\n}"));
        Assert.Null(Run("json", "not json"));
    }

    [Theory]
    [InlineData("aGVsbG8gd29ybGQ=", "hello world")]
    [InlineData("aGVsbG8gd29ybGQ", "hello world")]        // unpadded
    [InlineData("  aGVs\nbG8=  ", "hello")]               // wrapped
    [InlineData("-_8", "\ufffd")]                         // URL-safe alphabet; not UTF-8, so null below
    public void Base64Decodes(string text, string expected)
    {
        var result = Run("unbase64", text);
        if (expected == "\ufffd") Assert.Null(result);
        else Assert.Equal(expected, result);
    }

    [Fact]
    public void Base64RoundTripsUnicode()
    {
        var encoded = Run("base64", "naïve ✓")!;
        Assert.Equal("naïve ✓", Run("unbase64", encoded));
        Assert.Null(Run("unbase64", "!!!"));
    }

    [Fact]
    public void UrlEncodingBothWays()
    {
        Assert.Equal("a%20b%26c%3Dd", Run("url", "a b&c=d"));
        Assert.Equal("a b&c=d e", Run("unurl", "a%20b%26c%3Dd+e"));
    }

    [Theory]
    [InlineData("1700000000", "2023-11-14 22:13:20 +00:00")]
    [InlineData("1700000000123", "2023-11-14 22:13:20 +00:00")]
    [InlineData(" 0 ", "1970-01-01 00:00:00 +00:00")]
    public void TimestampsBecomeDates(string text, string date) => Assert.Equal(date, Transforms.FromTimestamp(text, TimeZoneInfo.Utc));

    [Fact]
    public void NotATimestampIsNull()
    {
        Assert.Null(Transforms.FromTimestamp("soon", TimeZoneInfo.Utc));
        Assert.Null(Transforms.FromTimestamp("999999999999999999", TimeZoneInfo.Utc));
    }

    [Fact]
    public void DatesBecomeTimestamps()
    {
        Assert.Equal("1700000000", Run("timestamp", "2023-11-14T22:13:20Z"));
        Assert.Null(Run("timestamp", "not a date"));
    }

    [Fact]
    public void TrimKeepsTheLineEndings()
    {
        Assert.Equal("a\r\n  b", Run("trim", "\r\n\r\na   \r\n  b  \r\n\r\n"));
        Assert.Equal("x\ny", Run("trim", "x \ny\t\n"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\src\app", "/mnt/c/Users/me/src/app")]
    [InlineData(@"""D:\a b\c""", "/mnt/d/a b/c")]
    [InlineData("/mnt/c/Users/me", @"C:\Users\me")]
    [InlineData("/mnt/e", @"E:\")]
    [InlineData("src/app/main.rs", @"src\app\main.rs")]
    [InlineData(@"src\app", "src/app")]
    public void PathsSwapBetweenWindowsAndWsl(string from, string to) => Assert.Equal(to, Run("path", from));

    [Fact]
    public void CaseAndPlainAndNames()
    {
        Assert.Equal("ABC", Run("upper", "abc"));
        Assert.Equal("abc", Run("LOWER".ToLowerInvariant(), "ABC"));
        Assert.Equal("as is", Run("plain", "as is"));
        Assert.NotNull(Transforms.Find("JSON"));
        Assert.Null(Transforms.Find("nope"));
        Assert.Equal(Transforms.All.Length, Transforms.All.Select(t => t.Name).Distinct().Count());
    }
}
