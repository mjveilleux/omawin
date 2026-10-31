// builtin <name>: the settings with no single registry value or SystemParametersInfo pair (plans/02-settings-and-one-search.md §5).
// Each reuses what Quick settings and the sound page already do (QuickSettings.cs, Audio.cs);
// the rest is here. Reads are off the UI thread (radios, WMI and WinRT block), except the few
// marked fast.

using System.Text.RegularExpressions;

namespace SystemMenu;

public sealed class BuiltinHandler : ISettingHandler
{
    interface IBuiltin
    {
        string? Read(SettingEntry e);
        string? Write(SettingEntry e, string value);
        IReadOnlyList<Choice>? Choices(SettingEntry e) => null;
        bool Fast => false;
    }

    static readonly Dictionary<string, IBuiltin> All = new()
    {
        ["wifi"] = new Radio(Windows.Devices.Radios.RadioKind.WiFi),
        ["bluetooth"] = new Radio(Windows.Devices.Radios.RadioKind.Bluetooth),
        ["airplane"] = new Airplane(),
        ["power-mode"] = new PowerMode(),
        ["brightness"] = new BrightnessLevel(),
        ["desktop-icons"] = new DesktopIcons(),
        ["refresh-rate"] = new RefreshRate(),
        ["hdr"] = new Hdr(),
        ["proxy"] = new Proxy(),
        ["hotspot"] = new Hotspot(),
        ["audio-output"] = new AudioDefault(input: false),
        ["audio-input"] = new AudioDefault(input: true),
        ["restart-explorer"] = new RestartExplorer(),
        ["classic-menu"] = new ClassicMenu(),
        // The shell's keyboard hook (KeyHook.cs): values it reads from launcher\keys\.
        ["caps-lock"] = new HookValue("caps-lock", "caps", CapsLockRemap.Modes),
        ["copilot-key"] = new HookValue("copilot-key", "windows", ["windows", "launcher"]),
        // Admin ones: read here without admin; written only by bin/settings-admin.ps1, in the one
        // prompt (the store queues them, so their Write is never reached).
        ["hibernate"] = new ReadOnly(_ => RegInt(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") switch
            { 1 => "on", 0 => "off", _ => null }),
        ["sudo"] = new ReadOnly(_ => RegInt(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo", "Enabled") switch
            { 0 => "off", 1 => "forceNewWindow", 2 => "disableInput", 3 => "normal", null => "off", _ => null }),
        ["windows-feature"] = new WindowsFeature(),
    };

    public static IEnumerable<string> Names => All.Keys;

    static IBuiltin Get(SettingEntry e) =>
        All.TryGetValue(e.How.Target, out var b) ? b : throw new InvalidOperationException($"no builtin '{e.How.Target}'");

    public string? Read(SettingEntry e) => Get(e).Read(e);
    // After a write, the line's flags as for reg (restart-explorer: the row says so, never a
    // surprise restart).
    public string? Write(SettingEntry e, string value)
    {
        var error = Get(e).Write(e, value);
        if (error == null) SettingApply.After(e);
        return error;
    }
    public IReadOnlyList<Choice> Choices(SettingEntry e) => Get(e).Choices(e) ?? e.Choices;
    public bool Fast(SettingEntry e) => Get(e).Fast;

    static int? RegInt(string path, string name)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) is int i ? i : null;
    }

    sealed class ReadOnly(Func<SettingEntry, string?> read) : IBuiltin
    {
        public string? Read(SettingEntry e) => read(e);
        public string? Write(SettingEntry e, string value) => "needs admin";
    }

    // Wi-Fi and Bluetooth: Quick settings' radio code. No radio of that kind: no value.
    sealed class Radio(Windows.Devices.Radios.RadioKind kind) : IBuiltin
    {
        public string? Read(SettingEntry e) =>
            QuickSettings.RadioList().Where(r => r.Kind == kind.ToString()).Select(r => r.State).FirstOrDefault() switch
            { "On" => "on", null => null, _ => "off" };
        public string? Write(SettingEntry e, string value) => Radios.Set(kind, value == "on");
    }

