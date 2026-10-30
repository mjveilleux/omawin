# Roadmap: Omarchy's developer experience on Windows

The big plan: where omawin stands on 2026-10-30, where it's going, and for each piece of work what to build, which files it touches, how we'll know it's done, and what could go wrong. The goal is to come close to Omarchy 4 in how omawin installs and how it feels to use: a menu that answers instantly, windows that move smoothly, an install that's done before your coffee is, Linux as a first-class home, and a shell you can extend with plugins.

[mission.md](mission.md) says why omawin exists; [mission-todo.md](mission-todo.md) is the running checklist by track. This file is the plan behind it. When an item lands, tick it in mission-todo.md with the commit; this file changes when the plan changes.

Sources read for this plan: this repo; Omarchy 4 (`mjveilleux/omarchy`: `shell/`, `docs/omarchy-shell.md`, `docs/menu.md`, `docs/update-process.md`, `agents/skills/motion.md`, `default/hypr/looknfeel.lua`, `install/omarchy-base.packages`); our GlazeWM fork (`mjveilleux/glazewm`, branches `omawin`, `feat/accordian-v1`, `feat/master-stack`, `feat/auto-tiling`); a web survey of Windows sandboxes and similar projects (2026-10-30, links in phase 7). File and line references are as of 2026-10-30 and will drift; they say where to start, not what to trust blindly.

## How to read this

- **Phases** run roughly in order, but most items inside a phase are independent: one agent or one sitting each.
- Every item says **why** (what you feel), **what to build**, **which files**, **done when**, and **risks**. Size: **S** (an afternoon), **M** (a few days), **L** (a week or more).
- **🖥 = try it on the PC.** This plan was written in a cloud session that can't run Windows. Items marked 🖥 need a try on a real Windows PC before they merge to main (AGENTS.md: ask before driving the desktop; screenshot visible changes). Items CI can check say so.
- Keys never change by guesswork (AGENTS.md). Nothing here rebinds a key; where an item proposes a binding, it's confirmed first.

## Decisions made

| Date | Decision |
|---|---|
| 2026-10-15 | One always-running native shell (the menu app) draws everything; no Zebar fork; Explorer stays ([shell-apps.md](shell-apps.md)) |
| 2026-10-30 | Plugins keep **Omarchy's `manifest.json`**; panels are **loose XAML** loaded by the shell; widgets and services are **commands that print JSON lines** |
| 2026-10-30 | omawin's **command-line tools come through mise**, pinned in the repo; desktop apps stay on winget |
| 2026-10-30 | **winget, Scoop and Chocolatey all stay** as your choice on the Packages page; omawin's own installs still avoid Chocolatey |
| 2026-10-30 | **Linux is first class; Arch is the default Linux** (Ubuntu alongside). Linux is the default terminal |
| 2026-10-30 | Linux follows **all of Omarchy's package sources and decisions**: Omarchy's repositories (stable channel), Omarchy's package choices for the terminal, **no AUR** |
| 2026-10-30 | Files means **the Win+F finder** (fff): it grows into a keyboard file manager; Explorer gets developer defaults only |
| 2026-10-30 | **Terminals show which side they're on**: Linux and PowerShell look different at a glance |
| 2026-10-30 | **Never open Settings again**: every Windows setting is searchable in the system menu (Win+Alt+Space), and the common ones switch on and off right there |
| 2026-10-30 | **Omarchy plugins run unchanged** on omawin: stand-ins for Omarchy's commands in Arch, and a Qt host on Windows that runs their QML (Windows runs QML; only Quickshell's Linux modules are missing, so omawin provides them) |

## Where we are

**Done and solid:** the one-line bootstrap; one admin prompt; presets on a lean core; releases built in CI, so a new PC downloads binaries instead of compiling; the install screen; migrations; `omawin check`; the menu app as one always-running native process (launcher, system menu, file finder, clipboard, Quick settings, screenshots with OCR, colour picker, QR, calculator, the packages page); 24 Omarchy themes applied live across every app; noctty (our Ghostty fork) and herdr; Linux in WSL with Arch as the default, on Omarchy's package repositories, Ubuntu as the other choice; dev environments through mise and Omarchy's Docker databases (Install › Development, [dev-envs.md](dev-envs.md)); Native AOT for the menu app (first start 0.34 s, 114 MB); the native bar, now the default.

**Built, not yet shipped:** window animations in the GlazeWM fork (branch `omawin`): new windows fade in where they tile, the others slide, drawn from snapshots (Windows Graphics Capture, D3D11, DirectComposition), ease-out-quint, 200 ms. Never tried on a PC, not in a release.

**The gaps against Omarchy, by what you feel:**

