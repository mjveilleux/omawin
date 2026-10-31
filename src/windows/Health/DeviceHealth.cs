using System.Globalization;

namespace Omawin.Windows;

// Disk, battery and device health (roadmap 8.41), on 8.15's report: what Windows knows about the
// hardware wearing out or failing, each with what to do. Pure: DeviceHealthWin.cs reads the disks and
// devices from WMI and the battery from its driver; this file decodes and words them, so
// test/windows.Tests checks it with the values and bytes Windows gives.

// A physical disk as the Storage module reports it (MSFT_PhysicalDisk), with its reliability counters
// (MSFT_StorageReliabilityCounter) when Windows gives them to this account; null where it doesn't.
public sealed record DiskHealth(
    string Name, string Media, string Bus, string Health,
    int? Wear, int? Temperature, int? TemperatureMax, long? PowerOnHours, long? ReadErrors, long? WriteErrors);

// A battery as its driver reports it (BATTERY_INFORMATION). Relative: the capacities are in the
// driver's own units, not mWh (the ratio still holds).
public sealed record BatteryHealth(string Name, long Design, long Full, int? Cycles, bool Relative);

// A device with a problem (Win32_PnPEntity's ConfigManagerErrorCode).
public sealed record DeviceProblem(string Name, int Code, string Id);

public static class DeviceHealth
{
    // MSFT_PhysicalDisk.MediaType and BusType, and HealthStatus.
    public static string MediaName(int? media) => media switch { 3 => "hard disk", 4 => "SSD", 5 => "SCM", _ => "disk" };

    public static string BusName(int? bus) => bus switch
    {
        1 => "SCSI", 3 => "ATA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA",
        12 => "SD", 13 => "MMC", 15 => "file-backed", 16 => "Storage Spaces", 17 => "NVMe",
        _ => "",
    };

