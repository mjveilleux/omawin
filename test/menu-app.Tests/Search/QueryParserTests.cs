using SystemMenu.Search;

namespace SystemMenu.Tests.Search;

public class QueryParserTests
{
    [Theory]
    [InlineData("/road", Tag.File, "road", false)]
    [InlineData(">  restart ", Tag.Command, "restart", false)]
    [InlineData("#TODO", Tag.File, "TODO", true)]
    public void PrefixesPickAFilter(string raw, Tag filter, string text, bool contents)
    {
        var q = QueryParser.Parse(raw, token: null);
        Assert.Equal(filter, q.Filter);
        Assert.Equal(text, q.Text);
        Assert.Equal(contents, q.Contents);
    }

    [Fact]
    public void APrefixWinsOverTheTokenAndWithoutOneTheTokenStays()
    {
        Assert.Equal(Tag.Command, QueryParser.Parse(">lock", Tag.Setting).Filter);
        Assert.Equal(Tag.Setting, QueryParser.Parse("dark", Tag.Setting).Filter);
        Assert.Null(QueryParser.Parse("dark", null).Filter);
    }

    [Theory]
    [InlineData("set", Tag.Setting)]
    [InlineData("SE", Tag.Setting)]
    [InlineData("a", Tag.App)]
    [InlineData("w", Tag.Window)]
    [InlineData("cl", Tag.Clipboard)]
    [InlineData("co", Tag.Command)]
    public void TypingAFiltersNameLocksIt(string typed, Tag expected) =>
        Assert.Equal(expected, QueryParser.TagFromTyped(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dark")]
    public void OtherTextLocksNothing(string typed) => Assert.Null(QueryParser.TagFromTyped(typed));

    [Fact]
    public void TabGoesThroughEveryFilterAndBackToAll()
    {
        Tag? at = null;
        var seen = new List<Tag?>();
        for (int i = 0; i <= QueryParser.Filters.Count; i++) { at = QueryParser.NextFilter(at); seen.Add(at); }
        Assert.Equal(QueryParser.Filters.Select(f => (Tag?)f.Tag).Append(null), seen);
    }

    [Fact]
    public void ShiftTabGoesBackFromAllToTheLastFilter()
    {
        Assert.Equal(Tag.Theme, QueryParser.NextFilter(null, back: true));
        Assert.Null(QueryParser.NextFilter(Tag.App, back: true));
    }
}
