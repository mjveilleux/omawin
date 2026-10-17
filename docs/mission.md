# Mission

**Make Windows the best development machine, with one line.**

You're handed a Windows PC. You open PowerShell, paste one command, and some minutes later it's a fast, lightweight, keyboard-driven developer desktop: tiling windows, a bar, a launcher and system menu, a terminal and editor that match, one theme across every app, and the tools a developer needs, already set up. That's omawin: Omarchy for Windows.

```powershell
irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex
```

## What we're building on

[Omarchy](https://github.com/basecamp/omarchy) (MIT) showed what an omakase developer desktop looks like on Linux: opinionated defaults, everything one key away, one theme everywhere, one menu for every setting, one command to update. We borrow freely from it, with credit: its themes (all 24 are imported, in its palette format), its menu layout (Apps, Style, Setup, Install, Update, System), its key scheme, its update-plus-migrations model. When Omarchy ships something new, we ask how it should look on Windows.

What Omarchy does with Hyprland, Waybar and its shell, we do with:

| Omarchy (Linux) | omawin (Windows) | Ours? |
|---|---|---|
| Hyprland | GlazeWM | our fork, mjveilleux/glazewm |
| Quickshell: bar, launcher, menu, panels, OSD (Waybar and Walker until v4) | **the shell**: one always-running native WinUI 3 / C# process (today's menu app) with the launcher, system menu, pages, screenshot tool, and soon the bar and dock (Zebar today, being replaced) | ours |
| Alacritty / Ghostty | noctty | our fork, mjveilleux/noctty |
| Neovim + LazyVim | Neovim + LazyVim | same |
| pacman, AUR | winget (and Bun for JavaScript) | |

Omarchy 4 (August 2026) folded its bar, launcher, notifications, OSD, panels and lock screen into one Quickshell process with plugins. Our menu app is already that kind of always-running shell; it's where new desktop features go (see [mission-todo.md](mission-todo.md), Shell).

## Overriding Windows

Windows ships defaults made for everyone; omawin replaces the ones that slow a developer down, and leaves the ones Windows does well.

- **Replace:** the taskbar (Zebar bar and dock; the taskbar auto-hides), Start and Search (the launcher on Win+Space), window snapping (GlazeWM tiling, workspaces), the Settings maze (the system menu on Win+Alt+Space, one row per thing you change), Snipping Tool (Win+Shift+S, with text from the screen), the clipboard panel (Win+Shift+V), ads and suggestions (no-ads), the default terminal and editor.
- **Keep:** the lock screen and Windows Hello (themed), notifications and Action Center, the volume and brightness flyouts, drivers and Windows Update, Explorer for files you browse.
- **Rule:** never break Windows. Everything omawin changes can be undone, and nothing is removed that a Windows update needs.

## Principles

1. **One line to install, one command to update.** `irm … | iex` on a fresh PC, under Windows PowerShell 5.1, no admin prompt unless a piece truly needs it. `omawin update` is safe to run any day: it never fails because you customized something.
2. **Fast.** Anything on a key answers instantly, so it runs in the always-running menu app or a native exe, never a fresh PowerShell. Terminal changes are measured (`omawin profile terminal`), not felt.
3. **Lightweight.** The whole desktop (WM, bar, dock, menu app, helpers) stays under ~600 MB RAM and ~1% CPU idle; every new feature says what it costs. The install is a lean core, with the rest opt-in.
4. **As native to Windows as possible.** WinUI 3 and C# (the Windows App SDK) for everything you see, Win32 and WinRT APIs for system features, Rust where it's already the right tool (the GlazeWM fork). No web views in the desktop itself: Zebar goes once the native bar replaces it. Like Omarchy 4's Hyprland + Quickshell split: GlazeWM is the engine with no UI; the shell is the one process that draws everything.
5. **Omakase, but yours.** Strong defaults, chosen for you; your changes live in your own folder and survive every update.
6. **Every action is an `omawin` command.** Keys, menu rows, dock and bar all call the same commands, and `omawin help` lists them.
7. **One theme, everywhere.** A theme is one palette; switching never edits a tracked file.
8. **For developers.** Languages, databases, containers, WSL, Git, coding agents, all installable from the menu, all working the first time.

## Where we are (2026-10-15)

Working daily on one PC: GlazeWM (fork) tiling, Zebar bar and dock, the native launcher, system menu, file finder, clipboard, audio page, screenshots with OCR, 24 themes across every app, noctty and herdr, LazyVim, dictation, music in the bar, no-ads, updates with migrations, `omawin check`.

Not yet a product: configs are linked straight into the git checkout so any edit blocks `git pull`, everything is built on the user's PC (the .NET SDK is a 1 GB dependency), there's no ARM64, and it's never been installed on a clean PC. [mission-todo.md](mission-todo.md) is the way there.

## How we'll know

- A clean Windows 11 VM goes from `irm … | iex` to a working desktop with no errors, in under 20 minutes, with at most one admin prompt.
- `omawin check`: 0 failed, 0 warnings, on that VM and on this PC.
- Someone who isn't the author installs it, uses it for a week, customizes it, updates it, and nothing breaks.
- Every row in the Omarchy parity list ([mission-todo.md](mission-todo.md)) is have, Windows-native instead, or decided against.