    // Airplane mode the way it can be done from outside Windows' own app (its flag has no public
    // API): every radio that's on goes off, and the list is kept, so turning it off brings back
    // just those.
    sealed class Airplane : IBuiltin
    {
        static string StateFile => Path.Combine(MenuModel.StateDir, "airplane");
        public string? Read(SettingEntry e) => File.Exists(StateFile) ? "on" : "off";
        public string? Write(SettingEntry e, string value)
        {
            var problems = new List<string>();
            if (value == "on")
            {
                var wasOn = QuickSettings.RadioList().Where(r => r.State == "On").Select(r => r.Kind).Distinct().ToList();
                Directory.CreateDirectory(MenuModel.StateDir);
                File.WriteAllLines(StateFile, wasOn);
                foreach (var k in wasOn)
                    if (Enum.TryParse<Windows.Devices.Radios.RadioKind>(k, out var kind) && Radios.Set(kind, false) is { } why) problems.Add($"{k}: {why}");
            }
            else if (File.Exists(StateFile))
            {
                foreach (var k in File.ReadAllLines(StateFile))
                    if (Enum.TryParse<Windows.Devices.Radios.RadioKind>(k, out var kind) && Radios.Set(kind, true) is { } why) problems.Add($"{k}: {why}");
                File.Delete(StateFile);
            }
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }
    }

    // The power mode slider (not the plan): Quick settings' overlay schemes.
    sealed class PowerMode : IBuiltin
    {
        static readonly Choice[] Modes =
        [
            new(Brightness.PowerEfficiency.ToString(), "Power efficiency"),
            new(Brightness.Balanced.ToString(), "Balanced"),
            new(Brightness.Performance.ToString(), "Best performance"),
        ];
        public IReadOnlyList<Choice>? Choices(SettingEntry e) => Modes;
        public string? Read(SettingEntry e) { var m = QuickSettings.PowerMode(); return m.Known ? m.Current.ToString() : null; }
        public string? Write(SettingEntry e, string value) =>
            Guid.TryParse(value, out var id) ? QuickSettings.SetPowerMode(id) : $"not a power mode: {value}";
    }

    // One slider for the first monitor with a brightness control; each monitor has its own in
    // Quick settings.
    sealed class BrightnessLevel : IBuiltin
    {
        public string? Read(SettingEntry e) => QuickSettings.Displays().FirstOrDefault(d => d.Writable)?.Current.ToString();
        public string? Write(SettingEntry e, string value) =>
            QuickSettings.Displays().FirstOrDefault(d => d.Writable) is { } d ? QuickSettings.SetBrightness(d, int.Parse(value)) : "no monitor with a brightness control";
    }

    // bin/desktop-icons.ps1, in C#: Explorer's own View › Show desktop icons (WM_COMMAND 0x7402 to
    // the desktop's view), and HideIcons for the next sign-in; the choice is remembered the same
    // way, so install's default doesn't hide them again.
    sealed class DesktopIcons : IBuiltin
    {
        public bool Fast => true;
        public string? Read(SettingEntry e) => Shown() switch
        {
            1 => "on",
            0 => "off",
            _ => Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced")?.GetValue("HideIcons") is 1 ? "off" : "on",
        };
        public string? Write(SettingEntry e, string value)
        {
            if (Shown() >= 0 && Read(e) != value) SettingsNative.SendMessage(View(), 0x0111, 0x7402, 0);
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                k.SetValue("HideIcons", value == "on" ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
            Directory.CreateDirectory(MenuModel.StateDir);
            File.WriteAllText(Path.Combine(MenuModel.StateDir, "desktop-icons"), value == "on" ? "shown" : "hidden");
            return null;
        }

        // The desktop's view: under Progman, or under a WorkerW once the wallpaper has changed.
        static nint View()
        {
            nint view = SettingsNative.FindWindowEx(SettingsNative.FindWindowEx(0, 0, "Progman", null), 0, "SHELLDLL_DefView", null);
            nint worker = 0;
            while (view == 0 && (worker = SettingsNative.FindWindowEx(0, worker, "WorkerW", null)) != 0)
                view = SettingsNative.FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
            return view;
        }

        // -1: no desktop (Explorer isn't running); 1 shown; 0 hidden.
        static int Shown()
        {
            var view = View();
            if (view == 0) return -1;
            var list = SettingsNative.FindWindowEx(view, 0, "SysListView32", null);
            return list != 0 && SettingsNative.IsWindowVisible(list) ? 1 : 0;
        }
    }

