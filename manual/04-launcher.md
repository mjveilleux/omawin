# The launcher and file finder

## Launcher (Win+Space)

Like Omarchy's Super+Space. It opens on tiles:
- a big tile for the first item in `default/launcher/items.txt` (`icon | name | command`), the terminal
- Projects and Clipboard, showing your recent projects and what you last copied
- your next four items, with a server count on Localhost
- a Menu bar
- **Frequent**: the eight apps you open most

Arrow keys move between tiles; the selected one lifts. Typing searches your items and every installed app (Start's All apps, desktop and Store apps, with their real icons). A newly installed app shows up within seconds.

**Typing searches** with fuzzy matching:
- **Word starts, capitals and runs of letters count most**, so "pbi" finds Power BI and "vsc" finds VS Code.
- **Program and package names match too** ("pwsh", "taskmgr").
- **One typo per word is forgiven** ("termnial").
- **Shorter names come first**, and the matched letters are highlighted.
- **It learns:** what you open ranks higher, most of all for what you'd typed.

Enter opens; Esc or Win+Space again hides it. **Menu** turns the window into the system menu (Esc comes back).

### Calculator

Type a sum and its value is the top row: **Enter copies it** (and closes the launcher). It knows `+ - * / ^` (or `**`), `x × ÷`, parentheses, `%` (`200*15%` is 30; `10 % 3` is the remainder), `pi` and `e`, and `sqrt abs round floor ceil ln log sin cos tan` (radians): `12*4.5`, `2^10`, `sqrt(2)*pi`, `(1+2`. A bare number or an app's name is still a search.

### Pages

Projects, Localhost, Clipboard, Audio, Quick settings, Packages, Reports, Shortcuts and About open in the same window: type to search, Esc to go back.

### Packages

Like Omarchy's package picker, in the launcher (System menu › Install › Package, the Packages item, or `omawin pkg find`): pick a package manager, type to find a package, Enter installs it.
- **Tab switches between winget, Chocolatey and Scoop** (or click their chips). winget's ~16,000 packages and Scoop's buckets are searched on the PC as you type, with the launcher's fuzzy matching; Chocolatey has no catalog on the PC, so its community site is asked once you stop typing.
- **Beside the list:** what the package is, its version, its homepage and license (winget's take a second or two), and whether it's already here (also ✓ in the list).
- **Ctrl+Space picks several**, from any of the three; Enter installs them one after another.
- **Installs run in a small terminal you can watch** (it stays open at the end). Chocolatey asks for admin first; Scoop installs into your user folder without.
- **A manager not on the PC?** Its tab offers to install it: winget (Microsoft's App Installer) registered for your account or put in, Chocolatey from winget or its own installer (asks for admin), Scoop with its own installer from scoop.sh.

`omawin pkg install` is the same for winget in a terminal, with fzf.

- **Projects**: git repos on Windows (in your home folder or one folder down, like `~/omawin` or `~/src/glazewm`; and Documents, OneDrive Documents, `~/source/repos`, `~/repos`) and in your Linux in WSL (under ~), zoxide favorites first. A repo you clone or `git init` shows up within seconds.
  - Opening one gives a terminal window on that project's own [herdr](https://herdr.dev) session, with tabs **shell | nvim | claude | opencode** (`default/launcher/project-tabs.txt`).
  - The session keeps running when the window closes, agents and all; opening the project again reattaches to it.
  - Linux repos open inside WSL, marked with your distro's logo (Arch or Ubuntu). WSL is only asked when it's already running (a cold start takes ~8 s); otherwise the last list is reused.
- **Localhost**: the web servers running right now, on Windows or in WSL ("wsl"), with each page's title, port and program. When Windows has reserved a common dev port at start-up (Hyper-V and WSL take random ranges, so a server on 3000 or 8080 fails though nothing is listening there), a yellow line says which and what to do. Under them are your favorite sites from `default/launcher/sites.txt`.
  - Enter opens one in Chrome.
  - **Ctrl+X** stops a server after you confirm. That's the program listening on its port, or the process inside WSL for a WSL server.
- **Clipboard** (or **Win+Shift+V**): everything you've copied (text), newest first, searchable, with the full text in a preview.
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

Also the launcher's **Files** item. A wider window:
- results on the left: the name with matched letters, the folders it's in, and when it changed or its git status
- a preview on the right: the picture itself for pictures (and how big it is), the first lines of text and code, a folder's contents
- chips on top: All, **Folders** (only folders), **Images** (only pictures: PNG, JPG, GIF, WebP, BMP, ICO, TIFF, HEIC, AVIF, SVG), then each place it searches; **Tab** moves between them. Ending what you type with `/` also shows only folders.

**Enter** opens; **Ctrl+Enter** shows it in its folder. Empty, it lists what you use most.

It searches Desktop and Documents (both your home folder's and, when OneDrive backs them up, OneDrive's: chips "OneDrive Desktop", "OneDrive Documents"), Downloads, `~/repos`, `~/source/repos` and git repos in your home folder. It watches them, honors `.gitignore` (so a repo's ignored files, like a Power BI project's `.pbix` copies, don't show; its `.pbip` does) and ranks files you open higher. Folders, then files, whose own name matches come first. Folders named in `default/launcher/finder-skip.txt` (node_modules, duckdb, …) are skipped. Search is by [fff](https://github.com/dmtrKovalenko/fff).
