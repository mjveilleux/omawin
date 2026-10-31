namespace Omawin.Windows.Tests;

// 8.41: the disks', battery's and devices' health as Windows reports them, worded with what to do.
public class DeviceHealthTests
{
    // BATTERY_INFORMATION byte by byte, as the battery driver fills it.
    static byte[] BatteryInfo(uint caps, uint design, uint full, uint cycles)
    {
        var b = new byte[DeviceHealth.BatteryInformationSize];
        BitConverter.GetBytes(caps).CopyTo(b, 0);
        b[4] = 1;                                    // rechargeable
        "LION"u8.ToArray().CopyTo(b, 8);             // chemistry
        BitConverter.GetBytes(design).CopyTo(b, 12);
        BitConverter.GetBytes(full).CopyTo(b, 16);
        BitConverter.GetBytes(300u).CopyTo(b, 20);   // alerts and bias, unused
        BitConverter.GetBytes(cycles).CopyTo(b, 32);
        return b;
    }

    [Fact]
    public void BatteryWearFromTheDriversBytes()
    {
        var b = DeviceHealth.ReadBattery(BatteryInfo(0x80000000, 54_000, 42_120, 412), "DELL 7FHHV\0")!;
        Assert.Equal(54_000, b.Design);
        Assert.Equal(412, b.Cycles);
        var (line, advice) = DeviceHealth.Battery(b);
        Assert.Equal("DELL 7FHHV: holds 78% of what it did new (42,120 of 54,000 mWh), 412 cycles", line);
        Assert.Contains("Worn", advice);
    }

    [Fact]
    public void BatteryNotThePcsOwnOrUnknownIsLeftOut()
    {
        Assert.Null(DeviceHealth.ReadBattery(BatteryInfo(0, 54_000, 50_000, 1), "UPS"));            // not a system battery
        Assert.Null(DeviceHealth.ReadBattery(BatteryInfo(0x80000000, 0xFFFFFFFF, 50_000, 1), "x")); // unknown capacity
        Assert.Null(DeviceHealth.ReadBattery(new byte[10], "short"));
    }

    [Fact]
    public void RelativeCapacityShowsOnlyTheShare()
    {
        var b = DeviceHealth.ReadBattery(BatteryInfo(0xC0000000, 100, 95, 0), "")!;
        Assert.Null(b.Cycles);
        var (line, advice) = DeviceHealth.Battery(b);
        Assert.Equal("Battery: holds 95% of what it did new", line);
        Assert.Null(advice);
    }

    [Fact]
    public void AHealthyDiskIsOneLine()
    {
        var (line, advice) = DeviceHealth.Disk(new DiskHealth("Samsung SSD 980 1TB", "SSD", "NVMe", "Healthy", 3, 41, 82, 2310, 0, 0));
        Assert.Equal("Samsung SSD 980 1TB (NVMe SSD): Healthy, 3% worn, 41°C, 2,310 hours on", line);
        Assert.Empty(advice);
    }

    [Fact]
    public void ATroubledDiskSaysWhatToDo()
    {
        var (line, advice) = DeviceHealth.Disk(new DiskHealth("WD Blue", "hard disk", "SATA", "Healthy", null, 72, null, null, 5, 0));
        Assert.Contains("5 uncorrected errors", line);
        Assert.Contains(advice, a => a.Contains("chkdsk"));
        Assert.Contains(advice, a => a.Contains("hot"));
        Assert.Contains(DeviceHealth.Disk(new DiskHealth("x", "SSD", "", "Unhealthy", 95, null, null, null, null, null)).Advice, a => a.Contains("expects this disk to fail"));
    }

    [Theory]
    [InlineData(22, false)] // disabled by you
    [InlineData(45, false)] // not connected now
    [InlineData(24, false)]
    [InlineData(28, true)]
    [InlineData(43, true)]
    public void OnlyRealProblemsCount(int code, bool problem) => Assert.Equal(problem, DeviceHealth.IsProblem(code));

    [Fact]
    public void ReportListsEachPartAndWhatWasntReadable()
    {
        var lines = DeviceHealth.Report(
            [new DiskHealth("NVMe", "SSD", "NVMe", "Healthy", null, null, null, null, null, null)],
            [],
            [new DeviceProblem("Fingerprint Reader", 28, @"USB\VID_1")],
            ["(disks' wear and temperature: admin only)"]);
        Assert.Equal("Disks:", lines[0]);
        Assert.Equal("    NVMe (NVMe SSD): Healthy", lines[1]);
        Assert.DoesNotContain("Battery:", lines); // a desktop has none: no section
        Assert.Contains("  ! Fingerprint Reader: no driver installed", lines);
        Assert.Contains(lines, l => l.Contains("Optional updates"));
        Assert.Equal("(disks' wear and temperature: admin only)", lines[^1]);
        Assert.Contains("  couldn't be read", DeviceHealth.Report(null, [], [], []));
    }
}
