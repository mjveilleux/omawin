using System.Text.RegularExpressions;

namespace Omawin.Windows.Tests;

public class HostsTests
{
    // Windows' own hosts file, as a new PC has it (shortened).
    const string Stock = "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n# For example:\r\n#\r\n#      102.54.94.97     rhino.acme.com          # source server\r\n\r\n# localhost name resolution is handled within DNS itself.\r\n#\t127.0.0.1       localhost\r\n#\t::1             localhost\r\n";

    [Fact]
    public void StockFileHasNoEntriesAndRoundTrips()
    {
        var f = new HostsFile(Stock);
        Assert.Empty(f.Entries());
        Assert.Equal(Stock, f.Text);
        Assert.Empty(f.BadLines());
    }

    [Fact]
    public void AddAppendsAndIsIdempotent()
    {
        var f = new HostsFile(Stock);
        Assert.Null(f.Add("127.0.0.1", ["myapp.test", "api.myapp.test"]));
        Assert.Equal("127.0.0.1\tmyapp.test api.myapp.test", f.Lines[^1]);
        var once = f.Text;
        Assert.Null(f.Add("127.0.0.1", ["myapp.test"]));
        Assert.Equal(once, f.Text);
        Assert.Single(f.Entries(), e => e.On && e.Names.Contains("myapp.test"));
    }

    [Theory]
    [InlineData("localhost:80", "a.test", "isn't an IP address")]
    [InlineData("127.0.0.1", "bad name!", "isn't a host name")]
    [InlineData("127.0.0.1", "-leading.test", "isn't a host name")]
    public void AddRefusesWhatIsntAnEntry(string address, string name, string says)
    {
        var f = new HostsFile(Stock);
        Assert.Contains(says, f.Add(address, [name]));
        Assert.Equal(Stock, f.Text);
    }

    [Fact]
    public void AddRefusesANameThatPointsElsewhere()
    {
        var f = new HostsFile("10.0.0.5 db.test\r\n");
        Assert.Contains("already points at 10.0.0.5 (line 1)", f.Add("127.0.0.1", ["db.test"]));
    }

    [Fact]
    public void OffCommentsWithAMarkerAndOnRestoresExactly()
    {
        var original = "127.0.0.1 a.test # mine\r\n";
        var f = new HostsFile(original);
        Assert.Null(f.Off("a.test"));
        Assert.Equal("# omawin-off 127.0.0.1 a.test # mine", f.Lines[0]);
        Assert.Single(f.Entries(), e => !e.On);
        Assert.Null(f.On("a.test"));
        Assert.Equal(original, f.Text);
    }

    [Fact]
    public void OffOnALineWithOtherNamesMovesOnlyThatName()
    {
        var f = new HostsFile("127.0.0.1\ta.test b.test # dev\r\n::1 other.test\r\n");
        Assert.Null(f.Off("b.test"));
        Assert.Equal(["127.0.0.1\ta.test # dev", "# omawin-off 127.0.0.1\tb.test", "::1 other.test"], f.Lines);
        Assert.Null(f.On("b.test"));
        Assert.Equal("127.0.0.1\tb.test", f.Lines[1]);
        Assert.Equal(2, f.Entries().Count(e => e.On && e.Address == "127.0.0.1"));
    }

    [Fact]
    public void AddingAnOffEntryTurnsItBackOn()
    {
        var f = new HostsFile("# omawin-off 127.0.0.1\tx.test\r\n");
        Assert.Null(f.Add("127.0.0.1", ["x.test"]));
        Assert.Equal(["127.0.0.1\tx.test"], f.Lines);
    }

    [Fact]
    public void OffAndOnSayWhenThereIsNothingToDo()
    {
        var f = new HostsFile(Stock);
        Assert.Contains("isn't in the hosts file", f.Off("nope.test"));
        Assert.Contains("isn't turned off here", f.On("nope.test"));
    }

    [Fact]
    public void OnRefusesWhenTheNameNowPointsElsewhere()
    {
        var f = new HostsFile("# omawin-off 127.0.0.1 a.test\r\n10.0.0.1 a.test\r\n");
        Assert.Contains("points at 10.0.0.1 now (line 2)", f.On("a.test"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("# anything at all", true)]
    [InlineData("127.0.0.1 localhost", true)]
    [InlineData("::1\tlocalhost ip6-localhost # comment", true)]
    [InlineData("fe80::1%12 link.test", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.0.0.1 bad;name", false)]
    [InlineData("Remove-Item C:\\ -Recurse", false)]
    public void LinePatternIsTheElevatedSidesCheck(string line, bool ok)
    {
        Assert.Equal(ok, HostsFile.IsValidLine(line));
        // The same pattern PowerShell's -match uses (.NET's regex there too).
        Assert.Equal(ok, Regex.IsMatch(line, HostsFile.LinePattern));
    }

    [Fact]
    public void BadLinesAreNumberedFromOne()
    {
        var f = new HostsFile("127.0.0.1 a.test\r\nnot a line;\r\n");
        Assert.Equal([(2, "not a line;")], f.BadLines());
    }

    [Fact]
    public void DiffShowsOnlyTheChange()
    {
        var a = new[] { "# x", "127.0.0.1 a.test", "::1 b.test" };
        var b = new[] { "# x", "# omawin-off 127.0.0.1 a.test", "::1 b.test", "10.0.0.1 c.test" };
        Assert.Equal(["- 127.0.0.1 a.test", "+ # omawin-off 127.0.0.1 a.test", "+ 10.0.0.1 c.test"], HostsFile.Diff(a, b));
    }
}
