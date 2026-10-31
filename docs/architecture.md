# Architecture

How omawin fits together. For using it, see [`manual/`](../manual/); for install and links, [install.md](install.md).

## The idea

**GlazeWM catches the keys; `omawin.exe` names every action; the always-running menu app does the work.** Everything else is config, linked into place so the running apps read it straight from this repo.

```
key ──> GlazeWM (config/glazewm/config.yaml)
          └─ shell-exec --hide-window omawin.exe <group> <name>
                └─ bin/commands.txt says what that is:
                     app <request>   → the menu app (shell/menu-app/, already running): instant
                     pwsh <script>   → a PowerShell script in this repo (hidden from a key)
                     run <program>   → a program
```

The same names work from a terminal (`omawin capture text`, `omawin theme set gruvbox`), from the system menu (`default/launcher/menu.txt` rows with an `omawin <command>` action) and from the bar.

## omawin.exe

`bin/omawin.cs`, compiled by the C# compiler that ships with Windows into `~/.local/bin` (C# 5). It reads `bin/commands.txt`, finds the longest command the words spell (`theme set gruvbox` = `theme set` + `gruvbox`), and runs its action with the remaining words appended. It starts in ~35 ms, much less than PowerShell's ~300 ms.

- **In a terminal** (it has a console or its output is redirected), scripts run in that terminal and it waits, returning their exit code.
- **From a key** (no console), scripts run hidden and it returns at once.
- `app` requests are written to `%LOCALAPPDATA%\launcher\system-menu-request`, and the named event `Local\dotfiles-system-menu-signal` wakes the menu app. If the app isn't running, it's started with the request as arguments.
- The repo is `%DOTFILES%` if set, else `~/omawin`.

## Defaults and launching (`omawin default`, `omawin launch`)

Like Omarchy's defaults, what opens things is a per-PC choice, never named in keys or scripts:

