// The declarative handlers (plans/02-settings-and-one-search.md §4): reg, spi and powercfg, the
// verbs a catalog line can use without any code of its own. Each is microseconds, safe on any
// thread. After a write, SettingApply tells Windows so the change shows without signing out.
// No WinUI and nothing of the menu app's: menu-app.Tests compiles this file, so CI's
// round-trip (RoundTripTests) runs these very handlers on a Windows runner.

using Microsoft.Win32;

namespace SystemMenu;

// --- reg ---------------------------------------------------------------------------------------
// A line can join several values with " + " (Catalog.ParseLine): each is one of e.Parts, written
// together and read as one switch, off only when every one is off.
public sealed class RegHandler : ISettingHandler
{
    public string? Read(SettingEntry e)
    {
        if (e.Also.Count == 0) return ReadOne(e);
        var words = e.Parts.Select(ReadOne).ToList();
        return words.All(w => w == "off") ? "off" : words.Any(w => w == "on") ? "on" : null;
    }

    static string? ReadOne(SettingEntry e)
    {
        var (hive, path, name) = Catalog.SplitReg(e.How.Target);
        using var key = Root(hive).OpenSubKey(path);
        return SettingValues.ToWords(e, SettingValues.RawText(key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
    }

    public string? Write(SettingEntry e, string value)
    {
        // Never here: the store queues admin entries for the one elevated run (§8).
        if (e.Admin) return "needs admin";
        foreach (var part in e.Parts)
        {
            var (hive, path, name) = Catalog.SplitReg(part.How.Target);
            var raw = SettingValues.FromWords(part, value);
            using var key = Root(hive).CreateSubKey(path, writable: true);
            if (raw == "-") key.DeleteValue(name, throwOnMissingValue: false);
            else switch (part.How.Args[0])
            {
                case "dword": key.SetValue(name, SettingValues.Dword(raw), RegistryValueKind.DWord); break;
                case "qword": key.SetValue(name, SettingValues.Qword(raw), RegistryValueKind.QWord); break;
                case "expand_sz": key.SetValue(name, raw, RegistryValueKind.ExpandString); break;
                default: key.SetValue(name, raw, RegistryValueKind.String); break;
            }
        }
        SettingApply.After(e);
        return null;
    }

    public static RegistryKey Root(string hive) => hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

    // The values exactly as they were (or that they weren't there), for putting back what a test
    // changed: a value that didn't exist must not exist afterwards either.
    public sealed record Saved(string Target, object? Value, RegistryValueKind Kind, bool KeyExisted);
    public sealed record Snapshot(IReadOnlyList<Saved> Values);

    public static Snapshot Take(SettingEntry e) => new(e.Parts.Select(part =>
    {
        var (hive, path, name) = Catalog.SplitReg(part.How.Target);
        using var key = Root(hive).OpenSubKey(path);
        if (key == null) return new Saved(part.How.Target, null, RegistryValueKind.Unknown, false);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new Saved(part.How.Target, value, value == null ? RegistryValueKind.Unknown : key.GetValueKind(name), true);
    }).ToList());

    public static void Restore(SettingEntry e, Snapshot snapshot)
    {
        foreach (var s in snapshot.Values) Restore(s);
    }

    static void Restore(Saved s)
    {
        var (hive, path, name) = Catalog.SplitReg(s.Target);
        if (!s.KeyExisted)
        {
            // The key was made by the write: it goes again, if nothing else got into it since.
            bool onlyOurs;
            using (var made = Root(hive).OpenSubKey(path))
                onlyOurs = made == null || made.SubKeyCount == 0 && made.GetValueNames().All(v => v.Equals(name, StringComparison.OrdinalIgnoreCase));
            int cut = path.LastIndexOf('\\');
            if (onlyOurs && cut > 0)
            {
                using var parent = Root(hive).OpenSubKey(path[..cut], writable: true);
                parent?.DeleteSubKey(path[(cut + 1)..], throwOnMissingSubKey: false);
                return;
            }
        }
        using var key = Root(hive).CreateSubKey(path, writable: true);
        if (s.Value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, s.Value, s.Kind);
    }
}

// --- after a write: tell Windows, so the change shows without signing out ------------------------
public static class SettingApply
{
    public static void After(SettingEntry e)
    {
        // broadcast=<area>: WM_SETTINGCHANGE with that string ("ImmersiveColorSet" is what dark
        // mode, accent and transparency listeners look for; "Environment", "Policy", ...).
        if (e.How.Broadcast is { } area) Broadcast(area.Length == 0 || area == "-" ? null : area);
        if (e.How.Flags.Contains("refresh-explorer"))
        {
            // Explorer re-reads its Advanced view settings on a broadcast plus an association
            // change notification (what the Folder Options dialog causes).
            Broadcast(null);
            SettingsNative.SHChangeNotify(SettingsNative.SHCNE_ASSOCCHANGED, SettingsNative.SHCNF_IDLIST, 0, 0);
        }
        if (e.How.Flags.Contains("restart-explorer")) ExplorerRestart.Request();
    }

    // Off the calling thread: a hung top-level window costs up to the timeout, per window.
    public static void Broadcast(string? area) =>
        Task.Run(() => SettingsNative.SendMessageTimeout(SettingsNative.HWND_BROADCAST, SettingsNative.WM_SETTINGCHANGE, 0, area,
            SettingsNative.SMTO_ABORTIFHUNG, 1000, out _));
}

// Restarting Explorer takes the taskbar away for a second and closes folder windows, so it's
// never done on its own: a change that needs it marks it pending, the row says "Restart Explorer
// to apply", and the restart is a row of its own (or `omawin setting set … --now`).
public static class ExplorerRestart
{
    public static bool Pending { get; private set; }
    public static event Action? Changed;

    public static void Request() { if (Pending) return; Pending = true; Changed?.Invoke(); }

    public static void Now()
    {
        Pending = false;
        Changed?.Invoke();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            using (p) try { p.Kill(); } catch (Exception) { } // another user's, or already gone
        // Windows starts the shell again by itself (AutoRestartShell); if it didn't, start it.
        Task.Delay(2000).ContinueWith(_ =>
        {
            if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
        });
    }
}

// --- spi ---------------------------------------------------------------------------------------
// Named SystemParametersInfo pairs: each has its own parameter shape (a pointer, the number in
// uiParam, the number in pvParam), so raw action numbers in the catalog would be a trap.
public sealed unsafe class SpiHandler : ISettingHandler
{
    const uint Save = SettingsNative.SPIF_UPDATEINIFILE | SettingsNative.SPIF_SENDCHANGE; // SENDCHANGE broadcasts for us

    public static readonly string[] Names =
        ["animations", "mouse-accel", "pointer-speed", "key-repeat", "key-delay", "sticky-hotkey", "swap-buttons",
         "scroll-lines", "double-click", "drag-full-windows", "font-smoothing", "snap", "focus-follows-mouse",
         "menu-delay", "min-animate", "filter-hotkey", "toggle-hotkey"];

    public string? Read(SettingEntry e)
    {
        switch (e.How.Target)
        {
            case "animations": return Bool(0x1042);          // SPI_GETCLIENTAREAANIMATION
            case "drag-full-windows": return Bool(0x0026);   // SPI_GETDRAGFULLWINDOWS
            case "font-smoothing": return Bool(0x004A);      // SPI_GETFONTSMOOTHING
            case "snap": return Bool(0x0082);                // SPI_GETWINARRANGING
            case "focus-follows-mouse": return Bool(0x1000); // SPI_GETACTIVEWINDOWTRACKING
            case "mouse-accel":                              // SPI_GETMOUSE: [threshold1, threshold2, acceleration]
            { int* p = stackalloc int[3]; return SettingsNative.SystemParametersInfo(0x0003, 0, p, 0) ? (p[2] != 0 ? "on" : "off") : null; }
            case "pointer-speed": return Number(0x0070);     // SPI_GETMOUSESPEED: 1..20
            case "key-repeat": return Number(0x000A);        // SPI_GETKEYBOARDSPEED: 0..31
            case "key-delay": return Number(0x0016);         // SPI_GETKEYBOARDDELAY: 0..3
            case "scroll-lines": return Number(0x0068);      // SPI_GETWHEELSCROLLLINES
            case "menu-delay": return Number(0x006A);        // SPI_GETMENUSHOWDELAY: milliseconds
            case "double-click": return SettingsNative.GetDoubleClickTime().ToString();
            case "swap-buttons": return SettingsNative.GetSystemMetrics(23) != 0 ? "on" : "off"; // SM_SWAPBUTTON
            case "sticky-hotkey":                            // SPI_GETSTICKYKEYS, SKF_HOTKEYACTIVE
            { var sk = new StickyKeys { Size = 8 }; return SettingsNative.SystemParametersInfo(0x003A, 8, &sk, 0) ? ((sk.Flags & 0x4) != 0 ? "on" : "off") : null; }
            case "min-animate":                              // SPI_GETANIMATION: ANIMATIONINFO.iMinAnimate
            { var ai = new AnimationInfo { Size = 8 }; return SettingsNative.SystemParametersInfo(0x0048, 8, &ai, 0) ? (ai.MinAnimate != 0 ? "on" : "off") : null; }
            case "filter-hotkey":                            // SPI_GETFILTERKEYS, FKF_HOTKEYACTIVE
            { var fk = new FilterKeys { Size = 24 }; return SettingsNative.SystemParametersInfo(0x0032, 24, &fk, 0) ? ((fk.Flags & 0x4) != 0 ? "on" : "off") : null; }
            case "toggle-hotkey":                            // SPI_GETTOGGLEKEYS, TKF_HOTKEYACTIVE
            { var tk = new StickyKeys { Size = 8 }; return SettingsNative.SystemParametersInfo(0x0034, 8, &tk, 0) ? ((tk.Flags & 0x4) != 0 ? "on" : "off") : null; }
            default: return null;
        }
    }

    public string? Write(SettingEntry e, string value)
    {
        bool on = value == "on";
        uint b = on ? 1u : 0u;
        bool ok = e.How.Target switch
        {
            // These take the BOOL itself in pvParam.
            "animations" => SettingsNative.SystemParametersInfo(0x1043, 0, (void*)(nint)b, Save),
            "focus-follows-mouse" => SettingsNative.SystemParametersInfo(0x1001, 0, (void*)(nint)b, Save),
            // Documented as pvParam; older builds read uiParam. Both carry it.
            "snap" => SettingsNative.SystemParametersInfo(0x0083, b, (void*)(nint)b, Save),
            // These take it in uiParam.
            "drag-full-windows" => SettingsNative.SystemParametersInfo(0x0025, b, null, Save),
            "font-smoothing" => SettingsNative.SystemParametersInfo(0x004B, b, null, Save),
            "swap-buttons" => SettingsNative.SystemParametersInfo(0x0021, b, null, Save),
            "key-repeat" => SettingsNative.SystemParametersInfo(0x000B, (uint)Clamp(e, value), null, Save),
            "key-delay" => SettingsNative.SystemParametersInfo(0x0017, (uint)Clamp(e, value), null, Save),
            "scroll-lines" => SettingsNative.SystemParametersInfo(0x0069, (uint)Clamp(e, value), null, Save),
            "double-click" => SettingsNative.SystemParametersInfo(0x0020, (uint)Clamp(e, value), null, Save),
            "menu-delay" => SettingsNative.SystemParametersInfo(0x006B, (uint)Clamp(e, value), null, Save), // SPI_SETMENUSHOWDELAY
            // SETMOUSESPEED takes the number in pvParam, not a pointer to it.
            "pointer-speed" => SettingsNative.SystemParametersInfo(0x0071, 0, (void*)(nint)Clamp(e, value), Save),
            // Windows' own "on" is thresholds 6 and 10 with acceleration 1; off is all zero.
            "mouse-accel" => SetMouse(on ? [6, 10, 1] : [0, 0, 0]),
            "sticky-hotkey" => SetSticky(on),
            "min-animate" => SetMinAnimate(on),
            "filter-hotkey" => SetFilterHotkey(on),
            "toggle-hotkey" => SetToggleHotkey(on),
            _ => throw new InvalidOperationException($"no spi setting '{e.How.Target}'"),
        };
        return ok ? null : $"SystemParametersInfo failed ({System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})";
    }

    static string? Bool(uint action) { int v = 0; return SettingsNative.SystemParametersInfo(action, 0, &v, 0) ? (v != 0 ? "on" : "off") : null; }
    static string? Number(uint action) { uint v = 0; return SettingsNative.SystemParametersInfo(action, 0, &v, 0) ? v.ToString() : null; }

    static bool SetMouse(int[] values) { fixed (int* p = values) return SettingsNative.SystemParametersInfo(0x0004, 0, p, Save); }

    static bool SetSticky(bool on)
    {
        var sk = new StickyKeys { Size = 8 };
        if (!SettingsNative.SystemParametersInfo(0x003A, 8, &sk, 0)) return false;
        sk.Flags = on ? sk.Flags | 0x4 : sk.Flags & ~0x4u;
        return SettingsNative.SystemParametersInfo(0x003B, 8, &sk, Save);
    }

    // The accessibility structs: read the whole thing, flip only the hotkey flag, write it back, so
    // the feature's own settings (and whether it's on) stay as they were.
    static bool SetToggleHotkey(bool on)
    {
        var tk = new StickyKeys { Size = 8 }; // TOGGLEKEYS has the same shape: cbSize, dwFlags
        if (!SettingsNative.SystemParametersInfo(0x0034, 8, &tk, 0)) return false;   // SPI_GETTOGGLEKEYS
        tk.Flags = on ? tk.Flags | 0x4 : tk.Flags & ~0x4u;                           // TKF_HOTKEYACTIVE
        return SettingsNative.SystemParametersInfo(0x0035, 8, &tk, Save);             // SPI_SETTOGGLEKEYS
    }

    static bool SetFilterHotkey(bool on)
    {
        var fk = new FilterKeys { Size = 24 };
        if (!SettingsNative.SystemParametersInfo(0x0032, 24, &fk, 0)) return false;  // SPI_GETFILTERKEYS
        fk.Flags = on ? fk.Flags | 0x4 : fk.Flags & ~0x4u;                           // FKF_HOTKEYACTIVE
        return SettingsNative.SystemParametersInfo(0x0033, 24, &fk, Save);            // SPI_SETFILTERKEYS
    }

    static bool SetMinAnimate(bool on)
    {
        var ai = new AnimationInfo { Size = 8, MinAnimate = on ? 1 : 0 };
        return SettingsNative.SystemParametersInfo(0x0049, 8, &ai, Save);             // SPI_SETANIMATION
    }

    static int Clamp(SettingEntry e, string value) =>
        Math.Clamp(int.Parse(value), e.Range?.Min ?? 0, e.Range?.Max ?? int.MaxValue);

    struct StickyKeys { public uint Size, Flags; }
    struct FilterKeys { public uint Size, Flags, WaitMSec, DelayMSec, RepeatMSec, BounceMSec; }
    struct AnimationInfo { public uint Size; public int MinAnimate; }
}

// --- powercfg -----------------------------------------------------------------------------------
// The active power scheme's plugged-in (ac) or on-battery (dc) value, through powrprof (no
// powercfg.exe start). Standard users may change the active scheme's values; a managed PC's
// policy can lock them, and then the read-back says so.
public sealed unsafe class PowerHandler : ISettingHandler
{
    static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    static readonly Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    static readonly Guid SubNone = new("fea3413e-7e05-4911-9a71-700331f1c294"); // settings in no subgroup

    // Seconds: Windows stores minutes as seconds; the catalog speaks minutes (0: never).
    public static readonly Dictionary<string, (Guid Sub, Guid Setting, bool Seconds)> Names = new()
    {
        ["screen-off"] = (SubVideo, new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"), true),      // VIDEOIDLE
        ["sleep"] = (SubSleep, new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), true),           // STANDBYIDLE
        ["hibernate-after"] = (SubSleep, new("9d7815a6-7ee4-497e-8888-515a05f02364"), true), // HIBERNATEIDLE
        ["lid"] = (SubButtons, new("5ca83367-6e45-459f-a27b-476b1d01c936"), false),          // LIDACTION
        ["power-button"] = (SubButtons, new("7648efa3-dd9c-4e3e-b566-50f929386280"), false), // PBUTTONACTION
        ["wake-timers"] = (SubSleep, new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), false),    // RTCWAKE: 0 off, 1 on, 2 important only
        ["standby-network"] = (SubNone, new("f15576e8-98b7-4186-b944-eafa664402d9"), false), // CONNECTIVITYINSTANDBY: 0 off, 1 on, 2 managed by Windows
    };

    static bool Dc(SettingEntry e) => e.How.Args is ["dc", ..];
    static bool Both(SettingEntry e) => e.How.Args is ["both", ..];

    public string? Read(SettingEntry e)
    {
        if (!Names.TryGetValue(e.How.Target, out var n)) return null;
        Guid* scheme;
        if (SettingsNative.PowerGetActiveScheme(0, &scheme) != 0) return null;
        try
        {
            uint v; var sub = n.Sub; var setting = n.Setting;
            uint err = Dc(e) ? SettingsNative.PowerReadDCValueIndex(0, scheme, &sub, &setting, &v) : SettingsNative.PowerReadACValueIndex(0, scheme, &sub, &setting, &v);
            // A switch's on= / off= name its raw values, as reg's do (wake-timers: on=1 off=0).
            return err != 0 ? null : SettingValues.ToWords(e, n.Seconds ? (v / 60).ToString() : v.ToString());
        }
        finally { SettingsNative.LocalFree((nint)scheme); }
    }

    public string? Write(SettingEntry e, string value)
    {
        if (!Names.TryGetValue(e.How.Target, out var n)) return $"no power setting '{e.How.Target}'";
        uint v = uint.Parse(SettingValues.FromWords(e, value)) * (n.Seconds ? 60u : 1u);
        Guid* scheme;
        if (SettingsNative.PowerGetActiveScheme(0, &scheme) != 0) return "no active power scheme";
        try
        {
            var sub = n.Sub; var setting = n.Setting;
            uint err = 0;
            if (!Dc(e) || Both(e)) err = SettingsNative.PowerWriteACValueIndex(0, scheme, &sub, &setting, v);
            if (err == 0 && (Dc(e) || Both(e))) err = SettingsNative.PowerWriteDCValueIndex(0, scheme, &sub, &setting, v);
            // Writing the index changes the stored scheme; activating it again applies it now.
            if (err == 0) err = SettingsNative.PowerSetActiveScheme(0, scheme);
            return err == 0 ? null : $"power setting: error {err}";
        }
        finally { SettingsNative.LocalFree((nint)scheme); }
    }
}