    public static string HealthName(int? health) => health switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };

    // BATTERY_INFORMATION (batclass.h), as IOCTL_BATTERY_QUERY_INFORMATION's BatteryInformation level
    // returns it: Capabilities (4), Technology (1), Reserved (3), Chemistry (4), DesignedCapacity (4),
    // FullChargedCapacity (4), DefaultAlert1, DefaultAlert2, CriticalBias (4 each), CycleCount (4).
    public const int BatteryInformationSize = 36;
    const uint BATTERY_SYSTEM_BATTERY = 0x80000000, BATTERY_CAPACITY_RELATIVE = 0x40000000;
    const uint BATTERY_UNKNOWN_CAPACITY = 0xFFFFFFFF;

    // Null for a battery that isn't the PC's own (a UPS reports through the same class) or reports
    // no capacity.
    public static BatteryHealth? ReadBattery(ReadOnlySpan<byte> info, string name)
    {
        if (info.Length < BatteryInformationSize) return null;
        uint caps = BitConverter.ToUInt32(info[..4]);
        uint design = BitConverter.ToUInt32(info.Slice(12, 4));
        uint full = BitConverter.ToUInt32(info.Slice(16, 4));
        uint cycles = BitConverter.ToUInt32(info.Slice(32, 4));
        if ((caps & BATTERY_SYSTEM_BATTERY) == 0) return null;
        if (design is 0 or BATTERY_UNKNOWN_CAPACITY || full is 0 or BATTERY_UNKNOWN_CAPACITY) return null;
        // The driver's name ends in NULs (its buffer's size, not the name's).
        var named = name.Trim('\0', ' ');
        return new BatteryHealth(named.Length > 0 ? named : "Battery", design, full,
            cycles is 0 or BATTERY_UNKNOWN_CAPACITY ? null : (int)cycles, (caps & BATTERY_CAPACITY_RELATIVE) != 0);
    }

    // What each device problem code means (Device Manager's own list), in a few words.
    public static string Meaning(int code) => code switch
    {
        1 => "not set up",
        3 => "its driver may be corrupted, or memory is low",
        10 => "can't start",
        12 => "not enough free resources for it",
        14 => "needs a restart to work",
        18 => "its driver needs reinstalling",
        19 => "its settings in the registry are damaged",
        21 => "Windows is removing it",
        28 => "no driver installed",
        29 => "turned off in the firmware (BIOS/UEFI)",
        31 => "not working properly",
        32 => "its driver is set not to start",
        37 => "its driver failed to start",
        38 => "an old copy of its driver is still loaded",
        39 => "its driver is missing or corrupted",
        40 => "its driver's registry entry is damaged",
        41 => "its driver loaded but the device can't be found",
        43 => "stopped after reporting a problem",
        48 => "its driver is blocked for known problems",
        52 => "its driver isn't signed",
        _ => $"problem code {code}",
    };

    // Codes that aren't problems to fix: 22 disabled by you, 24 not present or not all there (a
    // device unplugged), 45 not connected now.
    public static bool IsProblem(int code) => code is not (0 or 22 or 24 or 45);

    // What to do about one, by kind.
    public static string Advice(int code) => code switch
    {
        1 or 3 or 18 or 28 or 32 or 37 or 38 or 39 or 40 => "install its driver again: Settings › Windows Update › Advanced options › Optional updates, or its maker's site",
        10 or 31 or 41 or 43 => "unplug it and plug it back in, or restart; if it stays, update its driver",
        14 => "restart the PC",
        12 => "two devices want the same resources: Device Manager (devmgmt.msc) › the device › Resources",
        19 or 21 => "remove it in Device Manager (devmgmt.msc) and restart, so Windows finds it again",
        29 => "turn it on in the PC's firmware settings (BIOS/UEFI) if you need it",
        48 => "get a newer driver from its maker: Windows blocks this one",
        52 => "get a signed driver from its maker",
        _ => "Device Manager (devmgmt.msc) › the device › General says more",
    };

    // The report's section: disks, battery, devices, each line with what to do when it's not well.
    // Notes are the parts Windows didn't give this account.
    public static List<string> Report(IReadOnlyList<DiskHealth>? disks, IReadOnlyList<BatteryHealth> batteries,
        IReadOnlyList<DeviceProblem>? devices, IReadOnlyList<string> notes)
    {
        var lines = new List<string> { "Disks:" };
        if (disks == null) lines.Add("  couldn't be read");
        else if (disks.Count == 0) lines.Add("  none reported");
        foreach (var d in disks ?? [])
        {
            var (line, advice) = Disk(d);
            lines.Add($"  {(advice.Count > 0 ? "!" : " ")} {line}");
            foreach (var a in advice) lines.Add("      " + a);
        }

        if (batteries.Count > 0)
        {
            lines.Add("");
            lines.Add("Battery:");
            foreach (var b in batteries)
            {
                var (line, advice) = Battery(b);
                lines.Add($"  {(advice != null ? "!" : " ")} {line}");
                if (advice != null) lines.Add("      " + advice);
            }
        }

        lines.Add("");
        lines.Add("Devices with a problem:");
        if (devices == null) lines.Add("  couldn't be read");
        else if (devices.Count == 0) lines.Add("  none");
        foreach (var p in (devices ?? []).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"  ! {p.Name}: {Meaning(p.Code)}");
            lines.Add("      " + Advice(p.Code));
        }

        if (notes.Count > 0)
        {
            lines.Add("");
            foreach (var n in notes) lines.Add(n);
        }
        return lines;
    }

    // "Samsung SSD 980 1TB (NVMe SSD): Healthy, 3% worn, 41°C, 2,310 hours on", and what to do.
    public static (string Line, List<string> Advice) Disk(DiskHealth d)
    {
        var kind = string.Join(" ", new[] { d.Bus, d.Media }.Where(s => s.Length > 0));
        var facts = new List<string> { d.Health };
        var advice = new List<string>();
        if (d.Wear is { } w) facts.Add($"{w}% worn");
        if (d.Temperature is { } t and > 0) facts.Add($"{t}°C");
        if (d.PowerOnHours is { } h and > 0) facts.Add($"{h.ToString("N0", CultureInfo.InvariantCulture)} hours on");
        long errors = (d.ReadErrors ?? 0) + (d.WriteErrors ?? 0);
        if (errors > 0) facts.Add($"{errors.ToString("N0", CultureInfo.InvariantCulture)} uncorrected error{(errors == 1 ? "" : "s")}");

        if (d.Health == "Unhealthy") advice.Add("Windows expects this disk to fail: copy off what you need now, then replace it.");
        else if (d.Health == "Warning") advice.Add("Windows sees trouble on this disk: back it up, and check its maker's tool for what's wrong.");
        if (d.Wear is >= 90) advice.Add("It's near the writes it's rated for: back it up and plan a replacement.");
        else if (d.Wear is >= 70) advice.Add("Most of its rated writes are used: keep backups current.");
        if (errors > 0 && d.Health == "Healthy") advice.Add("It has lost data it couldn't correct: back it up, and run chkdsk /scan on it.");
        // The drive's own limit when it says one, else 70°C, where most SSDs start slowing themselves down.
        int hot = d.TemperatureMax is > 0 and < 120 ? d.TemperatureMax.Value : 70;
        if (d.Temperature is { } temp && temp >= hot) advice.Add("It's running hot: check its airflow (a laptop on a soft surface, a heatsink missing).");
        return ($"{d.Name}{(kind.Length > 0 ? $" ({kind})" : "")}: {string.Join(", ", facts)}", advice);
    }

    // "holds 78% of what it did new (42,100 of 54,000 mWh), 412 cycles", and advice when it's worn.
    public static (string Line, string? Advice) Battery(BatteryHealth b)
    {
        double held = (double)b.Full / b.Design;
        int percent = (int)Math.Round(Math.Min(held, 1.0) * 100);
        var line = $"{b.Name}: holds {percent}% of what it did new";
        if (!b.Relative) line += $" ({b.Full.ToString("N0", CultureInfo.InvariantCulture)} of {b.Design.ToString("N0", CultureInfo.InvariantCulture)} mWh)";
        if (b.Cycles is { } c) line += $", {c.ToString("N0", CultureInfo.InvariantCulture)} cycles";
        string? advice = percent switch
        {
            < 60 => "Worn out: it runs well short of its first hours. A replacement brings them back; powercfg /batteryreport has its history.",
            < 80 => "Worn: below 80% is where makers count a battery as worn. powercfg /batteryreport has its history.",
            _ => null,
        };
        return (line, advice);
    }
}
