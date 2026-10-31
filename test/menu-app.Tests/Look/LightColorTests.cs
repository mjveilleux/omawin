namespace SystemMenu.Tests.Look;

// The theme's accent as the keyboard's lights take it (roadmap 8.9).
public class LightColorTests
{
    [Fact]
    public void APalettesHexIsItsThreeChannels() => Assert.Equal(((byte)0x7a, (byte)0xa2, (byte)0xf7), LightColor.Parse("#7aa2f7"));

    [Fact]
    public void ShortHexAndNoHashWork()
    {
        Assert.Equal(((byte)0xff, (byte)0x88, (byte)0x00), LightColor.Parse("#f80"));
        Assert.Equal(((byte)0x12, (byte)0x34, (byte)0x56), LightColor.Parse(" 123456 "));
    }

    [Fact]
    public void AlphaIsDroppedSinceALampHasNone() => Assert.Equal(((byte)0x11, (byte)0x22, (byte)0x33), LightColor.Parse("#80112233"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#gg0000")]
    [InlineData("blue")]
    public void AnythingElseLeavesTheLightsAlone(string? hex) => Assert.Null(LightColor.Parse(hex));
}
