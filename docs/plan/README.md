# The master plan: omawin, nearly perfect

**Start here if you have no context.** This folder is the implementation side of [the roadmap](../roadmap.md): the roadmap says *what* and *why* for every piece of work; the files here hold the *how*, with the code each piece needs (new files, changes to existing ones, data formats, CI workflows), written so the work can continue even if every conversation that produced it is lost.

The code in these files is a **sketch that hasn't been compiled or run**. It names real files and functions in this repo (as of 2026-10-30), follows the repo's rules ([AGENTS.md](../../AGENTS.md)), and marks anything uncertain with *(check)*. Treat it as a strong first draft: build it, test it, fix it, and update the plan file when reality differs.

## The goal

**Make Windows the best development machine, with one line** ([mission.md](../mission.md)), and get there by being as good as Omarchy is on Linux:

1. **Snappy.** Every key answers in one frame; everything moves the way Omarchy moves.
2. **One search for everything.** Win+Space finds and does anything: apps, every Windows setting (switched in place), commands, files, windows, projects, clipboard, themes.
3. **Installed in under a minute,** with **no admin prompt**, updated in seconds, and every tool still yours to manage with winget, Scoop or Chocolatey.
4. **Linux first class.** Arch (by default) with Omarchy's own packages is the default terminal, as fast as PowerShell, and you always know which side you're on.
5. **Extensible like Omarchy.** Plugins in Omarchy's format; Omarchy's own plugins running unchanged.
6. **Windows fixed.** Its daily annoyances replaced by calm, measured, reversible fixes on its own APIs; idle memory under 4 GB.

## The files

| File | Roadmap | What it holds |
|---|---|---|
| [01-snappy.md](01-snappy.md) | Phase 1 (1.1-1.10) | `omawin profile menu`; keys straight to the shell (GlazeWM fork `emit`); AOT in CI; motion tokens and exit animations; shipping window animations (close animation, enabled flag, the CI frame check); Zebar's removal; a virtualized list; crisp icons; theme thumbnails built in CI and preloaded; community themes in the picker |
| [02-settings-and-one-search.md](02-settings-and-one-search.md) | Phase 1B, 1C | the Windows settings catalog (format, handlers, undo, `omawin setting`), Windows' settings index, One Search's sources, window, keys and Win+Alt+Space's retirement; also [one-search.md](../one-search.md) and its [mockup source](../design/one-search/) |
| [03-install-update-packages.md](03-install-update-packages.md) | Phase 2, 2B, 2.9 | the omawin bundle and its CI; the new bootstrap; the background queue; delta updates; `omawin pkg` over winget, Scoop, Chocolatey, mise, the bundle and pacman; no admin (a per-user GlazeWM, the opt-in admin batch, CI as a standard user); install timing in CI and in Windows Sandbox; also [install-speed.md](../install-speed.md) |
| [04-linux.md](04-linux.md) | Phase 3, 3B | Linux as the default shell (`omawin.cs`); Omarchy's terminal layer in Arch; the WSL VM's settings; keep-warm; Docker in Linux; credentials; the theme in Linux; the Linux / PowerShell tint, prompt and titles; backup and reset; container CI |
| [05-plugins.md](05-plugins.md) | Phase 4, 8.0 | the manifest and registry; plugin processes (Job Objects, JSON lines); command widgets; XAML panels with `omawin:Bind` / `omawin:Action`; providers; `omawin plugin`; the first-party plugin SDK and admin verbs; the Omarchy compatibility layer (stand-ins in Arch, the Qt QML host); the Omadoro port; also [plugins.md](../plugins.md) |
| [06-dx-finder-sandbox.md](06-dx-finder-sandbox.md) | Phases 5, 6, 7 | web apps; the key cheat sheet from GlazeWM's config; toasts and the OSD; idle lock; the scratchpad (fork); menu conditions; the finder's actions, browsing, content search, previews, filters and drag-out; `omawin sandbox test`; agents in a sandbox |
| [07-fixing-windows.md](07-fixing-windows.md) | Phase 8 (8.1-8.51, 8K) | who holds a file; whole-disk search; dev disk cleanup; Quick Look in Explorer; the window switcher and picture-in-picture; monitor profiles; per-app audio and mic mute; key remaps and snippets; the clipboard; the Health page from event logs; startup list; processes and ports; Windows Update; DNS and hosts; notifications; the tune-up catalog entries; Dev Drive, git, ports, PATH, environment variables; idle memory |
| [08-omarchy-review.md](08-omarchy-review.md) | Phase 9 | every Omarchy feature (at commit `7f4b1dd`) marked have / planned / new / n/a, and the new ones with how they work on Windows and code: the drop-down console, hooks, `setup git`, crash watch, the theme in more apps, `debug`, `pick`, agent accounts, `reset`, display modes |

