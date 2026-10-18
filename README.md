# omawin

**Omakase Windows**: one command turns a Windows 11 PC into a keyboard-driven developer desktop, after [Omarchy](https://omarchy.org) (omakase Arch + Hyprland). Tiling windows and workspaces, a bar and a dock, an instant launcher and system menu, screenshots and text from the screen, a terminal and Neovim set up and themed, and Windows' ads turned off. One theme runs through every app: **Starship** by default, or any of Omarchy's.

<!-- A screenshot goes here: the desktop with the launcher open. -->

```powershell
irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex
```

Run it from **Windows PowerShell**, then sign out and back in. Read [what it changes](#what-it-changes-on-your-pc) first; try it in a VM if you can.

## What you get

- **Tiling**: [GlazeWM](https://github.com/glzr-io/glazewm) (our [fork](https://github.com/mjveilleux/glazewm), which starts the moment you sign in). Alt+1..9 workspaces, Alt+H J K L focus.
- **Launcher** (`Win+Space`): your items and every app, projects, clipboard, servers on localhost, the key cheat sheet. A native app that's always running, so it opens instantly.
- **System menu** (`Win+Alt+Space`): themes, install or remove apps and presets, update, settings, power.
- **Screen tools**: screenshot (`Win+Shift+S`), text from the screen (`Win+Shift+T`), colour picker, QR reader.
- **File finder** (`Win+F`), **clipboard history** (`Win+Shift+V`), **audio** (`Win+A`).
- **A bar and a dock** ([Zebar](https://github.com/glzr-io/zebar)) in the theme's colours: workspaces, now playing, CPU, memory, network, battery, the tray. The Windows taskbar hides.
- **Terminal**: [noctty](https://github.com/mjveilleux/noctty) (Ghostty's core for Windows) with PowerShell 7 and starship; [herdr](https://herdr.dev) for project sessions; Windows Terminal if you prefer it.
- **Neovim** with [LazyVim](https://lazyvim.org), themed.
- **Opt-in presets**: language toolchains, data tools, AI agents, a music and podcast player in the bar (`Win+M`), local dictation (`Win+H`).
- **`omawin`**, a command for everything: `omawin help` lists them; the keys, menus and dock all call it.

## Requirements

- Windows 11 (Home or Pro), x64. ARM64 works, but builds the launcher and music player on the PC.
- winget (App Installer, from the Microsoft Store; every current Windows 11 has it).
- An internet connection and a few GB of disk (the core installs about 25 packages).
- A standard account is fine; run the install from a normal (not "as administrator") PowerShell. Expect one Windows admin prompt, at the start, for what every account shares (the programs that install for the whole PC, and the GlazeWM and Zebar forks); `omawin no-ads` asks for another.

## Install

The one-liner above:
1. installs git and clones omawin to `~/omawin` (it must live there);
2. sets up your own settings: the PC's **shared folder** (found if your main account made it, else empty or from your private backup) and this account's folder (from a private repo, or fresh; see [Make it yours](#make-it-yours));
3. runs `install.ps1`, which asks which presets you want, and your name and email for git.

By hand instead:

```powershell
winget install --id Git.Git -e        # then open a new Windows PowerShell
git clone https://github.com/mjveilleux/omawin.git $HOME\omawin
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
& $HOME\omawin\install.ps1
```

Then sign out and back in. Every step is safe to run again; `omawin install -Step <name>` reruns one. Details: [docs/install.md](docs/install.md).

## What it changes on your PC

Everything it replaces is moved aside to `<path>.backup-<timestamp>`, never deleted.

- **The taskbar auto-hides** (the bar and dock take its place; the bottom edge or the Win key still shows it), and the desktop's Recycle Bin icon goes.
- **The theme** sets dark or light mode, the wallpaper and the lock screen (which changes on each unlock).
- **At sign-in**: a scheduled task, `omawin GlazeWM (<you>)`, starts the desktop at once; Windows' start-up delay for other apps is turned off. A second task, `omawin lock screen (<you>)`, runs on unlock.
- **The GlazeWM fork** replaces `glazewm.exe` in Program Files (the official one is kept as `glazewm.official.exe`). It's signed with a certificate made on your PC, trusted only there: it's kept in the PC's own store, where only admins can use its private key (which can't be exported), and trusted through the machine's Root and Trusted Publishers stores. Its CLI and watcher are replaced too. Decline the admin prompt and the official GlazeWM stays.
- **The Zebar fork** replaces `zebar.exe` (the official one kept as `zebar.official.exe`; the same admin prompt). With both forks, each Windows account gets its own GlazeWM and Zebar ports (`GLAZEWM_IPC_PORT`, `ZEBAR_ASSET_PORT` in your user environment), so several accounts can be signed in at once, each with its own desktop.
- **No ads** (for you, no admin): Windows' suggestions, tips, promoted apps, the advertising ID, search highlights and the Copilot button off; the PC maker's notifications off (Lenovo, McAfee, Adobe, when present). `omawin no-ads` does the admin part: Windows' consumer-features policy, uninstalling Lenovo Now and McAfee's extras. [manual/11-no-ads.md](manual/11-no-ads.md) lists everything.
- **Your shell**: the PowerShell profile, `~/.gitconfig` (an include, plus your identity), `~/.bashrc`, `~/.wslconfig`, Windows Terminal's settings, Neovim's config; `~/.local/bin` and two variables (`STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`) on your user environment; the execution policy for you set to RemoteSigned; JetBrainsMono Nerd Font installed for you.
- **Packages** from winget ([`packages.ps1`](packages.ps1): a lean core plus the presets you pick), and a few installers run from their own sites (herdr; Claude Code and opencode with the AI preset).

There's no `omawin uninstall` yet. [Undoing it](docs/install.md#undoing-it) lists the steps by hand.

## Keys

| Keys | Does |
|---|---|
| `Win+Space` | launcher |
| `Win+Alt+Space` | system menu |
| `Win+Enter` / `Alt+Enter` | terminal in the focused window's folder / in ~ |
| `Win+F` · `Win+Shift+V` · `Win+A` | files · clipboard history · audio |
| `Win+Shift+S` · `Win+Shift+T` | screenshot · text from the screen |
| `Alt+1..9` / `Alt+Shift+1..9` | go to / send the window to a workspace |
| `Alt+H J K L` (or `Win+Arrows`) | focus a window; add `Shift` to move it |
| `Alt+F` · `Alt+Shift+Space` · `Alt+R` | fullscreen · float · resize mode |
| `Alt+Shift+Q` · `Alt+Shift+R` | close the window · reload the window manager |
| `Win+Ctrl+Space` | the theme's next background |

All of them: `Win+Space` → Shortcuts, or [manual/03-keys.md](manual/03-keys.md).

## Presets

`python` `node` `go` `elixir` `r` `c` `data` `typst` `docker` `cli-extras` `ai-agents` `music` `dictation` `powerbi-fabric`. Install asks the first time; later, `omawin preset add <name>` or the system menu's Install section.

## Make it yours

Don't edit `~/omawin`: `omawin update` replaces it. Your things go in two folders of your own: the PC's **shared folder** (`C:\ProgramData\omawin\shared`), what's yours in every Windows account (Claude Code's rules and skills, launcher items and sites), and **this account's folder** (`~/omawin-account`), who you are in that account (git identity, an employer's or client's launcher items, GlazeWM keys, Neovim plugins, PowerShell additions). Several Windows accounts (personal, a client, a job) each run their own omawin, read the shared folder (only the account that made it changes it) and keep their own sign-ins. Either folder can be backed up to a private repo: `omawin shared backup`, `omawin account new -Repo <name>`. [docs/work.md](docs/work.md).

## Updating and health

- `omawin update`: pulls omawin, relinks, takes the newest release's programs, runs migrations. Your own edits in `~/omawin` are stashed and put back.
- `omawin check`: is everything in place and running.
- `omawin profile ram`: what's using memory and the processor, omawin's parts against their budgets.
- Something wrong: [manual/12-troubleshooting.md](manual/12-troubleshooting.md).

## Read more

- [`manual/`](manual/01-welcome.md): using it (keys, launcher, menu, screenshots, bar, themes, dictation, terminal, troubleshooting)
- [`docs/`](docs/architecture.md): how it's built ([architecture](docs/architecture.md), [install](docs/install.md), [themes](docs/themes.md), [releases](docs/releases.md), [your own settings](docs/work.md)), and where it's going ([mission](docs/mission.md), [to do](docs/mission-todo.md))
- [`AGENTS.md`](AGENTS.md): rules and conventions for working on it, for people and coding agents (Claude reads it through `CLAUDE.md`)

## What's where

| Path | What |
|---|---|
| `bin/` | the `omawin` command (`omawin.cs`, `commands.txt`), update, migrate, profilers |
| `menu-app/` | the native app: launcher, file finder, system menu, screenshots, clipboard history |
| `launcher/` | its data (`items.txt`, `menu.txt`, `keys.txt`, `sites.txt`) |
| `glazewm/` · `zebar/` | window manager config and keys · the bar and dock |
| `themes/` | Omarchy's themes and `set-theme.ps1` |
| `install.ps1` · `install/` · `packages.ps1` · `migrations/` | setup, its steps, the packages, one-time changes for existing installs |
| `bootstrap.ps1` · `check.ps1` · `no-ads.ps1` | the one-liner · health check · ads off |
| `noctty/` · `herdr/` | the terminal's config · the multiplexer's |
| `music/` · `agent/` | the bar's music player · omawin's agent (`Ctrl+Win+H`) |
| `nvim/` `neovide/` `powershell/` `windows-terminal/` `starship/` `git/` `bash/` `r/` `wsl/` `claude/` | app configs, live-linked |
| `open-questions/` | decisions waiting for an answer |

## Credits

omawin stands on [Omarchy](https://github.com/basecamp/omarchy) (its themes, and the ideas behind its menus, keys and updates), [GlazeWM and Zebar](https://github.com/glzr-io), [Ghostty](https://ghostty.org) by way of noctty, [LazyVim](https://lazyvim.org), [herdr](https://herdr.dev), [Handy](https://handy.computer), [fff](https://github.com/dmtrKovalenko/fff), [starship](https://starship.rs), [mpv](https://mpv.io) and [yt-dlp](https://github.com/yt-dlp/yt-dlp). Details in [NOTICE](NOTICE).

## License

[MIT](LICENSE).
