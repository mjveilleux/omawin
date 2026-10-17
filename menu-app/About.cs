using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SystemMenu;

// The About page's facts (`omawin about`, System menu › About), laid out like Omarchy's
// fastfetch: Hardware, Software, then Age · Uptime · Update. Read on a worker thread: git and a
// few process lookups take tens of ms. The last answer is kept, so a second showing is instant
// while it's read again. No WMI anywhere: it takes a second to start; the registry and a few
// Win32 calls answer the same questions at once.
public static class About
{
    public const string Hardware = "Hardware", Software = "Software", Age = "Age · Uptime · Update";

    // Dots: the row shows the theme's colors after its value.
    public sealed record Fact(string Section, string Key, string Value, int Glyph, bool Dots = false);

    static List<Fact>? last;
    public static List<Fact>? Last => last;

    public static Task<List<Fact>> LoadAsync() => Task.Run(() => last = Read());

    static List<Fact> Read()
    {
        var facts = new List<Fact>();
        // git is the slow part (~0.1 s a call): ask it while the rest is read.
        var version = Task.Run(() => Safe(Version));
        var updated = Task.Run(() => Safe(Updated));
        string section = Hardware;
        void Add(string key, Func<string?> read, int glyph, bool dots = false)
        {
            var value = Safe(read);
            if (!string.IsNullOrWhiteSpace(value)) facts.Add(new(section, key, value.Trim(), glyph, dots));
        }

        Add("PC", Pc, HasBattery() ? 0xF0322 : 0xF01C5);
        Add("CPU", Cpu, 0xF0EE0);
        Add("GPU", Gpu, 0xF08AE);
        Add("Display", Displays, 0xF0379);
        Add("Disk (C:)", Disk, 0xF02CA);
        Add("Memory", Memory, 0xF035B);
        Add("Swap", Swap, 0xF04E2);

        section = Software;
        Add("omawin", () => version.Result, 0xF02A2);
        Add("Windows", WindowsVersion, 0xF05B3);
        Add("WM", () => Running("glazewm", "GlazeWM"), 0xF0570);
        Add("Bar", () => Running("zebar", "Zebar"), 0xF10A9);
        Add("Shell", MenuApp, 0xF0614);
        Add("Terminal", () => Running("noctty", "noctty"), 0xF018D);
        Add("Packages", Packages, 0xF03D7);
        Add("Theme", () => MenuModel.LoadTheme(MenuModel.CurrentThemeName()).Name, 0xF03D8, dots: true);
        Add("Font", Font, 0xF06D6);
        Add("Stay awake", () => StayAwake.On ? "on · the PC won't sleep" : "off · the PC sleeps as usual", 0xF0176);

        section = Age;
        Add("Windows age", WindowsAge, 0xF00EB);
        Add("Uptime", Uptime, 0xF0150);
        Add("Updated", () => updated.Result, 0xF06B0);
        return facts;
    }

    static string? Safe(Func<string?> read)
    {
        try { return read(); } catch { return null; }
    }

