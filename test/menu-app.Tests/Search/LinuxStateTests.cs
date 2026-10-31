namespace SystemMenu.Tests.Search;

// The bar's Linux: when WSL is asked, what its answer means, and the logo the finder and bar draw.
public class LinuxStateTests
{
    static readonly DateTime T = new(2026, 10, 31, 12, 0, 0);

    [Fact]
    public void WslIsAskedOnlyWhileTheVmIsUp()
    {
        Assert.False(LinuxState.ShouldAsk(vmUp: false, vmWasUp: true, T.AddMinutes(-5), T));
        Assert.True(LinuxState.ShouldAsk(vmUp: true, vmWasUp: false, T, T));          // it just came up
        Assert.False(LinuxState.ShouldAsk(vmUp: true, vmWasUp: true, T.AddSeconds(-10), T));
        Assert.True(LinuxState.ShouldAsk(vmUp: true, vmWasUp: true, T - LinuxState.AskEvery, T));
    }

    [Fact]
    public void TheRunningListIsReadInEitherEncoding()
    {
        Assert.True(LinuxState.Running("Ubuntu\r\narchlinux\r\n", "archlinux"));
        Assert.True(LinuxState.Running("A\0r\0c\0h\0l\0i\0n\0u\0x\0\r\0\n\0", "ArchLinux"));
        Assert.False(LinuxState.Running("archlinux-old\n", "archlinux"));
        Assert.False(LinuxState.Running("", "archlinux"));
    }

    [Fact]
    public void TheTipSaysTheStateAndTheMemory()
    {
        Assert.Equal("Arch: running · 812 MB · click: a terminal · right click: stop it",
            LinuxState.Tip(new LinuxState.Status("archlinux", "arch", true, 812L << 20)));
        Assert.Equal("Ubuntu: running · click: a terminal · right click: stop it",
            LinuxState.Tip(new LinuxState.Status("Ubuntu", "ubuntu", true, null)));
        Assert.Equal("Arch: stopped · click: start it, in a terminal",
            LinuxState.Tip(new LinuxState.Status("archlinux", "arch", false, null)));
        Assert.Equal("1.5 GB", LinuxState.Memory(3L << 29));
    }

    [Fact]
    public void LinuxGuiAppsAreKnownByTheirDistro()
    {
        string[] distros = ["archlinux", "Ubuntu"];
        Assert.Equal("archlinux", LinuxState.WslgDistro("Firefox (archlinux)", distros));
        Assert.Equal("Ubuntu", LinuxState.WslgDistro("Text Editor (ubuntu)", distros));
        Assert.Null(LinuxState.WslgDistro("Visual Studio Code (User)", distros));   // a Windows app's own brackets
        Assert.Null(LinuxState.WslgDistro("Firefox", distros));
        Assert.Null(LinuxState.WslgDistro("(archlinux)", distros));
    }

    [Fact]
    public void EachDistroHasItsLogo()
    {
        Assert.Equal(0xF303, LinuxFiles.Logo("archlinux"));
        Assert.Equal(0xF31B, LinuxFiles.Logo("Ubuntu-24.04"));
        Assert.Equal(0xF31A, LinuxFiles.Logo("kali-linux"));
        Assert.Equal(0xF303, LinuxFiles.LogoOfRoot(@"\\wsl.localhost\archlinux"));
        Assert.Equal(0xF31B, LinuxFiles.LogoOfRoot(@"\\WSL.localhost\Ubuntu\home"));
        Assert.Null(LinuxFiles.LogoOfRoot(@"C:\Users\you\Documents"));
        Assert.Null(LinuxFiles.LogoOfRoot(null));
    }
}
