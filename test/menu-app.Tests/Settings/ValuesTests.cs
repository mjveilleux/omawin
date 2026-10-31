namespace SystemMenu.Tests.Settings;

public class ValuesTests
{
    static SettingEntry Line(string line) => Catalog.ParseLine(line, "t:1");

    [Fact]
    public void AnAbsentValueIsTheSideThatDeletesIt()
    {
        var e = Line(@"system.startup-delay | - | No delay | x | switch | reg HKCU\A\StartupDelayInMSec dword on=0 off=- |");
        Assert.Equal("off", SettingValues.ToWords(e, null));
        Assert.Equal("on", SettingValues.ToWords(e, "0"));
        Assert.Equal("-", SettingValues.FromWords(e, "off"));
    }

    [Fact]
    public void ADwordWithItsTopBitSetIsReadUnsigned()
    {
        // TapsEnabled's "on" is 0xFFFFFFFF, which .NET hands back as the int -1.
        var e = Line(@"input.tap | - | Tap | x | switch | reg HKCU\A\TapsEnabled dword on=4294967295 off=0 |");
        Assert.Equal("on", SettingValues.ToWords(e, SettingValues.RawText(-1)));
        Assert.Equal(-1, SettingValues.Dword("4294967295"));
        Assert.Equal(16, SettingValues.Dword("0x10"));
    }

    [Fact]
    public void AnUnknownSwitchValueIsNotGuessed()
    {
        var e = Line(@"explorer.hidden | - | Hidden | x | switch | reg HKCU\A\Hidden dword on=1 off=2 |");
        Assert.Null(SettingValues.ToWords(e, "0"));
        Assert.Equal("?", SettingValues.Label(e, null, []));
    }

    [Fact]
    public void TypedValuesAreForgiving()
    {
        var sw = Line(@"look.x | - | X | x | switch | reg HKCU\A\B dword on=1 off=0 |");
        Assert.Equal("on", SettingValues.Normalize(sw, " Yes ", []));
        var choice = Fixture.Entries().First(e => e.Id == "explorer.open-to");
        Assert.Equal("3", SettingValues.Normalize(choice, "downloads", choice.Choices));
        Assert.Null(SettingValues.Validate(choice, "3", choice.Choices));
        Assert.Contains("1 (This PC)", SettingValues.Validate(choice, "9", choice.Choices));
        var slider = Fixture.Entries().First(e => e.Id == "input.pointer-speed");
        Assert.Contains("1 to 20", SettingValues.Validate(slider, "21", []));
        Assert.Contains("1 to 20", SettingValues.Validate(slider, "fast", []));
    }

    [Theory]
    [InlineData("2", 1, "3")]
    [InlineData("3", 1, "1")]   // wraps forward
    [InlineData("1", -1, "3")]  // and back
    [InlineData(null, 1, "1")]  // unknown: the first going forward
    [InlineData(null, -1, "3")] // the last going back
    public void AChoiceStepsAndWraps(string? current, int by, string next)
    {
        var e = Fixture.Entries().First(e => e.Id == "explorer.open-to");
        Assert.Equal(next, SettingValues.Next(e, current, by, e.Choices));
    }

    [Theory]
    [InlineData("10", 1, "11")]
    [InlineData("20", 1, "20")] // stops at its ends
    [InlineData("1", -1, "1")]
    public void ASliderStepsWithinItsRange(string current, int by, string next)
    {
        var e = Fixture.Entries().First(e => e.Id == "input.pointer-speed");
        Assert.Equal(next, SettingValues.Next(e, current, by, []));
    }
}
