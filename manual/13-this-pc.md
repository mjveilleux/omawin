# This PC: what Windows knows and doesn't show

Windows records why it restarted, who holds a file, what starts with it and which app is online, and shows almost none of it. These commands answer in your terminal, in plain sentences; **System menu › This PC** opens each in a small terminal you can read. None of them needs admin to look; the few changes that do (the hosts file, a startup entry for every account) say exactly what will change and then ask Windows once.

| Command | What it answers |
|---|---|
| `omawin who-has <path>` | which programs hold a file or folder open |
| `omawin health [days]` | why the PC restarted, crashed or woke up, and which apps crashed |
| `omawin startup` | everything that starts with Windows, with a switch each |
| `omawin top [name]` | processes by app, the busiest first |
| `omawin connections [app]` | which apps are talking to the internet, and to whom |
| `omawin hosts` | the hosts file, as entries |
| `omawin dns [preset]` | each network's DNS, and Cloudflare, Quad9, Google or your router's; `encrypt` for DNS over HTTPS |
| `omawin proxy` | this account's system proxy, set and switched |
| `omawin disk` | where a developer's disk space went, and getting it back |
| `omawin pip [title]` | a window live in a small window on top (picture-in-picture) |
| `omawin display` | each set of monitors' layout, put back when they're plugged in again |
| `omawin wifi share [network]` | the Wi-Fi as a QR code a phone joins from |
| `omawin clip <transform>` | the clipboard's text converted in place |
| `omawin mic` | every microphone muted or unmuted at once |
| `omawin classic-menu` | Explorer's full right-click menu back, or Windows 11's short one |
| `omawin updates` | what Windows Update is waiting to install, and the restart when you choose |

## "The file is open in another program"

`omawin who-has C:\path\to\file` lists who holds it: the program, its pid, whether it's a window, a service or a terminal program. Then:
- **c** closes them politely (as clicking ✕: they may ask to save), **e** ends them after asking; `c2` or `e2` acts on one of the list.
- With no path it takes the one on the clipboard: in Explorer, Shift+right-click the file › **Copy as path**, then **System menu › This PC › Who has this file?**
- A folder: every file in it is checked. If nothing holds one and the folder still won't go, a terminal is probably sitting in it (its current folder, which Windows doesn't report): `cd` out of it.
- Windows' own critical processes are never ended; another account's or an admin program says it needs admin.

From Windows' Restart Manager, the same thing installers use to ask which programs to close.

## Why did it restart, crash or wake up?

`omawin health` reads Windows' event logs for the last 30 days (`omawin health 7` for a week):
- **Restarts and crashes**, newest first: blue screens with their stop code (`0x9F DRIVER_POWER_STATE_FAILURE`, searchable), restarts without a shutdown (power lost, or it froze; held power button; died asleep), and who restarted it and why (`Restart, by Windows Update: Operating System: Service pack (Planned)`). Bad ones are marked `!`. How many times it started.
- **Apps that crashed or hung**, by app, with how many times and the module that failed.
- **What woke it from sleep**: a device (often the mouse or the network card) or a timer (often Windows Update).
- Whether Windows is **waiting for a restart** to finish installing updates (said first, when it is).
- **Slow starts**, and what slowed them, when Windows lets this account read that log (else it says so: it needs admin or the Event Log Readers group).

## What starts with Windows

`omawin startup` lists every place it's kept, on and off: your Run key and Startup folder, the PC's (every account's), and programs' services set to start automatically (Windows' own aren't listed).
- `omawin startup off Discord` stops one; `omawin startup on Discord` brings it back. Part of a name is enough when only one matches.
- It's Task Manager's own switch: the entry stays where it is, only marked off, so Task Manager › Startup apps agrees and nothing is lost.
- Yours switch at once. The PC's and services need admin: it says what will change and Windows asks once. A service turned off is set to start by hand (Manual), not disabled, so anything that needs it can still start it.

## Processes and connections

`omawin top` measures one second and lists apps by CPU, then memory (as Task Manager counts it: private memory). An app's helpers count toward it (a browser's tabs, a WebView, a terminal's console host); Windows' own processes are one line, **Windows**. `omawin top code` lists that app's processes with their pids and the ports they listen on; `omawin top end <pid>` ends one after asking (never Windows' own).

`omawin connections` lists the apps connected to other machines now, each with who it's talking to (names looked up, a second at most) and the service (`https`, `ssh`…). `omawin connections chrome` lists all of one app's.

## The hosts file