- `bin/defaults.txt` lists the supported terminals (noctty first, the default; Windows Terminal), editors (Neovim, Neovide, VS Code) and the AI agent in Neovim's split (OpenCode first, the default; Claude Code: `config/nvim/lua/plugins/ai.lua` reads the pick when it opens one, through LazyVim's sidekick extra). `omawin default <kind> <name>` saves a pick in `%LOCALAPPDATA%\launcher\defaults\<kind>`, installing it with winget first if it's missing.
- Everything that opens a terminal goes through `omawin launch terminal [--dir] [--title] [--wsl] [-- command]`, `omawin launch project <folder> [--wsl]` and `omawin launch editor <path>`, native code in `omawin.exe` that knows how to drive each one.
- Launched programs get a fresh environment from the registry (`FreshEnvironment`), not the stale one of whatever ran omawin (GlazeWM keeps the environment it started with).
- **noctty** (the terminal; our fork, [mjveilleux/noctty](https://github.com/mjveilleux/noctty), installed per-user into `%LOCALAPPDATA%\Programs\noctty` by `bin/noctty.ps1`): `omawin start terminal` (at login) starts it with no window (`--initial-window=false`; its config keeps it running after the last window closes). A plain new terminal is `noctty.com +new-window --working-directory=…` handed to that running instance, so there's no start-up; the fork keeps one PowerShell started ahead of time (`windows-spare-shell`), so the prompt is ready ~0.3 s after the key. Anything that runs a command (a Linux shell, a project) is a one-off noctty process of its own, since the running instance refuses commands from outside.
- **Projects** are [herdr](https://herdr.dev) sessions named after the folder (`config/herdr/config.toml`). A session that isn't running is started headless and, if empty, given a tab per `default/launcher/project-tabs.txt` line; then a noctty window attaches to it. Sessions keep running with no window, so opening the project again reattaches. Linux projects use the herdr in WSL, in the Linux `omawin default linux` picked (Arch or Ubuntu).
- **Win+Enter** (`omawin terminal here`, native: PowerShell would add ~0.35 s): over noctty, the fork takes the folder `inherit` in the request as that of its window in front (the shell reports its folder with OSC 7). Otherwise it reads a Windows Terminal tab's title, or File Explorer's folder.
- **Measuring**: `omawin profile terminal` presses Win+Enter through GlazeWM and prints a timeline from marks `omawin.exe`, our noctty fork and the profile write while `%LOCALAPPDATA%\launcher\trace-on` exists (QueryPerformanceCounter, shared by all processes). `omawin profile login` says how many seconds after sign-in each part was up, from process start times and those marks (`-Arm` turns them on for the next sign-in). `omawin profile menu` does the same for the menu keys (key → `omawin send` → `menu request` → `menu handle` → `menu shown` → `menu painted`, the next composed frame) and for typing in the launcher (`menu typed` → `rendered` → `list-painted`); its arithmetic is `src/cli/Profile/MenuProfile.cs`, tested in `cli.Tests`.

## The menu app (`shell/menu-app/`)

One WinUI 3 app (.NET 9, unpackaged), `SystemMenu.exe` in `%LOCALAPPDATA%\system-menu`, built by `shell\menu-app\build.ps1`. GlazeWM starts it hidden at login (`omawin start`) and it stays running, so its windows show instantly. A second copy only passes its arguments to the first and exits: a mutex detects it, then the request file and event above carry the arguments.

| File | What it is |
|---|---|
| `App.xaml.cs` | startup, the single-copy check, `HandleRequest` (which request opens what) |
| `MainWindow.xaml(.cs)` | the one window: system menu, launcher and file finder are modes of it; theming, keys, running menu actions |
| `MainWindow.Pages.cs` | launcher pages (Projects, Clipboard, Localhost, Audio, Reports, Shortcuts, VPN): how they're shown |
| `Pages.cs` | the pages' data: repos, servers, clipboard history, reports, keys |
| `Audio.cs` | the Audio page's data (Win+A): speakers and microphones through Windows' Core Audio, their volume and mute, and the default device |
| `MenuModel.cs` | reads `default/launcher/menu.txt` and the theme; paths (`Repo`, `StateDir`) |
| `Launcher.cs`, `Fuzzy.cs` | launcher items + installed apps, keywords, launch history; fzy-style fuzzy scoring |
| `FileSearch.cs` | the file finder, on [fff](https://github.com/dmtrKovalenko/fff)'s C library (`lib/fff_c.dll`, a pinned release that `build.ps1` downloads and checksums; not committed) |
| `AppIcons.cs` | real app icons |
| `Background.cs` | work done all the time in the background: clipboard history, the installed-apps index, the reports index |
| `Snip.cs` | screenshots, text from the screen, the colour picker and QR reading (below; QR by ZXing.Net, ~0.5 MB) |
| `Calc.cs` | the launcher's calculator: a small parser (no script engine), tried only when the query could be a sum |
| `About.cs`, `MainWindow.About.cs` | the About page (`omawin about`): version and PC facts, read off the UI thread |
| `StayAwake.cs` | `omawin toggle stay-awake`: `SetThreadExecutionState` held on the UI thread while on |
| `Motion.cs`, `MotionTokens.cs` | every animation's numbers (Omarchy's: in within 200 ms, out in 130, ease-out-quint) and helpers on the compositor; `omawin toggle animations` writes `%LOCALAPPDATA%\launcher\animations`, which it watches with Windows' own Animation effects switch; off, every animation jumps to its end |
| `Tiling.cs` | Omarchy's window placement: on each focus change (GlazeWM's IPC socket) it sets the focused window's next split across the split it sits in, so new windows spiral like Hyprland's dwindle layout: A | B, then B over C, then C | D |

Requests it understands: `--launcher [page]`, `--page <name>`, `--files`, `--snip [text|color|qr]`, `--toggle stay-awake`, `--toggle animations [on|off|status]`, `--open <menu id>`, `--start-hidden`, no arguments (system menu); plus `--select`, `--type` and `--key`, used for walkthrough recordings, and `--hide` (at once, no animation) for `omawin profile menu`.

### Screenshots and text (`Snip.cs`)

1. At start-up, the app creates the overlay, a borderless WinForms window. It lives hidden on its own STA thread, which the clipboard requires. WinForms is added with a `FrameworkReference`; `UseWindowsForms` would also switch on WPF's XAML build, which clashes with WinUI.
2. On `--snip`, one `BitBlt` copies every monitor (a few ms). The overlay shows that frozen picture dimmed, over all monitors.
3. Selecting:
   - Drag a region, which is drawn from the undimmed copy.
   - Hover or click a window: the window under the mouse is found by making the overlay briefly click-through, and its edges come from DWM's frame bounds, not counting shadows.
   - Enter takes everything.
   - Esc, right-click or switching away cancels.
   - T switches between picture and text.
4. A picture goes to the clipboard and is saved as a PNG in `Pictures\Screenshots`.
5. Text goes through Windows' own OCR (`Windows.Media.Ocr`: offline, in Windows' languages). Small regions are scaled up first (up to 3×), because it reads letters under ~20 px poorly. The lines go to the clipboard.
6. A small note at the bottom of the screen says what was copied.

About 0.2 s from the key to the overlay; Windows' Snipping Tool cold-starts each time.

## The bar (`shell/menu-app/Bar/`, `default/bar/`)

The bar is drawn by the menu app itself, natively, with no web engine ([native-bar.md](native-bar.md)): Omarchy 4's bar, rebuilt. `default/bar/bar.json` says which widgets sit where (left, centerBegin, center, centerEnd, right, as Omarchy's `shell.json`); each widget is a class in `Bar/Widgets.cs` with the same name and behaviour as Omarchy's, fed by GlazeWM's IPC (`Bar/GlazeWm.cs`), the tray (`Bar/Tray.cs`), the clock, network, battery and audio. Its logo is Omarchy's icon font (`default/bar/omarchy.ttf`). There is no dock. Clicks that start something go through `omawin.exe`.

