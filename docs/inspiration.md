# Inspiration: what others learned doing this

Other projects have tried to turn a stock Windows PC into a ready developer machine with one command. This file keeps what they learned that omawin can use: the lessons, where each one comes from (a file or issue in their repo), and the omawin track it helps ([mission-todo.md](mission-todo.md)). It also lists what they did that we shouldn't copy, because their issue trackers show what it broke.

Read on 2026-10-15, from shallow clones and each project's GitHub issues. Claims were checked against the code, not just the READMEs. Where a README and the code disagree, the code is what's written here.

| Project | What it is | Stars | License | Most useful for |
|---|---|---|---|---|
| [microsoft/WindowsDeveloperConfig](https://github.com/microsoft/WindowsDeveloperConfig) | Microsoft's `irm aka.ms/devconfig/… \| iex` dev setup ("Calm OS"): winget tools, OS tweaks, Terminal, WSL, one reboot | ~3.2k | MIT | install reliability: elevation, reboot-resume, winget failures, PS 5.1 hardening |
| [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil) | the most-used `irm \| iex` Windows tool: a WPF GUI of apps and tweaks | ~64k | MIT | tweaks as data with undo, restore points, and what broke Windows for users |
| [DEFALT314/glazewm-dotfiles](https://github.com/DEFALT314/glazewm-dotfiles) | Hyprland-style GlazeWM + Zebar + Flow Launcher, one `irm` command | new | MIT | our closest twin: GlazeWM rules, bar details, a small clean install |
| [jade-tam/dotfiles](https://github.com/jade-tam/dotfiles) | a GlazeWM + Zebar rice with a theme switcher, through chezmoi | ~195 | MIT | theme switching done the hard way; GlazeWM pain from its issues |
| [jayharris/dotfiles-windows](https://github.com/jayharris/dotfiles-windows) | PowerShell dotfiles with "developer-minded system defaults" (stale since 2024) | ~555 | MIT | Explorer and Snap defaults, profile speed |
| [freddiehaddad/windotfiles](https://github.com/freddiehaddad/windotfiles) | a README checklist plus configs (GlazeWM, starship, pwsh) | 5 | none (ideas only, no code) | the Windows 11 "For developers" toggles; a warning about Defender exclusions |
| [omacom/try-omarchy-windows](https://github.com/omacom/try-omarchy-windows) | the real Omarchy in a QEMU/WHPX VM, as a signed launcher exe | ~630 | MIT | what people who want Omarchy on Windows ask for; reboot-resume; a README for strangers |

## What to do now

The lessons that tie straight to the mission, in the order we'd take them. Each item names its source and the track it moves.

### Track 1, distribution: one line on any PC

1. **One elevated batch, everything else un-elevated.** Microsoft collects every admin step into one elevated process and runs per-user steps from it through a temporary scheduled task at `-RunLevel Limited` (`_elevation.ps1`, `Invoke-DevConfigUnelevatedCommand`). A `Global\` mutex stops two runs at once. A declined UAC prompt exits cleanly with "nothing was changed". Their issue #57 (a storm of UAC prompts) is the problem this replaced. WinUtil shows the other side: winget run elevated put per-user apps under the admin account, or left them impossible to uninstall (#5128, #5018, exit code `0x8A15007D`). For omawin: run winget un-elevated; gather the fork's certificate, long paths, sudo, Dev Drive and the no-ads policy into one admin child; that is the "at most one admin prompt" principle made real.
2. **Never run elevated code from a folder the user can write.** Microsoft copies whatever runs elevated into `%ProgramData%\CalmOS` with an Administrators-owned ACL (Users: read and execute), refuses junctions and reparse points, and checks each file's signature from the same in-memory text it then runs (`_security.ps1`). omawin's elevated step today runs `$PSCommandPath` from the user-writable `~/omawin` (`install/glazewm-fork.ps1`, `Invoke-Elevated`): anything running as the user could edit that file and get admin. Copy the elevated script somewhere locked first, or pass it as an encoded command the way WinUtil does (`New-WinUtilElevationCommand` serializes parameters to base64 so they can never run as code).
3. **Pin a run to one commit.** Microsoft's one-liner resolves the branch or tag to a SHA once (`bootstrap.ps1`, `Resolve-CalmOsRef`, from `info/refs`) and fetches every file of that run, even after the reboot, from it. This is the "update follows branch tip, not release tags" item: `omawin update` should move to a tag and stay there.
4. **Harden the `irm | iex` path for Windows PowerShell 5.1** (Microsoft, `setup-*.ps1`; jayharris #22):
   - `[Net.ServicePointManager]::SecurityProtocol` gains TLS 1.2.
   - `$env:PSModulePath = "$PSHOME\Modules;$env:PSModulePath"`, because a PS 7 module path leaks into 5.1 and breaks cmdlets (their #147).
   - `$ProgressPreference = 'SilentlyContinue'`: `Invoke-WebRequest` on 5.1 is many times slower with the progress bar.
   - If `MachinePolicy` or `UserPolicy` execution policy is `Restricted`, stop with a clear message. Otherwise pass `-ExecutionPolicy RemoteSigned` per process and never change the machine's policy. DEFALT314 avoids the question entirely by running the downloaded `install.ps1` as a scriptblock.
   - Web requests retry with backoff and honour `Retry-After`, within a total time limit.
   - Check the downloaded text before `iex`. WinUtil's known issues: where the short domain is blocked, it serves HTML, and users see `'<' is not recognized`. Document a raw GitHub release URL as the fallback.
   - UTF-8 console output, and glyphs written as `[char]0x2713` rather than literally.
5. **Reboot and resume, at most once** (Microsoft `_reboot-resume.ps1`; Try Omarchy `app/setup.go`, `ensureWHP`). Progress goes into a small state file. An AtLogOn task (`-RunLevel Limited`, 30 s delay) resumes and asks for UAC again, so nothing runs elevated silently. Restart with `shutdown.exe /r /t 0 /f`, not `Restart-Computer`, which goes through WMI and can time out. A pending reboot shows as `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`. `dism` returns 3010 for "enabled, restart needed". Try Omarchy writes a marker and compares its time with the boot time (`GetTickCount64`). If the PC has rebooted and the feature is still off, it says "turn on virtualization in the firmware" rather than looping. It also detects ARM64 before offering a pointless enable. The WSL preset needs exactly this.
6. **Treat winget as unreliable until proven otherwise** (Microsoft `steps/_winget.ps1`; WinUtil `Install-WinUtilProgramWinget.ps1`; DEFALT314 `install.ps1`). Decide on exit codes, never on output text, which is translated:

   | Code | Meaning |
   |---|---|
   | `-1978335189` / `0x8A15002B` | already installed, no applicable update: success |
   | `-1978335135` | already installed: success |
   | `0x8A150014` | no package found |
   | `-1978334967`, `-1978334965` | installed, restart needed |
   | `0x8A15000F`, `0x8A150045`, `0x8A15004B` | source outage: retry, then skip the remaining winget work this run |
   | `0x8A15007D` | won't change a per-user install from an admin context |
   | `0x800706BA` (RPC) | re-register App Installer (`Add-AppxPackage -Register`), then try again |

   Always `--exact --source winget --accept-package-agreements --accept-source-agreements`, one package per call. Match `winget list` on the package id, the only text never translated. Microsoft pins App Installer and installs it from the GitHub release with a SHA256 check when the PC's copy is too old (never downgrading a newer Store build). A fresh PC without the Store's update has an old winget.
7. **A System Restore point before changes, and don't let it stall** (WinUtil `tweaks.json`, `Invoke-WPFtweaksbutton.ps1`). Set `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore` `SystemRestorePointCreationFrequency=0` (Windows otherwise silently skips a point within 24 hours of the last); `Enable-ComputerRestore` if there's none yet; `Checkpoint-Computer -RestorePointType MODIFY_SETTINGS`. Their #2704: it hung at 99% for up to 90 minutes on some PCs. Run it as a job with a timeout and carry on with a warning.
8. **A README for strangers** (Try Omarchy `README.md`): a centred icon, one sentence, the one line (theirs is a download link with its SHA256 next to it), one line of requirements, a GIF with a still image for `prefers-reduced-motion`, three steps that end with "press Super+Space, then Super+K" (learn two keys first, their USER-GUIDE), what you can do, what to know before you start, then how it works and credits. Undo is a top-level FAQ entry. Consider an entry in Apps & features (`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\omawin`) so undo is where Windows users look for it.
9. **Docs never claim more than CI does.** Microsoft's docs say CI runs on every push and nightly; the workflow is manual-only. Their test design is still worth copying for the clean-VM test: a manifest drives the matrix, each step runs twice to prove the second run changes nothing, a `-PlanOnly` dry run, and WinUtil's unattended `-Preset` mode for driving a run without the GUI. DEFALT314's CI is the cheap first step: parse every script under PowerShell 5.1, run PSScriptAnalyzer, validate the JSON.

### Health, now

10. **One blocked registry write must not abort everything** (Microsoft #109, #124, #127). On fresh Windows 11, `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests` is denied even to an admin. `HKCU\…\SearchSettings\IsDynamicSearchBoxEnabled` is protected by cloud policy on Microsoft-account PCs. Each killed their whole run until they caught `UnauthorizedAccessException`, flagged the step and went on. `bin/no-ads.ps1` has this bug today: `$ErrorActionPreference = 'Stop'` and no `try` around `Set-Dword`, so the first blocked write skips the rest.
11. **Check, apply, re-check** (Microsoft `steps/_step-runner.ps1`, `New-DevConfigStep`). Every step is a `Check` and an `Apply`. After `Apply`, `Check` runs again, and if it still fails the step fails: "ran, but the check still says it isn't done". Steps may be best-effort (flagged, the run goes on). The run ends with "N changed, M already up to date, K flagged; run again to retry the flagged ones". If install steps and migrations were written this way, `omawin check` could run the same `Check` blocks instead of a second copy of the logic.

### Track 6, developer machine: OS defaults a developer wants

Every one of these is a plain registry value, so it can be undone. The HKCU ones need no admin. They'd be a "developer defaults" step beside no-ads, each row saving the PC's prior value first (see Remove, below). Sources: Microsoft `registry-*.ps1`, WinUtil `tweaks.json`, jayharris `windows.ps1`.

| Setting | Key | Value | Scope |
|---|---|---|---|
| Show file extensions | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` `HideFileExt` | 0 | user |
| Show hidden files | same key, `Hidden` | 1 | user |
| Explorer opens on This PC | same key, `LaunchTo` | 1 | user |
| No OneDrive / sync promos in Explorer | same key, `ShowSyncProviderNotifications` | 0 | user |
| No app-launch tracking | same key, `Start_TrackProgs` | 0 | user |
| No Start recommendations / account nags | same key, `Start_IrisRecommendations`, `Start_AccountNotifications` | 0 | user |
| End Task in the taskbar's right-click menu | `…\Explorer\Advanced\TaskbarDeveloperSettings` `TaskbarEndTask` | 1 | user |
| Full path in the title bar | `…\Explorer\CabinetState` `FullPath` | 1 | user |
| Quick Access without recent / frequent / cloud files | `…\Explorer` `ShowRecent`, `ShowFrequent`, `ShowCloudFilesInQuickAccess` | 0 | user |
| No web results in search | `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions` | 1 | user (policy) |
| Snap off, since GlazeWM tiles | `HKCU\Control Panel\Desktop` `WindowArrangementActive` = "0"; `…\Explorer\Advanced` `SnapAssist`, `SnapFill`, `JointResize` = 0 | | user |
| Narrator's Win+Ctrl+Enter off (next to Win+Enter) | `HKCU\Software\Microsoft\Narrator\NoRoam` `WinEnterLaunchEnabled` | 0 | user |
| No feedback prompts | `HKCU\Software\Microsoft\Siuf\Rules` `NumberOfSIUFInPeriod` | 0 | user |
| WSL's first-run window skipped | `HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss` `OOBEComplete` | 1 | user |
| Long paths | `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem` `LongPathsEnabled` | 1 | admin |
| sudo, inline (24H2+) | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo` `Enabled` (or `sudo config --enable normal`) | 3 | admin |
| Developer Mode | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock` `AllowDevelopmentWithoutDevLicense` | 1 | admin |
| No promoted apps installed | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` `DisableWindowsConsumerFeatures` | 1 | admin (no-ads has it) |
| No peer-to-peer update uploads | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization` `DODownloadMode` | 0 | admin |

Obsolete; don't add: `TaskbarMn` (Chat, removed in 23H2); `TaskbarDa` written directly (newer builds deny it; use the `AllowNewsAndInterests` policy, and allow it to fail); `EnableSuperfetch` (ignored since SysMain); `BingSearchEnabled` alone (the policy above is what holds on 24H2).

Explorer doesn't pick up `CabinetState` and some `Advanced` values until it reloads. Microsoft calls `SHGetSetSettings` / `WriteCabinetState` through P/Invoke so changes show at once without restarting Explorer, which both GlazeWM rice repos restart (and jade-tam gave up on for light/dark as "too buggy").

12. **Dev Drive, not Defender exclusions.** freddiehaddad's README (not its scripts: nothing automates it) suggests excluding `C:\` and the processes `git.exe`, `nvim.exe`, `pwsh.exe`, `starship.exe`. Microsoft's "exclusions to avoid" names exactly these. A process exclusion skips every file that process opens, so anything cloned or downloaded through `git` or `pwsh` is never scanned, and excluding `C:\` switches real-time protection off in practice. Microsoft's answer is [Dev Drive](https://learn.microsoft.com/en-us/windows/dev-drive/): a ReFS volume (Windows 11 22621.2338+, every edition including Home, 50 GB minimum, admin to create) that Defender scans in performance mode (asynchronous) while real-time protection stays on. As an opt-in preset: a VHDX under the user's folder made in the one admin batch (`Format-Volume -DevDrive`); `~/code` (or wherever projects go) and the package caches on it (`npm_config_cache`, `PIP_CACHE_DIR`, `CARGO_HOME`, `NUGET_PACKAGES`, `GRADLE_USER_HOME`, bun's and uv's caches), tools and SDKs left on C:; `omawin check` probes it with `fsutil devdrv query X:`. Not yet known: whether a VHDX Dev Drive mounts itself after a reboot.
13. **Don't use winget configure / DSC.** Microsoft is moving its own tool off it: WinUI "no longer ships a configuration.winget" (`development.md`, `workloads/winui.ps1`). Their DSC-era issues are why: it needs `powershell.exe` on the PATH (#52) and the VC++ redistributable (#86, error `-2146233079`), and its test scripts misread WSL (#48). omawin's plain PowerShell steps are the direction Microsoft itself took.

### Track 2, personal vs general

14. **Detect before offering** (WinUtil `Get-WinUtilToggleStatus.ps1`). Each toggle reads the registry for its current state, and a `DefaultState` field says what a missing key means. The menu app can do the same for its rows, and hide what doesn't apply, the way Try Omarchy checks for ARM64 first.
15. **Remove: snapshot the real prior value.** WinUtil keeps each tweak as data with its undo next to it: `Value` and `OriginalValue`, a `<RemoveEntry>` sentinel for "delete on undo", services with `StartupType` and `OriginalType` (`Set-WinUtilRegistry.ps1`, `Invoke-WinUtilTweaks.ps1`). Microsoft's uninstall does the same with a `ResetValue`. Both restore an assumed Windows default, not what the PC had, and Microsoft's removes tools "even if they predate setup" (its own warning). omawin's Remove should record the actual prior value (or "absent") in a state file under `%LOCALAPPDATA%\launcher\` before the first write, and restore from it. The tweak list itself can be data the same way, with WinUtil's tiers: a lean default, and a clearly marked "advanced, may break things" group.
16. **Logs that answer "was it us?"** WinUtil's maintainers spend much of their issue time ruling tweaks in or out (#5088, #4894, #5071). `Start-Transcript` on every run, plus Try Omarchy's one-click diagnostics bundle (Windows build, hardware, logs; user names, IPs and keys stripped) with an issue template asking for it, got their bugs fixed within a release (#19, #276, #328). For us: `omawin check --bundle`.
17. **A user file loaded last and never touched** (jayharris: `extra.ps1`, untracked, for secrets and identity, dot-sourced at the end of the profile). That's what the user layer already is; the profile should load the user layer's `profile.ps1` last, the same way.

### Tracks 3 and 4, window manager and bar

18. **Fullscreen in the fork.** Upstream glazewm #833 (open): F11, games and video only grow the window, which keeps its border and rounded corners; #1056 and #1104 ask the same. We found the same cause and has a fix on the fork's `fullscreen` branch. Upstream's commenters add: when a window's rect equals its monitor's, drop the border, force square corners, skip transparency, and raise an event so the bar and dock can hide. jade-tam's stopgap is an `ignore` rule for games by process (`VALORANT-Win64-Shipping|League of Legends|…|javaw`). Avoid their `toggle-fullscreen --maximized` with `shown_on_top: true`: jade-tam #50 shows the taskbar and focus trouble it causes. Upstreaming the fix is track 3's last item.
19. **`lalt`, not `alt`, in bindings** (DEFALT314's config). `alt` also matches right Alt, which is AltGr on many layouts (ą, ę, ł, ć, €, @, {). Our `config/glazewm/config.yaml` binds `alt+h`, `alt+shift+l` and others with plain `alt`, so typing those characters on a European layout can move windows instead.
20. **Keys Windows keeps for itself, and keys people want back** (Try Omarchy). Ctrl+Alt+Del and Win+L can't be intercepted (#181); they used Ctrl+Alt+End. Users asked for an off switch for every system chord taken over (#215, after Alt+Tab forwarding in #143). With Microsoft Pinyin in Chinese mode, every shortcut stopped working until #334 (#328): test our keys with a CJK input method active. Their low-level keyboard hook re-installs itself every 800 ms to stay first in the hook chain against other hooks (PowerToys, AutoHotkey), installing the new hook before removing the old (`app/winkey.go`); worth trying if our keys ever go missing beside other hook users. The manual's keys page should list Windows' reserved chords and the native ones kept (DEFALT314's README groups its keys as WM, apps, and "native Windows shortcuts kept": Win+\`, Win+V, Win+N), which helps the generated cheat sheet in track 3.
21. **Zebar-era details that carry into the native bar** (DEFALT314):
    - Battery: on a provider error keep the last value, and hide the widget only if a battery was never seen (it flickered otherwise).
    - VPN pill: profiles from `Get-VpnConnection` (user and all-users), connect and disconnect with `rasdial`, `rasphone -d <name>` when no credentials are saved, and "connected" when a network interface's `friendlyName` matches the profile.
    - Commands the bar may run are limited by an argument pattern (`{"program":"shutdown","argsRegex":"^/[srh]( /t 0)?$"}`): least privilege for anything a widget spawns.
    - Fonts at bar sizes: Windows draws unhinted variable fonts blurry, so they ship subset, hinted icon fonts (pyftsubset → woff2) and an opaque bar, which keeps ClearType (`transparent: false`). Worth checking in the WinUI bar.
    - Zebar loads every `~\.glzr\zebar\*\zpack.json`, so a backup copy of a pack beside the real one can replace the live bar. Our backups (`<path>.backup-<timestamp>`) must never land in that folder.
22. **Monitor order per PC** (jade-tam, DEFALT314). `bind_to_monitor` counts left to right, then top to bottom. jade-tam binds placeholder workspaces to the other monitors "to prevent error". Monitor order belongs in the user layer and gets rendered into the GlazeWM template, like keybindings today.
23. **Settings an app writes on exit** (DEFALT314: Flow Launcher). Stop the app before editing a settings file it saves when it quits, or the edit is lost. The same applies to any app we theme by editing its JSON.
24. **Pin what we don't fork.** jade-tam pins GlazeWM (`--version 3.10.1 --no-upgrade`) because a new version broke their configs (#47). We own the GlazeWM fork; Zebar, while it stays, should be pinned the same way.
25. **Don't start the desktop from the Run key.** Both rice repos start GlazeWM from `HKCU\…\Run` or a Startup shortcut, which waits for Explorer. Our scheduled task already does better; keep it. Watch upstream #1358 (cloak plus native minimize loses windows) and #1446 (`hide_method: hide` leaves windows invisible after RDP); we use `cloak`.

### Positioning (README and mission)

26. **Try Omarchy's limits are omawin's pitch.** It runs the real Omarchy in a VM: a ~2 GB download, virtualization required, a virtual GPU (freezes in #276, Vulkan failures in #173), and a running fight with the host over keys, clipboard (#16), volume (#277, #287), sleep (#216) and display scale (#213). Its users ask for what a native desktop gives: Omarchy's way of working permanently, beside Windows apps that must run natively (#88, #215). Their WSL experiment (`docs/WSL-GPU-EXPERIMENT.md`) found Hyprland can't run nested under WSLg, so our WSL preset should promise Linux dev tooling and CLI, not a Linux desktop. Say "Omarchy's way of working, on your Windows", and link Try Omarchy as the way to try the real thing.

## What not to copy

From their issue trackers, things that broke users' PCs or made them less safe:

- **Anything touching Windows Update or its protected services** (WinUtil). "Disable all updates" left the Store and winget stuck at Queued (#3859). WaaSMedicSvc is protected (`LaunchProtected=2`): a `Set-Service` on it failed silently and stayed half-applied (#5095). Leftover `WUServer` / `UseWUServer` policies broke Windows Update (#5130, open). This is the mission's "never break Windows" rule.
- **Removing AppX packages** without a reinstall path: Widgets removal has no undo; the WindowsAI tweak writes an `EndOfLife` key that blocks reinstalling; Xbox and Game Bar removal brought "missing app" popups (#4397). jayharris's mass `-AllUsers` removal uses a list from 2015–2022. Use policies instead.
- **Disabling background apps globally** (`BackgroundAccessApplications\GlobalUserDisabled=1`): broke Windows Search (WinUtil #2541).
- **Removing Edge**: WebView2 apps depend on it, and WinUtil's method plants a fake `MicrosoftEdge.exe`.
- **Weakening Defender**: process and drive exclusions (above); jayharris's `MAPSReporting 0` and `SubmitSamplesConsent 2`, which turn cloud protection off.
- **Machine-wide policies for personal choices**: Microsoft's Edge policies under `HKLM\SOFTWARE\Policies` make Edge say "managed by your organization". Do not disturb on, Remote Desktop on, the Bluetooth icon hidden: personal or security choices pushed as defaults belong in presets, if anywhere.
- **Performance folklore**: jade-tam's `high-ram-tuning.ps1` (`DisablePagingExecutive`, `LargeSystemCache`, `IoPageLockLimit=1`), `EnableSuperfetch`, `powercfg` standby changes and hibernate off as defaults.
- **Hacks on system files**: `icacls … /deny Everyone:F` on the Store's database to hide search results (WinUtil).
- **Editing an app's config in place to switch themes** (jade-tam's yq / jq patches, which `chezmoi apply` then reverts). Our render-from-template model is the fix.
- **Restarting Explorer** to apply a setting, where `SHGetSetSettings` or a settings broadcast does it live.
- **Profile work on every shell start**: `[Environment]::SetEnvironmentVariable` blocks on a `WM_SETTINGCHANGE` broadcast (jayharris #11, and his profile still writes environment variables on every start); freddiehaddad runs `VsDevCmd.bat`, `rustup completions` and `op completion | iex` each time. Set environment variables at install; cache generated init scripts to files.
- **A web runtime in the bar**: jade-tam's Zebar bar compiles React with Babel in the browser, from a CDN (`react@18?dev`), so it needs the network at first start and costs RAM against our budget.
- **Binaries and history in the repo**: jade-tam ships btop4win's exe and dlls (Defender flagged them, #53) and has a 111 MB `.git` (#41).
- **`HKCU\…\Policies\Explorer\NoWinKeys=1`** (freddiehaddad) as a default: it frees Win+ chords for the WM but kills all of Explorer's, Win+V and Win+Shift+S included. A preset at most.
- **A signed exe as the install** (Try Omarchy): more to trust and a signing chain to maintain (#177). The `irm` script with checked release binaries fits omawin. Their quick start's default credentials (`omarchy` / `omarchy`) and the agent-session notes published in their `docs/` aren't for us either.
- **Signed `.ps1` files with non-ASCII text and no BOM** fail with `HashMismatch` on PCs using the UTF-8 code page (Microsoft #137). When we sign scripts: a BOM or ASCII only, and a CI check that signed copies haven't drifted.
