namespace SystemMenu.Tests.Look;

// Motion's numbers keep Omarchy's rules, and its setting reads the way `omawin toggle
// animations` writes it (roadmap 1.4).
public class MotionTests
{
    [Fact]
    public void ArrivalsAreQuickAndExitsAboutTwoThirdsOfThem()
    {
        // Omarchy's motion.md: in within 250 ms, out in about two thirds of the way in.
        foreach (var t in new[] { Motion.Enter, Motion.Glide, Motion.Quick }) Assert.InRange(t.TotalMilliseconds, 1, 250);
        Assert.InRange(Motion.Exit / Motion.Enter, 0.6, 0.7);
        // The last row of a cascade has landed within 2.5 arrivals: a screenful never trickles in.
        var last = Motion.CascadeDelay(Motion.CascadeRows, Motion.CascadeRows)!.Value + Motion.Enter;
        Assert.True(last.TotalMilliseconds <= 2.5 * Motion.Enter.TotalMilliseconds, $"the cascade ends at {last.TotalMilliseconds} ms");
    }

    [Fact]
    public void TheEasingIsEaseOutQuint()
    {
        Assert.Equal((0.23f, 1f, 0.32f, 1f), (Motion.EaseX1, Motion.EaseY1, Motion.EaseX2, Motion.EaseY2));
        // An ease-out: steeper than linear at the start, flat at the end; never past its end.
        Assert.True(Motion.EaseY1 / Motion.EaseX1 > 1);
        Assert.True((1 - Motion.EaseY2) / (1 - Motion.EaseX2) < 1);
        Assert.True(Motion.EaseY1 <= 1 && Motion.EaseY2 <= 1);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off\r\n", false)]
    [InlineData(" OFF ", false)]
    [InlineData("", null)]
    [InlineData("maybe", null)]
    [InlineData(null, null)]
    public void TheSettingIsOnOrOffOrUnsaid(string? text, bool? on) => Assert.Equal(on, Motion.ParseSetting(text));

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(false, "toggle", true)]
    [InlineData(true, "on", true)]
    [InlineData(true, "Off", false)]
    [InlineData(true, "sideways", null)]
    public void AskedSaysWhatItsToBe(bool now, string? word, bool? next) => Assert.Equal(next, Motion.Asked(now, word));

    [Theory]
    [InlineData("Microsoft Corporation", "Virtual Machine", true)]       // Hyper-V, Windows 365, Azure
    [InlineData("VMware, Inc.", "VMware7,1", true)]
    [InlineData("innotek GmbH", "VirtualBox", true)]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", true)]
    [InlineData("Parallels International GmbH.", "Parallels ARM Virtual Machine", true)]
    [InlineData("Xen", "HVM domU", true)]
    [InlineData("Microsoft Corporation", "Surface Laptop 5", false)]
    [InlineData("LENOVO", "21CB0067US", false)]
    [InlineData("Dell Inc.", "XPS 15 9530", false)]
    [InlineData(null, null, false)]
    public void VirtualMachinesAreKnownByWhatWindowsCallsThem(string? manufacturer, string? model, bool vm) =>
        Assert.Equal(vm, Motion.IsVirtualMachine(manufacturer, model));

    [Fact]
    public void TheCascadeIsTheFirstScreenfulOnly()
    {
        Assert.Equal(Motion.Lead, Motion.CascadeDelay(0, 10));
        Assert.Equal(Motion.Lead + Motion.Stagger * 3, Motion.CascadeDelay(3, 10));
        Assert.Null(Motion.CascadeDelay(-1, 10)); // above the view
        Assert.Null(Motion.CascadeDelay(11, 10)); // past the cascade: there at once
    }
}
