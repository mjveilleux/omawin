# Terminal, shell and Neovim

## Terminal: noctty

The terminal is [noctty](https://noctty.com): Ghostty's terminal core in a native Windows app (tabs, splits, a command palette), from our fork ([mjveilleux/noctty](https://github.com/mjveilleux/noctty)): its tab strip takes the theme's colors and font, and it keeps a shell ready for the next window. It's set up like Omarchy's Ghostty: JetBrainsMono, padding, a see-through background, the current theme's colors. Its config is `config/noctty/config.ghostty`; `Ctrl+Shift+,` reloads it.

- It runs in the background from login with no window, and keeps one PowerShell started ahead of time, so Win+Enter gives a window with its prompt in ~0.3 s, and a new tab in ~0.12 s. The first terminal after login takes ~1 s: there's no spare shell yet, and PowerShell's own start-up is half of that. `omawin profile terminal` measures it (`-Scenario window|cold|tab`).
- Its keys: `Ctrl+Shift+V` paste, `Ctrl+Shift+\` / `Ctrl+Shift+E` split, `Alt+arrows` between panes, `Ctrl+Shift+P` command palette. Tabs are herdr's (below).
- noctty has no tabs of its own (no tab strip; `Ctrl+Shift+T` and the other tab keys pass through): [herdr](https://herdr.dev) is the multiplexer. A **project** (Win+Space › Projects) is a herdr session named after the folder, started with tabs shell, nvim, claude and opencode (`default/launcher/project-tabs.txt`). It keeps running with no window, so agents carry on, and opening the project again reattaches. herdr's keys: `Ctrl+Space` (as in tmux) then `1..9` (tab N), `c` (new tab), `n` / `p` (next / previous). Its config is `config/herdr/config.toml` in this repo; it takes its colors from the terminal, so it follows the theme.
- Linux terminals and projects (WSL) open as their own noctty window; WSL kept warm (below) makes them as quick as PowerShell's.
- `omawin update terminal` installs the fork's latest release (checked against its published checksums).

**Win+Enter** opens a terminal in the folder you're working in, in your default shell (below):
- the focused noctty window's folder
- a Windows Terminal tab's folder or Linux path
- the folder open in File Explorer (a Windows folder opens in Linux as `/mnt/c/…`; a Linux one, `\\wsl.localhost\…`, as itself)
- otherwise your home (Linux's, or Windows')

**Alt+Enter** opens one in ~.

### Linux or PowerShell

Once your Linux in WSL is set up (`omawin preset add wsl`: Arch by default), a new terminal opens **in Linux**. `omawin default shell windows` (System menu › Setup › Defaults) makes it PowerShell again; `omawin default shell linux` goes back. Without a Linux yet, terminals are PowerShell. For one window: `omawin launch terminal --windows` or `--wsl`. A project opens on the side its folder is on (a Linux repo in Linux, a Windows one in PowerShell).

You always know which side you're on:
- **The background:** Linux windows are tinted a little toward the theme's accent, PowerShell's are the theme's own background. It follows a theme switch at the next prompt.
- **The prompt** starts with the system's logo: Arch's or Ubuntu's in green, Windows' in blue.
- **The title:** `arch · ~/src/app` or `pwsh · omawin`, in the bar and in Windows Terminal's tabs.

**Kept warm:** a cold WSL takes ~8 s to start. While Linux is your default shell, `omawin start terminal` (at sign-in) keeps it running, so a Linux window opens at once; this costs the VM's idle memory. `omawin linux stop` stops it until the next Linux terminal (so does right-clicking Linux in the bar); a file `%LOCALAPPDATA%\launcher\linux-warm-off` turns the keep-warm off. Linux's boot doesn't wait for a network (WSL's is Windows', there already). To see the numbers on your PC: `omawin profile linux` times a cold start against a warm one with no window and says what keeping it warm costs in memory (it stops WSL, so it asks first); `omawin profile terminal -Wsl` times a whole Linux window (`-Scenario cold` from a stopped WSL).

**Windows Terminal:** its own profile for your Linux (Terminal makes one per distro) opens in Linux's home; omawin's windows open Linux whichever profile is Terminal's default.

**Your Linux, kept:** `omawin linux backup` exports it to `~/omawin-backups/linux` (kept until you delete it). `omawin linux reset` backs it up, then replaces it with a fresh one set up again and offers to bring `~/src` back; it asks you to type the distro's name first, and only ever resets a Linux omawin made. `omawin update` updates it too (packages, its shell setup, its migrations), and `omawin check` checks it while it runs.

### Another terminal

`omawin default terminal wt` makes Windows Terminal the default instead (System menu › Setup › Defaults); `omawin default terminal noctty` switches back. Everything that opens a terminal follows it. The editor works the same way: `omawin default editor nvim|neovide|code`, and so does the AI agent in Neovim: `omawin default agent opencode|claude`.

Terminals don't run as admin, because GlazeWM can't tile admin windows. In Windows Terminal, use the `PowerShell (Admin)` profile when you need it. If you often work in admin windows, `omawin glazewm admin` puts a GlazeWM for the whole PC in place that can tile them too: it says what it changes and asks for admin once (`omawin glazewm admin-off` undoes it). Nothing else in omawin needs admin.

## PowerShell

PowerShell 7 with starship, zoxide, fzf, eza and bat:
- **Shortcuts:** `..`, `...`, `which`, `lg` (lazygit), `reload`.
- **`gacp <msg>`:** git add, commit and push.
- **`iex`:** Elixir's IEx. Piped to, it's still Invoke-Expression, so `irm <url> | iex` works.
- **Starship and zoxide** are cached in `~/.cache/pwsh`; after upgrading either, run `Update-ShellCache`.
- Your own aliases and tool paths (a classic `sqlcmd`, say) go in your account folder's `powershell/profile.ps1` (interactive shells; the profile loads it last) or `powershell/all-hosts.ps1` (every PowerShell, scripts too) ([docs/work.md](../docs/work.md)).

- **`fastfetch`:** this PC and omawin at a glance, Omarchy-style: omawin's logo, then hardware, software (omawin's version, GlazeWM, the theme) and age / uptime / last update, in the theme's colors. Its config is `config/fastfetch/` in this repo.

Searchable shell keys are in Win+Space → Shortcuts.

## Linux (WSL)

Your Linux has Omarchy's terminal layer, as an Omarchy user has it: the packages from Omarchy's own list that make sense without a desktop (bat, btop, Docker, eza, fd, fzf, gum, herdr, lazygit, mise, Neovim, ripgrep, starship, tmux, tldr, zoxide and more; Arch takes them from Omarchy's repositories, Ubuntu from apt and mise), Omarchy's shell (its aliases, functions, history and keys: `ls` is `eza -lh`, `cd` uses zoxide, `n` opens Neovim, `t` tmux, `d` docker) and Omarchy's configs for btop, lazygit and tmux. On top, omawin's: the same prompt as Windows, the theme, and Windows close at hand:
- `explorer .`, `code .`, `clip` (text into Windows' clipboard), `open <file or URL>` and `omawin <command>` work from Linux. Windows' own PATH is left out (Linux's tools win), so these are small wrappers in `~/.local/bin`.
- **Docker** runs in Linux, started on first use; you're in the `docker` group, so no sudo. A server there is `localhost:<port>` for Windows' browser too (mirrored networking).
- **Languages and databases:** System menu › Install › Development asks whether it goes in Linux or Windows (Linux first while it's your default shell). In Linux it's Omarchy's way: mise for the languages, and Docker DB runs Omarchy's six databases (MySQL, PostgreSQL, Redis, MongoDB, MariaDB, MSSQL) in Docker, on this PC only, so `localhost:5432` works from Windows' apps too. In Linux itself: `omawin-dev-env install node`, `omawin-dev-env install docker-db`, `omawin-dev-env remove …`, `omawin-dev-env check`.
- **Git and SSH** use Windows' sign-in: git pushes with Windows' credential manager, and `ssh` uses Windows' SSH agent (its OpenSSH agent, or 1Password's), so a key added on one side works on both.
- **btop** follows the theme; everything else in the terminal takes the theme's colors already.
- Your own WSL settings go in your account folder: `wsl/wsl.conf` (applied over omawin's `/etc/wsl.conf`) and `wsl/.wslconfig` (used instead of omawin's) ([docs/work.md](../docs/work.md)).

## Neovim

LazyVim (Neovim 0.12) plus a personal layer in `config/nvim/lua/`:
- **The start screen:** procedural (stars, aurora, mountains, firs, bunnies), a panorama on wide windows; the same as on Linux.
- **The colorscheme:** follows the desktop theme live.
- **Neovide:** JetBrainsMono, no title bar, quick animations; `Ctrl+=` / `Ctrl+-` / `Ctrl+0` zoom.
- **Elixir:** with Expert (the official LSP). `Space i s` starts IEx (`iex -S mix` in Mix projects); `Space i r` sends the file or selection.
- **The AI agent:** your agent's own screen in a split on the right (LazyVim's sidekick extra). `Space a a` (or `Ctrl+.`) opens it, `Space a t` sends the line you're on, or the lines you've selected, as a reference it reads (`@file#L3-7`), and `Space a p` picks a prompt: explain, fix, diagnostics, review. Which agent opens is a default: `omawin default agent claude|opencode` (System menu › Setup › Defaults), per account; `Space a s` starts another beside it.
- **Databases:** dadbod on `Space db`.
- **The floating terminal:** `Ctrl+\`.
- **Running code:** `Space r…` runs Lua / Python (uv / poetry aware).
- **SQL** is formatted with sqlfluff.

**In Linux (WSL)** it's the same Neovim and the same config: `omawin preset add wsl` sets up your Linux (Arch by default, or Ubuntu: `omawin default linux`) for your account and links its `~/.config/nvim` to omawin's (Windows' theme and agent default included). Its plugins are its own, installed the first time `nvim` runs there; the markdown preview opens in Windows' browser.

Treesitter's parsers (the languages LazyVim highlights) come prebuilt with the install, so no C compiler is needed. A language beyond LazyVim's list (`:TSInstall <language>`) is built with the tree-sitter CLI and needs a C compiler: `omawin preset add c` (gcc). Don't run Neovim from an admin shell: its plugin folders would become admin-owned.