    // The main monitor's refresh rate, from the modes it offers at its current resolution.
    unsafe sealed class RefreshRate : IBuiltin
    {
        public bool Fast => true;
        public IReadOnlyList<Choice>? Choices(SettingEntry e)
        {
            var cur = Current();
            var rates = new SortedSet<uint>();
            var m = new SettingsNative.DEVMODE { dmSize = (ushort)sizeof(SettingsNative.DEVMODE) };
            for (int i = 0; SettingsNative.EnumDisplaySettings(null, i, &m); i++)
                if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmBitsPerPel == cur.dmBitsPerPel && m.dmDisplayFrequency > 1)
                    rates.Add(m.dmDisplayFrequency);
            return rates.Reverse().Select(r => new Choice(r.ToString(), $"{r} Hz")).ToList();
        }
        public string? Read(SettingEntry e) => Current().dmDisplayFrequency is var hz and > 1 ? hz.ToString() : null;
        public string? Write(SettingEntry e, string value)
        {
            var m = Current();
            m.dmDisplayFrequency = uint.Parse(value);
            m.dmFields = SettingsNative.DM_DISPLAYFREQUENCY;
            int r = SettingsNative.ChangeDisplaySettingsEx(null, &m, 0, SettingsNative.CDS_UPDATEREGISTRY, 0);
            return r == 0 ? null : $"the monitor refused {value} Hz ({r})";
        }
        static SettingsNative.DEVMODE Current()
        {
            var m = new SettingsNative.DEVMODE { dmSize = (ushort)sizeof(SettingsNative.DEVMODE) };
            SettingsNative.EnumDisplaySettings(null, SettingsNative.ENUM_CURRENT_SETTINGS, &m);
            return m;
        }
    }

    // HDR ("advanced colour") on every active display that supports it: QueryDisplayConfig's
    // paths, then DisplayConfigGet / SetDeviceInfo with the advanced colour packets. On when any
    // display that can has it on; no value when none can.
    unsafe sealed class Hdr : IBuiltin
    {
        public bool Fast => true;
        public string? Read(SettingEntry e)
        {
            var states = Targets().Select(Info).Where(i => (i.Value & 1) != 0).ToList();
            return states.Count == 0 ? null : states.Any(i => (i.Value & 2) != 0) ? "on" : "off";
        }
        public string? Write(SettingEntry e, string value)
        {
            var supported = Targets().Where(t => (Info(t).Value & 1) != 0).ToList();
            if (supported.Count == 0) return "no display here supports HDR";
            foreach (var (adapter, id) in supported)
            {
                var set = new SettingsNative.SetAdvancedColorState
                {
                    Header = new() { Type = SettingsNative.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE, Size = (uint)sizeof(SettingsNative.SetAdvancedColorState), Adapter = adapter, Id = id },
                    Value = value == "on" ? 1u : 0u,
                };
                if (SettingsNative.DisplayConfigSetDeviceInfo(&set) is var r and not 0) return $"the display refused it ({r})";
            }
            return null;
        }

