namespace SystemMenu.Tests;

public class MicMuteTests
{
    [Fact]
    public void ToggleMutesAllUnlessAllAreMuted()
    {
        Assert.True(MicMute.Target([false, false], "toggle"));
        Assert.True(MicMute.Target([true, false], "toggle")); // one still live: mute everything
        Assert.False(MicMute.Target([true, true], "toggle"));
        Assert.True(MicMute.Target([false], ""));
    }

    [Fact]
    public void MuteAndUnmuteAreWhatTheySay()
    {
        Assert.True(MicMute.Target([true, false], "mute"));
        Assert.False(MicMute.Target([true, false], "unmute"));
        Assert.Null(MicMute.Target([true], "on")); // ambiguous words aren't guessed
        Assert.Null(MicMute.Target([], "toggle"));
    }

    [Fact]
    public void SaySaysHowManyAreLive()
    {
        Assert.Equal("No microphone", MicMute.Say([]));
        Assert.Equal("Microphone muted", MicMute.Say([true]));
        Assert.Equal("All 2 microphones muted", MicMute.Say([true, true]));
        Assert.Equal("1 of 2 microphones live", MicMute.Say([true, false]));
        Assert.Equal("Microphone on", MicMute.Say([false]));
    }
}
