# Roadmap: Omarchy's developer experience on Windows

Where omawin stands on 2026-10-30, and the order we take the rest in to get close to Omarchy 4 in how it installs and how it feels to use. [mission.md](mission.md) says why; [mission-todo.md](mission-todo.md) is the full checklist by track. This file picks from it, adds what the 2026-10-30 review found, and puts it in phases. Tick items in mission-todo.md as they land; this file changes only when the order changes.

Sources: this repo; Omarchy 4 (`mjveilleux/omarchy`: `shell/`, `docs/omarchy-shell.md`, `docs/menu.md`, `agents/skills/motion.md`, `default/hypr/looknfeel.lua`); our GlazeWM fork (`mjveilleux/glazewm`, branch `omawin`).

## Where we are

**Done and solid:** one-line bootstrap; one admin prompt; presets on a lean core; releases built in CI so a new PC downloads binaries; the install screen; migrations; `omawin check`; the menu app as one always-running native process (launcher, system menu, file finder, clipboard, Quick settings, screenshots with OCR, calculator, packages page); 24 Omarchy themes, applied live; noctty and herdr; WSL with Arch as the default Linux, on Omarchy's package repositories; dev environments through mise and Omarchy's Docker databases (Install › Development, [dev-envs.md](dev-envs.md)); Native AOT for the menu app (first start 0.34 s, 114 MB); the native bar, now the default.

**Built, not yet shipped:** window animations in the GlazeWM fork (`omawin` branch: new windows fade in, the others slide, on snapshots drawn with Windows Graphics Capture and DirectComposition, ease-out-quint 200 ms). Not tried on a PC, not in a release.

**The gaps against Omarchy, by what you feel:**

| You notice | Omarchy | omawin today |
|---|---|---|
| A key opens the menu | the key goes straight to the resident shell; no process starts | GlazeWM starts `omawin.exe` (~35 ms), which writes a file and signals the menu app |
| The menu opens and closes | fades in within 250 ms, out in ⅔ of that (ease-out-quint) | appears and disappears |
| Windows open and move | pop in and slide (Hyprland) | instant (animations unreleased) |
| Install | packages from pacman, 5 downloads at once | winget, 4 at once, ~150 s of packages before the desktop is up; the AOT build only where Visual Studio's C++ tools are |
| Extending it | plugins: a folder with `manifest.json`, `omarchy plugin add <git-url>`, hot reload | menu rows and launcher items in text files; no widgets, panels or plugins of your own |
| Everyday dev setup | `install-dev-env` (mise), Docker databases, web apps, TUIs, a key cheat sheet built from the bindings | dev environments and Docker databases (Windows side); no web apps, a hand-kept key list |
| Linux | it *is* Linux | Arch in WSL, opt-in at install; a Linux terminal or project when asked (`--wsl`), otherwise Windows first |
| Files | Nautilus, with a preview (Sushi) and "here" keys | the Win+F finder (fff): search, preview, chips; no actions beyond open and show in folder, no browsing |

## Phase 1: snappy (now)

Every key answers in one frame, and everything moves the way Omarchy moves.

