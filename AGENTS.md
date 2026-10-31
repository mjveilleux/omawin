# omawin: notes for agents

omawin ("omakase Windows") is a keyboard-driven Windows developer desktop, after [Omarchy](https://github.com/basecamp/omarchy): GlazeWM tiling, a native bar, launcher, system menu and screenshot tool (`shell/menu-app/`), Omarchy's themes, LazyVim. The repo is public and its configs are live: apps read them straight from `~/omawin` through junctions, so editing a file here changes the running desktop.

Read before working:

- [`docs/architecture.md`](docs/architecture.md): how the pieces fit (keys → GlazeWM → `omawin.exe` → the menu app or a script), where state lives
- [`docs/install.md`](docs/install.md): install steps, what's linked where, migrations
- [`docs/themes.md`](docs/themes.md): how a theme reaches every app
- [`docs/work.md`](docs/work.md): your own settings, outside omawin: the PC's shared folder (`C:\ProgramData\omawin\shared`: yours in every Windows account, which every account reads and only the account that made it may change, so nothing secret or employer-specific) and each account's folder (`~/omawin-account`: identity, an employer's or client's launcher items and sites, tool paths, work shares, VPN, reports)
- [`manual/`](manual/): what the user sees and uses; keep it current when behavior changes
- [`plans/roadmap.md`](plans/roadmap.md) and [`plans/`](plans/README.md): where omawin is going (what, why, in what order) and how to build each piece, with code; check them before starting new work

## Where things go

The layout follows Omarchy's ([`plans/repo-layout.md`](plans/repo-layout.md)). A new file goes in the folder that answers its question; don't add top-level folders.

| Folder | Holds |
|---|---|
| `bin/` | the `omawin` command (`omawin.cs`, `commands.txt`) and the scripts it runs (`check.ps1`, `no-ads.ps1`, `update.ps1`, …) |
| `config/` | the apps' configs, live-linked into place: `glazewm/` (a template), `nvim/`, `noctty/`, `powershell/`, `windows-terminal/`, `starship/`, `git/`, `bash/`, `herdr/`, `fastfetch/`, `neovide/`, `claude/`, `r/` |
| `default/` | omawin's own data: `launcher/` (menus, items, sites, keys, settings), `bar/` (the bar's layout, the logo font) |
| `shell/` | the desktop: `menu-app/` (bar, launcher, menus, screenshots); `plugins/<id>/` (Omarchy's plugin format) |
| `src/` | the other compiled code and its scripts: `cli/` (the C# router), `music/`, `agent/`, `bundle/` (the tools bundle builder), `qs-host/` (the plugins' QML host: Rust and Qt) |
| `install/` | install steps, `steps.txt`, `packages.ps1`, `tools.txt` (the tools bundle's list) |
| `migrations/` · `build/` · `themes/` · `wsl/` | one-time changes · CI's release scripts · themes · Linux in WSL |
| `test/` | `cli.Tests/`, `menu-app.Tests/` (`dotnet test test/<name>`) |
| `docs/` · `manual/` · `plans/` · `open-questions/` | how it's built · how to use it · where it's going · questions waiting for an answer |

Moving a folder that's linked into place or read at a path needs a migration (`migrations/2026-10-31-repo-layout.ps1` is the pattern). The account and shared folders ([`docs/work.md`](docs/work.md)) keep their own layout (`launcher/`, `nvim/`, `git/`, …): they're yours, never moved.

## Rules

- **Keys never change by guesswork.** Win+Space = launcher, Win+Alt+Space = system menu, Win+Enter = always the terminal, Win+F = file finder, Win+Shift+S = screenshot, Win+Shift+T = text from the screen, Win+Shift+V = clipboard, Win+M = music. The user dictates, so a key name in a request can be mistranscribed ("enter" may mean "space"); confirm before rebinding anything.
- **Questions go in `open-questions/<slug>.md`**, one file each: the question, the context, the options, and the default you went with. Keep working with that default; delete the file once it's answered.
- **A theme switch never changes a tracked file.** Each theme is one palette (`themes/colors/<name>.toml`, Omarchy's format); apps read it live, or get a copy rendered from a template outside the repo (`themes/render.ps1`: `config/glazewm/config.yaml` is a template, Windows Terminal uses the scheme `omawin` from a fragment). Never hard-code a theme color in a config: use `{{ accent }}`-style placeholders in a template, or read the palette.
- **Never delete the user's files, or take over what's theirs.** omawin adds; what it replaces is moved aside (`<path>.backup-<timestamp>`), never removed. What the user had before omawin (a WSL distro, an app's config, a git setting) is used as it is and left alone unless they ask; it's only ever changed by an explicit choice (`wsl/install.ps1 -Adopt`). Nothing omawin runs empties a folder of theirs or unregisters a distro it didn't make. **Uninstalling is fine:** omawin removes what it installed once it's no longer needed (in a migration, said in the update's summary), and anything the user chooses to remove (Install › Remove, `omawin pkg remove`, `omawin uninstall`); what it never does is quietly remove a program the user installed themselves.
- **Private things stay private:** nothing personal goes in the repo: no names, emails, employers or clients, no paths or accounts of one PC; those go in the account's folder ([`docs/work.md`](docs/work.md)), and that folder's own notes (`~/omawin-account/README.md`, when there is one: read it) say what else is off limits on that PC, such as work repos agents must never run git in. Never commit `.pbix` files or other work documents.
- **The public history started fresh on 2026-10-17.** The old private history is archived in a private repo; never merge or push a branch made before that date (it carries the old commits). Bring its changes over by cherry-picking or rebasing onto `origin/main`. A local pre-push hook refuses such pushes.
- **Ask before driving the live desktop** (sending keys, clicking, opening windows to test). It's the user's working PC. GlazeWM ignores simulated keypresses anyway, so key bindings can only be tested with real keys.
- **After a visible change, show it**: a screenshot of the result.
- **No admin by default.** Installing, updating and using omawin never ask for admin: per-user installs (`%LOCALAPPDATA%`, `--scope user`, the bundle's portable copies) and `HKCU` settings first. Something that truly needs admin is opt-in, chosen by the user, and every admin change that choice needs is gathered into **one** prompt that first says exactly what will change; **an update never prompts** (it waits for the user to say when). No standing elevated service. Declining leaves everything else working. Details: [`plans/roadmap.md`](plans/roadmap.md) 2.9.
- Don't run Neovim elevated (plugin folders become admin-owned).

## Conventions

- **Every action has an omawin command.** A new feature gets a row in `bin/commands.txt` (`<group> <name> | <summary> | app … / pwsh <script> / run <program>`), and the keys (`config/glazewm/config.yaml`), menu rows (`default/launcher/menu.txt`, action `omawin <command>`) and dock call that name. `omawin help` lists them.
- **Never name a terminal or an editor.** Open them with `omawin launch terminal|project|editor`, which honors the user's default (`omawin default`, `bin/defaults.txt`).
- **The terminal is noctty** (`config/noctty/config.ghostty`, `bin/noctty.ps1`); Windows Terminal remains a choice. Measure terminal changes with `omawin profile terminal`, never by feel.
- **Call it `omawin.exe` from GlazeWM, the dock and the menu.** GlazeWM runs commands from `~`, where a bare `omawin` resolves to the repo folder and opens it in Explorer.
- **Removing something an older version installed?** Add a migration: `migrations/<yyyy-mm-dd>-<what>.ps1`, safe on a brand-new PC and safe to run twice. `install.ps1` and `omawin update` run the new ones; `%LOCALAPPDATA%\launcher\migrations.txt` records what ran.
- **Install changes** go in the matching step under `install/` (`install.ps1 -Step <name>` reruns one); a new step is a line in `install/steps.txt` and its file, and the install screen picks it up. Nothing in install needs admin by default; an opt-in admin feature goes in the one admin step (`install/admin.ps1`, `install/admin-batch.ps1`), run only when the user chooses it, never on its own prompt. New tools go in `install/packages.ps1`: the lean core only if every install needs them, else a preset (`omawin preset`). No Chocolatey for omawin's own installs (the launcher's Packages page offers it, and Scoop, as your choice). PowerShell 7 runs everything: `bootstrap.ps1` is the only script that starts in Windows PowerShell 5.1 (all a new PC has), and its job is to put PowerShell 7 in first (from the release's tools bundle) and hand over; `install.ps1` relaunches itself in 7. The one exception is the elevated admin step (`install/admin-batch.ps1`), which stays on Windows' own `powershell.exe`: an elevated run of a per-user `pwsh.exe` could be swapped by anything that can write to the user's folder. Core tools come from the tools bundle (`install/tools.txt`) where they have a portable build, and every package's source is recorded (`%LOCALAPPDATA%\launcher\packages.tsv`) so `omawin pkg update` updates it the same way.
- **Languages: compiled C# first, then Rust, then Bun.** Any new tool, script or helper is **compiled C#** (.NET 9, Native AOT where it ships; built in CI and shipped in the release, so no SDK on the user's PC). **Rust** where C# isn't the right tool (the GlazeWM fork, low-level or latency-critical work). **Bun** for JavaScript and TypeScript (plugins and web-shaped work). PowerShell only where nothing compiled can run yet (the one-line `bootstrap.ps1` entry under Windows PowerShell 5.1) and in the user's PowerShell profile; existing `.ps1` scripts move to C# over time ([`plans/roadmap.md`](plans/roadmap.md) 2.11). Inside Linux, bash for the distro's setup and shell config (Omarchy's world). No Python.
- **JavaScript runs on Bun.** Anything that needs a JavaScript runtime or package runner uses Bun (`bun`, `bunx`, winget `Oven-sh.Bun`), never Deno or a new Node / npm tool. Programs that need a runtime get pointed at Bun (the music player hands yt-dlp `--js-runtimes bun:<path>`).
- **Small native helpers** (`bin/omawin.cs`, `bin/voice-meter.cs`) are compiled by the in-box `csc.exe`: C# 5 only. The menu app (`shell/menu-app/`) is .NET 9 / WinUI 3, built by `shell\menu-app\build.ps1` (or `omawin restart menu`).
- **Speed matters.** Anything on a key goes through the always-running menu app or a native exe. Starting PowerShell costs ~0.3 s, so a script only runs where that wait doesn't show.
- **Comments** are plain English and say why; match the density of the file you're in. Markdown in `docs/` and `manual/` is not hard-wrapped.
- User data never goes in the repo: clipboard history, launch history and indexes live in `%LOCALAPPDATA%\launcher\`.

## Doing things

| To | Run |
|---|---|
| see every command | `omawin help` |
| restart the bar after editing `default/bar/bar.json` or `shell/menu-app/` | `omawin restart menu` |
| reload GlazeWM after editing its config | `omawin restart glazewm` |
| rebuild the menu app after editing `shell/menu-app/` | `omawin restart menu` |
| relink / rebuild everything | `omawin install -LinkOnly` |
| check the whole setup | `omawin check` (0 failed, 0 warnings is the bar) |

From an agent's shell, start GUI programs through GlazeWM (`glazewm command shell-exec …`) so they land on the desktop.

## Editing files

- **Line endings:** the repo stores LF; on Windows every file is checked out CRLF (`core.autocrlf=true`, `.gitattributes`), except `config/bash/` and `*.sh`, which stay LF. A file you write with LF is fine: git normalizes it on commit. Don't convert files by hand.
- `default/launcher/menu.txt` / `items.txt` hold Nerd Font glyphs: preserve them.
- Bash heredocs (and Python strings that aren't raw) turn `\t`, `\b`, `\r`, `\f` in Windows paths into control characters. Use the Edit tool, or write a Python script to a file with raw strings.
- Git Bash rewrites `/flag` arguments into paths; run `csc.exe` from PowerShell.