- `omawin hosts` lists the entries, on and off.
- `omawin hosts add 127.0.0.1 myapp.test api.myapp.test` adds one; if a name already points elsewhere it says where.
- `omawin hosts off myapp.test` comments it out (marked `# omawin-off`, so `omawin hosts on myapp.test` restores it exactly); other names on the same line keep working.
- Every change shows the lines that change, then asks Windows once (the hosts file needs admin). The old file is kept beside it as `hosts.backup-<date>-<time>`, and Windows' DNS cache is cleared so the change shows at once. Nothing else is touched: the lines you wrote stay as you wrote them.

## DNS

`omawin dns` says which DNS each connected network uses: your router's (handed out automatically), or set by hand, named when it's one of the well-known ones. `omawin dns cloudflare` (or `quad9`, `google`) switches every connected adapter to it; `omawin dns router` gives them back to your network; add part of an adapter's name for one only (`omawin dns quad9 wi-fi`). It says which adapters change to what, then Windows asks once; the cache is cleared so the next lookup uses the new servers. **System menu › This PC › DNS** has the same.

**Encrypted DNS.** `omawin dns encrypt` turns on DNS over HTTPS (Windows 11) for the servers in use that Windows knows an encrypted address for: Cloudflare, Google and Quad9. Lookups then go encrypted, and none is sent in the clear. `omawin dns encrypt off` is the undo; `omawin dns` says under each network whether its DNS is encrypted. Your router's DNS can't be encrypted this way: switch to one of the three first. **System menu › This PC › DNS › Encrypt DNS** does the same.

## The proxy

`omawin proxy` says which system proxy this account uses (the one Edge, Chrome, winget and most programs follow): a server, the names that skip it, a setup script. `omawin proxy set proxy.example.com:8080` sets one and turns it on (names that should skip it go after: `omawin proxy set proxy.example.com:8080 *.corp.example`; the local network always does); `omawin proxy off` and `on` switch it without forgetting the server; `omawin proxy script <url>` uses a setup script (PAC), `script off` removes it. Each change says what it was before. It's Settings › Network › Proxy, for your account only, no admin; running programs are told at once. The launcher's *System proxy* switch does the on and off.

## Share the Wi-Fi

`omawin wifi share` draws the network you're on as a QR code in the terminal; a phone's camera joins from it. `omawin wifi share <network>` for another saved one (with no connection it lists them); `--show` prints the password too. Company networks that sign in with an account have nothing to share. Windows gives the password to some accounts only: if it holds it back, it says so (run it from an admin terminal).

## The clipboard's text, converted

`omawin clip <transform>` changes the text on the clipboard in place, ready to paste; `omawin clip` lists them. **System menu › Trigger › Change the clipboard's text** has the common ones, and says what it did in the note at the bottom.

| | |
|---|---|
| `json`, `json-line` | JSON indented, or on one line (comments and trailing commas are fine) |
| `base64`, `unbase64` | Base64 (UTF-8; URL-safe and unpadded Base64 decode too) |
| `url`, `unurl` | URL encoding |
| `date`, `timestamp` | a Unix timestamp (seconds or milliseconds) as a local date, and back |
| `path` | `C:\Users\me\src` ⇄ `/mnt/c/Users/me/src`, or slashes swapped |
| `trim`, `lower`, `upper`, `plain` | trailing spaces and blank ends gone; case; the text without its formatting |

## Microphones

`omawin mic` mutes every microphone at once (not only the default one: an app may listen on any), or unmutes them when they're all muted; `omawin mic mute`, `omawin mic unmute`, `omawin mic status`. While they're all muted the bar shows a red crossed-out microphone; click it to unmute. It's the same mute as Sound settings, so it holds after a restart. It has no key yet: pick one and add it to `config/glazewm/config.yaml` (`omawin.exe mic`).

## The full right-click menu

`omawin classic-menu` (**System menu › This PC › Full right-click menu**) gives Explorer back Windows 10's full right-click menu, without **Show more options**; again, and Windows 11's short one is back (`on`, `off`, `status` to say which). It's for your account only and needs no admin. Explorer restarts to show it: the taskbar blinks and open folder windows close (in a terminal it asks first; say no and it shows from Explorer's next start).

## Windows Update without surprises