1. **Measure the menu first.** `omawin profile menu`, like `omawin profile terminal`: real key down to the first painted frame of the launcher and system menu, from the trace marks `omawin.exe` and the menu app already write. Budget: 50 ms to pixels, 16 ms per keystroke while typing (fuzzy search, icons off the UI thread). Measure before changing anything below.
2. **Keys straight to the shell, no process.** Omarchy's menu key is a Hyprland global shortcut the shell registered, so nothing starts. Ours: the menu app already holds a GlazeWM IPC subscription (`Tiling.cs`). Add a command to the fork that sends a named event to IPC subscribers (`emit omawin.launcher`), and bind Win+Space, Win+Alt+Space, Win+Shift+V, Win+A and Win+F to it; `omawin.exe` stays the fallback when the menu app isn't running. Saves the process start and the file write on every press.
3. **The AOT build for everyone.** Releases ship the Native AOT menu app (CI has the C++ tools), so no PC runs the slower JIT build because it lacks Visual Studio.
4. **Motion, from one place.** Omarchy's rules (`agents/skills/motion.md`): entrances within 250 ms, exits about ⅔ of that, ease-out-quint, never linear or ease-in. One set of tokens in the menu app (durations, curves, a global "animations off") used by the menu, the bar's fade-in, Quick settings and every future panel, through Composition animations (off the UI thread). The menu: fade and a slight scale-in on open, a quicker fade out.
5. **Ship window animations.** Try the fork's `omawin` branch here, fix what shows, release it. Then: `omawin toggle animations` (GlazeWM's config and the shell's tokens together), off by default in a VM (as Omarchy does), a close animation, and the fork's own frame check (`scripts/animation-check.ps1`) back on in CI so a change that judders fails. Workspace switches stay instant, as in Omarchy.
6. **Finish the native bar** ([native-bar.md](native-bar.md) step 3): media, then Zebar removed with a migration. One process fewer, ~220 MB less, a 2 s sign-in paint gone.

## Phase 2: fast install

Goal: a clean Windows 11 PC goes from the one line to a working desktop in **under 3 minutes**, and to everything in under 10. Measured, not guessed.

