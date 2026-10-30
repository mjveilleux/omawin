# Decisions and open questions

Everything decided while planning omawin's road to "nearly perfect" (the planning conversation of 2026-10-30), in the order it came up, each with where the plan carries it. The [roadmap](roadmap.md)'s *Decisions made* table is the short form; this is the full record, so nothing has to be re-asked. Open questions are at the end.

## Direction

| # | Decision | Where |
|---|---|---|
| 1 | Get omawin close to Omarchy 4 in **how it installs and how it feels**: a snappy menu, smooth window animations, a plugin system, and Omarchy's elements borrowed the way its themes were | [roadmap.md](roadmap.md) |
| 2 | The goal beyond Omarchy: **fix Windows**, on Windows' own APIs; every change measured, explained and reversible, no placebo tweaks | roadmap phase 8 |
| 3 | The plan is one big plan: the roadmap (what, why, order) plus this folder's per-area files with code, so work can continue without any conversation | [README.md](README.md) |

## The menu and search

| # | Decision | Where |
|---|---|---|
| 4 | **One Search**: one search bar for everything, each result tagged (Apps, Settings, Commands, Files, Windows, Projects, Clipboard, Themes; Calculator, Packages and Web as rows), filters by tag in the bar | [one-search.md](one-search.md), roadmap 1C |
| 5 | Omarchy's menu style and separate menus are **not** the model; the **bento home** (omawin's tiles) is kept for the empty state, a ranked list with a preview for results; themes show as a grid of pictures | one-search.md, guardrails 1-2 |
| 6 | The twelve guardrails (bento only when empty; a home that doesn't move; at most eight tags, never colour alone; per-tag caps in All; explainable ranking; the selected row never jumps; Enter acts and Space types; honest labels for settings that can only be linked; typing never lost; one key; speed; one look) | one-search.md |
| 7 | The look: **futuristic and minimal**: hairline borders, a translucent window with one soft accent glow, light large search text, small uppercase monospace labels, outlined glowing states, underlined filter tabs; **the theme shown as one bar whose colours blend into each other** | one-search.md guardrail 12, [design/one-search/](design/one-search/README.md) |
| 8 | **Win+Space for everything; Win+Alt+Space retired** (one release as an alias with a note, then removed by a migration, with AGENTS.md's key list and the manual updated in that change; `omawin menu` stays as a command). Win+F and Win+Shift+V stay, opening the same window on Files and Clipboard | one-search.md guardrail 10, roadmap 1C |
| 9 | **Never open Settings again**: every Windows setting searchable, the common ones switched in place (Enter), with undo and `omawin setting` commands; settings you change follow you to a new PC | roadmap 1B |
| 10 | fff and ripgrep are **not** used for settings search (settings are in-memory rows; the menu's own fuzzy matcher is right); fff's frecency idea and a ripgrep dev script are | roadmap 1B |

## Files

| # | Decision | Where |
|---|---|---|
| 11 | "My file explorer" is **the Win+F finder (fff)**; it grows into a keyboard file manager (actions, browsing, filters, previews, drag-out, file dialogs); Explorer only gets developer defaults | roadmap phase 6 |
| 12 | **Searching inside files is a mode of the finder** (a Contents chip or `#`), not a separate tool; fff's content index if its API allows, else ripgrep; indexing only folders you pick | roadmap 6.3 |

## Speed, look and feel

| # | Decision | Where |
|---|---|---|
| 13 | The menu's close animation: fade out ~130 ms, a key during the exit cancels it | roadmap 1.4 |
| 14 | **Crisp icons**: fetch at the pixels actually drawn per monitor scale, never scale up, show 1:1, Store apps' unplated logos, tray icons at the bar's real size | roadmap 1.8 |
| 15 | **Theme thumbnails pre-made in CI and preloaded**, the palette as a placeholder, full backgrounds fetched in the background after install | roadmap 1.9 |
| 16 | **Community themes in the picker** (a Community chip, live preview before installing, `omawin theme add <name>`), from a CI-built index whose main source is **omarchy.org/themes** | roadmap 1.10 |

## Install, update and packages

| # | Decision | Where |
|---|---|---|
| 17 | Install **under a minute, then less** (milestones < 60, < 30, < 15, stretch < 10 s), Omarchy's way: one prebuilt bundle in stages, `curl.exe` and `tar.exe`, configs seeded in one pass, the desktop started at once, questions on a welcome page, the rest in a background queue; fast delta updates | [install-speed.md](install-speed.md), roadmap 2.0 |
| 18 | omawin's command-line tools come through **mise** (or the bundle on first install) | roadmap 2.3 |
| 19 | **winget, Scoop and Chocolatey all stay**; you can manage any tool individually with them after install; your package manager's copy wins on the PATH | install-speed.md I, roadmap 2B |
| 20 | **`omawin pkg`**: one package command over winget, Scoop, Chocolatey, mise, the bundle and pacman in Arch; omawin picks and remembers the manager; you can choose or move any package | roadmap 2B |
| 21a | **Uninstall anything you choose**: Install › Remove lists everything from every manager, `omawin remove <part>` for omawin's optional pieces, `omawin uninstall` for all of it; omawin uninstalls what it no longer needs in a migration (said in the update's summary), never a program you installed yourself quietly. AGENTS.md's rule changed to match | roadmap 2.10, AGENTS.md |
| 21b | **Zebar is out of the roadmap**: the bar is the menu app's; Zebar is uninstalled by a migration (per user at once; machine-wide queued for the one admin OK) | roadmap 1.6, 01-snappy.md |
| 21 | **No admin by default**: install, update and use never ask; an opt-in feature that truly needs it asks once for everything it needs, says what will change first, never during an update; no standing elevated service. Now a rule in AGENTS.md | roadmap 2.9, AGENTS.md |

## Linux

| # | Decision | Where |
|---|---|---|
| 22 | **Linux is first class; Arch is the default Linux** (Ubuntu alongside, every feature for whichever you pick) | roadmap phase 3 |
| 23 | **Linux (WSL) is the default terminal**: Win+Enter opens your Linux; `omawin default shell windows` brings PowerShell back | roadmap 3.4 |
| 24 | **No pre-started shell for Linux** (that was only for PowerShell's slow start); keeping WSL warm is the fix, measured against its memory cost | roadmap 3.4, 3.5 |
| 25 | Linux follows **all of Omarchy's package sources and decisions**: Omarchy's repositories, its package list for the terminal, its shell defaults, **no AUR** ("anarchy" in dictation read as Omarchy) | roadmap 3.2 |
| 26 | **Terminals show which side they're on**: Linux and PowerShell differ at a glance (a background tint from the theme, the prompt's OS badge, window titles, later border colours) | roadmap 3B |

## Plugins

| # | Decision | Where |
|---|---|---|
| 27 | A plugin ecosystem like Omarchy's: **Omarchy's manifest format**, git repos, land disabled, diff on update, clone to customise | [plugins.md](plugins.md), roadmap phase 4 |
| 28 | Plugin **panels are loose XAML**; logic runs in the plugin's own process over JSON lines (Waybar's format for widgets) | plugins.md |
| 29 | **Omarchy's plugins run unchanged**: Windows runs QML (it's Qt); only Quickshell's Linux modules are missing, so omawin provides them in a Qt host on Windows, plus stand-ins in Arch for Omarchy's commands | plugins.md, roadmap 4.9 |
| 30 | Phase 8's fixes live in one of three homes: **core** (on a key or a shared hook), **a settings catalog entry** (a value), or **a first-party plugin** (an occasional page or service), written in C# AOT on a small SDK | roadmap 8.0 |