`omawin updates` says whether Windows is waiting for a restart to finish installing updates, whether updates are paused and until when, your active hours (when Windows never restarts by itself), what's waiting to install (as of Windows Update's last check: nothing is downloaded or started by asking) and what was installed lately. **System menu › This PC › Windows Update** has the same, and the common changes:
- `omawin updates restart tonight` restarts the PC at 03:00; `omawin updates restart 22:30` at another time; `omawin updates restart cancel` calls it off. Windows warns whoever is signed in before it happens. No admin.
- `omawin updates pause` pauses updates for a week (`pause 14` for two; five weeks at most, as Settings allows); `omawin updates resume` is the undo. It's the same pause as Settings › Windows Update, which shows it and whose **Resume** works too.
- `omawin updates hours 9-18` sets the active hours by hand (18 hours at most, Windows' limit); `omawin updates hours auto` lets Windows pick them from when you use the PC. They're also settings in the launcher (search *active hours*).

Pausing and active hours are settings for the whole PC: it says what will change, then Windows asks once. Nothing here hides an update or turns Windows Update off.

## Disk space

Windows' own cleanup misses where a developer's space goes. `omawin disk` measures it (**System menu › This PC › Disk space**):
- **Caches**: npm, pnpm, Yarn, Bun, NuGet, pip, uv, cargo, Go, Gradle, Maven, mise's downloads, and Temp's files older than a week, biggest first, wherever their environment variables put them. Everything in them comes back when a tool needs it. `omawin disk clean npm` empties one: through the tool's own command when it has one and it's installed (`npm cache clean --force`, `uv cache clean`, `go clean -modcache`…), else by deleting the folder's contents; files in use are left. It says the size and asks first.
- **Linux disks**: each WSL distro's disk and Docker Desktop's, with what they really take. They grow and never shrink by themselves; `omawin disk sparse Arch` stops that distro and makes its disk give freed space back to Windows from now on (WSL's own switch, no admin). Docker's: `docker system df`, then `docker builder prune` and `docker system prune`.
- **Old projects' build output**: `omawin disk projects` looks through your project folders (`~/src`, `~/code`, `~/projects`, `~/repos`, `~/source/repos`, Documents) for `node_modules`, `target`, `bin` and `obj`, `.venv`, `.next`, `build` and the like, only beside the file that makes them (a `target` with no `Cargo.toml` beside it is left alone), in projects untouched for 30 days. `omawin disk projects ~/work 90` for another folder or age; `omawin disk projects clean` deletes what it listed, after asking. Each comes back with `npm install`, `cargo build` and so on.

Nothing outside a cache or a project's build output is ever deleted, and nothing without asking.

## Picture-in-picture

`omawin pip teams` keeps an eye on a window while you work in another: the window whose title holds the words, live, in a small window that stays on top in the bottom-right corner (Windows' own live preview, the one Alt+Tab draws: nothing is recorded). Drag it anywhere, resize it at its edges, scroll to make it fainter or stronger; double-click goes to the window, right-click closes the picture, and it closes by itself when the window does. Bound to a key (`omawin.exe pip` in `config/glazewm/config.yaml`), it shows the window in front; it has no key until you choose one.

## Monitors that follow the dock

Docking and undocking can leave the monitors at the wrong resolution, in the wrong order or with the wrong main one. Lay them out once the way you want (Settings › Display), then `omawin display save` (**System menu › This PC › Monitors: remember this layout**): from then on, whenever that same set of monitors is plugged in, omawin puts the layout back by itself (each monitor's resolution, place and rotation, and which is the main one), a couple of seconds after the last one arrives. Each set is remembered on its own: the laptop alone, the laptop at the desk, the laptop in a meeting room. `omawin display` shows the monitors now and whether a layout is saved for them; `omawin display apply` puts it back by hand, `omawin display forget` stops it. Refresh rates and scaling are left as Windows keeps them. The layouts are in `%LOCALAPPDATA%\launcher\displays.txt`, a text file you can read and edit.
## The right-click menu's add-ins
`omawin shell-extensions` (**System menu › This PC › Right-click menu add-ins**) lists every add-in programs have put into Explorer (7-Zip, Git, an antivirus, a cloud drive…), the ones that aren't Windows' own first, each with its maker, where it shows (right-click on files, on folders, drag and drop…) and its program. Windows loads them all into Explorer itself, so one slow or broken add-in slows or hangs every right-click. `omawin shell-extensions off <name>` leaves one out, through Windows' own list of blocked add-ins, for your account only; `on <name>` puts it back. Explorer reads the list when it starts: add `--now` to restart it then.

## Environment variables

`omawin env` (**System menu › This PC › Environment variables**) lists your environment variables and the PC's, a PATH as its entries. `omawin env set <name> <value>` and `omawin env unset <name>` change yours at once and tell Windows, so the next terminal you open sees it; `omawin env undo` puts back what was there before the last change, a variable that wasn't set included. Add `-Machine` for the PC's, which every account shares: the change is shown first, then Windows asks for an administrator once. `omawin path` checks the PATH itself (broken entries, which `node` or `python` wins).
