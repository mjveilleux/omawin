# Terminal, shell and Neovim

## Terminal: noctty

The terminal is [noctty](https://noctty.com): Ghostty's terminal core in a native Windows app (tabs, splits, a command palette), from our fork ([mjveilleux/noctty](https://github.com/mjveilleux/noctty)): its tab strip takes the theme's colors and font, and it keeps a shell ready for the next window. It's set up like Omarchy's Ghostty: JetBrainsMono, padding, a see-through background, the current theme's colors. Its config is `noctty/config.ghostty`; `Ctrl+Shift+,` reloads it.

- It runs in the background from login with no window, and keeps one PowerShell started ahead of time, so Win+Enter gives a window with its prompt in ~0.3 s, and a new tab in ~0.12 s. The first terminal after login takes ~1 s: there's no spare shell yet, and PowerShell's own start-up is half of that. `omawin profile terminal` measures it (`-Scenario window|cold|tab`).
- Its keys: `Ctrl+Shift+V` paste, `Ctrl+Shift+\` / `Ctrl+Shift+E` split, `Alt+arrows` between panes, `Ctrl+Shift+P` command palette. Tabs are herdr's (below).
- noctty has no tabs of its own (no tab strip; `Ctrl+Shift+T` and the other tab keys pass through): [herdr](https://herdr.dev) is the multiplexer. A **project** (Win+Space › Projects) is a herdr session named after the folder, started with tabs shell, nvim, claude and opencode (`launcher/project-tabs.txt`). It keeps running with no window, so agents carry on, and opening the project again reattaches. herdr's keys: `Ctrl+Space` (as in tmux) then `1..9` (tab N), `c` (new tab), `n` / `p` (next / previous). Its config is `herdr/config.toml` in this repo; it takes its colors from the terminal, so it follows the theme.
- Linux terminals and projects (WSL) open as their own noctty window, with a slightly slower start.
- `omawin update terminal` installs the fork's latest release (checked against its published checksums).

**Win+Enter** opens a terminal in the folder you're working in:
- the focused noctty window's folder
- a Windows Terminal tab's folder or Linux path
- the folder open in File Explorer
- otherwise ~

**Alt+Enter** opens one in ~.

### Another terminal

`omawin default terminal wt` makes Windows Terminal the default instead (System menu › Setup › Defaults); `omawin default terminal noctty` switches back. Everything that opens a terminal follows it. The editor works the same way: `omawin default editor nvim|neovide|code`, and so does the AI agent in Neovim: `omawin default agent opencode|claude`.

Terminals don't run as admin, because GlazeWM can't tile admin windows. In Windows Terminal, use the `PowerShell (Admin)` profile when you need it.

## PowerShell

PowerShell 7 with starship, zoxide, fzf, eza and bat:
- **Shortcuts:** `..`, `...`, `which`, `lg` (lazygit), `reload`.
- **`gacp <msg>`:** git add, commit and push.
- **`iex`:** Elixir's IEx. Piped to, it's still Invoke-Expression, so `irm <url> | iex` works.
- **Starship and zoxide** are cached in `~/.cache/pwsh`; after upgrading either, run `Update-ShellCache`.
- Your own aliases and tool paths (a classic `sqlcmd`, say) go in your account folder's `powershell/profile.ps1` (interactive shells; the profile loads it last) or `powershell/all-hosts.ps1` (every PowerShell, scripts too) ([docs/work.md](../docs/work.md)).

- **`fastfetch`:** this PC and omawin at a glance, Omarchy-style: omawin's logo, then hardware, software (omawin's version, GlazeWM, the theme) and age / uptime / last update, in the theme's colors. Its config is `fastfetch/` in this repo.

Searchable shell keys are in Win+Space → Shortcuts.

## Neovim

LazyVim (Neovim 0.12) plus a personal layer in `nvim/lua/`:
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

Treesitter builds parsers with the tree-sitter CLI and zig. Don't run Neovim from an admin shell: its plugin folders would become admin-owned.
