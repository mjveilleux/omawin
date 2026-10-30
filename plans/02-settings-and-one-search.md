# Plan 02: every Windows setting (1B) and One Search (1C), the code

> **Uncompiled sketch.** Everything below was written in a cloud session that can't run Windows (2026-10-30). It's C# 12 for the menu app (.NET 9, Native AOT-safe: no reflection, `LibraryImport` source-generated P/Invoke, `System.Text.Json` only through `Json.cs`'s generated context), C# 5 for `bin/omawin.cs`, Windows PowerShell 5.1 for migrations. Names are suggestions. Registry paths and values marked **(check)** are from memory and must be confirmed on the PC before their catalog line ships; the rest are long-stable and widely documented, but CI's round-trip (§17) is still what proves them.

The why is in [roadmap.md](roadmap.md) (Phase 1B, 1C, 2.9) and [one-search.md](one-search.md). This file is the how, for someone who has lost all other context. one-search.md §1-§5 already has the result model, `ISearchSource`, `QueryParser`, `Merger` and the bar's key handler: they are referenced here, not repeated; §12 lists the corrections they need against the real code.

## 0. Files

| File | What |
|---|---|
| `default/launcher/settings.txt` | the catalog (layered like `items.txt`: repo, shared folder, account folder) |
| `default/launcher/settings-pages.txt` | the fallback list of Settings pages and classic consoles (§10) |
| `shell/menu-app/Settings/Catalog.cs` | parses the catalog; pure C#, no Windows calls (unit-tested) |
| `shell/menu-app/Settings/Native.cs` | every P/Invoke the settings code needs, `LibraryImport` |
| `shell/menu-app/Settings/Switches.cs` | the `reg`, `spi`, `powercfg`, `link`, `omawin` handlers |
| `shell/menu-app/Settings/Builtins.cs` | `builtin <name>`: radios, display modes, HDR, proxy, desktop icons, … (reuses `QuickSettings.cs`, `Audio.cs`) |
| `shell/menu-app/Settings/WinSettings.cs` | the service: cache, set / toggle, undo history, admin queue, `omawin setting` / `settings` |
| `shell/menu-app/Settings/Watch.cs` | `RegNotifyChangeKeyValue` and `WM_SETTINGCHANGE` watching |
| `shell/menu-app/Settings/Index.cs` | Windows' own settings index (`AllSystemSettings_*.xml`) and the fallback pages |
| `shell/menu-app/Reply.cs` | the answer channel for `omawin setting get` (the request channel is one-way today) |
| `shell/menu-app/Search/*.cs` | one-search.md's model, parser, merger (pure) and the sources (§11) |
| `bin/settings-admin.ps1` | the one elevated batch for shielded settings (PowerShell 5.1, fixed verbs) |
| `test/menu-app.Tests/` | xUnit tests (a sibling folder, so `SystemMenu.csproj`'s `**/*.cs` glob doesn't pick them up) |
| `.github/workflows/test.yml` | unit tests + the settings round-trip on a Windows runner |
| `migrations/<date>-retire-win-alt-space.ps1` | the second release of the Win+Alt+Space retirement (§15) |

The menu app's namespace stays `SystemMenu` for everything (as today). The service is `WinSettings`, not `Settings`, so it doesn't collide with the folder name or with `Windows.Storage` types; one-search.md's `Settings.UndoLast()` becomes `WinSettings.UndoLast()`.

---

## 1. The catalog: `default/launcher/settings.txt`

