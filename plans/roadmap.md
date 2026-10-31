# Roadmap: Omarchy's developer experience on Windows

The big plan: where omawin stands on 2026-10-30, where it's going, and for each piece of work what to build, which files it touches, how we'll know it's done, and what could go wrong. The goal is to come close to Omarchy 4 in how omawin installs and how it feels to use: a menu that answers instantly, windows that move smoothly, an install that's done before your coffee is, Linux as a first-class home, and a shell you can extend with plugins.

**The how, with code for every piece: [plan/](README.md)** (start there if you have no context). [mission.md](../docs/mission.md) says why omawin exists; [mission-todo.md](../docs/mission-todo.md) is the running checklist by track. This file is the plan behind it. When an item lands, tick it in mission-todo.md with the commit; this file changes when the plan changes.

Sources read for this plan: this repo; Omarchy 4 (`mjveilleux/omarchy`: `shell/`, `docs/omarchy-shell.md`, `docs/menu.md`, `docs/update-process.md`, `agents/skills/motion.md`, `default/hypr/looknfeel.lua`, `install/omarchy-base.packages`); our GlazeWM fork (`mjveilleux/glazewm`, branches `omawin`, `feat/accordian-v1`, `feat/master-stack`, `feat/auto-tiling`); a web survey of Windows sandboxes and similar projects (2026-10-30, links in phase 7). File and line references are as of 2026-10-30 and will drift; they say where to start, not what to trust blindly.

## How to read this

- **Phases** run roughly in order, but most items inside a phase are independent: one agent or one sitting each.
- Every item says **why** (what you feel), **what to build**, **which files**, **done when**, and **risks**. Size: **S** (an afternoon), **M** (a few days), **L** (a week or more).
- **🖥 = try it on the PC.** This plan was written in a cloud session that can't run Windows. Items marked 🖥 need a try on a real Windows PC before they merge to main (AGENTS.md: ask before driving the desktop; screenshot visible changes). Items CI can check say so.
- Keys never change by guesswork (AGENTS.md). Nothing here rebinds a key; where an item proposes a binding, it's confirmed first.

## Decisions made

