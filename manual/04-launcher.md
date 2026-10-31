# One Search and the file finder

## One Search (Win+Space)

One window and one bar for everything omawin can find or do, like Omarchy's Super+Space grown: apps, Windows settings, omawin's commands and the system menu's rows, files, open windows, projects, what you copied, themes, and sums. Every result is tagged with what it is: a colour, an icon and a word (APP, SETTING, COMMAND, FILE, WINDOW, PROJECT, CLIP, THEME).

### The home

With nothing typed it opens on tiles that keep their places, so you learn them by position:
- a big tile for the first item in `default/launcher/items.txt` (`icon | name | command`), the terminal
- **Projects** and **Clipboard**, showing your recent projects and what you last copied
- your next four items, with a server count on Localhost
- **Browse** (everything the system menu had) and **Theme** (yours, its palette as one gradient; Enter opens the Themes filter)
- **Quick**: Wi-Fi, Bluetooth, Dark mode and Microphone (apps' access to it), each saying On or Off; Enter switches it
- **Frequent**: the four apps you open most
- **Pinned**, once you've pinned something: up to four of yours, in the order you pinned them. **Ctrl+P** on any result (an app, a setting, a command, a file, a project, a theme) pins it; Ctrl+P again, on the result or on its tile here, unpins it. It's the last row, so pinning never moves anything above it. A setting's pin shows its value and Enter switches it there.

Arrow keys move between tiles; the selected one lifts. Only what's in the tiles changes, never where they are.

### Typing

Typing shows one ranked list, with the selected result's preview beside it (an app's icon, a file's first lines or picture, a theme's wallpaper, a setting's value, a command's whole description) and what Enter does in the footer.
- **All shows each kind's best few** (apps 4, settings 4, commands 3, files 3, the rest 2); a **Show all 12 Settings** row opens that filter.
- **Matching is fuzzy**: word starts, capitals and runs of letters count most ("pbi" finds Power BI, "vsc" VS Code); program and package names match too ("pwsh", "taskmgr"); one typo per word is forgiven ("termnial"); shorter names come first; the matched letters are highlighted.
- **It learns:** what you pick ranks higher, most of all for what you'd typed. The same query gives the same order until your habits change.
- **For one or two letters**, apps and commands come before files; files are searched from three letters (or with the Files filter). They arrive a moment later, below the row you're on: the selected row never moves under you.
- **Last, two rows you pick, never searched as you type**: find it in winget, Scoop and Chocolatey (the Packages page), and search the web.

### Filters

The tabs under the bar narrow it to one kind: **All, Apps, Settings, Commands, Files, Windows, Projects, Clipboard, Themes**. The one you're in also shows as a token at the start of the bar.
- **Tab / Shift+Tab** move through them, keeping what you typed. Click one too.
- **A filter's name, then Tab**, locks it: "set" Tab is Settings.
- **`/`** first searches files, **`>`** commands (the prefix becomes the token).
- **Backspace** on an empty bar, or **Esc**, drops the filter.

Some filters are richer views: **Files** is the file finder (below), **Clipboard** and **Projects** their pages, **Themes** the theme picker, and **Commands** with nothing typed is **Browse**: the system menu's tiles and sections ([The system menu](05-system-menu.md)). **Settings** with nothing typed lists Windows' settings by area.

### Keys