1. **Time it in CI.** A workflow runs `bootstrap.ps1` on a fresh GitHub Windows runner and fails when it gets slower than the budget or `omawin check` isn't 0 / 0. That is also the clean-PC test mission-todo.md asks for. `install-times.txt` gives the per-step times.
2. **Desktop first, the rest after.** Today everything installs, then the desktop starts. Reorder: GlazeWM, the menu app, noctty, the theme and the font first (a few downloads, all from our release or one winget call each); start the desktop; the remaining packages carry on in the background, with progress in the bar (an install indicator, like the update arrow) and a toast when done. You're tiling windows while Node and Neovim's toolchain arrive.
3. **The command-line tools without winget.** winget costs seconds per package before it downloads anything. The ~12 portable tools (ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, …) are single exes: fetch them in parallel, pinned and checked, into `~/.local/bin`. Either one bundle zip built by our release workflow, or mise with its GitHub-release backends (which Omarchy already uses for dev tools). Decision: [open-questions/cli-tools-source.md](../open-questions/cli-tools-source.md).
4. **Neovim ready on first open.** Run `nvim --headless "+Lazy! sync" +qa` (and Mason's core LSPs) in the background after the install, so the first `nvim` doesn't stop to install plugins.
5. **A leaner core.** Neovide and Windows Terminal move to presets (noctty is the terminal; Terminal is the fallback a new PC already has on Windows 11).
6. **One fetch for our own pieces.** The helpers, the menu app and the music player come from one release zip per architecture instead of three downloads.

## Phase 3: Arch in WSL, first class

Arch is already the default Linux (Omarchy's stable package repositories, no AUR, `wsl/setup.sh`), but it's an opt-in preset and Windows comes first everywhere. First class means Arch is where your code lives and runs, and omawin treats it as its second home: installed by default, warm when you need it, reachable from every key, kept up to date and checked like the rest. Never at the cost of a distro you already had: that one stays yours ([AGENTS.md](../AGENTS.md)).

1. **On by default.** The install's Linux question starts on Arch (as now), and the `wsl` preset is in the default set: pressing Enter through the questions gives you Arch. WSL's restart stays the one reboot, resumed at the next sign-in (it is today).
2. **A good WSL VM out of the box** (`wsl/.wslconfig`, `/etc/wsl.conf`): systemd on, mirrored networking (localhost works both ways, VPNs behave) with DNS tunneling, sparse disk, memory handed back; each a setting your account folder can override.
3. **Linux as your default side.** `omawin default side linux|windows`: with Linux, Win+Enter opens a terminal in Arch (in the folder of the window in front, when that's a Linux folder), new projects go in `~/src` inside Arch, and Install › Development installs into Arch. Windows stays the default until you pick.
4. **No cold start when it counts.** Arch starts in the background at sign-in when Linux is your side (`omawin toggle linux-warm` otherwise), so the first Linux terminal opens as fast as a Windows one; measured with `omawin profile terminal --wsl`.
5. **Omarchy's tooling in Arch.** Dev environments through mise and Omarchy's own lists, run inside Arch (`omawin install dev-env <lang> --linux`); Docker Engine in Arch (pacman, systemd) instead of Docker Desktop, so the Docker databases and lazydocker run there with no Windows-side licence or VM; Omarchy's bash defaults, as its Linux users have them.
6. **Linux files in the finder and launcher.** Win+F searches your Arch home and repos (indexed inside Arch, where it's fast, not over the `\\wsl.localhost` share); Linux GUI apps (WSLg) appear in the launcher with their icons; Localhost already lists Linux servers.
7. **Credentials that just work.** Git uses Windows' Git Credential Manager from Arch; the SSH agent and GPG reach Windows' (1Password or OpenSSH) through a relay, so one sign-in serves both sides.
8. **The theme and tools match, live.** The prompt, Neovim, fastfetch, lazygit and btop in Arch follow the omawin theme on a switch, as Windows' do (they read the same repo through `/mnt/c` today; anything rendered gets its copy inside Arch too).
9. **Kept and checked.** `omawin update` updates Arch (it does: `setup.sh update`) and runs Linux-side migrations (`migrations/linux/`, a marker file per user, Omarchy's way); `omawin check` checks the distro (running, systemd, the repos, the tools, the user); a bar indicator says Arch is running and what it uses, a click to stop it.
10. **Safe to break.** `omawin linux backup` (`wsl --export`, a dated file) and `omawin linux reset` (a fresh Arch beside your backup, set up again); never touches a distro omawin didn't make.

The clean-PC CI test (phase 2) can't run WSL (GitHub's runners have no nested virtualization), so Arch gets its own test: `setup.sh` in an Arch container in CI, and a run on a real PC per release.

## Phase 4: plugins (Omarchy's shell, on Windows)

Quickshell is Qt on Wayland and won't run on Windows, but its plugin model will: we keep **Omarchy's manifest format** and draw with our own shell. Like the themes, an Omarchy plugin's idea (and much of its manifest and data) carries over; its QML doesn't.

1. **The format.** A plugin is a folder (a git repo) with `manifest.json` in Omarchy's schema: `schemaVersion`, `id`, `name`, `version`, `author`, `description`, `kinds`, `entryPoints`, `keepLoaded`, the `barWidget` block (`displayName`, `defaultSection`, `defaults`, settings `schema`). Kinds as Omarchy's: `bar-widget`, `panel`, `menu`, `service`.
2. **What a plugin can contain.** The menu app is Native AOT, so it can't load plugin DLLs. Instead:
   - **Command widgets and services**: a program or script that prints JSON lines (Waybar's format, which Omarchy's `type: "command"` modules also use: `text`, `tooltip`, `class`, `percentage`); clicks call commands. Covers most bar widgets.
   - **XAML panels**: a loose `.xaml` file the shell loads (`XamlReader.Load`), bound to the plugin's JSON state, styled by the theme's brushes. For panels and drop-downs.
   - **Menu entries**: a `menu.txt` fragment, added after omawin's rows (as the account folder's already are), and launcher items.
   Decision on the panel format: [open-questions/plugin-format.md](../open-questions/plugin-format.md).
3. **Where they live.** `~/omawin-account/plugins/<id>/` for one account, `C:\ProgramData\omawin\shared\plugins\` for every account on the PC; never the repo. `omawin plugin add <git-url> | update | remove | enable | list`; new plugins arrive disabled, as in Omarchy. The shell watches the folders and reloads a plugin when its files change.
4. **Our own pieces become plugins.** The music panel, the agents panel, dictation and reports move to the format first, which proves it and keeps the core small.
5. **IPC for plugins and scripts.** `omawin shell <method>` over the menu app's existing signal: `toggle`, `reload`, `list-plugins`, `put-bar-widget`, like `omarchy-shell`.

## Phase 5: Omarchy's developer experience

Borrowed as data where we can, the way the themes were.

1. **Dev environments and Docker databases in Arch too** (they exist on the Windows side): see phase 3.
2. **A Docker TUI on a key** (Omarchy's Super+Shift+D: lazydocker).
3. **Web apps and TUIs** as launcher items with their own icon and window (`omawin install webapp <name> <url>`, Edge `--app=`), launch-or-focus.
4. **The key cheat sheet built from `glazewm/config.yaml`**: each binding gets a description (as Omarchy's `o.bind`), the Shortcuts page reads them, and `launcher/keys.txt` goes.
5. **Notifications and OSD in the shell**: our own toasts (reminders, timers, low battery, install finished) and a volume / brightness OSD drawn by the shell.
6. **Idle lock and screensaver** (Omarchy: 150 s screensaver, 300 s lock), a toggle in the menu.
7. **Window manager parity**: scratchpad (Super+S), window groups / tabs (the fork's old accordion branch is a start), pop-out and layout cycling.
8. **Detect before offering**: menu rows with a condition (`?wsl`, `?docker`), answered in the background and cached, so the menu opens on the last answers, as Omarchy's guards do.
9. **Update channels and undo**: `omawin update` follows release tags (stable, edge) with a restore point first; `omawin uninstall` does what install.md lists by hand.

## Phase 6: files (the finder, Win+F)

The finder on fff is already the file explorer you use: fuzzy search over your folders and repos, a preview, folder and image chips, Ctrl+Enter to show a file in its folder. Make it the place you act on files from, so Explorer is rarely needed. Explorer itself stays (replacing it breaks Windows; [shell-apps.md](shell-apps.md)).

1. **Actions on a result.** A small action list on the selected file or folder (Ctrl+K, or →): open in the editor (`omawin launch editor`), a terminal there (`omawin launch terminal --dir`), open as a project (herdr), copy the path or the relative path, copy the file itself (pastes into Explorer, Teams, mail), rename, send to the Recycle Bin. The common ones get keys of their own.
2. **Browse, not just search.** → goes into a folder and ← goes up, the list showing that folder's contents with the preview beside it (yazi's way), so the finder works as a keyboard file manager when you know roughly where something is.
3. **Search inside files.** A prefix (e.g. `#`) searches contents: fff's own grep if its library offers it, else ripgrep, results by file and line; Enter opens the editor at that line.
4. **Better previews.** Code with the theme's syntax colors, Markdown drawn, a PDF's first page (`Windows.Data.Pdf`), a CSV as a table, Office files and videos by Windows' own thumbnails.
5. **Filters and places.** `.pdf`, `today`, `this week` as filters; your own places in the account folder (`launcher/finder-places.txt`), WSL repos, and pinned favorites at the top of the empty list.
6. **Many at once and drag out.** Mark several (Space), act on all of them; drag a result into another app.
7. **Into file dialogs.** When an app's Open or Save dialog is in front, choosing a folder sends the dialog there, so the finder works inside every app.
8. **Explorer's developer defaults**, for when you do open it: extensions and hidden files shown, opens to your home folder, full path in the title, no Gallery / recommendations, dark or light following the theme; as tweaks with undo.

## Always: following Omarchy

`mjveilleux/omarchy` tracks upstream. At each Omarchy release, a short note in mission-todo.md of what's worth bringing over, checked against this roadmap; data we can import directly (themes, motion values, the menu's sections, the dev-env and database lists, plugin manifests) is imported by a script rather than copied by hand.

## How we'll know

- `omawin profile menu`: under 50 ms from key to pixels, on this PC and the clean runner.
- CI install: desktop up in under 3 minutes, everything in under 10, `omawin check` 0 / 0.
- Window animations at full frame rate in the fork's frame check, and no judder by eye.
- A fresh install, Enter through every question: Arch ready, Win+Enter in Arch as fast as in Windows once Linux is your side.
- One plugin of ours and one written from an Omarchy plugin's idea, installed with `omawin plugin add`.