## Fixing Windows

| # | Decision | Where |
|---|---|---|
| 31 | Phase 8: 51 fixes on Windows' own APIs (who holds a file, whole-disk search, Quick Look, a window switcher, monitor profiles, per-app audio, mic mute, a secret-aware clipboard, crash explanations, startup list, Windows Update without surprises, …) | roadmap phase 8 |
| 32 | Windows 11's common complaints (2026 survey) each answered, and tracked as things to explore, measure and fix | roadmap 8J, mission-todo track 9 |
| 33 | **Idle memory**: 6-7 GB in use at idle today; measure first, target under 4 GB with omawin under 600 MB | roadmap 8K |

## Sandboxes and tests

| # | Decision | Where |
|---|---|---|
| 34 | **Windows Sandbox** for a clean-install test (`omawin sandbox test`), a throwaway Windows from the menu, coding agents fenced in WSL, Try Omarchy for Windows linked for real Hyprland | roadmap phase 7 |
| 35 | Work that can't be tried in the cloud is marked 🖥 and tried on the PC before main; CI checks what it can (builds, install time, the frame check, `setup.sh` in containers, standard-user installs) | roadmap, README.md |

## Open questions

| Question | Default until decided | Where |
|---|---|---|
| One Search: which home tiles are fixed and which you pin (Ctrl+P)? | omawin's current tiles fixed, a pinned row below | one-search.md |
| One Search: Apps as a grid or a list when filtered with nothing typed? | a list | one-search.md |
| One Search: fixed window size or growing with results? | fixed | one-search.md |
| One Search: any kind missing from the eight tags? (processes and ports folded into Commands / Windows) | the eight | one-search.md |
| The structure of omarchy.org/themes' list (for the community index) | read the page in CI; to check from a session that can reach it | roadmap 1.10 |
| The Developer Tools plugins on plugins.omarchy.org, each scanned for porting | to do from a session that can reach it | plugins.md |
| Everything in the roadmap's *Things to explore* table (idle memory, the bundle's size, `tar.exe`, Defender's first scan, `XamlReader` under AOT, Quickshell's licence, fff's content API, herdr and OSC 11, the per-user GlazeWM's limits, …) | measure before building | roadmap |
