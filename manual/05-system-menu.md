# The system menu (Win+Alt+Space)

After Omarchy's menu, with these sections:
- **Learn**: shortcuts, omawin commands, docs
- **Trigger**: screenshot, text, clipboard, localhost, night light, speed test, colour picker, read a QR code, **Change the clipboard's text** (JSON, Base64, URL, a timestamp, a path for WSL), and **Toggle › Stay awake**
- **This PC**: why it restarted, crashed or woke up; what starts with Windows; processes by app; apps on the internet; who has a file; the hosts file; share the Wi-Fi; mute every microphone ([This PC](13-this-pc.md))
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

The window keeps that height. Submenus slide in as lists, colored by kind: submenus purple, pages and the pickers cyan, actions yellow, ones that ask first red, what's installed gray. Where you are shows as chips before the search (Install › Development). Type to search every row. Arrow keys move; Enter opens; Esc or ← goes back; clicking outside closes it.

**Theme** opens the picker: a tile per theme with its first wallpaper and its colors, your current one checked. **Backgrounds** opens the background picker: every wallpaper of your theme as a grid of pictures, the one on your desktop checked; Enter sets one, Tab shows the next theme's (picking one of those switches to that theme too). See [Themes](08-themes.md).

**Quick** rows work the machine's switches without opening anything: **Wi-Fi on / off** (`omawin wifi toggle`), **Bluetooth on / off** (`omawin bluetooth toggle`), **Brighter** / **Dimmer** (`omawin brightness up|down`), **Power mode: next** (`omawin power cycle`). **Night light** and **Do not disturb** open the Settings page that owns them: Windows lets no program set either, so that is the whole of it. **All of it** opens the Quick settings page itself: the switches, with the sound of Win+A below them (see [Keys](03-keys.md)). Each row says what it did in a small note at the bottom when it's done.

**Trigger › Toggle › Stay awake** (`omawin toggle stay-awake`) keeps the PC from sleeping and the screen from turning off, until you pick it again; the row says **On** while it is. It lasts only while the menu app runs: a rebuild, a restart or signing out turns it off. While it's on, `%LOCALAPPDATA%\launcher\stay-awake` exists, for the bar to show.

**Style › Animations** (`omawin toggle animations`, or `on`, `off`, `status`) turns every animation off or back on: the menu and launcher fading in and out, rows arriving, the bar's widgets, and GlazeWM's windows sliding. Off, everything is simply there at once; the row says **On** while they're on. They start off in a virtual machine, where frames are drawn without a GPU. Windows' own switch (Settings › Accessibility › Visual effects › Animation effects) turns the menu's off too.
## Windows settings

You shouldn't need Windows' Settings app. Type a few letters in the system menu ("dark", "ext", "hidden", "mic", "hz", "accel") and the Windows settings that match show under the menu's own rows, with their current value: a switch says **On** or **Off**, a choice or a slider shows its value between ‹ and ›. The launcher (Win+Space) shows the top three under its apps.

- **Enter** flips a switch or moves a choice to its next value, right there; the row shows the new value and the footer says what happened. **Space** does the same while nothing is typed (once you type, Space is a space).
- **← / →** on a choice or a slider: the previous or next value.
- **Ctrl+Z** puts the last change back, then the one before it, and so on.
- A setting omawin can't change from outside Windows' own app opens its page instead, and says so with ↗. So do the hundreds of other Settings pages, the consoles (Device Manager, Services, Event Viewer, Disk Management, Task Scheduler) and the old dialogs people still need (Environment variables, Advanced system settings, Network adapters): they're found the same way.
- A setting that applies to the whole PC (developer mode, long paths, hibernation, WSL, Hyper-V, Windows Sandbox) needs admin. Choosing one doesn't ask: it waits, and a row at the top says **Apply 2 admin changes**. Choosing that lists exactly what will change, then Windows asks for an administrator once, for all of them. Say no and nothing changes; they stay waiting until you choose.
- A row says when a change takes effect if it isn't at once: after a restart, after you sign out, or once Explorer restarts (a **Restart Explorer to apply** row does that: the taskbar blinks once).

**Setup › Windows settings** lists them by area, for browsing: Look, File Explorer and taskbar, Privacy, Mouse and keyboard, System, Display, Connections, Sound, Developer. There's an **Undo** row there too, **Admin changes waiting**, and **Save my settings for a new PC**.

Every change is also a command (`omawin setting set explorer.hidden on`, `omawin setting toggle look.dark-apps`, `omawin setting undo`; `omawin setting list` shows each id with its value), so a key, a script or an agent can change one and read it back.

**Your settings on a new PC.** `omawin settings save` writes the settings you've changed through omawin (`id = value`) to `settings.txt` in your account folder (`-Shared`: the PC's shared folder, [docs/work.md](../docs/work.md)); it keeps what's already in the file, so you can edit it by hand. Install sets what it says on a new PC (`omawin settings apply`), leaving the admin ones for `omawin settings admin`.

What omawin can switch is a list, `default/launcher/settings.txt` (about 90 settings; your account folder's and the shared folder's `launcher/settings.txt` can add their own or replace one). Only settings that are documented or long stable are on it, never the ones Windows protects (default apps), and every change is recorded in `%LOCALAPPDATA%\launcher\settings-history.tsv` with the value before it, which is what undo puts back.

## Install, update and the rest

- **Install › Presets** picks an opt-in group of tools (Python, Go, Elixir, R, data, AI agents, dictation, Linux in WSL, Power BI + Fabric…) and installs it now (`omawin preset add <name>`); **Which are picked** lists them (`omawin preset list`). Install installs the core and the picked presets on a new PC.
- **Install › Plugins** turns a plugin on and installs what it needs (`omawin plugin add music`: the music, podcast and radio player); **Which are on** lists them (`omawin plugin list`).
- **Install › Plugins › Gallery** (`omawin plugin gallery`) lists every plugin: omawin's own and the bar widgets of [Omarchy's marketplace](https://plugins.omarchy.org) its maintainers verified, each with its author, stars, what it does, and whether it runs here: *runs*, *partly* (it calls Linux programs Windows hasn't got, so some of it won't work), or *not yet* (it needs something omawin's plugin host doesn't do yet, named in the row). Type to search. Enter turns one on or off: a marketplace plugin asks first (its code is someone else's, and runs as you), then downloads it, at the version that was reviewed, in a small window you can watch. Turning it off removes the download. The list comes with omawin's updates (`omawin update`).
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
| `settings <area>` | Windows' settings of that area, switched in place (`settings undo`, `settings admin` too) |
| `themes` | the theme picker |
| `backgrounds` | the background picker |

A `!` in front asks first. Edit it from **Setup › This menu**; it's read fresh each time.
