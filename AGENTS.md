# omawin: notes for agents

omawin ("omakase Windows") is a keyboard-driven Windows developer desktop, after [Omarchy](https://github.com/basecamp/omarchy): GlazeWM tiling, a Zebar bar and dock, a native launcher / system menu / screenshot tool (`menu-app/`), Omarchy's themes, LazyVim. The repo is public and its configs are live: apps read them straight from `~/omawin` through junctions, so editing a file here changes the running desktop.

Read before working:

- [`docs/architecture.md`](docs/architecture.md): how the pieces fit (keys → GlazeWM → `omawin.exe` → the menu app or a script), where state lives
- [`docs/install.md`](docs/install.md): install steps, what's linked where, migrations
- [`docs/themes.md`](docs/themes.md): how a theme reaches every app
- [`docs/work.md`](docs/work.md): your own settings, outside omawin: the PC's shared folder (`C:\ProgramData\omawin\shared`: yours in every Windows account, which every account reads and may change, so nothing secret or employer-specific) and each account's folder (`~/omawin-account`: identity, an employer's or client's launcher items and sites, tool paths, work shares, VPN, reports)
- [`manual/`](manual/): what the user sees and uses; keep it current when behavior changes

## Rules

- **Keys never change by guesswork.** Win+Space = launcher, Win+Alt+Space = system menu, Win+Enter = always the terminal, Win+F = file finder, Win+Shift+S = screenshot, Win+Shift+T = text from the screen, Win+Shift+V = clipboard, Win+M = music. The user dictates, so a key name in a request can be mistranscribed ("enter" may mean "space"); confirm before rebinding anything.
- **Questions go in `open-questions/<slug>.md`**, one file each: the question, the context, the options, and the default you went with. Keep working with that default; delete the file once it's answered.
- **A theme switch never changes a tracked file.** Each theme is one palette (`themes/colors/<name>.toml`, Omarchy's format); apps read it live, or get a copy rendered from a template outside the repo (`themes/render.ps1`: `glazewm/config.yaml` is a template, Windows Terminal uses the scheme `omawin` from a fragment). Never hard-code a theme color in a config: use `{{ accent }}`-style placeholders in a template, or read the palette.
- **Private things stay private:** nothing personal goes in the repo: no names, emails, employers or clients, no paths or accounts of one PC; those go in the account's folder ([`docs/work.md`](docs/work.md)), and that folder's own notes (`~/omawin-account/README.md`, when there is one: read it) say what else is off limits on that PC, such as work repos agents must never run git in. Never commit `.pbix` files or other work documents.
- **The public history started fresh on 2026-10-17.** The old private history is archived in a private repo; never merge or push a branch made before that date (it carries the old commits). Bring its changes over by cherry-picking or rebasing onto `origin/main`. A local pre-push hook refuses such pushes.
- **Ask before driving the live desktop** (sending keys, clicking, opening windows to test). It's the user's working PC. GlazeWM ignores simulated keypresses anyway, so key bindings can only be tested with real keys.
- **After a visible change, show it**: a screenshot of the result.
- Don't run Neovim elevated (plugin folders become admin-owned).

## Conventions

- **Every action has an omawin command.** A new feature gets a row in `bin/commands.txt` (`<group> <name> | <summary> | app … / pwsh <script> / run <program>`), and the keys (`glazewm/config.yaml`), menu rows (`launcher/menu.txt`, action `omawin <command>`) and dock call that name. `omawin help` lists them.
- **Never name a terminal or an editor.** Open them with `omawin launch terminal|project|editor`, which honors the user's default (`omawin default`, `bin/defaults.txt`).
- **The terminal is noctty** (`noctty/config.ghostty`, `bin/noctty.ps1`); Windows Terminal remains a choice. Measure terminal changes with `omawin profile terminal`, never by feel.
- **Call it `omawin.exe` from GlazeWM, the dock and the menu.** GlazeWM runs commands from `~`, where a bare `omawin` resolves to the repo folder and opens it in Explorer.
- **Removing something an older version installed?** Add a migration: `migrations/<yyyy-mm-dd>-<what>.ps1`, safe on a brand-new PC and safe to run twice. `install.ps1` and `omawin update` run the new ones; `%LOCALAPPDATA%\launcher\migrations.txt` records what ran.
- **Install changes** go in the matching step under `install/` (`install.ps1 -Step <name>` reruns one). New tools go in `packages.ps1`: the lean core only if every install needs them, else a preset (`omawin preset`). No Chocolatey. The install scripts must run under Windows PowerShell 5.1 (a new PC has no pwsh yet).
- **JavaScript runs on Bun.** Anything that needs a JavaScript runtime or package runner uses Bun (`bun`, `bunx`, winget `Oven-sh.Bun`), never Deno or a new Node / npm tool. Programs that need a runtime get pointed at Bun (the music player hands yt-dlp `--js-runtimes bun:<path>`).
- **Small native helpers** (`bin/omawin.cs`, `zebar/tokyo-islands/*.cs`) are compiled by the in-box `csc.exe`: C# 5 only. The menu app (`menu-app/`) is .NET 9 / WinUI 3, built by `menu-app\build.ps1` (or `omawin restart menu`).
- **Speed matters.** Anything on a key goes through the always-running menu app or a native exe. Starting PowerShell costs ~0.3 s, so a script only runs where that wait doesn't show.
- **Comments** are plain English and say why; match the density of the file you're in. Markdown in `docs/` and `manual/` is not hard-wrapped.
- User data never goes in the repo: clipboard history, launch history and indexes live in `%LOCALAPPDATA%\launcher\`.

## Doing things

| To | Run |
|---|---|
| see every command | `omawin help` |
| restart the bar after editing `zebar/` | `omawin restart zebar` |
| reload GlazeWM after editing its config | `omawin restart glazewm` |
| rebuild the menu app after editing `menu-app/` | `omawin restart menu` |
| relink / rebuild everything | `omawin install -LinkOnly` |
| check the whole setup | `omawin check` (0 failed, 0 warnings is the bar) |

From an agent's shell, start GUI programs through GlazeWM (`glazewm command shell-exec …`) so they land on the desktop.

## Editing files

- **Line endings:** the repo stores LF; on Windows every file is checked out CRLF (`core.autocrlf=true`, `.gitattributes`), except `bash/` and `*.sh`, which stay LF. A file you write with LF is fine: git normalizes it on commit. Don't convert files by hand.
- `launcher/menu.txt` / `items.txt` hold Nerd Font glyphs: preserve them.
- Bash heredocs (and Python strings that aren't raw) turn `\t`, `\b`, `\r`, `\f` in Windows paths into control characters. Use the Edit tool, or write a Python script to a file with raw strings.
- Git Bash rewrites `/flag` arguments into paths; run `csc.exe` from PowerShell.