## The order of work

Each milestone is a release ([roadmap.md](../roadmap.md), Milestones). Within a milestone, items are independent unless noted. **Measure first** wherever a number decides (the roadmap's *Things to explore* table).

| # | Milestone | First do | Then | Done when |
|---|---|---|---|---|
| 0 | **Baseline** | on the PC: `omawin profile ram -Save` idle, `omawin profile terminal`, a timed install in Windows Sandbox (Pro) | write the numbers into the roadmap | we know where the time and memory go |
| 1 | **snappy** | 1.1 profile menu, 1.3 AOT in CI, 1.8 icons, 1.9 theme pictures | 1.4 motion, 1.2 `emit`, 1.5 window animations (a week of daily use), 1.6 Zebar out, 1.10 community themes | key to pixels < 50 ms; the frame check green |
| 2 | **one search** | One Search's model, parser, merger and tests (no UI) | sources, the window, keys, Win+Alt+Space's alias release then removal | Win+Space finds anything; the guardrails hold |
| 3 | **settings** | the catalog parser and `reg`/`spi`/`powercfg` handlers with CI round-trips | builtins, Windows' index, undo, `omawin setting`, `settings save/apply` | the "never open Settings" list of 30 passes |
| 4 | **fast** | 2.1 install timing in CI and the Sandbox test; 2.9 no admin (per-user GlazeWM, CI as a standard user) | the bundle (stage 1 then 2), the new bootstrap, the welcome page and queue, delta updates; 2B `omawin pkg` | desktop < 60 s, then < 30, < 15; no admin prompt on install or update |
| 5 | **linux** | 3.4 Linux as the default shell, 3B tint and prompt | 3.2 Omarchy's terminal layer, 3.3 VM, 3.5 keep-warm (measured), 3.6 Docker, 3.7-3.11 | Win+Enter opens Arch as fast as PowerShell did; Linux and PowerShell look different |
| 6 | **plugins** | the manifest, registry, plugin processes, command widgets; music and agents as plugins | XAML panels (prove `XamlReader.Load` under AOT first), providers, `omawin plugin`, the SDK; then the stand-ins and the QML host | Omadoro runs unchanged; `omawin plugin scan` over the marketplace |
| 7 | **parity** | 5.x DX, the finder's actions and browsing, `omawin sandbox test` | content search, previews, web apps, scratchpad, notifications | Omarchy's habits work |
| 8 | **fixed** | 8K idle memory (measure, then cut), 8.1 who-has, 8.15 Health, 8.14 clipboard, 8.11 mic mute | the rest of phase 8, most as settings catalog entries | idle < 4 GB; Windows' common complaints answered |
| 9 | **omarchy** | 9.2 hooks, 9.3 setup git, 9.6 debug, 9.9 reset (all small) | 9.1 drop-down console (after the scratchpad), 9.4 crash watch, 9.5 theme in more apps, 9.11 screen recording, 9.13 fonts | nothing an Omarchy user reaches for is missing |

**Cloud or PC?** Sessions without Windows (like the one that wrote this plan) can do: plain C# with unit tests (One Search's merger, the settings catalog parser, the plugin protocol), CI workflows, PowerShell and bash scripts, the GlazeWM fork's Rust with its tests and CI frame check, docs. Anything visual, on a key, or touching the live desktop needs the PC (🖥 in the roadmap), and AGENTS.md's rules: ask before driving the desktop, screenshot visible changes.

## Decisions (so they aren't reopened by accident)

The roadmap's *Decisions made* table is the record. The ones that shape the code most:
- One always-running native shell (the menu app) draws everything; Explorer stays; no code injected into other processes.
- **One Search on Win+Space for everything; Win+Alt+Space retired** (one release as an alias, then removed).
- Plugins: Omarchy's manifest; logic out of process (JSON lines); panels as inert XAML; Omarchy's plugins unchanged through stand-ins and a Qt QML host.
- Command-line tools from the bundle, then mise or any package manager by choice (`omawin pkg`); winget, Scoop and Chocolatey all stay.
- Linux first class, Arch by default, Omarchy's package sources and choices, no AUR; Linux is the default shell.
- **No admin by default**; at most one prompt for an opt-in feature; never during an update.
- Every Windows change is measured, explained and reversible; no placebo tweaks.

## Keeping this plan true

- When an item lands: tick it in [mission-todo.md](../mission-todo.md) with the commit, and correct its plan file where the code ended up different.
- When a measurement answers a *Things to explore* row: write the number into the roadmap and remove the row.
- When the maintainer decides an open question: add it to *Decisions made* and update the affected plan file.
- New ideas: the roadmap first (what and why), then a section here (how).
