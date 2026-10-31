# omawin

**Omakase Windows**: one command turns a Windows 11 PC into a keyboard-driven developer desktop, after [Omarchy](https://omarchy.org) (omakase Arch + Hyprland). Tiling windows and workspaces, a bar like Omarchy's (with your coding agents' usage), an instant launcher and system menu, screenshots and text from the screen, a terminal and Neovim set up and themed, and Windows' ads turned off. One theme runs through every app: **Starship** by default, or any of Omarchy's.

<!-- A screenshot goes here: the desktop with the launcher open. -->

```powershell
irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex
```

Run it from **Windows PowerShell**, then sign out and back in. It takes about a minute: every core tool comes prebuilt in one release bundle, downloaded and unpacked in parallel, with no admin prompt. Read [what it changes](#what-it-changes-on-your-pc) first; try it in a VM if you can.

## What you get

- **Tiling**: [GlazeWM](https://github.com/glzr-io/glazewm) (our [fork](https://github.com/mjveilleux/glazewm), which starts the moment you sign in). Win+1..9 workspaces, Alt+H J K L focus.
- **Launcher** (`Win+Space`): your items and every app, projects, clipboard, servers on localhost, the key cheat sheet. A native app that's always running, so it opens instantly.
- **System menu** (`Win+Alt+Space`): themes, install or remove apps and presets, update, settings, power.
- **Screen tools**: screenshot (`Win+Shift+S`), text from the screen (`Win+Shift+T`), colour picker, QR reader.
- **File finder** (`Win+F`), **clipboard history** (`Win+Shift+V`), **audio** (`Win+A`).
- **A bar like Omarchy 4's**, drawn natively by the menu app, in the theme's colours: the menu, workspaces, the clock, the tray and quick settings, network, battery. The Windows taskbar hides.
- **Terminal**: [noctty](https://github.com/mjveilleux/noctty) (Ghostty's core for Windows) with PowerShell 7 (installed first, and what omawin runs on) and starship; [herdr](https://herdr.dev) for project sessions; Windows Terminal if you prefer it.
- **Neovim** with [LazyVim](https://lazyvim.org), themed, its treesitter parsers prebuilt (no C compiler needed).
- **Opt-in presets**: language toolchains, data tools, AI agents, local dictation (`Win+H`).
- **Plugins in Omarchy's format** (`shell/plugins/`): the first is the music, podcast and radio player (`Win+M`), a Quickshell plugin that omawin's QML host (`src/qs-host`, Rust and Qt) draws in the bar.
- **`omawin`**, a command for everything: `omawin help` lists them; the keys, menus and bar all call it.

## Requirements

- Windows 11 (Home or Pro), x64. ARM64 works, but builds the launcher on the PC.
- Nothing else to install first. The core doesn't use winget; the presets you pick do (every current Windows 11 has it).
- An internet connection and about 1 GB of disk.
- A standard account is fine; run the install from a normal (not "as administrator") PowerShell. **Nothing asks for admin by default**: everything installs for your account. Admin is opt-in (the signed GlazeWM fork, WSL, `omawin no-ads`' policy part), gathered into one prompt that says first what will change.

## Install

The one-liner above:
1. downloads git and PowerShell 7 from the release at once, puts git in, and clones omawin to `~/omawin` (it must live there);
2. sets up your own settings: the PC's **shared folder** (found if your main account made it, else empty or from your private backup) and this account's folder (from a private repo, or fresh; see [Make it yours](#make-it-yours));
3. runs `install.ps1` in PowerShell 7, which asks which presets you want, and your name and email for git.

By hand instead:

```powershell
# with git already installed (any way you like), in a new Windows PowerShell:
git clone https://github.com/mjveilleux/omawin.git $HOME\omawin
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
& $HOME\omawin\install.ps1
```

Then sign out and back in. Every step is safe to run again; `omawin install -Step <name>` reruns one. Details: [docs/install.md](docs/install.md).

## What it changes on your PC

Everything it replaces is moved aside to `<path>.backup-<timestamp>`, never deleted.

- **The taskbar auto-hides** (the bar and the launcher take its place; the bottom edge or the Win key still shows it), and the desktop's icons are hidden (Windows' own *Show desktop icons*: your files stay on the desktop, and `omawin toggle desktop-icons` brings them back).
- **The theme** sets dark or light mode, the wallpaper and the lock screen (which changes on each unlock).
- **At sign-in**: a scheduled task, `omawin GlazeWM (<you>)`, starts the desktop at once; Windows' start-up delay for other apps is turned off. A second task, `omawin lock screen (<you>)`, runs on unlock.
- **GlazeWM**: our fork, installed for your account from the tools bundle. Optionally (admin, your choice) a signed copy replaces `glazewm.exe` in Program Files so it can start before Explorer (the official one is kept as `glazewm.official.exe`). It's signed with a certificate made on your PC, trusted only there: it's kept in the PC's own store, where only admins can use its private key (which can't be exported), and trusted through the machine's Root and Trusted Publishers stores. Its CLI and watcher are replaced too. Without it, the per-user copy starts a few seconds later.
- **Several accounts at once**: with the GlazeWM fork, each Windows account gets its own GlazeWM port (`GLAZEWM_IPC_PORT` in your user environment), so several can be signed in at once, each with its own desktop.
- **No ads** (for you, no admin): Windows' suggestions, tips, promoted apps, the advertising ID, search highlights and the Copilot button off; the PC maker's notifications off (Lenovo, McAfee, Adobe, when present). `omawin no-ads` does the admin part: Windows' consumer-features policy, uninstalling Lenovo Now and McAfee's extras. [manual/11-no-ads.md](manual/11-no-ads.md) lists everything.
- **Your shell**: the PowerShell profile, `~/.gitconfig` (an include, plus your identity), `~/.bashrc`, `~/.wslconfig`, Windows Terminal's settings, Neovim's config; `~/.local/bin` and two variables (`STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`) on your user environment; the execution policy for you set to RemoteSigned; JetBrainsMono Nerd Font (its four faces) installed for you.
- **Programs**: the core tools unpacked into `%LOCALAPPDATA%\omawin\tools` from the release's tools bundle ([`install/tools.txt`](install/tools.txt): PowerShell 7, git and Git Bash, Windows Terminal, Neovim, GlazeWM, noctty, Bun, ripgrep and the rest), the presets you pick from winget ([`install/packages.ps1`](install/packages.ps1)), and a few installers from their own sites (herdr; Claude Code and opencode with the AI preset). Where each came from is recorded, so `omawin pkg update` updates it the same way.

There's no `omawin uninstall` yet. [Undoing it](docs/install.md#undoing-it) lists the steps by hand.

## Keys

| Keys | Does |
|---|---|
| `Win+Space` | launcher |
| `Win+Alt+Space` | system menu |
| `Win+Enter` / `Alt+Enter` | terminal in the focused window's folder / in ~ |
| `Win+F` · `Win+Shift+V` · `Win+A` | files · clipboard history · audio |
| `Win+Shift+S` · `Win+Shift+T` | screenshot · text from the screen |
| `Win+1..9` / `Win+Shift+1..9` | go to / send the window to a workspace (Win+Shift+Alt: send it, stay here) |
| `Alt+H J K L` (or `Win+Arrows`) | focus a window; add `Shift` to move it |
| `Alt+F` · `Alt+Shift+Space` · `Alt+R` | fullscreen · float · resize mode |
| `Alt+Shift+Q` · `Alt+Shift+R` | close the window · reload the window manager |
| `Win+Ctrl+Space` | the theme's next background |

All of them: `Win+Space` → Shortcuts, or [manual/03-keys.md](manual/03-keys.md).

## Presets

`python` `node` `go` `elixir` `r` `c` `data` `typst` `docker` `cli-extras` `ai-agents` `dictation` `powerbi-fabric`. Install asks the first time; later, `omawin preset add <name>` or the system menu's Install section.

## Plugins

Plugins are folders in Omarchy's format (`shell/plugins/<id>`: a `manifest.json` and QML). `omawin plugin list` shows them; `omawin plugin add music` turns one on and installs its programs (also Install › Plugins in the system menu). The first is `omawin.music`, the music, podcast and radio player; it shows in the bar, drawn by omawin's QML host (`omawin-qs`, downloaded the first time a plugin is turned on; nothing otherwise).

## Make it yours

Don't edit `~/omawin`: `omawin update` replaces it. Your things go in two folders of your own: the PC's **shared folder** (`C:\ProgramData\omawin\shared`), what's yours in every Windows account (Claude Code's rules and skills, launcher items and sites), and **this account's folder** (`~/omawin-account`), who you are in that account (git identity, an employer's or client's launcher items, GlazeWM keys, Neovim plugins, PowerShell additions). Several Windows accounts (personal, a client, a job) each run their own omawin, read the shared folder (only the account that made it changes it) and keep their own sign-ins. Either folder can be backed up to a private repo: `omawin shared backup`, `omawin account new -Repo <name>`. [docs/work.md](docs/work.md).

## Updating and health

- `omawin update`: pulls omawin, relinks, takes the newest release's programs, runs migrations. Your own edits in `~/omawin` are stashed and put back. It never asks for admin.
- `omawin pkg update`: updates each program from where it came (the bundle, winget, Scoop, Chocolatey, mise, npm or its own installer).
- `omawin check`: is everything in place and running.
- `omawin profile ram`: what's using memory and the processor, omawin's parts against their budgets.
- Something wrong: [manual/12-troubleshooting.md](manual/12-troubleshooting.md).

## Read more

- [`manual/`](manual/01-welcome.md): using it (keys, launcher, menu, screenshots, bar, themes, dictation, terminal, troubleshooting)
- [`docs/`](docs/architecture.md): how it's built ([architecture](docs/architecture.md), [install](docs/install.md), [themes](docs/themes.md), [releases](docs/releases.md), [your own settings](docs/work.md)), and where it's going ([mission](docs/mission.md), [to do](docs/mission-todo.md))
- [`AGENTS.md`](AGENTS.md): rules and conventions for working on it, for people and coding agents (Claude reads it through `CLAUDE.md`)

## What's where

The layout follows Omarchy's ([plans/repo-layout.md](plans/repo-layout.md)):

| Path | What |
|---|---|
| `bootstrap.ps1` · `install.ps1` | the one-liner · the install |
| `bin/` | the `omawin` command (`omawin.cs`, `commands.txt`) and what it runs: `check.ps1`, `no-ads.ps1`, `update.ps1`, the profilers |
| `config/` | the apps' configs, live-linked: `glazewm/` (keys, rules, a theme template), `nvim/`, `noctty/`, `powershell/`, `windows-terminal/`, `starship/`, `git/`, `bash/`, `herdr/`, `fastfetch/`, `neovide/`, `claude/`, `r/` |
| `default/` | omawin's own data: `launcher/` (`items.txt`, `menu.txt`, `keys.txt`, `sites.txt`, `settings.txt`), `bar/` (the bar's layout) |
| `shell/` | the desktop: `menu-app/` (bar, launcher, file finder, system menu, screenshots, clipboard history) and `plugins/` (Omarchy's format: `omawin.music/`) |
| `src/` | the other compiled parts: `cli/` (the C# command router), `music/` (the music player), `agent/` (omawin's agent, `Ctrl+Win+H`), `bundle/` (the tools bundle builder) |
| `install/` · `migrations/` | the install's steps, `packages.ps1` (core and presets), `tools.txt` (the tools bundle) · one-time changes for existing installs |
| `themes/` · `wsl/` · `build/` · `test/` | Omarchy's themes and their palettes · Linux in WSL · CI's release scripts · unit tests |
| `docs/` · `manual/` · `plans/` · `open-questions/` | how it's built · how to use it · where it's going · decisions waiting for an answer |

## Credits

omawin stands on [Omarchy](https://github.com/basecamp/omarchy) (its themes, and the ideas behind its menus, keys and updates), [GlazeWM](https://github.com/glzr-io/glazewm) (and Zebar, whose bar omawin's began as, before it was drawn natively), [Ghostty](https://ghostty.org) by way of noctty, [LazyVim](https://lazyvim.org), [herdr](https://herdr.dev), [Handy](https://handy.computer), [fff](https://github.com/dmtrKovalenko/fff), [starship](https://starship.rs), [mpv](https://mpv.io) and [yt-dlp](https://github.com/yt-dlp/yt-dlp). Details in [NOTICE](NOTICE).

## License

[MIT](LICENSE).