        static SettingsNative.AdvancedColorInfo Info((SettingsNative.LUID Adapter, uint Id) target)
        {
            var info = new SettingsNative.AdvancedColorInfo
            {
                Header = new() { Type = SettingsNative.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, Size = (uint)sizeof(SettingsNative.AdvancedColorInfo), Adapter = target.Adapter, Id = target.Id },
            };
            return SettingsNative.DisplayConfigGetDeviceInfo(&info) == 0 ? info : default;
        }

        static List<(SettingsNative.LUID Adapter, uint Id)> Targets()
        {
            uint paths, modes;
            if (SettingsNative.GetDisplayConfigBufferSizes(SettingsNative.QDC_ONLY_ACTIVE_PATHS, &paths, &modes) != 0) return [];
            var pathArray = new SettingsNative.PathInfo[paths];
            var modeArray = new SettingsNative.ModeInfo[modes];
            fixed (SettingsNative.PathInfo* p = pathArray)
            fixed (SettingsNative.ModeInfo* m = modeArray)
                if (SettingsNative.QueryDisplayConfig(SettingsNative.QDC_ONLY_ACTIVE_PATHS, &paths, p, &modes, m, 0) != 0) return [];
            return pathArray.Take((int)paths).Select(x => (x.TargetAdapter, x.TargetId)).ToList();
        }
    }