| Date | Decision |
|---|---|
| 2026-10-15 | One always-running native shell (the menu app) draws everything (the bar included); Explorer stays ([shell-apps.md](../docs/shell-apps.md)) |
| 2026-10-30 | Plugins keep **Omarchy's `manifest.json`**; panels are **loose XAML** loaded by the shell; widgets and services are **commands that print JSON lines** |
| 2026-10-30 | omawin's **command-line tools come through mise**, pinned in the repo; desktop apps stay on winget |
| 2026-10-30 | **winget, Scoop and Chocolatey all stay** as your choice on the Packages page; omawin's own installs still avoid Chocolatey |
| 2026-10-30 | **Linux is first class; Arch is the default Linux** (Ubuntu alongside). Linux is the default terminal |
| 2026-10-30 | Linux follows **all of Omarchy's package sources and decisions**: Omarchy's repositories (stable channel), Omarchy's package choices for the terminal, **no AUR** |
| 2026-10-30 | Files means **the Win+F finder** (fff): it grows into a keyboard file manager; Explorer gets developer defaults only |
| 2026-10-30 | **Terminals show which side they're on**: Linux and PowerShell look different at a glance |
| 2026-10-30 | **`omawin pkg` is one package command over winget, Scoop, Chocolatey, mise, the bundle and Arch's pacman**: omawin picks the manager, remembers it, and updates each through its own; you can always choose or move |
| 2026-10-30 | **No telemetry**: omawin never sends anything on its own; `omawin report` makes a redacted report the user reads and chooses to post (10.7) |
| 2026-10-30 | **Compiled C# first, then Rust, then Bun** for every tool and script; PowerShell only for the bootstrap entry and the profile; bash only inside Linux; no Python (2.11, AGENTS.md) |
| 2026-10-30 | **Uninstall anything you choose** (Install › Remove for everything, `omawin remove <part>`, `omawin uninstall`); omawin uninstalls what it no longer needs in a migration, e.g. the old web bar (2.10) |
| 2026-10-30 | **No admin by default**: install, update and everyday use never ask for admin; a feature that truly needs it is opt-in, asks once for everything at the moment you choose it, says exactly what it will do, and never asks during an update (2.9) |
| 2026-10-30 | **Win+Space for everything; Win+Alt+Space retired** with One Search (one release as an alias, then removed by a migration) |
| 2026-10-30 | **One Search**: one bar for everything, results tagged and filterable, a bento home for the empty state, a list for results ([one-search.md](one-search.md)); proposed, open questions with the maintainer |
| 2026-10-30 | **Never open Settings again**: every Windows setting is searchable from Win+Space (One Search's Settings filter), and the common ones switch on and off right there |
| 2026-10-30 | **The music player is a Quickshell plugin** (QML in Omarchy's format), chosen in the plugin selector, not a preset; the first plugin the QML host runs (4.6) |
| 2026-10-30 | **Omarchy plugins run unchanged** on omawin: stand-ins for Omarchy's commands in Arch, and a Qt host on Windows that runs their QML (Windows runs QML; only Quickshell's Linux modules are missing, so omawin provides them) |

## Where we are

**Done and solid:** the one-line bootstrap; one admin prompt; presets on a lean core; releases built in CI, so a new PC downloads binaries instead of compiling; the install screen; migrations; `omawin check`; the menu app as one always-running native process (launcher, system menu, file finder, clipboard, Quick settings, screenshots with OCR, colour picker, QR, calculator, the packages page); 24 Omarchy themes applied live across every app; noctty (our Ghostty fork) and herdr; Linux in WSL with Arch as the default, on Omarchy's package repositories, Ubuntu as the other choice; dev environments through mise and Omarchy's Docker databases (Install › Development, [dev-envs.md](../docs/dev-envs.md)); Native AOT for the menu app (first start 0.34 s, 114 MB); the native bar, now the default.

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
| **"snappy"** | Phase 1: menu profiled, keys straight to the shell, motion tokens, window animations shipped, the native bar only | everything answers instantly and moves smoothly |
| **"one search"** | Phase 1C: one bar for everything, tagged results and filters, the bento home, the futuristic minimal look ([one-search.md](one-search.md), [mockup](https://claude.ai/artifact/FCAA1kuXE7H6BjjfVzrN8z)) | you type one thing and find anything |
| **"settings"** | Phase 1B: every Windows setting in the system menu, switches inline, `omawin setting`, your settings applied on a new PC | you never open Settings again |
| **"fast"** | Phase 2, 2.9 (no admin) and 7.1: the omawin bundle, CI install timing, desktop first, the welcome page, background steps, the Sandbox install test | a fresh PC is an omawin desktop in under a minute |
| **"linux"** | Phase 3: Linux by default, Omarchy's terminal layer in Arch, terminals coloured by side | Win+Enter is Arch, and you always know where you are |
| **"plugins"** | Phase 4: plugin format, `omawin plugin`, our panels as plugins, Omarchy plugins running unchanged | you add to the bar and menu without touching omawin, from omawin's gallery or Omarchy's |
| **"parity"** | Phases 5 to 7: Omarchy's DX, the finder as file manager, sandboxes | Omarchy's habits work on Windows |
| **"omarchy"** | Phase 9: the rest of Omarchy (drop-down console, hooks, setup git, crash watch, the theme in more apps, debug bundles, pick menus, agent accounts, config reset, display modes, recording, fonts) | nothing an Omarchy user reaches for is missing |
| **"fixed"** | Phase 8: Windows' annoyances replaced, on Windows' own APIs | the parts of Windows you used to fight are calm |

---

## Phase 1: snappy

Every key answers in one frame, and everything moves the way Omarchy moves.

### 1.1 `omawin profile menu` (S, 🖥)

**Why:** "snappy" has to be a number before we change anything, as `omawin profile terminal` did for the terminal.

**Build:**
- Trace marks where they're missing. `omawin.exe` has `Trace()` (`bin/omawin.cs:72-80`) but none in `Send` (`:189-204`); the menu app has `Program.Trace()` (`shell/menu-app/App.xaml.cs:52-56`) but none in `HandleRequest` (`:194-229`) or `Show` (`MainWindow.xaml.cs:196-247`). Add `omawin send`, `menu request`, `menu shown` and `menu painted` (on the first `CompositionTarget.Rendering` after `appWindow.Show()`).
- `bin/profile-menu.ps1`, modelled on `bin/profile-terminal.ps1` (`:32-105`: the trace-on file, `Mark`, lines of `label\tticks`): opens each surface N times, prints a timeline per run and the median, for the launcher, the system menu and the finder. Its label map gains `menu`.
- A row in `bin/commands.txt`: `profile menu | …`.
- Typing too: from a key in the search box to the list redrawn, so regressions in `Fuzzy.cs`, `Launcher.cs` and `FileSearch.cs` show.

**Done when:** `omawin profile menu` prints key → painted for each surface, and the baseline is written down here.

**Risks:** GlazeWM ignores simulated keys (AGENTS.md), so the driver calls `omawin.exe launcher` directly and measures from there, plus one hand-pressed run for the keyboard hook's share.

### 1.2 Keys straight to the shell (M, 🖥; fork + menu app)

**Why:** each menu key now starts a process (`shell-exec --hide-window omawin.exe launcher`, `config/glazewm/config.yaml:300`), writes `%LOCALAPPDATA%\launcher\system-menu-request` and sets `Local\dotfiles-system-menu-signal`. Omarchy's Super+Space starts nothing: the shell registered a global shortcut. We can do the same, because the menu app already holds a websocket to GlazeWM (`Bar/GlazeWm.cs:29-58`, `Tiling.cs:28-45`).

**Build (fork):** a new command `emit <name> [args…]` that broadcasts a custom event to IPC subscribers:
- `InvokeCommand::Emit { name: String, args: Vec<String> }` in `packages/wm-common/src/app_command.rs` (the clap enum at ~:157; `ShellExec` at :208 is the model).
- `WmEvent::Custom { name, args }` in `packages/wm-common/src/wm_event.rs`, and `SubscribableEvent::Custom` (`app_command.rs:137`).
- An arm in `IpcServer::process_event`'s exhaustive match (`packages/wm/src/ipc_server.rs:357-404`) and in `WindowManager::run_command` (`packages/wm/src/wm.rs`) calling `state.emit_event`.
- `emit_event` drops events before initialization and while paused (`wm_state.rs:558-570`); a menu key while paused should still work, so `Custom` gets the exemption `PauseChanged` has.
- A unit test for parsing; doc comments on every new function (the fork's guidelines).

**Build (menu app):** `Bar/GlazeWm.cs` subscribes to `custom` too (its `sub --events …`, :39) and hands `name == "omawin"` events to `App.HandleRequest(args)` on the UI thread, exactly as the request file does today.

**Build (config):** the menu bindings become `emit omawin --launcher` and so on, for Win+Space, Win+Shift+V, Win+A and Win+F (Win+Alt+Space is retired with One Search, 1C). If no subscriber is connected (the menu app isn't running) nothing happens; GlazeWM starts the menu app (`omawin start`) and `omawin check` warns when it isn't subscribed. Decide during the build whether to add an `emit --or-exec <command>` fallback.

**Done when:** `omawin profile menu` shows the process start and file write gone; the menu still opens right after the menu app restarts.

**Risks:** the official GlazeWM (no fork) doesn't know `emit`. `config/glazewm/config.yaml` is already a template (`themes/render.ps1`), so the renderer writes `emit` for the fork and `shell-exec … omawin.exe` otherwise.

### 1.3 The Native AOT build for everyone (S)

**Why:** AOT starts in 0.34 s instead of 1.04 s and uses 114 MB instead of 173 MB ([native-bar.md](../docs/native-bar.md)).

**Build:** the release job (`release.yml:87-105`) calls `menu-app\build.ps1 -BuildOnly` without `-Aot`; `build.ps1:39-44` picks AOT only if it finds Visual Studio's C++ tools. Pass `-Aot` in CI so a runner image change can't silently ship the JIT build, and fail the job unless `build-kind.txt` says `native`.

**Done when:** every release's `SystemMenu-*.zip` has `build-kind.txt` = `native`, checked by CI.

### 1.4 Motion from one place (M, 🖥)

**Why:** Omarchy's motion rules make it feel calm and quick: entrances within 250 ms, exits about ⅔ of that, ease-out-quint (`cubic-bezier(0.23, 1, 0.32, 1)`), never linear or ease-in (`agents/skills/motion.md`, `default/hypr/looknfeel.lua:69-93`). Ours are scattered: the menu's 140 ms fade and 180 ms rise (`MainWindow.xaml.cs:291-305`), staggered rows (`:350-376`), the bar's 280 ms widget fade (`Bar/Bar.cs:317-345`), the drawer's 200 ms (`Bar/Widgets.cs:310-315`), and no exit animation (`HideMenu` is `appWindow.Hide()`, `:249`).

**Build:**
- `shell/menu-app/Motion.cs`: the tokens (`Enter = 200 ms`, `Exit = 130 ms`, `Stagger = 18 ms`, `EaseOut` = ease-out-quint as a `CubicBezierEasingFunction`), a global `Enabled` read from `%LOCALAPPDATA%\launcher\animations` (`on` / `off`), and helpers (`FadeIn`, `FadeOut(then)`, `Rise(px)`).
- The menu's `Animate`, `Cascade` and `Slide`, the bar's fades and Quick settings use them. The bar's storyboards move to Composition (off the UI thread).
- **An exit animation:** `Dismiss()` fades the root out (130 ms) and hides when done; a key during the exit cancels it and re-shows, so nothing ever waits on an animation.
- `omawin toggle animations` (`bin/commands.txt`): writes the file the shell watches and sets GlazeWM's `animations.enabled` (1.5). Off by default in a VM (`Win32_ComputerSystem.Model`), as Omarchy's `vm-no-animations.sh` does.

**Done when:** every surface uses `Motion.cs`; the menu fades out; animations off makes every duration 0.

### 1.5 Ship window animations (L, 🖥; fork)

**Why:** the single most visible difference from Omarchy.

**What exists** (fork branch `omawin`): `packages/wm/src/animation_manager.rs` (906 lines) with three plans: `Slide` (a snapshot slides while the real window waits invisible at its target), `FadeIn` (the real window placed invisibly, then faded in) and `Live` (the real window moved each frame, Hyprland-style; judders on Windows because apps can't repaint while resized). Snapshots: `wm-platform/src/platform_impl/windows/animation_window.rs` (871 lines). Config in `wm-common/src/parsed_config.rs:393-437`: `animations.window_open` and `window_move` (`effect_type`, `duration_ms`, `easing`, `trigger_threshold`). omawin's `config/glazewm/config.yaml:55-65` asks for slide 200 ms ease-out-quint and fade 200 ms ease-out-cubic.

**Build:**
1. **Bring the frame check back to CI.** `scripts/animation-check.ps1` (opens Notepads, closes one, moves one; records frames; fails on a leftover overlay or a window not at full opacity) ran in `.github/workflows/animation-check.yaml` until commit `445d61e0` removed it. Restore it (`git revert 445d61e0`) so every fork change is checked on a Windows runner.
2. **A daily-use week on the PC** (🖥). Known rough edges in the code: flicker during sync (`platform_sync.rs:207`), the fullscreen check (`:452`), `trigger_threshold` accepting non-pixel values (`animation_manager.rs:523`), the 100 ms settle wait for new windows.
3. **A close animation.** No hook exists: `handle_window_destroyed.rs` → `unmanage_window` (`:28`) runs after the window is gone. Do it where we still have the window: GlazeWM's `close` command takes a snapshot, then closes; the snapshot fades out (Omarchy's windowsOut: 160 ms, pop-in to 95%). Windows closed by the app itself just vanish, as today. New config `animations.window_close`.
4. **An enabled switch:** today animations are off only when the section is absent; `omawin toggle animations` needs `animations.enabled: false`, keeping the settings while off. Config reload already re-applies (`reload_config.rs:40-43`).
5. **Release** the fork (`scripts/omawin-release.ps1`) and point `install/glazewm-fork.ps1` at it.
6. **Upstream** what's general to glzr-io/glazewm (the snapshot slide, easing functions), so the fork stays small.

**Done when:** the frame check passes in CI at 200 ms and 600 ms; a week of daily use without a stuck overlay; Omarchy's feel by eye.

**Risks:** GPU drivers; Graphics Capture's yellow border on older Windows builds (a fix commit exists); fullscreen apps and games (animations skipped for fullscreen, as now); a DPI change mid-animation. Workspace switches stay instant, as Omarchy keeps them.

### 1.6 The native bar only (M, 🖥)

**Done 2026-10-31 (Zebar removed, ahead of its last two panels):** the maintainer chose to remove Zebar now and rebuild the music and agents drop-downs later as bar plugins (4.6). Zebar is out of the core packages and the admin step (no more Zebar fork), `zebar/` is gone (the native bar's layout moved to `default/bar/bar.json`; `voice-meter`, the one helper the native bar uses, to `bin/`), `omawin start bar` holds the bar's place at sign-in (the colour from `%LOCALAPPDATA%\launcher\bar-background.txt`), and `migrations/2026-10-31-zebar-out.ps1` stops and uninstalls it (this account's copy at once; one installed for the whole PC is named for Install › Remove, since an update never asks for admin), removes the old link, generated files and helpers, and clears `ZEBAR_ASSET_PORT`. Win+M stays bound and says the panel is coming back. Also fixed on the way: `voice-meter` quit every 3 s when Zebar wasn't running, so the native bar kept restarting it.

The bar is the menu app's own ([native-bar.md](../docs/native-bar.md)); what's left: its media widget, and the music and agents drop-downs moved into the shell (they become the first plugins, 4.6). Then the old web bar is **uninstalled**: a migration stops it, removes what omawin set up for it, and uninstalls the program omawin installed for it (per user: at once; if an older install put it machine-wide, the uninstall waits in the pending admin list for you to say when, 2.9, never a prompt during the update). Saves ~220-350 MB and the 2 s sign-in paint.

### 1.7 Typing never waits (S, 🖥)

The launcher builds up to 150 rows on each keystroke (`MainWindow.xaml.cs` ~:574). Virtualize the list (an `ItemsRepeater` with recycling) so only visible rows are built, and keep icon loading off the UI thread. Measured by 1.1's typing timeline: under 16 ms per key.

### 1.8 Crisp icons everywhere (M, 🖥)

**Why:** app and file icons in the launcher, its tiles, the finder and the bar's tray look soft. The cause is in the code: every icon is fetched at one fixed size and stretched to whatever it's drawn at.
- `shell/menu-app/AppIcons.cs` asks Windows for **48 × 48 pixels** for every app and file icon (`const int Size = 48`, through `IShellItemImageFactory::GetImage` with flags 0, *resize to fit*), whatever the monitor's scale. A list row draws it at 26 units (`MainWindow.xaml.cs:977`): 39 pixels at 150%, so roughly fine; a **tile** draws it at the tile's size (`MainWindow.Tiles.cs:303`, `MainWindow.xaml.cs:1049`), several times larger at 150-200%, so a 48-pixel image is **stretched up**: soft. And *resize to fit* lets Windows hand back a smaller icon already scaled when the app has no 48-pixel image.
- `shell/menu-app/Bar/Tray.cs` draws each tray icon into a **32 × 32** picture (`DrawIconEx`) from the icon the app gave Windows, which is often its 16-pixel small icon: stretched up too.
- Theme pictures are fine (`ThemeThumbs.cs` decodes at 320 pixels wide, with good scaling); Nerd Font glyphs are vector and always sharp.

**Build:**
1. **Ask for the pixels actually drawn.** Each request carries its size in units and the window's scale (`XamlRoot.RasterizationScale`, per monitor); `AppIcons` asks for the next standard icon size at or above that (16, 20, 24, 32, 40, 48, 64, 96, 128, 256), so Windows returns an image the app actually ships at that size instead of a resized one. The cache key includes the size; a monitor change (the bar already watches) re-requests at the new scale.
2. **Never scale up; scale down well.** Flags `SIIGBF_ICONONLY` (an app's icon, not a thumbnail) and `SIIGBF_BIGGERSIZEOK` (a larger image is fine): when the result is larger than needed, `AppIcons` scales it down with high-quality filtering (Fant, as `ThemeThumbs` does) to the exact pixel size; when only a smaller image exists, it's drawn at its own size, centred, rather than blurred up.
3. **Show it 1:1.** The `Image` is sized so one image pixel lands on one screen pixel (its size in units = pixels ÷ scale), so the display doesn't filter it again.
4. **Store apps at their best.** Packaged apps ship logos per size and per light / dark (`targetsize-N`, `altform-unplated`); ask the package for its logo at the needed size (`AppListEntry.DisplayInfo.GetLogo`) and prefer the unplated one, so Store apps don't arrive on a coloured plate or at the wrong size.
5. **Tray icons from the best source.** Draw each tray icon at the bar's real pixel size (the bar is 26 units tall, so 20 units at 150% is 30 pixels): ask Windows for the icon at that size when the app registered one by resource (`LoadIconWithScaleDown` on the icon's module and id where available); otherwise draw the app's own icon at its native size, centred, not stretched. Apps that draw their state into the tray icon (unread counts, battery) keep their own pictures.
6. **Files and thumbnails** in the finder: the same rule (thumbnails at the preview's pixel size, `big:` previews at the panel's real size rather than a fixed 256).
7. **Future icons arrive large**: web apps (5.3) take the site's manifest icons (192 and 512 pixels) or `apple-touch-icon`, never the 16-pixel favicon; plugins (phase 4) declare an icon of at least 64 pixels or a Nerd Font glyph.

**Check:** `SystemMenu --icon-selftest` lists every icon on screen with its requested, received and drawn pixel sizes, and flags any that were scaled up; screenshots of the launcher, tiles, finder and tray at 100%, 150% and 200% scale, side by side before and after (🖥).

**Done when:** no icon is drawn larger than the image it came from, at any scale; the tiles and tray look as crisp as Windows' own Start menu and taskbar at 150% and 200%.

**Built 2026-10-31 (steps 1-3 and 5; not yet seen on screen, 🖥):** `AppIcons.Get(id, units, scale, …)` asks Windows for the next standard size at or above the drawn pixels, with `SIIGBF_BIGGERSIZEOK` (and `SIIGBF_ICONONLY` for apps), shrinks a bigger result to the exact pixels by area averaging (`IconScale.cs`, pure, with tests in `test/menu-app.Tests/Look/IconScaleTests.cs`), and calls back with the size to draw it at, so the launcher's rows and tiles show it 1:1; the cache is per size. Tray icons are drawn at their own size (up to 32, from `GetIconInfo`), never stretched, then shrunk to the bar's pixels the same way. Also built: the finder's rows go through the same path, and its previews are fetched at the pane's real pixels (6); `SystemMenu --icon-selftest` prints, for the first 25 apps at 100%, 150% and 200%, the pixels each icon is drawn at, the size asked, what came back and what's drawn, and counts any that came back smaller (drawn at their own size, never stretched).

**Built 2026-10-31 (step 4; not yet seen on screen, 🖥):** a Store app (its id an AUMID, `<family>!<app>`) gets its package's own logo file: `StoreLogos.cs` finds the package for the current user (`PackageManager.FindPackagesForUser`, no admin), reads its `AppxManifest.xml` for the app's `Square44x44Logo`, lists the files beside it (and in qualifier folders such as `scale-200\`), and decodes the chosen one with Windows' decoder, then it's fitted like the shell's (`IconScale`). The choice is pure (`PackageLogo.cs`, tests in `test/menu-app.Tests/Look/PackageLogoTests.cs`): the unplated logo for the theme's background (`altform-lightunplated` on a light theme, then `altform-unplated`), `targetsize-N` before `scale-S`, the smallest at or above the drawn pixels, else the largest; high-contrast variants, the other theme's and other altforms are never chosen. The plain (plated) files are used only when the app's plate is transparent (`BackgroundColor="transparent"`): otherwise they're often a white glyph meant for the plate, and the shell, which draws the plate, is better. Anything else (a desktop program, no package, no suitable file, a read that fails) falls back to the shell's icon. Light or dark follows the menu's theme (`Mode`), and the cache keeps them apart. `SystemMenu --icon-selftest` now says where each icon came from (`package <file>` or `shell, asked N`), and adds every Store app after the first 25.

### 1.9 Theme pictures ready before you look (S-M, 🖥)

**Why:** the theme picker's tiles fill in slowly the first time it opens. From the code (`shell/menu-app/ThemeThumbs.cs`): a theme's thumbnail is made **the first time the picker shows that theme**; to make it, the menu app **downloads that theme's full-size first background** (from the theme's URL, often several MB), two at a time, then scales it to 320 pixels wide and saves a PNG in `%LOCALAPPDATA%\launcher\thumbnails`. So the first open of the picker downloads ~24 full wallpapers just to show small pictures. The full backgrounds themselves are fetched only when you switch to a theme (`themes/set-theme.ps1:132`), so a first switch waits on a download too.

**Build:**
1. **Thumbnails built in CI, shipped with omawin.** The release workflow downloads each theme's backgrounds, makes the thumbnails (320 and 640 pixels wide, for 100-250% scale; WebP or PNG, ~20-60 KB each, measured), and ships them in the bundle's stage 1 (`themes/thumbs/<theme>--<picture>.webp`; [install-speed.md](install-speed.md)). Not committed to the repo (they're derived, and they change when Omarchy's themes do); `themes/import-omarchy.ps1` notes new themes so CI makes theirs. `ThemeThumbs` reads the shipped file first, makes one itself only for a theme added locally.
2. **Preloaded in memory.** When the menu app is idle after start-up (low priority, off the UI thread), it decodes every theme's thumbnail at the picker's pixel size (1.8's rule), so opening Style › Theme shows every tile at once. ~24 small images: a few MB.
3. **A placeholder that's never empty.** Until a picture is there (a theme added locally, offline), the tile shows the theme's own colours (its palette as a gradient and swatches, as the tile's colour row already draws) rather than a blank, so the picker is complete from the first frame.
4. **Full backgrounds fetched in the background.** After install (the background queue, [install-speed.md](install-speed.md) G), each theme's backgrounds download quietly at low priority into `%LOCALAPPDATA%\launcher\backgrounds\<theme>`, so the first switch to any theme is instant; on a metered connection, only the current theme's and on demand.
5. **The background picker too** (mission-todo track 5, "a grid of the theme's pictures"): thumbnails for every background of every theme, made by CI the same way, so that grid is instant.

**Done when:** on a fresh install, opening the theme picker shows every theme's picture in the first frame, and switching to any theme for the first time doesn't wait on a download.

**Built 2026-10-31 (step 1):** `src/thumbs` (C#, SkiaSharp) makes every theme's first background 320 pixels wide in CI (23 themes, ~1.9 MB, a cubic filter), named as `ThemeThumbs.cs` names them; release.yml's `thumbs` job publishes them as the bundle entry `theme-thumbs`, unpacked into `%LOCALAPPDATA%\launcher\thumbnails` at install, so the picker's first open finds them all and downloads nothing. Also built: the menu app decodes every picture already on the PC once it's up and idle (`ThemeThumbs.Preload`, 2), so the picker's first frame has them; until a picture is there, a tile shows the theme's background, an Aa sample and its colour bars (3, already so); and after start-up it fetches every theme's backgrounds one at a time into `launcher\backgrounds\<theme>`, only on an unmetered connection (`ThemeThumbs.FetchBackgrounds`, 4).

**Built 2026-10-31 (step 5):** the background picker. System menu › Style › Backgrounds (menu.txt action `backgrounds`) or `omawin theme backgrounds [theme]` (`app --open style.backgrounds`) shows a grid of the current theme's backgrounds (tile style `picture` in `MainWindow.Tiles.cs`), the one on the desktop checked (from `launcher\background`); arrows move, typing narrows by name, Enter sets one and stays (`set-theme.ps1 -Background <name>` with no theme named now changes only the wallpaper and lock screen, like `-NextBackground`; `omawin theme background <name>` runs the same), Tab / Shift+Tab shows the next theme's, where Enter switches to that theme on that picture. `src/thumbs` now makes every background of every theme by default (`--first` for the first only): 79 pictures of 23 themes, 6.2 MB (6.5 MB zipped), so 320 pixels stays for all of them; `ThemeThumbs.Get` takes any background, `Preload` adds the current theme's, and making one no longer deletes the theme's other pictures (only ones it no longer lists). The names come from one pure file, `shell/menu-app/ThemePictures.cs` (unit-tested; `src/thumbs` compiles it too). Left: seen on screen (the grid, its check, Tab across themes) on a Windows PC; the menu app's C# compiles against the Windows App SDK (checked off Windows with stubs for the XAML fields) but hasn't been run.

### 1.10 Community themes in the picker (M, 🖥)

**Why:** Omarchy's community has many themes beyond its built-in 24 (listed at omarchy.org/themes; each is a git repo, usually `omarchy-<name>-theme`). omawin can already install one: `omawin theme install <git link>` (`themes/theme.ps1`: clones into `%LOCALAPPDATA%\launcher\themes\sources\<name>`, converts it with `convert-theme.ps1`, applies it; like Omarchy, nothing that runs code is taken from it), plus `theme update`, `remove`, `list`, and `browse`, which opens Omarchy's web page to copy a link from. What's missing: **seeing community themes in omawin's own picker, with their pictures and colours, before installing**, and installing by name.

**How a community theme gives us what we need, without cloning it:** an Omarchy theme repo holds `colors.toml` (the palette; older ones `alacritty.toml`, which `convert-theme.ps1` and Omarchy's `omarchy-theme-colors-from-alacritty` turn into a palette), `backgrounds/`, and usually **`preview.png`** (what Omarchy's own switcher shows), sometimes `light.mode`. All readable as single files from GitHub (`raw.githubusercontent.com/<repo>/<commit>/…`), so the picker needs only the palette and one picture per theme, not the repo.

**Build:**
1. **A community theme index, built by CI** (a scheduled workflow, weekly): the list of community themes (**primarily [omarchy.org/themes](https://omarchy.org/themes/)**, which lists them; it isn't in Omarchy's repo, so CI reads the page itself; plus GitHub repos with the `omarchy-theme` topic or named `omarchy-*-theme`, plus a hand-kept `themes/community-extra.txt`), and for each, at a pinned commit: its palette (converted to omawin's format), whether it's light or dark, its author and licence, and a thumbnail made from `preview.png` (or its first background) at the picker's sizes (1.9). Published as one small release asset (`community-themes-<date>.zip`: an index JSON and the thumbnails; a few MB) and refreshed in the background by the menu app.
2. **The picker shows them.** Style › Theme gets a **Community** chip (Tab) beside the built-in themes: every community theme's picture and colour swatches at once (from the index, preloaded like 1.9), searchable by name, author and light / dark, sorted by popularity (stars) or newest.
3. **Try before installing.** Selecting a community theme previews it live: omawin's themes are a palette plus pictures, so the index's palette is enough to apply it to every app for a moment (the same render as `theme set`, from a temporary palette) with its thumbnail as the background; moving away or Esc puts your theme back. **Enter installs it** (the existing `theme install`, at the index's reviewed commit) and keeps it.
4. **Install by name:** `omawin theme add <name>` finds it in the index (`omawin theme add <git link>` still works for anything not listed); `omawin theme search <words>` lists matches. `theme install` stays as an alias.
5. **Installed themes get proper thumbnails** at install time, from the clone's `preview.png` first (it's what the theme's author chose to show), else its first background; and their backgrounds come from the clone (already on disk), so switching to them never downloads.
6. **Safety as Omarchy's:** only data is used from a community theme (the palette, pictures, the light / dark marker); never code (Neovim Lua, terminal configs, VS Code extensions: Omarchy's denylist, which `theme.ps1` already follows). The index records the commit it read; installing takes that commit, and `omawin theme update` shows what changed before pulling, as plugins do.
7. **Credit:** each community theme shows its author and links to its repo, in the picker and in `omawin theme list`.

**Done when:** opening Style › Theme › Community shows every listed community theme's picture and colours in the first frame, selecting one previews it across the desktop, and Enter installs it with no link to copy.

**Read 2026-10-30:** [omarchy.org/themes](https://omarchy.org/themes/) is static HTML (built with Astro), 218 themes, one list item each: a link to the theme's GitHub repo (its owner is the author), its name, and **a 1200×675 screenshot omarchy.org already hosts** (`https://omarchy.org/assets/themes/<slug>.webp`), which makes the thumbnail directly. Light or dark isn't on the page, so it comes from the repo's `light.mode`. The reader for the CI tool (C#):

```csharp
// Reads omarchy.org/themes' list: one entry per <li><a href=repo><img src=/assets/themes/slug.webp alt="Name theme screenshot">.
static readonly Regex Card = new(
    @"<li><a href=""(?<repo>https://github\.com/[^""]+)""[^>]*><img src=""(?<img>/assets/themes/[^""]+\.webp)"" alt=""(?<name>[^""]+?) theme screenshot""",
    RegexOptions.Compiled);

public static IEnumerable<(string Name, string Repo, Uri Screenshot)> Themes(string html) =>
    Card.Matches(html).Select(m => (
        WebUtility.HtmlDecode(m.Groups["name"].Value),
        m.Groups["repo"].Value,
        new Uri(new Uri("https://omarchy.org"), m.Groups["img"].Value)));
```


---

## Phase 1B: every Windows setting in the system menu (next)

**Started 2026-10-30:** the catalog and its parser are in: `default/launcher/settings.txt` (the first 56 settings across the nine areas; the 12 marked `check` stay hidden until verified on a PC) and `shell/menu-app/Settings/Catalog.cs` (pure C#, [02-settings-and-one-search.md](02-settings-and-one-search.md) §1-2), with tests in `test/menu-app.Tests/Settings/`, including one that fails CI when a line in the shipped catalog doesn't parse. Next: the handlers that read and write each setting (§3-5), which need Windows.

**Why:** Windows' Settings app is slow to open, deep, and changes between builds. Omarchy has one menu for everything you change; omawin's system menu (Win+Alt+Space, retired by One Search) has a few rows that open Settings pages (`default/launcher/menu.txt:70-76`). The goal: **you never open Settings again**. Type a few letters ("dark", "ext", "mic", "hz"), see the setting with its current state, press Space to switch it. What can't be switched from outside Windows' own app opens on its exact page, one keystroke away.

What exists to build on: the system menu's search (`MenuModel.Search`, fuzzy, `MainWindow.xaml.cs:598`), the launcher's usage ranking (`Launcher.cs:173-222`), and Quick settings' working switches (`shell/menu-app/QuickSettings.cs`: Wi-Fi, Bluetooth and other radios, brightness per monitor, power mode, battery, night light's state, notifications' state, change watching).

### 1B.1 The index: every setting Windows has (M, 🖥)

- **Windows' own search data.** Settings' search box reads an index Windows ships: `%WINDIR%\ImmersiveControlPanel\Settings\AllSystemSettings_*.xml`, one entry per setting with its name, keywords and the page that holds it, in the PC's language (names are resource strings, resolved with `SHLoadIndirectString`). Read it once in the background, cache it in `%LOCALAPPDATA%\launcher\settings-index.tsv`, rebuild when Windows updates (the file's date). Confirm the format on the PC first; it's undocumented.
- **Fallback and extras:** the documented `ms-settings:` pages (a curated list in the repo), Control Panel items (the shell's Control Panel folder), the admin consoles (Device Manager, Services, Event Viewer, Disk Management, Task Scheduler), and classic dialogs people still need (environment variables: `rundll32 sysdm.cpl,EditEnvironmentVariables`; advanced system settings; network adapters).
- Each becomes a row: icon, name, the page it's on (`System › Display`) as the dimmed second line.

### 1B.2 Switches: change it without opening anything (L, 🖥)

The core of the feature: a catalog of settings omawin can **read and change itself**, shown as switches, choices or sliders in the row.

- **The catalog** is data, `default/launcher/settings.txt` (layered like `items.txt`: the PC's shared folder and the account folder can add their own), one setting per line: `id | icon | name | keywords | kind | how | notes`.
  - `kind`: `switch` (on / off), `choice` (one of a few values), `slider` (a number in a range), `action` (do it once), `link` (open its page).
  - `how`, declarative where possible: `reg HKCU\…\Value on=1 off=0 [broadcast] [restart-explorer]`; `spi <SystemParametersInfo action>`; `powercfg <setting>`; `builtin <name>` for anything that needs code (radios, display modes, HDR, default audio device, proxy); `link ms-settings:<page>`.
  - `notes`: `admin` (needs elevation), `signout` / `restart` (when it takes effect), `minBuild` (the Windows build it's known to work on).
- **Handlers** in `shell/menu-app/Settings/` (`Catalog.cs` reads the file; `Switches.cs` does `reg`, `spi`, `powercfg`; `Builtins.cs` reuses `QuickSettings.cs` and adds the rest). After a registry change, the `broadcast` flag sends `WM_SETTINGCHANGE` so Explorer and apps pick it up without a restart.
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
- `default/launcher/menu.txt`'s Setup section gains a *Windows* submenu by area (Look, Explorer, Privacy, Input, System, Display, Connections, Sound, Developer) for browsing without typing.

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

The "never open Settings" list: 30 everyday changes (dark mode, file extensions, the microphone for apps, refresh rate, mouse acceleration, notifications, power mode, developer mode, …) each done from Win+Space in a few letters and one key, with undo; CI's round-trip passing; `omawin settings apply` setting up a fresh PC (in `omawin sandbox test`, 7.1).

**Risks:** the settings index's format is undocumented (fallback: the curated page list); Windows changing a setting's registry value between builds (`minBuild`, CI on current Windows); settings that only apply after sign-out (said in the row); some settings have no supported way to change (night light, do not disturb): those stay links, honestly labelled.

## Phase 1C: One Search (one bar, tagged results, a bento home)

**Started 2026-10-30:** step 1 of [one-search.md](one-search.md)'s order of work is in: the result model, query parser and merger (`shell/menu-app/Search/Result.cs`, `QueryParser.cs`, `Merger.cs`; pure C#, the launcher's scoring scale) with 31 unit tests in `test/menu-app.Tests/` (`dotnet test menu-app.Tests`; CI: `.github/workflows/test.yml`). Not wired into the window yet; the menu app hasn't been built with it on Windows (🖥: `omawin restart menu` should still build).

The launcher, the system menu, its pages and the file finder become **one window with one search bar**: every result tagged with what it is (app, setting, command, file, window, project, clipboard, theme), filters by tag in the bar (Tab, `/`, `>`, a filter's name + Tab), a bento home when nothing is typed, a ranked list with a preview when something is, Enter to act (settings switch in place, with undo), Ctrl+K for more. **Win+Space is the one key for everything; Win+Alt+Space is retired** (the maintainer's decision, 2026-10-30): for one release it opens One Search with a note, then a migration removes the binding and the key list in AGENTS.md and the manual change with it. Win+F and Win+Shift+V stay, opening the same window on Files and Clipboard. The design, its twelve guardrails, what happens to today's pieces and the open questions: **[one-search.md](one-search.md)**; clickable mockup: [omawin One Search](https://claude.ai/artifact/FCAA1kuXE7H6BjjfVzrN8z). Phase 1B's settings search becomes its Settings filter.

## Phase 2: fast install

**Goal:** a clean Windows 11 PC goes from the one line to a working omawin desktop in **under one minute**, with no admin prompt on the way; everything else (presets, WSL, the forks' machine-wide install, dev tools) carries on in the background while you already use it. Measured, not guessed.

### 2.0 How Omarchy is fast, and the same on Windows

**The full sub-plan is [install-speed.md](install-speed.md)**: workstreams (measure, the two-stage bundle, download and unpack, one-pass configs, the desktop at once, the welcome page, the background queue, fast delta updates, package managers staying in charge, offline installs), milestones from under a minute to under 15 seconds, and what to explore. This section is the summary.

Omarchy's install is fast because almost nothing is fetched or decided on the machine (from its code, `docs/file-layout.md`):
- **Everything comes on the image.** The ISO carries an **offline mirror** of every package in its list (the ISO builder reads `install/omarchy-base.packages` to build it), so installing is copying, not downloading; prebuilt images go further and are set up before they reach the machine.
- **Configs are packages.** Omarchy's defaults ship as two Arch packages (`omarchy`, `omarchy-settings`) and reach a new account from `/etc/skel`: no per-file setup.
- **What depends on the machine waits.** Hardware steps are queued and run at the first boot (`/var/lib/omarchy/image/deferred-steps`); migrations are simply marked done on a fresh install (`omarchy-provision-user`).
- Downloads that do happen go five at a time (pacman's `ParallelDownloads = 5`).

omawin's install today does the opposite: it decides and downloads everything on the PC, one package manager call after another (winget ~150 s before anything else, then three release downloads, then the steps), and the desktop starts only after a sign-out. The plan, in Omarchy's spirit:

**The omawin bundle: one prebuilt download (L).**
- CI builds, per release and architecture, **`omawin-bundle-<tag>-win-<arch>.zip`**: everything the desktop needs to run, as portable files, at pinned versions, checked by `SHA256SUMS.txt`. Our offline mirror.
  - omawin's own: `omawin.exe`, the menu app (Native AOT, self-contained: no .NET runtime to install), noctty, the GlazeWM fork (per-user; the machine-wide install with its signed `uiAccess` follows in the background, see below).
  - The command-line core as single exes (2.3's list: ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, gh, uv, mise), **MinGit** (Git for Windows' portable build) and **Neovim**, with **LazyVim's plugins already installed** at `lazy-lock.json`'s versions, so the first `nvim` opens ready.
  - The JetBrainsMono Nerd Font, the default theme's rendered files and its first background.
  - The repo itself at that tag (configs, scripts), so the install needs no `git clone`.
  - Size target: ~200 MB compressed (to be measured; Neovim's plugins and MinGit are the big ones), ~15-20 s on a 100 Mbit/s line.
- **`bootstrap.ps1` becomes a short, fixed sequence** (Windows PowerShell 5.1, no admin):
  1. Download the bundle with **`curl.exe`** (in Windows 10 and 11; parallel ranges, resumable, far faster than 5.1's `Invoke-WebRequest`), verify its hash.
  2. Unpack with **`tar.exe`** (also in Windows; much faster than `Expand-Archive`) into `%LOCALAPPDATA%\omawin\<tag>\`, with a `current` junction (an update is a new folder and a junction flip, and a rollback is the old one).
  3. **Seed configs, don't build them**: the user's config files written from the bundle in one pass (Omarchy's `/etc/skel` idea), the PATH entry, the per-user font registration, the sign-in task.
  4. **Start the desktop now**: GlazeWM, the bar and the menu app, in the default theme. No sign-out.
  5. Mark every migration as done (a fresh install needs none, as Omarchy does).
- **No questions before the desktop.** The defaults are good (that's omakase); the choices (presets, which Linux, git name and email, Dev Drive) move to a **welcome page** in the launcher, open on the first sign-in, which starts the background work below.
- **Then, in the background**, with progress in the bar and a toast at the end (Omarchy's deferred steps): the one admin prompt (the forks' machine-wide install, WSL, whole-PC packages), the presets through mise and winget, WSL and Arch, Windows' tune-up (8G), the no-ads tweaks, the rest of the steps. A failed step stays queued and is retried at the next sign-in, as Omarchy's deferred queue does.
- **What can't be in the bundle:** anything that installs machine-wide or needs admin (the forks' signed `uiAccess` install, WSL, Windows features), Store-only apps, and the user's presets; all of them background work.
- **Updates the same way:** `omawin update` downloads the next bundle (later, a delta of the files that changed), unpacks it beside the current one, runs the new migrations, flips `current`; the running desktop restarts its parts.

**The minute, budgeted** (100 Mbit/s, an SSD; CI measures each line, 2.1):

| Step | Seconds |
|---|---|
| `irm … \| iex` to `bootstrap.ps1` running | 2 |
| Download the bundle (~200 MB) | 15-20 |
| Verify and unpack | 5-8 |
| Seed configs, PATH, font, sign-in task | 3-5 |
| Start GlazeWM, the bar and the menu app | 2-3 |
| **Desktop up** | **~30-40** |

**Risks:** a slow connection (the bundle is the floor: on 20 Mbit/s it's ~80 s, so the welcome page says so and the desktop still comes before everything else); SmartScreen and Defender scanning many new executables on first run (signing in CI, release-signing.md, and Defender's own cache); the per-user GlazeWM without `uiAccess` can't move elevated windows until the background step installs the signed one; keeping the bundle's pinned tools current (Renovate-style bumps in CI, each release).

What install does today: `install.ps1` asks the questions, starts the one admin prompt as a separate hidden elevated process (`install/admin.ps1:61-89`; machine-wide packages three at a time, `admin-batch.ps1:40-80`), then runs `install/steps.txt` in order: `no-ads`; `software` (all core and preset winget packages in one batch, four at a time, `Invoke-WingetBatch`, `install/software.ps1:52-118`; then extras one by one; then the Nerd Font); `shell`; `desktop`; `helpers` (three separate release downloads, each resolving the tag and manifest again, `bin/install-binaries.ps1`); `look`; `other`; then migrations. The desktop isn't started: the summary says to sign out and back in (`install/ui.ps1:453`).

### 2.1 Time the install in CI (M)

**Build:** `.github/workflows/install.yml`, scheduled and on demand: a fresh `windows-2025` runner runs `bootstrap.ps1` from the branch (`OMAWIN_PLAIN=1`), then `omawin check`, and uploads `install.log` and `install-times.txt`. It fails when `check` reports a failure or the time passes the budget. Runners have no WSL (no nested virtualization) and no interactive desktop, so the job skips the `wsl` preset and the desktop start, and says so.

**Done when:** every push to main has an install time, with the trend in the job summary.

**Risks:** runner images come with Git, Node and PowerShell 7 preinstalled, so the install looks faster than on a real clean PC. Report install's own "already there" counts, and keep the Windows Sandbox run (7.1) as the honest clean test.

**Progress:** started 2026-10-30 with today's install (no bundle yet): `.github/workflows/install.yml` makes a standard user on `windows-2025`, runs the README's bootstrap as that user through a scheduled task (a real logon; `Start-Process -Credential` can't from the runner's service session), then `omawin check`, and writes the step times to the job summary. When the bundle lands, it switches to 03 §8.1's version.

**First numbers (2026-10-30, `windows-2025`, a new standard user, no admin):** its first runs found six bugs that hit a brand-new account, now fixed:
- `bootstrap.ps1` stopped after 2 s, on the error PowerShell raises when the user's execution policy is set under a Process one.
- winget's package list isn't registered for an account before its first desktop sign-in (`0x8a15000f` for every package). The install now registers it, or adds Microsoft's `source.msix` for the account.
- The font step crashed on a missing `HKCU\…\Fonts` key, stopping the install.
- `release.ps1`: a signed-out `gh` writes to stderr, which Windows PowerShell treats as fatal, so the release was skipped and everything was built on the PC.
- The Nerd Font took 5 min 40 s through `Invoke-WebRequest`'s progress bar; now 9 s with `curl.exe`.
- `omawin check` never finished when GlazeWM wasn't running: `glazewm query` waits for it forever. Now it asks only when GlazeWM runs, with a 5 s limit.

Whole install: 11 min 26 s → 8 min 53 s → **5 min 46 s** (the last run: software 2 min 39 s, helpers 2 min 39 s, everything else 17 s); `omawin check` then reports 9 failures, all expected without admin or a desktop (whole-PC installers, Windows Terminal on Windows Server, the unsigned GlazeWM, nothing running). Software 3 min 5 s (winget, four at a time), helpers 4 min 48 s (of which noctty 1 min 43 s and the menu app 2 min 53 s, built on the PC only because this branch's menu-app code is newer than the release), look 20 s, migrations 18 s. With a current release the helpers step is about a minute: the bundle (2.0) is what takes this under a minute.

**Under a minute (2026-10-30, the tools bundle, 2.0):** **51 s** from the bootstrap to the end (`install.ps1` itself 46 s), as a new standard user with no admin, against this branch's release laid out locally (no download). Omarchy's way: the release carries every core tool prebuilt (`install/tools.txt`, built by `build/bundle` in `release.yml`), and the install unpacks them instead of running winget 20 times. On the way: PowerShell 7 first (the bootstrap is the only 5.1 script), the unpacking in the background from the start (`install/unpack-tools.ps1`; zig's 19,500 files take 22 s, all of it hidden), winget only for what's left (Zebar, the .NET desktop runtime), herdr's installer alongside, Windows Terminal's unpackaged settings written directly (was a 34 s wait). Steps: software 13 s, migrations 5 s, helpers 5 s, ads off 4 s, desktop 3 s, the rest under 2 s each. `omawin check`: 4 failures, all a runner's (Zebar needs admin, the per-user GlazeWM is unsigned, no desktop running).

**With real downloads: 53 s (2026-10-30, pre-release `v0.3.0-rc.3`).** `release.yml` now times every published release (its `time-install` job, `.github/actions/time-install`): a new standard user on `windows-2025` runs the README's one-line bootstrap against the release just published, downloading everything from GitHub, PowerShell 7 included (`OMAWIN_PWSH=bundle`, since a runner already has one). Bootstrap to the end, **53 s**; `install.ps1` 41 s. What got it there after the local 51 s: the helpers and the font download in the background from the first second (`Start-EarlyHelpers`, the font as a bundle entry), no winget whole-list export on a PC omawin has never been on (it took 10-22 s; commands and known files decide instead, `WingetFiles`), and zig marked `later`: on the PATH at once, its 19,500 files finishing in the background after the install. The install waited 24 s for the bundle (Neovim 13 s, the font 9 s). Runs: rc.1 77 s, rc.2 57 s (PowerShell 7 not yet downloaded), rc.3 53 s. Not covered: the git clone (the job uses the checkout: the bootstrap would clone main), a home connection's speed (a runner's is fast), and a PC's own disk and antivirus.

**No winget in the core, 46 s (2026-10-31, the branch's install test, from a local copy of the release):** git now comes from the bundle too (Git for Windows' portable build, shipped as its own 57 MB self-extractor: a zip of what's inside was 157 MB), the bootstrap fetches it and PowerShell 7 at once (`OMAWIN_GIT=bundle` makes the test take it though the runner has git), the .NET runtime left the core (the release's menu app is Native AOT), zig is gone (Neovim's parsers come prebuilt, 1.6 MB), and the font is its four faces. The bundle: 23 tools, 394 MB on the release, of which the install downloads what this PC doesn't have.

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

*Updated by [install-speed.md](install-speed.md):* the first install takes the command-line tools from the bundle's stage 2 (fastest); mise stays for languages and for anyone who picks it as a tool's source; winget, Scoop and Chocolatey can take over any tool at any time (`omawin tools source`, `omawin tools handover`), and their copy wins on the PATH.

**Why:** winget spends seconds per package before downloading; these are single exes.

**Build:**
- `mise.toml` in the repo: ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, gh, tree-sitter, uv, and Neovim if mise's Windows build is good (else it stays on winget). Exact versions and mise's lockfile.
- mise itself: move `Initialize-Mise` from `bin/install-dev-env.ps1:170-189` (winget `jdx.mise`, user scope, shims first on the user PATH) into `install/lib.ps1`, shared by install and dev envs; or fetch mise's release exe directly, which is faster than winget.
- The early step runs `mise install` (parallel downloads) alongside the winget batch.
- `install/packages.ps1` drops those winget ids and their `$WingetCommands` rows.
- `bin/check.ps1:44-60`: `Get-Command` finds shims on PATH; its fallback paths learn `%LOCALAPPDATA%\mise\shims`.
- On existing PCs, a migration: tools winget installed stay (never uninstall the user's programs); mise's shims come first on PATH; `omawin update` runs `mise install` for new pins and `mise upgrade` for omawin's tools.

**Done when:** a clean install fetches all the tools in a few seconds; `omawin check` passes.

**Risks:** GitHub's anonymous limit (60 API requests an hour per IP): pinned versions and the lockfile avoid lookups. A work network blocking github.com: winget fallback per missing tool, said in the summary.

### 2.4 Neovim ready on first open (S)

lazy.nvim bootstraps on the first `nvim` (`config/nvim/lua/config/lazy.lua:1-15`), so the first open waits. After install's last step, run `nvim --headless "+Lazy! restore" +qa` hidden in the background (restore from `lazy-lock.json`, so versions match), and Mason's core tools. Same inside Linux (3.4).

### 2.5 One download for our own pieces (S)

Resolve the release tag once, fetch `SHA256SUMS.txt` once, then download the helpers zip, the menu app zip and the music exe in parallel (PowerShell 5.1-safe: `HttpClient` tasks or BITS), verifying each. Later, one `omawin-<tag>-win-<arch>.zip`.

### 2.6 A leaner core (S)

Move Neovide and Windows Terminal (Windows 11 ships it) to presets; drop `OpenJS.NodeJS.LTS` when mise provides Node (Mason needs `npm` on PATH; mise's shims give it). `zig.zig` stays while nvim-treesitter compiles parsers with it. Each change: `install/packages.ps1`, `bin/check.ps1`'s list, the docs.

### 2.7 Is the desktop runtime still needed? (S)

`Microsoft.DotNet.DesktopRuntime.9` is one of the slowest items. The AOT menu app is self-contained; if it no longer needs the runtime, only the music player does: publish the player self-contained (or AOT) too, and the runtime leaves the core.

---

### 2.9 No admin: zero prompts by default, one when you ask for something that needs it (M)

**The rule:** installing, updating and using omawin never ask for admin. If you choose something that truly needs it, omawin gathers everything that choice needs into **one** prompt, says exactly what it will change before Windows asks, and does it all at once. **Updates never ask**: a pending admin change waits for you to say when (a quiet line on the welcome page or the bar, never a surprise prompt). Declining is always fine: everything else keeps working.

**Everything that needs admin today or in this plan, and how it stops needing it:**

| What | Why it needs admin now | Without admin |
|---|---|---|
| **The GlazeWM fork's install** (`install/glazewm-fork.ps1`) | GlazeWM asks for `uiAccess` (to manage windows of programs running as admin); Windows grants that only to a program signed by a certificate the PC trusts, in Program Files. Today a certificate is made on the PC and the fork copied into Program Files | **A per-user GlazeWM by default**: the fork built twice in CI, a normal build (no `uiAccess`) that runs from the bundle in `%LOCALAPPDATA%` with no admin at all, and the `uiAccess` build. The only loss: windows of programs you started *as admin* aren't tiled or moved. **Opt-in** "Manage admin windows too": one prompt puts the CI-signed `uiAccess` build in Program Files (no certificate made on the PC once CI signing exists, [release-signing.md](../docs/release-signing.md)); its later updates are offered, never forced |
| **Whole-PC packages** (`$WholePC` in `install/packages.ps1`: PowerShell, Git, Node, Neovim, …) | installed machine-wide, for every account | **Per user by default** (`--scope user`, or the bundle's portable copies); machine-wide only if you choose it (a shared PC) through `omawin pkg install --machine` |
| **The .NET desktop runtime** | machine-wide installer | not needed: the menu app is AOT, the music player published self-contained (2.7) |
| **The local code-signing certificate** | the PC's certificate store | gone: signing happens in CI |
| **WSL** (Arch by default) | Windows' *Virtual Machine Platform* feature must be on, which only admin can turn on, plus a restart | checked first: on many PCs it's already on, and then WSL and Arch install **with no prompt** (the WSL app and distros are per user). If it's off, this is **the** one prompt, at the moment you pick Linux on the welcome page, never in the first minute |
| **No-ads' admin half** (machine-wide policies) | `HKLM` policies | the per-user equivalents (`HKCU`) wherever Windows honours them (most suggestion and ad settings); the few machine-only ones become opt-in catalog entries |
| **Chocolatey** | installs machine-wide | only if you choose it (`omawin pkg prefer`); winget and Scoop work per user |
| **A restore point before changes** (8.42) | `Checkpoint-Computer` needs admin | omawin's own rollback needs none (version folders, `omawin update --rollback`, every setting's undo); a restore point is made only inside an admin batch you already approved |
| **Phase 8 and 1B's admin settings** (developer mode, long paths, sudo, Windows features, Dev Drive, DNS, the hosts file, the dev port ranges, Defender exclusions, the whole-disk index, driver and update policies) | they're machine-wide | **opt-in, shown with a shield**; choosing several queues them, and one prompt applies the batch |
| **The admin helper** (8.0) | a standing service would hold admin rights all the time | **no standing service**: each approved batch runs as one short elevated process with a fixed list of verbs, then exits; nothing elevated stays running. (A whole-disk index service, 8.2, is the one exception, and only if you turn that feature on) |

**What already needs no admin, kept that way:** per-user fonts (Windows 10 1809+), the sign-in task (a scheduled task for your own account), the theme, the bar, the menu app, noctty, the configs, the PATH, Scoop, mise, per-user winget packages, WSL distros once the feature is on.

**How a prompt looks when one is needed:** before Windows' UAC dialog, omawin shows the list (*"Turn on Virtual Machine Platform (needs a restart), developer mode, long paths"*), why, and *Not now*. One UAC dialog; the work runs; a summary of what changed; undo where possible. Never more than one dialog for one choice.

**Checked in CI:** the install job (2.1) and the update job run **as a standard (non-admin) user** created on the runner, and fail if anything tries to elevate (no `RunAs` process, no UAC). The Sandbox test (7.1) does the same.

**Done when:** a fresh install and an update, Enter through every question, raise **no** admin prompt; picking Linux on a PC without Virtual Machine Platform raises exactly one; turning on any shielded setting raises at most one per batch.

### 2.10 Uninstall anything you choose (M)

**Why:** omawin is omakase, not a cage. Anything it installed, and anything else on the PC, can be removed when you choose, from the same place you'd install it.

- **Install › Remove** (exists for winget packages) lists everything installed by any manager (`omawin pkg list`, 2B): winget, Scoop, Chocolatey, mise, the bundle's tools, Arch's packages, and Windows' own extra apps (8.50); Enter removes, Ctrl+Space picks several. `omawin pkg remove <name>` does the same from a terminal.
- **omawin's own parts, one at a time:** `omawin remove <part>` for the optional pieces (the music player, dictation, the agents, WSL's Arch, a preset, a plugin, a theme): stops it, removes it, removes its settings and its sign-in entries, says what's left (your files: never touched).
- **All of omawin:** `omawin uninstall` does what [install.md](../docs/install.md)'s *Undoing it* lists by hand: stops the desktop, removes the sign-in tasks, puts back each config it replaced (the `.backup-<timestamp>` copies), removes its PATH entries and environment variables, the taskbar back to normal, and asks whether to also remove the programs it installed (each listed, all ticked by default) and the Linux it made. Your files, your account folder and anything you installed yourself stay unless you tick them.
- **What omawin no longer needs**, it uninstalls itself in a migration, said in the update's summary (the old web bar, 1.6); never something you installed yourself.
- **No admin unless the program needs it** (2.9): per-user installs uninstall without a prompt; a machine-wide one waits in the pending admin list, and several go in one prompt.

**Done when:** every installed thing shows in Remove with its source; `omawin uninstall` on a test PC (Windows Sandbox, 7.1) leaves Windows as it was before omawin, apart from what you chose to keep.

### 2.11 Scripts become compiled C# (L, gradual)

**The rule** (the maintainer's, 2026-10-30; now in AGENTS.md): any new tool, script or helper is **compiled C#**; **Rust** where C# isn't the right tool; **Bun** for JavaScript and TypeScript; PowerShell only for the one-line bootstrap entry and the user's profile; bash only inside Linux; no Python.

**Why:** every `pwsh` command costs ~0.3 s to start (41 of `bin/commands.txt`'s commands run a PowerShell script today, and 91 `.ps1` files exist); compiled code starts in milliseconds, is typed and testable, and an elevated step as a signed exe is safer than a script in a folder the user can write to.

**How:**
1. **One `omawin` CLI in modern C#.** `bin/omawin.cs` is C# 5 today only because it's built on the PC with Windows' in-box `csc.exe`. With releases built in CI and the bundle shipping binaries (2.0, [install-speed.md](install-speed.md)), it becomes a .NET 9 Native AOT console app (`src/cli/Omawin.Cli.csproj`) with subcommands as classes (`Commands/Theme.cs`, `Commands/Pkg.cs`, …), the same `bin/commands.txt` routing, and `csc` kept only as the from-source fallback for the dispatcher.
2. **What moves first:** anything on a key or a hot path (theme set and render, `terminal here`'s helpers, profile tools); anything elevated (the admin verbs become one signed `omawin-admin.exe` with its fixed verb list, 2.9, 8.0); the new tools of this plan (`omawin pkg`, `omawin plugin`, `omawin setting`, the bundle's first-run and queue worker, delta updates, `omawin linux backup|reset`, `omawin sandbox test`, web apps, `omawin uninstall`).
3. **Install after the bootstrap:** `bootstrap.ps1` (Windows PowerShell 5.1, the `irm | iex` entry) only downloads and unpacks the bundle; everything after that (seeding configs, the queue, steps, migrations) is the C# CLI from the bundle.
4. **Migrations** become C# classes in the CLI (`Migrations/M2026_10_30_ZebarOut.cs`), each safe on a new PC and twice, recorded in `migrations.txt` as today; existing `.ps1` migrations stay until they age out.
5. **CI tools in C# too:** release packing, the bundle builder, theme thumbnails (SkiaSharp, which decodes WebP everywhere), the community theme index, the plugin scan.
6. **The rest of the `.ps1` scripts** port when they're next changed; none is rewritten only for the sake of it.

**Done when:** no command on a key starts PowerShell; nothing elevated is a script; new tools in the plan are C# (or Rust / Bun where stated).

**Progress:** started 2026-10-30. `src/cli/` is the .NET 9 Native AOT `omawin` (3 MB): `CommandTable` (bin/commands.txt's rows, the longest-match routing, help) and `Actions` (app, pwsh, run, builtin rows turned into what to run, with `{repo}`/`{user}` and Windows quoting) are pure and tested in `cli.Tests` (34 tests, including every shipped row routing to itself, every builtin known and every repo script present); `Program.cs` sends menu-app requests and starts processes as `bin/omawin.cs` does. Next: port the builtins (launch-terminal, launch-project, launch-editor, terminal-here, terminal-warm, start-glazewm, update-terminal, default; start-zebar goes with 2.10), build `win-x64` in the release, and switch `install.ps1` to it, keeping `bin/omawin.cs` as the fallback.

## Phase 2B: `omawin pkg`, one package command over winget, Scoop and Chocolatey

**Why:** Windows has three package managers, each with a different catalogue, different ids, different scopes and different update commands, and you have to remember which one installed what. Omarchy wraps pacman and the AUR in its own commands (`omarchy-pkg-add`, `omarchy-pkg-drop`, `omarchy-update`); omawin should do the same for Windows: **one command, and omawin picks and remembers the manager.** omawin is already partway there: the launcher's Packages page searches all three (`omawin pkg find`), `omawin pkg install` / `remove` are winget-only today (`bin/pkg-install.ps1`, `bin/pkg-remove.ps1`), and `%LOCALAPPDATA%\launcher\installed.txt` records what's installed.

**What it is:** a thin layer, not a new package manager. It never resolves dependencies, builds packages or keeps its own repository: it chooses a backend, runs it, and records the choice. Backends: **winget**, **Scoop**, **Chocolatey**, **mise** (languages and dev tools), the **omawin bundle** (2.0: the fast first-install copies), and **pacman in Arch** for Linux packages (`--linux`).

**Commands** (rows in `bin/commands.txt`, the same in the Packages page and the system menu):

| Command | Does |
|---|---|
| `omawin pkg install <name> [--from winget\|scoop\|choco\|mise] [--linux]` | finds the package in every backend's catalogue, picks one (below), installs it, records where it came from |
| `omawin pkg remove <name>` | removes it through the backend that installed it |
| `omawin pkg update [name]` | updates one package, or everything, each through its own backend (`winget upgrade`, `scoop update`, `choco upgrade`, `mise upgrade`, a new bundle, `pacman -Syu` in Arch), admin ones in one prompt |
| `omawin pkg list` | everything installed, by any backend (including things you installed without omawin), with version, backend and whether an update is waiting |
| `omawin pkg info <name>` | which backends have it, at what versions, which copy runs now (PATH order) |
| `omawin pkg move <name> --to <backend>` | moves a package to another backend: installs there, removes or sets aside the old copy (the bundle's copy is set aside, never deleted) |
| `omawin pkg prefer <backend> [backend…]` | your order of preference |

**How it picks a backend**, in order: your explicit `--from`; a per-package pin you set before (`omawin pkg move` records one); omawin's own default for that package (a small table in the repo, e.g. languages through mise, GUI apps through winget, the bundled CLI tools from the bundle); your preference order (`omawin pkg prefer`, default winget, then Scoop, then Chocolatey); the first backend that has it. Machine-wide or per-user follows the backend's default unless `--machine` / `--user` is given; anything needing admin waits for one batched prompt.

**What it records:** `%LOCALAPPDATA%\launcher\packages.tsv`: name, backend, backend id, scope, version, installed by omawin or found. `omawin pkg list` refreshes it from each backend's own list (`winget export`, `scoop export`, `choco list`, `mise ls`), so packages installed outside omawin show up too, and nothing drifts.

**One name for one thing:** the same tool has different ids per backend (`BurntSushi.ripgrep.MSVC` in winget, `ripgrep` in Scoop and Chocolatey). A name map in the repo (`packages/names.tsv`: omawin name → winget id, Scoop name, Chocolatey id, mise tool, Arch package) covers the common developer tools; anything else is found by search across backends, and you choose when it's ambiguous.

**How it fits the rest:**
- **The fast install** ([install-speed.md](install-speed.md)) uses the bundle backend for the first minute; `omawin pkg` is how you take over any of those tools with winget, Scoop or Chocolatey later (it replaces that plan's `omawin tools source` / `handover`, which become `omawin pkg move` and `omawin pkg prefer`). Your package manager's copy always wins on the PATH.
- **Presets** (`install/packages.ps1`) become lists of omawin names, installed through `omawin pkg`, so each preset works with whichever backend you prefer.
- **Update › All apps** (5.9) is `omawin pkg update`.
- **The Packages page** shows, per result, which backends have it and which you prefer, and marks what's installed and from where.
- **Linux**: `omawin pkg install <name> --linux` runs pacman in Arch (Omarchy's repositories, no AUR; 3.2) or apt in Ubuntu, so one command covers both sides.

**Prior art:** UniGetUI (formerly WingetUI) already puts one interface over winget, Scoop, Chocolatey, pip, npm and others; read its handling of ids, scopes and update detection before building (licence to check before borrowing any code). omawin's difference is a keyboard-first command and launcher page in the shell, with omawin's own defaults and the bundle as a backend.

**Risks:** each backend's quirks leak (winget's ids and its `--scope`, Scoop's shims, Chocolatey's admin and machine-wide installs, version strings that don't compare); the layer reports each backend's own error as is rather than hiding it, and `--from` always lets you go direct. It doesn't make installs faster by itself (each backend is as fast as it is); speed comes from the bundle and from running backends in parallel.

**Done when:** `omawin pkg install`, `remove`, `update`, `list`, `info`, `move` and `prefer` work across all three managers and mise; `omawin pkg list` matches what each manager reports; presets install through it; CI exercises winget and Scoop on a Windows runner (Chocolatey too, with admin).

## Phase 3: Linux in WSL, first class (Arch by default, Omarchy's way)

Linux is first class; Arch is the default Linux, Ubuntu the other choice. On Arch, omawin follows **all of Omarchy's package sources and decisions**: Omarchy's repositories on the stable channel (already: `wsl/setup.sh` writes Omarchy's `pacman.conf` and mirrorlist and trusts its key), Omarchy's package choices for anything that runs in a terminal, Omarchy's shell defaults, mise for languages, Docker the way Omarchy runs it, and **no AUR** (no `yay`, though Omarchy's base list has it). Your Linux is the default terminal and where your code lives. A distro you already had stays yours ([AGENTS.md](../AGENTS.md)).

What exists: `wsl/install.ps1` picks the distro (your `-Linux`, the saved pick, a distro you have, else Arch), installs it with `wsl --install -d <name> --no-launch`, makes a Linux user named after yours (sudo without a password), then runs `wsl/setup.sh root` and `setup.sh user`. Arch gets base-devel, the CLI tools, Neovim, Node, Python and herdr from Omarchy's repos; Ubuntu gets apt's equivalents plus Neovim and tree-sitter from their releases. `setup.sh user` links `~/.config/nvim` to the repo, adds an omawin block to `~/.bashrc` that sources `config/bash/bashrc`, points git at Windows' credential manager and installs the agents. `omawin update` runs `setup.sh update` (Arch only). `/etc/wsl.conf` gets only the default user. Linux projects show in the launcher, Linux servers in Localhost, and Neovim in Linux follows the theme live.

### 3.1 On by default (S)

The `wsl` preset joins the default set (`install/packages.ps1`, `Initialize-Presets`) with Arch preselected: Enter through the questions gives you Arch. WSL is already in the one admin prompt (`install/admin-batch.ps1:97-125`) and resumes at the next sign-in after Windows' restart (`wsl/install.ps1:51-65`).

**Risks:** virtualization off in the firmware. Detect it before offering (Try Omarchy does: [inspiration.md](../docs/inspiration.md)) and say "turn on virtualization" rather than looping; PowerShell stays the shell until then.

### 3.2 Omarchy's terminal layer in Arch (M)

**Why:** "all of Omarchy's decisions": the same packages an Omarchy user has in their terminal, chosen and kept current by Omarchy.

**Build:**
- **Packages from Omarchy's own list.** `setup.sh` reads Omarchy's `install/omarchy-base.packages` (from the `mjveilleux/omarchy` fork at a pinned commit) and installs what makes sense without a desktop. A small omawin file, `wsl/omarchy-skip.txt`, lists what to leave out: the desktop (hyprland, quickshell, sddm, uwsm, plymouth, xdg-desktop-portal-*), hardware (bluez, brightnessctl, networkmanager, cups, power-profiles, fcitx5), GUI apps (chromium, nautilus, obsidian, libreoffice, imv), fonts Windows draws, and `yay` (no AUR). Everything else comes: today bat, btop, docker (+ buildx, compose), eza, fastfetch, fd, fzf, git, gum, herdr, jq, lazygit, less, man-db, mise, nvim, ripgrep, starship, tldr, tmux, tree-sitter-cli, try, usage, yt-dlp, zoxide and more. A new terminal tool in Omarchy arrives on the next `omawin update` with no change in omawin.
- **Omarchy's shell defaults:** Omarchy's `default/bash/` (rc, aliases, functions, envs, inputrc) sourced in Linux, then omawin's `config/bash/bashrc` on top for what's ours (the theme, `OMAWIN`, Windows interop, 3B). Where they disagree, Omarchy wins unless there's a Windows reason, written next to the override.
- **Omarchy's configs** for terminal tools omawin doesn't configure itself (btop, lazygit, tmux): copied once from Omarchy's `config/<tool>` (its refresh-config pattern), themed by 3.8.
- **Neovim stays omawin's config** (`config/nvim/`, shared with Windows), not `omarchy-nvim`: one config on both sides.
- Ubuntu gets the nearest apt equivalents (as now), mise for what apt lacks.

**Files:** `wsl/setup.sh`, new `wsl/omarchy-skip.txt`, `config/bash/bashrc`.

**Done when:** a fresh Arch has every terminal package in Omarchy's base list except the skip list; `omawin check` compares the two.

**Risks:** a package pulling a desktop in as a dependency (check what `pacman -S --needed` brought); Omarchy's bash files assuming `$OMARCHY_PATH` (source the files directly, or set it to a folder with just them).

### 3.3 A good WSL VM out of the box (S)

`wsl/.wslconfig` today only sets `autoMemoryReclaim`. Add `networkingMode=mirrored` (localhost both ways, VPNs behave), `dnsTunneling=true`, `firewall=true`, `sparseVhd=true`, and a longer `vmIdleTimeout` (3.5). `setup.sh root` writes `/etc/wsl.conf`: `[boot] systemd=true` (Docker, timers), `[interop] appendWindowsPath=false` (Linux tools win in Linux), `[user] default=<user>` (as now). Each overridable by the account folder ([work.md](../docs/work.md)).

**Risks:** mirrored networking trips some VPN clients; `appendWindowsPath=false` breaks `code .` and `explorer.exe .`: small wrappers in `~/.local/bin` for `code`, `explorer`, `clip` and `omawin` (calling `omawin.exe` through interop).

### 3.4 The terminal opens in Linux (M, 🖥)

**Build** (`bin/omawin.cs`):
- `bin/defaults.txt` gains `shell | linux | Linux (WSL) | …` first (the default) and `shell | windows | PowerShell | … | pwsh.exe`. `Choices()` / `Current()` (`:236-257`) work unchanged; `Default()`'s installed check (`:311-312`) treats `linux` as "the picked distro is registered" (`DistroRegistered`, `:334-348`).
- `LaunchTerminal` (`:375-396`): with neither `--wsl` nor a new `--windows`, `Wsl = Current("shell").Name == "linux" && DistroRegistered(Distro)`. No Linux yet: PowerShell, silently.
- `TerminalHere` (`:1010-1072`): a Windows folder in front (Explorer, a PowerShell tab) opens Linux there (`wsl.exe --cd C:\…` maps it to `/mnt/c/…`); a Linux folder opens there (as now); no folder opens Linux's home.
- **OSC 7 from bash:** `omawin.cs:1000-1003` expects bash to report its folder, but `config/bash/bashrc` doesn't. Add it to `PROMPT_COMMAND` (`printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$PWD"`), so noctty's "the folder of the window in front" works for Linux windows.
- **No pre-started shell for Linux.** PowerShell windows are a hand-off to the running noctty with a spare `pwsh` kept warm (`+new-window`, `:581`; `windows-spare-shell`) only because PowerShell takes ~0.6 s to start. bash in a warm WSL starts in milliseconds, so Linux windows keep their own path (`--single-instance=false -e wsl.exe …`, `:589-597`) with no spare shell. What makes Linux fast is keeping WSL warm (3.5). `omawin profile terminal --wsl` confirms it; only if starting a noctty process per window turns out slow does the fork learn `+new-window -e <command>` (a hand-off, still no spare shell).
- `config/noctty/config.ghostty:6` (`command = pwsh.exe -NoLogo`) stays as the Windows shell; Linux windows pass their command.
- Windows Terminal: a profile per distro in `config/windows-terminal/`, `defaultProfile` from the pick.
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
- `omawin check` (today `bin/check.ps1:67-78` only checks the distro is registered): running, systemd up, Omarchy's repos in `pacman.conf`, the tools present, the default user, `.wslconfig` current.
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
2. **The prompt says where.** One `config/starship/starship.toml` for both: the `os` module first in `format` (Arch , Ubuntu , Windows ), coloured by side, and the prompt character's colour by side (from `OMAWIN_SIDE`, set by the PowerShell profile and the bashrc block). Works in every terminal, in herdr, over SSH.
3. **Window titles say where:** "arch · ~/src/app" or "pwsh · C:\…", from bash's `PROMPT_COMMAND` (with 3.4's OSC 7) and the PowerShell profile (`config/powershell/Microsoft.PowerShell_profile.ps1:120-136` already sets the title). The bar's window title and herdr's tabs show it too.
4. **The window border by side** (fork, L): GlazeWM window rules gain a per-window border colour (matching the title prefix), so the tiling layout itself says which windows are Linux. GlazeWM already sets Windows 11's DWM border colour (`window_effects.*.border`).

**Done when:** a screenshot of a Linux and a PowerShell terminal side by side is obvious at a glance, in a dark, a light and a high-contrast theme.

**Risks:** light themes need the tint to go darker (mix toward the accent, then keep contrast); some TUIs reset the background on exit, so it's re-applied on each prompt.

---

## Phase 4: plugins (Omarchy's shell, on Windows)

Quickshell is Qt on Wayland and won't run on Windows, but its plugin model will. We keep **Omarchy's manifest format** and draw with our own shell, so an Omarchy plugin's idea (and much of its manifest and data) carries over; its QML doesn't.

What exists: the bar reads its layout from `zebar/tokyo-islands/bar.json` (`Bar/Bar.cs:21, 150-163`: `left`, `centerBegin`, `center`, `centerEnd`, `right`); each widget is a class chosen by a switch on its name (`Bar/Widgets.cs:35-49`); a `BarWidget` base with `Attach`, `Update(BarData)` and `Paint(Theme)`; data pushed from background threads to the UI thread (`Bar.cs:168-235`). The menu reads only the repo's `default/launcher/menu.txt` (`MenuModel.cs:64`), while `items.txt`, `sites.txt` and `keys.txt` are layered (repo, shared folder, account folder; `MenuModel.cs:57-61`). Theme colours are hex strings on a `Theme` object, brushes made in code; no XAML resources. `XamlReader.Load` isn't used yet. Requests are joined and split on spaces (`App.xaml.cs:114-128`), so arguments with spaces don't survive.

### 4.1 The manifest (S)

**Started 2026-10-30:** the manifest model, the manifest check and the JSON-lines protocol are in (`shell/menu-app/Plugins/Manifest.cs`, `ManifestCheck.cs`, `Protocol.cs`; pure C#, [05-plugins.md](05-plugins.md) §2.1-2.2, §3.1), with tests in `test/menu-app.Tests/Plugins/`. Built as a real Native AOT binary with the trimming and AOT analysers on: no warnings, and it runs (manifests round-trip, messages parse and write). Next: the registry and plugin processes (§2.4, §3.2-3.4), which need Windows.

A plugin is a folder (a git repo) with `manifest.json` in Omarchy's schema (`shell/services/PluginRegistry.qml`): `schemaVersion` (1), `id`, `name`, `version`, `author`, `description`, `kinds` (`bar-widget`, `panel`, `menu`, `service`), `entryPoints`, `keepLoaded`, and the `barWidget` block (`displayName`, `category`, `allowMultiple`, `defaultSection`, `defaults`, settings `schema`). An `omawin` block holds what's ours (`"command": "widget.exe"`, `"xaml": "Panel.xaml"`). [`plugins.md`](plugins.md) has the full design (protocol, panels, safety, commands), with Omarchy's names kept so an Omarchy plugin author recognizes it.

### 4.2 Widgets and services as commands (M, 🖥)

The menu app is Native AOT, so it can't load plugin DLLs. A widget or service is a program or script that prints **JSON lines** (Waybar's format, which Omarchy's `type: "command"` modules use: `text`, `tooltip`, `class`, `percentage`, `alt`), once per `interval` or continuously. Clicks, right-clicks and the wheel run the manifest's commands with the widget's settings.

**Build:** `Bar/CommandWidget.cs`, a `BarWidget` owning a process, reading lines off the UI thread, drawing `text` (Nerd Font glyphs) in the theme's colours by `class`; `BarWidget.Create` falls back to the plugin registry for unknown names. A plugin process restarts with backoff if it dies, ends with the shell, and shows in `omawin profile ram`.

### 4.3 Panels as XAML (M, 🖥)

A panel (a drop-down from a widget, or a launcher page) is a loose `.xaml` file the shell loads with `XamlReader.Load`, bound to the JSON state the plugin's command prints. Buttons call the plugin's commands.

**Build:**
- **Theme resources:** the shell puts the theme into `Application.Resources` as brushes with fixed keys (`OmawinBackground`, `OmawinSurface`, `OmawinAccent`, `OmawinForeground`, `OmawinMuted`, … from `MenuModel.cs:14-33`) and updates them on a theme switch; plugin XAML uses `{ThemeResource OmawinAccent}`. Our own pages can move to them too.
- `shell/menu-app/Plugins/PanelHost.cs`: loads, sizes and places a panel under its widget, with the motion tokens (1.4).
- **Prove it under AOT first:** WinUI's XAML parser is native, so `XamlReader.Load` should work trimmed; bindings need a reflection-free path. A sample panel decides; if it can't bind, the fallback is a declarative JSON format (rows, toggles, sliders, lists) drawn by the shell.

### 4.4 Menu entries (S)

A plugin's `menu.txt` fragment (same row format, `MenuModel.cs:107-142`) goes after omawin's rows, under an id prefix the plugin owns. While here, **layer `menu.txt` itself** like `items.txt` (shared folder, then account folder).

### 4.5 Where they live, and `omawin plugin` (M)

- `~/omawin-account/plugins/<id>/` (this account) and `C:\ProgramData\omawin\shared\plugins\<id>/` (every account); never the repo.
- `bin/commands.txt`: `plugin add <git-url>`, `plugin update [id]`, `plugin remove <id>`, `plugin enable|disable <id>`, `plugin list`. New plugins arrive **disabled**, as in Omarchy; `%LOCALAPPDATA%\launcher\plugins.txt` lists the enabled ones.
- Hot reload: a `FileSystemWatcher` per plugin folder (the pattern in `Background.cs` and `Bar.cs:347-356`), debounced.
- `bar.json` moves out of `zebar/` to the account folder (the repo's as the default), as Omarchy's `shell.json`, so plugins can be placed.

### 4.6 Our own pieces become plugins (M)

The music drop-down (Zebar's web page until 2026-10-31, gone with Zebar), the agents panel, dictation and reports move to the format first: that proves it and keeps the core small. `omawin-music watch` already prints lines; it becomes the widget's command.

**Decided 2026-10-30: the music player is written as a Quickshell plugin** (QML, Omarchy's `manifest.json`), the first one, so the same plugin runs in Omarchy and in omawin's QML host (4.9, `omawin-qs.exe`). Its UI is QML; `omawin-music` (C#: mpv, yt-dlp, feeds, ratings) stays its service, spoken to over `Process` and JSON lines. It leaves the presets: it's picked in the plugin selector (Install › Plugins, `omawin plugin add music`), which brings yt-dlp and mpv with it. Order: the QML host's core modules (4.9.4) first, then the music plugin as its proof.

**Built 2026-10-31:** [`shell/plugins/omawin.music/`](../shell/plugins/omawin.music/README.md): Omarchy's manifest, a `BarWidget.qml` (the glyph and title; it runs `omawin-music watch` and answers Win+M), a `Panel.qml` (cover, seek, controls, shuffle, stars, volume, and Music / Podcasts / Radio tabs) on Omarchy's own `qs.Ui` components, and `MusicModel.js` for the watch's lines. Its QML parses (`qmlformat`); a test checks every shipped plugin's manifest (`ShippedPluginsPassTheCheck`). Not yet run: it needs the QML host's core (4.9.4). Picked with `omawin plugin list|add|remove` (Install › Plugins): a plugin's programs are a preset tagged with it (`Plugin` in `install/packages.ps1`), so turning it on installs them, and PCs that had the `music` preset have it on already.

### 4.7 IPC for plugins and scripts (S)

`omawin shell <method> [args]`: `toggle <panel>`, `reload`, `list-plugins`, `put-bar-widget <id> <section>`, `notify` (5.5), like Omarchy's `omarchy-shell`. Fix the request format while here: one argument per line in the request file instead of space-joined (`App.xaml.cs:114-128`, `omawin.cs:189-204`).

### 4.8 Script commands (S)

From Raycast and PowerToys' Command Palette: a script dropped in `~/omawin-account/scripts/` (PowerShell, bash for Linux, anything with a header comment for its name and icon) shows up in the launcher, no manifest needed. The smallest possible plugin.

### 4.9 Omarchy plugins unchanged (L)

**Why:** Omarchy's marketplace has thousands of plugins. Porting each one by hand (4.1-4.8) means rewriting its UI; running them as they are gives omawin the whole catalog at once. It's possible because **Windows runs QML**: QML is part of Qt, which is cross-platform. What doesn't run on Windows is Quickshell, the Linux program that hosts Omarchy's QML and gives it Linux-only modules (`Quickshell` for edge-pinned windows, `Quickshell.Io` for processes and files, `Quickshell.Hyprland`, system services). A plugin is ordinary QML plus a few of those modules, so omawin provides the same module names on Windows and the plugin's QML loads unchanged. Four community plugins checked (2026-10-30) import only Qt Quick, Omarchy's `qs.Commons` / `qs.Ui`, `Quickshell` and `Quickshell.Io`, and two of them `Quickshell.Hyprland` ([plugins.md](plugins.md), "Running Omarchy plugins unchanged").

**Scanned 2026-10-30** (the 30 most-starred Developer Tools plugins, [plugins.md](plugins.md)): 4 run as is, 11 with the command stand-ins, 9 with extra host modules, 5 run but describe Linux rather than Windows; none use Quickshell's system services. So the host also provides `Quickshell.Wayland` (layer settings → a topmost, non-activating window) and `Quickshell.Widgets`, and the stand-ins cover the `omarchy-launch-*` family, `notify-send` and `wl-copy` / `wl-paste`. The gallery (4.10) reads Omarchy's own `catalog.json` and adds each plugin's scan verdict.

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

**Built 2026-10-31 (step 4, first slice):** [`src/qs-host/`](../src/qs-host/README.md), in Rust with cxx-qt (not C++): `Quickshell` (`execDetached`, `env`), `Quickshell.Io` (`Process` with `SplitParser` / `StdioCollector`, `FileView`), and `qs.Commons` / `qs.Ui` as omawin draws them, reading omawin's palette. The bar keeps a cell for each plugin that's on (`shell/menu-app/Bar/PluginSlot.cs`) and the host draws the widget over it; it ends with the bar (`--parent`). Built by `release.yml`'s `qs-host` job (Qt 6.8, `windeployqt`; the music plugin loaded with `--check` as its test) and shipped as an optional tools bundle entry, downloaded only when a plugin is turned on. The music plugin is its first: widget and panel render (checked on Linux; the Windows run is CI's `--check`). Still to do: `PanelWindow` / `FloatingWindow`, `Quickshell.Hyprland`, `IpcHandler`, the JSON-lines protocol, hot reload, and other monitors' bars.

**Files:** `src/qs-host/` (Rust and QML), `.github/workflows/release.yml` (a job building it), `install/software.ps1` (an `optional` bundle entry: downloaded only when a QML plugin is enabled), `shell/menu-app/Bar/` (slots for QML widgets), `wsl/omarchy-shims/`.

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
- **5.4 The key cheat sheet from `config/glazewm/config.yaml`** (M): each binding gets a description comment in a fixed form (`# desc: Open the launcher`); the Shortcuts page (`Pages.cs`) reads them instead of `default/launcher/keys.txt`; Enter runs the binding's command. Omarchy builds its cheat sheet from bind descriptions the same way.
- **5.5 Notifications and the OSD in the shell** (M, 🖥): `AppNotificationManager` toasts for omawin's own events (install finished, update available, reminders, low battery) and `omawin notify <text>`; a volume / brightness OSD drawn by the shell (Windows' flyout as the fallback).
- **5.6 Idle lock and screensaver** (S): Omarchy's defaults (screensaver 150 s, lock 300 s): `omawin toggle idle-lock`, Windows' own lock (`LockWorkStation`) after idle (`GetLastInputInfo`), skipped while stay awake is on or media plays.
- **5.7 Window manager parity** (fork):
  - **Scratchpad** (M): a workspace that's never displayed (a workspace flag `special: true`; `Workspace::is_displayed()` is true only for the monitor's first in focus order, `models/workspace.rs:67`) and `toggle-scratchpad`, which shows its windows floating and centred over the current workspace. Omarchy's Super+S.
  - **Window groups / tabs** (L): `feat/accordian-v1` (accordion directions on `SplitContainer`, a hardcoded 30 px title offset, a z-order hack; 180 commits behind) is a reference, not a base: redo it on `omawin`, with the tab strip drawn by the shell from IPC.
  - Layout cycling, pop-out (float and centre), corner style and transparency toggles in the menu (GlazeWM's `window_effects` supports both).
  - Upstream's `feat/master-stack` and `feat/auto-tiling` (both WIP) are worth watching for Omarchy's master layout.
- **5.8 Detect before offering** (M): menu rows with a condition (`?wsl`, `?docker`, `?installed <id>`), answered in the background and cached; the menu opens on the last answers, as Omarchy's guards do (`docs/menu.md`).
- **5.9 Update channels and undo** (M): `omawin update` follows release tags (stable, edge) with a restore point first; `omawin uninstall` does what [install.md](../docs/install.md) lists by hand. Update › All apps (`default/launcher/menu.txt:257`, only `winget upgrade --all` today) also runs `scoop update *` and `choco upgrade all` when those are installed (Chocolatey's needs admin: one prompt, said up front).
- **5.10 Coding agents first class:** install, sign in, a key for the default agent, the theme followed (mission-todo track 7).
- **5.11 From Raycast and the Command Palette** (2026): a window-capture action in the snip tool (a window's image with its shadow); pinnable CPU / GPU / network readouts on the bar (as Command Palette's dock has); dictation in the launcher (omawin has dictation; a launcher row to start it).

---

## Phase 6: files (the finder, Win+F)

The finder on fff is already the file explorer you use: fuzzy search over your folders and repos, a preview, folder and image chips, Ctrl+Enter to show a file in its folder. It grows into the place you act on files from. Explorer stays (replacing it breaks Windows; [shell-apps.md](../docs/shell-apps.md)).

What exists: fff v0.11.0's C library (`shell/menu-app/fetch-fff.ps1:10`), one index per root, started in the background (`FileSearch.cs:44-85`); roots are Desktop, Documents (and OneDrive's), Downloads, `~/repos`, `~/source/repos` and git repos in your home folder; `default/launcher/finder-skip.txt` skips folders; results ranked by own-name match, folders first, then fff's score, with opened files tracked (`fff_track_query`). Bound functions: `fff_create_instance_with`, `fff_search_mixed`, `fff_track_query` and result accessors; content indexing exists in the options (`EnableContentIndexing`) but is off, and no grep call is bound. The preview (`MainWindow.xaml.cs:743-844`) shows images, the first 22 lines of text, or a folder's first 18 entries. Keys: Tab cycles chips, Enter opens, Ctrl+Enter reveals, Esc clears or goes back.

- **6.1 Actions on a result** (M, 🖥): Ctrl+K (or →) opens an action list on the selected row: open in the editor (`omawin launch editor`), a terminal there (`omawin launch terminal --dir`, Linux or Windows by the file's side), open as a project (herdr), copy path, copy the path relative to its repo, copy the file itself (`DataPackage.SetStorageItems`: pastes into Explorer, Teams, mail), rename inline, send to the Recycle Bin, properties. The common ones get keys of their own (proposed: Ctrl+E editor, Ctrl+T terminal, Ctrl+C copy path), confirmed before binding.
- **6.2 Browse, not just search** (M, 🖥): → on a folder lists its contents (folders first), ← goes up, the preview beside it, the query filtering the current folder; yazi's model, for when you know roughly where something is.
- **6.3 Search inside files, as a finder mode** (M): part of the finder, not a separate tool: a **Contents** chip next to All / Folders / Images (Tab reaches it), or a `#` prefix, switches the same window from names to contents, with the same roots, chips, preview and actions. Results are by file and line, the preview shows the matching lines highlighted, and Enter opens the editor at that line (`nvim +<line>`). fff already has the start of it: its options carry `EnableContentIndexing` (off today, `FileSearch.cs:191, 210-224`). First check fff v0.11's C API for a grep call (its upstream `fff.h`; fff.nvim has live grep); if there is one, turn indexing on and bind it; otherwise `rg --json` (ripgrep is in the core) in the chosen root, streamed into the list. Indexing contents costs memory and disk, so it's on only for the roots you pick (code repos by default, not Downloads).
- **6.4 Better previews** (M): code in the theme's colours (tree-sitter's `highlight`, or a small tokenizer per common language), Markdown drawn, a PDF's first page (`Windows.Data.Pdf`), CSV as a table, Office files and videos by Windows' own thumbnails (`IShellItemImageFactory`); all off the UI thread (the `previewToken` pattern exists).
- **6.5 Filters and places** (S): `.pdf`, `today`, `this week`, `>10mb` as filter words; your own places in `default/launcher/finder-places.txt` (layered: shared, account); Linux's files (3.7); pinned favourites at the top of the empty list.
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

`omawin agent sandbox [claude|codex|opencode]`: opens the agent inside Linux (Arch) in the current project, with its sandbox on (Claude Code's sandboxing works in WSL2; `failIfUnavailable` so it never silently runs open), the project folder the only writable place. On native Windows, Codex's own sandbox where the user picks Codex. A launcher row and a herdr tab option (`default/launcher/project-tabs.txt`).

### 7.4 Sandbox screenshots as acceptance tests (later)

Omarchy runs graphical acceptance tests in a disposable VM (`agents/skills/acceptance-tests.md`). Ours: 7.1's sandbox run, plus scripted checks inside (the menu app's `--snip-selftest` and `--qs-selftest`, the bar's preview mode), screenshots compared by eye per release.

### 7.5 Real Omarchy, when you want it (S)

A launcher item and a short manual page: "Try real Omarchy" installs Try Omarchy for Windows (its signed launcher) for someone who wants Hyprland itself. Omawin and Omarchy side by side, credited.

---

## Phase 8: fixing Windows

The second half of the mission: Windows has good APIs under a poor experience. Each item below takes a daily Windows annoyance and builds the better version on an API Windows already offers, drawn by the shell (keyboard first, themed, searchable, every action an `omawin` command). Ordered roughly by how often the annoyance bites a developer. **Undocumented** marks APIs that aren't officially supported (used by well-known tools, but they can change); those get a fallback and a `minBuild`.

### 8.0 Where each fix lives, and how it's written

Not everything should be a plugin, and not everything belongs in the shell. Each fix has one of three homes.

**The rule**, in order (the first that applies decides):
1. **On a key, or needing one of the shell's shared hooks** (the keyboard hook, the clipboard listener, the notification listener, Explorer's selection) → **core**, in the menu app. A key must answer in one frame (phase 1), a plugin process can't share the shell's hook, and two keyboard hooks fighting is worse than one.
2. **A setting Windows already has, changed by a value** → **a settings catalog entry** (1B): one line of data, no code, with undo and `omawin setting`.
3. **A page, a panel, a launcher source or a background service you could live without** → **a first-party plugin**: in the repo, written to the plugin protocol ([plugins.md](plugins.md)), started only when used, turned off with one command. Keeps the core small, crashes contained, memory spent only by what you use, and it proves the plugin system on real work (Omarchy built its own bar, menu and panels as plugins the same way).

Shared building blocks that several features need (who holds a file, display layouts, the event log readers) live in a small library both the core and first-party plugins use, so nothing is written twice.

**Where each item goes:**

| Item | Home | Why |
|---|---|---|
| 8.1 Who holds a file | core library + command | the finder's actions need it instantly; `omawin who-has` for scripts |
| 8.2 Whole-disk search | first-party plugin (service + finder source) | an admin service most people may not want; adds an *Everywhere* chip to the finder |
| 8.3 Disk cleanup | first-party plugin (page) | an occasional page |
| 8.4 Quick Look in Explorer | core | Space in Explorer must answer instantly; needs the keyboard hook |
| 8.5 Window switcher | core | on a key |
| 8.6 Picture-in-picture | first-party plugin (own window) | self-contained; a window of its own |
| 8.7 Monitor profiles | core | reacts to every dock and undock; the bar already watches monitors |
| 8.8 Wallpaper per monitor | core (themes) | part of a theme switch |
| 8.9 Keyboard lights | first-party plugin (service) | only for people with such a keyboard |
| 8.10 Per-app volume and output | core (the Audio page) | the Audio page is core (`Audio.cs`) |
| 8.11 Microphone mute | core | on a key; a bar indicator |
| 8.12 Key remaps | core (hook) + catalog (`Scancode Map`) | the shell's one keyboard hook; sign-in-screen remaps are a setting |
| 8.13 Text snippets | core | the same hook |
| 8.14 Clipboard | core | the clipboard listener exists (`Background.cs`) |
| 8.15 Why did my PC restart / crash / wake | first-party plugin (page) | an occasional page |
| 8.16 Startup list | first-party plugin (page) | an occasional page |
| 8.17 Processes in the launcher | first-party plugin (launcher source) | a search source, not on a key of its own |
| 8.18 Windows Update | catalog entries (active hours, pause) + first-party plugin (status page, bar indicator) | the settings are values; the status is a service |
| 8.19 DNS, hosts, proxy | catalog entries (proxy, DNS presets) + first-party plugin (hosts editor) | values where possible |
| 8.20 Connections per app | first-party plugin (launcher page) | an occasional page |
| 8.21 Wi-Fi QR | core (Quick settings) | Quick settings is core |
| 8.22 Notification history | core | the listener needs the shell's package identity and toasts |
| 8.23 Copilot key | core | on a key |
| 8.24 Classic right-click menu | catalog entry | one value |
| 8.25 Files opened by type | core (forwarders to the shell) | the shell's viewer |

8K (idle memory) cuts across homes: mostly measurement, omawin's own parts, and catalog entries. Count for 8.1-8.25: 12 core, 9 first-party plugins (some with catalog entries alongside), 3 catalog-only (plus the catalog parts of 8.12, 8.18 and 8.19). Items 8.26-8.43 (below) are almost all catalog entries, a few with an admin helper verb (8.32, 8.34) or a core page (8.35, 8.36); the Health additions (8.41) go in 8.15's plugin.

**How first-party plugins are written:**

- **C#, compiled with Native AOT into a small exe**, like the menu app: no runtime to install, starts in ~50 ms, ~15-30 MB while running. One project per plugin: `plugins/<id>/` holds `manifest.json`, `src/<Id>.csproj`, and `Panel.xaml` when it has a panel.
- **A plugin SDK**, `plugins/sdk/Omawin.Plugin` (a small library each plugin references): the JSON-lines protocol (read `init` / `action` / `settings`, write bar lines, `state`, `notify`, `log`), settings with defaults from the manifest, a data folder, logging, and a `--selftest` entry point. Win32 calls declared with the **CsWin32** source generator (typed, AOT-safe P/Invoke from Microsoft's metadata), so `RmGetList`, `EventLogReader`, `GetExtendedTcpTable` and the rest are one line each.
- **The shared library**, `shared/Omawin.Windows` (also referenced by the menu app): the building blocks used by both, such as Restart Manager, display configuration, event log queries, so the core and plugins share code, not copies.
- **Built and shipped by CI**: the release workflow builds each first-party plugin for x64 and arm64 and publishes it as an asset; a plugin is downloaded when it's first enabled (as the menu app is), so the default install stays lean. Checked against `SHA256SUMS.txt`.
- **Admin, safely, and rarely** (2.9): a plugin never elevates itself. Admin work (DNS, the hosts file, installing the disk index) is a fixed list of verbs (`dns set`, `hosts write`, `usn-index install`, …) the plugin requests with `{"type":"admin","verb":…}`; the shell shows what will change, and an approved batch runs as one short elevated process that then exits. No standing admin service.
- **New plugin kinds** these need, added to [plugins.md](plugins.md): `page` (a launcher page, like Localhost or Audio), `launcher-provider` (rows for a query: `top`, connections), and `finder-source` (8.2's index as a finder chip).
- **Tested in CI** on a Windows runner: each plugin's `--selftest` (the Restart Manager on a file the test locks itself, an event log query, a TCP table read), the SDK's protocol tests, and `omawin plugin check` on every manifest.
- **On by default or not**: the occasional pages (8.3, 8.15, 8.16, 8.20) are on, since they cost nothing until opened; services with a cost (8.2's index, 8.9's lights) are off until you turn them on, from the Plugins page.

### 8A. Files and disks

- **8.1 "This file is in use" answered** (S). Windows refuses to delete or rename a locked file and doesn't say who holds it. The **Restart Manager API** (`RmStartSession`, `RmRegisterResources`, `RmGetList`) names the processes holding any file or folder. In the finder's actions (6.1) and as `omawin who-has <path>`: the processes, with *close* (politely, `WM_CLOSE`) and *end* (after confirming). Also when a delete fails in Explorer: a launcher row "Who has …" on the path in the clipboard.
- **8.2 Instant whole-disk search** (M, admin once). Windows Search is slow and incomplete. The **NTFS USN change journal** (`FSCTL_ENUM_USN_DATA`, `FSCTL_READ_USN_JOURNAL`) lists every file name on a drive in seconds and keeps up with changes (how Everything works). A small service (admin, installed in the one admin prompt) keeps a name index the finder searches as an *Everywhere* chip beside fff's roots. Then Windows' own indexer can be switched off (a catalog setting, 1B), saving CPU and disk.
- **8.3 Reclaim disk space a developer loses** (M). Windows' cleanup misses where developers' space goes. A *Disk* page: the biggest folders (from 8.2's index, or a fast `FindFirstFileEx` walk), and dev caches by name with their size and a clean action: `node_modules`, `target`, `.venv`, `bin/obj`, the npm / pnpm / Bun / NuGet / cargo / pip / uv / Go caches, Docker's and **WSL's virtual disks**, which never shrink on their own (`wsl --manage <distro> --set-sparse true`, or compaction after `wsl --shutdown`). Never deletes outside caches without asking.
- **8.4 Quick Look in Explorer** (M, 🖥). Press Space on a file in Explorer and see it, as on a Mac. The selected item comes from Explorer's own automation (`IShellWindows` → the window's `IShellView` / `IFolderView2` selection); the preview reuses the finder's (6.4), with Windows' **preview handlers** (`IPreviewHandler`, what Explorer's preview pane uses) for Office files and PDFs. A keyboard hook passes Space through everywhere except Explorer's file list.

### 8B. Windows and the desktop

- **8.5 A window switcher that finds things** (M, 🖥). Alt+Tab shows thumbnails of everything, in no useful order. A fuzzy switcher in the shell: every window from GlazeWM's IPC (title, app, workspace), typed to filter, Enter to focus; live thumbnails with the **DWM thumbnail API** (`DwmRegisterThumbnail`, no capturing). Bound only once a key is confirmed (Alt+Tab itself stays Windows' unless you choose).
- **8.6 Picture-in-picture for any window** (S). Keep an eye on a build, a call or a video while working: a small always-on-top window mirroring another window, or a region of it, live, through the same **DWM thumbnail API**. `omawin pip` on the focused window; GlazeWM floats it.
- **8.7 Monitor profiles that follow the dock** (M, 🖥). Docking and undocking scrambles resolutions, scaling, layout and the primary monitor. `QueryDisplayConfig` / `SetDisplayConfig` save and restore a whole layout per set of monitors (identified by their EDID names); applied by itself when the monitors change (the bar already notices, `Bar/Bar.cs`), with **DDC/CI** (`SetVCPFeature`, already used for brightness) for an external monitor's input source and contrast.
- **8.8 Wallpaper per monitor, from the theme** (S). `IDesktopWallpaper` sets a different picture per monitor and the slideshow; the theme's backgrounds spread across monitors instead of one stretched or repeated.
- **8.9 The keyboard's lights follow the theme** (S, 🖥). Windows 11's **Dynamic Lighting** API (`Windows.Devices.Lights.LampArray`) drives RGB keyboards and mice of any brand that supports it; omawin paints them in the theme's accent on a theme switch (as an ambient controller, which Windows lets the user pick in Settings).

### 8C. Sound, input and the clipboard

- **8.10 Per-app volume and output** (M). Windows' volume mixer is buried. The Audio page (Win+A) gains each app's own volume and mute (**`IAudioSessionManager2`**, documented) and which speaker it plays to (**undocumented** `IAudioPolicyConfig`, as EarTrumpet does; fallback: open Windows' per-app page).
- **8.11 A real microphone mute** (S). One key that mutes every microphone (`IAudioEndpointVolume::SetMute` on the capture devices), with a red indicator in the bar while muted and a toast when an app tries to listen (the privacy log `voice-meter` already reads). Key confirmed before binding.
- **8.12 Keys the way you want them** (M). Caps Lock as Escape or Control, a Hyper key, swapped keys, without PowerToys: the menu app's low-level keyboard hook for live remaps (no restart), or the registry's `Scancode Map` for remaps that must work at the sign-in screen (admin, restart). In the settings catalog (1B) as choices.
- **8.13 Text snippets** (S). Type `;sig` or `;date` anywhere and it expands (`SendInput`, triggered by the same hook); snippets in the account folder.
- **8.14 A clipboard that respects secrets and does more** (S). Password managers mark what they copy (the clipboard formats `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory`, which Windows' own history honours); omawin's history honours them too. Plus images in the history, pins, and transforms on paste: plain text, JSON pretty, Base64, URL encode / decode, a timestamp to a date.

### 8D. What's going on with my PC

- **8.15 Why did my PC restart, crash or wake up?** (M). Windows records it all and shows none of it. A *Health* page from the event logs (`EventLogReader`): unexpected restarts (Kernel-Power 41) and blue screens (BugCheck 1001), who restarted it and why (User32 1074: often Windows Update), app crashes (Application Error 1000) by app, what woke it from sleep (`powercfg /lastwake`, Power-Troubleshooter 1), slow boots and which driver or app slowed them (Diagnostics-Performance 100-110), overnight battery drain (`powercfg /sleepstudy`). Plain sentences, newest first.
- **8.16 Everything that starts with Windows, in one list** (M). Startup is scattered across Run keys, the Startup folder, scheduled tasks and services. One page lists them all with a switch each (the same `StartupApproved` values Task Manager uses for Run entries; disabling, never deleting, for tasks and services), what each costs at boot (8.15's boot events), and undo.
- **8.17 A process list in the launcher** (S). Type `top` (or a process name): processes by CPU and memory, grouped by app, with the ports they listen on (Localhost has this already) and the files they hold (8.1); end with confirmation. Faster than opening Task Manager.
- **8.18 Windows Update without surprises** (M). Restarts at bad moments are the oldest complaint. The **Windows Update Agent API** (`IUpdateSession`, `IUpdateSearcher`) says what's pending and whether a restart is needed; a bar indicator when it is; the menu sets active hours, pauses updates for a week, and schedules the restart (documented update policies, undo included). Nothing hidden from Windows Update; it just stops surprising you.

### 8E. Network

- **8.19 DNS, hosts and proxy from the menu** (S, admin). DNS per network adapter (`SetInterfaceDnsSettings`, Windows 10 2004+), with presets (your router, Cloudflare, Quad9) and DNS over HTTPS; a hosts file editor that adds and comments out lines (backed up first); the system proxy on / off (1B). Admin changes in one prompt.
- **8.20 Which app is talking to the internet** (M). Connections per process (`GetExtendedTcpTable` / `GetExtendedUdpTable` with owning process ids), names resolved, refreshed live; a launcher page beside Localhost.
- **8.21 Share Wi-Fi with a QR code** (S). The saved network's password (`WlanGetProfile` with the plain-key flag, for the signed-in user) drawn as a QR code in Quick settings (mission-todo's "QR to share a network").

### 8F. Windows' own surfaces, done better

- **8.22 A notification history you can search** (L, 🖥). Windows' notification centre is a pile. With package identity (the sparse package, [shell-apps.md](../docs/shell-apps.md) decision 7) and your consent, **`UserNotificationListener`** reads every notification: the shell keeps a searchable history, groups by app, and does what Omarchy's notifications do, like spotting **verification codes** and offering to copy them, and opening links.
- **8.23 The Copilot key opens the launcher** (S). The Copilot key sends Win+Shift+F23; the keyboard hook takes it for the launcher (or anything you choose), as [shell-apps.md](../docs/shell-apps.md) notes.
- **8.24 The full right-click menu back** (S). Windows 11's shortened context menu hides most entries behind *Show more options*; the classic menu returns with one documented-in-practice registry key (a catalog switch, 1B; Explorer restarts).
- **8.25 Files and folders opened by type go to omawin** (later). Images, `.md`, `.json`, `.log` opened in the shell's own viewer (tiny forwarder exes, [shell-apps.md](../docs/shell-apps.md) decision 6); defaults set by you once, since Windows forbids setting them by script (UCPD).

### 8G. Faster and calmer by default

omawin's install already turns off Windows' ads and suggestions, OEM upsells and Copilot's background process (`bin/no-ads.ps1`). What's left is speed, distractions and conflicts with omawin itself. **The principle that sets this apart from the many "debloat" scripts: every tweak is measured, explained and reversible.** Each is a settings catalog entry (1B) marked *recommended*, with a sentence saying what it does and why; `omawin tune-up` shows what's applied, `omawin profile login` and `omawin profile ram` (exist) show the effect before and after, and anything that can't show a difference isn't shipped. No placebo tweaks (HPET, "network throttling", registry "speed-ups" from forum posts), and nothing that breaks Windows Update, Defender or the Store.

- **8.26 Windows stops fighting the tiling manager** (S, catalog, on by default). Snap Layouts' flyout on the maximize button and dragging to edges (`EnableSnapAssistFlyout`, `WindowArrangementActive`), Aero Shake, and Windows' own Win+key shortcuts that clash with GlazeWM's (`DisabledHotkeys`, Explorer restart; [shell-apps.md](../docs/shell-apps.md)). GlazeWM tiles; Windows shouldn't also try.
- **8.27 Snappier menus and switching** (S, catalog). Classic menus open after 400 ms by default (`MenuShowDelay` → 100); Alt+Tab shows Edge's tabs as windows (`MultiTaskingAltTabFilter` → windows only); minimize / maximize animations off where GlazeWM animates instead.
- **8.28 Less running in the background** (M, catalog). Edge's startup boost and running after it's closed (Edge's per-user policies), Widgets and its board, the Game Bar's background recording (`GameDVR`), Phone Link, Teams' and OneDrive's start at sign-in (asked, never removed: they may hold your files), per-app background permission for Store apps you don't use, Delivery Optimization uploading updates to other PCs, automatic maintenance waking the PC at night on battery. Each shown with the memory or wake-ups it was costing.
- **8.29 Accidents that don't happen** (S, catalog). Alt+Shift switching the keyboard layout mid-sentence, the Narrator shortcut (Win+Ctrl+Enter), Sticky Keys' and Filter Keys' prompts, the "let's finish setting up your device" screen after updates (partly done), Recall and other AI features that record the screen (their policies), Spotlight's "learn about this picture" icon.
- **8.30 Sleep that doesn't drain the battery** (S, catalog). Modern Standby can drain a laptop overnight: hibernate after a chosen time asleep (`powercfg` standby-to-hibernate), wake timers off on battery, fast startup off (it keeps the kernel's uptime and driver state across "shut down", which confuses drivers and dual boot). 8.15 shows the drain before and after.
- **8.31 Windows' file indexer off** once the launcher, the finder and whole-disk search (8.2) replace it: less CPU and disk activity, especially after big git checkouts.

### 8H. Developer papercuts

- **8.32 Dev Drive for code and caches** (M, admin once, 🖥). Microsoft's **Dev Drive** (ReFS, with Defender's performance mode, which scans asynchronously) speeds up builds and installs noticeably on Windows. omawin offers to create one (a VHD of a chosen size, or a spare partition; `Format-Volume -DevDrive`), puts `~/src` and the package caches on it (npm, pnpm, Bun, NuGet, pip, uv, cargo, Go, mise, through their environment variables), and measures a build before and after. Existing code is never moved without asking.
- **8.33 Git as fast on Windows as on Linux** (S, the `shell` step). Git for Windows is slow on big repos without its Windows-specific settings: `core.fscache`, `core.preloadindex`, `core.untrackedCache`, and Git's built-in **file system monitor** (`core.fsmonitor`, a daemon that watches the working tree so `git status` doesn't walk it). In omawin's git includes; `git status` timed in a big repo to prove it.
- **8.34 "Port already in use" that isn't** (S, admin). Hyper-V and WSL reserve random ranges of ports at boot (`netsh int ipv4 show excludedportrange protocol=tcp`), so a dev server on 3000 or 8080 fails for no visible reason. omawin moves Windows' dynamic port range out of the common dev ports and reserves them (`netsh int ipv4 set dynamicport` / `add excludedportrange`), through the admin helper; Localhost (exists) explains a blocked port when it sees one.
- **8.35 A PATH doctor** (S). Windows' PATH collects duplicates, folders that no longer exist, a system PATH that silently beats the user's (the mise shims problem, [dev-envs.md](../docs/dev-envs.md)), and a 2,047-character limit for some programs. `omawin path` lists both PATHs, flags each problem, says which `node` / `python` / `git` wins and why, and fixes with undo.
- **8.36 An environment variables editor in the menu** (M). Windows' dialog is from 1995. A launcher page: search variables (user and system), edit inline, PATH as a list you reorder, admin for system ones in one prompt, and `WM_SETTINGCHANGE` so new terminals see it at once.
- **8.37 UTF-8 everywhere** (S, catalog, asked). Windows' system-wide UTF-8 option (`ACP` / `OEMCP` 65001, Settings' "Beta: Use Unicode UTF-8") ends a whole class of encoding bugs in scripts and old tools; it can break a few legacy programs, so it's offered with that said, not applied silently. PowerShell's own output encoding is set to UTF-8 in the profile regardless.
- **8.38 Firewall prompts for every dev server** (S, asked). Each new runtime that listens pops "Allow on private networks?". omawin asks once which dev runtimes (node, bun, python, dotnet, go) may listen on **private networks only**, and adds those rules; public networks keep asking. Servers that only need `localhost` never need a rule.
- **8.39 The SSH agent and OpenSSH, working** (S). Windows' `ssh-agent` service is disabled by default: set to start automatically, keys added once, and Git for Windows pointed at Windows' OpenSSH so one agent serves Git, the terminal and WSL (3.9).
- **8.40 Long paths, symlinks and case-sensitive folders** (S, admin; partly in 1B). Long paths on; Developer Mode so symlinks work without admin; case sensitivity per folder (`fsutil file setCaseSensitiveInfo`) for repos shared with Linux tools.

### 8I. Keep it healthy

- **8.41 Disk and battery health** on the Health page (8.15): SMART reliability counters (`Get-StorageReliabilityCounter`: wear, temperature, read errors), battery wear (`powercfg /batteryreport`'s design vs full charge capacity), devices with driver problems (`Win32_PnPEntity` with an error code), each with what to do.
- **8.42 A restore point before omawin changes Windows** (S). Only inside an admin batch you already approved (it needs admin itself, 2.9): before that batch's changes (`Checkpoint-Computer`). omawin's own changes roll back without admin (version folders, every setting's undo); `omawin undo` lists both.
- **8.43 Driver updates on your terms** (S, catalog, asked). Windows Update can replace a working vendor driver with an older or generic one; the documented policy to exclude drivers from quality updates, offered with that trade-off said.

### 8J. What Windows 11 users complain about, answered

A web survey of 2026 coverage (Tom's Hardware, TechRadar, PCWorld, Windows Central, Windows Latest, Microsoft's Tech Community forums; links below) finds the same complaints again and again; Microsoft itself said 2026 would focus on reliability and performance over new features. Each one, and omawin's answer: an item above, or a new one here.

| Complaint (2026) | omawin's answer |
|---|---|
| **Updates that break things**: shutdown failures, PCs that don't boot, blue screens after a monthly update, emergency fixes | 8.18 (no surprises), 8.42 (restore point first), and new **8.44** (link a problem to the update that caused it, and undo it) |
| **AI pushed everywhere**: Copilot in Notepad, Paint, Photos, the right-click menu, Recall | no-ads (the Copilot app, done) and new **8.45** (every AI feature switchable in one place) |
| **File Explorer is slow**, slowest on first open, folders that take seconds to show | the finder (phase 6) for finding, 8.4 (Quick Look) and new **8.46** (Explorer's known slow paths fixed) |
| **The right-click menu is slow** (shell extensions loaded into Explorer) and the new one hides entries | 8.24 (the classic menu) and new **8.47** (find and switch off the slow extensions) |
| **Start's search is slow and fills with Bing** | the launcher replaces it; new **8.48** (web results out of Windows search) |
| **Centred taskbar, can't move it, missing features**; the redesigned Start | omawin replaces both (the native bar, the launcher); the taskbar auto-hides (done) |
| **Higher idle memory and CPU than Windows 10** | 8.28 (less in the background), 8.31 (indexer off), measured by `omawin tune-up` (8G) |
| **Bluetooth headphones drop to phone-call quality**, audio latency and clicks | new **8.49** (headphones stay in stereo) |
| **Random restarts, shutdowns that don't finish** | 8.15 (why it restarted) and 8.30 (fast startup off) |
| **Preinstalled apps** | new **8.50** (remove Windows' extra apps, reinstallable) |
| **Telemetry** | new **8.51** (diagnostic data at the minimum Windows allows) |
| **A Microsoft account required at setup** | before omawin runs, so out of reach; the manual's install page gets the documented way to set up with a local account |

- **8.44 Undo the update that broke it** (M, admin). When the Health page (8.15) sees crashes, failed shutdowns or blue screens start right after an update installed (Windows Update's history through the Update Agent API, against the event times), it says so: "This started after KB…, installed Tuesday." One action uninstalls that update (`wusa /uninstall /kb:` for an update package, DISM for the rest), pauses updates for a week (8.18), and offers the restore point (8.42). Microsoft's own advice for several 2026 updates was exactly this, done by hand.
- **8.45 Every AI feature in one place** (S, catalog). Copilot in Notepad, Paint and Photos, Click to Do, the AI actions in the right-click menu, Recall, Copilot in Edge: each a switch with its documented setting or policy, under *Look › AI* in the settings catalog (1B), off by default in omawin's *recommended* set and on with one key for anyone who wants them.
- **8.46 Explorer's known slow paths fixed** (S, catalog, recommended). Folder type discovery (Explorer guesses each folder's type by sampling its files, which makes big folders like Downloads slow: set them to *General items*), Home's recent and cloud files (fetched from OneDrive and Microsoft 365 on every open), the Gallery, opening to Home instead of This PC, and folder windows in a separate process so one hung folder doesn't freeze the desktop. Each timed (a big folder's open) before and after.
- **8.47 Find the slow right-click extensions** (M). Microsoft confirmed that context menu handlers run inside Explorer and slow it down. A page lists every installed shell extension (the `shellex` handlers and their DLLs), times each one's load, and switches off the slow or unwanted ones through Windows' own **blocked extensions** list (`Shell Extensions\Blocked`, per user, undoable), the way ShellExView does it. Explorer restarts once.
- **8.48 Web results out of Windows search** (S, catalog). Bing results and suggestions as you type in Windows' search box off (`BingSearchEnabled`, `DisableSearchBoxSuggestions`), so even when Start's search is used, it searches the PC and answers at once.
- **8.49 Headphones that stay in stereo** (S, 🖥). When any app opens a Bluetooth headset's microphone, Windows switches the headset to the hands-free profile: mono, phone-call quality, for everything. The Audio page shows when that's happening and offers, per headset, to turn off its hands-free endpoint (so it stays in stereo and another microphone is used) or back on for calls. Also: audio enhancements per device off (a common cause of clicks), and Bluetooth's codec shown where Windows reports it.
- **8.50 Remove Windows' extra apps** (S). Install › Remove gets *Windows apps*: the preinstalled Store apps (Clipchamp, News, Solitaire, Xbox apps, Microsoft To Do, …) with their size, removed for your account (`Remove-AppxPackage`), never Windows' own parts (the Store, Settings, the shell's apps), each reinstallable from the Store; the default removals are a list in the repo, asked about, not silent.
- **8.51 Diagnostic data at the minimum** (S, catalog). Windows' diagnostic data at *Required* (the minimum Home and Pro allow), optional diagnostics and tailored experiences off (partly done), feedback prompts off, activity history off; Windows Update and Defender untouched.

Sources: [Tom's Hardware: Microsoft working to fix Windows 11's most annoying flaws](https://tomshardware.com/software/windows/microsoft-is-reportedly-working-to-fix-windows-11s-most-annoying-flaws-wants-to-rebuild-trust-in-the-os) · [TechRadar: 5 big problems with Windows 11 to fix in 2026](https://techradar.com/computing/windows/5-big-problems-with-windows-11-that-i-really-hope-microsoft-fixes-in-2026) · [TechRadar: File Explorer sluggish](https://www.techradar.com/computing/windows/finding-windows-11-sluggish-when-youre-working-with-files-and-folders-youre-not-alone-and-its-high-time-for-microsoft-to-sort-out-file-explorer) · [Windows Latest: Microsoft admits right-click menus are worse than Windows 10's](https://windowslatest.com/2026/08/19/microsoft-admits-it-made-windows-11-worse-than-windows-10-for-right-click-menus-promises-to-fix-sluggish-and-cluttered-ux) · [Windows Central: faster context menus, navigation, transfers and search promised](https://windowscentral.com/microsoft/windows-11/microsoft-promises-to-speed-up-context-menus-folder-navigation-file-transfers-and-search-on-windows-11) · [PCWorld: the decluttered right-click menu's flaw](https://www.pcworld.com/article/2964382/windows-11s-decluttered-right-click-menu-draws-criticism-for-one-big-flaw.html) · [Pureinfotech: 2026 will focus on fixing Windows 11's biggest problems](https://pureinfotech.com/microsoft-fix-windows-11-2026/) · [Pureinfotech: 25H2 known issues](https://pureinfotech.com/windows-11-25h2-known-issues/) · [Microsoft Tech Community: why is everyone hating on Windows 11](https://techcommunity.microsoft.com/t5/windows-11/why-is-everyone-hating-on-windows-11/m-p/4517831) · [Yahoo Tech: 1 billion users, and they're furious](https://tech.yahoo.com/computing/articles/windows-11-1-billion-users-154000182.html) · [tbreak: the 2026 update and the taskbar](https://tbreak.com/windows-11-2026-update-taskbar-copilot/)

### 8K. Idle memory: from 6-7 GB to under 4

**The problem:** on the maintainer's PC, Windows reports **6-7 GB in use with nothing going on** (2026-10-30). The mission's budget is ~600 MB for the whole omawin desktop (mission.md, principle 3); a clean Windows 11 sits around 2.5-3.5 GB in use. So the target: **under 4 GB in use at idle**, with omawin's own part under 600 MB, and every megabyte accounted for.

**Measure first, on the PC (🖥).** `omawin profile ram -Save` (exists; the `profile-ram` skill drives it) already lists omawin's parts against budgets. Extend it to the whole machine, grouped by what you'd recognise:
- **omawin's parts** (as now): GlazeWM, the menu app (with the bar inside), noctty and its spare PowerShell, herdr and the agents in it (OpenCode's budget is 800 MB), dictation (Handy, 300 MB with its speech model loaded), the music player.
- **The WSL virtual machine** (`vmmem` / `VmmemWSL`): Linux's memory as Windows sees it, which grows with Linux's file cache and gives back slowly.
- **Browsers and web views**: Edge and Chrome, and every app that's secretly a web page (new Outlook, Teams, Widgets, Phone Link), grouped by their host app (the script already attributes WebView2 children).
- **Windows' services**: Defender (keep), Search and its indexer, Widgets, Copilot, Phone Link, OEM services, OneDrive.
- **The kernel**: drivers' memory (non-paged pool; a driver leak shows here) and the compressed memory store.
- **And what isn't really used:** the standby cache (Windows keeps recently used files in RAM and gives it back instantly; it's free memory, not waste), so the report says *in use* and *cached* separately and nobody chases the wrong number.
Saved runs (`-Save`) give before / after for every change below.

**Then cut, biggest first (each measured):**
1. **The old web bar uninstalled** (1.6): ~220-350 MB → the native bar inside the menu app (already running), ~0 extra.
2. **Agents only while used** (M): OpenCode and other agents in herdr's sessions stay resident (up to ~800 MB each). Project tabs start the agent when the tab is first focused, and herdr stops an agent idle for a set time (its session and history kept, so reopening resumes); `omawin profile ram` shows each agent session.
3. **Dictation's model loaded on demand** (S): Handy keeps its speech model in memory (~300 MB); load it on the dictation key, unload after a few idle minutes (Handy's own setting if it has one; else start and stop Handy around use).
4. **The WSL VM sized for idle** (S, 3.3 and 3.5): `autoMemoryReclaim=gradual` (gives Linux's cache back over time, rather than only on drop), a memory cap in `.wslconfig` (half the RAM by default is a lot for an idle VM; e.g. 4-8 GB on a 32 GB PC), and the keep-warm of 3.5 **measured against its cost**: if a warm, idle Arch costs more than ~500 MB, Linux starts on first use instead, and the first terminal waits once (the trade written down, the default chosen by the numbers).
5. **Windows' background apps** (8.28, 8.31): Widgets, Phone Link, Edge's startup boost and background running, Teams' and OneDrive's start at sign-in (asked), the Search indexer once the finder and whole-disk search replace it.
6. **noctty's spare shell** (S): the pre-started PowerShell (~60-80 MB) only while the PC is on AC power, or released after an idle hour; measured against Win+Enter's time.
7. **The menu app itself** (S): AOT build everywhere (1.3: 114 MB vs 173 MB), the clipboard history's images kept on disk with thumbnails in memory, icon caches bounded.
8. **Drivers that leak** (8.41): when the non-paged pool is large, the Health page names the driver (pool tags, `poolmon`'s method) and the update that fixes it.

**Budgets made real:** `bin/profile-ram.ps1`'s budgets tightened to the 600 MB desktop (GlazeWM 80, the shell 200 with the bar inside, noctty 150, herdr 100 without agents), agents and dictation counted separately as *on demand*, and `omawin check` warning when an idle desktop is over budget.

**Done when:** a saved idle profile on the maintainer's PC shows under 4 GB in use (cached reported apart), omawin's parts under 600 MB, and each step's saving written here.

### What not to do

Never inject code into Explorer or other processes (no hook DLLs, no Windhawk-style patching; shell-apps.md decision 5); never disable Windows Update, Defender or SmartScreen outright; never touch the protected default-app keys; anything undocumented has a fallback and is hidden when it stops working.

## Phase 9: the rest of Omarchy (from a code review)

A review of Omarchy's whole codebase at commit `7f4b1dd` (2026-10-30: all 557 commands, the menu, every shell plugin, the Hyprland bindings, bash, templates, install, docs, manual), each feature marked *have*, *planned*, *new* or *n/a on Windows*: **[plan/08-omarchy-review.md](08-omarchy-review.md)**, with how each new one works on Windows, its home (core, catalog, first-party plugin, script, Linux side), its size and code. The new items, in the review's order of value for a developer:

1. **9.1 A drop-down console** (`omawin console`; Omarchy's Quake-style console): a terminal that slides down over everything on a key and hides again; built on the scratchpad (5.7), with Windows Terminal's quake mode as the bridge until the fork has it (M).
2. **9.2 Hooks** (`omawin hook`): your scripts run on events (theme set, after an update, font set, …), from the account folder, as Omarchy's hooks (S).
3. **9.3 `omawin setup git`**: name and email, `gh auth login`, `gh auth setup-git`, the same inside Linux (S).
4. **9.4 Crash watch**: a toast when a program crashes, with a button that asks your coding agent to diagnose it from the event log (8.15's reader) (M).
5. **9.5 The theme reaches more apps**: the browser's colour (per-user policy), Claude Code, OpenCode, lazygit, Helix, Alacritty (M).
6. **9.6 `omawin debug` and `omawin version`**: a redacted support bundle for bug reports (S).
7. **9.7 `omawin pick`**: menus scripts and plugins can show and get an answer from (over the reply channel of [plan/02](02-settings-and-one-search.md)) (S-M).
8. **9.8 Agent accounts**: several accounts per coding agent (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`), switched near a usage limit; Codex in the usage panel (M).
9. **9.9 `omawin reset <config>`**: one config back to omawin's version, yours kept as `.backup-<timestamp>` (S).
10. **9.10 `omawin display internal|external|mirror|extend`** (`SetDisplayConfig`, no admin), and display scaling (S, scaling M).
11. **Next in line:** a Tailscale panel and Taildrop; toggles for the bar and the gaps; herdr's keys on the Shortcuts page; Share through Windows' share sheet or LocalSend; and the small items in the review's N15 table.

**Already on the to-do list but not yet on the roadmap** (the review found them only in [mission-todo.md](../docs/mission-todo.md); they belong here, and two would rank in the top ten):
- **9.11 Screen recording** (Windows.Graphics.Capture or ffmpeg's `ddagrab`), and **9.12 annotation** in the snip tool (pen, arrow, box).
- **9.13 A font switcher** (one font across the bar, the menu, the terminal and the editor, rendered like the theme) and **9.14 text size** as one knob.
- **9.15 Claude Code follows the theme** (Omarchy 4 does) — part of 9.5.
- **9.16 Defaults beyond terminal and editor**: browser and dictation (`omawin default`).
- **9.17 The bar's missing widgets** (weather, calendar popup, keyboard layout, indicators, agent usage) — as plugins where they fit (phase 4).
- **9.18 Emoji** in the launcher, falling back to Windows' Win+. picker.

**From a user's favourite plugins** ([plugins.md](plugins.md)):
- **9.19 Backups like Time Machine**: restic (it runs on Windows) backing up your Windows home and your Arch home to the cloud on a schedule, with the bar showing the last backup and a launcher page to browse and restore files (M).
- **9.20 Workspaces that show what's on them**: the native bar's workspace indicators show each workspace's apps (their icons), from GlazeWM's IPC, as Omarchy's Spaces plugin does (S).
- **9.21 A tray overflow for plugin widgets**: drag a plugin's widget into the tray's overflow to keep the bar tidy, as Omarchy's Tray plugin does (S).

## Phase 10: omawin for everyone

Code and detail: [09-for-everyone.md](09-for-everyone.md). The plan above makes omawin fast and complete on a personal x64 PC in English, at a desk; this phase covers everyone else, and the trust a stranger needs before running the install line.

| # | What | Size | Notes |
|---|---|---|---|
| 10.1 | **Security model**: the bootstrap from a release, the bundle checked against signed `SHA256SUMS.txt`, themes are data only, plugins pinned to a commit with new permissions shown on update, secrets in Credential Manager, `SECURITY.md` | M | `Sums`, `ThemeFiles` built and tested 2026-10-30 (`src/cli/`) |
| 10.2 | **Work and school PCs**: the user's proxy and PAC passed to omawin's tools, Windows' trusted roots for git, Bun and Arch (TLS inspection), an AppLocker probe before unpacking, policy-managed settings shown locked, managed-device facts in `omawin check` | M | (check) behind a PAC file |
| 10.3 | **ARM64**: every binary built for `win-arm64` on GitHub's ARM runners, a bundle per architecture, the machine's architecture read through `IsWow64Process2`; Linux on ARM defaults to Ubuntu until Arch has an aarch64 WSL image | M | |
| 10.4 | **Accessibility**: text from any theme readable (`Contrast.Readable`), Narrator names and live counts, Windows' animation switch honoured by the menu and the window animations, text size, high contrast, focus rings | M | contrast check built and tested 2026-10-30: accent as text fails in Rosé Pine, Miasma, Catppuccin Latte; `comment` in most themes |
| 10.5 | **Windows in other languages**: APIs or machine output instead of translated command output (a CI lint), known folders, localized setting names through Windows' index, digits bound by key position, the user's number and date formats | S–M | the lint built 2026-10-30 |
| 10.6 | **Laptops**: power mode in One Search and the bar, a battery widget driven by events, background work waits on low battery, the lid as a setting, four-finger swipes move workspaces, dock/undock re-applies the monitor profile | M | |
| 10.7 | **Strangers**: a README with a loop and a Sandbox trial, a website built from `manual/` with Bun, release notes from commits and migrations, no telemetry (`omawin report` makes a redacted report you choose to post), issue forms, `CONTRIBUTING.md` | M | `Redact`, release `Notes` built and tested 2026-10-30 |

## Things to explore

Open questions to measure or try before building, collected from every phase. Each says what would decide it. Answers go back into the item; this list shrinks.

| To explore | Decides | Where |
|---|---|---|
| **Idle memory**: where 6-7 GB goes on the maintainer's PC (whole-machine `omawin profile ram`, in use vs cached) | what to cut first, and whether under 4 GB is realistic | 8K |
| **Windows 11's common complaints**, each reproduced and timed on this PC (slow Explorer, slow right-click, Bing in search, Bluetooth call quality, broken updates, AI everywhere) | which fixes ship in the recommended set | 8J, mission-todo track 9 |
| **Idle CPU and wake-ups** by process, next to memory | 8.28's list | 8G, 8K |
| **A warm WSL's cost** vs the first terminal's wait | whether Linux is kept warm by default | 3.5, 8K |
| **The bundle's real size and time** (MinGit, Neovim with plugins, the menu app AOT) on 20, 100 and 500 Mbit/s | the one-minute promise, and what's left out | 2.0, [install-speed.md](install-speed.md) |
| **Today's install, timed per step and per package** (the baseline) | where to cut first | [install-speed.md](install-speed.md) M0 |
| **Windows' `tar.exe` formats and speed; Defender's first-run scan of new files** | the bundle's format, and whether signing is needed before M2 | [install-speed.md](install-speed.md) |
| **The per-user GlazeWM without `uiAccess`**: what it can't do until the signed install lands | whether the background admin step must come sooner | 2.0 |
| **Windows' settings index format** (`AllSystemSettings_*.xml`) on the current build | settings search's source, or the curated fallback | 1B.1 |
| **`XamlReader.Load` under Native AOT**, with omawin's binding | XAML panels, or the declarative fallback | 4.3 |
| **Quickshell's licence** (which modules plugins use is answered for Developer Tools' top 30: core, Io, Wayland, Hyprland, Widgets; [plugins.md](plugins.md)) | how much of the QML host to build, and what can be reused | 4.9 |
| **herdr and OSC 11**: does it pass a pane's background through | the Linux / PowerShell tint without a herdr change | 3B |
| **`hey` and `gws` on Windows** (for the calendar plugin) | whether that plugin can ever run outside Linux | plugins.md |
| **Window animations on this PC's GPU**, a week of daily use | shipping 1.5 | 1.5 |

## Always: following Omarchy

`mjveilleux/omarchy` tracks upstream. At each Omarchy release, a short note in mission-todo.md of what's worth bringing over. Data we can import is imported by a script, not copied by hand:

| From Omarchy | Into omawin | How |
|---|---|---|
| themes (`themes/*/colors.toml`) | `themes/colors/` | `themes/import-omarchy.ps1` (exists) |
| terminal packages (`install/omarchy-base.packages`) | Arch in WSL | `wsl/setup.sh` minus `wsl/omarchy-skip.txt` (3.2) |
| package repositories (stable channel) | Arch in WSL | `wsl/setup.sh` (exists) |
| bash defaults (`default/bash/`) | Linux shells | sourced (3.2) |
| motion values (`looknfeel.lua`, `motion.md`) | `shell/menu-app/Motion.cs`, `config/glazewm/config.yaml` | by hand; rarely changes |
| menu sections (`omarchy-menu.jsonc`) | `default/launcher/menu.txt` | compared per release |
| dev envs and databases | `bin/install-dev-env.ps1` | compared per release |
| plugin manifests | `plans/plugins.md` | the schema followed (4.1) |
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
- The CI install: desktop up in under one minute from the bundle, everything else finishing in the background, `omawin check` without failures; `omawin sandbox test` clean on the PC.
- Window animations pass the frame check, and a week of daily use finds no stuck overlay.
- A fresh install, Enter through every question: Arch ready with Omarchy's terminal packages, Win+Enter opens it as fast as it opened PowerShell, and a Linux terminal looks different from a PowerShell one at a glance.
- One plugin of ours and one written from an Omarchy plugin's idea, installed with `omawin plugin add`.
- Every row in mission-todo.md's Omarchy parity list is have, Windows-native instead, or decided against.