    // --- hardware ---------------------------------------------------------------------------
    // The maker and model, from the firmware's tables as Windows keeps them (what WMI's
    // Win32_ComputerSystem reports too). Built PCs leave "To be filled by O.E.M." there; the
    // motherboard's name stands in then.
    static string? Pc()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        if (key == null) return null;
        static bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && !s.Contains("O.E.M", StringComparison.OrdinalIgnoreCase)
            && !s.Contains("System Product", StringComparison.OrdinalIgnoreCase) && !s.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase)
            && !s.Contains("Default string", StringComparison.OrdinalIgnoreCase);
        var maker = key.GetValue("SystemManufacturer") as string;
        var model = key.GetValue("SystemProductName") as string;
        if (!Real(model)) { maker = key.GetValue("BaseBoardManufacturer") as string; model = key.GetValue("BaseBoardProduct") as string; }
        if (!Real(model)) return null;
        maker = Real(maker) ? maker!.Trim() : "";
        model = model!.Trim();
        // Lenovo's model is a machine type ("82XM"); the name people know is its family.
        if (!model.Contains(' ') && key.GetValue("SystemFamily") is string family && Real(family) && family.Trim().Contains(' '))
            model = $"{family.Trim()} ({model})";
        // "HP" + "HP Laptop 15-ef2xxx", "LENOVO" + "20XW0026US": the maker once, in its own case.
        var first = maker.Split(' ', ',')[0];
        return first.Length == 0 || model.StartsWith(first, StringComparison.OrdinalIgnoreCase) ? model : $"{Title(maker)} {model}";
    }

    static string Title(string s) => s.Length > 3 && s == s.ToUpperInvariant() ? s[0] + s[1..].ToLowerInvariant() : s;

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS { public byte AcLine, BatteryFlag, BatteryPercent, SystemStatus; public int LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    static bool HasBattery() => GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;

    static string? Cpu()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var name = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        if (name == null) return null;
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ");
        var mhz = key?.GetValue("~MHz") is int m && m > 0 ? $" @ {m / 1000.0:F2} GHz" : "";
        return $"{name} · {Environment.ProcessorCount} threads{mhz}";
    }

    // Display adapters from the registry, without Windows' own stand-ins (Basic Display,
    // remote desktop).
    static string? Gpu()
    {
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls == null) return null;
        var names = new List<string>();
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is string d && !d.Contains("Basic Display") && !d.Contains("Remote") && !names.Contains(d)) names.Add(d);
            }
            catch { } // some adapters' keys are admin-only
        }
        return names.Count > 0 ? string.Join(" · ", names) : null;
    }

    // Each screen: its mode, its scale and how often it refreshes, the main one first.
    delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX { public int Size; public RECT Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);

    static string? Displays()
    {
        var screens = new List<(bool Main, string Text)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var mode = new DEVMODE { Size = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(info.Device, -1, ref mode)) return true; // ENUM_CURRENT_SETTINGS
            var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? $" @ {dpi / 96.0:0.##}x" : "";
            var hz = mode.DisplayFrequency > 1 ? $", {mode.DisplayFrequency} Hz" : "";
            screens.Add(((info.Flags & 1) != 0, $"{mode.PelsWidth}x{mode.PelsHeight}{scale}{hz}"));
            return true;
        }, IntPtr.Zero);
        return screens.Count > 0 ? string.Join(" · ", screens.OrderByDescending(s => s.Main).Select(s => s.Text)) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    const double GB = 1 << 30;

    static string? Memory()
    {
        var m = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return null;
        return $"{(m.Total - m.Avail) / GB:F1} GB of {m.Total / GB:F1} GB in use ({m.Load}%)";
    }

    // The page files, how big and how full (SystemPageFileInformation: sizes in pages).
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returned);

    static string? Swap()
    {
        const int size = 4096;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQuerySystemInformation(18, buffer, size, out var returned) != 0) return null;
            if (returned == 0) return "no page file";
            long total = 0, used = 0;
            for (int at = 0; at + 16 <= returned;)
            {
                total += (uint)Marshal.ReadInt32(buffer, at + 4);
                used += (uint)Marshal.ReadInt32(buffer, at + 8);
                int next = Marshal.ReadInt32(buffer, at);
                if (next <= 0) break;
                at += next;
            }
            double page = Environment.SystemPageSize;
            return total == 0 ? "no page file" : $"{used * page / GB:F1} GB of {total * page / GB:F1} GB in use ({used * 100 / total}%) · page file";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    static string? Disk()
    {
        var d = new DriveInfo("C");
        long used = d.TotalSize - d.AvailableFreeSpace;
        return $"{used / GB:F0} GB of {d.TotalSize / GB:F0} GB used ({used * 100 / d.TotalSize}%) · {d.AvailableFreeSpace / GB:F0} GB free · {d.DriveFormat}";
    }

    // --- software ---------------------------------------------------------------------------
    // The repo's release tag (v*) or commit, and its branch. Only release tags count: other
    // tags (checkpoints) would read as versions.
    static string? Version()
    {
        var describe = Git("describe", "--tags", "--match", "v*", "--always", "--dirty");
        if (describe == null) return null;
        var branch = Git("rev-parse", "--abbrev-ref", "HEAD");
        return branch != null && branch != "HEAD" ? $"{describe} · {branch}" : describe;
    }

    static string? Git(params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(MenuModel.Repo);
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info);
        if (p == null) return null;
        var output = p.StandardOutput.ReadToEnd().Trim();
        if (!p.WaitForExit(3000) || p.ExitCode != 0) return null;
        return output.Length > 0 ? output : null;
    }

    static string? WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key == null) return null;
        var name = key.GetValue("ProductName") as string ?? "Windows";
        var build = key.GetValue("CurrentBuild") as string ?? "";
        // Windows 11 still says "Windows 10" here; the build tells them apart.
        if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
        var display = key.GetValue("DisplayVersion") as string;
        var ubr = key.GetValue("UBR") is int u ? $".{u}" : "";
        return $"{name}{(display != null ? " " + display : "")} · build {build}{ubr} · {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
    }

    // A running program, by name, and its version, read from its exe (it's running, so where
    // it is is known).
    static string Running(string process, string name)
    {
        var ps = Process.GetProcessesByName(process);
        try
        {
            foreach (var p in ps)
            {
                var file = ImagePath(p.Id);
                if (file == null) continue;
                var v = FileVersionInfo.GetVersionInfo(file);
                var version = (v.ProductVersion ?? v.FileVersion ?? "").Split('+')[0].Trim();
                return version.Length > 0 && version != "0.0.0" ? $"{name} {version} · running" : $"{name} · running";
            }
            return ps.Length > 0 ? $"{name} · running" : $"{name} · not running";
        }
        finally { foreach (var p in ps) p.Dispose(); }
    }

    // Where a process's exe is; asks only for limited information, which works for elevated
    // ones too (GlazeWM can be), where Process.MainModule is refused.
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

    static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var name = new System.Text.StringBuilder(1024);
            int size = name.Capacity;
            return QueryFullProcessImageName(h, 0, name, ref size) ? name.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // This app: the launcher and system menu, on .NET.
    static string MenuApp()
    {
        using var me = Process.GetCurrentProcess();
        return $"omawin menu · .NET {Environment.Version.ToString(3)} · {me.WorkingSet64 / (1 << 20)} MB · up {Short(DateTime.Now - me.StartTime)}";
    }

    // What's installed, by source, from the list the menu keeps for its Installed tags
    // (installed.txt, "winget:<id>" a line; rebuilt in the background by `winget export`).
    static string? Packages()
    {
        if (!File.Exists(MenuModel.InstalledFile)) return null;
        var counts = File.ReadLines(MenuModel.InstalledFile).Select(l => l.Trim()).Where(l => l.Contains(':'))
            .GroupBy(l => l[..l.IndexOf(':')]).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}").ToList();
        return counts.Count > 0 ? string.Join(" · ", counts) : null;
    }

    // The terminal's font (noctty's config; the last setting wins, as in Ghostty).
    static string? Font()
    {
        var file = Path.Combine(MenuModel.Repo, "noctty", "config.ghostty");
        if (!File.Exists(file)) return null;
        string? family = null, size = null;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || !line.Contains('=')) continue;
            var key = line[..line.IndexOf('=')].Trim();
            var value = line[(line.IndexOf('=') + 1)..].Trim().Trim('"');
            if (key == "font-family" && value.Length > 0) family = value;
            else if (key == "font-size") size = value;
        }
        return family == null ? null : size != null ? $"{family} ({size}pt)" : family;
    }

    // --- age, uptime, update ----------------------------------------------------------------
    // When Windows was installed (a feature update reinstalls it, and resets this).
    static string? WindowsAge()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key?.GetValue("InstallDate") is not int seconds) return null;
        var installed = DateTimeOffset.FromUnixTimeSeconds((uint)seconds).LocalDateTime;
        int days = (int)(DateTime.Now - installed).TotalDays;
        return $"{days} {(days == 1 ? "day" : "days")} · since {installed:MMMM d, yyyy}";
    }

    static string Uptime() => Long(TimeSpan.FromMilliseconds(Environment.TickCount64));

    // omawin's last update: when the checkout's newest commit was made (`omawin update` pulls
    // it; there's no separate stamp).
    static string? Updated()
    {
        var iso = Git("log", "-1", "--format=%cI");
        if (iso == null || !DateTimeOffset.TryParse(iso, out var when)) return null;
        var local = when.LocalDateTime;
        return $"{local:dddd, MMMM d yyyy} at {local:HH:mm} · last commit, {Short(DateTime.Now - local)} ago";
    }

    static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";

    // "2 days, 18 hours, 31 mins", as fastfetch says it.
    static string Long(TimeSpan t)
    {
        var parts = new List<string>();
        if (t.TotalDays >= 1) parts.Add(Plural((int)t.TotalDays, "day"));
        if (t.Hours > 0) parts.Add(Plural(t.Hours, "hour"));
        if (t.TotalDays < 1 || t.Minutes > 0) parts.Add(Plural(t.Minutes, "min"));
        return string.Join(", ", parts);
    }

    static string Short(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";
}
