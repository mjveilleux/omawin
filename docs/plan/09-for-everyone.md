# 09: omawin for everyone (roadmap phase 10)

The plan so far makes omawin fast and complete on one kind of PC: a personal x64 Windows 11 in English, used by someone who can see the screen, at a desk. This file covers everyone else, and the trust a stranger needs before running `irm … | iex`. Each section: why, what to build, code, done when.

Code follows the language rule ([README.md](README.md)): compiled C#; PowerShell only in `bootstrap.ps1`.

Sections:

1. [Security model](#1-security-model): what omawin trusts, and how each thing it downloads is checked
2. [Work and school PCs](#2-work-and-school-pcs): proxies, TLS inspection, AppLocker, policies, managed devices
3. [ARM64](#3-arm64): native builds for Snapdragon PCs
4. [Accessibility](#4-accessibility): Narrator, contrast, reduced motion, text size, high contrast
5. [Windows in other languages](#5-windows-in-other-languages): no English-only parsing, other keyboard layouts
6. [Laptops](#6-laptops): power modes, the lid, touchpad gestures, battery-aware background work
7. [Strangers](#7-strangers): the README, the website, release notes, reports without telemetry

---

## 1. Security model

**Why:** omawin runs code from five places (its bootstrap, its release, package managers, themes, plugins) on a developer's PC, which holds SSH keys, tokens and work. Today the release is signed and has `SHA256SUMS.txt` (`.github/workflows/release.yml`), and the GlazeWM fork is signature-checked (`install/glazewm-fork.ps1:110`), but nothing is written down as a whole, and the bootstrap runs from `main`.

**What omawin trusts, and how each is checked:**

| Thing | Comes from | Check | If it fails |
|---|---|---|---|
| `bootstrap.ps1` | the user runs it | a release asset, not `main` (the README's line names `releases/latest/download/bootstrap.ps1`); it's short enough to read, and the README says so | n/a |
| the bundle (2.0) | GitHub release | SHA-256 against `SHA256SUMS.txt`, and that file's own signature (Authenticode on the exes, the sums file signed with the same cert via a detached `SHA256SUMS.txt.sig` checked by the CLI) | stop; nothing unpacked is used |
| updates, deltas | GitHub release | the same, and the version only goes up (no downgrade unless `omawin update --to <old>`) | keep the current version |
| packages | winget, Scoop, Chocolatey, mise, pacman | their own checks (hashes in manifests, pacman's keyring); omawin never passes `--ignore-security-hash` or `--force` | the package's error, shown |
| themes | git, the community index | **data only**: a theme folder may hold palettes, pictures and the config files in the allow-list; anything else (scripts, exes, `.lnk`) is skipped and named | the theme installs without them |
| plugins | git, the official catalog | pinned to a commit; permissions shown before install; first install and every update show what changed; Job Object limits; no admin (first-party verbs only, 8.0) | not installed / kept at the old commit |
| the account and shared folders | the user | the shared folder is writable only by the account that made it (docs/work.md); omawin refuses to run scripts from it if its ACL lets others write | warn in `omawin check`, skip its scripts |
| plugin secrets | the user | Windows Credential Manager (`omawin:<plugin>:<key>`), never in `settings.json` | n/a |

**Never:** a standing elevated service; running anything from `%TEMP%` that another user can write; `Set-ExecutionPolicy` beyond the process; turning off Defender or SmartScreen; downloading over http.

**Code: verifying the bundle.** Built 2026-10-30: [`cli/Security/Sums.cs`](../../cli/Security/Sums.cs) reads `SHA256SUMS.txt` (names only, never paths) and checks a file against it in constant time; tested in `cli.Tests/SecurityTests.cs`.

The bootstrap (Windows PowerShell 5.1) does the same with `Get-FileHash` before it unpacks anything:

```powershell
$sums = (Invoke-RestMethod "$release/SHA256SUMS.txt") -split "`n" | ForEach-Object { $h, $n = $_ -split '\s+\*?', 2; @{ $n.Trim() = $h } }
$want = ($sums | Where-Object { $_.ContainsKey($bundleName) })[$bundleName]
if ((Get-FileHash $bundle -Algorithm SHA256).Hash -ne $want) { throw "the download doesn't match the release's SHA256SUMS; nothing was installed" }
```

**Code: a theme is data.** Built 2026-10-30: [`cli/Themes/ThemeFiles.cs`](../../cli/Themes/ThemeFiles.cs). `Allowed` takes what Omarchy's themes hold (palettes, `.theme`, `.rgb`, pictures, `.mp4` backgrounds) and refuses scripts, programs, dotfiles and paths that climb out. `neovim.lua` is the one Lua file, and it's code by nature: lazy.nvim downloads and runs the plugins it names. So `Spec` accepts it only as a plain table (no function, call or bare identifier) and returns the plugins it names, which `omawin theme install` shows before installing ("this theme asks Neovim for EdenEast/nightfox.nvim"). All 23 shipped specs (`themes/nvim/`) pass.

**Code: plugin pinning** (`%LOCALAPPDATA%\launcher\plugins.lock`, one JSON line per plugin):

```csharp
public sealed record PluginLock(string Id, string Url, string Commit, string[] Permissions, DateTimeOffset Installed);

// An update fetches, then shows: the commit range, files changed, and any permission added.
// A new permission needs a yes; without one the plugin stays at its pinned commit.
public static IReadOnlyList<string> NewPermissions(PluginLock pinned, Manifest next) =>
    next.Omawin?.Permissions.Except(pinned.Permissions).ToList() ?? [];
```

**Done when:** the threat table is in `docs/security.md` with a "report a vulnerability" address in `SECURITY.md`; the bootstrap comes from a release and checks the bundle; `omawin theme install` skips non-data files (tested); plugin updates show new permissions; `omawin check` warns on a shared folder others can write.

---

## 2. Work and school PCs

**Why:** many people want omawin on the PC they're paid to use. Those PCs have proxies, TLS inspection, AppLocker or WDAC, Intune policies and no admin. omawin's no-admin rule (2.9) is most of the battle; the rest is detecting the walls and saying so instead of failing halfway.

**Build:**

- **Proxy.** Read the user's proxy the way Windows does (`WinHttpGetIEProxyConfigForCurrentUser`, PAC via `WinHttpGetProxyForUrl`) and pass it on: `curl.exe --proxy`, `HTTPS_PROXY` for git, Bun and mise in omawin's own processes only (never set machine-wide). winget and Scoop already follow the system proxy.
- **TLS inspection.** A corporate root CA is in Windows' store, not in the copies tools bring. Use Windows' store everywhere it's a setting: `git config --global http.sslBackend schannel` (only if the user hasn't set one), `curl.exe` (schannel by default on Windows' own curl), Bun (`NODE_EXTRA_CA_CERTS` from an export of the user's trusted roots to `%LOCALAPPDATA%\omawin\roots.pem`, refreshed by `omawin check`), WSL (copied into Arch's `/etc/ca-certificates/trust-source/anchors/` by `omawin linux certs`).
- **AppLocker / WDAC.** Unsigned exes in `%LOCALAPPDATA%` are often blocked. omawin's exes are signed; before the bundle is unpacked, the CLI runs a tiny signed probe from the target folder (`omawin-probe.exe`, prints "ok") and, if it's blocked, says which folder policy allows (often only `Program Files`, which needs admin) and stops cleanly.
- **Policies win.** A setting under `HKLM\SOFTWARE\Policies` or `HKCU\SOFTWARE\Policies` is managed: One Search shows it with a lock ("managed by your organization") instead of a switch, and `omawin setting set` refuses with that reason. The catalog gets a `policy` note (`policy=HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer!DisableSearchBoxSuggestions`).
- **Managed device facts** for `omawin check` and the install screen: MDM enrolled (`HKLM\SOFTWARE\Microsoft\Enrollments\*` with `ProviderID`), domain joined (`GetComputerNameEx`/`NetGetJoinInformation`), WSL allowed (`wsl --status` exit code and the `AllowWSL` policy), Store allowed, Developer Mode. Shown as facts, never sent anywhere.
- **Nothing employer-specific in the repo** (AGENTS.md): the account folder (docs/work.md) holds the proxy exceptions, work shares, VPN.

**Code** (`cli/Machine/Managed.cs`):

```csharp
using Microsoft.Win32;

public sealed record ManagedFacts(bool MdmEnrolled, bool DomainJoined, bool WslBlocked, bool StoreBlocked, string? Proxy);

public static class Managed
{
    public static ManagedFacts Read()
    {
        bool mdm = false;
        using (var e = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments"))
            if (e != null)
                foreach (var id in e.GetSubKeyNames())
                    using (var k = e.OpenSubKey(id))
                        if (k?.GetValue("ProviderID") is string p && p.Length > 0) { mdm = true; break; }
        var wslBlocked = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Lxss", "AllowWSL", 1) is 0;
        var storeBlocked = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore", 0) is 1;
        return new(mdm, Environment.UserDomainName != Environment.MachineName, wslBlocked, storeBlocked, Proxy.ForUrl("https://github.com"));
    }

    // A catalog setting is managed when its policy value exists (in either hive).
    public static bool IsManaged(string policy) // "HKLM\...\Key!Value"
    {
        var (key, value) = (policy[..policy.IndexOf('!')], policy[(policy.IndexOf('!') + 1)..]);
        var root = key.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
        using var k = root.OpenSubKey(key[(key.IndexOf('\\') + 1)..]);
        return k?.GetValue(value) != null;
    }
}
```

`Proxy.ForUrl` wraps `WinHttpGetIEProxyConfigForCurrentUser` and, when there's a PAC URL or auto-detect, `WinHttpGetProxyForUrl` (LibraryImport, blittable structs; 05-plugins.md §6 shows the pattern). (check) on a PC behind a PAC file.

**Done when:** the install, on a PC with a proxy and an inspecting CA (simulated in Windows Sandbox with mitmproxy, 7.1), completes; on an AppLocker PC it stops at once with the folder it needs; managed settings show a lock.

---

## 3. ARM64

**Why:** Snapdragon X laptops are a real share of new Windows PCs. Today omawin "works, but builds the launcher and music player on the PC" (README) and runs x64 binaries under emulation elsewhere, which is slower and costs battery.

**Build:**

- **Release matrix:** every binary for `win-x64` and `win-arm64`. GitHub has `windows-11-arm` runners; .NET Native AOT targets `win-arm64` from them; Rust targets `aarch64-pc-windows-msvc` (the GlazeWM fork, noctty's Rust parts, fff). The bundle (2.0) is per architecture: `omawin-<version>-<arch>.zip`.
- **Pick by the machine, not the process:** an x64 PowerShell under emulation reports `AMD64`. The bootstrap reads `PROCESSOR_ARCHITEW6432` / the registry; the CLI uses `IsWow64Process2`.
- **Packages:** winget picks the arm64 installer when the manifest has one; the core list (`packages.ps1`) marks tools without an arm64 build, and the install summary names those running emulated (so they get asked upstream, the Omarchy way: fix upstream, leave the gap visible).
- **WSL on ARM:** Arch Linux ARM isn't Arch; the Arch distro image omawin uses is x86_64. On ARM, Linux defaults to the other choice (Ubuntu, which has an arm64 WSL image) and says why, until an Arch aarch64 WSL image exists. Omarchy's own aarch64 work (its platform rules) is the guide.

**Code** (`.github/workflows/release.yml`, the build job):

```yaml
  build:
    strategy:
      matrix:
        include:
          - { arch: x64,   runner: windows-2025,    rust: x86_64-pc-windows-msvc }
          - { arch: arm64, runner: windows-11-arm,  rust: aarch64-pc-windows-msvc }
    runs-on: ${{ matrix.runner }}
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
        with: { global-json-file: global.json }
      - run: dotnet publish cli -c Release -r win-${{ matrix.arch }} -o out/${{ matrix.arch }}/bin -p:Version=${{ github.ref_name }}
      - run: dotnet publish menu-app -c Release -r win-${{ matrix.arch }} -o out/${{ matrix.arch }}/menu
      - run: cargo build --release --target ${{ matrix.rust }} --manifest-path glazewm/Cargo.toml
```

```csharp
// The machine's architecture, even from an emulated process.
[LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
private static partial bool IsWow64Process2(nint process, out ushort processMachine, out ushort nativeMachine);

public static string NativeArch()
{
    if (IsWow64Process2(Process.GetCurrentProcess().Handle, out _, out var native))
        return native switch { 0xAA64 => "arm64", 0x8664 => "x64", _ => "x64" };
    return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
}
```

**Done when:** a Snapdragon PC installs with no compile step and no emulated omawin process (`omawin check` lists emulated processes among omawin's own: none).

---

## 4. Accessibility

**Why:** a keyboard-driven desktop is already friendly to people who can't use a mouse, but nothing in omawin has been tried with Narrator, high contrast, larger text, or reduced motion. Some of Omarchy's themes are faint as text.

**Found 2026-10-30** (the contrast check, `menu-app/Contrast.cs`, tested over every shipped theme in `menu-app.Tests/Look/ContrastTests.cs`): every theme's foreground and muted text pass 4.5:1 on the background and on a selected row, and every accent passes 3:1 as a graphic. As small *text*, the accent falls short in Rosé Pine (3.1), Miasma (3.9) and Catppuccin Latte (4.3), and the `comment` colour is 1.7 to 5.9 (Everforest lowest). The menu app draws text in the accent (selected chips, folder names, the path) and in `comment` (Packages' source labels).

**Build:**

- **Readable text from any theme:** every place the menu app draws *text* in a palette colour other than foreground/muted goes through `Contrast.Readable(color, theme.Background, theme.Foreground)` (keeps the hue, moves toward the foreground just enough). Icons and borders keep the raw colour (3:1 holds). The same in the native bar. Done once in the theme loader: `theme.AccentText`, `theme.CommentText`. Community themes (1.10) get the same treatment at load, and the picker shows a small "low contrast" mark when the raw palette fails.
- **Narrator:** every row of the launcher, finder and One Search gets `AutomationProperties.Name` ("Dark mode, setting, on"), the result count is a live region ("12 results"), switches expose `Toggle` patterns, the bento tiles are a list with names. Keys stay as they are; Narrator's own keys (Caps Lock + …) don't collide with omawin's.
- **Reduced motion:** read `SPI_GETCLIENTAREAANIMATION` (Settings › Accessibility › Visual effects › Animation effects). Off means: the menu appears and disappears without the rise (fade only, 80 ms), and the GlazeWM fork's window animations are off (its `enabled` flag, 01-snappy.md §5). Watched live (`WM_SETTINGCHANGE`).
- **Text size:** `UISettings.TextScaleFactor` (Settings › Accessibility › Text size) scales the menu's fonts and row heights; the window grows rather than clipping.
- **High contrast:** when on (`SystemParameters.HighContrast`, `UISettings.GetColorValue`), the menu app and the bar use the system's high-contrast colours instead of the theme; GlazeWM's border uses `COLOR_HIGHLIGHT`. Theme switching still works and applies when high contrast goes off.
- **Focus:** a visible focus ring (2 px, accent, 3:1) on every focusable thing; nothing reachable only by mouse (the bar's widgets get keys or a One Search entry).

**Code** (the menu app's theme loader):

```csharp
// After the theme is read: text variants of colours that may be faint.
theme.AccentText = Contrast.Readable(theme.Accent, theme.Background, theme.Foreground);
theme.CommentText = Contrast.Readable(theme.Comment, theme.Background, theme.Foreground);

// Reduced motion: Windows' own switch.
[LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")] [return: MarshalAs(UnmanagedType.Bool)]
private static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
public static bool ReduceMotion() => SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var on, 0) && on == 0;
```

```csharp
// A result row for Narrator: what it is, what kind, and its state.
AutomationProperties.SetName(row, result.State?.Invoke() is { } state ? $"{result.Name}, {Tag(result.Tag)}, {state}" : $"{result.Name}, {Tag(result.Tag)}");
AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
```

**Done when:** One Search, the finder and Quick settings are usable with Narrator alone (a scripted check with UI Automation in CI on Windows: every row has a name, the count changes); with animation effects off nothing slides; every shipped theme's text passes 4.5:1 through `Readable` (tested: done).

---

## 5. Windows in other languages

**Why:** half of Windows installs aren't English. Code that reads a command's English output (`powercfg /list`, `netsh`, `winget list`'s table, `ipconfig`) breaks on them, and so do paths like `Desktop` and keys on other layouts.

**Build:**

- **APIs or machine output, never human output.** `powercfg` → `PowerEnumerate`/`PowerGetActiveScheme` (GUIDs); `netsh` → `GetAdaptersAddresses` / WMI `MSFT_NetAdapter`; winget → `winget export` / the COM API (`Microsoft.Management.Deployment`), Scoop → `scoop export` (JSON), Chocolatey → `choco list -r` (pipe-separated, not translated); WSL → `wsl -l -v` is UTF-16 and translated: use `wsl --list --quiet` plus the registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss`).
- **A CI lint** (`cli.Tests`) that fails when a script or C# file matches known English-output parsing (`powercfg.*Select-String`, `netsh .*Select-String`, `-match 'Power Scheme GUID'`, `winget list \|`), with an allow-list comment for justified cases.
- **Known folders**, never `~\Desktop`: `SHGetKnownFolderPath(FOLDERID_Desktop)` (folders can also be moved to OneDrive).
- **Settings in your language:** the catalog's names are English; One Search also matches Windows' own localized names through Windows' settings index (02 §5), and each catalog entry's `ms-settings:` page title, read from the system, as an extra keyword.
- **Keyboard layouts:** GlazeWM binds by key names that map to virtual keys; on AZERTY the number row is `&é"'(…` without Shift, so `lwin+1` must mean the key in that position. The fork binds digits by scan code (position), not by character, which Hyprland also does (`code:10`). (check) in the fork's key parser.
- **Numbers and dates:** the calculator accepts the user's decimal separator; the bar's clock uses the user's format (`CultureInfo.CurrentCulture`) unless set; all omawin files use invariant culture.
- **omawin's own text** stays English for now; strings move to `.resx`-style files only when someone offers a translation (Omarchy's i18n catalog is the model).

**Code: the lint.** Built 2026-10-30: `cli.Tests/EnglishOutputTests.cs` fails on a line that runs `powercfg`, `netsh`, `ipconfig`, `systeminfo`, `w32tm`, `route print`, `sc query`, `tasklist` or `schtasks` and parses what it prints; `any-language: <why>` in the line allows one. The repo was already clean (it uses APIs, `winget export` and `WSL_UTF8=1`).

**Done when:** the install and `omawin check` pass in Windows Sandbox with the display language set to German and Japanese and an AZERTY layout (7.1's sandbox test takes a `-Language` and `-Layout`); the lint is in CI.

---

## 6. Laptops

**Why:** omawin is built at a desk. On a laptop people want the power mode a key away, the lid to do the right thing, the touchpad's gestures to move between omawin's workspaces (not Windows' virtual desktops), and background work to calm down on battery.

**Build:**

- **Power mode** (Best power efficiency / Balanced / Best performance) as a One Search choice and a bar click: `PowerSetActiveOverlayScheme` with the three known overlay GUIDs; per-user, no admin. Shown in the bar's battery widget.
- **Battery widget** in the native bar: percent, charging, time left (`GetSystemPowerStatus`, `RegisterPowerSettingNotification(GUID_BATTERY_PERCENTAGE_REMAINING)`: an event, no polling).
- **On battery, calmer:** the plugin host sends the `power` event (05-plugins.md); omawin's own background work (the file index refresh, theme thumbnail downloads, the music cache, package update checks) waits while on battery under 30% or with battery saver on (`GUID_POWER_SAVING_STATUS`).
- **Lid:** a catalog entry (`power.lid`: do nothing / sleep / hibernate / shut down) via `PowerWriteACValueIndex`/`DCValueIndex` on the active scheme, `SUB_BUTTONS` / `LIDACTION`. Writing the active scheme's values is allowed for the user on most PCs (check); if denied, the entry says it needs admin and joins the opt-in batch (2.9).
- **Touchpad gestures:** Windows' three- and four-finger swipes switch Windows' virtual desktops, which omawin doesn't use. Settings › Bluetooth & devices › Touchpad › Advanced gestures can send any shortcut; omawin sets four-finger left/right to GlazeWM's workspace keys (`Win+Ctrl+Left/Right` mapped in `glazewm/config.yaml` to `focus --prev-workspace` / `--next-workspace`) and up to One Search, through that page's per-user registry values (`HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad`, (check) the value names on a PC: set them in Settings once and read back with `reg query`). An entry in the catalog, off by default, chosen in the install's Laptop question.
- **Displays come and go:** docking and undocking (GlazeWM's `monitor_updated`) re-applies the matching monitor profile (8.x, 07 §6) and moves workspaces back to the monitor they were on.
- **Install asks once** "Is this a laptop?" (pre-answered from `GetSystemPowerStatus().BatteryFlag != 128`) and turns on the gestures and the battery widget.

**Code** (`cli/Machine/Power.cs`):

```csharp
public static partial class Power
{
    // Windows' power mode slider (the overlay schemes). Balanced is the empty GUID.
    public static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [LibraryImport("powrprof.dll")] private static partial uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [LibraryImport("powrprof.dll")] private static partial uint PowerSetActiveOverlayScheme(in Guid scheme);

    public static Guid Mode() => PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : Balanced;
    public static bool SetMode(Guid mode) => PowerSetActiveOverlayScheme(mode) == 0;

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static bool HasBattery() => GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;
    public static (int Percent, bool Charging, bool Saver)? Battery() =>
        GetSystemPowerStatus(out var s) && s.BatteryFlag != 128
            ? (s.BatteryLifePercent, s.AcLineStatus == 1, (s.SystemStatusFlag & 1) != 0) : null;
}
```

Catalog rows (`launcher/settings.txt`):

```
power.mode | 󰂄 | Power mode | battery performance efficiency balanced | choice | omawin power mode | options=efficiency,balanced,performance
power.lid  | 󰌢 | When I close the lid | laptop sleep hibernate | choice | powercfg SUB_BUTTONS LIDACTION | options=nothing:0,sleep:1,hibernate:2,shut down:3 check
```

**Done when:** on a laptop the power mode is one search away, the lid is a setting, four-finger swipes move workspaces, and an hour on battery with omawin idle uses no more than an hour with stock Windows (measured with `powercfg /batteryreport` before and after, (check)).

---

## 7. Strangers

**Why:** omawin is public. Someone who finds it should, in a minute, see what it is, trust it enough to try it (in Windows Sandbox if they like), and know how to undo it. Today the README has a placeholder for its screenshot, still describes Zebar and Win+Alt+Space, and there's no website or release notes.

**Build:**

- **README, top to bottom:** one sentence; a 20-second loop (One Search, a theme switch, windows tiling); the install line (from a release); "try it first in Windows Sandbox" with a `.wsb` file in the release that runs the bootstrap inside the sandbox; what it changes and how to undo it (`omawin uninstall`, 2.10); requirements; links to the manual. Rewritten when One Search and the native bar ship, so it never describes what's gone.
- **Website:** `omawin.org` (or GitHub Pages), built from `manual/` by a small Bun static site build (Bun for web-shaped work); the theme gallery from the theme index (1.10) and the plugin catalog (4.x) as pages. No trackers, no cookies.
- **Release notes:** generated in CI from the commits since the last tag, grouped by area (the commit subject's leading `Area:`), plus each new migration's summary line ("removes Zebar; your bar is now the native one"). Shown by `omawin update` before it applies.
- **No telemetry.** Instead, `omawin report`: runs `omawin check`, collects versions and the last install/update logs, **redacts** them (user name, PC name, home paths, emails, tokens, IPs, the account folder's contents), shows the result, and opens a GitHub issue form with it pasted only when the user says so.
- **Issue forms** (`.github/ISSUE_TEMPLATE/bug.yml`): what happened, `omawin report` output, Windows version and language, x64/ARM64, managed PC yes/no.
- **First run:** the welcome page (2.x) shows the five keys that matter and a "take the tour" (a One Search walk-through), dismissable forever.
- **Contributing:** `CONTRIBUTING.md` from AGENTS.md's human-relevant parts: the language rule, how to run the tests (`dotnet test menu-app.Tests`, `dotnet test cli.Tests`), how to make a theme or a plugin.

**Code: redaction.** Built 2026-10-30: [`cli/Report/Redact.cs`](../../cli/Report/Redact.cs): the home folder becomes `~`; anything under the account folder becomes `<account>`, file names included (the test found a client's name surviving in a path below it); tokens (GitHub, API keys, Slack, AWS, JWTs), `password=`/`token:`/`Authorization:` values, emails and IPs (not localhost) are replaced; the user and PC names as whole words only.

**Release notes.** Built 2026-10-30: [`cli/Release/Notes.cs`](../../cli/Release/Notes.cs): subjects with a short `Area:` prefix grouped by area (case-insensitive), the rest under Other, merges dropped, then "What changes on your PC" from the migrations. `release.yml` calls it once the CLI is in the release (2.11).

**Done when:** a stranger can go from the README to a tiled desktop, or to a sandbox trial, without reading anything else; every release has notes; `omawin report` produces a redacted report (tested with a fixture full of fake secrets: none survive).
