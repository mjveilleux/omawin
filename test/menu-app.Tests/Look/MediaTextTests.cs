namespace SystemMenu.Tests.Look;

// The bar's media widget's words (shell/menu-app/Bar/MediaText.cs).
public class MediaTextTests
{
    [Fact]
    public void TitleThenArtistUnlessTheTitleNamesIt()
    {
        Assert.Equal("Teardrop · Massive Attack", MediaText.Label("Teardrop", "Massive Attack", "Spotify.exe"));
        Assert.Equal("Massive Attack - Teardrop", MediaText.Label("Massive Attack - Teardrop", "Massive Attack", "Chrome"));
        Assert.Equal("Massive Attack", MediaText.Label("", "Massive Attack", null));
        Assert.Equal("Spotify", MediaText.Label(null, "  ", "Spotify.exe"));
    }

    [Fact]
    public void LongTitlesAreCutWithAnEllipsis()
    {
        var label = MediaText.Label("A very long title that goes on and on and on", "Someone", null, max: 20);
        Assert.Equal(20, label.Length);
        Assert.EndsWith("…", label);
        Assert.StartsWith("A very long title", label);
        // Never half an emoji.
        var emoji = MediaText.Label("abcdefghi🎵xyz", null, null, max: 11);
        Assert.False(char.IsHighSurrogate(emoji[^2]));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "Vlc")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("SomeVendor.Player_abc123!App", "Player")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("mpv.exe", "Mpv")] // omawin's own player: Media.cs leaves it to the record
    [InlineData("308046B0AF4A39CB", null)]
    [InlineData("", null)]
    public void AppNames(string id, string? name) => Assert.Equal(name, MediaText.AppName(id));

    [Fact]
    public void TheTipSaysEverythingAndTheClicks()
    {
        var tip = MediaText.Tip("Teardrop", "Massive Attack", "Spotify.exe", playing: false);
        Assert.Equal("Teardrop\nMassive Attack\nSpotify · paused\nClick: play/pause · right click: previous", tip);
    }
}