At sign-in, `omawin start bar` (`bin/omawin.cs`) first paints a plain strip in the theme's background (`%LOCALAPPDATA%\launcher\bar-background.txt`, written by `themes/render.ps1`), the bar's size, across every monitor, in ~50 ms; the menu app's bar draws over it within a few seconds.

One native helper, `bin/voice-meter.cs`, built into `~/.local/bin` by install: the dictation indicator. It says whether dictation is listening (from Windows' microphone privacy log) and the default mic's level; it never opens the mic itself, and quits when the bar that started it is gone. Beside it, `bin/path-doctor.cs` (`omawin-path.exe`, `omawin path`): both PATHs read raw, what's broken in them, which program wins for each command, and a fix of your PATH with undo.

Plugins add to the bar ([`plans/05-plugins.md`](../plans/05-plugins.md)): `shell/plugins/<id>/` in Omarchy's format. The first is the music player (`shell/plugins/omawin.music/`, QML), drawn by the QML host (`src/qs-host`, `omawin-qs.exe`): the bar keeps an empty cell for each plugin that's on (`Bar/PluginSlot.cs`, from `%LOCALAPPDATA%\launcher\plugins.txt`) and starts the host over it with `--plugin-dir <folder> --at x,y,w,h --parent <bar's pid>` (roadmap 4.9.4).

## The music player (`src/music/`)

`omawin-music.exe` (.NET 9, no window, one file in `~/.local/bin`, built by `src\music\build.ps1`) plays music, podcasts and radio; its face is the bar's music plugin (`shell/plugins/omawin.music/`). One exe, three roles:

- `--serve`: **the player** (`Player.cs`). Started by the first command that needs it, out of its starter's job so it outlives it; quits after 20 idle minutes. It plays through **mpv** (`Mpv.cs`), which it starts hidden (`--idle --no-video --no-config`) and drives over mpv's JSON pipe `\\.\pipe\omawin-mpv`:
  - The whole playlist goes to mpv as links (`loadfile … append`). mpv's ytdl hook runs our yt-dlp (with Bun) for a YouTube track only when it's about to play, so a playlist of hundreds starts at once and an expired stream is simply looked up again; a podcast episode is its audio file. mpv decodes with its own ffmpeg and plays to whatever speaker Windows is using.
  - What's playing comes back as mpv's events (`observe_property` on the track, pause, position; `start-file`, `end-file`), kept for the bar's `S` lines. mpv shows in Windows' media controls itself (`--media-controls`), so the media keys, the lock screen and the bar's media provider see it.
  - A track that fails (an error, or an end before any sound) is skipped; after three in a row the player stops and the bar shows why (yt-dlp's own `ERROR:` line, from mpv's log messages), rather than going round a playlist that can't play.
  - While dictation is listening (the same test as `voice-meter`), it lowers mpv's volume to 10%.
  - **Ratings** (`Ratings.cs`): 1-5 stars a track (`rate <n>`, the bar's stars, keys 1-5 in the panel), in `~/Music/omawin/ratings.tsv` (plain text: the library's loader and watcher take every `.json` there for a playlist). 1 star skips the track and leaves it out of every playlist from then on. A skip is not a rating.
  - **Suno** (`suno/`): Suno's audio is encrypted (only its own page decrypts it; yt-dlp refuses the site), so a Suno playlist plays in Suno's own page: `omawin-music --suno` (`SunoHost.cs`), a WinForms window with WebView2, profile in `%LOCALAPPDATA%\launcher\suno` (sign in there for private playlists). The player drives it like mpv (`SunoEngine.cs`, JSON lines over `\\.\pipe\omawin-suno`): open a page and press its own Play button; play, pause, next, back, seek; a gain for your volume and ducking. `suno-bridge.js`, injected before Suno's scripts, catches the page's media-key handlers (to work it), scales its `<audio>`'s volume by the gain, and reports title, artist, cover and position on the audio's own events (a hidden page's timers are throttled). The window is never hidden, only parked off screen (a page never shown doesn't start playing); the bar docks it beside the panel on the Suno tab (`suno-show x y w h` / `suno-hide`), and GlazeWM ignores it. Adding a Suno link (`SunoLinks.cs`) only reads its name and song list from the public JSON Suno's pages load.
  - **Your station** (`Station.cs`, `station [songs|jams]`): an endless queue, never saved. Seeds are your 4- and 5-star tracks (else your playlists' tracks); a seed's YouTube radio (`watch?v=<id>&list=RD<id>`, the first 60 through yt-dlp, ~5 s) and your own playlists give candidates; 1-star tracks, artists you average 1.5 or less on, recent tracks and the other length (songs under 10 minutes, jams longer) are dropped; the rest are ranked by your stars for them and their artist, with chance in it. The player tops mpv's queue up a few tracks ahead.
  - Why mpv: Windows' own `MediaPlayer`, used until October 2026, stopped playing YouTube's streams (it opened them, then failed every track with `0xC00D0035`). Omarchy's music plugins all play with mpv and yt-dlp the same way.
  - mpv comes with the `music` preset (`shinchiro.mpv`); `Mpv.Exe()` also finds it in `%LOCALAPPDATA%\Programs\mpv` or winget's folders when it isn't on the PATH. `src\music\build.ps1` stops the player's mpv along with the player.
- `watch`: **the bar's feed**, like `voice-meter`: `S {state}` lines from the player, `P [collections]` whenever `~/Music/omawin` changes, and `L <level>` (the speakers' peak meter) ~25 a second while playing, for the waves. It never starts the player, and quits when the bar that started it does. The bar tells it over stdin whether its panel is open (`panel 1|0`) and whether its monitor has focus (`focus 1|0`); it keeps both as named events (`Local\omawin-music-<pid>-panel|focus|open|close`).
- `open` (Win+M): **no player needed**. It reads every watch's flags: if any panel is open, it signals `close` to those; otherwise `open` to the focused monitor's bar. The watch passes it on as `O open` / `O close`.
- anything else: **a command**, sent to the player over the pipe `\\.\pipe\omawin-music` (a JSON array of words per line; one JSON line back), or done on the library files directly (`add`, `list`, `tracks`, `refresh`, `rename`, `remove`).

The library is files, not a database: `~/Music/omawin/<slug>.json` for music, `podcasts/<slug>.json` for podcasts, `radio/<slug>.json` for stations (title, source link, tracks; `Library.cs`). A station is one live stream (`Library.AddRadio`): adding one reads the stream's headers only — its content type (a web page is refused) and its `icy-name`, which names it (`NTS Radio`, `NTS Radio 2` when the address is a second channel) — and the player hands that address to mpv as it is, so `live` in the state tells the bar to show `LIVE` instead of a progress bar, and seeking does nothing. Readers: `YouTube.cs` (yt-dlp, with Bun) and `Feed.cs` (RSS, and Apple Podcasts links through Apple's lookup). Slugs are unique across every folder, so commands only need the slug.

