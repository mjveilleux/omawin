# Install, links and migrations

## A new machine

```powershell
# 1. From Windows PowerShell:
winget install --id Git.Git -e
# 2. New shell so git is on PATH, then:
git clone https://github.com/mjveilleux/omawin.git $HOME\omawin
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
& $HOME\omawin\install.ps1
```

Or run the one-liner, `irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex`, which does those steps itself and asks for your layers (optional). Keep it at `~/omawin`, because a few paths point there: the Terminal profile "rename", Neovim's live theme, and `omawin.exe`'s default.

`omawin.exe`, the bar's helpers, the menu app and the music player come from the newest release, downloaded and checked against the hashes published with it, so no .NET SDK and no compile ([releases.md](releases.md)). If there is no release for the PC (ARM64 today, a fork, offline), they're built here instead. `OMAWIN_PREBUILT=0` always builds.

Your own settings live outside omawin, in the PC's shared folder and this account's folder ([work.md](work.md)); `bootstrap.ps1` sets both up. Installing by hand with this account's folder in a private repo? Clone it to `~/omawin-account` first, so install uses it.

`install.ps1` is safe to re-run; every step skips what's already done.
- `-LinkOnly` skips the installs and only relinks and rebuilds.
- `-Step <name>` runs one step.

It asks everything up front, then needs nobody:
1. **The questions**: which presets and which Linux for WSL, Arch (the default), Ubuntu or none (first install only), and your git name and email (only if git has none).
2. **One admin prompt**, when anything needs admin (`install/admin.ps1`). It says what for, Windows asks once, and `install/admin-batch.ps1` does it all in the background, hidden, while the steps carry on: the whole-PC packages (`$WholePC` in `install/packages.ps1`, `winget --scope machine`), omawin's GlazeWM and Zebar, and WSL itself when the `wsl` preset is picked and the PC hasn't got it (`wsl --install --no-distribution`). In a standard account that prompt takes an admin's password and the batch runs as that admin, so it only does what's the same for every account; everything per-account stays in the steps. It's the only prompt: after it, approved or not, nothing asks for admin again (install.ps1 sets `OMAWIN_NO_ADMIN` for the steps, the scripts they run and the migrations). Declined, or no admin password at hand, is the non-admin path: the whole-PC packages install for this account where they have such an installer (`winget --scope user`), and what only admin can do is left out and said in the summary (the forks: the official GlazeWM and Zebar stay; WSL). Run the install again, or `-Step desktop`, to get them with one prompt. Like any admin prompt for a script, it runs this account's `~/omawin` as admin: on a PC shared with an account you don't trust, run the install there without an admin password, or from an account you do.
3. **The steps**, below.
4. **The migrations**.

If it warns that Windows Terminal's settings couldn't be linked (the install shell was running inside Terminal), close Terminal and run `.\install.ps1 -LinkOnly` from a plain console: Win+R, `conhost powershell`.

## The steps (`install/`)

The steps are listed in `install/steps.txt`, one line each (`name | what it does | seconds on a new PC`), in the order they run; each is `install/<name>.ps1`. `install.ps1`, its `-Step`, the install screen and `omawin check` all read that list, so adding or removing a step is a line there and its file. `install.ps1` dot-sources `install/lib.ps1` (output, junctions, stub files), then runs them:

| Step | What it does |
|---|---|
| `no-ads` | first, before anything is installed or started: `bin/no-ads.ps1 -UserOnly` (Windows' suggestions off; Copilot off the taskbar and out of the background; the Lenovo / McAfee / Adobe / Visual Studio modules only where that software is found). The admin part is `omawin no-ads` |
| `software` | the core winget packages and the chosen presets' (`install/packages.ps1`, see Presets below), four at once, with the whole-PC ones installing in the admin step alongside; other installers (herdr; Claude Code, opencode, harlequin, the Fabric CLI when their preset is picked); JetBrainsMono Nerd Font, per-user; then Neovim's and Neovide's config and the one-line `.Rprofile` for RStudio themes. No Chocolatey. `-LinkOnly` skips the installs and keeps the config. |
| `shell` | PowerShell profile, Windows Terminal, noctty's config stub, starship, git (includes, and your name and email: asked once if git has none, written to your own `~/.gitconfig`), Git Bash |
| `desktop` | GlazeWM + Zebar, GlazeWM at sign-in (a scheduled task, `install/login-start.ps1`, so it doesn't wait for Windows' delayed start-up list); our GlazeWM fork ([mjveilleux/glazewm](https://github.com/mjveilleux/glazewm), `install/glazewm-fork.ps1`: it can start before Explorer, so the desktop is up the moment you sign in; signed with a certificate made on that PC, in the PC's own store where only admins can use its key, and trusted there only) and our Zebar fork (`install/zebar-fork.ps1`: each account's bar on its own ports), both put in by the admin step; declined or failed, the official ones stay and GlazeWM waits for Explorer; Handy (dictation) on PATH, configured |
| `helpers` | `omawin.exe` and the bar's helpers into `~/.local/bin` (on PATH), noctty (the terminal, our fork's latest release, into `%LOCALAPPDATA%\Programs\noctty`; `bin/noctty.ps1`), the menu app and the music player (`bin/install-binaries.ps1`: the release's prebuilt file for this PC, checked against its published hash, or a build here when there is none — [releases.md](releases.md)) |
| `look` | the default theme (Starship, on its first background) if none is set, so a new install shows at once that it took; no Recycle Bin icon, and no desktop icons (`bin/desktop-icons.ps1 -Default`: hidden, unless you chose to show them); the lock-screen rotation task; the taskbar set to auto-hide |
| `other` | `~/.wslconfig` (the account folder's when it has one); Claude Code's rules, skills, settings and plugins from the shared folder and this account's (`omawin claude sync`) |

Then the migrations (below).

### What it shows

In a console at least 80 by 24 the install draws one screen and redraws it in place, smoothly: a small program, `install/screen.cs`, built once with Windows' own `csc.exe` into `%LOCALAPPDATA%\launcher` (again whenever `screen.cs` changes), which draws at up to 60 frames a second while the installer sends it one line per event (`install/board.ps1`). On the left, everything the install touches, by group (Programs, Tools, Settings, Desktop, Windows), each with a count and a bar and each item marked new, already there or failed; what's running now, with a sliding light; and the step's last lines. Steps report items with `Add-InstallItem` or `Invoke-InstallItem` (ui.ps1, board.ps1); links and stub files report themselves. On the right, the omawin logo, lit by a snake that glides along its strokes as the install gets further, the percentage and time left, and every step on a line. At the end the screen gives way to a plain summary of everything, new and already there, the warnings and what to do next, which stays in the terminal's history. It keeps nothing of its own: the steps come from `steps.txt`, and each step's share of the bar from how long it took last time (`%LOCALAPPDATA%\launcher\install-times.txt`; the first install goes by `steps.txt`'s guesses). No compiler, a console too small, or the program gone mid-install: the line-by-line face below.

In a smaller console, and for `-Step`, it prints line after line instead (`install/ui.ps1`: each step with its time, a progress bar, a spinner while winget works, a finish box); `OMAWIN_BOARD=0` asks for that. `bootstrap.ps1` opens with the omawin banner (a small copy: it runs before the clone). When the output isn't a console (`omawin update`'s log, an agent), or `OMAWIN_PLAIN=1` or `NO_COLOR` is set, it prints the plain `==> step` lines instead; `OMAWIN_PLAIN=0` forces colors, and `OMAWIN_SCREEN=120x36` gives the screen a fixed size (to record it into a file).

Every line, with winget's output for a failed package, also goes to `%LOCALAPPDATA%\launcher\install.log`, timestamped (moved to `install.log.old` past 1 MB). `install\demo-ui.ps1` plays the whole screen with pretend steps and packages and installs nothing (`-Fail` for a failing step, `-Speed 2` to hurry).

By hand, once:
- Open a terminal and run `nvim` once: lazy.nvim installs the plugins, mason.nvim the LSPs.
- **Handy**: nothing; the desktop step downloads its speech model (Parakeet V3) when none is picked. Another one: open Handy from the tray.
- **WSL**: nothing, when you picked a Linux at the start: WSL itself comes with the admin prompt, and the Linux is set up for your account (the `wsl` preset, below). Just installed, WSL works after a restart: the Linux then finishes setting up by itself at your next sign-in.
- **Your account folder** (if you didn't clone it before installing): `~/omawin-account` ([work.md](work.md)), then `.\install.ps1 -LinkOnly`.
- Sign out and back in, or start GlazeWM: it starts Zebar and the menu app.

Not covered (set up by hand): SQL Server tools / ODBC drivers, Docker Desktop, Visual Studio, opencode config, harlequin connection profiles.

## Undoing it

There's no `omawin uninstall` yet (it's on [the to-do list](mission-todo.md)). By hand, in this order:

1. **The official GlazeWM and Zebar back** (if the forks went in): `~\omawin\install\glazewm-fork.ps1 -Restore` and `~\omawin\install\zebar-fork.ps1 -Restore` (an admin prompt each). Then remove the certificate it made: run `certlm.msc` as admin and delete `omawin code signing (<your PC's name>)` from *Personal*, *Trusted Root Certification Authorities* and *Trusted Publishers*; and `C:\ProgramData\omawin\glazewm-fork.txt` and `zebar-fork.txt`.
2. **The sign-in and unlock tasks**: `Unregister-ScheduledTask "omawin GlazeWM ($env:USERNAME)","omawin lock screen ($env:USERNAME)" -Confirm:$false`.
3. **The taskbar**: Settings › Personalization › Taskbar › Taskbar behaviors › untick *Automatically hide the taskbar*. The Recycle Bin icon: Settings › Personalization › Themes › Desktop icon settings. The desktop's icons: `omawin toggle desktop-icons show` (or right-click the desktop › View › Show desktop icons).
4. **Your files back**: each config omawin replaced is next to its link as `<path>.backup-<timestamp>` (the PowerShell profile folder, `~/.gitconfig`, Windows Terminal's settings, Neovim's config, `~/.bashrc`, `~/.wslconfig`). Delete the link or stub, rename the backup back.
5. **The environment**: remove `~\.local\bin` from your user PATH, and the user variables `STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`, `GLAZEWM_IPC_PORT` and `ZEBAR_ASSET_PORT` (Settings › System › About › Advanced system settings › Environment Variables).
6. **What it installed**: `winget uninstall <id>` for the packages you don't want (the list is in `install/packages.ps1`); `%LOCALAPPDATA%\launcher` (omawin's data), `%LOCALAPPDATA%\system-menu` (the menu app), `%LOCALAPPDATA%\Programs\noctty`.
7. **No ads** stays off: those are Windows' own settings, each turned back on where Windows shows it (Settings › Privacy, Personalization › Start, Notifications), and the programs `omawin no-ads` uninstalled can be reinstalled from their makers.
8. Delete `~/omawin`. Your own settings ([work.md](work.md)) are yours to keep or delete: `~/omawin-account`, and `C:\ProgramData\omawin\shared` once no account on the PC uses omawin (back it up first if you want it again: `omawin shared backup`).

## How configs are linked

Configs are **live**: the apps read omawin's configs straight from this repo, so an edit here shows at once. What the apps write themselves lives outside the checkout, so `omawin update` never trips over it: Terminal's settings (its settings UI), `lazy-lock.json` after `:Lazy update` and `lazyvim.json` after `:LazyExtras` (in `%LOCALAPPDATA%\nvim-data`), pwsh's `powershell.config.json` and `Modules\`. Your own changes go in your account folder ([work.md](work.md)), never in the checkout. Directory junctions are used instead of symlinks because they need neither admin nor Developer Mode.

| Repo path | Live location | How |
|---|---|---|
| `config/nvim/` | `%LOCALAPPDATA%\nvim` (Neovide uses it too); `config/nvim/lazy-lock.json` and `config/nvim/lazyvim.json` only seed `%LOCALAPPDATA%\nvim-data\`, where lazy.nvim and LazyVim keep theirs (`config/nvim/lua/config/lazy.lua`); an extra added to `config/nvim/lazyvim.json` later is still turned on there once, at the next nvim start (noted in `omawin-extras-offered.txt`, so one you then turn off stays off) | junction |
| `config/neovide/` | `%APPDATA%\neovide` (only Neovide's own `config.toml`) | junction |
| `config/powershell/*.ps1` | `Documents\PowerShell\`, a folder of its own: `$PROFILE` and `profile.ps1` there are two-line stubs that load the repo's; pwsh's own `powershell.config.json` (seeded once) and `Modules\` stay there | stub |
| `config/windows-terminal/settings.json` | `%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_*\LocalState\settings.json`: seeded once (`%LOCALAPPDATA%\launcher\seeded-windows-terminal` says so), then Terminal's own; seeded again only if it's gone | copied once |
| `config/noctty/config.ghostty` | `%LOCALAPPDATA%\noctty\config.ghostty` is a stub that includes it, then the current theme's colors (`%LOCALAPPDATA%\launcher\theme.ghostty`, rendered by `themes/render.ps1`) | stub |
| `config/herdr/config.toml` | read in place: `HERDR_CONFIG_PATH` points herdr at it (`%APPDATA%\herdr` keeps only its sessions and logs) | env var |
| `config/glazewm/config.yaml` | `~/.glzr/glazewm/config.yaml`, rendered with the theme's colors plus your account folder's keybindings and window rules (`themes/render.ps1`; GlazeWM starts at sign-in from the scheduled task "omawin GlazeWM") | rendered |
| `themes/templates/windows-terminal.json` | Windows Terminal's fragment folder: the color scheme `omawin` | rendered |
| `zebar/` | `~/.glzr/zebar` (custom pack `tokyo-islands`) | junction |
| `config/fastfetch/` | `~/.config/fastfetch` | junction |
| `config/starship/starship.toml` | the `STARSHIP_CONFIG` user env var points here | env var |
| `config/git/gitconfig`, `config/git/ignore` | `~/.gitconfig` `[include]`s it (then the account folder's `git/gitconfig`, if any); `core.excludesFile` | git config |
| `config/bash/bashrc` | `~/.bashrc` is a one-line stub that sources it (Git Bash) | stub |
| `config/r/rprofile.R` | `Documents\.Rprofile` is a one-line stub that sources it | stub |
| `wsl/.wslconfig` (or the account folder's) | `~/.wslconfig` (memory left at WSL's default, half the RAM) | copied |
| `config/claude/settings.json` (and the shared folder's and the account folder's) | `~/.claude/settings.json` | merged (`omawin claude sync`; Claude rewrites it) |
| Handy settings | `%APPDATA%\com.pais.handy\settings_store.json` | written by install |
| `default/launcher/`, `themes/`, `bin/` | read in place by the menu app, `omawin.exe` and the scripts | (none) |

Anything already at a live location is moved to `<path>.backup-<timestamp>` first, never deleted. What Zebar writes into its linked folder (WebView caches, `errors.log`, `theme.css`) is gitignored; Zebar is on its way out (mission-todo, track 4). PCs set up before 2026-10-15 had Terminal's LocalState and `Documents\PowerShell` linked into the repo: the migration `2026-10-15-writable-files-out` turns them into folders of their own with the settings you had (copies in `%LOCALAPPDATA%\omawin-backup\<date>\`).

## Presets

`install/packages.ps1` is a lean core (shell tools, the desktop, the editor and what it needs, Git, the .NET desktop runtime the menu app and the music player run on; the 1 GB SDK only where they have to be built, [releases.md](releases.md)) plus presets, opt-in groups: `python`, `node`, `go`, `elixir`, `r`, `c`, `data` (DuckDB, harlequin, sqlcmd), `typst`, `docker`, `cli-extras`, `ai-agents` (Claude Code, opencode), `music` (yt-dlp and mpv for the bar's player), `dictation` (Handy), `wsl`, `powerbi-fabric`.
- `wsl`: Linux in WSL for this account, Arch (the default) or Ubuntu: `omawin default linux arch|ubuntu` picks it (`bin/defaults.txt`), `wsl/install.ps1` installs it, then `wsl/setup.sh` sets it up inside: the shell and Neovim tools, a Linux user named after the account (sudo without a password: Windows reaches root there without one anyway, `wsl -u root`, so a password would guard nothing and the install never stops to ask), `~/.config/nvim` linked to the repo's `config/nvim/` (`config/nvim/lua/omawin.lua` finds Windows' folders from there, so the theme and the agent default follow Windows'), the prompt (`config/bash/bashrc`), herdr for Linux projects, the agents this account has on Windows, and git with Windows' sign-in and this account's identity.
  - **Arch** takes its packages from Omarchy's repositories, as Omarchy does: its own (`[omarchy]`, `pkgs.omarchy.org`: herdr, `omarchy-keyring`) and its copy of Arch's, the stable channel (`stable-mirror.omarchy.org`), which moves forward a tested set at a time. Signed with Omarchy's key, fetched by fingerprint from keys.openpgp.org as Omarchy's own updater does. No AUR. Arch's own `pacman.conf` and mirror list are kept as `*.arch`. `omawin update` brings its packages up to date (`setup.sh update`, as root; only an Arch omawin set up).
  - **Ubuntu**: Neovim and tree-sitter from their releases (Ubuntu's are too old), the rest from apt.
  - **A distro you already had is yours**: omawin uses it as it is (terminals and projects open in it) and never sets it up or changes it; no pick yet means the one you have, so nothing moves away from your files. Only `wsl\install.ps1 -Adopt` sets one of yours up, and then your configs are moved aside, never deleted. omawin's own distros say so in `/etc/omawin`. Nothing omawin runs removes a distro.
  - **WSL itself** takes admin and a restart. The install's one admin prompt puts it in; `omawin preset add wsl` on a PC without it asks once on its own. Until Windows restarts, `wsl/install.ps1` only arranges to carry on at the next sign-in (this account's RunOnce, no admin; `%LOCALAPPDATA%\launcher\wsl-restart` says since when).
- `omawin preset list` shows them, the picked ones marked; `omawin preset add <name>` picks one and installs it now; `omawin preset remove <name>` unpicks it (what it installed stays). Also in the system menu: Install > Presets.
- The picks are per PC, in `%LOCALAPPDATA%\launcher\presets.txt`; `install.ps1` installs the core and those. The first install asks (or starts with none when nobody's there to answer).
- `omawin check` expects the tools of the picked presets only.
- A package whose command is already on the PATH (installed some other way) counts as installed.

## Migrations (`migrations/`)

`install.ps1` adds and links; it never removes what an older version of the setup left behind. A migration does that, as in Omarchy: one small script per change, named `<yyyy-mm-dd>-<what>.ps1`, run once per PC, oldest first.
- `install.ps1` runs the pending ones last, and so does `omawin update`.
- `omawin migrate` runs them on its own; `-List` shows which have run.
- `%LOCALAPPDATA%\launcher\migrations.txt` records the ones done.

Every migration must be safe on a brand-new PC (nothing to clean up) and safe to run twice. A failing one stops the run; the rest wait for the next time.

## Updating

`omawin update` (system menu > Update > This setup, `bin/update.ps1`) fetches and fast-forwards the checkout, then runs `install.ps1 -LinkOnly -UpdateInfo <file>`: relinks, takes the release's binaries (or rebuilds) for the pieces whose source changed, upgrades yt-dlp, runs new migrations. It shows on the install screen: the pull is its first step (its notes and warnings with it), each change it brought is an item under **Changes**, and the finale says "omawin is updated: N changes" with what's new, or "already up to date". The summary it leaves lists the changes one a line. It never stops because the checkout was changed by hand:
- Local changes and new files go into a stash named `omawin update <time>` and are put back after the update. If they clash with it, the checkout is left as the update made it, the stash is kept, and it says how to see, re-apply or drop it ([troubleshooting](../manual/12-troubleshooting.md)).
- Local commits are replayed on top of the update; if they clash, they're kept on a branch `omawin-local/<time>` and the checkout moves to the update.
- An unfinished merge, rebase or cherry-pick is undone first; the repo is never left in the middle of one.
- Offline, detached, or a branch without an upstream: it says so and relinks what's there.

`omawin update -DryRun` says what it would do (it only fetches). It follows the branch's latest commit; release tags and channels come later (mission-todo, track 1).

## Health check and checkpoints

`omawin check` (`bin/check.ps1`) is read-only. It checks:
- the tools on PATH and the config links
- GlazeWM at login
- the helper exes, and whether they're older than their source
- the menu app
- that every script parses, and that the JSON configs and `menu.txt` are valid
- that the migrations have run
- that GlazeWM, Zebar and the menu app are running

`-Quiet` prints only problems; it exits 1 on any failure.

Before a big change, tag a checkpoint (`git tag -a checkpoint-<date>`, then push it) and back up what lives outside the repo.
