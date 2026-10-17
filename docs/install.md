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

Have a user layer (your own git identity, settings, launcher items: [work.md](work.md))? Clone it to `~/omawin-work` first, so install uses it.

`install.ps1` is safe to re-run; every step skips what's already done.
- `-LinkOnly` skips the installs and only relinks and rebuilds.
- `-Step <name>` runs one step.

If it warns that Windows Terminal's settings couldn't be linked (the install shell was running inside Terminal), close Terminal and run `.\install.ps1 -LinkOnly` from a plain console: Win+R, `conhost powershell`.

## The steps (`install/`)

`install.ps1` dot-sources `install/lib.ps1` (step headings, junctions, stub files), then these in order:

| Step | What it does |
|---|---|
| `no-ads` | first, before anything is installed or started: `no-ads.ps1 -UserOnly` (Windows' suggestions off; Copilot off the taskbar and out of the background; the Lenovo / McAfee / Adobe / Visual Studio modules only where that software is found). The admin part is `omawin no-ads` |
| `software` | the core winget packages and the chosen presets' (`packages.ps1`, see Presets below; asks which presets on the first run); other installers (herdr; Claude Code, opencode, harlequin, the Fabric CLI when their preset is picked); JetBrainsMono Nerd Font, per-user. No Chocolatey. Skipped by `-LinkOnly`. |
| `neovim` | Neovim and Neovide config; the one-line `.Rprofile` for RStudio themes |
| `shell` | PowerShell profile, Windows Terminal, noctty's config stub, starship, git (includes, and your name and email: asked once if git has none, written to your own `~/.gitconfig`), Git Bash |
| `desktop` | GlazeWM + Zebar, GlazeWM at sign-in (a scheduled task, `install/login-start.ps1`, so it doesn't wait for Windows' delayed start-up list); our GlazeWM fork ([mjveilleux/glazewm](https://github.com/mjveilleux/glazewm), `install/glazewm-fork.ps1`: it can start before Explorer, so the desktop is up the moment you sign in; signed with a certificate made on and trusted by that PC only, one admin prompt; declined or failed, the official GlazeWM stays and waits for Explorer); Handy (dictation) on PATH, configured |
| `helpers` | `omawin.exe` and the bar's helpers into `~/.local/bin` (on PATH), the `btop` shim, noctty (the terminal, our fork's latest release, into `%LOCALAPPDATA%\Programs\noctty`; `bin/noctty.ps1`), the menu app and the music player (`bin/install-binaries.ps1`: the release's prebuilt file for this PC, checked against its published hash, or a build here when there is none — [releases.md](releases.md)) |
| `look` | the default theme (Starship, on its first background) if none is set, so a new install shows at once that it took; no Recycle Bin icon; the lock-screen rotation task; the taskbar set to auto-hide |
| `other` | `~/.wslconfig` (the user layer's when it has one); Claude Code's rules, skills, settings and plugins from your layers (`omawin claude sync`) |

Then the migrations (below).

By hand, once:
- Open a terminal and run `nvim` once: lazy.nvim installs the plugins, mason.nvim the LSPs.
- **Handy**: open it from the tray and download a speech model (Parakeet V3 for English).
- **WSL**: set up your distro with your own Linux dotfiles.
- **Your user layer** (if you didn't clone it before installing): `~/omawin-work` ([work.md](work.md)), then `.\install.ps1 -LinkOnly`.
- Sign out and back in, or start GlazeWM: it starts Zebar and the menu app.

Not covered (set up by hand): SQL Server tools / ODBC drivers, Docker Desktop, Visual Studio, WSL itself, opencode config, harlequin connection profiles.

## Undoing it

There's no `omawin uninstall` yet (it's on [the to-do list](mission-todo.md)). By hand, in this order:

1. **The official GlazeWM and Zebar back** (if the forks went in): `~\omawin\install\glazewm-fork.ps1 -Restore` and `~\omawin\install\zebar-fork.ps1 -Restore` (an admin prompt each). Then remove the certificate it made: run `certlm.msc` as admin and delete `omawin code signing (<your PC's name>)` from *Trusted Root Certification Authorities* and *Trusted Publishers*; in `certmgr.msc`, delete it from *Personal*.
2. **The sign-in and unlock tasks**: `Unregister-ScheduledTask "omawin GlazeWM ($env:USERNAME)","omawin lock screen ($env:USERNAME)" -Confirm:$false`.
3. **The taskbar**: Settings › Personalization › Taskbar › Taskbar behaviors › untick *Automatically hide the taskbar*. The Recycle Bin icon: Settings › Personalization › Themes › Desktop icon settings.
4. **Your files back**: each config omawin replaced is next to its link as `<path>.backup-<timestamp>` (the PowerShell profile folder, `~/.gitconfig`, Windows Terminal's settings, Neovim's config, `~/.bashrc`, `~/.wslconfig`). Delete the link or stub, rename the backup back.
5. **The environment**: remove `~\.local\bin` from your user PATH, and the user variables `STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`, `GLAZEWM_IPC_PORT` and `ZEBAR_ASSET_PORT` (Settings › System › About › Advanced system settings › Environment Variables).
6. **What it installed**: `winget uninstall <id>` for the packages you don't want (the list is in `packages.ps1`); `%LOCALAPPDATA%\launcher` (omawin's data), `%LOCALAPPDATA%\system-menu` (the menu app), `%LOCALAPPDATA%\Programs\noctty`.
7. **No ads** stays off: those are Windows' own settings, each turned back on where Windows shows it (Settings › Privacy, Personalization › Start, Notifications), and the programs `omawin no-ads` uninstalled can be reinstalled from their makers.
8. Delete `~/omawin`.

## How configs are linked

Configs are **live**: the apps read omawin's configs straight from this repo, so an edit here shows at once. What the apps write themselves lives outside the checkout, so `omawin update` never trips over it: Terminal's settings (its settings UI), `lazy-lock.json` after `:Lazy update` and `lazyvim.json` after `:LazyExtras` (in `%LOCALAPPDATA%\nvim-data`), pwsh's `powershell.config.json` and `Modules\`. Your own changes go in your user layer ([work.md](work.md)), never in the checkout. Directory junctions are used instead of symlinks because they need neither admin nor Developer Mode.

| Repo path | Live location | How |
|---|---|---|
| `nvim/` | `%LOCALAPPDATA%\nvim` (Neovide uses it too); `nvim/lazy-lock.json` and `nvim/lazyvim.json` only seed `%LOCALAPPDATA%\nvim-data\`, where lazy.nvim and LazyVim keep theirs (`nvim/lua/config/lazy.lua`) | junction |
| `neovide/` | `%APPDATA%\neovide` (only Neovide's own `config.toml`) | junction |
| `powershell/*.ps1` | `Documents\PowerShell\`, a folder of its own: `$PROFILE` and `profile.ps1` there are two-line stubs that load the repo's; pwsh's own `powershell.config.json` (seeded once) and `Modules\` stay there | stub |
| `windows-terminal/settings.json` | `%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_*\LocalState\settings.json`: seeded once (`%LOCALAPPDATA%\launcher\seeded-windows-terminal` says so), then Terminal's own; seeded again only if it's gone | copied once |
| `noctty/config.ghostty` | `%LOCALAPPDATA%\noctty\config.ghostty` is a stub that includes it, then the current theme's colors (`%LOCALAPPDATA%\launcher\theme.ghostty`, rendered by `themes/render.ps1`) | stub |
| `herdr/config.toml` | read in place: `HERDR_CONFIG_PATH` points herdr at it (`%APPDATA%\herdr` keeps only its sessions and logs) | env var |
| `glazewm/config.yaml` | `~/.glzr/glazewm/config.yaml`, rendered with the theme's colors plus your user layer's keybindings and window rules (`themes/render.ps1`; GlazeWM starts at sign-in from the scheduled task "omawin GlazeWM") | rendered |
| `themes/templates/windows-terminal.json` | Windows Terminal's fragment folder: the color scheme `omawin` | rendered |
| `zebar/` | `~/.glzr/zebar` (custom pack `tokyo-islands`) | junction |
| `fastfetch/` | `~/.config/fastfetch` | junction |
| `starship/starship.toml` | the `STARSHIP_CONFIG` user env var points here | env var |
| `git/gitconfig`, `git/ignore` | `~/.gitconfig` `[include]`s it (then the user layer's `git/gitconfig`, if any); `core.excludesFile` | git config |
| `bash/bashrc` | `~/.bashrc` is a one-line stub that sources it (Git Bash) | stub |
| `r/rprofile.R` | `Documents\.Rprofile` is a one-line stub that sources it | stub |
| `wsl/.wslconfig` (or the user layer's) | `~/.wslconfig` (memory left at WSL's default, half the RAM) | copied |
| `claude/settings.json` (or the user layer's) | `~/.claude/settings.json` | copied once (Claude rewrites it) |
| Handy settings | `%APPDATA%\com.pais.handy\settings_store.json` | written by install |
| `launcher/`, `themes/`, `bin/` | read in place by the menu app, `omawin.exe` and the scripts | (none) |

Anything already at a live location is moved to `<path>.backup-<timestamp>` first, never deleted. What Zebar writes into its linked folder (WebView caches, `errors.log`, `theme.css`) is gitignored; Zebar is on its way out (mission-todo, track 4). PCs set up before 2026-10-15 had Terminal's LocalState and `Documents\PowerShell` linked into the repo: the migration `2026-10-15-writable-files-out` turns them into folders of their own with the settings you had (copies in `%LOCALAPPDATA%\omawin-backup\<date>\`).

## Presets

`packages.ps1` is a lean core (shell tools, the desktop, the editor and what it needs, Git, the .NET desktop runtime the menu app and the music player run on; the 1 GB SDK only where they have to be built, [releases.md](releases.md)) plus presets, opt-in groups: `python`, `node`, `go`, `elixir`, `r`, `c`, `data` (DuckDB, harlequin, sqlcmd), `typst`, `docker`, `cli-extras`, `ai-agents` (Claude Code, opencode), `music` (yt-dlp and mpv for the bar's player), `dictation` (Handy), `powerbi-fabric`.
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

`omawin update` (system menu > Update > This setup, `bin/update.ps1`) fetches and fast-forwards the checkout, then runs `install.ps1 -LinkOnly`: relinks, takes the release's binaries (or rebuilds) for the pieces whose source changed, runs new migrations. It never stops because the checkout was changed by hand:
- Local changes and new files go into a stash named `omawin update <time>` and are put back after the update. If they clash with it, the checkout is left as the update made it, the stash is kept, and it says how to see, re-apply or drop it ([troubleshooting](../manual/12-troubleshooting.md)).
- Local commits are replayed on top of the update; if they clash, they're kept on a branch `omawin-local/<time>` and the checkout moves to the update.
- An unfinished merge, rebase or cherry-pick is undone first; the repo is never left in the middle of one.
- Offline, detached, or a branch without an upstream: it says so and relinks what's there.

`omawin update -DryRun` says what it would do (it only fetches). It follows the branch's latest commit; release tags and channels come later (mission-todo, track 1).

## Health check and checkpoints

`omawin check` (`check.ps1`) is read-only. It checks:
- the tools on PATH and the config links
- GlazeWM at login
- the helper exes, and whether they're older than their source
- the menu app
- that every script parses, and that the JSON configs and `menu.txt` are valid
- that the migrations have run
- that GlazeWM, Zebar and the menu app are running

`-Quiet` prints only problems; it exits 1 on any failure.

Before a big change, tag a checkpoint (`git tag -a checkpoint-<date>`, then push it) and back up what lives outside the repo.