| You notice | Omarchy | omawin today |
|---|---|---|
| A key opens the menu | the key goes straight to the resident shell; no process starts | GlazeWM starts `omawin.exe` (~35 ms), which writes a request file and signals the menu app |
| The menu opens and closes | in within 250 ms, out in ⅔ of that (ease-out-quint) | a 140 ms fade and rise in; out is instant |
| Windows open and move | pop in and slide (Hyprland) | instant (animations unreleased) |
| Install | pacman, 5 downloads at once, a prebuilt image | winget, 4 at once; ~150 s of packages before anything else; the desktop starts only after you sign out and in |
| Extending it | plugins: a folder with `manifest.json`, `omarchy plugin add <git-url>`, hot reload | rows in text files; no widgets, panels or plugins of your own |
| Everyday dev setup | dev envs, Docker databases, web apps, TUIs, a key cheat sheet built from the bindings | dev envs and Docker databases on Windows; no web apps; a hand-kept key list |
| Linux | it *is* Linux | WSL opt-in; the terminal opens PowerShell unless the window in front is in a Linux folder |
| Where am I? | one OS | Linux and PowerShell terminals look identical |
| Changing a setting | one menu for every setting (Omarchy's Setup menu) | the system menu opens a few Settings pages; everything else is Windows' Settings app |
| Files | Nautilus, Sushi previews, "here" keys | the Win+F finder: search, preview, chips; open or show in folder, nothing more |
| Trying things safely | disposable VMs in its acceptance tests | nothing; the install has never run on a clean PC |

## Milestones

Each milestone is a release (`v*` tag, built by `.github/workflows/release.yml`).

| Release | Contents | Feels like |
|---|---|---|
| **"snappy"** | Phase 1: menu profiled, keys straight to the shell, motion tokens, window animations shipped, Zebar gone | everything answers instantly and moves smoothly |
| **"settings"** | Phase 1B: every Windows setting in the system menu, switches inline, `omawin setting`, your settings applied on a new PC | you never open Settings again |
| **"fast"** | Phase 2 and 7.1: CI install timing, desktop first, mise tools, one release zip, the Sandbox install test | a fresh PC is a desktop in three minutes |
| **"linux"** | Phase 3: Linux by default, Omarchy's terminal layer in Arch, terminals coloured by side | Win+Enter is Arch, and you always know where you are |
| **"plugins"** | Phase 4: plugin format, `omawin plugin`, our panels as plugins, Omarchy plugins running unchanged | you add to the bar and menu without touching omawin, from omawin's gallery or Omarchy's |
| **"parity"** | Phases 5 to 7: Omarchy's DX, the finder as file manager, sandboxes | Omarchy's habits work on Windows |

---

## Phase 1: snappy

Every key answers in one frame, and everything moves the way Omarchy moves.

### 1.1 `omawin profile menu` (S, 🖥)

**Why:** "snappy" has to be a number before we change anything, as `omawin profile terminal` did for the terminal.

**Build:**
- Trace marks where they're missing. `omawin.exe` has `Trace()` (`bin/omawin.cs:72-80`) but none in `Send` (`:189-204`); the menu app has `Program.Trace()` (`menu-app/App.xaml.cs:52-56`) but none in `HandleRequest` (`:194-229`) or `Show` (`MainWindow.xaml.cs:196-247`). Add `omawin send`, `menu request`, `menu shown` and `menu painted` (on the first `CompositionTarget.Rendering` after `appWindow.Show()`).
- `bin/profile-menu.ps1`, modelled on `bin/profile-terminal.ps1` (`:32-105`: the trace-on file, `Mark`, lines of `label\tticks`): opens each surface N times, prints a timeline per run and the median, for the launcher, the system menu and the finder. Its label map gains `menu`.
- A row in `bin/commands.txt`: `profile menu | …`.
- Typing too: from a key in the search box to the list redrawn, so regressions in `Fuzzy.cs`, `Launcher.cs` and `FileSearch.cs` show.

**Done when:** `omawin profile menu` prints key → painted for each surface, and the baseline is written down here.

**Risks:** GlazeWM ignores simulated keys (AGENTS.md), so the driver calls `omawin.exe launcher` directly and measures from there, plus one hand-pressed run for the keyboard hook's share.

### 1.2 Keys straight to the shell (M, 🖥; fork + menu app)

**Why:** each menu key now starts a process (`shell-exec --hide-window omawin.exe launcher`, `glazewm/config.yaml:300`), writes `%LOCALAPPDATA%\launcher\system-menu-request` and sets `Local\dotfiles-system-menu-signal`. Omarchy's Super+Space starts nothing: the shell registered a global shortcut. We can do the same, because the menu app already holds a websocket to GlazeWM (`Bar/GlazeWm.cs:29-58`, `Tiling.cs:28-45`).

**Build (fork):** a new command `emit <name> [args…]` that broadcasts a custom event to IPC subscribers:
- `InvokeCommand::Emit { name: String, args: Vec<String> }` in `packages/wm-common/src/app_command.rs` (the clap enum at ~:157; `ShellExec` at :208 is the model).
- `WmEvent::Custom { name, args }` in `packages/wm-common/src/wm_event.rs`, and `SubscribableEvent::Custom` (`app_command.rs:137`).
- An arm in `IpcServer::process_event`'s exhaustive match (`packages/wm/src/ipc_server.rs:357-404`) and in `WindowManager::run_command` (`packages/wm/src/wm.rs`) calling `state.emit_event`.
- `emit_event` drops events before initialization and while paused (`wm_state.rs:558-570`); a menu key while paused should still work, so `Custom` gets the exemption `PauseChanged` has.
- A unit test for parsing; doc comments on every new function (the fork's guidelines).

**Build (menu app):** `Bar/GlazeWm.cs` subscribes to `custom` too (its `sub --events …`, :39) and hands `name == "omawin"` events to `App.HandleRequest(args)` on the UI thread, exactly as the request file does today.

**Build (config):** the menu bindings become `emit omawin --launcher` and so on, for Win+Space, Win+Alt+Space, Win+Shift+V, Win+A and Win+F. If no subscriber is connected (the menu app isn't running) nothing happens; GlazeWM starts the menu app (`omawin start`) and `omawin check` warns when it isn't subscribed. Decide during the build whether to add an `emit --or-exec <command>` fallback.

**Done when:** `omawin profile menu` shows the process start and file write gone; the menu still opens right after the menu app restarts.

**Risks:** the official GlazeWM (no fork) doesn't know `emit`. `glazewm/config.yaml` is already a template (`themes/render.ps1`), so the renderer writes `emit` for the fork and `shell-exec … omawin.exe` otherwise.

### 1.3 The Native AOT build for everyone (S)

**Why:** AOT starts in 0.34 s instead of 1.04 s and uses 114 MB instead of 173 MB ([native-bar.md](native-bar.md)).

**Build:** the release job (`release.yml:87-105`) calls `menu-app\build.ps1 -BuildOnly` without `-Aot`; `build.ps1:39-44` picks AOT only if it finds Visual Studio's C++ tools. Pass `-Aot` in CI so a runner image change can't silently ship the JIT build, and fail the job unless `build-kind.txt` says `native`.

**Done when:** every release's `SystemMenu-*.zip` has `build-kind.txt` = `native`, checked by CI.

### 1.4 Motion from one place (M, 🖥)

**Why:** Omarchy's motion rules make it feel calm and quick: entrances within 250 ms, exits about ⅔ of that, ease-out-quint (`cubic-bezier(0.23, 1, 0.32, 1)`), never linear or ease-in (`agents/skills/motion.md`, `default/hypr/looknfeel.lua:69-93`). Ours are scattered: the menu's 140 ms fade and 180 ms rise (`MainWindow.xaml.cs:291-305`), staggered rows (`:350-376`), the bar's 280 ms widget fade (`Bar/Bar.cs:317-345`), the drawer's 200 ms (`Bar/Widgets.cs:310-315`), and no exit animation (`HideMenu` is `appWindow.Hide()`, `:249`).

**Build:**
- `menu-app/Motion.cs`: the tokens (`Enter = 200 ms`, `Exit = 130 ms`, `Stagger = 18 ms`, `EaseOut` = ease-out-quint as a `CubicBezierEasingFunction`), a global `Enabled` read from `%LOCALAPPDATA%\launcher\animations` (`on` / `off`), and helpers (`FadeIn`, `FadeOut(then)`, `Rise(px)`).
- The menu's `Animate`, `Cascade` and `Slide`, the bar's fades and Quick settings use them. The bar's storyboards move to Composition (off the UI thread).
- **An exit animation:** `Dismiss()` fades the root out (130 ms) and hides when done; a key during the exit cancels it and re-shows, so nothing ever waits on an animation.
- `omawin toggle animations` (`bin/commands.txt`): writes the file the shell watches and sets GlazeWM's `animations.enabled` (1.5). Off by default in a VM (`Win32_ComputerSystem.Model`), as Omarchy's `vm-no-animations.sh` does.

**Done when:** every surface uses `Motion.cs`; the menu fades out; animations off makes every duration 0.

### 1.5 Ship window animations (L, 🖥; fork)

**Why:** the single most visible difference from Omarchy.

**What exists** (fork branch `omawin`): `packages/wm/src/animation_manager.rs` (906 lines) with three plans: `Slide` (a snapshot slides while the real window waits invisible at its target), `FadeIn` (the real window placed invisibly, then faded in) and `Live` (the real window moved each frame, Hyprland-style; judders on Windows because apps can't repaint while resized). Snapshots: `wm-platform/src/platform_impl/windows/animation_window.rs` (871 lines). Config in `wm-common/src/parsed_config.rs:393-437`: `animations.window_open` and `window_move` (`effect_type`, `duration_ms`, `easing`, `trigger_threshold`). omawin's `glazewm/config.yaml:55-65` asks for slide 200 ms ease-out-quint and fade 200 ms ease-out-cubic.

**Build:**
1. **Bring the frame check back to CI.** `scripts/animation-check.ps1` (opens Notepads, closes one, moves one; records frames; fails on a leftover overlay or a window not at full opacity) ran in `.github/workflows/animation-check.yaml` until commit `445d61e0` removed it. Restore it (`git revert 445d61e0`) so every fork change is checked on a Windows runner.
2. **A daily-use week on the PC** (🖥). Known rough edges in the code: flicker during sync (`platform_sync.rs:207`), the fullscreen check (`:452`), `trigger_threshold` accepting non-pixel values (`animation_manager.rs:523`), the 100 ms settle wait for new windows.
3. **A close animation.** No hook exists: `handle_window_destroyed.rs` → `unmanage_window` (`:28`) runs after the window is gone. Do it where we still have the window: GlazeWM's `close` command takes a snapshot, then closes; the snapshot fades out (Omarchy's windowsOut: 160 ms, pop-in to 95%). Windows closed by the app itself just vanish, as today. New config `animations.window_close`.
4. **An enabled switch:** today animations are off only when the section is absent; `omawin toggle animations` needs `animations.enabled: false`, keeping the settings while off. Config reload already re-applies (`reload_config.rs:40-43`).
5. **Release** the fork (`scripts/omawin-release.ps1`) and point `install/glazewm-fork.ps1` at it.
6. **Upstream** what's general to glzr-io/glazewm (the snapshot slide, easing functions), so the fork stays small.

**Done when:** the frame check passes in CI at 200 ms and 600 ms; a week of daily use without a stuck overlay; Omarchy's feel by eye.

**Risks:** GPU drivers; Graphics Capture's yellow border on older Windows builds (a fix commit exists); fullscreen apps and games (animations skipped for fullscreen, as now); a DPI change mid-animation. Workspace switches stay instant, as Omarchy keeps them.

### 1.6 Finish the native bar (M, 🖥)

[native-bar.md](native-bar.md) step 2's media widget, then step 3: Zebar removed with a migration (`migrations/<date>-zebar-out.ps1`: stop it, uninstall the package, remove our Zebar fork's files and `ZEBAR_ASSET_PORT`). `bar.json` stays where the native bar reads it until 4.5 moves it. The music and agents drop-downs move to the shell (the first plugins, 4.6). Saves ~220 MB and the 2 s sign-in paint; removes `glzr-io.zebar` and the Zebar client build from install.

### 1.7 Typing never waits (S, 🖥)

The launcher builds up to 150 rows on each keystroke (`MainWindow.xaml.cs` ~:574). Virtualize the list (an `ItemsRepeater` with recycling) so only visible rows are built, and keep icon loading off the UI thread. Measured by 1.1's typing timeline: under 16 ms per key.

---

## Phase 1B: every Windows setting in the system menu (next)

**Why:** Windows' Settings app is slow to open, deep, and changes between builds. Omarchy has one menu for everything you change; omawin's system menu (Win+Alt+Space) has a few rows that open Settings pages (`launcher/menu.txt:70-76`). The goal: **you never open Settings again**. Type a few letters ("dark", "ext", "mic", "hz"), see the setting with its current state, press Space to switch it. What can't be switched from outside Windows' own app opens on its exact page, one keystroke away.

What exists to build on: the system menu's search (`MenuModel.Search`, fuzzy, `MainWindow.xaml.cs:598`), the launcher's usage ranking (`Launcher.cs:173-222`), and Quick settings' working switches (`menu-app/QuickSettings.cs`: Wi-Fi, Bluetooth and other radios, brightness per monitor, power mode, battery, night light's state, notifications' state, change watching).

### 1B.1 The index: every setting Windows has (M, 🖥)

- **Windows' own search data.** Settings' search box reads an index Windows ships: `%WINDIR%\ImmersiveControlPanel\Settings\AllSystemSettings_*.xml`, one entry per setting with its name, keywords and the page that holds it, in the PC's language (names are resource strings, resolved with `SHLoadIndirectString`). Read it once in the background, cache it in `%LOCALAPPDATA%\launcher\settings-index.tsv`, rebuild when Windows updates (the file's date). Confirm the format on the PC first; it's undocumented.
- **Fallback and extras:** the documented `ms-settings:` pages (a curated list in the repo), Control Panel items (the shell's Control Panel folder), the admin consoles (Device Manager, Services, Event Viewer, Disk Management, Task Scheduler), and classic dialogs people still need (environment variables: `rundll32 sysdm.cpl,EditEnvironmentVariables`; advanced system settings; network adapters).
- Each becomes a row: icon, name, the page it's on (`System › Display`) as the dimmed second line.

### 1B.2 Switches: change it without opening anything (L, 🖥)

The core of the feature: a catalog of settings omawin can **read and change itself**, shown as switches, choices or sliders in the row.

- **The catalog** is data, `launcher/settings.txt` (layered like `items.txt`: the PC's shared folder and the account folder can add their own), one setting per line: `id | icon | name | keywords | kind | how | notes`.
  - `kind`: `switch` (on / off), `choice` (one of a few values), `slider` (a number in a range), `action` (do it once), `link` (open its page).
  - `how`, declarative where possible: `reg HKCU\…\Value on=1 off=0 [broadcast] [restart-explorer]`; `spi <SystemParametersInfo action>`; `powercfg <setting>`; `builtin <name>` for anything that needs code (radios, display modes, HDR, default audio device, proxy); `link ms-settings:<page>`.
  - `notes`: `admin` (needs elevation), `signout` / `restart` (when it takes effect), `minBuild` (the Windows build it's known to work on).
- **Handlers** in `menu-app/Settings/` (`Catalog.cs` reads the file; `Switches.cs` does `reg`, `spi`, `powercfg`; `Builtins.cs` reuses `QuickSettings.cs` and adds the rest). After a registry change, the `broadcast` flag sends `WM_SETTINGCHANGE` so Explorer and apps pick it up without a restart.
- **The first catalog, ~80 settings** (each checked on the PC before it ships):
  - *Look:* dark mode for apps and for Windows, transparency, animations, accent colour from the omawin theme, show desktop icons.
  - *Explorer:* file extensions, hidden files, protected system files, full path in the title, compact view, open to Home or This PC, Gallery and recommendations, end task on the taskbar's menu, seconds on the clock.
  - *Privacy:* camera, microphone and location for apps (the per-user consent store), advertising ID, tailored experiences, online speech recognition, activity history.
  - *Input:* mouse acceleration, pointer speed, key repeat rate and delay, sticky keys' shortcut prompt, touchpad tap to click and scrolling direction.
  - *System:* clipboard history, notifications on / off, game mode and the game bar, startup delay, power mode (exists), screen and sleep timeouts, lid close action, hibernate and fast startup (admin).
  - *Display:* refresh rate and resolution per monitor (`ChangeDisplaySettingsEx`), HDR (`DisplayConfigSetDeviceInfo`), brightness (exists), night light (state readable; switching opens its page, as today).
  - *Connections:* Wi-Fi, Bluetooth, airplane mode (radios, exist), mobile hotspot (`NetworkOperatorTetheringManager`), the system proxy on / off, VPN connections (exist).
  - *Sound:* default output and input (exist in Audio), mono audio.
  - *Developer:* developer mode, long paths, sudo for Windows (`sudo config`), Windows features (WSL, Windows Sandbox, Hyper-V; admin and a restart), Dev Drive and Defender exclusions for dev folders (admin; explained before doing it).
- Reading is lazy (only rows on screen), each a registry or API read of microseconds, and kept fresh with `RegNotifyChangeKeyValue` and the APIs' change events, so a change made elsewhere shows at once.

### 1B.3 In the menu (M, 🖥)

- Typing in the system menu searches its own rows **and** every setting, mixed and ranked by the fuzzy score plus what you use most (the launcher's history, so "dark" puts *Dark mode* first after you've used it once). Tab reaches a **Settings** chip to search settings only.
- A switch row shows its state as a pill (On / Off); **Space** or **Enter** flips it at once, then re-reads to confirm. A choice row lists its values inline (← / → to step, Enter to pick); a slider moves with ← / →. A link row ends in ↗ and opens its page. Admin rows show a shield; `signout` / `restart` rows say when the change takes effect.
- **Undo**: every change goes to `%LOCALAPPDATA%\launcher\settings-history.tsv` with the value before it; Ctrl+Z in the menu and an *Undo last change* row put it back.
- The launcher (Win+Space) shows the top setting matches under apps, so either key finds them.
- `launcher/menu.txt`'s Setup section gains a *Windows* submenu by area (Look, Explorer, Privacy, Input, System, Display, Connections, Sound, Developer) for browsing without typing.

### 1B.4 Every setting is a command (S)

`omawin setting list [area]`, `omawin setting get <id>`, `omawin setting set <id> <value>`, `omawin setting toggle <id>` (rows in `bin/commands.txt`, handled by the menu app, so no PowerShell start). Bind one to a key, run it from a script, or let an agent change a setting with a command it can read back. Tab completion in the PowerShell profile.

### 1B.5 Your settings follow you to a new PC (M)

`omawin settings save` writes the settings you've changed (`id = value`) to the account folder (`~/omawin-account/settings.txt`) or the PC's shared folder; install and `omawin settings apply` set them on a new PC, admin ones in the one admin prompt. The no-ads tweaks and Explorer's developer defaults (6.8) become entries in the same catalog: one mechanism for every Windows setting omawin touches, each with undo. Dotfiles, for Windows itself.

### 1B.6 Safety (part of every item)

Never break Windows (mission.md): only settings that are documented or long-stable; never the keys Windows protects (default apps, under UCPD); no group policies unless you ask; admin changes said up front, batched into one prompt, never silent; every change undoable; each catalog entry carries the build it's known to work on, and is hidden on others.

### Testing

CI (a Windows runner) round-trips every `reg`, `spi` and `powercfg` entry: read, set the other value, read back, restore. The PC checks the builtins and what takes effect where (🖥).

### fff and ripgrep here

Neither is the right tool at run time, and the plan doesn't use them there: fff indexes files on disk and ripgrep searches file contents, while settings are a few thousand rows in memory, which the menu's own fuzzy matcher scores in microseconds (what the system menu already does). fff's idea does carry over: rank by what you use (frecency). ripgrep helps at development time: a script that searches Windows' settings files and policy definitions (`C:\Windows\PolicyDefinitions\*.admx`) after a Windows update for new settings worth adding to the catalog.

### Done when

The "never open Settings" list: 30 everyday changes (dark mode, file extensions, the microphone for apps, refresh rate, mouse acceleration, notifications, power mode, developer mode, …) each done from Win+Alt+Space in a few letters and one key, with undo; CI's round-trip passing; `omawin settings apply` setting up a fresh PC (in `omawin sandbox test`, 7.1).

**Risks:** the settings index's format is undocumented (fallback: the curated page list); Windows changing a setting's registry value between builds (`minBuild`, CI on current Windows); settings that only apply after sign-out (said in the row); some settings have no supported way to change (night light, do not disturb): those stay links, honestly labelled.

## Phase 2: fast install

**Goal:** a clean Windows 11 PC goes from the one line to a working desktop in **under 3 minutes**, and to everything in **under 10**. Measured, not guessed.

What install does today: `install.ps1` asks the questions, starts the one admin prompt as a separate hidden elevated process (`install/admin.ps1:61-89`; machine-wide packages three at a time, `admin-batch.ps1:40-80`), then runs `install/steps.txt` in order: `no-ads`; `software` (all core and preset winget packages in one batch, four at a time, `Invoke-WingetBatch`, `install/software.ps1:52-118`; then extras one by one; then the Nerd Font); `shell`; `desktop`; `helpers` (three separate release downloads, each resolving the tag and manifest again, `bin/install-binaries.ps1`); `look`; `other`; then migrations. The desktop isn't started: the summary says to sign out and back in (`install/ui.ps1:453`).

### 2.1 Time the install in CI (M)

**Build:** `.github/workflows/install.yml`, scheduled and on demand: a fresh `windows-2025` runner runs `bootstrap.ps1` from the branch (`OMAWIN_PLAIN=1`), then `omawin check`, and uploads `install.log` and `install-times.txt`. It fails when `check` reports a failure or the time passes the budget. Runners have no WSL (no nested virtualization) and no interactive desktop, so the job skips the `wsl` preset and the desktop start, and says so.

**Done when:** every push to main has an install time, with the trend in the job summary.

**Risks:** runner images come with Git, Node and PowerShell 7 preinstalled, so the install looks faster than on a real clean PC. Report install's own "already there" counts, and keep the Windows Sandbox run (7.1) as the honest clean test.

### 2.2 Desktop first (M, 🖥)

**Why:** today you wait for every package, then sign out. The desktop should come up while the rest installs.

**Build:**
- A new early step `essentials` in `install/steps.txt`, right after `no-ads`: winget installs only GlazeWM and the .NET desktop runtime (if still needed, 2.7), in parallel with the one release download (2.5) and noctty; then the theme and the font.
- Then **start the desktop**: register the sign-in task (`install/login-start.ps1`) and also start it now (`omawin start glazewm`), so GlazeWM, the bar and the menu app are up. The admin step's fork install restarts GlazeWM when it lands (`glazewm-fork.ps1 -Restart` exists).
- The rest continues; the install screen stays in its terminal window (a window rule keeps it floating, like `menu-task`). The bar shows an install indicator that opens the log, and a toast says when it's done (5.5).
- The summary stops saying "sign out and back in"; only WSL's restart remains.

**Done when:** in 2.1's job, "GlazeWM and the menu app running" comes under 3 minutes; on the PC you're tiling windows while packages install.

**Risks:** Explorer's taskbar auto-hide (the `look` step) should move earlier too; the console resizing under the install screen (it redraws on resize already).

### 2.3 Command-line tools through mise (M)

**Why:** winget spends seconds per package before downloading; these are single exes.

**Build:**
- `mise.toml` in the repo: ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, gh, tree-sitter, uv, and Neovim if mise's Windows build is good (else it stays on winget). Exact versions and mise's lockfile.
- mise itself: move `Initialize-Mise` from `bin/install-dev-env.ps1:170-189` (winget `jdx.mise`, user scope, shims first on the user PATH) into `install/lib.ps1`, shared by install and dev envs; or fetch mise's release exe directly, which is faster than winget.
- The early step runs `mise install` (parallel downloads) alongside the winget batch.
- `packages.ps1` drops those winget ids and their `$WingetCommands` rows.
- `check.ps1:44-60`: `Get-Command` finds shims on PATH; its fallback paths learn `%LOCALAPPDATA%\mise\shims`.
- On existing PCs, a migration: tools winget installed stay (never uninstall the user's programs); mise's shims come first on PATH; `omawin update` runs `mise install` for new pins and `mise upgrade` for omawin's tools.

**Done when:** a clean install fetches all the tools in a few seconds; `omawin check` passes.

**Risks:** GitHub's anonymous limit (60 API requests an hour per IP): pinned versions and the lockfile avoid lookups. A work network blocking github.com: winget fallback per missing tool, said in the summary.

### 2.4 Neovim ready on first open (S)

lazy.nvim bootstraps on the first `nvim` (`nvim/lua/config/lazy.lua:1-15`), so the first open waits. After install's last step, run `nvim --headless "+Lazy! restore" +qa` hidden in the background (restore from `lazy-lock.json`, so versions match), and Mason's core tools. Same inside Linux (3.4).

### 2.5 One download for our own pieces (S)

Resolve the release tag once, fetch `SHA256SUMS.txt` once, then download the helpers zip, the menu app zip and the music exe in parallel (PowerShell 5.1-safe: `HttpClient` tasks or BITS), verifying each. Later, one `omawin-<tag>-win-<arch>.zip`.

### 2.6 A leaner core (S)

Move Neovide and Windows Terminal (Windows 11 ships it) to presets; drop `OpenJS.NodeJS.LTS` when mise provides Node (Mason needs `npm` on PATH; mise's shims give it). `zig.zig` stays while nvim-treesitter compiles parsers with it. Each change: `packages.ps1`, `check.ps1`'s list, the docs.

### 2.7 Is the desktop runtime still needed? (S)

`Microsoft.DotNet.DesktopRuntime.9` is one of the slowest items. The AOT menu app is self-contained; if it no longer needs the runtime, only the music player does: publish the player self-contained (or AOT) too, and the runtime leaves the core.

---

## Phase 3: Linux in WSL, first class (Arch by default, Omarchy's way)

Linux is first class; Arch is the default Linux, Ubuntu the other choice. On Arch, omawin follows **all of Omarchy's package sources and decisions**: Omarchy's repositories on the stable channel (already: `wsl/setup.sh` writes Omarchy's `pacman.conf` and mirrorlist and trusts its key), Omarchy's package choices for anything that runs in a terminal, Omarchy's shell defaults, mise for languages, Docker the way Omarchy runs it, and **no AUR** (no `yay`, though Omarchy's base list has it). Your Linux is the default terminal and where your code lives. A distro you already had stays yours ([AGENTS.md](../AGENTS.md)).

What exists: `wsl/install.ps1` picks the distro (your `-Linux`, the saved pick, a distro you have, else Arch), installs it with `wsl --install -d <name> --no-launch`, makes a Linux user named after yours (sudo without a password), then runs `wsl/setup.sh root` and `setup.sh user`. Arch gets base-devel, the CLI tools, Neovim, Node, Python and herdr from Omarchy's repos; Ubuntu gets apt's equivalents plus Neovim and tree-sitter from their releases. `setup.sh user` links `~/.config/nvim` to the repo, adds an omawin block to `~/.bashrc` that sources `bash/bashrc`, points git at Windows' credential manager and installs the agents. `omawin update` runs `setup.sh update` (Arch only). `/etc/wsl.conf` gets only the default user. Linux projects show in the launcher, Linux servers in Localhost, and Neovim in Linux follows the theme live.

### 3.1 On by default (S)

The `wsl` preset joins the default set (`packages.ps1`, `Initialize-Presets`) with Arch preselected: Enter through the questions gives you Arch. WSL is already in the one admin prompt (`install/admin-batch.ps1:97-125`) and resumes at the next sign-in after Windows' restart (`wsl/install.ps1:51-65`).

**Risks:** virtualization off in the firmware. Detect it before offering (Try Omarchy does: [inspiration.md](inspiration.md)) and say "turn on virtualization" rather than looping; PowerShell stays the shell until then.

### 3.2 Omarchy's terminal layer in Arch (M)

**Why:** "all of Omarchy's decisions": the same packages an Omarchy user has in their terminal, chosen and kept current by Omarchy.

**Build:**
- **Packages from Omarchy's own list.** `setup.sh` reads Omarchy's `install/omarchy-base.packages` (from the `mjveilleux/omarchy` fork at a pinned commit) and installs what makes sense without a desktop. A small omawin file, `wsl/omarchy-skip.txt`, lists what to leave out: the desktop (hyprland, quickshell, sddm, uwsm, plymouth, xdg-desktop-portal-*), hardware (bluez, brightnessctl, networkmanager, cups, power-profiles, fcitx5), GUI apps (chromium, nautilus, obsidian, libreoffice, imv), fonts Windows draws, and `yay` (no AUR). Everything else comes: today bat, btop, docker (+ buildx, compose), eza, fastfetch, fd, fzf, git, gum, herdr, jq, lazygit, less, man-db, mise, nvim, ripgrep, starship, tldr, tmux, tree-sitter-cli, try, usage, yt-dlp, zoxide and more. A new terminal tool in Omarchy arrives on the next `omawin update` with no change in omawin.
- **Omarchy's shell defaults:** Omarchy's `default/bash/` (rc, aliases, functions, envs, inputrc) sourced in Linux, then omawin's `bash/bashrc` on top for what's ours (the theme, `OMAWIN`, Windows interop, 3B). Where they disagree, Omarchy wins unless there's a Windows reason, written next to the override.
- **Omarchy's configs** for terminal tools omawin doesn't configure itself (btop, lazygit, tmux): copied once from Omarchy's `config/<tool>` (its refresh-config pattern), themed by 3.8.
- **Neovim stays omawin's config** (`nvim/`, shared with Windows), not `omarchy-nvim`: one config on both sides.
- Ubuntu gets the nearest apt equivalents (as now), mise for what apt lacks.

**Files:** `wsl/setup.sh`, new `wsl/omarchy-skip.txt`, `bash/bashrc`.

**Done when:** a fresh Arch has every terminal package in Omarchy's base list except the skip list; `omawin check` compares the two.

**Risks:** a package pulling a desktop in as a dependency (check what `pacman -S --needed` brought); Omarchy's bash files assuming `$OMARCHY_PATH` (source the files directly, or set it to a folder with just them).

### 3.3 A good WSL VM out of the box (S)

`wsl/.wslconfig` today only sets `autoMemoryReclaim`. Add `networkingMode=mirrored` (localhost both ways, VPNs behave), `dnsTunneling=true`, `firewall=true`, `sparseVhd=true`, and a longer `vmIdleTimeout` (3.5). `setup.sh root` writes `/etc/wsl.conf`: `[boot] systemd=true` (Docker, timers), `[interop] appendWindowsPath=false` (Linux tools win in Linux), `[user] default=<user>` (as now). Each overridable by the account folder ([work.md](work.md)).

**Risks:** mirrored networking trips some VPN clients; `appendWindowsPath=false` breaks `code .` and `explorer.exe .`: small wrappers in `~/.local/bin` for `code`, `explorer`, `clip` and `omawin` (calling `omawin.exe` through interop).

### 3.4 The terminal opens in Linux (M, 🖥)

**Build** (`bin/omawin.cs`):
- `bin/defaults.txt` gains `shell | linux | Linux (WSL) | …` first (the default) and `shell | windows | PowerShell | … | pwsh.exe`. `Choices()` / `Current()` (`:236-257`) work unchanged; `Default()`'s installed check (`:311-312`) treats `linux` as "the picked distro is registered" (`DistroRegistered`, `:334-348`).
- `LaunchTerminal` (`:375-396`): with neither `--wsl` nor a new `--windows`, `Wsl = Current("shell").Name == "linux" && DistroRegistered(Distro)`. No Linux yet: PowerShell, silently.
- `TerminalHere` (`:1010-1072`): a Windows folder in front (Explorer, a PowerShell tab) opens Linux there (`wsl.exe --cd C:\…` maps it to `/mnt/c/…`); a Linux folder opens there (as now); no folder opens Linux's home.
- **OSC 7 from bash:** `omawin.cs:1000-1003` expects bash to report its folder, but `bash/bashrc` doesn't. Add it to `PROMPT_COMMAND` (`printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$PWD"`), so noctty's "the folder of the window in front" works for Linux windows.
- **No pre-started shell for Linux.** PowerShell windows are a hand-off to the running noctty with a spare `pwsh` kept warm (`+new-window`, `:581`; `windows-spare-shell`) only because PowerShell takes ~0.6 s to start. bash in a warm WSL starts in milliseconds, so Linux windows keep their own path (`--single-instance=false -e wsl.exe …`, `:589-597`) with no spare shell. What makes Linux fast is keeping WSL warm (3.5). `omawin profile terminal --wsl` confirms it; only if starting a noctty process per window turns out slow does the fork learn `+new-window -e <command>` (a hand-off, still no spare shell).
- `noctty/config.ghostty:6` (`command = pwsh.exe -NoLogo`) stays as the Windows shell; Linux windows pass their command.
- Windows Terminal: a profile per distro in `windows-terminal/`, `defaultProfile` from the pick.
- `omawin launch project` (`:530-558`): Linux by default (`--windows` overrides); new projects in `~/src` in Linux.
- Install › Development asks the side, Linux first: mise inside the distro (`wsl.exe -d <distro> -- mise use -g <tool>`).

**Done when:** Win+Enter from anywhere opens Linux in the right folder, as fast as PowerShell (3.5); `omawin default shell windows` brings PowerShell back.

**Risks:** git over `/mnt/c` is slow in big repos; projects go in Linux's own disk (`~/src`), and the finder and launcher show them (3.7).

### 3.5 No cold start (S, 🖥)

A cold WSL start takes ~8 s (`manual/04-launcher.md`). When Linux is the default shell, `omawin start` (at sign-in, after the desktop is up) runs `wsl.exe -d <distro> --exec /bin/true` hidden, and `vmIdleTimeout` keeps the VM alive between terminals. `omawin profile terminal` gains `--wsl`; `omawin profile ram` shows WSL's VM as its own line with a budget.

### 3.6 Docker the Omarchy way, in Linux (M)

Omarchy runs Docker Engine (docker, buildx, compose) under systemd, its databases with `docker run --restart unless-stopped` bound to 127.0.0.1 (`omarchy-install-docker-dbs`), and lazydocker on Super+Shift+D. In Linux: Docker Engine from Omarchy's list (3.2), systemd on (3.3), the user in the `docker` group; Install › Development › Docker DB runs inside Linux; mirrored networking makes `localhost:5432` reachable from Windows apps too. Docker Desktop stays a choice, never installed by default.

### 3.7 Linux files in the finder and launcher (M, 🖥)

- **Win+F over Linux:** fff over `\\wsl.localhost\<distro>` would crawl (9P is slow). Instead an indexer inside Linux (`fd` into `~/.cache/omawin/files.tsv`, refreshed by a systemd path unit or inotify) that `FileSearch.cs` reads as one more root, with the distro's chip and logo. A Linux file opens in Linux (`omawin launch editor` with a Linux path).
- **Linux GUI apps** (WSLg) in the launcher: WSL makes Start menu shortcuts for them, which `AppsFolder.cs` may already pick up; check, then give them the distro's logo.
- Projects: `Pages.cs:131-152` asks WSL only while it runs; kept warm (3.5), it always does.

### 3.8 The theme in Linux, live (S)

Starship, fzf, bat and herdr use the terminal's ANSI colours, so they follow already; Neovim follows live. Missing: btop (`themes/set-theme.ps1:209-226` writes btop4win only) and lazygit. `set-theme.ps1` also writes the Linux copies (through `\\wsl.localhost` while the distro runs; at its next start otherwise, from the bashrc block), using Omarchy's own per-theme files where its themes have them (`btop.theme`).

### 3.9 Credentials that just work (S)

Git already uses Windows' credential manager from Linux. Add SSH: Windows' OpenSSH agent (or 1Password's) reached from Linux through `npiperelay` and `socat` (in Omarchy's list), `SSH_AUTH_SOCK` set in the bashrc block; GPG likewise when there are keys. One sign-in, both sides.

### 3.10 Kept, checked, recoverable (M)

- `omawin update`: `setup.sh update` for Ubuntu too (apt), and **Linux-side migrations** (`wsl/migrations/<date>-<what>.sh`, a marker each in `~/.local/state/omawin/migrations/`, Omarchy's pattern), run by `setup.sh update`.
- `omawin check` (today `check.ps1:67-78` only checks the distro is registered): running, systemd up, Omarchy's repos in `pacman.conf`, the tools present, the default user, `.wslconfig` current.
- The bar: a Linux indicator (running, memory) with a click to stop it.
- `omawin linux backup` (`wsl --export` to a dated file) and `omawin linux reset` (export, then unregister only a distro omawin made — the `/etc/omawin` marker — install fresh, set up again, offer to restore `~/src`). Never on a distro omawin didn't make.

### 3.11 Tests for Linux (M)

CI can't run WSL, but it can run `setup.sh` in containers: a workflow with `archlinux:latest` and `ubuntu:24.04` jobs runs `setup.sh root ci` and `setup.sh user` with the repo mounted, then checks the tools and the bashrc block. Plus one run on a real PC per release (🖥).

### 3B. Always know which side you're on (M, 🖥)

**Why:** Linux and PowerShell terminals look the same, so it's hard to tell what you're developing in.

Four signals, cheapest first; the first two alone solve most of it.

1. **The background tints by side.** Linux windows get a slightly different background from PowerShell's, made from the theme (the theme's background mixed ~6-8% toward its accent), so every theme stays coherent.
   - `themes/render.ps1` computes `linux_background` into a small rendered file (`%LOCALAPPDATA%\launcher\theme-linux.sh`).
   - The Linux shell sets its terminal's background with the standard escape for it (OSC 11: `printf '\e]11;%s\e\\' "$OMAWIN_LINUX_BG"`) at start and on each prompt, re-reading the file through `/mnt/c`, so a theme switch shows at the next prompt. noctty (Ghostty's core) and Windows Terminal both honour OSC 11; no terminal change needed.
   - Check that herdr (projects' multiplexer) passes OSC 11 through; if not, herdr's pane background per side, or a small herdr patch.
   - The cleaner alternative, later: the noctty fork takes a per-window colour (`--background`, or a `theme-linux.ghostty` include for Linux windows), with no escape codes.
2. **The prompt says where.** One `starship/starship.toml` for both: the `os` module first in `format` (Arch , Ubuntu , Windows ), coloured by side, and the prompt character's colour by side (from `OMAWIN_SIDE`, set by the PowerShell profile and the bashrc block). Works in every terminal, in herdr, over SSH.
3. **Window titles say where:** "arch · ~/src/app" or "pwsh · C:\…", from bash's `PROMPT_COMMAND` (with 3.4's OSC 7) and the PowerShell profile (`powershell/Microsoft.PowerShell_profile.ps1:120-136` already sets the title). The bar's window title and herdr's tabs show it too.
4. **The window border by side** (fork, L): GlazeWM window rules gain a per-window border colour (matching the title prefix), so the tiling layout itself says which windows are Linux. GlazeWM already sets Windows 11's DWM border colour (`window_effects.*.border`).

**Done when:** a screenshot of a Linux and a PowerShell terminal side by side is obvious at a glance, in a dark, a light and a high-contrast theme.

**Risks:** light themes need the tint to go darker (mix toward the accent, then keep contrast); some TUIs reset the background on exit, so it's re-applied on each prompt.

---

## Phase 4: plugins (Omarchy's shell, on Windows)

Quickshell is Qt on Wayland and won't run on Windows, but its plugin model will. We keep **Omarchy's manifest format** and draw with our own shell, so an Omarchy plugin's idea (and much of its manifest and data) carries over; its QML doesn't.

What exists: the bar reads its layout from `zebar/tokyo-islands/bar.json` (`Bar/Bar.cs:21, 150-163`: `left`, `centerBegin`, `center`, `centerEnd`, `right`); each widget is a class chosen by a switch on its name (`Bar/Widgets.cs:35-49`); a `BarWidget` base with `Attach`, `Update(BarData)` and `Paint(Theme)`; data pushed from background threads to the UI thread (`Bar.cs:168-235`). The menu reads only the repo's `launcher/menu.txt` (`MenuModel.cs:64`), while `items.txt`, `sites.txt` and `keys.txt` are layered (repo, shared folder, account folder; `MenuModel.cs:57-61`). Theme colours are hex strings on a `Theme` object, brushes made in code; no XAML resources. `XamlReader.Load` isn't used yet. Requests are joined and split on spaces (`App.xaml.cs:114-128`), so arguments with spaces don't survive.

### 4.1 The manifest (S)

A plugin is a folder (a git repo) with `manifest.json` in Omarchy's schema (`shell/services/PluginRegistry.qml`): `schemaVersion` (1), `id`, `name`, `version`, `author`, `description`, `kinds` (`bar-widget`, `panel`, `menu`, `service`), `entryPoints`, `keepLoaded`, and the `barWidget` block (`displayName`, `category`, `allowMultiple`, `defaultSection`, `defaults`, settings `schema`). An `omawin` block holds what's ours (`"command": "widget.exe"`, `"xaml": "Panel.xaml"`). [`plugins.md`](plugins.md) has the full design (protocol, panels, safety, commands), with Omarchy's names kept so an Omarchy plugin author recognizes it.

### 4.2 Widgets and services as commands (M, 🖥)

The menu app is Native AOT, so it can't load plugin DLLs. A widget or service is a program or script that prints **JSON lines** (Waybar's format, which Omarchy's `type: "command"` modules use: `text`, `tooltip`, `class`, `percentage`, `alt`), once per `interval` or continuously. Clicks, right-clicks and the wheel run the manifest's commands with the widget's settings.

**Build:** `Bar/CommandWidget.cs`, a `BarWidget` owning a process, reading lines off the UI thread, drawing `text` (Nerd Font glyphs) in the theme's colours by `class`; `BarWidget.Create` falls back to the plugin registry for unknown names. A plugin process restarts with backoff if it dies, ends with the shell, and shows in `omawin profile ram`.

### 4.3 Panels as XAML (M, 🖥)

A panel (a drop-down from a widget, or a launcher page) is a loose `.xaml` file the shell loads with `XamlReader.Load`, bound to the JSON state the plugin's command prints. Buttons call the plugin's commands.

**Build:**
- **Theme resources:** the shell puts the theme into `Application.Resources` as brushes with fixed keys (`OmawinBackground`, `OmawinSurface`, `OmawinAccent`, `OmawinForeground`, `OmawinMuted`, … from `MenuModel.cs:14-33`) and updates them on a theme switch; plugin XAML uses `{ThemeResource OmawinAccent}`. Our own pages can move to them too.
- `menu-app/Plugins/PanelHost.cs`: loads, sizes and places a panel under its widget, with the motion tokens (1.4).
- **Prove it under AOT first:** WinUI's XAML parser is native, so `XamlReader.Load` should work trimmed; bindings need a reflection-free path. A sample panel decides; if it can't bind, the fallback is a declarative JSON format (rows, toggles, sliders, lists) drawn by the shell.

### 4.4 Menu entries (S)

A plugin's `menu.txt` fragment (same row format, `MenuModel.cs:107-142`) goes after omawin's rows, under an id prefix the plugin owns. While here, **layer `menu.txt` itself** like `items.txt` (shared folder, then account folder).

### 4.5 Where they live, and `omawin plugin` (M)

- `~/omawin-account/plugins/<id>/` (this account) and `C:\ProgramData\omawin\shared\plugins\<id>/` (every account); never the repo.
- `bin/commands.txt`: `plugin add <git-url>`, `plugin update [id]`, `plugin remove <id>`, `plugin enable|disable <id>`, `plugin list`. New plugins arrive **disabled**, as in Omarchy; `%LOCALAPPDATA%\launcher\plugins.txt` lists the enabled ones.
- Hot reload: a `FileSystemWatcher` per plugin folder (the pattern in `Background.cs` and `Bar.cs:347-356`), debounced.
- `bar.json` moves out of `zebar/` to the account folder (the repo's as the default), as Omarchy's `shell.json`, so plugins can be placed.

### 4.6 Our own pieces become plugins (M)

The music drop-down (Zebar's today, `zebar/tokyo-islands/music.js`), the agents panel, dictation and reports move to the format first: that proves it and keeps the core small. `omawin-music watch` already prints lines; it becomes the widget's command.

### 4.7 IPC for plugins and scripts (S)

`omawin shell <method> [args]`: `toggle <panel>`, `reload`, `list-plugins`, `put-bar-widget <id> <section>`, `notify` (5.5), like Omarchy's `omarchy-shell`. Fix the request format while here: one argument per line in the request file instead of space-joined (`App.xaml.cs:114-128`, `omawin.cs:189-204`).

### 4.8 Script commands (S)

From Raycast and PowerToys' Command Palette: a script dropped in `~/omawin-account/scripts/` (PowerShell, bash for Linux, anything with a header comment for its name and icon) shows up in the launcher, no manifest needed. The smallest possible plugin.

### 4.9 Omarchy plugins unchanged (L)

**Why:** Omarchy's marketplace has thousands of plugins. Porting each one by hand (4.1-4.8) means rewriting its UI; running them as they are gives omawin the whole catalog at once. It's possible because **Windows runs QML**: QML is part of Qt, which is cross-platform. What doesn't run on Windows is Quickshell, the Linux program that hosts Omarchy's QML and gives it Linux-only modules (`Quickshell` for edge-pinned windows, `Quickshell.Io` for processes and files, `Quickshell.Hyprland`, system services). A plugin is ordinary QML plus a few of those modules, so omawin provides the same module names on Windows and the plugin's QML loads unchanged. Four community plugins checked (2026-10-30) import only Qt Quick, Omarchy's `qs.Commons` / `qs.Ui`, `Quickshell` and `Quickshell.Io`, and two of them `Quickshell.Hyprland` ([plugins.md](plugins.md), "Running Omarchy plugins unchanged").

**Build, in order:**
1. **Stand-ins in Arch** (S): scripts named like Omarchy's commands, forwarding to Windows through `omawin.exe`: `omarchy-notification-send` (a toast), `omarchy-shell` (`omawin shell`), `xdg-open` (https and files only), `hyprctl` (the common reads answered from GlazeWM's IPC and the theme; binds become proposed keys, never bound silently). omawin's theme switch also writes `~/.config/omarchy/current/theme/` in Arch, in Omarchy's format. Omarchy's command bar modules then run unchanged (`side: linux`). Files: `wsl/omarchy-shims/`, `wsl/setup.sh`, `themes/set-theme.ps1`.
2. **`omawin plugin scan <git-url>`** (S): reads a plugin's QML imports and the commands it runs; says *runs as is*, *runs with stand-ins*, or *needs a port*. Run over the marketplace's catalog to rank which modules and stand-ins unlock the most plugins. Files: `bin/plugin-scan.ps1` (or Bun), `bin/commands.txt`.
3. **Prototype in Arch through WSLg** (M, 🖥): Quickshell from Omarchy's repositories plus a small `omawin-qs` shell (Omarchy's plugin host: `qs.Commons`, `qs.Ui`, the injected `shell` / `manifest` objects, its IPC) shows plugin panels and overlays as windows on the Windows desktop; GlazeWM floats and places them. Proves plugins run unchanged before investing in the native host. Limits: no layer-shell (panels as plain windows), weaker transparency, WSLg's latency and memory, the VM's view of battery and media.
4. **The native host: `omawin-qs.exe`** (L, 🖥): a Qt 6 program for Windows (MSVC build, released by CI like the menu app) that provides the module subset plugins use, implemented for Windows:
   - `Quickshell`: `PanelWindow` / `FloatingWindow` as frameless, always-on-top, positioned windows, anchored to the bar or the screen's edges, hidden from GlazeWM's tiling (window class rule), per monitor; `Singleton`; `Quickshell.env`; `IpcHandler` over omawin's shell IPC.
   - `Quickshell.Io`: `Process` runs Windows programs directly and anything else in Arch (`wsl.exe -d <distro> -- …`), with stdout / stderr parsers as Quickshell's; `FileView` with `$HOME` and `$XDG_*` paths mapped into Arch's home (`\\wsl.localhost\<distro>\…`) or to Windows folders, per the plugin's `side`.
   - `Quickshell.Hyprland`: workspaces, monitors, the focused window, `dispatch` and global shortcuts, answered by GlazeWM's IPC (and the fork's `emit`, 1.2).
   - Omarchy's `qs.Commons` and `qs.Ui` shipped as they are (MIT, credited), reading omawin's palette; Omarchy's plugin registry behaviour (enable, disable, hot reload) driven by `omawin plugin`.
   - **Bar widgets**: omawin's native bar reserves a slot for each QML widget; the host draws it in a small borderless window kept exactly over the slot (moved with the bar, per monitor), so it looks part of the bar.
   - System-service modules (`Quickshell.Services.*`: Mpris, UPower, Notifications, PipeWire) come later, by demand from the scan: Windows' media controls, battery, toasts, Core Audio.
5. **Upstream and credit**: offer the Windows module implementations to Quickshell if its maintainers want them; NOTICE entries for Qt (LGPL, shipped as DLLs) and Omarchy's QML.

**Files:** new `qs-host/` (Qt 6 / C++ and QML, CMake), `.github/workflows/release.yml` (a job building it), `bin/install-binaries.ps1` (downloaded only when a QML plugin is enabled), `menu-app/Bar/` (slots for QML widgets), `wsl/omarchy-shims/`.

**Done when:** Omadoro and Omastonk run unchanged on the native host, their widgets in omawin's bar and their panels opening under them in the omawin theme; Calendar for Omarchy runs with the stand-ins; `omawin plugin scan` over the catalog reports the share that runs as is.

**Risks:** keeping the module subset in step with Quickshell and Omarchy's host API (pin versions; the scan shows what plugins need); Qt's size (~50 MB on disk, ~80-120 MB of memory while a QML plugin is on, nothing otherwise); a second UI toolkit in the desktop: an exception to "WinUI for everything you see" (mission.md), only for plugins you add, never for omawin's own surfaces; Quickshell's licence (believed LGPL, to confirm before reusing its code); plugins that rely on Wayland-only behaviour still need a port.

### 4.10 A gallery (later)

A launcher page listing plugins from a curated `plugins.txt` in the repo (name, description, git URL), installable with Enter.

---

## Phase 5: Omarchy's developer experience

Borrowed as data where we can, the way the themes were.

- **5.1 Dev environments and Docker databases in Linux** (they exist on Windows): 3.4 and 3.6.
- **5.2 lazydocker on a key** (Omarchy's Super+Shift+D): a launcher item now; a key once confirmed.
- **5.3 Web apps and TUIs** (M): `omawin install webapp <name> <url> [icon]` makes a launcher item that opens Edge's `--app=<url>` with its own profile folder and the site's icon; `omawin install tui <name> <command>` likewise in the terminal; launch-or-focus through GlazeWM's IPC. Omarchy's web app list as the starting set.
- **5.4 The key cheat sheet from `glazewm/config.yaml`** (M): each binding gets a description comment in a fixed form (`# desc: Open the launcher`); the Shortcuts page (`Pages.cs`) reads them instead of `launcher/keys.txt`; Enter runs the binding's command. Omarchy builds its cheat sheet from bind descriptions the same way.
- **5.5 Notifications and the OSD in the shell** (M, 🖥): `AppNotificationManager` toasts for omawin's own events (install finished, update available, reminders, low battery) and `omawin notify <text>`; a volume / brightness OSD drawn by the shell (Windows' flyout as the fallback).
- **5.6 Idle lock and screensaver** (S): Omarchy's defaults (screensaver 150 s, lock 300 s): `omawin toggle idle-lock`, Windows' own lock (`LockWorkStation`) after idle (`GetLastInputInfo`), skipped while stay awake is on or media plays.
- **5.7 Window manager parity** (fork):
  - **Scratchpad** (M): a workspace that's never displayed (a workspace flag `special: true`; `Workspace::is_displayed()` is true only for the monitor's first in focus order, `models/workspace.rs:67`) and `toggle-scratchpad`, which shows its windows floating and centred over the current workspace. Omarchy's Super+S.
  - **Window groups / tabs** (L): `feat/accordian-v1` (accordion directions on `SplitContainer`, a hardcoded 30 px title offset, a z-order hack; 180 commits behind) is a reference, not a base: redo it on `omawin`, with the tab strip drawn by the shell from IPC.
  - Layout cycling, pop-out (float and centre), corner style and transparency toggles in the menu (GlazeWM's `window_effects` supports both).
  - Upstream's `feat/master-stack` and `feat/auto-tiling` (both WIP) are worth watching for Omarchy's master layout.
- **5.8 Detect before offering** (M): menu rows with a condition (`?wsl`, `?docker`, `?installed <id>`), answered in the background and cached; the menu opens on the last answers, as Omarchy's guards do (`docs/menu.md`).
- **5.9 Update channels and undo** (M): `omawin update` follows release tags (stable, edge) with a restore point first; `omawin uninstall` does what [install.md](install.md) lists by hand. Update › All apps (`launcher/menu.txt:257`, only `winget upgrade --all` today) also runs `scoop update *` and `choco upgrade all` when those are installed (Chocolatey's needs admin: one prompt, said up front).
- **5.10 Coding agents first class:** install, sign in, a key for the default agent, the theme followed (mission-todo track 7).
- **5.11 From Raycast and the Command Palette** (2026): a window-capture action in the snip tool (a window's image with its shadow); pinnable CPU / GPU / network readouts on the bar (as Command Palette's dock has); dictation in the launcher (omawin has dictation; a launcher row to start it).

---

## Phase 6: files (the finder, Win+F)

The finder on fff is already the file explorer you use: fuzzy search over your folders and repos, a preview, folder and image chips, Ctrl+Enter to show a file in its folder. It grows into the place you act on files from. Explorer stays (replacing it breaks Windows; [shell-apps.md](shell-apps.md)).

What exists: fff v0.11.0's C library (`menu-app/fetch-fff.ps1:10`), one index per root, started in the background (`FileSearch.cs:44-85`); roots are Desktop, Documents (and OneDrive's), Downloads, `~/repos`, `~/source/repos` and git repos in your home folder; `launcher/finder-skip.txt` skips folders; results ranked by own-name match, folders first, then fff's score, with opened files tracked (`fff_track_query`). Bound functions: `fff_create_instance_with`, `fff_search_mixed`, `fff_track_query` and result accessors; content indexing exists in the options (`EnableContentIndexing`) but is off, and no grep call is bound. The preview (`MainWindow.xaml.cs:743-844`) shows images, the first 22 lines of text, or a folder's first 18 entries. Keys: Tab cycles chips, Enter opens, Ctrl+Enter reveals, Esc clears or goes back.

- **6.1 Actions on a result** (M, 🖥): Ctrl+K (or →) opens an action list on the selected row: open in the editor (`omawin launch editor`), a terminal there (`omawin launch terminal --dir`, Linux or Windows by the file's side), open as a project (herdr), copy path, copy the path relative to its repo, copy the file itself (`DataPackage.SetStorageItems`: pastes into Explorer, Teams, mail), rename inline, send to the Recycle Bin, properties. The common ones get keys of their own (proposed: Ctrl+E editor, Ctrl+T terminal, Ctrl+C copy path), confirmed before binding.
- **6.2 Browse, not just search** (M, 🖥): → on a folder lists its contents (folders first), ← goes up, the preview beside it, the query filtering the current folder; yazi's model, for when you know roughly where something is.
- **6.3 Search inside files, as a finder mode** (M): part of the finder, not a separate tool: a **Contents** chip next to All / Folders / Images (Tab reaches it), or a `#` prefix, switches the same window from names to contents, with the same roots, chips, preview and actions. Results are by file and line, the preview shows the matching lines highlighted, and Enter opens the editor at that line (`nvim +<line>`). fff already has the start of it: its options carry `EnableContentIndexing` (off today, `FileSearch.cs:191, 210-224`). First check fff v0.11's C API for a grep call (its upstream `fff.h`; fff.nvim has live grep); if there is one, turn indexing on and bind it; otherwise `rg --json` (ripgrep is in the core) in the chosen root, streamed into the list. Indexing contents costs memory and disk, so it's on only for the roots you pick (code repos by default, not Downloads).
- **6.4 Better previews** (M): code in the theme's colours (tree-sitter's `highlight`, or a small tokenizer per common language), Markdown drawn, a PDF's first page (`Windows.Data.Pdf`), CSV as a table, Office files and videos by Windows' own thumbnails (`IShellItemImageFactory`); all off the UI thread (the `previewToken` pattern exists).
- **6.5 Filters and places** (S): `.pdf`, `today`, `this week`, `>10mb` as filter words; your own places in `launcher/finder-places.txt` (layered: shared, account); Linux's files (3.7); pinned favourites at the top of the empty list.
- **6.6 Many at once and drag out** (M): Space marks rows and actions apply to all; dragging a row starts a real file drag into any app.
- **6.7 Into file dialogs** (M, 🖥): when an app's Open or Save dialog is in front, choosing a folder sends the dialog there (UI Automation types the path into the dialog's file name box), Listary's best trick.
- **6.8 Explorer's developer defaults** (S): extensions and hidden files shown, opens to your home folder, full path in the title, compact view, no Gallery / Home recommendations, dark or light by the theme; tweaks with undo, in the `look` step.

---

## Phase 7: sandboxes

"Sandbox" means three useful things for omawin: a clean Windows to test the install in, a throwaway Windows to try something risky in, and a fenced-in place for coding agents to work. What's out there (web survey, 2026-10-30):

- **Windows Sandbox** (built in): a disposable clean Windows in seconds, gone when closed. Windows 11 **Pro, Enterprise and Education only, not Home**; turned on as the optional feature `Containers-DisposableClientVM` (admin and a restart). Configured by a `.wsb` file (mapped folders, read-only or not; a logon command; networking; vGPU; memory). Since 24H2 it updates through the Store, folders can be shared while it runs, and a **`wsb` command line** scripts it (`start`, `list`, `exec`, `share`, `stop`, `connect`, `ip`). Limits: **no winget or Store inside** by default (it has to be bootstrapped, e.g. with the `Microsoft.WinGet.Client` module); on 24H2 the inbox Store apps (Terminal, Notepad) are missing; no nested virtualization, so **no WSL inside**. Whether `wsb exec` returns a command's output is unclear in the docs, so results go through a shared folder. ([overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview), [CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli))
- **Install testers built on it:** winget-pkgs' [`Tools/SandboxTest.ps1`](https://github.com/microsoft/winget-pkgs/blob/master/Tools/SandboxTest.ps1) (maintained: writes a `.wsb`, bootstraps winget, installs, runs a script), [bonben365/windows-sandbox](https://github.com/bonben365/windows-sandbox) (one-line winget in Sandbox), and generator gists. That's exactly the pattern omawin needs.
- **Agent sandboxes:** Claude Code's sandbox runs on macOS, Linux and **WSL2**; on native Windows it runs commands unsandboxed ([docs](https://code.claude.com/docs/en/sandboxing)). OpenAI's Codex built its own native Windows sandbox from restricted tokens (May 2026; it rejected Windows Sandbox because Home lacks it). Microsoft's "agent workspace" (a separate Windows session for agents) is in preview behind Settings › Experimental agentic features. For omawin, **Arch in WSL is the practical agent sandbox today**, since it's already there.
- **Try Omarchy for Windows** ([omacom/try-omarchy-windows](https://github.com/omacom/try-omarchy-windows), MIT): the real Omarchy in a QEMU VM on Windows' hypervisor, still a preview. Not a competitor: a way to run real Hyprland when you want it.

### 7.1 `omawin sandbox test`: the install on a clean Windows (M, 🖥)

**Why:** omawin has never been installed on a clean PC (mission.md); Windows Sandbox makes that a few-minute command on any Pro PC, and the honest counterpart to CI's preloaded runners (2.1).

**Build:** `bin/sandbox-test.ps1` (a row in `bin/commands.txt`):
- Writes a `.wsb`: the repo mapped **read-only** (or a fresh clone of a branch, to test what's pushed), a writable `out` folder, networking on, vGPU off, memory 8 GB.
- The logon command bootstraps winget the way `SandboxTest.ps1` does, then runs `bootstrap.ps1` (or `install.ps1` from the mapped repo) with the `wsl` preset off, `OMAWIN_PLAIN=1`, and writes `install.log`, `install-times.txt`, `omawin check`'s output and a screenshot of the desktop to `out`.
- Starts it with `wsb start --config` on 24H2 or later, `WindowsSandbox.exe <file>.wsb` before; waits for a done marker in `out`; prints the summary and the times.
- Turning the feature on: an option in the one admin prompt (`install/admin.ps1`), offered only on Pro and up; on Home, the command explains that Windows Sandbox isn't available there and points at CI.

**Done when:** `omawin sandbox test` on the PC ends with a clean `omawin check` and a screenshot of omawin's desktop inside the sandbox.

**Risks:** winget's bootstrap changing (follow `SandboxTest.ps1`); the missing Store apps on 24H2; GlazeWM and the bar in a sandbox's session (screenshots will show if they draw).

### 7.2 A throwaway Windows from the menu (S)

System menu › Tools › Sandbox: opens Windows Sandbox with Downloads mapped read-only, for running an installer or a file you don't trust. `omawin sandbox` (no arguments). Pro and up; hidden on Home (5.8's conditions).

### 7.3 Coding agents fenced in (M)

`omawin agent sandbox [claude|codex|opencode]`: opens the agent inside Linux (Arch) in the current project, with its sandbox on (Claude Code's sandboxing works in WSL2; `failIfUnavailable` so it never silently runs open), the project folder the only writable place. On native Windows, Codex's own sandbox where the user picks Codex. A launcher row and a herdr tab option (`launcher/project-tabs.txt`).

### 7.4 Sandbox screenshots as acceptance tests (later)

Omarchy runs graphical acceptance tests in a disposable VM (`agents/skills/acceptance-tests.md`). Ours: 7.1's sandbox run, plus scripted checks inside (the menu app's `--snip-selftest` and `--qs-selftest`, the bar's preview mode), screenshots compared by eye per release.

### 7.5 Real Omarchy, when you want it (S)

A launcher item and a short manual page: "Try real Omarchy" installs Try Omarchy for Windows (its signed launcher) for someone who wants Hyprland itself. Omawin and Omarchy side by side, credited.

---

## Always: following Omarchy

`mjveilleux/omarchy` tracks upstream. At each Omarchy release, a short note in mission-todo.md of what's worth bringing over. Data we can import is imported by a script, not copied by hand:

| From Omarchy | Into omawin | How |
|---|---|---|
| themes (`themes/*/colors.toml`) | `themes/colors/` | `themes/import-omarchy.ps1` (exists) |
| terminal packages (`install/omarchy-base.packages`) | Arch in WSL | `wsl/setup.sh` minus `wsl/omarchy-skip.txt` (3.2) |
| package repositories (stable channel) | Arch in WSL | `wsl/setup.sh` (exists) |
| bash defaults (`default/bash/`) | Linux shells | sourced (3.2) |
| motion values (`looknfeel.lua`, `motion.md`) | `menu-app/Motion.cs`, `glazewm/config.yaml` | by hand; rarely changes |
| menu sections (`omarchy-menu.jsonc`) | `launcher/menu.txt` | compared per release |
| dev envs and databases | `bin/install-dev-env.ps1` | compared per release |
| plugin manifests | `docs/plugins.md` | the schema followed (4.1) |
| web apps | 5.3's starting list | imported |

## Testing without a PC in the loop

This plan leans on what CI can check and saves the PC for what only a person can judge.

| Check | Where | Covers |
|---|---|---|
| Release builds, AOT kind checked | `release.yml` | 1.3, every menu app change |
| Install time and `omawin check` | new `install.yml`, Windows runner | phase 2 |
| Window animation frame check | the fork's `animation-check.yaml`, restored | 1.5 |
| `setup.sh` in Arch and Ubuntu containers | new `linux.yml` | phase 3 |
| Menu app self-tests (`--snip-selftest`, `--qs-selftest`) | `release.yml` (add) | snip and Quick settings |
| A clean Windows install | `omawin sandbox test`, the PC (Pro) | the honest fresh install (7.1) |
| Keys, feel, visuals | the PC, by hand, with a screenshot | 🖥 items |

## Risks

| Risk | Effect | Mitigation |
|---|---|---|
| The fork drifts from upstream GlazeWM | harder merges, missed fixes | upstream the general parts (1.5); keep omawin's changes in few files |
| Window animations judder on some GPUs | it feels worse, not better | `omawin toggle animations`, off in VMs, the frame check in CI |
| mise or GitHub blocked on a work network | tools missing | winget fallback per tool, said in the summary |
| Virtualization off, or WSL unavailable | no Linux | detect first; PowerShell as the shell then, with a clear message |
| `XamlReader.Load` can't bind under AOT | XAML panels don't work as planned | a sample first; the declarative fallback |
| Omarchy changes its package list or repos | Arch breaks on update | Omarchy's list pinned to a commit, bumped per Omarchy release after the container test |
| Windows Sandbox missing (Home) | no local clean test | CI's install job; say so plainly |
| Too much in one process | a crash takes the whole shell | plugins out of process (commands), crash logging, the shell restarts itself |

## How we'll know

- `omawin profile menu`: under 50 ms from key to pixels; under 16 ms per keystroke.
- The CI install: desktop up in under 3 minutes, everything in under 10, `omawin check` without failures; `omawin sandbox test` clean on the PC.
- Window animations pass the frame check, and a week of daily use finds no stuck overlay.
- A fresh install, Enter through every question: Arch ready with Omarchy's terminal packages, Win+Enter opens it as fast as it opened PowerShell, and a Linux terminal looks different from a PowerShell one at a glance.
- One plugin of ours and one written from an Omarchy plugin's idea, installed with `omawin plugin add`.
- Every row in mission-todo.md's Omarchy parity list is have, Windows-native instead, or decided against.