One setting per line, `|`-separated, seven fields (the roadmap's): `id | icon | name | keywords | kind | how | notes`.

- **id**: `<area>.<name>`. The area is the part before the first dot and is one of `look explorer privacy input system display connections sound developer` (the *Windows* submenu's sections, 1B.3). Ids are forever: they're in history files, `settings.txt` of every account, and people's key bindings.
- **icon**: a Nerd Font glyph, or `-` for the area's own (a table in `Catalog.Areas`). Preserve glyphs when editing (AGENTS.md).
- **keywords**: space-separated, what people type that isn't in the name ("ext" for file extensions, "hz" for refresh rate).
- **kind**: `switch` (on / off), `choice` (one of `values=`), `slider` (an integer in `range=`), `action` (do it once), `link` (open a page).
- **how**: a verb, its target, then `key=value` options and bare flags. Tokens are split on spaces; double quotes group (`"HKCU\Control Panel\Mouse\MouseSpeed"`, `values="1:This PC,2:Home"`).
  - `reg <HIVE\key path\value name> <dword|qword|sz|expand_sz> on=<raw> off=<raw> [values=...] [range=a..b] [default=<raw>] [broadcast=<area>] [refresh-explorer] [restart-explorer]`. `off=-` means "delete the value" (Windows' default when absent). The value name is the last path segment.
  - `spi <name>`: a named `SystemParametersInfo` pair from the table in `Switches.cs` (each has its own parameter shape, so raw action numbers in the file would be a trap).
  - `powercfg <name> [ac|dc|both]`: a named power setting (subgroup + setting GUID table in `Switches.cs`); slider values in minutes.
  - `builtin <name> [args]`: code in `Builtins.cs`.
  - `link <uri or command>`: `ms-settings:…`, a `.msc`, `rundll32 …`.
  - `omawin <command>`: one of omawin's commands, for `action` rows.
- **notes**: space-separated: `admin` (machine-wide, shielded, batched: §8), `signout` / `restart` (when it takes effect, shown in the row), `minBuild=<n>` / `maxBuild=<n>` (Windows build range it's verified on; hidden elsewhere), `check` (not yet verified on a PC: hidden unless `OMAWIN_SETTINGS_UNVERIFIED=1`, so an unchecked line can be merged without reaching users).

Values are strings everywhere above the handlers: a switch is `on` / `off`, a choice is its value key (`1`), a slider its number (`20`). Handlers map those to raw values.

### Example lines (the first ~45; the full ~80 grow from here)

```text
# Windows settings omawin reads and changes itself (plans/02-settings-and-one-search.md §1).
#   id | icon | name | keywords | kind | how | notes
# Layered: the shared folder's and this account's launcher/settings.txt add lines or replace one
# by id. "check" in notes: not yet verified on a PC, hidden from users.

# --- look -------------------------------------------------------------------------------------
look.dark-apps | - | Dark mode for apps | dark theme black night light | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme dword on=0 off=1 broadcast=ImmersiveColorSet |
look.dark-windows | - | Dark mode for Windows (taskbar, Start) | dark theme taskbar start | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme dword on=0 off=1 broadcast=ImmersiveColorSet |
look.transparency | - | Transparency effects | blur acrylic mica glass | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency dword on=1 off=0 broadcast=ImmersiveColorSet |
look.animations | - | Animation effects | motion reduce animate | switch | spi animations |
look.accent-taskbar | - | Accent colour on Start and taskbar | color colour accent | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\ColorPrevalence dword on=1 off=0 broadcast=ImmersiveColorSet |
look.accent-titlebars | - | Accent colour on title bars | color colour accent window border | switch | reg HKCU\Software\Microsoft\Windows\DWM\ColorPrevalence dword on=1 off=0 broadcast=ImmersiveColorSet |
look.desktop-icons | - | Desktop icons | desktop icons show hide | switch | builtin desktop-icons |

# --- explorer ---------------------------------------------------------------------------------
explorer.extensions | - | File name extensions | ext extension .txt suffix | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt dword on=0 off=1 refresh-explorer |
explorer.hidden | - | Hidden files and folders | hidden dotfiles show | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden dword on=1 off=2 refresh-explorer |
explorer.system-files | - | Protected operating system files | superhidden system protected | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowSuperHidden dword on=1 off=0 refresh-explorer |
explorer.full-path | - | Full path in the title bar | path title address | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState\FullPath dword on=1 off=0 refresh-explorer |
explorer.compact | - | Compact view | density spacing compact | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\UseCompactMode dword on=1 off=0 refresh-explorer | minBuild=22000
explorer.open-to | - | File Explorer opens to | home this pc quick access start | choice | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\LaunchTo dword values="1:This PC,2:Home,3:Downloads" default=2 |
explorer.end-task | - | End task on the taskbar's menu | kill end task taskbar right-click | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask dword on=1 off=0 | minBuild=22631 check
explorer.clock-seconds | - | Seconds on the taskbar clock | clock time seconds | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowSecondsInSystemClock dword on=1 off=0 restart-explorer | minBuild=22621 check

# --- privacy ----------------------------------------------------------------------------------
privacy.camera | - | Camera for apps | webcam camera permission | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\Value sz on=Allow off=Deny |
privacy.microphone | - | Microphone for apps | mic microphone permission | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\Value sz on=Allow off=Deny |
privacy.location | - | Location for apps | gps location permission | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location\Value sz on=Allow off=Deny | check
privacy.ad-id | - | Advertising ID | ads tracking advertising | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled dword on=1 off=0 |
privacy.tailored | - | Tailored experiences | diagnostic tailored tips | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy\TailoredExperiencesWithDiagnosticDataEnabled dword on=1 off=0 |
privacy.online-speech | - | Online speech recognition | speech voice cloud | switch | reg HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy\HasAccepted dword on=1 off=0 | check

# --- input ------------------------------------------------------------------------------------
input.mouse-accel | - | Enhance pointer precision (mouse acceleration) | acceleration accel mouse | switch | spi mouse-accel |
input.pointer-speed | - | Pointer speed | mouse sensitivity cursor | slider | spi pointer-speed range=1..20 default=10 |
input.key-repeat | - | Key repeat rate | keyboard repeat rate | slider | spi key-repeat range=0..31 default=31 |
input.key-delay | - | Key repeat delay | keyboard delay | choice | spi key-delay values="0:Shortest,1:Short,2:Long,3:Longest" default=1 |
input.sticky-shortcut | - | Sticky keys shortcut (Shift five times) | sticky shift accessibility | switch | spi sticky-hotkey |
input.tap-to-click | - | Touchpad: tap to click | touchpad tap click | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\TapsEnabled dword on=4294967295 off=0 | check
input.scroll-direction | - | Touchpad: down motion scrolls down | natural scroll reverse touchpad | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\ScrollDirection dword on=4294967295 off=0 | check

# --- system -----------------------------------------------------------------------------------
system.clipboard-history | - | Windows' clipboard history (Win+V) | clipboard history paste | switch | reg HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory dword on=1 off=0 |
system.notifications | - | Notifications | toast banners notify | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications\ToastEnabled dword on=1 off=0 | check
system.game-mode | - | Game mode | gaming game mode | switch | reg HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled dword on=1 off=0 |
system.game-bar | - | Game bar captures | xbox game bar dvr record | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled dword on=1 off=0 | check
system.startup-delay | - | No delay for startup apps | startup delay login boot | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize\StartupDelayInMSec dword on=0 off=- | check
system.power-mode | - | Power mode | battery performance efficiency balanced | choice | builtin power-mode |
system.screen-off | - | Turn off the screen after (minutes, plugged in) | display timeout screen off | slider | powercfg screen-off ac range=0..240 |
system.sleep | - | Sleep after (minutes, plugged in) | sleep standby timeout | slider | powercfg sleep ac range=0..240 |
system.lid | - | Closing the lid (on battery) | lid laptop close | choice | powercfg lid dc values="0:Do nothing,1:Sleep,2:Hibernate,3:Shut down" |
system.hibernate | - | Hibernate | hibernation hiberfil | switch | builtin hibernate | admin
system.fast-startup | - | Fast startup | hiberboot boot fast | switch | reg "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled" dword on=1 off=0 | admin restart

# --- display ----------------------------------------------------------------------------------
display.refresh-rate | - | Refresh rate | hz refresh 120 144 60 | choice | builtin refresh-rate |
display.hdr | - | HDR | hdr high dynamic range | switch | builtin hdr | minBuild=19041 check
display.brightness | - | Brightness | brightness dim bright | slider | builtin brightness range=0..100 |
display.night-light | - | Night light | night light blue warm | link | link ms-settings:nightlight |

# --- connections ------------------------------------------------------------------------------
connections.wifi | - | Wi-Fi | wifi wireless wlan | switch | builtin wifi |
connections.bluetooth | - | Bluetooth | bt bluetooth | switch | builtin bluetooth |
connections.airplane | - | Airplane mode (every radio off) | airplane flight radios | switch | builtin airplane |
connections.proxy | - | System proxy | proxy http | switch | builtin proxy |
connections.hotspot | - | Mobile hotspot | hotspot tether share | switch | builtin hotspot | check

# --- sound ------------------------------------------------------------------------------------
sound.mono | - | Mono audio | mono stereo accessibility | switch | reg HKCU\Software\Microsoft\Multimedia\Audio\AccessibilityMonoMixState dword on=1 off=0 | check
sound.output | - | Sound output | speakers headphones default output | choice | builtin audio-output |

# --- developer --------------------------------------------------------------------------------
developer.dev-mode | - | Developer mode | developer sideload symlink | switch | reg HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense dword on=1 off=0 | admin
developer.long-paths | - | Long paths (over 260 characters) | maxpath long path 260 | switch | reg HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled dword on=1 off=0 | admin
developer.sudo | - | sudo for Windows | sudo elevate admin | choice | builtin sudo values="off:Off,forceNewWindow:In a new window,disableInput:Input closed,normal:Inline" | admin minBuild=26052
developer.vm-platform | - | Virtual Machine Platform (WSL) | wsl hyper-v virtualization feature | switch | builtin windows-feature VirtualMachinePlatform | admin restart
developer.env-vars | - | Environment variables | path env variables | link | link "rundll32.exe sysdm.cpl,EditEnvironmentVariables" |
developer.restart-explorer | - | Restart Explorer | explorer restart taskbar refresh | action | builtin restart-explorer |
```

---

## 2. The parser: `shell/menu-app/Settings/Catalog.cs`

Pure C#: no registry, no P/Invoke, no WinUI, so `menu-app.Tests` links it directly.

```csharp
namespace SystemMenu;

public enum SettingKind { Switch, Choice, Slider, Action, Link }

[Flags]
public enum Takes { Now = 0, SignOut = 1, Restart = 2 }

public sealed record Choice(string Value, string Label);

// The "how" field, tokenized: verb, target, then key=value options and bare flags.
public sealed record How(
    string Verb,                                   // reg | spi | powercfg | builtin | link | omawin
    string Target,                                 // the path, the spi / powercfg / builtin name, the uri
    IReadOnlyList<string> Args,                    // positional words after the target (reg: its type; powercfg: ac|dc|both)
    IReadOnlyDictionary<string, string> Options,   // on=, off=, values=, range=, default=, broadcast=
    IReadOnlySet<string> Flags)                    // refresh-explorer, restart-explorer
{
    public string? On => Options.GetValueOrDefault("on");
    public string? Off => Options.GetValueOrDefault("off");
    public string? Broadcast => Options.GetValueOrDefault("broadcast");
}

public sealed record SettingEntry(
    string Id, string Icon, string Name, string Keywords, SettingKind Kind, How How,
    bool Admin, Takes Takes, int MinBuild, int MaxBuild, bool Unverified,
    IReadOnlyList<Choice> Choices, (int Min, int Max)? Range, string? Default,
    string Origin)                                 // "settings.txt:12", for error messages
{
    public string Area => Id[..Id.IndexOf('.')];
    // Shown on this Windows? (§4: Catalog.Visible does the filtering.)
    public bool FitsBuild(int build) => build >= MinBuild && (MaxBuild == 0 || build <= MaxBuild);
}

public static class Catalog
{
    public static readonly string[] Areas =
        ["look", "explorer", "privacy", "input", "system", "display", "connections", "sound", "developer"];
    static readonly HashSet<string> Verbs = ["reg", "spi", "powercfg", "builtin", "link", "omawin"];

    public sealed record Problem(string Origin, string Message);

    // Every line of every layer, later layers replacing earlier ones by id (the account folder
    // wins over the shared one, which wins over omawin's). Bad lines are skipped and reported.
    public static (List<SettingEntry> Entries, List<Problem> Problems) Parse(IEnumerable<(string File, string[] Lines)> layers)
    {
        var byId = new Dictionary<string, SettingEntry>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var problems = new List<Problem>();
        foreach (var (file, lines) in layers)
            for (int i = 0; i < lines.Length; i++)
            {
                var origin = $"{Path.GetFileName(file)}:{i + 1}";
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                try
                {
                    var e = ParseLine(line, origin);
                    if (!byId.ContainsKey(e.Id)) order.Add(e.Id);
                    byId[e.Id] = e;
                }
                catch (FormatException ex) { problems.Add(new(origin, ex.Message)); }
            }
        return (order.Select(id => byId[id]).ToList(), problems);
    }

    public static SettingEntry ParseLine(string line, string origin)
    {
        var f = line.Split('|').Select(p => p.Trim()).ToArray();
        if (f.Length is < 6 or > 7) throw new FormatException($"{f.Length} fields, want 7: id | icon | name | keywords | kind | how | notes");
        var id = f[0];
        if (id.IndexOf('.') is <= 0 || !Areas.Contains(id[..id.IndexOf('.')]))
            throw new FormatException($"id '{id}' must be <area>.<name>, area one of {string.Join(' ', Areas)}");
        var kind = f[4] switch
        {
            "switch" => SettingKind.Switch, "choice" => SettingKind.Choice, "slider" => SettingKind.Slider,
            "action" => SettingKind.Action, "link" => SettingKind.Link,
            var k => throw new FormatException($"kind '{k}'"),
        };
        var how = ParseHow(f[5]);
        var notes = f.Length > 6 ? Tokens(f[6]) : [];

        bool admin = false, unverified = false; var takes = Takes.Now; int min = 0, max = 0;
        foreach (var n in notes)
        {
            if (n == "admin") admin = true;
            else if (n == "signout") takes |= Takes.SignOut;
            else if (n == "restart") takes |= Takes.Restart;
            else if (n == "check") unverified = true;
            else if (n.StartsWith("minBuild=") && int.TryParse(n[9..], out var b1)) min = b1;
            else if (n.StartsWith("maxBuild=") && int.TryParse(n[9..], out var b2)) max = b2;
            else throw new FormatException($"note '{n}'");
        }

        // A machine-wide registry value is admin whether or not the line says so: never let an
        // HKLM write reach the non-elevated path (it would fail, or worse, half apply).
        if (how.Verb == "reg" && how.Target.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) && !admin)
            throw new FormatException("an HKLM value needs the 'admin' note");

        var choices = how.Options.TryGetValue("values", out var vs) ? ParseChoices(vs) : [];
        (int, int)? range = how.Options.TryGetValue("range", out var r) ? ParseRange(r) : null;

        switch (kind)
        {
            case SettingKind.Switch when how.Verb == "reg" && (how.On is null || how.Off is null):
                throw new FormatException("a reg switch needs on= and off=");
            case SettingKind.Choice when how.Verb is "reg" or "spi" or "powercfg" && choices.Count == 0:
                throw new FormatException("a choice needs values=");
            case SettingKind.Slider when range is null:
                throw new FormatException("a slider needs range=a..b");
            case SettingKind.Link when how.Verb != "link":
                throw new FormatException("a link row's how is 'link <uri>'");
        }
        if (how.Verb == "reg" && (how.Args.Count == 0 || how.Args[0] is not ("dword" or "qword" or "sz" or "expand_sz")))
            throw new FormatException("reg needs a type after the path: dword | qword | sz | expand_sz");

        return new SettingEntry(id, f[1], f[2], f[3], kind, how, admin, takes, min, max, unverified,
            choices, range, how.Options.GetValueOrDefault("default"), origin);
    }

    public static How ParseHow(string text)
    {
        var t = Tokens(text);
        if (t.Count < 2 || !Verbs.Contains(t[0])) throw new FormatException($"how '{text}': want <verb> <target> …, verb one of {string.Join(' ', Verbs)}");
        var args = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // link and omawin take the rest of the line as written (a command and its arguments).
        if (t[0] is "link" or "omawin") return new How(t[0], string.Join(' ', t.Skip(1)), [], options, flags);
        foreach (var w in t.Skip(2))
        {
            int eq = w.IndexOf('=');
            if (eq > 0) options[w[..eq]] = w[(eq + 1)..];
            else if (w is "refresh-explorer" or "restart-explorer") flags.Add(w);
            else args.Add(w);
        }
        return new How(t[0], t[1], args, options, flags);
    }

    // Space-separated words; double quotes group, anywhere in a word (values="1:This PC,2:Home").
    public static List<string> Tokens(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (c == ' ' && !quoted)
            {
                if (any) words.Add(current.ToString());
                current.Clear(); any = false; continue;
            }
            current.Append(c); any = true;
        }
        if (quoted) throw new FormatException($"unclosed quote in '{text}'");
        if (any) words.Add(current.ToString());
        return words;
    }

    // "1:This PC,2:Home" -> (1, This PC), (2, Home). A bare "on" is its own label.
    static List<Choice> ParseChoices(string text) =>
        text.Split(',').Select(p => p.Split(':', 2) is [var v, var l] ? new Choice(v.Trim(), l.Trim()) : new Choice(p.Trim(), p.Trim())).ToList();

    static (int, int) ParseRange(string text) =>
        text.Split("..") is [var a, var b] && int.TryParse(a, out var lo) && int.TryParse(b, out var hi) && lo <= hi
            ? (lo, hi) : throw new FormatException($"range '{text}': want a..b");

    // The registry target split for the reg handler: hive, key path, value name.
    public static (string Hive, string Key, string Value) SplitReg(string target)
    {
        int first = target.IndexOf('\\'), last = target.LastIndexOf('\\');
        if (first <= 0 || last == first) throw new FormatException($"reg path '{target}': want HIVE\\key\\value");
        var hive = target[..first].ToUpperInvariant();
        if (hive is not ("HKCU" or "HKLM")) throw new FormatException($"hive '{hive}': HKCU or HKLM");
        return (hive, target[(first + 1)..last], target[(last + 1)..]);
    }
}
```

Loading (in `WinSettings`, not in the pure file), cached on the files' stamps like `Launcher.Load`:

```csharp
static List<SettingEntry>? entries;
static string stamp = "";

public static List<SettingEntry> Entries()
{
    var files = MenuModel.Layered("settings.txt");
    var now = string.Join("|", files.Select(f => File.GetLastWriteTimeUtc(f).Ticks));
    if (entries != null && now == stamp) return entries;
    var (all, problems) = Catalog.Parse(files.Where(File.Exists).Select(f => (f, File.ReadAllLines(f))));
    foreach (var p in problems) App.Log(new FormatException($"{p.Origin}: {p.Message}"));
    int build = Environment.OSVersion.Version.Build;
    bool unverified = Environment.GetEnvironmentVariable("OMAWIN_SETTINGS_UNVERIFIED") == "1";
    stamp = now;
    return entries = all.Where(e => e.FitsBuild(build) && (unverified || !e.Unverified)).ToList();
}
```

`Environment.OSVersion.Version.Build` is honest on .NET 5+ (no manifest lie). `omawin check` should print the catalog's problems (one line each), so a typo in an account folder's `settings.txt` doesn't fail silently.

---

## 3. Native calls: `shell/menu-app/Settings/Native.cs`

All source-generated. `SystemMenu.csproj` already has `AllowUnsafeBlocks` (Audio.cs). Existing `[DllImport]`s elsewhere can stay; new code uses `LibraryImport`.

```csharp
using System.Runtime.InteropServices;

namespace SystemMenu;

static unsafe partial class Native
{
    // --- SystemParametersInfo ---------------------------------------------------------------
    public const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, void* pvParam, uint winIni);

    // --- WM_SETTINGCHANGE broadcast ----------------------------------------------------------
    public const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;
    public static readonly nint HWND_BROADCAST = 0xFFFF;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nuint wParam, string? lParam, uint flags, uint timeoutMs, out nuint result);

    // --- the shell's refresh after Explorer's view settings change --------------------------
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_IDLIST = 0x0000;
    [LibraryImport("shell32.dll")]
    public static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    // --- registry change notification -------------------------------------------------------
    public const uint REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4, REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;
    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(nint key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint filter, nint evt, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    // --- power settings (powrprof) ----------------------------------------------------------
    [LibraryImport("powrprof.dll")] public static partial uint PowerGetActiveScheme(nint root, Guid** scheme);
    [LibraryImport("powrprof.dll")] public static partial uint PowerReadACValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint* value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerReadDCValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint* value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerWriteACValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerWriteDCValueIndex(nint root, Guid* scheme, Guid* sub, Guid* setting, uint value);
    [LibraryImport("powrprof.dll")] public static partial uint PowerSetActiveScheme(nint root, Guid* scheme);
    [LibraryImport("kernel32.dll")] public static partial nint LocalFree(nint mem);

    // --- resource strings ("@%SystemRoot%\system32\shell32.dll,-21817") ---------------------
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, char* outBuf, uint outChars, nint reserved);

    // --- WinINet: tell running programs the proxy changed ----------------------------------
    public const uint INTERNET_OPTION_REFRESH = 37, INTERNET_OPTION_SETTINGS_CHANGED = 39;
    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InternetSetOption(nint handle, uint option, nint buffer, uint length);

    // --- window subclassing, to hear WM_SETTINGCHANGE on the menu app's own window ---------
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    // --- display modes ---------------------------------------------------------------------
    // DEVMODEW is 220 bytes; only the display fields are used (dmPelsWidth/Height, dmDisplayFrequency).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        public fixed char dmDeviceName[32];
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        public fixed char dmFormName[32];
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint DM_DISPLAYFREQUENCY = 0x00400000, CDS_UPDATEREGISTRY = 0x1;
    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplaySettings(string? device, int mode, DEVMODE* devMode);
    [LibraryImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ChangeDisplaySettingsEx(string? device, DEVMODE* devMode, nint hwnd, uint flags, nint param);
}
```

---

## 4. Handlers: `shell/menu-app/Settings/Switches.cs`

```csharp
using Microsoft.Win32;

namespace SystemMenu;

// One per verb. Read returns the setting's value as the catalog speaks it ("on", "1", "20"), or
// null when it can't tell (a key never written, a radio that isn't there). Write returns null on
// success, else a short reason for the note. Both are safe off the UI thread.
public interface ISettingHandler
{
    string? Read(SettingEntry e);
    string? Write(SettingEntry e, string value);
    // A choice whose values come from the PC (refresh rates, audio outputs). Default: the catalog's.
    IReadOnlyList<Choice> Choices(SettingEntry e) => e.Choices;
    // Cheap enough to read on the UI thread for rows on screen? (reg, spi, powercfg: microseconds.)
    bool Fast => true;
}

public static class Handlers
{
    public static readonly Dictionary<string, ISettingHandler> ByVerb = new()
    {
        ["reg"] = new RegHandler(), ["spi"] = new SpiHandler(), ["powercfg"] = new PowerHandler(),
        ["builtin"] = new BuiltinHandler(), ["link"] = new LinkHandler(), ["omawin"] = new OmawinHandler(),
    };
    public static ISettingHandler For(SettingEntry e) => ByVerb[e.How.Verb];

    // Raw value <-> the catalog's words: on=/off= for a switch, values= keys for a choice, the
    // number itself for a slider.
    public static string? ToWords(SettingEntry e, string? raw) => e.Kind switch
    {
        _ when raw is null => e.How.Off == "-" && e.Kind == SettingKind.Switch ? "off" : null,
        SettingKind.Switch => raw == e.How.On ? "on" : raw == e.How.Off ? "off" : null, // anything else: unknown, shown as "?"
        _ => raw,
    };
    public static string FromWords(SettingEntry e, string value) => e.Kind == SettingKind.Switch
        ? (value == "on" ? e.How.On! : e.How.Off!) : value;
}

// --- reg -------------------------------------------------------------------------------------
sealed class RegHandler : ISettingHandler
{
    public string? Read(SettingEntry e)
    {
        var (hive, path, name) = Catalog.SplitReg(e.How.Target);
        using var key = Root(hive).OpenSubKey(path);
        var v = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        // A DWORD reads back as int: 0xFFFFFFFF is -1, so compare as unsigned (TapsEnabled).
        var raw = v switch { int i => unchecked((uint)i).ToString(), long l => unchecked((ulong)l).ToString(), null => null, _ => v.ToString() };
        return Handlers.ToWords(e, raw ?? e.Default);
    }

    public string? Write(SettingEntry e, string value)
    {
        if (e.Admin) return "needs admin"; // never here: WinSettings queues it (§8)
        var (hive, path, name) = Catalog.SplitReg(e.How.Target);
        var raw = Handlers.FromWords(e, value);
        using var key = Root(hive).CreateSubKey(path, writable: true);
        if (raw == "-") key.DeleteValue(name, throwOnMissingValue: false);
        else switch (e.How.Args[0])
        {
            case "dword": key.SetValue(name, unchecked((int)uint.Parse(raw)), RegistryValueKind.DWord); break;
            case "qword": key.SetValue(name, unchecked((long)ulong.Parse(raw)), RegistryValueKind.QWord); break;
            case "expand_sz": key.SetValue(name, raw, RegistryValueKind.ExpandString); break;
            default: key.SetValue(name, raw, RegistryValueKind.String); break;
        }
        Apply.After(e);
        return null;
    }

    public static RegistryKey Root(string hive) => hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
}

// After a write: tell Windows, so the change shows without signing out.
static class Apply
{
    public static void After(SettingEntry e)
    {
        // broadcast=<area>: WM_SETTINGCHANGE with that string as lParam ("ImmersiveColorSet" is
        // what dark mode / accent / transparency listeners look for; plain broadcast: null).
        if (e.How.Options.ContainsKey("broadcast")) Broadcast(e.How.Broadcast);
        if (e.How.Flags.Contains("refresh-explorer"))
        {
            // Explorer re-reads its Advanced view settings on a broadcast plus an association
            // change notification (what the Folder Options dialog causes). (check: per value)
            Broadcast(null);
            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, 0, 0);
        }
        if (e.How.Flags.Contains("restart-explorer")) ExplorerRestart.Request();
    }

    public static void Broadcast(string? area) =>
        // Off the UI thread: a hung top-level window costs up to the timeout, per window.
        Task.Run(() => Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, 0, area,
            Native.SMTO_ABORTIFHUNG, 1000, out _));
}

// Restarting Explorer kills the taskbar and open folder windows for a second, so it's never done
// on its own: the row says "Restart Explorer to apply" and the first switch that needs it
// queues one restart, offered as a row (and done at once only from `omawin setting set ... --now`).
static class ExplorerRestart
{
    public static bool Pending { get; private set; }
    public static event Action? Changed;
    public static void Request() { Pending = true; Changed?.Invoke(); }

    public static void Now()
    {
        Pending = false; Changed?.Invoke();
        // Explorer restarts itself when killed (AutoRestartShell); start it if it didn't.
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer")) using (p) try { p.Kill(); } catch { }
        Task.Delay(1500).ContinueWith(_ =>
        {
            if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        });
        // GlazeWM must re-adopt the taskbar's windows; the bar is ours and unaffected. (check)
    }
}

// --- spi -------------------------------------------------------------------------------------
unsafe sealed class SpiHandler : ISettingHandler
{
    const uint Save = Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE; // SENDCHANGE broadcasts for us

    public string? Read(SettingEntry e)
    {
        switch (e.How.Target)
        {
            case "animations":   // SPI_GETCLIENTAREAANIMATION: "Animation effects" in Settings (check)
            { int on = 0; return Native.SystemParametersInfo(0x1042, 0, &on, 0) ? (on != 0 ? "on" : "off") : null; }
            case "mouse-accel":  // SPI_GETMOUSE: [threshold1, threshold2, acceleration]
            { int* p = stackalloc int[3]; return Native.SystemParametersInfo(0x0003, 0, p, 0) ? (p[2] != 0 ? "on" : "off") : null; }
            case "pointer-speed": // SPI_GETMOUSESPEED: 1..20
            { int s = 0; return Native.SystemParametersInfo(0x0070, 0, &s, 0) ? s.ToString() : null; }
            case "key-repeat":   // SPI_GETKEYBOARDSPEED: 0..31
            { uint s = 0; return Native.SystemParametersInfo(0x000A, 0, &s, 0) ? s.ToString() : null; }
            case "key-delay":    // SPI_GETKEYBOARDDELAY: 0..3
            { int d = 0; return Native.SystemParametersInfo(0x0016, 0, &d, 0) ? d.ToString() : null; }
            case "sticky-hotkey": // SPI_GETSTICKYKEYS, SKF_HOTKEYACTIVE
            { var sk = new StickyKeys { Size = 8 }; return Native.SystemParametersInfo(0x003A, 8, &sk, 0) ? ((sk.Flags & 0x4) != 0 ? "on" : "off") : null; }
            default: return null;
        }
    }

    public string? Write(SettingEntry e, string value)
    {
        bool on = value == "on";
        bool ok = e.How.Target switch
        {
            // pvParam carries the BOOL itself for SETCLIENTAREAANIMATION. (check)
            "animations" => Native.SystemParametersInfo(0x1043, 0, (void*)(nint)(on ? 1 : 0), Save),
            // Windows' own "on" is thresholds 6 and 10 with acceleration 1; off is all zero.
            "mouse-accel" => SetMouse(on ? [6, 10, 1] : [0, 0, 0]),
            // SETMOUSESPEED takes the number in pvParam, not a pointer to it.
            "pointer-speed" => Native.SystemParametersInfo(0x0071, 0, (void*)(nint)Clamp(e, value), Save),
            "key-repeat" => Native.SystemParametersInfo(0x000B, (uint)Clamp(e, value), null, Save),
            "key-delay" => Native.SystemParametersInfo(0x0017, uint.Parse(value), null, Save),
            "sticky-hotkey" => SetSticky(on),
            _ => false,
        };
        return ok ? null : $"SystemParametersInfo failed ({System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})";
    }

    static bool SetMouse(int[] values) { fixed (int* p = values) return Native.SystemParametersInfo(0x0004, 0, p, Save); }

    static bool SetSticky(bool on)
    {
        var sk = new StickyKeys { Size = 8 };
        if (!Native.SystemParametersInfo(0x003A, 8, &sk, 0)) return false;
        sk.Flags = on ? sk.Flags | 0x4 : sk.Flags & ~0x4u;
        return Native.SystemParametersInfo(0x003B, 8, &sk, Save);
    }

    static int Clamp(SettingEntry e, string value) =>
        Math.Clamp(int.Parse(value), e.Range?.Min ?? int.MinValue, e.Range?.Max ?? int.MaxValue);

    struct StickyKeys { public uint Size, Flags; }
}

// --- powercfg --------------------------------------------------------------------------------
// The active power scheme's AC or DC value, through powrprof (no powercfg.exe start). Standard
// users may change the active scheme's values; no admin. (check on a managed PC: policy can lock it)
unsafe sealed class PowerHandler : ISettingHandler
{
    static readonly Dictionary<string, (Guid Sub, Guid Setting, bool Seconds)> Names = new()
    {
        ["screen-off"] = (new("7516b95f-f776-4464-8c53-06167f40cc99"), new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"), true),  // SUB_VIDEO / VIDEOIDLE
        ["sleep"]      = (new("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), true),  // SUB_SLEEP / STANDBYIDLE
        ["hibernate-after"] = (new("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new("9d7815a6-7ee4-497e-8888-515a05f02364"), true), // HIBERNATEIDLE
        ["lid"]        = (new("4f971e89-eebd-4455-a8de-9e59040e7347"), new("5ca83367-6e45-459f-a27b-476b1d01c936"), false), // SUB_BUTTONS / LIDACTION
    };

    bool Dc(SettingEntry e) => e.How.Args.Count > 0 && e.How.Args[0] == "dc";

    public string? Read(SettingEntry e)
    {
        if (!Names.TryGetValue(e.How.Target, out var n)) return null;
        Guid* scheme;
        if (Native.PowerGetActiveScheme(0, &scheme) != 0) return null;
        try
        {
            uint v; var sub = n.Sub; var setting = n.Setting;
            uint err = Dc(e) ? Native.PowerReadDCValueIndex(0, scheme, &sub, &setting, &v) : Native.PowerReadACValueIndex(0, scheme, &sub, &setting, &v);
            return err != 0 ? null : (n.Seconds ? (v / 60).ToString() : v.ToString());
        }
        finally { Native.LocalFree((nint)scheme); }
    }

    public string? Write(SettingEntry e, string value)
    {
        if (!Names.TryGetValue(e.How.Target, out var n)) return $"no power setting '{e.How.Target}'";
        uint v = uint.Parse(value) * (n.Seconds ? 60u : 1u);
        Guid* scheme;
        if (Native.PowerGetActiveScheme(0, &scheme) != 0) return "no active power scheme";
        try
        {
            var sub = n.Sub; var setting = n.Setting;
            bool both = e.How.Args.Count > 0 && e.How.Args[0] == "both";
            uint err = 0;
            if (!Dc(e) || both) err |= Native.PowerWriteACValueIndex(0, scheme, &sub, &setting, v);
            if (Dc(e) || both) err |= Native.PowerWriteDCValueIndex(0, scheme, &sub, &setting, v);
            // Writing the index changes the stored scheme; re-activating it applies it now.
            if (err == 0) err = Native.PowerSetActiveScheme(0, scheme);
            return err == 0 ? null : $"power setting: error {err}";
        }
        finally { Native.LocalFree((nint)scheme); }
    }
}

// --- link, omawin ---------------------------------------------------------------------------
sealed class LinkHandler : ISettingHandler
{
    public string? Read(SettingEntry e) => null; // nothing to show: the row ends in ↗ "Opens Settings"
    public string? Write(SettingEntry e, string value)
    {
        var target = e.How.Target;
        if (Launcher.Link.IsMatch(target)) QuickSettings.Open(target);
        else
        {
            // "rundll32.exe sysdm.cpl,EditEnvironmentVariables": program + arguments.
            var (exe, args) = target.Split(' ', 2) is [var x, var a] ? (x, a) : (target, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        return null;
    }
}

sealed class OmawinHandler : ISettingHandler
{
    public string? Read(SettingEntry e) => null;
    public string? Write(SettingEntry e, string value) { MenuModel.Omawin(Catalog.Tokens(e.How.Target).ToArray()); return null; }
}
```

---

## 5. Builtins: `shell/menu-app/Settings/Builtins.cs`

Code for what has no single registry value. Each reuses what `QuickSettings.cs` / `Audio.cs` already do where it exists.

```csharp
namespace SystemMenu;

sealed class BuiltinHandler : ISettingHandler
{
    interface IBuiltin
    {
        string? Read(SettingEntry e);
        string? Write(SettingEntry e, string value);
        IReadOnlyList<Choice>? Choices(SettingEntry e) => null;
        bool Fast => false; // radios, WMI, WinRT: read off the UI thread
    }

    static readonly Dictionary<string, IBuiltin> All = new()
    {
        ["wifi"] = new Radio(Windows.Devices.Radios.RadioKind.WiFi),
        ["bluetooth"] = new Radio(Windows.Devices.Radios.RadioKind.Bluetooth),
        ["airplane"] = new Airplane(),
        ["power-mode"] = new PowerMode(),
        ["brightness"] = new Brightness(),
        ["desktop-icons"] = new DesktopIcons(),
        ["refresh-rate"] = new RefreshRate(),
        ["hdr"] = new Hdr(),
        ["proxy"] = new Proxy(),
        ["hotspot"] = new Hotspot(),
        ["audio-output"] = new AudioOutput(),
        ["restart-explorer"] = new RestartExplorer(),
        // admin ones: reading needs no admin; Write is never called (WinSettings queues them for
        // bin/settings-admin.ps1, §8), so their Write only says so.
        ["hibernate"] = new ReadOnly(e => File.Exists(@"C:\hiberfil.sys") ? "on" : "off"), // (check: hidden system file; Exists works without admin)
        ["sudo"] = new ReadOnly(_ => RegRead(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo", "Enabled") switch // (check)
            { 0 => "off", 1 => "forceNewWindow", 2 => "disableInput", 3 => "normal", _ => null }),
        ["windows-feature"] = new WindowsFeature(),
    };

    IBuiltin Get(SettingEntry e) => All.TryGetValue(e.How.Target, out var b) ? b : throw new InvalidOperationException($"no builtin '{e.How.Target}'");
    public string? Read(SettingEntry e) => Get(e).Read(e);
    public string? Write(SettingEntry e, string value) => Get(e).Write(e, value);
    public IReadOnlyList<Choice> Choices(SettingEntry e) => Get(e).Choices(e) ?? e.Choices;
    public bool Fast => false;

    static int? RegRead(string path, string name) =>
        Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path)?.GetValue(name) is int i ? i : null;

    sealed class ReadOnly(Func<SettingEntry, string?> read) : IBuiltin
    {
        public string? Read(SettingEntry e) => read(e);
        public string? Write(SettingEntry e, string value) => "needs admin";
    }

    // Wi-Fi and Bluetooth: QuickSettings' radio code (Radios.IsOn / Radios.Set).
    sealed class Radio(Windows.Devices.Radios.RadioKind kind) : IBuiltin
    {
        public string? Read(SettingEntry e) => Radios.IsOn(kind) ? "on" : "off";
        public string? Write(SettingEntry e, string value) => Radios.Set(kind, value == "on");
    }

    // Airplane mode as Windows does it from outside its own app: every radio off, remembered so
    // turning it off restores what was on. (Windows' own airplane flag has no public API.)
    sealed class Airplane : IBuiltin
    {
        static string StateFile => Path.Combine(MenuModel.StateDir, "airplane");
        public string? Read(SettingEntry e) => File.Exists(StateFile) ? "on" : "off";
        public string? Write(SettingEntry e, string value)
        {
            if (value == "on")
            {
                var wasOn = QuickSettings.RadioList().Where(r => r.State == "On").Select(r => r.Kind).ToList();
                File.WriteAllLines(StateFile, wasOn);
                foreach (var k in wasOn) Radios.Set(Enum.Parse<Windows.Devices.Radios.RadioKind>(k), false);
            }
            else if (File.Exists(StateFile))
            {
                foreach (var k in File.ReadAllLines(StateFile)) Radios.Set(Enum.Parse<Windows.Devices.Radios.RadioKind>(k), true);
                File.Delete(StateFile);
            }
            return null;
        }
    }

    sealed class PowerMode : IBuiltin
    {
        static readonly Choice[] Modes =
        [
            new(Brightness_.PowerEfficiency.ToString(), "Power efficiency"),
            new(Brightness_.Balanced.ToString(), "Balanced"),
            new(Brightness_.Performance.ToString(), "Best performance"),
        ];
        public IReadOnlyList<Choice>? Choices(SettingEntry e) => Modes;
        public string? Read(SettingEntry e) { var m = QuickSettings.PowerMode(); return m.Known ? m.Current.ToString() : null; }
        public string? Write(SettingEntry e, string value) => QuickSettings.SetPowerMode(Guid.Parse(value));
        // Brightness_ stands for the static class in QuickSettings.cs that holds the overlay GUIDs
        // (named Brightness there; its PowerEfficiency / Balanced / Performance fields).
    }

    // One slider for the first monitor that reports a brightness control; per-monitor rows are
    // the Quick settings view's job.
    sealed class Brightness : IBuiltin
    {
        public string? Read(SettingEntry e) => QuickSettings.Displays().FirstOrDefault(d => d.Writable)?.Current.ToString();
        public string? Write(SettingEntry e, string value) =>
            QuickSettings.Displays().FirstOrDefault(d => d.Writable) is { } d ? QuickSettings.SetBrightness(d, int.Parse(value)) : "no monitor with a brightness control";
    }

    // bin/desktop-icons.ps1, in C#: Explorer's own "Show desktop icons" command (WM_COMMAND 0x7402
    // to SHELLDLL_DefView) and HideIcons for the next sign-in. The ps1 stays for install.
    sealed class DesktopIcons : IBuiltin
    {
        public string? Read(SettingEntry e) => DesktopView.Shown() switch { 1 => "on", 0 => "off", _ => null };
        public string? Write(SettingEntry e, string value)
        {
            if (Read(e) == value) return null;
            DesktopView.ToggleCommand(); // the same message the desktop's View menu sends
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            k.SetValue("HideIcons", value == "on" ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
            File.WriteAllText(Path.Combine(MenuModel.StateDir, "desktop-icons"), value == "on" ? "show" : "hide"); // the ps1's remembered choice
            return null;
        }
    }

    // The primary monitor's refresh rate, from the modes it offers at its current resolution.
    // (Per monitor: one entry per display, generated at load, display.refresh-rate.<n>; later.)
    unsafe sealed class RefreshRate : IBuiltin
    {
        public bool Fast => true;
        public IReadOnlyList<Choice>? Choices(SettingEntry e)
        {
            var cur = Current();
            var rates = new SortedSet<uint>();
            var m = new Native.DEVMODE { dmSize = (ushort)sizeof(Native.DEVMODE) };
            for (int i = 0; Native.EnumDisplaySettings(null, i, &m); i++)
                if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmBitsPerPel == cur.dmBitsPerPel)
                    rates.Add(m.dmDisplayFrequency);
            return rates.Reverse().Select(r => new Choice(r.ToString(), $"{r} Hz")).ToList();
        }
        public string? Read(SettingEntry e) => Current().dmDisplayFrequency.ToString();
        public string? Write(SettingEntry e, string value)
        {
            var m = Current();
            m.dmDisplayFrequency = uint.Parse(value);
            m.dmFields = Native.DM_DISPLAYFREQUENCY;
            int r = Native.ChangeDisplaySettingsEx(null, &m, 0, Native.CDS_UPDATEREGISTRY, 0);
            return r == 0 ? null : $"the monitor refused {value} Hz ({r})";
        }
        static Native.DEVMODE Current()
        {
            var m = new Native.DEVMODE { dmSize = (ushort)sizeof(Native.DEVMODE) };
            Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, &m);
            return m;
        }
    }

    // HDR ("advanced colour") per display path: DisplayConfigGetDeviceInfo with
    // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO (9) to read, ..._SET_ADVANCE_COLOR_STATE (10)
    // to set, over QueryDisplayConfig's active paths. (check: struct layouts; on 24H2 the newer
    // GET_ADVANCED_COLOR_INFO_2 (15) / SET_HDR_STATE (16) are the ones Settings uses)
    sealed class Hdr : IBuiltin
    {
        public string? Read(SettingEntry e) => DisplayConfig.HdrSupported() ? (DisplayConfig.HdrOn() ? "on" : "off") : null;
        public string? Write(SettingEntry e, string value) => DisplayConfig.SetHdr(value == "on");
    }

    // The WinINet proxy switch (Internet Settings\ProxyEnable), then the two notifications every
    // WinINet program (and Edge / Chrome, through it) listens for.
    sealed class Proxy : IBuiltin
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        public bool Fast => true;
        public string? Read(SettingEntry e) =>
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)?.GetValue("ProxyEnable") is 1 ? "on" : "off";
        public string? Write(SettingEntry e, string value)
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (value == "on" && k.GetValue("ProxyServer") is not string { Length: > 0 })
                return "no proxy server set: opening its page"; // and WinSettings opens ms-settings:network-proxy
            k.SetValue("ProxyEnable", value == "on" ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            Native.InternetSetOption(0, Native.INTERNET_OPTION_SETTINGS_CHANGED, 0, 0);
            Native.InternetSetOption(0, Native.INTERNET_OPTION_REFRESH, 0, 0);
            return null;
        }
    }

    // Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager over the current
    // internet profile. (check: unpackaged apps may need the wiFiControl capability, as with
    // Windows.Devices.WiFi; if so this row becomes a link to ms-settings:network-mobilehotspot)
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
            return r.Status == Windows.Networking.NetworkOperators.TetheringOperationStatus.Success ? null : r.Status.ToString();
        }
    }

    // The default speaker: Audio.cs's device list and MakeDefault.
    sealed class AudioOutput : IBuiltin
    {
        public IReadOnlyList<Choice>? Choices(SettingEntry e) =>
            Audio.List().Where(d => d.IsOutput).Select(d => new Choice(d.Id, d.Name)).ToList(); // (AudioDevice's field names: check Audio.cs)
        public string? Read(SettingEntry e) => Audio.DefaultSpeaker()?.Id;
        public string? Write(SettingEntry e, string value) { Audio.MakeDefault(value); return null; }
    }

    sealed class RestartExplorer : IBuiltin
    {
        public string? Read(SettingEntry e) => null;
        public string? Write(SettingEntry e, string value) { ExplorerRestart.Now(); return null; }
    }

    // Reading an optional feature's state needs no admin through the registry key DISM keeps.
    // (check: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Notifications\OptionalFeatures\<name>\Selection is 1 when on)
    sealed class WindowsFeature : IBuiltin
    {
        public string? Read(SettingEntry e)
        {
            var name = e.How.Args.FirstOrDefault() ?? "";
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Notifications\OptionalFeatures\{name}");
            return k?.GetValue("Selection") is int s ? (s == 1 ? "on" : "off") : null;
        }
        public string? Write(SettingEntry e, string value) => "needs admin";
    }
}
```

`DesktopView` is the C# half of `bin/desktop-icons.ps1`'s `Add-Type` block (find `SHELLDLL_DefView` under Progman or a WorkerW, `IsWindowVisible` of its list view, `SendMessage(view, WM_COMMAND, 0x7402, 0)`), moved into `Native.cs` with `LibraryImport`. `DisplayConfig` is a new small file (QueryDisplayConfig + DisplayConfigGet/SetDeviceInfo), sized like `Brightness` in QuickSettings.cs.

---

## 6. The service: `shell/menu-app/Settings/WinSettings.cs`

Cache, lazy reads, set / toggle, history, undo, admin queue. All writes go through `Set`, so history and the admin rule can't be skipped.

```csharp
using System.Collections.Concurrent;

namespace SystemMenu;

public static partial class WinSettings
{
    // Last known value per id; missing: not read yet. Null value: read, but Windows wouldn't say.
    static readonly ConcurrentDictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, byte> reading = new();

    // An id's value changed (read for the first time, written here, or changed elsewhere). Raised
    // on any thread; the window marshals to its dispatcher and redraws that row only.
    public static event Action<string>? Changed;

    public static SettingEntry? Find(string id) => Entries().FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // For a row on screen: the value if known; else null now, and a read is started (inline for
    // fast handlers, on the thread pool for builtins), and Changed fires when it lands. The row
    // shows "…" meanwhile. Also starts watching the entry (Watch.cs), only once it's been shown.
    public static string? Peek(SettingEntry e)
    {
        if (values.TryGetValue(e.Id, out var v)) return v;
        var h = Handlers.For(e);
        if (h.Fast) return ReadNow(e);
        if (reading.TryAdd(e.Id, 0))
            Task.Run(() => { ReadNow(e); reading.TryRemove(e.Id, out _); });
        return null;
    }

    public static string? ReadNow(SettingEntry e)
    {
        string? v;
        try { v = Handlers.For(e).Read(e); }
        catch (Exception ex) { App.Log(ex); v = null; }
        var old = values.TryGetValue(e.Id, out var o) ? o : "\0";
        values[e.Id] = v;
        SettingWatch.Ensure(e);
        if (old != v) Changed?.Invoke(e.Id);
        return v;
    }

    // Called by Watch.cs when a key / SPI / power setting the entry depends on changed.
    internal static void Invalidate(SettingEntry e) { values.TryRemove(e.Id, out _); Task.Run(() => ReadNow(e)); }

    public sealed record Outcome(bool Ok, string Message, bool Queued = false);

    // Every change: validate, refuse admin (queue it), write, record before/after, re-read to
    // confirm. Never called on the UI thread (radios and WMI block).
    public static Outcome Set(SettingEntry e, string value, string source = "menu")
    {
        value = Normalize(e, value);
        if (Validate(e, value) is { } bad) return new(false, bad);
        var before = ReadNow(e);
        if (before == value && e.Kind != SettingKind.Action && e.Kind != SettingKind.Link) return new(true, $"{e.Name}: already {Label(e, value)}");
        if (e.Admin)
        {
            AdminQueue.Add(e, value);
            return new(true, $"{e.Name}: needs admin. Queued with {AdminQueue.Count - 1} other(s); apply them in one prompt: omawin settings admin", Queued: true);
        }
        string? error;
        try { error = Handlers.For(e).Write(e, value); }
        catch (Exception ex) { App.Log(ex); error = ex.Message; }
        if (error != null) return new(false, $"{e.Name}: {error}");
        if (e.Kind is SettingKind.Action or SettingKind.Link) return new(true, e.Kind == SettingKind.Link ? $"{e.Name}: opened" : $"{e.Name}: done");
        var after = ReadNow(e);
        History.Append(e.Id, before, value, source);
        var when = e.Takes.HasFlag(Takes.Restart) ? " (after a restart)" : e.Takes.HasFlag(Takes.SignOut) ? " (after you sign out)"
            : e.How.Flags.Contains("restart-explorer") ? " (Restart Explorer to apply)" : "";
        return after == value || after == null
            ? new(true, $"{e.Name}: {Label(e, value)}{when}")
            : new(false, $"{e.Name}: set, but Windows still says {Label(e, after)}"); // a policy, or a value this build ignores
    }

    public static Outcome Toggle(SettingEntry e, string source = "menu") => e.Kind switch
    {
        SettingKind.Switch => Set(e, ReadNow(e) == "on" ? "off" : "on", source),
        SettingKind.Choice => Step(e, +1, source),
        _ => Set(e, "", source), // action, link: just do it
    };

    // A choice: the next value (wraps); a slider: one step of ~5% of its range.
    public static Outcome Step(SettingEntry e, int by, string source = "menu")
    {
        var cur = ReadNow(e);
        if (e.Kind == SettingKind.Slider && e.Range is var (lo, hi))
            return Set(e, Math.Clamp((int.TryParse(cur, out var n) ? n : lo) + by * Math.Max(1, (hi - lo) / 20), lo, hi).ToString(), source);
        var choices = Handlers.For(e).Choices(e);
        int i = choices.ToList().FindIndex(c => c.Value == cur);
        return Set(e, choices[((i < 0 ? 0 : i + by) % choices.Count + choices.Count) % choices.Count].Value, source);
    }

    // Typed values are forgiving: "true/1/yes" for on, a choice's label for its key.
    static string Normalize(SettingEntry e, string v) => e.Kind switch
    {
        SettingKind.Switch => v.ToLowerInvariant() switch { "1" or "true" or "yes" or "on" => "on", "0" or "false" or "no" or "off" => "off", var x => x },
        SettingKind.Choice => Handlers.For(e).Choices(e).FirstOrDefault(c => c.Label.Equals(v, StringComparison.OrdinalIgnoreCase))?.Value ?? v,
        _ => v,
    };

    static string? Validate(SettingEntry e, string v) => e.Kind switch
    {
        SettingKind.Switch when v is not ("on" or "off") => $"{e.Id}: on or off",
        SettingKind.Choice when Handlers.For(e).Choices(e).All(c => c.Value != v) =>
            $"{e.Id}: one of {string.Join(", ", Handlers.For(e).Choices(e).Select(c => c.Value))}",
        SettingKind.Slider when !int.TryParse(v, out var n) || n < e.Range!.Value.Min || n > e.Range.Value.Max =>
            $"{e.Id}: a number from {e.Range!.Value.Min} to {e.Range.Value.Max}",
        _ => null,
    };

    public static string Label(SettingEntry e, string? v) => v is null ? "?" :
        e.Kind == SettingKind.Choice ? Handlers.For(e).Choices(e).FirstOrDefault(c => c.Value == v)?.Label ?? v
        : e.Kind == SettingKind.Switch ? (v == "on" ? "On" : "Off") : v;
}
```

### Undo history (`%LOCALAPPDATA%\launcher\settings-history.tsv`)

One line per change: `<unix seconds>\t<id>\t<before>\t<after>\t<source>`, source `menu | command | apply | undo:<line>`. Before is `\0`-free text, `?` when it couldn't be read (then undo can't restore it and says so). Append-only; trimmed to the last 1000 lines on start.

```csharp
public static partial class WinSettings
{
    static class History
    {
        static string FilePath => Path.Combine(MenuModel.StateDir, "settings-history.tsv");
        static readonly object gate = new();

        public static void Append(string id, string? before, string after, string source)
        {
            lock (gate)
            {
                Directory.CreateDirectory(MenuModel.StateDir);
                File.AppendAllText(FilePath, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\t{id}\t{before ?? "?"}\t{after}\t{source}\n");
            }
        }

        public static string[] Lines() { lock (gate) return File.Exists(FilePath) ? File.ReadAllLines(FilePath) : []; }

        // The last change not undone yet (an undo line names the line it undid), newest first.
        public static (int Line, string Id, string Before)? LastUndoable()
        {
            var lines = Lines();
            var undone = lines.Select(l => l.Split('\t')).Where(p => p.Length == 5 && p[4].StartsWith("undo:"))
                .Select(p => int.Parse(p[4][5..])).ToHashSet();
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var p = lines[i].Split('\t');
                if (p.Length == 5 && !p[4].StartsWith("undo:") && !undone.Contains(i)) return (i, p[1], p[2]);
            }
            return null;
        }

        // The ids changed through omawin, for `settings save`.
        public static IEnumerable<string> ChangedIds() => Lines().Select(l => l.Split('\t')).Where(p => p.Length == 5).Select(p => p[1]).Distinct();
    }

    // Ctrl+Z in the window, the "Undo last change" row, `omawin setting undo`.
    public static Outcome UndoLast()
    {
        if (History.LastUndoable() is not var (line, id, before)) return new(false, "Nothing to undo");
        if (Find(id) is not { } e) return new(false, $"{id}: no longer in the catalog");
        if (before == "?") return new(false, $"{e.Name}: its earlier value wasn't known; set it yourself");
        var r = Set(e, before, source: $"undo:{line}");
        return r with { Message = "Undone: " + r.Message };
    }
}
```

`Set` with source `undo:<n>` records the undo line itself, so undoing twice walks back two changes and never re-undoes.

---

## 7. Watching for changes: `shell/menu-app/Settings/Watch.cs`

Only entries that have been shown are watched (`Ensure` is called from `ReadNow`), so the handle count stays at the few dozen keys someone looked at. Registry: one `RegNotifyChangeKeyValue` per distinct key, thread-agnostic (the waiting thread can be the pool), re-armed after each signal. SPI: the menu app's own top-level window gets `WM_SETTINGCHANGE` with `wParam` = the SPI action. Radios / network: `QuickSettings.Watch` already. Power settings: no event worth wiring; re-read when the window is shown.

```csharp
using Microsoft.Win32;

namespace SystemMenu;

static class SettingWatch
{
    sealed class KeyWatch
    {
        public required RegistryKey Key;
        public required AutoResetEvent Signal;
        public RegisteredWaitHandle? Wait;
        public readonly List<SettingEntry> Entries = [];
    }

    static readonly Dictionary<string, KeyWatch> keys = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> watched = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();

    public static void Ensure(SettingEntry e)
    {
        lock (gate)
        {
            if (!watched.Add(e.Id)) return;
            if (e.How.Verb == "reg") WatchKey(e);
            else if (e.How.Verb == "builtin" && e.How.Target == "proxy")
                WatchKey(e with { How = e.How with { Target = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyEnable" } });
            // spi: SettingChange below; builtins with radios: QuickSettings.Watch (wired in WinSettings.Start).
        }
    }

    static void WatchKey(SettingEntry e)
    {
        var (hive, path, _) = Catalog.SplitReg(e.How.Target);
        // A key that doesn't exist yet (never set) can't be watched: watch the nearest parent
        // that does, with its subtree, so its creation is seen.
        string at = path; bool subtree = false;
        RegistryKey? key;
        while ((key = RegHandler.Root(hive).OpenSubKey(at, RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.ReadKey)) == null)
        {
            int cut = at.LastIndexOf('\\');
            if (cut < 0) return;
            at = at[..cut]; subtree = true;
        }
        var name = hive + "\\" + at + (subtree ? "\\*" : "");
        if (keys.TryGetValue(name, out var existing)) { existing.Entries.Add(e); key.Dispose(); return; }
        var w = new KeyWatch { Key = key, Signal = new AutoResetEvent(false) };
        w.Entries.Add(e);
        keys[name] = w;
        Arm(w, subtree);
        w.Wait = ThreadPool.RegisterWaitForSingleObject(w.Signal, (_, _) =>
        {
            Arm(w, subtree); // re-arm first, so a change during the re-read isn't missed
            List<SettingEntry> list; lock (gate) list = [.. w.Entries];
            foreach (var x in list) WinSettings.Invalidate(x);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    static void Arm(KeyWatch w, bool subtree) =>
        Native.RegNotifyChangeKeyValue(w.Key.Handle.DangerousGetHandle(), subtree,
            Native.REG_NOTIFY_CHANGE_NAME | Native.REG_NOTIFY_CHANGE_LAST_SET | Native.REG_NOTIFY_THREAD_AGNOSTIC,
            w.Signal.SafeWaitHandle.DangerousGetHandle(), asynchronous: true);

    // --- WM_SETTINGCHANGE on the menu app's window ------------------------------------------
    // MainWindow calls this once with its HWND (WinRT.Interop.WindowNative.GetWindowHandle).
    // Top-level windows get the broadcast (message-only windows don't), and the hidden menu
    // window is always there.
    public static unsafe void HookWindow(nint hwnd) => Native.SetWindowSubclass(hwnd, &SubclassProc, 0x5E77, 0);

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == Native.WM_SETTINGCHANGE)
        {
            uint action = (uint)wParam; // SPI_SET* action for SystemParametersInfo changes, 0 otherwise
            // Rather than map each action, re-read every spi entry that's been shown: a handful,
            // microseconds each. lParam "ImmersiveColorSet" etc. also lands here: re-read reg
            // entries with that broadcast area (their key watch usually beat us to it).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var e in WinSettings.Entries().Where(e => e.How.Verb == "spi" && watched.Contains(e.Id))) WinSettings.Invalidate(e);
            });
        }
        return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
```

---

## 8. Admin entries: queued, one prompt (roadmap 2.9)

The rule ([roadmap 2.9](roadmap.md)): nothing asks for admin by default; a shielded setting you choose is queued; **one** prompt applies everything queued, after omawin has listed exactly what will change; declining keeps everything else working; **an update never prompts**.

- `AdminQueue` (in `WinSettings`): `%LOCALAPPDATA%\launcher\settings-admin-queue.tsv`, lines `<id>\t<value>`; the same id twice keeps the last.
- In the window, a shielded row's Enter adds to the queue and the list gains a sticky row at the top: **"Apply 2 admin changes ⛨"**. Enter on it opens a confirm view listing each change in words ("Turn on developer mode", "Long paths on (applies to programs started after)", "Restart needed: Virtual Machine Platform") with *Apply* and *Not now*. *Apply* = one UAC prompt.
- `omawin settings admin` does the same from a terminal (prints the list, asks y/N, one prompt).
- `omawin settings apply` (§9) puts admin ones in the same queue; `omawin update` runs `apply --no-admin` and leaves a quiet line on the welcome page / bar ("2 settings need admin: omawin settings admin").
- Install: `install/admin.ps1`'s `Get-AdminPlan` reads the queue too and passes the ids to `install/admin-batch.ps1` as `-Settings <path>`, so the install's single prompt covers them. No second prompt.

The elevated part is a short PowerShell 5.1 script with **fixed verbs**: it never runs a command from the catalog text. It accepts only `HKLM` registry values of entries marked `admin` and a closed list of builtins; it runs as whatever admin account typed the password, so it only does machine-wide things (all admin entries are machine-wide by construction: §2 refuses HKLM without `admin`, and an HKCU entry can't be `admin`-only).

```csharp
public static partial class WinSettings
{
    public static class AdminQueue
    {
        static string FilePath => Path.Combine(MenuModel.StateDir, "settings-admin-queue.tsv");
        public static List<(string Id, string Value)> Items() =>
            File.Exists(FilePath) ? File.ReadAllLines(FilePath).Select(l => l.Split('\t')).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList() : [];
        public static int Count => Items().Count;
        public static void Add(SettingEntry e, string value)
        {
            var items = Items().Where(i => !i.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)).Append((e.Id, value));
            File.WriteAllLines(FilePath, items.Select(i => $"{i.Id}\t{i.Value}"));
        }

        // The words shown before the prompt (2.9: "says exactly what it will change").
        public static List<string> Describe() => Items().Select(i => Find(i.Id) is { } e
            ? $"{e.Name}: {Label(e, i.Value)}" + (e.Takes.HasFlag(Takes.Restart) ? " (needs a restart)" : "")
            : $"{i.Id}: unknown, skipped").ToList();

        // One elevated, hidden powershell.exe (5.1: always there); waits, then re-reads every
        // entry and records history for each that changed. Declined UAC: the queue stays.
        public static async Task<Outcome> ApplyAsync()
        {
            var items = Items();
            if (items.Count == 0) return new(true, "No admin changes waiting");
            var before = items.Select(i => (i.Id, Before: Find(i.Id) is { } e ? ReadNow(e) : null)).ToList();
            // The elevated script gets resolved, validated lines, not catalog text: id, verb,
            // target, type, raw value. It re-checks each against its allow-list.
            var plan = Path.Combine(MenuModel.StateDir, "settings-admin-plan.tsv");
            File.WriteAllLines(plan, items.Select(i => Find(i.Id) is { Admin: true } e ? PlanLine(e, i.Value) : null).OfType<string>());
            var result = Path.Combine(MenuModel.StateDir, "settings-admin-result.tsv");
            File.Delete(result);
            var info = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{Path.Combine(MenuModel.Repo, "bin", "settings-admin.ps1")}\" -Plan \"{plan}\" -Result \"{result}\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            try { using var p = System.Diagnostics.Process.Start(info)!; await p.WaitForExitAsync(); }
            catch (System.ComponentModel.Win32Exception) { return new(false, "Not now: nothing changed"); } // UAC declined (ERROR_CANCELLED)
            foreach (var (id, was) in before)
                if (Find(id) is { } e && ReadNow(e) is var now && now != was && now != null) History.Append(id, was, now, "admin");
            File.Delete(FilePath);
            var failed = File.Exists(result) ? File.ReadAllLines(result).Where(l => l.Contains("\tfail\t")).ToList() : [];
            return failed.Count == 0 ? new(true, $"Applied {items.Count} admin change(s)") : new(false, string.Join("; ", failed));
        }

        static string PlanLine(SettingEntry e, string value) => e.How.Verb switch
        {
            "reg" => string.Join('\t', e.Id, "reg", e.How.Target, e.How.Args[0], Handlers.FromWords(e, value)),
            "builtin" => string.Join('\t', e.Id, "builtin", e.How.Target, string.Join(' ', e.How.Args), value),
            _ => throw new InvalidOperationException($"{e.Id}: no admin path for '{e.How.Verb}'"),
        };
    }
}
```

`bin/settings-admin.ps1` (Windows PowerShell 5.1, run elevated only by the above or by `install/admin-batch.ps1`):

```powershell
<#
    The one elevated batch for shielded settings (plans/02-settings-and-one-search.md §8).
    Started by the menu app (Verb runas) or the install's admin step with a plan file the
    non-elevated side wrote: "<id>`t<verb>`t<target>`t<type or args>`t<value>" per line. Only
    fixed verbs: HKLM registry values, and a closed list of builtins. Nothing in the plan is run
    as a command. Writes "<id>`t<done|fail>`t<text>" per line to -Result. Exits; nothing stays elevated.
#>
param([Parameter(Mandatory)][string]$Plan, [Parameter(Mandatory)][string]$Result)
$ErrorActionPreference = 'Stop'

function Say([string]$Id, [string]$State, [string]$Text = '') {
    [IO.File]::AppendAllText($Result, "$Id`t$State`t$($Text -replace '[\r\n\t]+', ' ')" + [Environment]::NewLine)
}

foreach ($line in [IO.File]::ReadAllLines($Plan)) {
    $p = $line -split "`t"
    if ($p.Count -ne 5) { continue }
    $id, $verb, $target, $arg, $value = $p
    try {
        switch ($verb) {
            'reg' {
                if ($target -notmatch '^HKLM\\(SOFTWARE|SYSTEM)\\') { throw "not an HKLM value: $target" }
                $key = $target.Substring(5, $target.LastIndexOf('\') - 5)
                $name = $target.Substring($target.LastIndexOf('\') + 1)
                $k = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($key)
                switch ($arg) {
                    'dword' { $k.SetValue($name, [int][uint32]$value, [Microsoft.Win32.RegistryValueKind]::DWord) }
                    'sz'    { $k.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::String) }
                    default { throw "type $arg" }
                }
                $k.Close()
            }
            'builtin' {
                switch ($target) {
                    'hibernate' { & powercfg.exe /hibernate $(if ($value -eq 'on') { 'on' } else { 'off' }); if ($LASTEXITCODE) { throw "powercfg $LASTEXITCODE" } }
                    'sudo' {
                        if ($value -notin 'off', 'forceNewWindow', 'disableInput', 'normal') { throw "sudo mode $value" }
                        if ($value -eq 'off') { & sudo.exe config --disable } else { & sudo.exe config --enable $value }   # (check: flags)
                    }
                    'windows-feature' {
                        if ($arg -notin 'VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux', 'Containers-DisposableClientVM', 'Microsoft-Hyper-V-All') { throw "feature $arg" }
                        if ($value -eq 'on') { Enable-WindowsOptionalFeature -Online -FeatureName $arg -All -NoRestart | Out-Null }
                        else { Disable-WindowsOptionalFeature -Online -FeatureName $arg -NoRestart | Out-Null }
                    }
                    default { throw "builtin $target" }
                }
            }
            default { throw "verb $verb" }
        }
        Say $id 'done'
    } catch { Say $id 'fail' $_.Exception.Message }
}
```

A restore point (8.42) is made here, first, only when the batch includes a `restart` entry: it's already elevated, so it costs no extra prompt.

---

## 9. Commands: `omawin setting …` and `omawin settings …`

### The answer channel

Today `app` actions are one-way (a request file + an event; bin/omawin.cs `Send`). `get` and `list` must print. New action kind **`ask`**: the same request with `--reply <pid>` in front; the menu app writes `%LOCALAPPDATA%\launcher\replies\<pid>.txt` (first line: exit code; then output) and sets the named event `Local\omawin-reply-<pid>`. If the menu app isn't running, `Send` already starts `SystemMenu.exe <request>`, whose `OnLaunched` → `HandleRequest(startArgs)` answers the same way (slower, once).

`bin/commands.txt` rows (in a new section after `about`):

```text
setting list | Windows settings omawin can read and change, with their values; an area to narrow: look, explorer, privacy, input, system, display, connections, sound, developer | ask --setting list
setting get | One setting's value: omawin setting get explorer.extensions | ask --setting get
setting set | Change a setting (undoable; admin ones are queued): omawin setting set explorer.hidden on | ask --setting set
setting toggle | Flip a switch, or step a choice: omawin setting toggle look.dark-apps | ask --setting toggle
setting undo | Put back the last setting change | ask --setting undo
settings save | The Windows settings you've changed, into your account folder's settings.txt (-Shared: the PC's shared folder), for a new PC | ask --settings save
settings apply | Set what settings.txt says (the shared folder's, then this account's); admin ones in one prompt (-NoAdmin: queue them) | ask --settings apply
settings admin | The admin changes waiting: listed, then one prompt applies them all | ask --settings admin
```

`bin/omawin.cs` (C# 5: no interpolation, no `?.`, no expression bodies):

```csharp
// In Run's switch:
            case "ask":
                return Ask((rest + " " + string.Join(" ", extra)).Trim());

    // A request whose answer is printed: the menu app writes it to a file named for this
    // process and sets an event; we wait (10 s: a cold start of the menu app included).
    static int Ask(string request)
    {
        string id = Process.GetCurrentProcess().Id.ToString();
        string dir = Path.Combine(StateDir, "replies");
        string reply = Path.Combine(dir, id + ".txt");
        Directory.CreateDirectory(dir);
        if (File.Exists(reply)) File.Delete(reply);
        using (var done = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\omawin-reply-" + id))
        {
            int sent = Send("--reply " + id + " " + request);
            if (sent != 0) return sent;
            if (!done.WaitOne(10000)) return Fail("the menu app didn't answer (omawin restart menu)");
        }
        string[] lines;
        try { lines = File.ReadAllLines(reply); File.Delete(reply); }
        catch (IOException ex) { return Fail("no answer: " + ex.Message); }
        int code = 0;
        if (lines.Length > 0) int.TryParse(lines[0], out code);
        for (int i = 1; i < lines.Length; i++) { if (code == 0) Say(lines[i]); else Fail(lines[i]); }
        return code;
    }
```

`shell/menu-app/Reply.cs`:

```csharp
namespace SystemMenu;

// The other half of omawin.exe's `ask`: run the request off the UI thread, write the answer
// file, signal the waiting omawin.exe.
static class Reply
{
    public static void Answer(string pid, string[] request)
    {
        if (!pid.All(char.IsAsciiDigit)) return; // it names a file and an event: digits only
        Task.Run(() =>
        {
            (int Code, string Text) r;
            try
            {
                r = request switch
                {
                    ["--setting", .. var rest] => WinSettings.Command(rest),
                    ["--settings", .. var rest] => WinSettings.SettingsCommand(rest).GetAwaiter().GetResult(),
                    _ => (2, "unknown request: " + string.Join(' ', request)),
                };
            }
            catch (Exception ex) { App.Log(ex); r = (1, ex.Message); }
            var dir = Path.Combine(MenuModel.StateDir, "replies");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, pid + ".txt"), r.Code + "\n" + r.Text);
            if (EventWaitHandle.TryOpenExisting(@"Local\omawin-reply-" + pid, out var done)) using (done) done.Set();
        });
    }
}
```

`App.HandleRequest` gains, **before** its `if (window == null) return;` (settings need no window, and an early request at sign-in must still be answered):

```csharp
        if (args.Length > 2 && args[0] == "--reply") { Reply.Answer(args[1], args[2..]); return; }
```

`WinSettings.Command`:

```csharp
public static partial class WinSettings
{
    // omawin setting list [area] | get <id> | set <id> <value> [--now] | toggle <id> | undo
    public static (int Code, string Text) Command(string[] a)
    {
        string verb = a.Length > 0 ? a[0] : "list";
        SettingEntry? E(int i) => a.Length > i ? Find(a[i]) : null;
        switch (verb)
        {
            case "list":
            {
                var area = a.Length > 1 ? a[1] : null;
                if (area == "--ids") return (0, string.Join('\n', Entries().Select(e => e.Id))); // tab completion
                var rows = Entries().Where(e => area == null || e.Area == area).ToList();
                if (rows.Count == 0) return (1, $"no area '{area}': {string.Join(", ", Catalog.Areas)}");
                int w = rows.Max(e => e.Id.Length) + 2;
                return (0, string.Join('\n', rows.Select(e =>
                    e.Id.PadRight(w) + (e.Kind is SettingKind.Link or SettingKind.Action ? "↗" : Label(e, ReadNow(e))).PadRight(18)
                    + e.Name + (e.Admin ? "  [admin]" : ""))));
            }
            case "get":
                return E(1) is { } g ? (0, ReadNow(g) ?? "?") : (1, Unknown(a));
            case "set":
                if (E(1) is not { } s || a.Length < 3) return (1, a.Length < 3 ? "omawin setting set <id> <value>" : Unknown(a));
                var r = Set(s, string.Join(' ', a[2..].Where(x => x != "--now")), "command");
                if (r.Ok && a.Contains("--now") && ExplorerRestart.Pending) ExplorerRestart.Now();
                return (r.Ok ? 0 : 1, r.Message);
            case "toggle":
                return E(1) is { } t ? Result(Toggle(t, "command")) : (1, Unknown(a));
            case "undo":
                return Result(UndoLast());
            default:
                return (2, "omawin setting list [area] | get <id> | set <id> <value> | toggle <id> | undo");
        }
        static (int, string) Result(Outcome o) => (o.Ok ? 0 : 1, o.Message);
        static string Unknown(string[] a) => a.Length > 1 ? $"no setting '{a[1]}' (omawin setting list)" : "which setting? (omawin setting list)";
    }
}
```

Tab completion, in `config/powershell/Microsoft.PowerShell_profile.ps1`:

```powershell
Register-ArgumentCompleter -Native -CommandName omawin -ScriptBlock {
    param($word, $ast, $cursor)
    $words = $ast.CommandElements | ForEach-Object { $_.ToString() }
    if ($words.Count -ge 3 -and $words[1] -eq 'setting' -and $words[2] -in 'get', 'set', 'toggle') {
        if (-not $script:OmawinSettingIds) { $script:OmawinSettingIds = omawin.exe setting list --ids }  # once per session
        $script:OmawinSettingIds | Where-Object { $_ -like "$word*" } | ForEach-Object { [Management.Automation.CompletionResult]::new($_) }
    }
}
```

### `omawin settings save | apply` (1B.5)

File: `~/omawin-account/settings.txt` (or `C:\ProgramData\omawin\shared\settings.txt` with `-Shared`), **not** under `default/launcher/`: it's values, not catalog lines.

```text
# Windows settings for this account, set on a new PC by `omawin settings apply` (install runs it).
# Written by `omawin settings save`; edit freely. id = value (omawin setting list shows both).
look.dark-apps = on
explorer.extensions = on
explorer.hidden = on
input.mouse-accel = off
developer.long-paths = on
```

```csharp
public static partial class WinSettings
{
    static string SettingsFile(bool shared) => Path.Combine(shared ? MenuModel.Shared : MenuModel.Work, "settings.txt");

    public static async Task<(int, string)> SettingsCommand(string[] a)
    {
        bool shared = a.Contains("-Shared"), noAdmin = a.Contains("-NoAdmin");
        switch (a.FirstOrDefault())
        {
            case "save": return Save(shared);
            case "apply": return await ApplyFiles(noAdmin);
            case "admin":
                var o = await AdminQueue.ApplyAsync(); // the terminal already showed the list and asked (see below)
                return (o.Ok ? 0 : 1, o.Message);
            default: return (2, "omawin settings save [-Shared] | apply [-NoAdmin] | admin");
        }
    }

    // Merge, never clobber: existing lines (and comments) keep their place, values are updated,
    // newly changed ids are appended. Ids saved: those already in the file, plus every one
    // changed through omawin (the history).
    static (int, string) Save(bool shared)
    {
        var path = SettingsFile(shared);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() :
            ["# Windows settings, set on a new PC by `omawin settings apply`. id = value (omawin setting list)."];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
            if (ParseValueLine(lines[i]) is var (id, _) && Find(id) is { } e && ReadNow(e) is { } v)
            { lines[i] = $"{e.Id} = {v}"; seen.Add(e.Id); }
        int added = 0;
        foreach (var id in History.ChangedIds())
            if (!seen.Contains(id) && Find(id) is { Kind: SettingKind.Switch or SettingKind.Choice or SettingKind.Slider } e && ReadNow(e) is { } v)
            { lines.Add($"{e.Id} = {v}"); seen.Add(e.Id); added++; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return (0, $"{seen.Count} settings in {path} ({added} new)");
    }

    // The shared folder's, then the account's (the account wins, as for launcher files).
    static async Task<(int, string)> ApplyFiles(bool noAdmin)
    {
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[] { SettingsFile(true), SettingsFile(false) }.Where(File.Exists))
            foreach (var l in File.ReadAllLines(f))
                if (ParseValueLine(l) is var (id, v)) wanted[id] = v;
        var report = new List<string>();
        int failed = 0;
        foreach (var (id, v) in wanted)
        {
            if (Find(id) is not { } e) { report.Add($"skipped {id}: not in this omawin's catalog (or not on this Windows build)"); continue; }
            var r = Set(e, v, "apply");
            if (!r.Ok) failed++;
            if (r.Ok && r.Message.Contains("already")) continue;
            report.Add(r.Message);
        }
        if (!noAdmin && AdminQueue.Count > 0)
        {
            // From `omawin settings apply` typed by you: you asked, so this is the one prompt.
            var o = await AdminQueue.ApplyAsync();
            report.Add(o.Message);
            if (!o.Ok) failed++;
        }
        else if (AdminQueue.Count > 0) report.Add($"{AdminQueue.Count} admin change(s) waiting: omawin settings admin");
        return (failed == 0 ? 0 : 1, report.Count == 0 ? "Everything already as settings.txt says" : string.Join('\n', report));
    }

    static (string Id, string Value)? ParseValueLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] == '#' || line.IndexOf('=') is not (> 0 and var eq)) return null;
        return (line[..eq].Trim(), line[(eq + 1)..].Trim());
    }
}
```

`omawin settings admin` from a terminal shows the list before the prompt: the `ask` round trip for `--settings admin` first returns the description with code 3 ("confirm"), omawin.cs prints it and asks `Apply these? [y/N]`, and on `y` sends `--settings admin --yes`, which runs `ApplyAsync`. (Implement as `if (!a.Contains("--yes")) return (3, string.Join('\n', AdminQueue.Describe()))` and a code-3 branch in `Ask`.)

Install: a new line in `install/steps.txt` after the desktop step, `install/settings.ps1` (5.1): `omawin.exe settings apply -NoAdmin`; `install/admin.ps1`'s plan adds the queue (§8). `omawin update` runs `settings apply -NoAdmin` only when `settings.txt` changed since the last run (its stamp in `%LOCALAPPDATA%\launcher\settings-applied`).

The no-ads tweaks and Explorer's developer defaults (6.8) move into the catalog as entries over time; `bin/no-ads.ps1` keeps working meanwhile (its OEM modules aren't settings and stay a script). Not part of this plan's first cut.

---

## 10. Windows' own settings index: `shell/menu-app/Settings/Index.cs`

Every searchable setting, as rows that open its page (switches come from the catalog; the index is the long tail). The file is undocumented: everything here is **(check)** against the PC's `C:\Windows\ImmersiveControlPanel\Settings\AllSystemSettings_{253E530E-387D-4BC2-959D-E6F86122E5F2}.xml` first. From memory each setting is a `<SearchableContent>` element holding `<ApplicationInformation>` (`<AppID>`, `<DeepLink>`, `<Icon>`), `<SettingIdentity>` (`<PageID>`, `<HostID>`, `<GroupID>`, `<SettingID>`) and `<SettingInformation>` (`<Name>`, `<Description>`, `<Keywords>`, `<HighKeywords>`), names as `@resource,-id` strings. The reader below is written to survive layout changes: it collects text by element name anywhere inside each `SearchableContent`.

Cache: `%LOCALAPPDATA%\launcher\settings-index.tsv`, first line `#\t<xml path>\t<xml write ticks>\t<UI language>`, then `name\tpage\tkeywords\turi`. Rebuilt in the background (from `Background.Start`) when the XML's stamp or the UI language changes.

```csharp
using System.Xml;

namespace SystemMenu;

public sealed record IndexedSetting(string Name, string Page, string Keywords, string Uri);

public static unsafe class SettingsIndex
{
    static string Cache => Path.Combine(MenuModel.StateDir, "settings-index.tsv");
    static List<IndexedSetting>? loaded;

    public static List<IndexedSetting> Load()
    {
        if (loaded != null) return loaded;
        try
        {
            var lines = File.ReadAllLines(Cache);
            return loaded = lines.Skip(1).Select(l => l.Split('\t')).Where(p => p.Length == 4)
                .Select(p => new IndexedSetting(p[0], p[1], p[2], p[3])).ToList();
        }
        catch { return loaded = Fallback(); }
    }

    // Background.Start: rebuild if Windows updated the file or the language changed.
    public static void Refresh()
    {
        var xml = Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"ImmersiveControlPanel\Settings"), "AllSystemSettings_*.xml").FirstOrDefault();
        if (xml == null) return; // Fallback() covers it
        var stamp = $"#\t{xml}\t{File.GetLastWriteTimeUtc(xml).Ticks}\t{System.Globalization.CultureInfo.CurrentUICulture.Name}";
        try { if (File.ReadLines(Cache).FirstOrDefault() == stamp) return; } catch { }
        var rows = Read(xml);
        if (rows.Count < 50) { App.Log(new FormatException($"settings index: only {rows.Count} rows from {xml}; format changed?")); return; }
        File.WriteAllLines(Cache, rows.Select(r => $"{Clean(r.Name)}\t{Clean(r.Page)}\t{Clean(r.Keywords)}\t{r.Uri}").Prepend(stamp));
        loaded = null;
    }

    public static List<IndexedSetting> Read(string xmlPath)
    {
        var rows = new List<IndexedSetting>();
        var seen = new HashSet<(string, string)>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true, XmlResolver = null };
        using var reader = XmlReader.Create(xmlPath, settings);
        Dictionary<string, string>? item = null;
        string? field = null;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.LocalName == "SearchableContent":
                    item = new(StringComparer.Ordinal); break;
                case XmlNodeType.Element when item != null:
                    field = reader.LocalName; break;
                case XmlNodeType.Text or XmlNodeType.CDATA when item != null && field != null:
                    item.TryAdd(field, reader.Value.Trim()); break; // first of each name wins
                case XmlNodeType.EndElement when reader.LocalName == "SearchableContent" && item != null:
                    var name = Resolve(item.GetValueOrDefault("Name"));
                    var page = item.GetValueOrDefault("PageID") ?? "";
                    var uri = item.GetValueOrDefault("DeepLink") is { Length: > 0 } link && link.StartsWith("ms-settings:") ? link
                        : PageUris.GetValueOrDefault(page, "ms-settings:");
                    if (name is { Length: > 0 } && seen.Add((name, uri)))
                        rows.Add(new(name, PageLabel(page), string.Join(' ', new[] { "Keywords", "HighKeywords", "Description" }
                            .Select(k => Resolve(item.GetValueOrDefault(k))).Where(s => s is { Length: > 0 })), uri));
                    item = null; field = null; break;
            }
        }
        return rows;
    }

    // "@%SystemRoot%\system32\shell32.dll,-12345" or "@{Package?ms-resource://...}" -> the text
    // in this PC's language. Plain text comes back as it is.
    public static string? Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '@') return text;
        char* buffer = stackalloc char[1024];
        return Native.SHLoadIndirectString(text, buffer, 1024, 0) == 0 ? new string(buffer) : null;
    }

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    // PageID -> ms-settings URI, for entries without a DeepLink. (check: the PageIDs; extend
    // from the PC's file with a ripgrep over it, roadmap 1B "fff and ripgrep here")
    static readonly Dictionary<string, string> PageUris = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SettingsPageDisplay"] = "ms-settings:display",
        ["SettingsPageAppsSizes"] = "ms-settings:appsfeatures",
        ["SettingsPagePowerSleep"] = "ms-settings:powersleep",
        ["SettingsPageNetworkWiFi"] = "ms-settings:network-wifi",
        ["SettingsPageMouseTouchpad"] = "ms-settings:mousetouchpad",
        ["SettingsPageBluetooth"] = "ms-settings:bluetooth",
    };
    static string PageLabel(string pageId) => PageUris.TryGetValue(pageId, out var u) && Pages.FirstOrDefault(p => p.Uri == u) is { } p ? p.Page : "Settings";

    // --- the fallback: default/launcher/settings-pages.txt (and its layers) -----------------------
    // "uri | name | page | keywords". Used when the XML is missing or unreadable, and always
    // merged in (it carries the classic consoles the index doesn't have).
    static List<IndexedSetting>? pages;
    public static List<IndexedSetting> Pages => pages ??= MenuModel.LayeredLines("settings-pages.txt")
        .Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')
        .Select(l => l.Split('|').Select(p => p.Trim()).ToArray()).Where(p => p.Length == 4)
        .Select(p => new IndexedSetting(p[1], p[2], p[3], p[0])).ToList();

    static List<IndexedSetting> Fallback() => [.. Pages];
}
```

`default/launcher/settings-pages.txt` (the documented `ms-settings:` URIs, plus consoles and classic dialogs, 1B.1):

```text
# Settings pages and consoles, for search when Windows' own index can't be read, and the
# classic tools it doesn't list. uri | name | page | keywords
ms-settings:display | Display | System › Display | monitor resolution scale hdr refresh
ms-settings:nightlight | Night light | System › Display | blue light warm
ms-settings:sound | Sound | System › Sound | speakers microphone volume
ms-settings:notifications | Notifications | System › Notifications | toast banners
ms-settings:quiethours | Do not disturb | System › Notifications | focus quiet dnd
ms-settings:powersleep | Power and sleep | System › Power | screen timeout sleep
ms-settings:batterysaver | Battery saver | System › Power | energy saver battery
ms-settings:storagesense | Storage | System › Storage | disk space cleanup
ms-settings:multitasking | Multitasking | System › Multitasking | snap alt tab
ms-settings:clipboard | Clipboard | System › Clipboard | history sync
ms-settings:about | About this PC | System › About | rename pc specs windows version
ms-settings:developers | For developers | System › For developers | developer mode sudo
ms-settings:bluetooth | Bluetooth and devices | Bluetooth & devices | pair devices
ms-settings:printers | Printers and scanners | Bluetooth & devices | printer scanner
ms-settings:mousetouchpad | Mouse | Bluetooth & devices › Mouse | pointer scroll
ms-settings:devices-touchpad | Touchpad | Bluetooth & devices › Touchpad | gestures tap
ms-settings:typing | Typing | Bluetooth & devices › Typing | autocorrect keyboard
ms-settings:network-wifi | Wi-Fi | Network & internet | wireless networks
ms-settings:network-ethernet | Ethernet | Network & internet | wired dns
ms-settings:network-vpn | VPN | Network & internet | vpn
ms-settings:network-proxy | Proxy | Network & internet | proxy pac
ms-settings:network-mobilehotspot | Mobile hotspot | Network & internet | tether
ms-settings:network-airplanemode | Airplane mode | Network & internet | flight
ms-settings:personalization-background | Background | Personalization | wallpaper
ms-settings:colors | Colours | Personalization | dark mode accent transparency
ms-settings:lockscreen | Lock screen | Personalization | lock
ms-settings:taskbar | Taskbar | Personalization › Taskbar | taskbar icons
ms-settings:personalization-start | Start | Personalization › Start | start menu recommendations
ms-settings:appsfeatures | Installed apps | Apps | uninstall programs
ms-settings:defaultapps | Default apps | Apps | browser default open with
ms-settings:startupapps | Startup apps | Apps | autostart login
ms-settings:optionalfeatures | Optional features | System › Optional features | features
ms-settings:privacy-webcam | Camera | Privacy & security | camera permission
ms-settings:privacy-microphone | Microphone | Privacy & security | mic permission
ms-settings:privacy-location | Location | Privacy & security | gps
ms-settings:privacy-general | General privacy | Privacy & security | advertising id
ms-settings:windowsupdate | Windows Update | Windows Update | updates patch
ms-settings:dateandtime | Date and time | Time & language | clock timezone
ms-settings:regionlanguage | Language and region | Time & language | locale keyboard layout
ms-settings:gaming-gamebar | Game Bar | Gaming | xbox capture
ms-settings:gaming-gamemode | Game Mode | Gaming | performance
control.exe | Control Panel | Classic | control panel
devmgmt.msc | Device Manager | Classic | drivers hardware
services.msc | Services | Classic | services daemons
eventvwr.msc | Event Viewer | Classic | logs events errors
diskmgmt.msc | Disk Management | Classic | partitions volumes drives
taskschd.msc | Task Scheduler | Classic | scheduled tasks cron
ncpa.cpl | Network adapters | Classic | adapter ipv4 dns
appwiz.cpl | Programs and Features | Classic | uninstall msi
mmsys.cpl | Sound (classic) | Classic | playback recording devices
SystemPropertiesAdvanced.exe | Advanced system settings | Classic | performance virtual memory
rundll32.exe sysdm.cpl,EditEnvironmentVariables | Environment variables | Classic | path env
```

Control Panel's own items (the shell folder `shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}`, "All Control Panel Items") can be enumerated exactly as `AppsFolder.cs` enumerates `FOLDERID_AppsFolder` (IShellItem, BHID_EnumItems): a later addition, same code with another folder id.

---

## 11. One Search: the sources

one-search.md §1-2 define `Result`, `Tag`, `SettingKind`, `ISearchSource`. The sources wrap what exists; none of them ranks (the merger does). Each returns its own candidates for a query with **all** its items when the query is empty (the merger's filter view sorts them).

Corrections to one-search.md's model needed by the code below:
- one-search.md's `SettingKind` enum collides with §2's catalog enum: name the result's one `RowKind { None, Switch, Choice, Slider, Link }` and add `Func<string?>? State` (current value word) instead of `IsOn` / `Value` (one lazy getter covers switch, choice and slider).
- Add `string? Icon`, `string? IconKey` (app icons, `AppIcons`), `List<int>? Positions` (highlight, filled by the merger from `Fuzzy.Match`).

```csharp
namespace SystemMenu.Search;

// --- apps and your launcher items: Launcher.Load() ---------------------------------------------
public sealed class AppSource : ISearchSource
{
    public Tag Tag => Tag.App;
    public event Action? Updated;   // Background.cs rewrites apps.tsv: raise from its callback
    public IReadOnlyList<Result> Query(string query, CancellationToken cancel) =>
        Launcher.Load()
            // Built-in page items (@projects, @clipboard, @menu, @files) are filters or views now,
            // not results; @vpn and plain items stay.
            .Where(i => i.BuiltIn is null or "vpn")
            .Select(i => new Result(
                Id: "app:" + i.Key, Tag: Tag.App, Name: i.Name, Detail: i.IsApp ? "App" : "Yours",
                Keywords: string.Join(' ', i.Keywords),
                Primary: () => { MainWindow.Current!.Launch(i); return Task.CompletedTask; },
                Actions: [
                    new("Run as administrator", "Ctrl Shift Enter", () => Run.AsAdmin(i)),
                    new("Show in folder", null, () => Run.Reveal(i)),
                    new("Pin to home", "Ctrl P", () => Pins.Toggle("app:" + i.Key)),
                ]) { Icon = i.Icon, IconKey = i.IconKey })
            .ToList();
}

// --- settings: the catalog's switches, then Windows' index as links ---------------------------
public sealed class SettingSource : ISearchSource
{
    public Tag Tag => Tag.Setting;
    public event Action? Updated;
    public SettingSource() => WinSettings.Changed += _ => Updated?.Invoke(); // a row's state changed: redraw

    public IReadOnlyList<Result> Query(string query, CancellationToken cancel)
    {
        var list = new List<Result>();
        foreach (var e in WinSettings.Entries())
            list.Add(new Result(
                Id: "setting:" + e.Id, Tag: Tag.Setting, Name: e.Name,
                Detail: AreaLabel(e.Area) + (e.Admin ? " · needs admin" : "") + (e.Takes.HasFlag(Takes.SignOut) ? " · after sign-out" : ""),
                Keywords: e.Keywords,
                Primary: () => Task.Run(() => Snip.Say(WinSettings.Toggle(e).Message)))
            {
                Row = e.Kind switch { SettingKind.Switch => RowKind.Switch, SettingKind.Choice => RowKind.Choice, SettingKind.Slider => RowKind.Slider, _ => RowKind.Link },
                State = () => WinSettings.Peek(e),     // lazy: only rows drawn ask
                Admin = e.Admin,
                Step = by => Task.Run(() => WinSettings.Step(e, by)), // ← / → on a choice or slider
                Icon = e.Icon == "-" ? AreaIcon(e.Area) : e.Icon,
            });
        // The long tail: every setting Windows indexes, as "Opens Settings" links (guardrail 8).
        // Skipped for empty queries (thousands of rows; the filter's empty view is the catalog by area).
        if (query.Length > 0)
            foreach (var s in SettingsIndex.Load().Concat(SettingsIndex.Pages))
                list.Add(new Result("page:" + s.Uri + "#" + s.Name, Tag.Setting, s.Name, s.Page + " · opens Settings", s.Keywords,
                    Primary: () => { QuickSettings.Open(s.Uri); return Task.CompletedTask; }) { Row = RowKind.Link });
        if (WinSettings.AdminQueue.Count is > 0 and var n)
            list.Insert(0, new Result("admin-queue", Tag.Setting, $"Apply {n} admin change{(n > 1 ? "s" : "")}", "One prompt", "admin apply",
                Primary: () => MainWindow.Current!.ConfirmAdmin()) { Admin = true, Pinned = true }); // Pinned: the merger keeps it first
        if (ExplorerRestart.Pending)
            list.Insert(0, new Result("explorer-restart", Tag.Setting, "Restart Explorer to apply", "The taskbar blinks once", "explorer restart",
                Primary: () => { ExplorerRestart.Now(); return Task.CompletedTask; }) { Pinned = true });
        return list;
    }

    static string AreaLabel(string area) => area switch
    {
        "look" => "Look", "explorer" => "File Explorer", "privacy" => "Privacy", "input" => "Mouse and keyboard",
        "system" => "System", "display" => "Display", "connections" => "Connections", "sound" => "Sound", "developer" => "Developer", _ => area,
    };
    static string AreaIcon(string area) => "\uF013"; // one glyph per area; pick from default/launcher/menu.txt's
}

// --- commands: bin/commands.txt and menu.txt's actions -------------------------------------
// One result per command (omawin help's list) and per menu.txt row with an action, so "update",
// "theme next background" and "lock" are all here. Rows already reachable as an App or Setting
// (menu.txt's `run ms-settings:…`) are dropped to avoid doubles.
public sealed class CommandSource : ISearchSource
{
    public Tag Tag => Tag.Command;
    public event Action? Updated;
    public sealed record OmawinCommand(string Name, string Summary, string Action);

    static List<OmawinCommand>? cache; static DateTime stamp;
    public static List<OmawinCommand> Commands()
    {
        var file = Path.Combine(MenuModel.Repo, "bin", "commands.txt");
        var now = File.GetLastWriteTimeUtc(file);
        if (cache != null && now == stamp) return cache;
        stamp = now;
        return cache = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray()).Where(p => p.Length == 3)
            .Select(p => new OmawinCommand(p[0], p[1], p[2])).ToList();
    }

    public IReadOnlyList<Result> Query(string query, CancellationToken cancel)
    {
        var list = new List<Result>();
        foreach (var c in Commands())
        {
            // Background starters (start, start zebar, …) and the keys' own entry points are noise.
            if (c.Name.StartsWith("start") || c.Name is "launcher" or "files" or "menu" or "search") continue;
            // "> theme set gruvbox": once the typed text starts with a command's full name, the rest are its arguments.
            var args = query.StartsWith(c.Name + " ", StringComparison.OrdinalIgnoreCase) ? query[(c.Name.Length + 1)..] : "";
            bool needsArgs = c.Summary.Contains('<');
            list.Add(new Result("cmd:" + c.Name, Tag.Command, "omawin " + c.Name + (args.Length > 0 ? " " + args : ""), First(c.Summary), c.Summary,
                Primary: () =>
                {
                    if (needsArgs && args.Length == 0) { MainWindow.Current!.TypeText("> " + c.Name + " "); return Task.CompletedTask; } // fill the bar, wait for arguments
                    MenuModel.Omawin([.. c.Name.Split(' '), .. Catalog.Tokens(args)]);
                    MainWindow.Current!.Dismiss();
                    return Task.CompletedTask;
                }) { Key = KeyFor(c.Name) });
        }
        var entries = MenuModel.Load(MenuModel.Installed());
        foreach (var e in entries.Values.Where(e => !e.IsSubmenu && !(e.Kind == "run" && e.Argument.StartsWith("ms-settings:"))))
            list.Add(new Result("menu:" + e.Id, Tag.Command, e.Label, MenuModel.PathLabel(entries, e.Parent), e.Id.Replace('.', ' '),
                Primary: () => { MainWindow.Current!.Pick(e); return Task.CompletedTask; }) { Icon = e.Icon, Confirm = e.Confirm });
        return list;
    }

    static string First(string summary) => summary.Split(':', 2)[0];
    // "Win+Shift+S" from the summary's "(Win+…)" so the row shows its key. Cheap and honest: it's the text the key list uses.
    static string? KeyFor(string name) => null; // later: Shortcuts.Load() matched by command
}

// --- files: FileSearch (fff); slow, so it answers later ------------------------------------
public sealed class FileSource : ISearchSource
{
    public Tag Tag => Tag.File;
    public int MinQueryLength => 3; // guardrail 5; the Files filter / "/" sets it to 0 (the merger asks)
    public event Action? Updated;
    List<Result> latest = [];
    string asked = "";

    public IReadOnlyList<Result> Query(string query, CancellationToken cancel)
    {
        if (query != asked)
        {
            asked = query;
            // fff is fast but not free: off the UI thread; what's on screen stays until it lands (guardrail 6).
            Task.Run(() =>
            {
                var hits = FileSearch.Search(query, limit: 40);
                if (cancel.IsCancellationRequested) return;
                latest = hits.Select(h => new Result("file:" + h.Path, Tag.File, h.Name,
                    h.RootLabel + (h.RelativeDir.Length > 0 ? " › " + h.RelativeDir.Replace('\\', ' ').Replace(" ", " › ") : ""),
                    Primary: () => { FileSearch.Opened(h.Path, query); MainWindow.Current!.OpenFile(h, query, reveal: false); return Task.CompletedTask; },
                    Actions: [
                        new("Show in folder", "Ctrl Enter", () => { MainWindow.Current!.OpenFile(h, query, reveal: true); return Task.CompletedTask; }),
                        new("Copy path", "Ctrl C", () => { ClipboardHistory.SetText(h.Path); return Task.CompletedTask; }),
                        new("Terminal here", null, () => { MenuModel.Omawin("launch", "terminal", "--dir", h.IsDir ? h.Path : Path.GetDirectoryName(h.Path)!); return Task.CompletedTask; }),
                    ]) { FileHit = h }).ToList(); // FileHit: the preview pane draws from it (ShowPreview today)
                Updated?.Invoke();
            }, cancel);
        }
        return latest; // the previous answer until the new one arrives
    }
}

// --- open windows: GlazeWM's IPC (Bar.Wm) ---------------------------------------------------
// GlazeWm.cs subscribes to events and queries monitors; add a "query windows" round trip.
// (check: GlazeWM v3 answers {"messageType":"client_response","clientMessage":"query windows",
//  "data":{"windows":[{"id","title","processName","handle",...}]}})
public sealed class WindowSource : ISearchSource
{
    public Tag Tag => Tag.Window;
    public event Action? Updated;
    List<Result> latest = [];
    DateTime asked;

    public IReadOnlyList<Result> Query(string query, CancellationToken cancel)
    {
        if (DateTime.UtcNow - asked > TimeSpan.FromSeconds(1)) // once per showing, near enough
        {
            asked = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                var windows = await Bar.Wm.QueryWindowsAsync(); // List<(string Id, string Title, string Process)>
                latest = windows.Select(w => new Result("win:" + w.Id, Tag.Window, w.Title, w.Process,
                    Primary: () => { Bar.Wm.Command($"focus --container-id {w.Id}"); MainWindow.Current!.Dismiss(); return Task.CompletedTask; },
                    Actions: [new("Close", "Ctrl W", () => { Bar.Wm.Command($"close --container-id {w.Id}"); return Task.CompletedTask; })])).ToList();
                Updated?.Invoke();
            });
        }
        return latest;
    }
}
```

`GlazeWm.QueryWindowsAsync` (in `Bar/GlazeWm.cs`): keep a `ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>` keyed by the sent message; `Handle` routes `client_response` messages whose `clientMessage` matches to the waiting task; parse with `JsonDocument` (no reflection), 500 ms timeout returning the last list.

`ProjectSource` (`Projects.Latest`, `Projects.Open`), `ClipboardSource` (`ClipboardHistory.Entries()`, `SetText`) and `ThemeSource` (`MenuModel.Themes()`, `SetTheme`) follow the same shape; they exist as pages today, so they're mechanical.

Merger fixes against one-search.md §4 (it was written ahead of the code):
- `Fuzzy.Score` doesn't exist: it's `Fuzzy.Match(text, query, positions)`, returning `Fuzzy.None` (−∞) for no match, not 0. Filter with `!= Fuzzy.None`, and add `Launcher.Search`'s extras (+2 for a name start, −0.02 per extra character), so apps rank the same as today.
- `History.Boost` is an instance method on `History.Load()`, keyed by `LaunchItem.Key`. Make it a parameter so the merger stays pure and testable: `Merge(candidates, query, Func<string, string, double> boost)`; the window passes `(id, q) => History.Load().Boost(id, q)`. History keys become result ids (`app:<key>`, `setting:<id>`), and a one-time conversion of `launch-history.json` keys (prefix `app:`) is a migration (`migrations/<date>-search-history-ids.ps1`).
- `Pinned` results (admin queue, Explorer restart) sort first regardless of score.
- `MinQueryLength` is per source, but under a filter (or `/`) the File source's minimum is 0.

---

## 12. The window

### XAML sketch (`MainWindow.xaml`, evolved, not a new window)

The current structure maps directly: search row → bar + token; `Scopes` → filter tabs; `Viewport/Rows/Tiles` → list or bento; `Preview` stays; the footer stays. New: the token, the tabs as underlined text, and the actions flyout.

```xml
<Window x:Class="SystemMenu.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="omawin">
    <!-- Colors, glow and hairlines from the theme in code (ApplyTheme); guardrail 12's look. -->
    <Grid x:Name="Root">
        <Grid.RowDefinitions>
            <RowDefinition Height="64" />   <!-- the bar -->
            <RowDefinition Height="34" />   <!-- filter tabs -->
            <RowDefinition Height="1" />    <!-- hairline + sheen -->
            <RowDefinition Height="*" />    <!-- list + preview, or the bento home -->
            <RowDefinition Height="34" />   <!-- footer -->
        </Grid.RowDefinitions>

        <!-- The bar: icon, the active filter as a token, the typed text (drawn), the caret. The real
             TextBox is invisible and takes the keys (as today). -->
        <Grid Padding="20,0,20,0" ColumnSpacing="10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="SearchIcon" FontSize="18" VerticalAlignment="Center" />
            <Border x:Name="FilterToken" Grid.Column="1" Visibility="Collapsed" BorderThickness="1"
                    CornerRadius="4" Padding="8,2,8,2" VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <TextBlock x:Name="FilterTokenIcon" FontSize="12" />
                    <!-- small uppercase monospace with wide tracking: "SETTINGS" -->
                    <TextBlock x:Name="FilterTokenText" FontSize="11" FontFamily="Cascadia Mono" CharacterSpacing="120" />
                </StackPanel>
            </Border>
            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock x:Name="QueryText" FontSize="22" FontWeight="Light" />
                <Border x:Name="Caret" Width="2" Height="24" Margin="1,0,0,0" CornerRadius="1" />
                <TextBlock x:Name="Placeholder" FontSize="22" FontWeight="Light" Margin="4,0,0,0" />
            </StackPanel>
            <TextBlock x:Name="ResultCount" Grid.Column="3" FontSize="11" FontFamily="Cascadia Mono" VerticalAlignment="Center" />
            <TextBox x:Name="Input" Grid.Column="2" Opacity="0" Width="1" Height="1" HorizontalAlignment="Left" IsSpellCheckEnabled="False" />
        </Grid>

        <!-- Filter tabs: All, Apps, Settings, Commands, Files, Windows, Projects, Clipboard, Themes.
             Underlined, not pills; each has its fixed colour, icon and word (guardrail 3). Built in
             code (SearchTabs), selected one underlined in its colour. -->
        <StackPanel x:Name="Tabs" Grid.Row="1" Orientation="Horizontal" Spacing="18" Padding="20,0,20,0" />

        <Grid x:Name="DividerLine" Grid.Row="2">
            <Border x:Name="Divider" Opacity="0.7" />
            <Border x:Name="Sheen" Width="180" HorizontalAlignment="Left" />
        </Grid>

        <Grid x:Name="Body" Grid.Row="3">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition x:Name="PreviewColumn" Width="0" />  <!-- 380 while results show -->
            </Grid.ColumnDefinitions>
            <!-- Rows (pooled RowView, as today) or the bento home's tiles (MainWindow.Tiles.cs). A
                 row: tag colour bar + icon, name with matched letters, dimmed detail, and on the
                 right the tag word, a setting's pill (ON glowing green / OFF outlined), a choice's
                 "‹ Balanced ›", a key ("Win Shift S"), a shield, or ↗. -->
            <Grid x:Name="Viewport" Padding="8">
                <Canvas x:Name="Pane">
                    <Grid x:Name="ListLayer">
                        <StackPanel x:Name="Rows" Spacing="2" />
                        <Grid x:Name="Tiles" ColumnSpacing="6" RowSpacing="6" Visibility="Collapsed" />
                    </Grid>
                </Canvas>
            </Grid>
            <!-- The selected result: big icon or picture, name, where it is, facts, text preview,
                 and its main action as a button with its key. -->
            <Border x:Name="Preview" Grid.Column="1" Margin="0,8,12,10" Padding="18,16,18,16"
                    CornerRadius="12" BorderThickness="1" Visibility="Collapsed">
                <Grid RowSpacing="12">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" /><RowDefinition Height="*" /><RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>
                    <Border x:Name="PreviewArt" Height="150" CornerRadius="10" />
                    <StackPanel Grid.Row="1" Spacing="4">
                        <TextBlock x:Name="PreviewName" FontSize="17" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                        <StackPanel x:Name="PreviewPath" Orientation="Horizontal" Spacing="4" />
                    </StackPanel>
                    <StackPanel x:Name="PreviewFacts" Grid.Row="2" Orientation="Horizontal" Spacing="8" />
                    <Border x:Name="PreviewTextBox" Grid.Row="3" CornerRadius="8" Padding="12,10,12,10">
                        <TextBlock x:Name="PreviewText" FontSize="11.5" TextWrapping="NoWrap" />
                    </Border>
                    <Border x:Name="PreviewAction" Grid.Row="4" BorderThickness="1" CornerRadius="6" Padding="10,6,10,6">
                        <Grid><TextBlock x:Name="PreviewActionLabel" /><TextBlock x:Name="PreviewActionKey" HorizontalAlignment="Right" FontFamily="Cascadia Mono" FontSize="11" /></Grid>
                    </Border>
                </Grid>
            </Border>
            <!-- Ctrl+K: the selected result's actions, over the preview's corner. Its own small
                 list (same RowView look), its own selection; Esc closes it first (guardrail 9). -->
            <Border x:Name="Actions" Grid.ColumnSpan="2" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                    Margin="0,0,16,12" Width="300" Padding="6" CornerRadius="10" BorderThickness="1" Visibility="Collapsed">
                <StackPanel x:Name="ActionRows" Spacing="2" />
            </Border>
        </Grid>

        <!-- Footer: what's shown on the left (or the theme as one gradient bar on the home); keys on the right. -->
        <Grid x:Name="FooterBar" Grid.Row="4" Padding="16,0,12,0">
            <TextBlock x:Name="FooterLeft" FontSize="11" FontFamily="Cascadia Mono" CharacterSpacing="80" VerticalAlignment="Center" />
            <Border x:Name="ThemeBar" Height="3" Width="120" CornerRadius="1.5" HorizontalAlignment="Left" Visibility="Collapsed" />
            <StackPanel x:Name="Keys" Orientation="Horizontal" Spacing="14" HorizontalAlignment="Right" VerticalAlignment="Center" />
        </Grid>
    </Grid>
</Window>
```

### Render with sources (`MainWindow.Search.cs`)

```csharp
public sealed partial class MainWindow
{
    public static MainWindow? Current { get; private set; } // set in the constructor
    Tag? filter;
    bool browsing;        // `--search command --browse`: menu.txt's tree under Commands
    CancellationTokenSource? searchCancel;
    List<Result> shown = [];
    readonly ISearchSource[] sources =
        [new AppSource(), new SettingSource(), new CommandSource(), new FileSource(), new WindowSource(),
         new ProjectSource(), new ClipboardSource(), new ThemeSource()];

    void HookSources()
    {
        // A slow source answered: merge again on the UI thread without moving the selection.
        foreach (var s in sources) s.Updated += () => DispatcherQueue.TryEnqueue(() => RenderSearch(fromUpdate: true));
        // A setting changed elsewhere: redraw only the rows whose state getter is for it.
        WinSettings.Changed += id => DispatcherQueue.TryEnqueue(() => RefreshRowState("setting:" + id));
    }

    void RenderSearch(bool fromUpdate = false)
    {
        var q = QueryParser.Parse(Input.Text, filter);
        // Empty bar, no filter: the bento home. Empty bar with a filter: that tag's own view
        // (themes as a grid, settings by area, browse tree under Commands).
        if (q.Text.Length == 0 && q.Filter is null) { ShowTiles(OneHome()); return; }
        if (q.Text.Length == 0 && q.Filter == Tag.Theme) { ShowTiles(ThemePicker("")); return; }

        if (!fromUpdate) { searchCancel?.Cancel(); searchCancel = new(); }
        var token = searchCancel!.Token;
        var candidates = sources
            .Where(s => q.Filter is null || s.Tag == q.Filter)
            .Where(s => q.Filter is not null || q.Text.Length >= s.MinQueryLength)
            .SelectMany(s => s.Query(q.Text, token));
        var fresh = Merger.Merge(candidates, q, (id, text) => History.Load().Boost(id, text));
        shown = fromUpdate ? Merger.Stable(shown, fresh, selected) : fresh;
        DrawRows(shown);   // pooled RowViews; rows read State() only as they're drawn
        ShowPreviewFor(shown.ElementAtOrDefault(selected));
        SetTabs(q.Filter);
        SetKeys(("↑↓", "move"), ("↵", Verb(shown.ElementAtOrDefault(selected))), ("ctrl k", "actions"), ("tab", "filter"), ("esc", "back"));
    }

    static string Verb(Result? r) => r switch
    {
        null => "",
        { Row: RowKind.Switch } => "switch",
        { Row: RowKind.Choice } => "next",
        { Row: RowKind.Link } => "open ↗",
        { Tag: Tag.Command } => "run",
        _ => "open",
    };
}
```

### The bento home, reusing `MainWindow.Tiles.cs`

`TileLayout`, `TileSpec` (styles `hero`, `wide`, `tile`, `chip`), `RenderTiles`, `NavTile` and `LiftTile` stay as they are. `OneHome()` is `LauncherHome()` plus the system menu's Theme tile and a row of quick switches; `MenuHome()` survives as the Browse view's home under Commands.

```csharp
public sealed partial class MainWindow
{
    // Guardrail 2: places never move; only contents update.
    TileLayout OneHome()
    {
        var layout = LauncherHome(); // Terminal hero, Projects + Clipboard wide, your items, Menu bar, Frequent
        // Quick switches: catalog entries, as chips that show and flip their state with Space / Enter.
        var switches = new[] { "connections.wifi", "connections.bluetooth", "look.dark-apps", "privacy.microphone" }
            .Select(WinSettings.Find).OfType<SettingEntry>()
            .Select(e => new TileSpec
            {
                Key = "setting:" + e.Id, Style = "chip", Icon = e.Icon == "-" ? "\uF013" : e.Icon, Label = e.Name,
                Sub = WinSettings.Label(e, WinSettings.Peek(e)), Color = Hex(WinSettings.Peek(e) == "on" ? theme.Green : theme.Comment),
                Run = () => Task.Run(() => { var r = WinSettings.Toggle(e); DispatcherQueue.TryEnqueue(() => { Snip.Say(r.Message); Render(); }); }),
            });
        layout.Flow(switches, "Quick");
        // The theme: one tile showing the palette as a single gradient (guardrail 12); Enter opens the Themes filter.
        layout.Flow([new TileSpec { Key = "themes", Style = "wide", ColSpan = 4, Icon = "\uF53F", Label = theme.Name, Swatch = theme, Color = Hex(theme.Accent),
                                    Run = () => SetFilter(Tag.Theme) }], "Theme");
        return layout;
    }
}
```

`LauncherHome()`'s Menu bar tile becomes "Browse" (`Run = () => { SetFilter(Tag.Command); browsing = true; Render(); }`). Its Frequent row already uses `History.Load().Boost(key, "")`; after the history-id migration it passes `"app:" + item.Key`.

---

## 13. `--search [filter]` requests

`App.HandleRequest`, new branch (before the old `--launcher` / `--files` / toggle fallbacks):

```csharp
        else if (args.Length > 0 && args[0] == "--search")
        {
            // --search [filter] [--browse] [--retired-key] [text…]
            var rest = args[1..].ToList();
            bool browse = rest.Remove("--browse");
            bool retired = rest.Remove("--retired-key");
            Tag? filter = rest.Count > 0 ? QueryParser.TagFromTyped(rest[0]) : null;
            if (filter != null) rest.RemoveAt(0);
            window.OpenSearch(filter, browse, string.Join(' ', rest));
            if (retired) Snip.Say("Win+Space does this now"); // the alias release (§14)
        }
```

`MainWindow.OpenSearch(Tag? filter, bool browse, string text)`: if the window is visible on the same filter, hide (the toggle behaviour every key has today); else reset `Input.Text = text`, `filter`, `browsing`, `selected = 0`, `Show(...)`, `RenderSearch()`.

`bin/commands.txt` changes (the keys' entry points; their names stay so GlazeWM, the dock and scripts keep working):

```text
launcher | One Search (Win+Space): apps, settings, commands, files, windows, projects, clipboard, themes; a filter's name to open on it: omawin launcher clipboard | app --search
files | One Search on Files (Win+F) | app --search file
menu | One Search on Commands, browsing the menu's sections (what Win+Alt+Space was); a section id to open it: omawin menu install | app --search command --browse
search | One Search; a filter and text to start with: omawin search settings dark | app --search
```

Old page names given to `launcher` map in `HandleRequest` before `TagFromTyped`: `clipboard`→Clipboard, `projects`→Projects, and the views that aren't filters (`audio`, `quick`, `packages`, `localhost`, `keys`, `reports`, `about`) keep opening their pages through `window.OpenPage` exactly as today. `--launcher` and `--files` stay accepted for one release (older `omawin.exe` copies, the dock), routed to `--search`.

---

## 14. Retiring Win+Alt+Space

Where the binding lives: **`config/glazewm/config.yaml` in the repo is the template** (tracked, with `{{ accent }}`-style placeholders). `themes/render.ps1` renders it to **`~/.glzr/glazewm/config.yaml`** (what GlazeWM reads, untracked), merging the account folder's `glazewm/config.yaml` (`keybindings:` and `window_rules:` only, `Merge-GlazeUser`). So the binding is changed in the template, by commit; the rendered copy follows on the next render; the account folder's file is the user's and is never edited.

**Release 1 (alias):** in the template, the system menu's binding (currently `config/glazewm/config.yaml` ~line 316) becomes:

```yaml
  # Win+Alt+Space was the system menu; it's One Search now (Win+Space), for one release with a
  # note, then removed by migrations/<date>-retire-win-alt-space.ps1.
  - commands: ['shell-exec --hide-window omawin.exe search --retired-key']
    bindings: ['lwin+alt+space']
```

**Release 2 (removal), in one commit:** delete that item from the template; update `AGENTS.md`'s key list ("Win+Alt+Space = system menu" goes; "Win+Space = One Search: launcher, settings, commands, …"), `manual/05-system-menu.md` (becomes "Browse, in One Search"), `default/launcher/menu.txt`'s header comment and `docs/architecture.md`'s keys table; and add the migration, which makes the running desktop match even if the template change didn't arrive cleanly (a local edit to the template kept by `omawin update`'s merge, or a render that hasn't run since):

`migrations/<yyyy-mm-dd>-retire-win-alt-space.ps1` (Windows PowerShell 5.1; safe on a new PC and twice):

```powershell
# Win+Alt+Space (the system menu) is retired: One Search on Win+Space does everything it did
# (docs/one-search.md, guardrail 10). The binding left config/glazewm/config.yaml (the template) in this
# release; this makes the desktop match: the template too if a local edit kept the binding (the
# old file is kept beside it), then GlazeWM's rendered config, then a reload. A binding of your
# own in your account folder's glazewm/config.yaml is yours: it's only reported.
# Safe on a new PC (nothing bound: nothing to do) and twice.
$repo = Split-Path $PSScriptRoot
$template = Join-Path $repo 'config\glazewm\config.yaml'
$rendered = Join-Path $HOME '.glzr\glazewm\config.yaml'
$changed = $false

# One binding item: its own comment lines right above, "- commands: [...]" naming omawin.exe's
# menu or search, then "bindings: ['lwin+alt+space']". Nothing else on the line, nothing else bound.
$item = '(?m)(?:^[ ]{2}#[^\r\n]*\r?\n)*^[ ]{2}- commands: \[''shell-exec --hide-window omawin\.exe (?:menu|search --retired-key)''\]\r?\n^[ ]{4}bindings: \[''lwin\+alt\+space''\]\r?\n'

if (Test-Path $template) {
    $text = [IO.File]::ReadAllText($template)
    if ($text -match $item) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        Copy-Item $template "$template.backup-$stamp"
        [IO.File]::WriteAllText($template, ([regex]::Replace($text, $item, '', 1)), (New-Object Text.UTF8Encoding $false))
        "Win+Alt+Space: removed from config/glazewm/config.yaml (the old file is config.yaml.backup-$stamp)"
        $changed = $true
    }
    elseif ($text -match "lwin\+alt\+space") {
        # Bound some other way (edited by hand): leave it, say so.
        "Win+Alt+Space is still bound in config/glazewm/config.yaml, not the way omawin wrote it; left as it is"
    }
}

# The rendered copy still has it (no render since the update): render and reload.
$stillRendered = (Test-Path $rendered) -and (Select-String -Path $rendered -Pattern 'lwin\+alt\+space' -Quiet)
$stillTemplate = (Test-Path $template) -and (Select-String -Path $template -Pattern 'lwin\+alt\+space' -Quiet)
if ($changed -or ($stillRendered -and -not $stillTemplate)) {
    if (Get-Process glazewm -ErrorAction SilentlyContinue) { & (Join-Path $repo 'themes\render.ps1') -Reload }
    elseif (Test-Path $rendered) { & (Join-Path $repo 'themes\render.ps1') }
}

# The account folder's own GlazeWM additions (docs/work.md): report only.
$account = if ($env:OMAWIN_USER_LAYER) { $env:OMAWIN_USER_LAYER } else { Join-Path $HOME 'omawin-account' }
$mine = Join-Path $account 'glazewm\config.yaml'
if ((Test-Path $mine) -and (Select-String -Path $mine -Pattern 'lwin\+alt\+space' -Quiet)) {
    "Your account folder's glazewm/config.yaml binds Win+Alt+Space; it's yours, so it stays"
}
```

(Re-render only when the rendered file has the key and the template no longer does, so a user who bound the key themselves through the template isn't fought every run.)

---

## 15. Unit tests: `test/menu-app.Tests/`

A plain `net9.0` test project beside `shell/menu-app/` (not inside it: the WinUI project globs every `.cs` below it). It links the pure files, so it builds and runs without WinUI, on Windows or Linux.

`test/menu-app.Tests/menu-app.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType> <!-- xunit.v3 test projects are executables -->
    <RootNamespace>SystemMenu.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- The pure parts of the menu app: no WinUI, no App.Log, no P/Invoke. Keep them that way. -->
    <Compile Include="..\menu-app\Fuzzy.cs" Link="Linked\Fuzzy.cs" />
    <Compile Include="..\menu-app\Settings\Catalog.cs" Link="Linked\Catalog.cs" />
    <Compile Include="..\menu-app\Search\Result.cs" Link="Linked\Result.cs" />
    <Compile Include="..\menu-app\Search\QueryParser.cs" Link="Linked\QueryParser.cs" />
    <Compile Include="..\menu-app\Search\Merger.cs" Link="Linked\Merger.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit.v3" Version="1.0.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.0" />
  </ItemGroup>
</Project>
```

Tests: each guards behaviour a regression would actually break; no snapshots, no counts of catalog lines.

```csharp
using Xunit;

namespace SystemMenu.Tests;

public class CatalogTests
{
    const string Dark = @"look.dark-apps | - | Dark mode for apps | dark | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme dword on=0 off=1 broadcast=ImmersiveColorSet |";

    [Fact]
    public void ParsesARegSwitch()
    {
        var e = Catalog.ParseLine(Dark, "t:1");
        Assert.Equal("look", e.Area);
        Assert.Equal(SettingKind.Switch, e.Kind);
        Assert.Equal("reg", e.How.Verb);
        Assert.Equal(("0", "1"), (e.How.On, e.How.Off));
        Assert.Equal("ImmersiveColorSet", e.How.Broadcast);
        Assert.Equal(("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme"), Catalog.SplitReg(e.How.Target));
    }

    [Fact]
    public void QuotesGroupPathsAndValuesWithSpaces()
    {
        var e = Catalog.ParseLine(@"explorer.open-to | - | Opens to | home | choice | reg HKCU\A\B\LaunchTo dword values=""1:This PC,2:Home"" |", "t:1");
        Assert.Equal(["1", "2"], e.Choices.Select(c => c.Value));
        Assert.Equal("This PC", e.Choices[0].Label);
        var f = Catalog.ParseLine(@"system.fast-startup | - | Fast startup | boot | switch | reg ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled"" dword on=1 off=0 | admin restart", "t:2");
        Assert.Equal("HiberbootEnabled", Catalog.SplitReg(f.How.Target).Value);
        Assert.True(f.Admin);
        Assert.Equal(Takes.Restart, f.Takes);
    }

    [Theory]
    [InlineData(@"nope.x | - | X | x | switch | reg HKCU\A\B dword on=1 off=0 |", "area")]                // unknown area
    [InlineData(@"look.x | - | X | x | switch | reg HKCU\A\B dword on=1 |", "on= and off=")]              // half a switch
    [InlineData(@"look.x | - | X | x | switch | reg HKLM\A\B dword on=1 off=0 |", "admin")]               // HKLM without admin
    [InlineData(@"look.x | - | X | x | slider | spi pointer-speed |", "range")]                           // slider without range
    [InlineData(@"look.x | - | X | x | switch | reg HKCU\A\B on=1 off=0 |", "type")]                      // reg without a type
    [InlineData(@"look.x | - | X | x | switch | reg ""HKCU\A\B dword on=1 off=0 |", "quote")]             // unclosed quote
    public void RejectsBadLines(string line, string because) =>
        Assert.Contains(because, Assert.Throws<FormatException>(() => Catalog.ParseLine(line, "t:1")).Message);

    [Fact]
    public void LaterLayersReplaceByIdAndKeepOrder()
    {
        var (entries, problems) = Catalog.Parse([
            ("repo", [Dark, @"look.transparency | - | T | t | switch | reg HKCU\A\EnableTransparency dword on=1 off=0 |"]),
            ("account", [Dark.Replace("Dark mode for apps", "Mine")]),
        ]);
        Assert.Empty(problems);
        Assert.Equal(["look.dark-apps", "look.transparency"], entries.Select(e => e.Id));
        Assert.Equal("Mine", entries[0].Name);
    }

    [Fact]
    public void BuildRangeHidesEntries()
    {
        var e = Catalog.ParseLine(@"explorer.end-task | - | End task | kill | switch | reg HKCU\A\TaskbarEndTask dword on=1 off=0 | minBuild=22631", "t:1");
        Assert.False(e.FitsBuild(22621));
        Assert.True(e.FitsBuild(26100));
    }

    // The shipped catalog parses with no problems and no duplicate ids: a typo in
    // default/launcher/settings.txt would otherwise only show up as a missing row on someone's PC.
    [Fact]
    public void ShippedCatalogIsClean()
    {
        var file = Path.Combine(RepoRoot(), "default", "launcher", "settings.txt");
        var (entries, problems) = Catalog.Parse([(file, File.ReadAllLines(file))]);
        Assert.Empty(problems);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    internal static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}

public class QueryTests
{
    [Theory]
    [InlineData("/road", Tag.File, "road")]
    [InlineData(">update", Tag.Command, "update")]
    [InlineData("dark", null, "dark")]
    public void Prefixes(string raw, Tag? filter, string text) =>
        Assert.Equal(new ParsedQuery(filter, text), QueryParser.Parse(raw, null));

    [Fact] public void TypedFilterName() => Assert.Equal(Tag.Setting, QueryParser.TagFromTyped("set"));
    [Fact] public void EmptyIsNoFilter() => Assert.Null(QueryParser.TagFromTyped(""));
    [Fact] public void TokenKeepsWhenNoPrefix() => Assert.Equal(Tag.Theme, QueryParser.Parse("nord", Tag.Theme).Filter);
}

public class MergerTests
{
    static Result R(Tag tag, string name, string? id = null) => new(id ?? $"{tag}:{name}", tag, name, "");
    static double NoHistory(string id, string q) => 0;

    [Fact]
    public void CapsEachTagAndOffersShowAll()
    {
        var files = Enumerable.Range(0, 10).Select(i => R(Tag.File, $"dark{i}.md"));
        var list = Merger.Merge([.. files, R(Tag.Setting, "Dark mode for apps")], new(null, "dark"), NoHistory);
        Assert.Equal(3, list.Count(r => r.Tag == Tag.File));
        Assert.Contains(list, r => r.Id == "more:File");
    }

    [Fact]
    public void ShortQueriesPreferAppsOverFiles()
    {
        var list = Merger.Merge([R(Tag.File, "vs.txt"), R(Tag.App, "Visual Studio Code")], new(null, "vs"), NoHistory);
        Assert.Equal(Tag.App, list[0].Tag);
    }

    [Fact]
    public void HistoryForThisQueryWins()
    {
        var list = Merger.Merge([R(Tag.App, "Chrome"), R(Tag.App, "Character Map")], new(null, "c"),
            (id, q) => id == "App:Character Map" && q == "c" ? 3 : 0);
        Assert.Equal("Character Map", list[0].Name);
    }

    [Fact]
    public void NonMatchesAreDropped() =>
        Assert.DoesNotContain(Merger.Merge([R(Tag.App, "Notepad")], new(Tag.App, "zzz"), NoHistory), r => r.Name == "Notepad");

    [Fact]
    public void LateResultsNeverMoveTheSelection()
    {
        var onScreen = new List<Result> { R(Tag.App, "A"), R(Tag.App, "B"), R(Tag.App, "C") };
        var fresh = new List<Result> { R(Tag.File, "Z"), R(Tag.App, "A"), R(Tag.App, "B"), R(Tag.App, "C") };
        var stable = Merger.Stable(onScreen, fresh, selected: 1);
        Assert.Equal(["A", "B"], stable.Take(2).Select(r => r.Name)); // B is still under the cursor
    }

    // Ranking fixtures (one-search.md §7): the expected top result for real queries.
    [Theory]
    [InlineData("dark", "Dark mode for apps")]
    [InlineData("ext", "File name extensions")]
    [InlineData("hz", "Refresh rate")]
    public void Fixtures(string q, string top)
    {
        Result[] all = [
            new("setting:look.dark-apps", Tag.Setting, "Dark mode for apps", "", "dark theme black"),
            new("setting:explorer.extensions", Tag.Setting, "File name extensions", "", "ext extension .txt"),
            new("setting:display.refresh-rate", Tag.Setting, "Refresh rate", "", "hz refresh 120 144"),
            R(Tag.App, "Docker Desktop"), R(Tag.App, "Excel"), R(Tag.File, "darktable.md"),
        ];
        Assert.Equal(top, Merger.Merge(all, new(null, q), NoHistory)[0].Name);
    }
}
```

The settings round-trip (roadmap 1B "Testing") lives in the same project as a separate class that only runs on Windows with `OMAWIN_ROUNDTRIP=1`; it links `Switches.cs`, `Native.cs` and `Catalog.cs` (so the project's TFM becomes `net9.0-windows` for that job, via a `Condition` on the env var), and uses `Assert.Skip` (xunit.v3) elsewhere:

```csharp
public class RoundTrip
{
    // Every reg / spi / powercfg entry: read, set the other value, read back, restore. Runs on the
    // CI runner's own session; HKLM entries only when elevated (GitHub's runners are).
    [Fact]
    public void EveryEntrySetsAndRestores()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("OMAWIN_ROUNDTRIP") != "1")
            Assert.Skip("Windows CI only (OMAWIN_ROUNDTRIP=1)");
        var file = Path.Combine(CatalogTests.RepoRoot(), "launcher", "settings.txt");
        var (entries, _) = Catalog.Parse([(file, File.ReadAllLines(file))]);
        var failures = new List<string>();
        foreach (var e in entries.Where(e => e.How.Verb is "reg" or "spi" or "powercfg" && e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider))
        {
            if (!e.FitsBuild(Environment.OSVersion.Version.Build)) continue;
            var h = Handlers.ByVerb[e.How.Verb];
            var before = h.Read(e);
            var other = e.Kind switch
            {
                SettingKind.Switch => before == "on" ? "off" : "on",
                SettingKind.Choice => e.Choices.First(c => c.Value != before).Value,
                _ => (before == e.Range!.Value.Min.ToString() ? e.Range.Value.Max : e.Range.Value.Min).ToString(),
            };
            try
            {
                // Admin entries: written directly here (the runner is elevated), never through WinSettings.
                if (h.Write(e with { Admin = false }, other) is { } err) { failures.Add($"{e.Id}: write: {err}"); continue; }
                if (h.Read(e) != other) failures.Add($"{e.Id}: wrote {other}, read {h.Read(e)}");
            }
            finally { if (before != null) h.Write(e with { Admin = false }, before); }
        }
        Assert.Empty(failures);
    }
}
```

`Apply.After` broadcasts are harmless on a runner; `refresh-explorer` / `restart-explorer` are skipped when `OMAWIN_ROUNDTRIP=1` (a flag check in `Apply.After`) so CI never kills the runner's Explorer. SPI on a runner's non-interactive session may refuse writes: if so, mark those entries' test as expected-skip with the reason, not silent. **(check on the first CI run)**

---

## 16. CI: `.github/workflows/test.yml`

```yaml
# Unit tests for the menu app's pure parts (catalog, query parser, merger), and the settings
# round-trip on a real Windows. Runs on every push and pull request; fast (no WinUI build).
name: Test
on:
  push:
    branches: [main]
  pull_request:

jobs:
  unit:
    runs-on: ubuntu-latest   # the pure tests need no Windows; cheaper and faster
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - name: Unit tests
        run: dotnet test menu-app.Tests --logger "console;verbosity=normal"

  settings-roundtrip:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - name: Every reg / spi / powercfg setting, set and restored
        shell: pwsh
        env:
          OMAWIN_ROUNDTRIP: '1'
          OMAWIN_SETTINGS_UNVERIFIED: '1'   # the "check" lines too: this is where they get checked
        run: dotnet test menu-app.Tests --filter "FullyQualifiedName~RoundTrip" --logger "console;verbosity=normal"
```

`release.yml` needs no change; a release can `needs:` this workflow later if a red test should block a tag.

---

## 17. Order of work

Each step is one change (one commit, per AGENTS.md), shippable alone.

1. `Catalog.cs` + `default/launcher/settings.txt` (≈45 lines above) + `menu-app.Tests` (catalog tests) + `test.yml`'s unit job. No UI.
2. `Native.cs`, `Switches.cs`, `WinSettings` (cache, Set, history, undo), the `ask` action + `Reply.cs` + the `setting` rows in `commands.txt`. Usable from a terminal; the round-trip job turns `check` lines into verified ones.
3. `Watch.cs`, `Builtins.cs` (radios, power mode, brightness, desktop icons, proxy, refresh rate first; HDR, hotspot, audio output after a PC check).
4. Admin queue + `bin/settings-admin.ps1` + `omawin settings admin`; the install's admin plan reads the queue (one prompt, 2.9).
5. `settings save | apply`, the install step, `update`'s `-NoAdmin` run.
6. `Index.cs` + `settings-pages.txt`; Settings rows in today's system menu search (1B.3) before One Search lands, so "settings" ships first as its milestone says.
7. One Search: `Search/` model, parser, merger (one-search.md §1-4 with §11's fixes) + their tests.
8. Sources over existing code; `MainWindow.Search.cs`; the XAML changes; `OneHome()`.
9. `--search` requests and `commands.txt`'s new actions; history-id migration; the Win+Alt+Space alias (release 1).
10. Next release: remove the binding (template, AGENTS.md, manual, architecture.md) + `migrations/<date>-retire-win-alt-space.ps1`.
11. Measure with `omawin profile menu` before and after 7-9 (roadmap 1.1); under 16 ms per keystroke or it doesn't merge.

Visible steps (6, 8, 9) need a screenshot on the PC before merging (AGENTS.md); every `check` line stays hidden until the round-trip or the PC confirms it.
