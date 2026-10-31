namespace SystemMenu.Tests.Look;

// The bar's install glyph and end toast read what install.ps1 leaves (shell/menu-app/Bar/InstallMark.cs).
public class InstallMarkTests
{
    [Fact]
    public void RunningNeedsAProcess()
    {
        var r = InstallMark.ParseRunning(["kind\tupdate", "pid\t4242", @"log	C:\Users\me\AppData\Local\launcher\install.log"]);
        Assert.Equal(new InstallMark.Running("update", 4242, @"C:\Users\me\AppData\Local\launcher\install.log"), r);
        Assert.Contains("updating", InstallMark.Tip(r!));
        Assert.Null(InstallMark.ParseRunning(["kind\tinstall", "pid\tx"]));
        Assert.Null(InstallMark.ParseRunning([]));
    }

    [Fact]
    public void TheToastSaysHowItWent()
    {
        Assert.Equal("omawin's update is done in 52 s", InstallMark.Toast(InstallMark.ParseDone(["kind\tupdate", "result\tok", "seconds\t52", "failed\t", "warnings\t0"])!));
        Assert.Equal("omawin's install is done in 3 min 05 s, with 1 warning", InstallMark.Toast(InstallMark.ParseDone(["result\twarnings", "seconds\t185", "warnings\t1"])!));
        Assert.Equal("omawin's install stopped at tools: its window says why", InstallMark.Toast(InstallMark.ParseDone(["kind\tinstall", "result\tfailed", "seconds\t9", "failed\ttools"])!));
        Assert.Null(InstallMark.ParseDone(["kind\tinstall"]));
    }

    [Fact]
    public void OnlyANewEndIsSaid()
    {
        var now = new DateTime(2026, 10, 31, 12, 0, 0);
        Assert.True(InstallMark.Fresh(now.AddSeconds(-3), now));
        Assert.False(InstallMark.Fresh(now.AddHours(-2), now));
    }
}