## Scripts that remain

- `default/launcher/menu.ps1`:
  - runs the system menu's longer jobs, which the menu app hands it as `-Run <row id>`, in a small floating "menu-task" terminal: installs, removals, `task` rows
  - keeps `installed.txt`
  - `-SetWallpaper` and `-SearchPackage`
  - `-Install "<source>:<id>;..."` and `-InstallManager winget|choco|scoop` for the launcher's Packages page (`shell/menu-app/Packages.cs`, `MainWindow.Packages.cs`): winget's catalog from `bin/pkg-index.ps1`, Scoop's from its cloned buckets, Chocolatey's community feed asked as you type
- `default/launcher/common.ps1`: the fzf look and helpers for the terminal pieces that remain.
- `default/launcher/rename-workspace.ps1`: Alt+N (`omawin workspace rename`), in a small terminal titled "rename".
- Reports: the page is omawin's, the folders and the import are the account folder's (`~/omawin-account`, see [work.md](work.md)).
- `themes/*.ps1`: themes; see [themes.md](themes.md).

## State (`%LOCALAPPDATA%\launcher\`)

Never in the repo:

| Path | What it holds |
|---|---|
| `clipboard\` | clipboard history, newest 200, one file each |
| `launch-history.json` | what you open, so it ranks higher |
| `apps.tsv` | the installed-apps index |
| `installed.txt` | installed winget / choco / scoop packages |
| `reports.tsv` | the reports index |
| `wsl-projects.txt` | WSL repos, reused while WSL is off |
| `fff\` | the file finder's indexes |
| `dirs\` | PowerShell tab title → folder, for Win+Enter |
| `theme`, `background`, `nvim-theme`, `lockscreen*` | the current theme, its background, and the lock screen |
| `backgrounds\`, `thumbnails\` | themes' wallpapers, downloaded once; small copies of every theme's backgrounds, for the theme and background pickers (the release brings them) |
| `tasks\` | menu-task scripts |
| `migrations.txt` | migrations already run on this PC |
| `music.log` | the music player's errors (its playlists and podcasts are in `~/Music/omawin`) |
| `system-menu-request` | the request being handed to the menu app |
| `system-menu.log` | the menu app's errors |
| `stay-awake` | present while stay awake is on (removed when it's turned off or the menu app starts) |
| `animations` | `on` or `off` (`omawin toggle animations`); the menu app writes the default on its first start: `off` in a virtual machine |
