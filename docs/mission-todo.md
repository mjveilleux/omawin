# Mission to-do

The work between today and [the mission](mission.md), in tracks. Each track can be picked up on its own (one agent or one sitting per track); within a track, top items first. Tick items off here as they land, with the commit. Decisions for the maintainer are in `open-questions/` and linked from the item.

Sources: the distribution audit and the Omarchy 4 parity survey of 2026-10-15 (Omarchy v4.0.4, `basecamp/omarchy` branch `quattro`). What similar Windows projects learned, mapped to these tracks: [inspiration.md](inspiration.md).

## 0. Health (now)

- [x] Review fixes: install on PowerShell 5.1, fork install on a fresh PC, safe migrations, session-aware GlazeWM start, menu-app leaks and races, netstat off the UI thread, music open without the player (2026-10-15)
- [x] WezTerm out of the code; noctty is the only terminal besides Windows Terminal (2026-10-15)
- [x] Line endings: the repo stores LF, Windows copies are CRLF (bash LF); rule in `.gitattributes` and AGENTS.md; every file re-checked out to match (2026-10-15)
- [ ] Try Win+M (music panel open / close) and Win+Shift+T (text snip) by hand after the fixes

## 1. Distribution: one line on any PC

- [ ] Public repo with a LICENSE (MIT, like Omarchy; credit Omarchy for themes and ideas). Its history cleaned of personal and work details first.
- [x] `bootstrap.ps1` without `gh auth login`: a plain https clone, layers optional (2026-10-17); a short URL is still to do
- [x] No identity in the product: `git/gitconfig` asks for name and email at install (or leaves them to an untracked include); AGENTS.md and docs stop naming the owner, their work and the old private repo (2026-10-15: install asks once into `~/.gitconfig`, or the user layer's `git/gitconfig` is included)
- [ ] Product vs user config: the product checkout is never edited by the user; user overrides live in their own folder (e.g. `~/.config/omawin/`: items, sites, keys, menu rows, nvim `lua/plugins/user/`, `profile.ps1`, gitconfig include) and are merged at render time. Seed copies, not junctions into the repo
- [x] Files apps write (nvim `lazy-lock.json`, Windows Terminal `settings.json`) live outside the checkout (2026-10-15: also `lazyvim.json` and pwsh's `Documents\PowerShell`; migration `writable-files-out`)
- [ ] `omawin update` that can't fail on local changes: fetch and move to the latest release tag (channels: stable, edge), migrations as today, a System Restore point first. Done 2026-10-15: local changes are stashed and put back (kept in a named stash on a clash), local commits rebased or kept on a branch, never left mid-merge, `-DryRun`. **Next: release tags and channels — releases now exist to follow** (2026-10-16: `.github/workflows/release.yml` publishes them on a `v*` tag, install takes their binaries); the restore point
- [x] Releases built in CI (GitHub Actions): `omawin.exe`, the menu app, `omawin-music`, so a new PC downloads binaries instead of installing the .NET SDK (2026-10-16: `.github/workflows/release.yml` on a `v*` tag or by hand — the same `build/helpers.ps1`, `menu-app\build.ps1`, `music\build.ps1` this PC runs, SDK pinned in `global.json`, actions by SHA; `build/release.ps1` packs `omawin-helpers-<tag>.zip`, `SystemMenu-<tag>-win-<arch>.zip`, `omawin-music-<tag>-win-<arch>.exe` and `SHA256SUMS.txt`; `bin/release.ps1` + `bin/install-binaries.ps1` download, verify and fall back to a build, `-NoDownload`/`-NoBuild` to force; `build/test-release.ps1` proves it against a fake release, [releases.md](../docs/releases.md))
- [ ] Code signing in CI (e.g. Azure Trusted Signing) instead of a self-signed root made on every PC. **Designed 2026-10-16** ([release-signing.md](../docs/release-signing.md)): a `sign` step in the release job, off until `CODESIGN` is true, values as repo variables and secrets. Next: the Azure account, the certificate profile and identity validation (an account decision, not code)
- [ ] No Chocolatey: move its packages to winget or presets; the default install needs no admin except where a piece truly does (one prompt, batched). Packages done 2026-10-15 (all winget or presets; `unzip` / `gzip` dropped: no winget package puts them on the PATH, Windows' `tar` and Git Bash cover them); the menu still understands `choco <id>` rows. Admin prompts batched 2026-10-17: one prompt at the start (`install/admin.ps1`) for the whole-PC packages and both forks; `omawin no-ads`'s admin part is still its own
- [ ] ARM64: menu app, music, the GlazeWM and noctty forks, helpers. Done 2026-10-16: the menu app and the player build for `win-arm64` (`menu-app\build.ps1 -Arch arm64`, `music\build.ps1 -Arch arm64`, the arm64 fff pinned in `menu-app\fetch-fff.ps1`; both verified by PE machine and published as arm64 assets), and the helpers are architecture-neutral IL. Next: the workflow's ARM64 jobs (`vars.ARM64_RUNNERS`) wait on a public repo, since GitHub's `windows-11-arm` runners are free for public repositories only; the GlazeWM and noctty forks are their own repos
- [ ] Repo location not fixed to `~/omawin`
- [ ] Clean-VM test: Windows 11 Home and Pro, no WSL, no OEM bloat; time and size measured; `omawin check` 0/0
- [ ] README quickstart for strangers: what it is, a screenshot, the one line, how to undo

## 2. Personal vs general

Personal things move to the owner's own user layer (today `~/omawin-work`, which becomes the general user-extension folder from track 1).

- [x] Personal out of the product: `git/gitconfig` identity, Claude settings, launcher items and sites, tool paths in the profile, WSL memory (2026-10-15: all in the user layer, docs/work.md; WSL memory left at its default, half the RAM, the user layer can cap it)
- [x] The Reports page and the remaining `omawin-work` hard-coding (menu app `MenuModel.Work`, `omawin reports index` in `bin/commands.txt`, the profile hooks): one user-layer path, set in one place (2026-10-15: one definition per language, `OMAWIN_USER_LAYER` overrides; docs/work.md)
- [x] `no-ads.ps1` split: Windows debloat by default; Lenovo, McAfee, Adobe as modules that run only when that software is found
- [x] Presets instead of one big list: a lean core (WM, bar, launcher, terminal, editor, shell tools, Git) plus opt-in groups (data: DuckDB, harlequin, SQL; R; Elixir; Go; Zig; AI agents; music; dictation; Power BI / Fabric), picked in the menu's Install section or at first run (2026-10-15: `packages.ps1` `$Presets`, `omawin preset`, Install > Presets, asked on the first install; Zig stays in the core as nvim-treesitter's compiler)
- [ ] Detect before offering: hide WSL items when there's no distro, Office items without Office, Chrome profiles without Chrome (needs the menu app: launcher items are static rows today; e.g. an item condition like `?wsl`)
- [x] One Windows account per job or client, each with its own user layer repo; `omawin user new` starts one (2026-10-17: `bin/user-new.ps1`, docs/work.md "One Windows account each")
- [ ] More than one account on a PC: packages machine-wide where winget allows (`--scope machine`), and `install.ps1` split into the machine's part (admin, once) and the account's (links, sign-in task, menu app, themes), so a second account's install skips what's done; `omawin check` clean in a second account
- [ ] Music: decide whether YouTube playback ships by default: [open-questions/music-default.md](../open-questions/music-default.md)
- [x] Radio in the player: a third tab (Music | Podcasts | Radio) for internet stations, each one live stream — `omawin music add radio <url> [name]`, named from the stream's own `icy-name` (`NTS Radio`, `NTS Radio 2` for a second channel), `LIVE` instead of a progress bar, nothing to seek; a web page pasted there is refused with the reason (2026-10-16: `Library.AddRadio` and its header probe, `live` in the player's state, `zebar/tokyo-islands/music.js`, `manual/07-bar-and-dock.md`)

## 3. Window manager (GlazeWM fork, toward Hyprland)

- [ ] Scratchpad / special workspace (Omarchy's Super+S): a hidden workspace and a toggle, in the fork or through IPC
- [ ] Window animations: new windows fade in where they tile, the others slide (snapshots, Omarchy's easeOutQuint, quicker). Built 2026-10-28 on upstream's `animations-test` (fork branch `claude/smooth-window-animations`), on in `glazewm/config.yaml`; not yet tried on a PC or released. Workspace switches stay instant, as in Omarchy
- [ ] Window groups / tabbed containers (Omarchy's Super+G)
- [ ] Layout cycling, pop-out (float and center), restore sizes
- [ ] Corner style and transparency toggles in the menu (GlazeWM already supports both)
- [ ] Touchpad gestures for workspace switching
- [ ] Shortcuts page generated from `glazewm/config.yaml` (Omarchy builds its cheat sheet from bind descriptions) instead of the hand-kept `launcher/keys.txt`
- [ ] Upstream what's general to glzr-io/glazewm, so the fork stays small

## 4. Bar and dock: native, in the shell (replacing Zebar)

Decided 2026-10-15: no Zebar fork. The bar and dock move into the menu app (the shell), the way Omarchy 4 folded Waybar into Quickshell; GlazeWM stays the engine. **The dock comes first**, and replaces the Windows taskbar itself: the plan, the limits and every other Windows surface we replace are in [shell-apps.md](shell-apps.md).

- [ ] Bar prototype: a WinUI 3 window per monitor docked to the top edge, reserving its space with `SHAppBarMessage` (as the taskbar does), no focus stealing, hidden from GlazeWM's tiling; workspaces from GlazeWM's IPC subscription; clock; themed from the palette like the other pages
- [ ] Parity with `zebar/tokyo-islands`: workspaces, clock, CPU / RAM, network, battery, tray, VPN, media and the music panel, dictation meter, then the dock (open windows by workspace, launcher buttons, quick settings)
- [ ] Widgets Zebar never had: volume and mic mute, Bluetooth, brightness, weather, calendar popup on the clock, keyboard layout, update-available badge, indicators (do not disturb, stay awake, recording, dictation), coding-agent usage; read in-process, no helper exes
- [ ] Bar layout in one config (which widgets, which side, which edge), in the user layer
- [ ] Bar plugins: a widget format for the user layer (decide: a small declarative file, or a C# assembly)
- [ ] Zebar removed: a migration stops and uninstalls it; `voice-meter`, `cpu-cores` and the other Zebar helpers retire into the shell
- [ ] RAM budget: the whole shell (bar, dock, launcher, menu, pages) under 200 MB, against ~570 MB for Zebar + menu app today (WinUI's own floor is ~100 MB per process)

## 5. Shell: the menu app (toward Omarchy's panels, menus and toggles)

- [x] Control pages in one Quick settings page (Win+A, grown from Audio): Wi-Fi (networks, connect, disconnect, signal), Bluetooth (radio, paired devices, connect), a brightness tile per monitor (WMI for a built-in panel, DDC/CI for an external one), power mode, battery, night light, do not disturb, and the sound below them (2026-10-15). Left: QR to share a network, text scale, a profile per AC / battery, battery health, airplane mode
- [x] Toggle: stay awake (`omawin toggle stay-awake`, `SetThreadExecutionState`; `%LOCALAPPDATA%\launcher\stay-awake` while on, for a bar indicator) (2026-10-15)
- [x] Toggles: do not disturb and night light (`omawin toggle dnd`, `omawin toggle night-light`; neither has a public API to set, so both open the Settings page that owns it, as this item allowed) (2026-10-15)
- [ ] Toggle: idle lock
- [x] Capture: color picker and QR reading in the snip overlay (`omawin capture color`, `omawin capture qr`; QR by ZXing.Net) (2026-10-15)
- [ ] Capture: simple annotation (pen, arrow, box); screen recording (Windows.Graphics.Capture or ffmpeg `ddagrab`)
- [ ] Clipboard with images, and sensitive entries (password managers) skipped
- [x] Launcher: calculator (`Calc.cs`: a sum's value as the top row, Enter copies) (2026-10-15)
- [ ] Launcher: unit conversion; web apps (install a site as an app window with its icon, `--app=`); emoji falls back to Windows' Win+.
- [ ] Reminders and toasts (`AppNotificationManager`): timers, low battery
- [x] Menu section About, fastfetch-style (`omawin about`) (2026-10-15)
- [ ] Menu sections Omarchy has that we don't: Setup › Input / Language / Security, Install › Web app / TUI / Fonts, Remove (uninstall what Install added)
- [ ] Visual background picker (grid of the theme's pictures), not only "next"

## 6. Developer machine

- [ ] Dev environments through mise (`jdx.mise`): Node / Bun, Python, Go, Rust, Ruby, Java, .NET, Elixir, Zig, PHP, one menu row each, like `omarchy-install-dev-env`
- [ ] Databases in Docker from the menu (Postgres, MySQL, Redis), like `omarchy-install-docker-dbs`; Docker Desktop or Podman as the engine
- [x] WSL as an option: install a distro, link the Linux dotfiles, projects from WSL in the launcher (2026-10-19: the `wsl` preset, `wsl/`; WSL itself still by hand, open-questions/wsl-in-admin-step.md)
- [ ] Defaults beyond terminal and editor: browser, coding agent (Omarchy's `a`), dictation
- [ ] Coding agents first-class: install, sign in, a key for the default agent, project tabs (herdr), crash help
- [ ] Fonts: a font switcher that renders into every app's config, like the theme

## 7. Themes

- [ ] Claude Code theme follows the omawin theme (Omarchy 4 does)
- [ ] Font and text size as theme-level settings (one knob for bar, menu app, terminal, editor)
- [ ] New Omarchy themes picked up by `themes/import-omarchy.ps1` on update

## 8. Following Omarchy

- [ ] Fork basecamp/omarchy (mjveilleux/omarchy) to track it; a note per Omarchy release of what's worth bringing over
- [ ] Credit and license notices for everything borrowed (themes, backgrounds, ideas)
- [ ] Keep this file's parity list current: each Omarchy feature is have, Windows-native instead (Windows Hello, Action Center, volume flyout), to do, or decided against (ISO install, hardware fixes, Windows VM)