    // The system proxy (WinINet's ProxyEnable), then the two notifications every WinINet
    // program (Edge and Chrome through it) listens for. With no proxy server set, turning it on
    // can only open the page where one is set.
    sealed class Proxy : IBuiltin
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        public bool Fast => true;
        public string? Read(SettingEntry e)
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("ProxyEnable") is 1 ? "on" : "off";
        }
        public string? Write(SettingEntry e, string value)
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (value == "on" && k.GetValue("ProxyServer") is not string { Length: > 0 })
            {
                QuickSettings.Open("ms-settings:network-proxy");
                return "no proxy server set: opened its page";
            }
            k.SetValue("ProxyEnable", value == "on" ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            SettingsNative.InternetSetOption(0, SettingsNative.INTERNET_OPTION_SETTINGS_CHANGED, 0, 0);
            SettingsNative.InternetSetOption(0, SettingsNative.INTERNET_OPTION_REFRESH, 0, 0);
            return null;
        }
    }

    // Sharing this PC's internet connection (NetworkOperatorTetheringManager over the current
    // connection profile).
    sealed class Hotspot : IBuiltin
    {
        static Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager? Manager()
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            return profile == null ? null : Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
        }
        public string? Read(SettingEntry e) => Manager()?.TetheringOperationalState switch
        {
            Windows.Networking.NetworkOperators.TetheringOperationalState.On => "on",
            Windows.Networking.NetworkOperators.TetheringOperationalState.Off => "off",
            _ => null,
        };
        public string? Write(SettingEntry e, string value)
        {
            var m = Manager();
            if (m == null) return "not connected to the internet";
            var r = (value == "on" ? m.StartTetheringAsync() : m.StopTetheringAsync()).GetAwaiter().GetResult();
            return r.Status == Windows.Networking.NetworkOperators.TetheringOperationStatus.Success ? null : $"{r.Status} {r.AdditionalErrorMessage}".Trim();
        }
    }

    // The default speaker or microphone: the sound page's device list and its "make default"
    // (all three roles, as Sound settings does).
    sealed class AudioDefault(bool input) : IBuiltin
    {
        IEnumerable<AudioDevice> Devices() => Audio.List().Where(d => d.Input == input);
        public IReadOnlyList<Choice>? Choices(SettingEntry e) => Devices().Select(d => new Choice(d.Id, d.Name)).ToList();
        public string? Read(SettingEntry e) => Devices().FirstOrDefault(d => d.Default)?.Id;
        public string? Write(SettingEntry e, string value)
        {
            if (Devices().All(d => d.Id != value)) return "that device isn't connected";
            Audio.MakeDefault(value);
            return null;
        }
    }

    sealed class RestartExplorer : IBuiltin
    {
        public bool Fast => true;
        public string? Read(SettingEntry e) => null;
        public string? Write(SettingEntry e, string value) { ExplorerRestart.Now(); return null; }
    }

    // The full right-click menu (roadmap 8.24), the same registration `omawin classic-menu`
    // (src/windows/Explorer/ClassicMenu.cs) makes: an empty per-account InprocServer32 for the
    // Windows 11 menu's COM class sends Explorer back to the full one. On: that key with an empty
    // default value. Off: only that key goes, then the class key if it's left empty, so a class
    // key something else put there stays. Explorer reads it when it starts (restart-explorer).
    sealed class ClassicMenu : IBuiltin
    {
        const string Clsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string Server = Clsid + @"\InprocServer32";

        public bool Fast => true;

        public string? Read(SettingEntry e)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Server);
            return key != null && key.GetValue("") is string { Length: 0 } ? "on" : "off";
        }

        public string? Write(SettingEntry e, string value)
        {
            var hkcu = Microsoft.Win32.Registry.CurrentUser;
            if (value == "on")
            {
                using var key = hkcu.CreateSubKey(Server);
                key.SetValue("", "", Microsoft.Win32.RegistryValueKind.String);
                return null;
            }
            hkcu.DeleteSubKey(Server, throwOnMissingSubKey: false);
            using (var clsid = hkcu.OpenSubKey(Clsid))
                if (clsid == null || clsid.SubKeyCount > 0 || clsid.ValueCount > 0) return null;
            hkcu.DeleteSubKey(Clsid, throwOnMissingSubKey: false);
            return null;
        }
    }

    // A choice the keyboard hook reads (KeyHook.Read): one word in a file of omawin's own, so
    // nothing of Windows' changes and the hook follows at once (it watches the folder).
    sealed class HookValue(string name, string otherwise, string[] allowed) : IBuiltin
    {
        public bool Fast => true;
        public string? Read(SettingEntry e) => KeyHook.Read(name, otherwise) is var v && allowed.Contains(v) ? v : otherwise;
        public string? Write(SettingEntry e, string value)
        {
            if (!allowed.Contains(value)) return $"not one of: {string.Join(", ", allowed)}";
            KeyHook.Write(name, value);
            return null;
        }
    }

    // An optional feature's state, read without admin through WMI (Win32_OptionalFeature:
    // InstallState 1 is on, 2 off; 3 means this edition doesn't have it).
    sealed class WindowsFeature : IBuiltin
    {
        static readonly Regex Name = new("^[A-Za-z0-9-]+$");
        public string? Read(SettingEntry e)
        {
            var name = e.How.Args.FirstOrDefault() ?? "";
            if (!Name.IsMatch(name)) return null;
            using var wmi = Wmi.Connect(@"root\cimv2");
            foreach (var feature in wmi.Query($"SELECT InstallState FROM Win32_OptionalFeature WHERE Name = '{name}'"))
                using (feature)
                    return feature["InstallState"] switch { 1 => "on", 2 => "off", _ => null };
            return null;
        }
        public string? Write(SettingEntry e, string value) => "needs admin";
    }
}

// The other two verbs: a page to open, or one of omawin's commands.
public sealed class LinkHandler : ISettingHandler
{
    public string? Read(SettingEntry e) => null;
    public string? Write(SettingEntry e, string value)
    {
        var target = e.How.Target;
        if (Launcher.Link.IsMatch(target)) { QuickSettings.Open(target); return null; }
        // "rundll32.exe sysdm.cpl,EditEnvironmentVariables": a program and its arguments.
        var (exe, args) = target.Split(' ', 2) is [var x, var a] ? (x, a) : (target, "");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true })?.Dispose();
        return null;
    }
}

public sealed class OmawinHandler : ISettingHandler
{
    public string? Read(SettingEntry e) => null;
    public string? Write(SettingEntry e, string value) { MenuModel.Omawin([.. Catalog.Tokens(e.How.Target)]); return null; }
}