- **Enter** does the obvious thing: opens, runs, switches a setting, pastes a copy, applies a theme. A command that needs words (`theme set <name>`) fills the bar for them; scripts run in a terminal so you can see what they say.
- **Ctrl+K** lists the selected result's other actions: run as administrator, show in folder, copy path, terminal here, open in the editor, run in a terminal, copy the command, close a window, delete a copy, a theme's backgrounds, a setting's previous value, pin to the home. Esc closes the list.
- **Ctrl+P** pins the selected result to the home, or unpins it (above).
- **Ctrl+Enter** shows a file in its folder; **Ctrl+Shift+Enter** runs one of your items as administrator (Windows asks first).
- **Settings switch in place**: a switch says On or Off and Enter flips it; **← / →** step a choice; **Ctrl+Z** puts the last change back ([Windows settings](05-system-menu.md#windows-settings)).
- **Esc** steps back one thing at a time: the actions, then what you typed, then a view, then the filter, then the window. Win+Space again also hides it.

**Win+F** opens it on Files and **Win+Shift+V** on Clipboard. Win+Alt+Space, the system menu's key, is retired: for this release it opens One Search and says so.

From a terminal or a script: `omawin search [filter] [words]` (`omawin search settings dark`), `omawin launcher [filter or page]`, `omawin files`, and `omawin menu [section]` for Browse.

### Calculator

Type a sum and its value is the top row: **Enter copies it** (and closes the window). It knows `+ - * / ^` (or `**`), `x × ÷`, parentheses, `%` (`200*15%` is 30; `10 % 3` is the remainder), `pi` and `e`, and `sqrt abs round floor ceil ln log sin cos tan` (radians): `12*4.5`, `2^10`, `sqrt(2)*pi`, `(1+2`. A bare number or an app's name is still a search.

### Pages

Localhost, Audio, Quick settings, Packages, Reports, Shortcuts and About open in the same window from their tiles or results: type to search, Esc to go back. Projects and Clipboard are filters with the same pages.

### Packages

Like Omarchy's package picker, in One Search (Browse › Install › Package, the Packages item, the last row of a search, or `omawin pkg find`): pick a package manager, type to find a package, Enter installs it.
- **Tab switches between winget, Chocolatey and Scoop** (or click their chips). winget's ~16,000 packages and Scoop's buckets are searched on the PC as you type, with the launcher's fuzzy matching; Chocolatey has no catalog on the PC, so its community site is asked once you stop typing.
- **Beside the list:** what the package is, its version, its homepage and license (winget's take a second or two), and whether it's already here (also ✓ in the list).
- **Ctrl+Space picks several**, from any of the three; Enter installs them one after another.
- **Installs run in a small terminal you can watch** (it stays open at the end). Chocolatey asks for admin first; Scoop installs into your user folder without.
- **A manager not on the PC?** Its tab offers to install it: winget (Microsoft's App Installer) registered for your account or put in, Chocolatey from winget or its own installer (asks for admin), Scoop with its own installer from scoop.sh.

`omawin pkg install` with no name is the same for winget in a terminal, with fzf; with a name it installs by name with whichever manager suits it ([The omawin command](02-omawin-command.md#packages)).

- **Projects**: git repos on Windows (in your home folder or one folder down, like `~/omawin` or `~/src/glazewm`; and Documents, OneDrive Documents, `~/source/repos`, `~/repos`) and in your Linux in WSL (under ~), zoxide favorites first. A repo you clone or `git init` shows up within seconds.
  - Opening one gives a terminal window on that project's own [herdr](https://herdr.dev) session, with tabs **shell | nvim | claude | opencode** (`default/launcher/project-tabs.txt`).
  - The session keeps running when the window closes, agents and all; opening the project again reattaches to it.
  - Linux repos open inside WSL, marked with your distro's logo (Arch or Ubuntu). WSL is only asked when it's already running (a cold start takes ~8 s); otherwise the last list is reused.
- **Localhost**: the web servers running right now, on Windows or in WSL ("wsl"), with each page's title, port and program. When Windows has reserved a common dev port at start-up (Hyper-V and WSL take random ranges, so a server on 3000 or 8080 fails though nothing is listening there), a yellow line says which and what to do. Under them are your favorite sites from `default/launcher/sites.txt`.
  - Enter opens one in Chrome.
  - **Ctrl+X** stops a server after you confirm. That's the program listening on its port, or the process inside WSL for a WSL server.
- **Clipboard** (the filter, or **Win+Shift+V**): everything you've copied, text and pictures, newest first, searchable, with the full text or the picture in a preview; Enter pastes it. **Ctrl+P** pins an entry to the top for good (again to unpin); the rest keeps the newest 200. **Ctrl+X** deletes one. What a password manager marks private is never kept, as in Windows' own clipboard history.
  - Enter pastes it into the window you were in; **Ctrl+X** deletes an entry.
  - The newest 200 copies are kept. Copies that password managers mark private are skipped.
- **Audio** (or **Win+A**): every speaker and headset, with what's playing on each inside it (the music player, Chrome, Teams…), then every microphone, each with its volume as a bar; the one Windows is using says "in use". It opens on the first speaker.
  - **↑ ↓** move; **← →** turn the selected one down or up, 5% a press (**Shift** for 1%; **+ −** do the same). Turning it up unmutes it.
  - **Space** mutes or unmutes it; **Enter** makes a speaker the one Windows uses, or on a playing app moves it to another output.
  - It's there for keyboards whose volume keys Windows doesn't understand (some compact keyboards send volume keys only in a Mac mode, which Windows rejects).
- **Quick settings** (or the system menu's **Quick › All of it**): the machine's switches, with the Audio page below them. Across the top: Wi-Fi (the network in use, its signal), Bluetooth, a brightness tile per monitor, the power mode, night light, do not disturb, the battery. It opens on the first speaker, and reads the machine afresh each time it opens.
  - **Space** works the selected switch: Wi-Fi or Bluetooth on or off (off takes a second press: the first says "Press again to turn Wi-Fi off", and any other key cancels), the next power mode. **← →** (or **+ −**) set a monitor's brightness, 5% a press (**Shift** for 1%), never below 5%.
  - **Enter** opens what a switch holds: the networks nearby (Enter joins one Windows has saved, on the one in use it disconnects), the paired devices, the three power modes. The lists open on the first network or device; turning the radio off is the last row.
  - Night light and do not disturb have no API Windows lets a program use, so those two open the Settings page that owns them; so does a Wi-Fi network Windows hasn't saved (the launcher's box would show the password as you typed it).
- **Reports** (only when your account folder, `~/omawin-account`, has a reports script: [docs/work.md](../docs/work.md)): reports from your own folders and shares, newest first. A share you can't reach right now (VPN off) keeps its last list.
  - Picking one runs your script's import; a share that's offline asks to connect the VPN first.
  - The list refreshes in the background every 2 hours, or now with `omawin reports index`.
- **Shortcuts**: the searchable key list (`default/launcher/keys.txt`).
- **About** (`omawin about`, or **System menu › About**): fastfetch-style facts, one a row: omawin's version (its git tag or commit, and date), Windows edition and build, uptime, CPU, GPU, memory, disk, theme, GlazeWM / noctty versions, the menu app's own memory, stay awake. **Enter** copies a row.
- **VPN items** (`@vpn <name>`, in your account folder's `launcher/items.txt`, say): connect that Windows VPN connection. Windows' own connect box opens already dialing and connects in a couple of seconds with the saved sign-in. When it's connected, the item offers Disconnect and the bar shows **VPN** and its name.
- **Your own items and sites**: `launcher/items.txt` and `launcher/sites.txt` in the PC's shared folder (`C:\ProgramData\omawin\shared`, every account) and in this account's folder (`~/omawin-account`) add theirs after omawin's: an email profile, a work app, a VPN. ([docs/work.md](../docs/work.md) says which goes where.)

## File finder (Win+F)

One Search's **Files** filter (also the **Files** item, `/` typed first, or Tab to it):
- results on the left: the name with matched letters, the folders it's in, and when it changed or its git status
- a preview on the right: the picture itself for pictures (and how big it is), the first lines of text and code, a folder's contents
- chips on top: All, **Folders** (only folders), **Images** (only pictures: PNG, JPG, GIF, WebP, BMP, ICO, TIFF, HEIC, AVIF, SVG), then each place it searches; **Ctrl+Tab** moves between them (Tab moves between One Search's filters). Ending what you type with `/` also shows only folders.

**Enter** opens; **Ctrl+Enter** shows it in its folder. Empty, it lists what you use most.

It searches Desktop and Documents (both your home folder's and, when OneDrive backs them up, OneDrive's: chips "OneDrive Desktop", "OneDrive Documents"), Downloads, `~/repos`, `~/source/repos` and git repos in your home folder. It watches them, honors `.gitignore` (so a repo's ignored files, like a Power BI project's `.pbix` copies, don't show; its `.pbip` does) and ranks files you open higher. Folders, then files, whose own name matches come first. Folders named in `default/launcher/finder-skip.txt` (node_modules, duckdb, …) are skipped. Search is by [fff](https://github.com/dmtrKovalenko/fff).

Your Linux in WSL is one more place, its chip marked with the distro's logo (Arch or Ubuntu): Linux keeps a list of its home's files itself (refreshed every 10 minutes and when `~/src` changes, .gitignore honoured, no hidden folders), so the finder never crawls `\\wsl.localhost`. A Linux file opens in Linux (`omawin launch editor`); Ctrl+Enter shows it in Explorer.

**Linux apps:** a GUI app you install in your Linux (WSLg puts it in Start as "Firefox (archlinux)") is an app here too, with "Arch app" beside it; typing `linux` or the distro's name finds them.
