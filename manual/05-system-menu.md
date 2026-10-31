# The system menu (Win+Alt+Space)

After Omarchy's menu, with these sections:
- **Learn**: shortcuts, omawin commands, docs
- **Trigger**: screenshot, text, clipboard, localhost, night light, speed test, colour picker, read a QR code, and **Toggle › Stay awake**
- **Quick**: the Quick settings page and its switches (Wi-Fi, Bluetooth, brightness, power mode, night light, do not disturb)
- **Style**: theme, backgrounds, configs to edit
- **Setup**: launcher items, keys, packages, Windows settings, health check, no ads
- **Install** and **Remove**
- **Update**: this setup, apps, Windows, restarts
- **About**: omawin's version and this PC, fastfetch-style (the launcher's About page)
- **System**: lock, sleep, sign out, restart, shut down

It's translucent and rounded, in the current theme. It opens on tiles:
- a big **Theme** tile with the current theme's colors
- **Install** and **Update**, with a peek at what's in them
- the other sections, plus **Lock**
- a **System** bar

The window keeps that height. Submenus slide in as lists, colored by kind: submenus purple, pages and the theme picker cyan, actions yellow, ones that ask first red, what's installed gray. Where you are shows as chips before the search (Install › Development). Type to search every row. Arrow keys move; Enter opens; Esc or ← goes back; clicking outside closes it.

**Theme** opens the picker: a tile per theme with its first wallpaper and its colors, your current one checked.

**Quick** rows work the machine's switches without opening anything: **Wi-Fi on / off** (`omawin wifi toggle`), **Bluetooth on / off** (`omawin bluetooth toggle`), **Brighter** / **Dimmer** (`omawin brightness up|down`), **Power mode: next** (`omawin power cycle`). **Night light** and **Do not disturb** open the Settings page that owns them: Windows lets no program set either, so that is the whole of it. **All of it** opens the Quick settings page itself: the switches, with the sound of Win+A below them (see [Keys](03-keys.md)). Each row says what it did in a small note at the bottom when it's done.

**Trigger › Toggle › Stay awake** (`omawin toggle stay-awake`) keeps the PC from sleeping and the screen from turning off, until you pick it again; the row says **On** while it is. It lasts only while the menu app runs: a rebuild, a restart or signing out turns it off. While it's on, `%LOCALAPPDATA%\launcher\stay-awake` exists, for the bar to show.

- **Install › Presets** picks an opt-in group of tools (Python, Go, Elixir, R, data, AI agents, music, dictation, Linux in WSL, Power BI + Fabric…) and installs it now (`omawin preset add <name>`); **Which are picked** lists them (`omawin preset list`). Install installs the core and the picked presets on a new PC.
- **Install** also lists winget packages by group: Development, Azure + Fabric (Azure CLI, the Fabric CLI `fab`, azd, Bicep, Functions, the Az and Power BI PowerShell modules…), Data + SQL (Power BI Desktop, Report Builder, DAX Studio, SSMS, DBeaver, SQL Server Developer, sqlcmd, PostgreSQL…), Office (Microsoft 365, Teams, OneDrive, Zoom…), editors, terminals, AI (Claude Code, Codex CLI and OpenCode in the terminal; the Claude, ChatGPT + Codex and OpenCode desktop apps; Ollama, LM Studio), browsers, services, tools, gaming. Ones already on the PC show **✓ installed**; **Any package** searches all of winget.
- **Remove** lists exactly the installed ones.
- Installs, removals and updates run in a small floating terminal you can watch.
- **Update › This setup** runs `omawin update`: pulls the latest, relinks, runs new migrations.

The rows are one text file, `default/launcher/menu.txt`: `id | icon | label | action`, and dotted ids make the submenus. Actions:

| Action | Does |
|---|---|
| `winget <id>` / `choco <id>` | install that package |
| `task <command>` | PowerShell in a small terminal you can watch |
| `edit <path>` | open it in Neovim |
| `run <command>` | start a program, URL or settings page |
| `omawin <command>` | one of omawin's commands, hidden |
| `@<page>` | a launcher page |
| `themes` | the theme picker |

A `!` in front asks first. Edit it from **Setup › This menu**; it's read fresh each time.
