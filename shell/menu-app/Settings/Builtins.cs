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

    // The policies bin/settings-admin.ps1 sets for look.ai-paint (the same three, fixed there).
    static readonly string[] PaintAi = ["DisableImageCreator", "DisableCocreator", "DisableGenerativeFill"];

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
        ["quick-look"] = new HookValue("quick-look", "on", ["on", "off"]),
        // RGB keyboards and mice in the theme's accent (KeyboardLights.cs, roadmap 8.9).
        ["keyboard-lights"] = new KeyboardLightsSwitch(),
        // Phase 8 (roadmap 8G-8J; their rules are BuiltinRules.cs, tested).
        ["disabled-hotkeys"] = new DisabledHotkeys(),
        ["startup-item"] = new StartupItem(),
        ["layout-hotkey"] = new LayoutHotkey(),
        ["folder-type-general"] = new FolderTypeGeneral(),
        ["case-sensitive"] = new CaseSensitive(),
        // Admin ones: read here without admin; written only by bin/settings-admin.ps1, in the one
        // prompt (the store queues them, so their Write is never reached).
        ["hibernate"] = new ReadOnly(_ => RegInt(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") switch
            { 1 => "on", 0 => "off", _ => null }),
        ["sudo"] = new ReadOnly(_ => RegInt(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo", "Enabled") switch
            { 0 => "off", 1 => "forceNewWindow", 2 => "disableInput", 3 => "normal", null => "off", _ => null }),
        ["windows-feature"] = new WindowsFeature(),
        ["service"] = new ReadOnly(e => ServiceStart.Switch(e, ServiceMode(e.How.Args.FirstOrDefault()))),
        ["ssh-agent"] = new ReadOnly(_ => ServiceMode("ssh-agent") switch { null => null, "auto" or "delayed" => "on", _ => "off" }),
        ["utf8"] = new ReadOnly(_ => RegString(@"SYSTEM\CurrentControlSet\Control\Nls\CodePage", "ACP") switch
            { null => null, "65001" => "on", _ => "off" }),
        ["ai-paint"] = new ReadOnly(_ => PaintAi.All(v => RegInt(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", v) == 1) ? "off" : "on"),
        // An action: nothing to read; the runtimes it's for are found by ResolveAdmin, below.
        ["firewall-dev"] = new ReadOnly(_ => null),
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

    static string? RegString(string path, string name)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    static readonly Regex ServiceName = new("^[A-Za-z0-9_.-]{1,80}$");

    // A service's start mode from its registry key, readable without admin; null: no such service.
    static string? ServiceMode(string? name) => name != null && ServiceName.IsMatch(name)
        ? ServiceStart.Mode(RegInt($@"SYSTEM\CurrentControlSet\Services\{name}", "Start"), RegInt($@"SYSTEM\CurrentControlSet\Services\{name}", "DelayedAutostart"))
        : null;

    // The store's ResolveAdmin (WinSettings): an admin change whose value is worked out here,
    // before it's queued. developer.firewall-dev: the dev runtimes on this account's PATH and in
    // mise's installs, since the elevated run may be another account that can see neither.
    public static (string? Value, string? Error) ResolveAdmin(SettingEntry e, string value)
    {
        if (e.How.Verb != "builtin" || e.How.Target != "firewall-dev") return (value, null);
        var found = DevRuntimes.Pick(RuntimeCandidates());
        return found.Count == 0
            ? (null, $"none of {string.Join(", ", DevRuntimes.Names)} found on your PATH or in mise's installs")
            : (DevRuntimes.Encode(found), null);
    }

    static List<string> RuntimeCandidates()
    {
        var found = new List<string>();
        void Add(string dir)
        {
            foreach (var name in DevRuntimes.Names)
                try
                {
                    var exe = Path.Combine(dir, name + ".exe");
                    if (File.Exists(exe)) found.Add(Path.GetFullPath(exe));
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { } // a broken PATH entry
        }
        // The PATH as it is now (yours, then the PC's), not as this app inherited it at sign-in:
        // a runtime installed since counts.
        var path = string.Join(';', Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine), Environment.GetEnvironmentVariable("Path"));
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Add(Environment.ExpandEnvironmentVariables(dir.Trim('"')));
        // mise puts shims on the PATH, which can't carry a rule: each installed version's program.
        var mise = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mise", "installs");
        foreach (var name in DevRuntimes.Names)
        {
            var tool = Path.Combine(mise, name);
            if (!Directory.Exists(tool)) continue;
            foreach (var version in Directory.EnumerateDirectories(tool))
            {
                Add(version);
                Add(Path.Combine(version, "bin"));
            }
        }
        return found;
    }

    // A small file in %LOCALAPPDATA%\launcher holding what a builtin changed, so turning it back
    // puts back exactly that (the undo history only knows on / off).
    static string StateFile(string name) => Path.Combine(MenuModel.StateDir, name);

    static void Remember(string name, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(MenuModel.StateDir);
        File.WriteAllLines(StateFile(name), lines);
    }

    static string[]? Remembered(string name) => File.Exists(StateFile(name)) ? File.ReadAllLines(StateFile(name)) : null;

    static void Forget(string name) { try { File.Delete(StateFile(name)); } catch (IOException) { } }

    // reg.exe / fsutil.exe: Windows' own tools, hidden; the first line of what they said when they fail.
    static string? RunTool(string exe, params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info);
        if (p == null) return $"{exe} didn't start";
        var output = p.StandardOutput.ReadToEndAsync();
        var errors = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode == 0) return null;
        var said = (errors + "\n" + output.Result).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return said ?? $"{exe} exited {p.ExitCode}";
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

    // look.keyboard-lights (8.9): omawin's own file; the lights follow (or let go) at once.
    sealed class KeyboardLightsSwitch : IBuiltin
    {
        public bool Fast => true;
        public string? Read(SettingEntry e) => KeyboardLights.On ? "on" : "off";
        public string? Write(SettingEntry e, string value)
        {
            KeyboardLights.Set(value == "on");
            return null;
        }
    }

    // input.win-hotkeys (8.26): Explorer's DisabledHotkeys, with the letters omawin binds as
    // plain Win+<key> in GlazeWM's config (the rendered copy, which has your account folder's keys
    // too; else the repo's template). Off adds ours and remembers what was there; on takes out
    // only what was added. Explorer reads it when it starts (restart-explorer).
    sealed class DisabledHotkeys : IBuiltin
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", Value = "DisabledHotkeys", State = "disabled-hotkeys";
        public bool Fast => true;

        static string Ours()
        {
            var rendered = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".glzr", "glazewm", "config.yaml");
            var file = File.Exists(rendered) ? rendered : Path.Combine(MenuModel.Repo, "config", "glazewm", "config.yaml");
            return File.Exists(file) ? WinHotkeys.Letters(File.ReadLines(file)) : "";
        }

        static string? Current()
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Value) as string;
        }

        public string? Read(SettingEntry e)
        {
            var ours = Ours();
            return ours.Length == 0 ? null : WinHotkeys.AllDisabled(Current(), ours) ? "off" : "on";
        }

        public string? Write(SettingEntry e, string value)
        {
            var ours = Ours();
            if (ours.Length == 0) return "GlazeWM's config binds no Win+letter keys";
            var current = Current();
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (value == "off")
            {
                var next = WinHotkeys.Disable(current, ours);
                // Line 1: the value before omawin's first change ("-": not set); line 2: what was added.
                var saved = Remembered(State);
                var added = new string([.. next.Where(c => !(current ?? "").Contains(c, StringComparison.OrdinalIgnoreCase))]);
                Remember(State, saved is [var before, var had, ..] ? [before, had + added] : [current ?? "-", added]);
                k.SetValue(Value, next, Microsoft.Win32.RegistryValueKind.String);
            }
            else
            {
                var saved = Remembered(State);
                string? before = saved is [var b, ..] && b != "-" ? b : null;
                // Ours now and ours then (the config may have changed since): neither stays unless it was there before.
                var left = WinHotkeys.Restore(current, ours + (saved is [_, var had, ..] ? had : ""), before);
                if (left == null) k.DeleteValue(Value, throwOnMissingValue: false);
                else k.SetValue(Value, left, Microsoft.Win32.RegistryValueKind.String);
                Forget(State);
            }
            return null;
        }
    }

    // system.startup-onedrive / -teams (8.28): an app's start at sign-in, switched the way Task
    // Manager does it, never removed. A Run entry: its StartupApproved bytes. A Store app (the new
    // Teams): its startup task's state. Not set up to start on this PC: no value.
    sealed class StartupItem : IBuiltin
    {
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Tasks = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
        static Microsoft.Win32.RegistryKey Hkcu => Microsoft.Win32.Registry.CurrentUser;
        public bool Fast => true;

        static string? RunValue(string app)
        {
            using var k = Hkcu.OpenSubKey(Run);
            return k == null ? null : StartupApproved.RunName(k.GetValueNames(), app);
        }

        // "<package family>\<task>" for each of the app's startup tasks.
        static List<string> TaskKeys(string app)
        {
            var list = new List<string>();
            using var root = Hkcu.OpenSubKey(Tasks);
            if (root == null) return list;
            foreach (var family in root.GetSubKeyNames().Where(f => StartupApproved.IsPackageOf(f, app)))
                using (var fk = root.OpenSubKey(family))
                    foreach (var task in fk?.GetSubKeyNames() ?? [])
                        using (var tk = fk!.OpenSubKey(task))
                            if (tk?.GetValue("State") is int) list.Add($@"{family}\{task}");
            return list;
        }

        static int? TaskState(string task)
        {
            using var k = Hkcu.OpenSubKey($@"{Tasks}\{task}");
            return k?.GetValue("State") as int?;
        }

        public string? Read(SettingEntry e)
        {
            var app = e.How.Args.FirstOrDefault() ?? "";
            if (RunValue(app) is { } name)
            {
                using var a = Hkcu.OpenSubKey(Approved);
                return StartupApproved.IsEnabled(a?.GetValue(name) as byte[]) ? "on" : "off";
            }
            var states = TaskKeys(app).Select(TaskState).OfType<int>().ToList();
            if (states.Count == 0) return null;
            return states.Any(s => StartupApproved.TaskEnabled(s) == true) ? "on" : "off";
        }

        public string? Write(SettingEntry e, string value)
        {
            var app = e.How.Args.FirstOrDefault() ?? "";
            if (RunValue(app) is { } name)
            {
                using var a = Hkcu.CreateSubKey(Approved);
                a.SetValue(name, value == "on" ? StartupApproved.Enabled() : StartupApproved.Disabled(DateTime.UtcNow), Microsoft.Win32.RegistryValueKind.Binary);
                return null;
            }
            var tasks = TaskKeys(app);
            if (tasks.Count == 0) return $"{app} isn't set to start with Windows on this PC";
            if (tasks.Any(t => TaskState(t) is int s && StartupApproved.ByPolicy(s))) return "your organization's policy decides this one";
            foreach (var t in tasks)
                using (var k = Hkcu.CreateSubKey($@"{Tasks}\{t}"))
                    k.SetValue("State", value == "on" ? StartupApproved.TaskOn : StartupApproved.TaskOffByUser, Microsoft.Win32.RegistryValueKind.DWord);
            return null;
        }
    }

    // input.layout-hotkey (8.29): Alt+Shift switching the keyboard layout. Off frees only what's
    // on Alt+Shift (Ctrl+Shift for layouts stays) and remembers what was there; on puts it back.
    // SPI_SETLANGTOGGLE has Windows read the values again, so it applies without signing out.
    sealed class LayoutHotkey : IBuiltin
    {
        const string Key = @"Keyboard Layout\Toggle", State = "layout-hotkey";
        const uint SPI_SETLANGTOGGLE = 0x005B;
        public bool Fast => true;

        static Dictionary<string, string?> Values()
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return LayoutHotkeys.Names.ToDictionary(n => n, n => k?.GetValue(n) as string);
        }

        public string? Read(SettingEntry e) => LayoutHotkeys.Read(Values());

        public unsafe string? Write(SettingEntry e, string value)
        {
            var now = Values();
            IReadOnlyDictionary<string, string?> next;
            if (value == "off")
            {
                if (Remembered(State) == null) Remember(State, LayoutHotkeys.Save(now));
                next = LayoutHotkeys.Off(now).ToDictionary(p => p.Key, p => (string?)p.Value);
            }
            else
            {
                next = LayoutHotkeys.On(Remembered(State) is { } lines ? LayoutHotkeys.Load(lines) : null);
                Forget(State);
            }
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
                foreach (var (name, v) in next)
                    if (v == null) k.DeleteValue(name, throwOnMissingValue: false);
                    else k.SetValue(name, v, Microsoft.Win32.RegistryValueKind.String);
            SettingsNative.SystemParametersInfo(SPI_SETLANGTOGGLE, 0, null, SettingsNative.SPIF_SENDCHANGE);
            return null;
        }
    }

    // explorer.folder-type (8.46): Explorer stops guessing each folder's type from its files (slow
    // on big folders) and treats every folder as general items. That only holds once the views it
    // saved per folder (Bags, BagMRU) are cleared, so they're exported first and cleared; turning
    // it back on imports them again. Explorer reads it when it starts (restart-explorer).
    sealed class FolderTypeGeneral : IBuiltin
    {
        const string Shell = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell";
        const string AllFolders = Shell + @"\Bags\AllFolders\Shell";
        static readonly string[] Saved = ["Bags", "BagMRU"];
        static string Backup(string part) => StateFile($"explorer-{part.ToLowerInvariant()}.reg");
        public bool Fast => true;

        public string? Read(SettingEntry e)
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AllFolders);
            return k?.GetValue("FolderType") as string == "NotSpecified" ? "off" : "on";
        }

        public string? Write(SettingEntry e, string value)
        {
            var hkcu = Microsoft.Win32.Registry.CurrentUser;
            if (value == "off")
            {
                Directory.CreateDirectory(MenuModel.StateDir);
                foreach (var part in Saved)
                {
                    using (var k = hkcu.OpenSubKey($@"{Shell}\{part}")) if (k == null) continue;
                    // A backup from an earlier time stays: it's the views from before omawin's change.
                    if (!File.Exists(Backup(part)) && RunTool("reg.exe", "export", $@"HKCU\{Shell}\{part}", Backup(part), "/y") is { } why)
                        return $"couldn't keep Explorer's saved views first ({why}); nothing changed";
                }
                foreach (var part in Saved) hkcu.DeleteSubKeyTree($@"{Shell}\{part}", throwOnMissingSubKey: false);
                using var all = hkcu.CreateSubKey(AllFolders);
                all.SetValue("FolderType", "NotSpecified", Microsoft.Win32.RegistryValueKind.String);
                return null;
            }
            using (var all = hkcu.OpenSubKey(AllFolders, writable: true)) all?.DeleteValue("FolderType", throwOnMissingValue: false);
            foreach (var part in Saved)
            {
                if (!File.Exists(Backup(part))) continue;
                if (RunTool("reg.exe", "import", Backup(part)) is { } why) return $"Explorer's saved views weren't put back ({why}); they're kept in {Backup(part)}";
                File.Delete(Backup(part));
            }
            return null;
        }
    }

    // developer.case-sensitive (8.40): one folder's names case-sensitive, as Linux tools expect
    // (fsutil, no admin for a folder you own; folders made in it later inherit it). The folder:
    // the one given (omawin setting set developer.case-sensitive <folder>), else one you pick.
    sealed class CaseSensitive : IBuiltin
    {
        public string? Read(SettingEntry e) => null;
        public string? Write(SettingEntry e, string value)
        {
            var folder = value.Trim().Trim('"');
            if (folder.Length == 0) folder = FolderPicker.Pick("Make a folder case-sensitive") ?? "";
            if (folder.Length == 0) return "no folder chosen";
            if (!Directory.Exists(folder)) return $"no folder {folder}";
            return RunTool("fsutil.exe", "file", "setCaseSensitiveInfo", Path.GetFullPath(folder), "enable") is { } why
                ? $"{folder}: {why}" : null;
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
