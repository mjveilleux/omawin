# 08: Omarchy review: every feature, and what omawin's plan does with it

A review on **2026-10-30** of Omarchy at commit **`7f4b1dd`** (`mjveilleux/omarchy`, `git -C ~/omarchy rev-parse --short HEAD`) against omawin and its plan. Read on Omarchy's side: every command in `bin/` (557, 228 of them hidden helpers) with its `# omarchy:summary=` header and the groups in `bin/omarchy` (`GROUP_DESCRIPTIONS`), the whole menu tree (`default/omarchy/omarchy-menu.jsonc`), every shell plugin and its `manifest.json` (`shell/plugins/`), the key bindings with their descriptions (`default/hypr/bindings/*.lua`), `default/bash/`, `default/themed/`, `install/`, `docs/` and `manual/`. On omawin's side: `bin/commands.txt`, `launcher/menu.txt`, `launcher/items.txt`, `glazewm/config.yaml`, `manual/`, the menu app's sources where a row needed checking, [mission-todo.md](../mission-todo.md), [roadmap.md](../roadmap.md), this folder (01, 02, 04, 06; 03, 05 and 07 are named in [README.md](README.md) but weren't in the folder at the time, so their roadmap items are cited instead), [plugins.md](../plugins.md), [one-search.md](../one-search.md), [install-speed.md](../install-speed.md) and [shell-apps.md](../shell-apps.md).

**Status words:** **have**: omawin does it today (the command or file is named). **planned**: a roadmap item, a plan section or a mission-todo line covers it (named; "mission-todo only" means it isn't in the roadmap yet). **new**: neither omawin nor its plan has it. **n/a**: doesn't apply on Windows, with the reason. Hidden helpers (`omarchy:hidden=true`) are grouped with the feature they serve. Keys in this file are Omarchy's; nothing here binds a key in omawin (AGENTS.md: keys never change without the maintainer).

---

## 1. Apps and launching

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `omarchy-launch-terminal`, `cmd-terminal-cwd` (Super+Return) | terminal in the focused terminal's folder | **have**: `omawin terminal here` (Win+Enter), `omawin launch terminal` |
| `launch-editor`, `default-editor` (Super+Shift+N) | default editor | **have**: `omawin launch editor`, `omawin default editor` (no key) |
| `launch-browser`, `default-browser` (Super+Shift+B, Alt for private) | default browser, private window | **planned** (default browser: mission-todo track 6 "Defaults beyond terminal and editor"); a browser key is **new** (key, maintainer's call) |
| `launch-nautilus` (Super+Shift+F) | file manager | **have**: Win+F finder (`omawin files`); Explorer in the launcher |
| `launch-nautilus-cwd` (Super+Alt+Shift+F) | file manager in the terminal's folder | **new** (`omawin files here`, below) |
| `launch-or-focus`, `hyprland-focus-app`, `launch-or-focus-tui`, `launch-or-focus-webapp` | launch, or focus the open window | **planned**: roadmap 5.3 (launch-or-focus through GlazeWM IPC, `menu-app/WebApps.cs`) |
| `launch-tui`, `tui-install`, `tui-remove`, `tui-remove-all` | TUIs as launcher apps | **planned**: roadmap 5.3 (`omawin install tui`) |
| `launch-webapp`, `webapp-install`, `webapp-remove`, `webapp-remove-all`, `webapp-window-class` | sites as app windows | **planned**: roadmap 5.3, plan 06 `bin/webapp.ps1` |
| `webapp-handler-hey`, `webapp-handler-zoom` | mailto / zoom links into a web app | **new** (an addition to 5.3, below) |
| app keys: Signal, Obsidian, Omawrite, 1Password, ChatGPT, Grok, HEY mail/calendar, YouTube, WhatsApp, Google Messages/Photos/Maps, X (`bindings/applications.lua`) | one key per daily app | **new** as keys (maintainer's call); the apps are **have** in Install, the web apps **planned** (5.3) |
| `launch-1password`, `launch-signal`, `launch-spotify` | launch, or install first when missing | **new** (small: a launcher item whose target is missing offers its Install row; fits 5.8's conditions) |
| `launch-terminal-tmux` (Super+Alt+Return), `launch-terminal-herdr` (Super+Ctrl+Return) | attach the persistent multiplexer session | **have**: `omawin launch project` (herdr sessions), `omawin agent open`; tmux is in Linux (roadmap 3.2) |
| `launch-about` | fastfetch About | **have**: `omawin about` |
| `launch-discord-community`, Learn › Community | Omarchy's Discord | **n/a**: omawin has no community channel yet |
| `launch-config-editor` | open a config with a toast | **have**: menu `edit` rows (Setup, Style) |
| `launch-floating-terminal-with-presentation`, `install-and-launch`, `install-app` | a floating terminal for a job | **have**: menu `task` rows |
| `omacalc` (Super+Shift+Q) | calculator | **have**: the launcher's math row (`Calc.cs`) |
| Activity: btop (Super+Shift+T) | process monitor | **planned**: 8.17 process list in the launcher; btop is an Install › Tools row |
| Docker TUI (Super+Shift+D, `launch-docker-tui`) | lazydocker | **planned**: roadmap 5.2 |
| `restart-app` | kill and relaunch an app | **new** (low value; `omawin restart app <name>`) |

## 2. The menu, launcher and search

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `omarchy-menu` (Super+Space), `shell/plugins/menu` | the Omarchy menu | **have**: system menu (`omawin menu`, Win+Alt+Space) and launcher (Win+Space); **planned**: One Search (1C) |
| Apps (Super+Alt+Space, `dock apps`) | app grid | **have**: launcher apps |
| Learn › Keybindings (`menu-keybindings`, Super+K) | cheat sheet built from bind descriptions | **have** hand-kept (`launcher/keys.txt`, @keys); **planned**: 5.4 (from `glazewm/config.yaml`) |
| Learn › Tmux, Herdr (`menu-tmux-keybindings`, `menu-herdr-keybindings`) | the multiplexer's keys | **new** (herdr keys in the Shortcuts page) |
| Learn › Omarchy, Hyprland, Arch, Neovim, Bash | manuals as web apps | **have**: Learn rows (GlazeWM, Zebar, LazyVim, PowerShell, herdr) |
| `menu-select`, `menu-input`, `file-select`, `menu-file`, `menu-images` | pickers scripts can call (pick, type, choose a file or an image) | **new** (`omawin pick`); image grid **planned** (mission-todo "visual background picker", 1.9) |
| `when` guards, `checked`, providers (`docs/menu.md`) | rows shown by condition | **planned**: 5.8 |
| Setup › Config (Git, Hyprland, Hyprsunset, XCompose) | edit configs | **have**: Setup / Style `edit` rows |
| aliases in menu rows | alternate names | **have** in spirit: fuzzy search |
| i18n (`omarchy-i18n`, `default/i18n`, 50 manual languages) | translated UI | **n/a** for now: omawin is English-only by choice; Windows' own UI is localized |

## 3. Capture

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `capture-screenshot`, `capture-region` (Print, window select with Tab/arrows) | screenshot a region, window or screen | **have**: `omawin capture screenshot` (Win+Shift+S) |
| `capture-text` (Super+Ctrl+Print) | OCR | **have**: `omawin capture text` (Win+Shift+T) |
| `capture-qr` | decode a QR | **have**: `omawin capture qr` |
| hyprpicker (Super+Print) | colour picker | **have**: `omawin capture color` |
| `capture-screenrecording`, `-with-webcam`, `-process` (Alt+Print), menu Screenrecord with/without audio | record the screen, desktop/mic audio, webcam overlay | **planned** (mission-todo only: track 5 "screen recording (Windows.Graphics.Capture or ffmpeg ddagrab)"; shell-apps "add recording") |
| `capture-webcam-resize`, `capture-webcam-list` | webcam overlay size, cameras | **planned** with recording (mission-todo only) |
| ScreenRecording bar indicator | red dot while recording | **planned** with recording |
| window capture with its shadow | | **planned**: roadmap 5.11 |
| annotation | | **planned** (mission-todo only: track 5) |
| `transcode`, `transcode-ascii` (Super+Ctrl+.) | shrink a picture or video for sharing; image to ASCII | **new** (transcode); ASCII **new**, not worth it |
| `ascii` | text as ASCII art in the logo's font | **new**, not worth it |

## 4. Toggles and indicators

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `toggle`, `toggle-enabled`, `toggle-input-device` | the toggle framework (flag files) | **have** in spirit: `%LOCALAPPDATA%\launcher\<flag>` files (stay-awake) |
| `toggle-idle` (Super+Ctrl+I), Stay Awake / Allow Sleep | stay awake | **have**: `omawin toggle stay-awake`; idle lock **planned** (5.6) |
| `toggle-nightlight`, `services/nightlight`, `restart-hyprsunset` | night light | **have** (opens Windows' setting: no API), `omawin toggle night-light` |
| `toggle-notification-silencing` (Super+Ctrl+Alt+,) | do not disturb | **have** (opens the setting), `omawin toggle dnd` |
| `toggle-screensaver`, `launch-screensaver` | screensaver | **planned**: 5.6 |
| `toggle-animations` | animations, blur, shadows | **planned**: 1.4 (`omawin toggle animations`) |
| `toggle-bar` (Super+Shift+Space) | hide/show the bar | **new** |
| `toggle-fullscreen-desktop` (Super+Ctrl+Alt+F) | no bar, no gaps | **new** |
| `toggle-crash-capture`, `crash-mute` | crash notifications | **new** (with crash watch, below) |
| `toggle-suspend` | hide Suspend | **n/a**: Windows' sleep is controlled by its own power settings |
| `toggle-touchpad`, `toggle-touchscreen`, `restart-trackpad` | input devices on/off | touchpad **planned** (1B: touchpad settings, index link to `ms-settings:devices-touchpad`); touchscreen **n/a** (disabling the device needs admin) |
| `toggle-hybrid-gpu` | integrated vs dedicated GPU (supergfxd) | **n/a**: Windows picks per app (Settings › Graphics) and the vendor's MUX tool switches |
| `toggle-theme-sync` | mirror theme to herdr machines | **new**, low value |
| bar indicators: DND, NightLight, Reminder, StayAwake, PasswordlessSudo, RemoteSession | | stay awake **have**; DND, night light, reminder **planned** (mission-todo track 4 "indicators", 5.5); passwordless sudo **n/a**; remote session **new**, low value |

## 5. Theme and look

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `theme-set`, `theme-list`, `theme-current`, `theme-switcher` (Super+Ctrl+T) | apply/pick a theme | **have**: `omawin theme set/list`, menu Style › Theme |
| `theme-install`, `theme-update`, `theme-remove`, `theme-extras`, `theme-dir` | git themes | **have**: `omawin theme install/update/remove/list` |
| `dev-theme-preview`, theme picker previews | preview before applying | **planned**: 1.9, 1.10 |
| `theme-bg-next` (Super+Ctrl+Space), `theme-bg-set`, `theme-bg-switcher`, `theme-bg-install`, `theme-bg-current`, `theme-bg-cache` | backgrounds | next **have** (`omawin theme next-background`); set any image **have** (Style › Wallpaper); grid switcher **planned** (mission-todo track 5, 1.9) |
| `theme-bg-intro`, `theme-bg-boot-intro`, video backgrounds (`plugins/background`) | animated intro, video wallpaper | **new**, not worth it (memory, 8K) |
| `theme-refresh`, `theme-set-templates` | re-render templates | **have**: `themes/render.ps1` (`omawin restart glazewm`) |
| `theme-set-vscode` | VS Code | **have** (`themes/vscode`, set-theme.ps1 "VS Code, Positron") |
| `theme-set-claude` (`default/themed/claude.json.tpl`) | Claude Code | **planned** (mission-todo only: track 7) |
| `theme-set-browser`, `theme-set-browser-policy`, `chromium.theme.tpl` | the browser's colour via policy | **new** |
| `theme-set-obsidian`, `theme-set-hermes`, `theme-set-pi`, `theme-set-t3code`, `theme-set-hunk`, `helix.toml.tpl`, `alacritty.toml.tpl`, `kitty.conf.tpl`, `restart-opencode`, `restart-helix` | more apps follow the theme | **new** (opencode, helix, alacritty, lazygit, Obsidian are offered by omawin and not themed) |
| `theme-set-tmux`, `theme-set-herdr-machines`, `restart-btop` | | btop **have**; herdr / tmux in Linux **planned** (3.8) |
| `theme-set-keyboard*` (`keyboard.rgb.tpl`, Asus ROG, Framework 16) | keyboard lights follow | **planned**: 8.9 |
| `theme-set-gnome`, `-fcitx5`, `-foot`, gtk | toolkit themes | **n/a**: Windows' light/dark is set (`AppsUseLightTheme`, **have**) |
| `theme-set-glass`, `hyprland-window-transparency`, `-transparency-toggle` (Super+Backspace) | opaque / transparent / glassy windows | **planned**: 5.7 (transparency toggle) |
| `hyprland-corners` | sharp / rounded corners | **planned**: 5.7 (corner style) |
| `hyprland-window-gaps-toggle` (Super+Shift+Backspace) | gaps on/off | **new** (`omawin toggle gaps`, with full-screen desktop) |
| `hyprland-window-single-square-aspect-toggle` | one window kept square | **new**, low value (fork) |
| `bar` (position, opaque/glassy/clear background, indicators, clock format), `refresh-shell`, `bar-text-color` | bar settings from the menu | bar layout **planned** (mission-todo track 4, 4.5 `bar.json` in the account folder); position/background rows **new** |
| `dock` (auto-hide, always-on, position), `plugins/dock` | dock | **planned** (mission-todo track 4 "then the dock"; manual says none today) |
| `branding-about`, `branding-screensaver`, `branding-about-animation` | your own About/screensaver text or image | About animation **have** (`AboutWindow.xaml.cs` ports it); custom branding **new**, not worth it |
| `display-text-size` | scale text everywhere | **planned** (mission-todo only: track 7 "Font and text size") |
| `plymouth-*`, `refresh-plymouth`, `refresh-sddm` | boot splash, login screen | **n/a**: Windows' boot logo is the firmware's; the lock screen background is **have** (`omawin theme lockscreen`) |

## 6. Fonts

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `font-set`, `font-list`, `font-current`, Appearance › Font | one monospace font everywhere | **planned** (mission-todo only: track 6 "a font switcher that renders into every app's config") |
| `install-font`, Install › Style › Font (Cascadia, Meslo, Fira, Victor, Bitstream, Iosevka) | install a Nerd Font and switch | **planned** with the switcher (mission-todo only); per-user font install needs no admin (`%LOCALAPPDATA%\Microsoft\Windows\Fonts` + HKCU) |
| `font-cjk` | CJK glyph forms | **n/a**: Windows' language packs choose CJK fonts |
| `font-set` hook | scripts after a font change | **new** (hooks, below) |

## 7. Setup, input and hardware

| Omarchy feature | What it does | omawin status |
|---|---|---|
| Setup › Monitors, `hyprland-monitor-scaling` (Super+/) | monitor layout, scaling up/down | layout **have** (link); scaling keys **new** |
| `hyprland-monitor-internal` (Super+Ctrl+Delete), `-internal-mirror`, `hw-recover-internal-monitor`, clamshell | laptop screen off, mirror to external | **new** (`omawin display`) |
| `hyprland-monitor-watch`, `monitor-state` | react to monitor changes | **planned**: 8.7 monitor profiles |
| `brightness-display`, `-ddc`, `-apple` | brightness, DDC/CI | **have**: `omawin brightness` (WMI + DDC/CI) |
| `brightness-keyboard*` | keyboard backlight | **n/a**: vendor-specific on Windows (Dynamic Lighting is 8.9) |
| `setup-keyboard`, `setup-input`, `input-method` (Super+I), `restart-xcompose`, XCompose | layouts, IMEs, compose | settings **planned** (1B.1 index); the cycle key **new**; compose **n/a** (WinCompose exists, a winget row at most) |
| `language`, `locale-generate`, `menu-timezone`, `update-time` | language, locale, time zone, time sync | **planned**: 1B.1 (Windows' settings index links); time sync **n/a** (Windows syncs itself) |
| Setup › Input (mouse, keyboard repeat, touchpad) | | **planned**: 1B catalog (`input.*`) |
| `dns` (DHCP, Cloudflare, Google, custom) | DNS presets | **planned**: 8.19 |
| `network-qr`, `network-password`, `panels/wifiqr` | Wi-Fi as a QR | **planned**: 8.21 |
| `network-band`, `network-captive-portal` | Wi-Fi band, captive portal sign-in | **n/a**: band is a driver property (admin); Windows' NCSI opens captive portals itself |
| `restart-wifi`, `restart-bluetooth` | recover radios | **have** in effect: `omawin wifi off/on`, `omawin bluetooth off/on` |
| `restart-audio`, `audio-tuning`, `hw-intel-sof` | recover audio, speaker DSP | **n/a**: restarting Audiosrv needs admin; tuning is Linux driver work |
| `powerprofiles-set`, `-list`, `-init` | power profile per AC/battery | **have**: `omawin power` (Windows keeps a mode per power source) |
| `hibernation-setup`, `-remove`, `-available`, System › Hibernate | hibernate | **planned**: 1B catalog `system.hibernate` (admin); the System › Hibernate row **new** (S, with 5.8's condition) |
| `hw-*` (40 detection helpers), `apply-hardware`, `provision-hardware`, `install/hardware/*`, `n1x-probe` | hardware quirks | **n/a**: Windows' drivers; detection omawin needs is 5.8's conditions |
| `kernel`, `refresh-limine`, `setup-direct-boot`, `boot-direct`, `update-boot` | bootloader | **n/a** |
| `drive-info`, `drive-select`, `drive-password` | drives, LUKS password | **n/a**: Disk Management / BitLocker (index links, 1B.1) |
| `system-factory-reset` | reset the machine | **n/a**: Windows' Reset this PC (index link) |
| `windows-key` | OEM Windows key from firmware | **new**, low value |
| `windows-vm` (Install/Remove › Windows) | a Windows VM on Linux | **n/a**; the reverse is roadmap 7.5 (Try Omarchy for Windows) |

## 8. Install and remove

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `pkg-install`, `pkg-remove`, `pkg-add`, `pkg-drop`, `pkg-present/missing` | fuzzy package install/remove | **have**: `omawin pkg install/remove/find`; **planned**: 2B |
| `pkg-aur-*`, Install › AUR | AUR | **n/a**: decision "no AUR" |
| `install-dev-env`, `remove-dev-env`, `install-docker-dbs` | dev environments, Docker databases | **have**: `omawin dev install/remove` |
| `install-ai-*` (ChatGPT, Claude, Hermes, OpenClaw, T3 Code), `remove-ai-*` (+ Ollama, LM Studio, Perplexity, Grok Bot) | AI apps | ChatGPT, Claude, Ollama, LM Studio **have** (Install › AI, Remove › Package); Hermes, OpenClaw, T3 Code **new**, low value (winget rows if packaged) |
| `install-editor-*` (VS Code, Zed, Helix, Emacs) | editors, themed | **have** (VS Code, Cursor, Zed, Helix, Sublime, Neovide); theming beyond VS Code **new** (§5) |
| `install-terminal` (Alacritty, Foot, Ghostty, Kitty) | terminals, set as default | **have**: noctty, Windows Terminal, Alacritty |
| `install-browser`, `remove-browser` (Chrome, Edge, Brave, Brave Origin, Firefox, Zen) | browsers | **have** (Edge is Windows') |
| `install-service-*` (1Password, Dropbox, Signal, Spotify, Tailscale, Microsoft, NordVPN, ONCE, Sunshine) | services | **have** most; Microsoft web apps **planned** (5.3); NordVPN, Sunshine **new**, trivial rows; ONCE **n/a** (a Linux server; runs in Arch if wanted) |
| `install-gaming-*` (Steam, Heroic, Lutris, Battle.net, RetroArch, GeForce NOW, Xbox Cloud, Xbox controllers, lib32), `games-retro-*`, Minecraft | games | Steam **have**; Heroic, Battle.net, GeForce NOW, RetroArch, Minecraft **new**, trivial rows; Lutris, lib32, Xbox controllers **n/a** (Windows runs Windows games; controllers are native) |
| `install-dictation-*`, `remove-dictation-*`, `default-dictation`, `dictation*`, `voxtype-*` | dictation backends | **have**: Handy (Win+H, dictation preset); default picker **planned** (mission-todo track 6) |
| `install-chromium-*` (Claude extension, Copy URL, yt-dlp host, Google account) | Chromium fixes | **n/a**: Chrome on Windows has the Web Store and Google sign-in |
| `install-camera`, `install-video-decode` | Snapdragon fixes | **n/a** |
| `install-preinstalls`, `remove-preinstalls` | Omarchy's default apps | **n/a**; Windows' own extra apps are **planned** (8.50) |
| `refresh-applications`, `remove-launcher-entry` | .desktop files | **n/a**: the launcher reads Start's apps |
| `mise-install`, `mise-default-tools`, `update-mise` | mise wrappers | **planned**: 2.3 |

## 9. Update, reset and versions

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `update`, `update-confirm`, `update-system-pkgs`, `update-status`, `update-lock` | update everything | **have**: `omawin update`, Update › All apps |
| `update-available`, SystemUpdate bar widget | update badge | **have**: `bin/update-available.ps1`, the bar's arrow |
| `channel-set`, `channel-current`, `version-channel`, `branch`, Update › Channel | stable / rc / edge / dev | **planned**: 5.9 |
| `version`, `version-pkgs`, `version-branch` | print the version | **new** (`omawin version`, folded into `omawin debug`) |
| `migrate`, `migrate-notify`, `dev-add-migration` | migrations | **have**: `omawin migrate`; the scaffold **new** (small) |
| `update-restart` | reboot / restart services after an update | **planned**: 8.18 (restart needed, from the Update Agent) |
| `update-firmware` (fwupd) | firmware | **n/a**: Windows Update and the vendor's tool; driver policy is 8.43 |
| `update-orphan-pkgs`, `update-pkg-prune`, `update-keyring`, `update-aur-pkgs`, `update-pacman*`, `refresh-pacman`, `update-requires-free-space` | pacman upkeep | **n/a** (pacman); caches are **planned** in 8.3 |
| `update-analyze-logs` | known failure patterns in the update log | **new**, low value until updates have logs (2.0's queue) |
| `update-stay-awake` | keep awake during an update | **new**, tiny (call `StayAwake` from `bin/update.ps1`) |
| `update-dev`, `dev-link`, `dev-unlink`, `dev-status`, `dev-pkg-test`, `upgrade-to-quattro` | dev checkouts, package layout | **n/a**: omawin runs from its git checkout |
| `refresh-config`, `refresh-hyprland`, `refresh-tmux`, `refresh-herdr`, `refresh-hyprsunset`, `refresh-chromium`, Update › Config | reset one config to the default, backing yours up | **new** (`omawin reset`) |
| `reinstall`, `reinstall-configs`, `reinstall-pkgs` | reset all configs, reinstall packages | packages **have** (`omawin install`); configs **new** (`omawin reset --all`) |
| `snapshot` (snapper), `install/config/snapper.sh` | system snapshots, restore | **planned**: 8.42 (restore point inside an approved admin batch), 5.9 |
| Update › Password (drive, user) | change passwords | **n/a**: BitLocker / sign-in options (index links) |

## 10. Developer tools and shell

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `default/bash` aliases and functions (`ga`/`gd` worktrees, `compress`, `rsw` rsync watchers, `fip`/`dip` SSH port forwards, `tdl`/`hdl` dev layouts, `iso2sd`) | shell helpers | Linux **planned** (3.2 sources `default/bash`); PowerShell has a few (`ls`, `lg`, `gacp`); the worktree and layout helpers in PowerShell **new** (low; Linux is the default shell, 3.4) |
| `default/bash/fns/herdr` (`hdl`, `hds`) | herdr dev layouts | **have**: `omawin launch project` (`launcher/project-tabs.txt`) |
| `setup-git` | name, email, `gh auth login` | identity **have** (install asks); GitHub sign-in **new** (`omawin setup git`) |
| `setup-security-ssh-agent`, `remove-service-ssh-agent` | an SSH agent | **planned**: 8.39, 3.9 |
| `setup-security-sshd`, `remove-security-sshd` | OpenSSH server, firewall, a key | **new** (opt-in admin) |
| `setup-security-sudoless-docker`, `sudo-docker` | docker without sudo | **n/a** on Windows; Linux **planned** (3.6) |
| `sudo-passwordless`, `sudo-keepalive`, polkit (`plugins/polkit`) | privilege prompts | **n/a**: UAC; Windows' `sudo` is **planned** (1B `developer.sudo`); one admin prompt is 2.9 |
| `hook`, `hook-install`, `~/.config/omarchy/hooks/<event>.d/` (post-update, theme-set, font-set, pre-refresh-pacman) | your scripts on omarchy's events | **new** |
| `debug`, `debug-idle`, `upload-log` | diagnostics for a bug report | **new** (`omawin debug`) |
| `dev-benchmark-cli`, `dev-benchmark-theme-switcher` | timing | **planned**: 1.1 (`omawin profile menu`), 1.9; **have**: `omawin profile terminal/login/ram` |
| `dev-ui-preview`, `plugins/dev-gallery` | the UI kit on one page | **new**, low value |
| `dev-font`, `dev-install-ydoo`, `dev-notification-demo` | dev chores | **n/a** |
| `disk-speedtest`, `panels/disk-speedtest` | disk read/write speed | **new**, low value |
| `network-speedtest`, `panels/speedtest` | internet speed dials | **have** as a link (Trigger › Speed test, fast.com) |
| `system-stats` | CPU/RAM for the bar | **planned**: 5.11 |
| Docker databases, dev envs | | **have** (§8) |

## 11. Shell plugins (`shell/plugins/`)

| Omarchy plugin | What it does | omawin status |
|---|---|---|
| `bar` (+ widgets ActiveWindow, Indicators, KeyboardLayout, Microphone, Spacer, SystemUpdate, Tray, Workspaces) | the bar | **have**: native bar (`menu-app/Bar`); keyboard layout, mic **planned** (mission-todo track 4, 8.11); active window title **planned** (3B "the bar's window title") |
| `menu` | the menu + bar button | **have** |
| `clipboard` | history overlay | **have** (Win+Shift+V); images and secrets **planned** (8.14) |
| `emojis` (Super+Ctrl+E) | emoji search, type | **planned**: shell-apps "emoji search in the launcher", mission-todo track 5 (Win+. as the fallback) |
| `image-picker` | image grid for themes/backgrounds | **planned**: 1.9, one-search "themes as a grid" |
| `notifications` (grouped stacks, actions, DND, history; Super+, keys) | notification daemon | **n/a** as a daemon (Action Center); history **planned** (8.22); keyboard dismissal **n/a** (Win+N) |
| `osd` | volume/brightness OSD | **planned**: 5.5 |
| `lock` | lock screen with password/fingerprint | **n/a**: Windows' lock and Hello; background **have** |
| `polkit` | auth dialog | **n/a**: UAC's secure desktop can't be themed |
| `background` | wallpaper renderer, transitions | **have** (wallpaper); per monitor **planned** (8.8) |
| `dock` | app dock | **planned** (mission-todo track 4) |
| `reminders` (`reminder`, Super+Ctrl+R) | set/show/clear reminders | **planned**: 5.5 (`omawin remind`) |
| `agents` (+ `agent-usage-*` for 27 providers) | usage, limits, pace, balances | **have** for Claude (`bin/agent-usage.ps1`, bar robot, Zebar's panel for now); other providers **new** |
| `dev-gallery` | UI kit preview | **new**, low value |
| `panels/audio` | volume, output, per-app mixer | **have** (Win+A); per-app volume **planned** (8.10) |
| `panels/bluetooth`, `panels/network`, `panels/monitor`, `panels/power` | quick panels | **have**: Quick settings (`omawin launcher quick`); laptop display controls **new** (§7) |
| `panels/clock` (calendar popup) | | **planned** (mission-todo track 4, shell-apps "Calendar flyout", plugins.md Calendar example) |
| `panels/weather`, `weather-*`, `notification-weather` | weather pill and popup | **planned** (mission-todo track 4, shell-apps "Weather") |
| `panels/elsewhen` (Super+Ctrl+Alt+E) | world clock | **new** |
| `panels/tailscale`, `tailscale-send`, `tailscale-receive`, `installed-service-tailscale` | tailnet status, exit nodes, Taildrop | **new** |
| `panels/dropbox` | Dropbox status | **n/a**: Dropbox's own tray icon does it |
| `panels/speedtest`, `panels/disk-speedtest` | | §10 |
| `services/battery` | low battery warning | **planned**: 5.5 |
| `services/idle` | screensaver, lock, display wake | **planned**: 5.6 |
| `services/media` (MPRIS) | media keys, sources | **have**: Windows' media session + the music player |
| `services/nightlight` | | **have** (§4) |
| `services/remote-session` | indicator while a remote session is in | **new**, low value |
| `plugin-add/clone/enable/disable/list/remove/update/validate`, `menu-plugin`, `plugin-catalog`, `shell` (IPC) | plugin management | **planned**: phase 4 (4.1-4.9; `omawin plugin`, `omawin shell`); `validate` **new** as a 4.5 verb |

## 12. Notifications and notices

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `notification-send`, `notification-wait`, `notification-dismiss` | send toasts from scripts | **planned**: 5.5 (`omawin notify`) |
| `notification-time` (Super+Ctrl+Alt+T), `notification-battery` (…+B) | hotkey notices | **new**, low value (the bar shows both) |
| `crash-watch`, `agent-crash`, `toggle-crash-capture`, `crash-mute`, `default/agents/skills/diagnose-crash` | a crash toast that offers an AI diagnosis | **new** (crash explanations are **planned** in 8.15; the watch and the agent hand-off aren't) |
| `battery-low` | | **planned**: 5.5 |
| first-run notices (`install/user/first-run/welcome.sh`, `wifi.sh`, `setup-agent.hook`) | first-login nudges | **planned**: install-speed F (the welcome page) |

## 13. Lock, idle and power

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `system-lock` (Super+Ctrl+L) | lock and display off | **have**: System › Lock (Win+L is Windows') |
| `system` (logout, reboot, shutdown after closing apps), System menu, power key | power menu | **have**: System rows; closing apps first **new** (with close-all) |
| `system-sleep-*`, `system-lid-*`, `sleep-disabled`, `system-wake` | lid and sleep handling | **planned**: 1B catalog (`system.lid`, `system.sleep`), 8.30 |
| `toggle-idle`, `debug-idle` | | **planned**: 5.6 |
| `launch-screensaver` | | **planned**: 5.6 |

## 14. Window management keys (`default/hypr/bindings/tiling.lua`)

| Omarchy key | What it does | omawin status |
|---|---|---|
| Super+W / Super+Q close | close window | **have**: Alt+Shift+Q |
| Ctrl+Alt+Delete (`hyprland-window-close-all`) | close every window | **new** (Ctrl+Alt+Delete is Windows' secure attention key: a command, a key only if the maintainer picks one) |
| Super+J toggle split | | **have**: Alt+V |
| Super+P pseudo | | **n/a**: dwindle-only concept |
| Super+T float, Super+F fullscreen, Super+Alt+F full width | | **have**: Alt+Shift+Space, Alt+F; maximized has no key |
| Super+Ctrl+F tiled fullscreen | fullscreen inside its tile | **new**, low value |
| Super+O pop out (`hyprland-window-pop`) | float, size, pin on top | **planned**: 5.7 |
| Super+Home / Super+Alt+Home (`hyprland-window-width`) | save/restore width | **planned** (mission-todo track 3 "restore sizes") |
| Super+L layout cycle (`hyprland-workspace-layout-*`: dwindle, scrolling, floating) | per-workspace layout | **planned**: 5.7 layout cycling; a scrolling layout itself isn't in the plan (watch upstream) |
| Super+arrows focus, Super+Shift+arrows swap, Super+1-9, Super+Shift(+Alt)+1-9 | | **have** (Win+arrows, Win+Shift+arrows, Win+1-9, Win+Shift(+Alt)+1-9) |
| Super+S / Super+Alt+S scratchpad | | **planned**: 5.7 (plan 06 `toggle_scratchpad.rs`) |
| Super+` / Super+Shift+` Quake console | drop-down console workspace | **new** |
| Super+M minimize to the dock (`hyprland-window-minimize`) | | **have**: Alt+M (`toggle-minimized`); the dock is planned |
| Super+Tab / Shift+Tab / Ctrl+Tab | next, previous, former workspace | **have**: Alt+S, Alt+A, Alt+D |
| Super+Shift+Alt+arrows, Super+Shift+Ctrl+arrows | workspace / window to another monitor | **have**: `move-workspace` (Alt+Shift+A/F/D/S); window across monitors by `move --direction` |
| Alt+Tab cycle, Ctrl+Alt+Tab next monitor | | Alt+Tab is Windows'; the switcher is **planned** (8.5) |
| Super+-/= resize (three steps), mouse move/resize | | **have**: Alt+U/P/O/I, resize mode (Alt+R), GlazeWM's mouse drag |
| Super+G groups (and 12 group keys) | tabbed groups | **planned**: 5.7 (L) |
| Super+mouse wheel workspaces | | **have** in the bar (wheel on workspaces) |
| Super+Ctrl+Z zoom | screen zoom | **n/a**: Windows Magnifier (Win+Plus) |
| Super+C/V/X/A universal clipboard | copy in terminals too | **n/a**: Windows' terminals take Ctrl+C/V themselves |
| Super+Ctrl+A/B/D/N/P, Super+Ctrl+Alt+D/E | open a panel by key | Audio **have** (Win+A); the others are Quick settings **have** without keys |
| `hyprland-reload-guard`, Alt+Shift+R | reload | **have**: `omawin restart glazewm` |
| touchpad gestures | | **planned** (mission-todo track 3) |

## 15. Media and audio keys (`bindings/media.lua`)

| Omarchy feature | What it does | omawin status |
|---|---|---|
| volume, mute, play/pause/next/prev keys, precise steps | | **have**: Windows handles the media keys; the music player listens |
| `audio-input-mute` (XF86AudioMicMute, LED) | mic mute | **planned**: 8.11 |
| `audio-output-switch` (Shift+Mute) | next output, mute kept | **new** (`omawin audio next-output`) |
| `audio-source-switch` | move playback between media sources | **n/a**: Windows' media flyout picks the session |
| `audio-input/output-set-default` | default devices, streams moved | **have**: the Audio page picks defaults |
| `audio-output-volume` + OSD | | **planned**: 5.5 |
| `audio-output-sink`, `audio-sink-availability` | | **n/a** (PulseAudio internals) |
| F9 / Right Alt hold push-to-talk, Super+Ctrl+X dictation | | **have**: Win+H (Handy) |

## 16. Web apps, TUIs and GUIs (`manual/21-tuis.md`, `22-guis.md`, `25-web-apps.md`)

| Omarchy feature | What it does | omawin status |
|---|---|---|
| web apps: HEY, Basecamp, ChatGPT, Grok, WhatsApp, Google apps, X, YouTube, Zoom, Discord, Microsoft 365 | | **planned**: 5.3 ("Omarchy's web app list as the starting set") |
| TUIs: lazygit, btop, herdr, fastfetch, cliamp | | lazygit, herdr, fastfetch **have**; btop **have** as an install row; cliamp **n/a** (omawin has its own player) |
| Disktree | treemap of what fills the disk | **planned**: 8.3 (Disk page) |
| Omawrite, Hype, Signed, Omacut, Pinta, Aether | Basecamp's small apps, image edit, theme maker | **n/a**: Linux apps (Signed's PDF filling: Edge does it; Aether's idea is **new**, low) |
| LocalSend + Share menu (`menu-share`, Super+Ctrl+S) | send clipboard, files, folders to nearby devices | **new** |
| Obsidian, Signal, Spotify, 1Password, mpv, LibreOffice | | **have** as Install rows (LibreOffice, mpv in presets/winget) |

## 17. Agents and AI (`manual/17-ai.md`)

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `agent` (Super+Shift+Ctrl+A, `--pick`), `default-agent` | default coding agent | **have**: `omawin default agent`, `omawin agent open/ask/listen` |
| `agent-prompt` | agent with a prompt | **have**: `omawin agent ask` |
| `agent-account-add/list/use/rename/remove/mode/copy` (+ hidden `-exec`, `-home`, `-state`, `-transfer`) | several Claude/Codex/Grok subscriptions, auto-switch near a limit | **new** |
| `agent-usage-update`, `agent-usage-*` | usage for 27 providers | Claude **have**; Codex, Copilot, Cursor, OpenRouter, … **new** |
| `default/agents/skills/omarchy`, `omarchy-app` | skills that teach agents the system | **have** in part: `claude sync`, `docs/agent.md`; an omawin skill for any agent **new**, small (part of crash watch) |
| `install-hermes-cli`, `install-openclaw-cli`, `openclaw-onboard`, `launch-openclaw` | agent platforms | **new**, low value |
| agents fenced in | | **planned**: 7.3 |

## 18. Backup, sync and sharing

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `agent-account-copy` | copy agent logins to another machine over SSH | **new** (with agent accounts) |
| `toggle-theme-sync`, `theme-set-herdr-machines` | theme to remote machines | **new**, low |
| `tailscale-send/receive` | Taildrop | **new** (§11) |
| dotfiles (`manual/31-dotfiles.md`): autostart, hooks, own menu entries, own aliases | personal layer | **have**: the account folder (docs/work.md), layered `items.txt`; menu layering **planned** (4.4); hooks **new** |
| settings follow you | | **planned**: 1B.5 (`omawin settings save/apply`), `omawin shared backup` **have** |

## 19. Misc

| Omarchy feature | What it does | omawin status |
|---|---|---|
| `show-done`, `show-logo`, `cmd-*`, `state`, `git-url-check`, `i18n`, `lifecycle-dispatch`, `provision-*`, `apply-*`, `realtime-repair`, `security-functions`, `restart-gum` | internal helpers | **n/a** (omawin has its own internals) |
| `file-select` (portal chooser) | | **new** as `omawin pick file` |
| `regions`, `docs/regions.md` | region defaults (weather units, formats) | **n/a**: Windows' region settings |
| `manual/27-filling-out-pdfs.md` | fill and sign PDFs | **n/a**: Edge fills and signs PDFs |
| `manual/48-security.md` (firewall closed but LocalSend) | firewall | **planned** in part: 8.38 dev-server firewall rules |
| `manual/44-mac-support.md`, `49-omarchy-on.md`, `50-dual-boot`, `51-unattended-installs` | other hardware, dual boot, unattended install | **n/a** except unattended install: **planned** in effect (bootstrap one line, install-speed F "no questions") |

---

## In mission-todo but not yet in the roadmap

These are Omarchy features the plan knows about only as a mission-todo line, with no roadmap item, plan section, size or files. Each deserves a roadmap item; they're not repeated in the next section.

| Omarchy | mission-todo | Suggested roadmap home |
|---|---|---|
| `capture-screenrecording` (+ audio, webcam, indicator) | track 5 | phase 5 (5.12): Windows.Graphics.Capture + Media Foundation's `MediaTranscoder` in the snip overlay (no ffmpeg needed), or `ffmpeg -f ddagrab` from the `music` preset's ffmpeg; the bar's red dot; `omawin capture record [--audio] [--mic]` |
| annotation | track 5 | with recording: pen, arrow, box in `Snip.cs` before copy |
| `font-set`, `install-font`, Appearance › Font | track 6 | phase 5: `themes/render.ps1` gains `{{ font }}` (noctty, WT, GlazeWM's bar, VS Code, the menu app), per-user font install from Nerd Fonts' releases (`%LOCALAPPDATA%\Microsoft\Windows\Fonts`, HKCU) |
| `display-text-size` | track 7 | with fonts: `HKCU\Software\Microsoft\Accessibility\TextScaleFactor` plus the terminals' font size |
| `theme-set-claude` | track 7 | with the theme reaching more apps (N5 below) |
| `default-browser`, `default-dictation` | track 6 | `omawin default browser` (Windows forbids setting the default silently: opens Default apps on the browser's page) |
| weather, calendar popup, keyboard layout, mic, indicators | track 4 | phase 4.6 as first-party plugins |
| `emojis` | track 5 | One Search source (1C) |
| touchpad gestures | track 3 | 5.7 |

---

## New for the roadmap

Every **new** item worth doing, in order of the ranking below and then by area. Home is one of roadmap 8.0's: **core** (menu app or `omawin.exe`), **settings catalog**, **first-party plugin**, **script** (a `bin/*.ps1` row in `bin/commands.txt`), or **Linux side**. Every one is an `omawin` command; none binds a key without the maintainer; none needs admin unless marked opt-in.

### N1. A drop-down console (Omarchy's Quake console) (M)

- **Omarchy:** `default/hypr/bindings/tiling.lua`: Super+` toggles the special workspace `qconsole`, Super+Shift+` sends a window there.
- **Why on Windows:** the one-key terminal that drops over whatever you're doing and goes away again is the most-used thing a developer could get from a scratchpad; Win+Enter opens a new terminal each time.
- **How:** the scratchpad's machinery (roadmap 5.7, plan 06 `toggle_scratchpad.rs`) generalized to **named** special workspaces (`scratchpad`, `console`), then `omawin console`: if the console workspace has a terminal, toggle it; else start `omawin launch terminal --title omawin-console` and move it there (a window rule on that title). Shown floating at the top, full width, 40% height. Until the fork lands: Windows Terminal's own quake mode (`wt -w _quake`) when the default terminal is `wt`. Home: core (`omawin.cs` builtin) + fork.
- **Key:** proposed Win+` (confirm first; an open question).

```csharp
// bin/omawin.cs (C# 5): `omawin console`, a drop-down terminal. GlazeWM's fork shows and hides
// the named special workspace; the first call starts the terminal that lives there.
static int DropConsole(string[] args)
{
    string ws = "console";
    if (!ForkCan("special-workspaces"))           // the fork's capability file (plan 01, 1.2)
    {
        if (DefaultTerminal() == "wt") return Run("wt.exe", "-w _quake");
        Console.Error.WriteLine("The console needs omawin's GlazeWM (scratchpad workspaces).");
        return 1;
    }
    if (GlazeQueryHasWindowTitled("omawin-console"))
        return GlazeCommand("toggle-scratchpad --name " + ws);
    LaunchTerminal(new[] { "--title", "omawin-console" });  // the window rule moves it to "console"
    return GlazeCommand("toggle-scratchpad --name " + ws);
}
```

```yaml
# glazewm/config.yaml (template): the console's window, floating along the top.
window_rules:
  - commands: ['move --workspace console', 'set-floating --x 0 --y 0 --width 100% --height 40%']
    match:
      - window_title: { equals: 'omawin-console' }
```

### N2. Hooks: your scripts on omawin's events (S)

- **Omarchy:** `bin/omarchy-hook`, `bin/omarchy-hook-install`, `~/.config/omarchy/hooks/<event>.d/` (`post-update`, `theme-set`, `font-set`, `pre-refresh-pacman`), `manual/31-dotfiles.md`.
- **Why on Windows:** everyone who customizes omawin ends up wanting "after a theme switch, also do X" (a third-party app's colours, a work tool) or "after an update, run my setup". Today that means editing the repo, which `omawin update` stashes.
- **How:** `~/omawin-account/hooks/<event>.d/*` (and the shared folder's), run in name order, each with the event's argument, hidden, with a 30 s timeout, errors logged to `%LOCALAPPDATA%\launcher\hooks.log` and never failing the caller. Events: `post-install`, `post-update`, `theme-set <name>`, `font-set <name>` (with fonts), `post-login` (after the sign-in task). `omawin hook <event> [args]` runs one by hand; `omawin hook install <event> <file>` copies a script in. `.ps1` runs in pwsh, `.sh` in Linux (through `wsl.exe`), anything else directly. Callers: `themes/set-theme.ps1` (end), `bin/update.ps1` (end), `install.ps1` (end), `install/login-start.ps1`. Home: core (`omawin.cs`, so it costs no PowerShell when there are no hooks).

```csharp
// bin/omawin.cs (C# 5): `omawin hook <event> [args]`. Runs every file in the shared folder's and
// this account's hooks/<event>.d, in name order. Never fails its caller: a hook's error is logged.
static int Hook(string[] args)
{
    if (args.Length == 0) { Console.Error.WriteLine("omawin hook <event> [args]"); return 2; }
    string evt = args[0];
    string rest = string.Join(" ", args.Skip(1).Select(Quote).ToArray());
    var files = new List<string>();
    foreach (string root in new[] { SharedFolder(), UserLayer() })
    {
        string dir = Path.Combine(root, "hooks", evt + ".d");
        if (Directory.Exists(dir))
            files.AddRange(Directory.GetFiles(dir).Where(f => !f.EndsWith(".sample")).OrderBy(f => Path.GetFileName(f)));
    }
    foreach (string f in files)
    {
        string ext = Path.GetExtension(f).ToLowerInvariant();
        var psi = ext == ".ps1" ? new ProcessStartInfo("pwsh.exe", "-NoLogo -NoProfile -File " + Quote(f) + " " + rest)
                : ext == ".sh"  ? new ProcessStartInfo("wsl.exe", "-- bash " + Quote(WslPath(f)) + " " + rest)
                :                 new ProcessStartInfo(f, rest);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        try
        {
            using (var p = Process.Start(psi))
                if (!p.WaitForExit(30000)) { p.Kill(); Log("hooks.log", f + ": killed after 30 s"); }
                else if (p.ExitCode != 0) Log("hooks.log", f + ": exit " + p.ExitCode);
        }
        catch (Exception e) { Log("hooks.log", f + ": " + e.Message); }
    }
    return 0;
}
```

```powershell
# themes/set-theme.ps1, last lines: your hooks after a theme switch (omawin hook, docs/work.md).
& "$HOME\.local\bin\omawin.exe" hook theme-set $Name
```

### N3. Crash watch with an agent's diagnosis (M)

- **Omarchy:** `bin/omarchy-crash-watch` (hidden; follows systemd-coredump), `bin/omarchy-agent-crash`, `bin/omarchy-toggle-crash-capture`, `bin/omarchy-crash-mute`, `default/agents/skills/diagnose-crash/SKILL.md`.
- **Why on Windows:** programs die silently on Windows (a dev server's runtime, an editor, the bar itself); Windows Error Reporting records it (Application Error, event 1000; .NET Runtime 1026; Windows Error Reporting 1001 with the report folder) and tells nobody. A toast "node.exe crashed. Diagnose?" that opens the default agent with the faulting module, exception code and the WER report is a real time-saver.
- **How:** the event log reader roadmap 8.15 already plans (shared library) gains a live `EventLogWatcher` on `Application` for those IDs; deduped per program per 60 s; muted programs in `%LOCALAPPDATA%\launcher\crash-mute.txt`; on/off with `omawin toggle crash-capture` (on by default, as Omarchy). The toast's button runs `omawin agent crash <record-id>`: a script collects the event, the report folder (`%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive\…\Report.wer`), the program's version, and opens the default agent in a terminal with a prompt and an omawin `diagnose-crash` skill (`claude/skills/`). No admin: the Application log and the per-user WER folders are readable. Home: first-party plugin (service), started at login, on the 8.15 library; toasts through 5.5.

```csharp
// menu-app/Plugins/CrashWatch (first-party plugin, .NET 9 AOT): a toast when a program crashes.
// (check: System.Diagnostics.EventLog's EventLogWatcher under Native AOT; else poll EvtQuery
// every 10 s with a bookmark, as the Health page reads it.)
var query = new EventLogQuery("Application", PathType.LogName,
    "*[System[(EventID=1000 or EventID=1026) and Provider[@Name='Application Error' or @Name='.NET Runtime']]]");
using var watcher = new EventLogWatcher(query);
watcher.EventRecordWritten += (_, e) =>
{
    if (e.EventRecord is not { } r) return;
    string app = r.Properties.Count > 0 ? r.Properties[0].Value?.ToString() ?? "a program" : "a program";
    if (CrashMute.Contains(app) || Recently(app, TimeSpan.FromSeconds(60))) return;
    Notify.Show($"{app} crashed", "Diagnose it with your agent?",
        action: ("Diagnose", $"omawin agent crash {r.RecordId}"),
        second: ("Mute", $"omawin crash mute {app}"));
};
watcher.Enabled = true;
```

### N4. `omawin debug`: everything a bug report needs (S)

- **Omarchy:** `bin/omarchy-debug` (`--print`, `--no-sudo`), `bin/omarchy-upload-log`, `bin/omarchy-debug-idle`, `bin/omarchy-version*`.
- **Why on Windows:** omawin is public now; a stranger's "the bar is blank" needs the versions, `omawin check`'s output and the logs, and today that's a back-and-forth.
- **How:** `bin/debug.ps1`: `omawin about` as text, `omawin version` (repo commit and tag, menu app, GlazeWM fork, noctty, Windows build, PowerShell), `omawin check`, the last 200 lines of each log in `%LOCALAPPDATA%\launcher\` and `%LOCALAPPDATA%\omawin\`, GlazeWM's rendered config, the WSL pick and `wsl -l -v`; **redacted** (user name, home path, account folder name, Wi-Fi names) before it's written to `Downloads\omawin-debug-<time>.txt`; `-Print` shows it; `-Gist` uploads it as a secret gist through `gh` only when asked. Nothing from the account folder's contents (docs/work.md: private). `omawin version` is the first part on its own. Home: script.

```powershell
# bin/debug.ps1: what a bug report needs, redacted, in one file (omawin debug [-Print] [-Gist]).
param([switch]$Print, [switch]$Gist)
$out = [System.Text.StringBuilder]::new()
function Section($title, [scriptblock]$body) {
    [void]$out.AppendLine("== $title")
    try { [void]$out.AppendLine((& $body | Out-String).TrimEnd()) } catch { [void]$out.AppendLine("(failed: $_)") }
    [void]$out.AppendLine()
}
Section 'version' { & omawin.exe version }
Section 'check'   { & pwsh -NoProfile -File "$HOME\omawin\check.ps1" -Plain }
Section 'wsl'     { wsl.exe -l -v 2>&1 }
foreach ($log in Get-ChildItem "$env:LOCALAPPDATA\launcher\*.log", "$env:LOCALAPPDATA\omawin\*.log" -ErrorAction SilentlyContinue) {
    Section "log $($log.Name)" { Get-Content $log -Tail 200 }
}
# Redact what identifies the person or the PC.
$text = $out.ToString().Replace($HOME, '~').Replace($env:USERNAME, '<user>').Replace($env:COMPUTERNAME, '<pc>')
$text = $text -replace '(?m)(SSID\s*:\s*).*', '$1<wifi>'
$file = Join-Path ([Environment]::GetFolderPath('UserProfile')) "Downloads\omawin-debug-$(Get-Date -Format yyyyMMdd-HHmm).txt"
Set-Content $file $text -Encoding utf8
if ($Print) { $text } else { "Written to $file (read it before you share it)." }
if ($Gist) { gh gist create $file --desc 'omawin debug' }
```

### N5. The theme reaches every app omawin offers (M)

- **Omarchy:** `bin/omarchy-theme-set-browser`, `-browser-policy`, `-claude`, `-obsidian`, `-hermes`, `-pi`, `-t3code`, `-hunk`; templates in `default/themed/` (`chromium.theme.tpl`, `claude.json.tpl`, `helix.toml.tpl`, `alacritty.toml.tpl`, `obsidian.css.tpl`); `restart-opencode`, `restart-helix`.
- **Why on Windows:** omawin's identity is "one theme, every app", but the browser, Claude Code, OpenCode (omawin's own agent backend), lazygit, Helix and Alacritty (all offered in Install) keep their own colours.
- **How:** new sections in `themes/set-theme.ps1`, each skipped when the app isn't there, templates from Omarchy's `default/themed/` imported by `themes/import-omarchy.ps1` into `themes/templates/omarchy/`:
  - **Chrome / Brave / Edge:** the user policy `BrowserThemeColor` (`HKCU\Software\Policies\Google\Chrome`, `…\BraveSoftware\Brave`, `…\Microsoft\Edge`), as Omarchy's browser policy does; applies on the browser's next policy refresh (`chrome://policy`). (check: Chrome honours this HKCU policy on a non-domain PC; Edge's support for `BrowserThemeColor`.)
  - **Claude Code:** `claude.json.tpl` into `~/.claude/` (mission-todo track 7).
  - **OpenCode:** a theme file in `%USERPROFILE%\.config\opencode\themes\omawin.json`, `"theme": "omawin"`.
  - **lazygit, Helix, Alacritty:** rendered configs in their `%APPDATA%` folders, included from the user's config (never replacing it; AGENTS.md).
  - **Windows' accent colour:** `HKCU\Software\Microsoft\Windows\DWM\AccentColor` from the palette's accent (only while `look.accent-*` catalog switches are on).
- Home: script (`set-theme.ps1`), no admin (HKCU only).

```powershell
# themes/set-theme.ps1 --- browsers --- the theme's background as the browser's colour (Omarchy's
# omarchy-theme-set-browser-policy). A user policy: no admin; the browser picks it up on its next
# policy refresh. Only for browsers that are installed.
$bg = $palette.background.TrimStart('#')
foreach ($b in @(
    @{ Exe = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"; Key = 'HKCU:\Software\Policies\Google\Chrome' },
    @{ Exe = "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe"; Key = 'HKCU:\Software\Policies\BraveSoftware\Brave' })) {
    if (-not (Test-Path $b.Exe)) { continue }
    if (-not (Test-Path $b.Key)) { New-Item $b.Key -Force | Out-Null }
    Set-ItemProperty $b.Key BrowserThemeColor "#$bg"
}
```

### N6. Agent accounts: several subscriptions, switched near a limit (M)

- **Omarchy:** `bin/omarchy-agent-account-add`, `-list`, `-use`, `-rename`, `-remove`, `-mode`, `-copy`, hidden `-exec`, `-home`, `-state`, `-transfer`; usage from `agent-usage-*` (27 providers); `shell/plugins/agents`.
- **Why on Windows:** developers with a personal and a work Claude (or Codex) subscription sign out and in by hand; omawin already reads Claude's limits (`bin/agent-usage.ps1`) and knows when one is close.
- **How:** each account is its own config folder (`%LOCALAPPDATA%\omawin\agents\accounts\<agent>\<name>`), used by pointing the agent at it (`CLAUDE_CONFIG_DIR` for Claude Code, `CODEX_HOME` for Codex) when omawin starts it (`omawin launch project`, `omawin agent`, herdr tabs); the current pick per agent in `accounts.json`. `omawin agent account add|list|use [name|next]|rename|remove|mode auto|notify`. `mode auto` makes `agent-usage.ps1`, when a limit passes 95%, switch new sessions to the next account and toast it. Account folders live in `%LOCALAPPDATA%` (secrets never in the repo or the account repo). Codex usage: a `bin/agent-usage-codex.ps1` collector writing the same record format, so the bar's panel shows both. `-copy` to another PC: **later** (secrets over SSH need care). Home: script + the bar's agents widget.

```powershell
# bin/agent-account.ps1: omawin agent account <add|list|use|remove|mode> [claude|codex] [name|next]
param([Parameter(Position = 0)][string]$Verb = 'list', [Parameter(Position = 1)][string]$Agent = 'claude', [Parameter(Position = 2)][string]$Name)
$root = Join-Path $env:LOCALAPPDATA "omawin\agents\accounts\$Agent"
$state = Join-Path $env:LOCALAPPDATA 'omawin\agents\accounts.json'
$envVar = @{ claude = 'CLAUDE_CONFIG_DIR'; codex = 'CODEX_HOME' }[$Agent]
$cfg = if (Test-Path $state) { Get-Content $state -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
switch ($Verb) {
    'add' {
        $dir = Join-Path $root $Name; New-Item -ItemType Directory $dir -Force | Out-Null
        Set-Item "env:$envVar" $dir
        if ($Agent -eq 'claude') { claude /login } else { codex login }   # signs in into that folder
    }
    'use' {
        $names = @(Get-ChildItem $root -Directory | ForEach-Object Name)
        if ($Name -eq 'next') { $Name = $names[([array]::IndexOf($names, $cfg[$Agent]) + 1) % $names.Count] }
        if ($Name -notin $names) { throw "No $Agent account '$Name' (omawin agent account list)." }
        $cfg[$Agent] = $Name; $cfg | ConvertTo-Json | Set-Content $state
        "New $Agent sessions use '$Name'."
    }
    'list' { Get-ChildItem $root -Directory | ForEach-Object { '{0} {1}' -f ($(if ($_.Name -eq $cfg[$Agent]) { '*' } else { ' ' })), $_.Name } }
}
# omawin.cs's LaunchTerminal / LaunchProject read accounts.json and set CLAUDE_CONFIG_DIR / CODEX_HOME
# for the agent tabs they start (C# 5: Environment variables on the ProcessStartInfo).
```

### N7. `omawin pick`: menus for your scripts (S-M)

- **Omarchy:** `bin/omarchy-menu-select`, `omarchy-menu-input`, `omarchy-file-select`, `omarchy-menu-images`.
- **Why on Windows:** user scripts, script commands (roadmap 4.8) and plugins need to ask a question; today they fall back to `Read-Host` in a terminal. Omarchy's scripts use these everywhere (`menu-share`, `transcode`, `reminder`).
- **How:** `omawin pick [--multi] [--prompt <text>] < options` shows the launcher's list with the options from stdin (one per line, `icon | label | value` optional), prints the chosen value(s) to stdout, exit 1 on Esc. `omawin pick --input <prompt>` for text, `omawin pick --file [--folder]` for the finder in pick mode, `--images <folder>` for the grid. Uses plan 02 §9's **answer channel** (the request file plus a reply pipe that `omawin setting` introduces), so omawin.exe waits for the menu app's answer without PowerShell. Home: core (menu app page + `omawin.cs`).

```csharp
// menu-app (C# 12, AOT): `--pick` requests. The options arrive in the request (one per line), the
// chosen values go back on the reply pipe named in the request (plan 02 §9, the answer channel).
case "--pick":
    var opts = PickRequest.Parse(args[1..], body);          // --multi, --prompt, the option lines
    var chosen = await mainWindow.ShowPickAsync(opts);      // the launcher list, its own items, Esc = null
    await Reply.SendAsync(request.ReplyPipe, chosen is null ? null : string.Join('\n', chosen));
    break;
```

### N8. Laptop screen, mirroring and scaling as commands (S, scaling M)

- **Omarchy:** `bin/omarchy-hyprland-monitor-internal` (Super+Ctrl+Delete), `-internal-mirror` (Super+Ctrl+Alt+Delete), `-monitor-scaling` (Super+/ and Super+Alt+/), `hw-recover-internal-monitor`.
- **Why on Windows:** at a desk with the lid open, turning the laptop panel off, or mirroring for a presentation, is Win+P's flyout and a mouse; scaling is three Settings clicks per monitor.
- **How:** `omawin display internal|external|mirror|extend` through `SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_*)` (documented; no admin; what Win+P does). `omawin display scale up|down` steps the focused monitor through Windows' allowed scale list with `DisplayConfigSetDeviceInfo` and the DPI-scale packet (**undocumented**, the same call well-known tools use; `minBuild` and a fallback that opens `ms-settings:display`). Recovery as Omarchy's: if `external` leaves no active display (the external was unplugged), the next `QueryDisplayConfig` change restores `extend`. Quick settings' monitor tile gets the four as buttons. Home: core (`omawin.cs`, C# 5: no window needed, instant).

```csharp
// bin/omawin.cs (C# 5): `omawin display internal|external|mirror|extend`, Win+P's four choices.
[DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);
const uint SDC_APPLY = 0x80, SDC_TOPOLOGY_INTERNAL = 0x1, SDC_TOPOLOGY_CLONE = 0x2,
           SDC_TOPOLOGY_EXTEND = 0x4, SDC_TOPOLOGY_EXTERNAL = 0x8;
static int Display(string[] args)
{
    uint topo;
    switch (args.Length > 0 ? args[0] : "")
    {
        case "internal": topo = SDC_TOPOLOGY_INTERNAL; break;
        case "external": topo = SDC_TOPOLOGY_EXTERNAL; break;
        case "mirror":   topo = SDC_TOPOLOGY_CLONE; break;
        case "extend":   topo = SDC_TOPOLOGY_EXTEND; break;
        default: Console.Error.WriteLine("omawin display internal|external|mirror|extend"); return 2;
    }
    int rc = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | topo);
    if (rc != 0) Console.Error.WriteLine("Windows refused the display change (" + rc + ").");
    return rc == 0 ? 0 : 1;
}
```

### N9. `omawin setup git`: identity, GitHub sign-in, credentials (S)

- **Omarchy:** `bin/omarchy-setup-git` ("Set the name and email for your git commits, and sign in to GitHub"), `install/user/first-run/git.sh`.
- **Why on Windows:** install asks the identity once, but signing in to GitHub (`gh auth login`), making Git use that sign-in (`gh auth setup-git`, or Git Credential Manager), and an SSH key for GitHub are each a separate hunt; after it, `git push` works in Windows and in Linux.
- **How:** `bin/setup-git.ps1`: show and change name / email (the same `~/.gitconfig` include install writes); `gh auth login --web --git-protocol https`; `gh auth setup-git`; optionally `ssh-keygen -t ed25519` plus `gh ssh-key add` when no key exists (never overwriting one); then the Linux side: the same identity and `gh auth setup-git` in the default distro (`wsl/` wrapper, which already shares Windows' SSH agent per 3.9). Menu row Setup › Git; the welcome page (install-speed F) offers it. Home: script; Linux side for the distro half.

```powershell
# bin/setup-git.ps1: who you are in commits, and GitHub signed in for Git (Omarchy's omarchy-setup-git).
$name  = git config --global user.name
$email = git config --global user.email
$n = Read-Host "Name for commits [$name]";  if ($n) { git config --global user.name $n }
$e = Read-Host "Email for commits [$email]"; if ($e) { git config --global user.email $e }
if (-not (gh auth status 2>$null)) { gh auth login --web --git-protocol https }
gh auth setup-git                                  # git uses gh's token for github.com
if (-not (Test-Path "$HOME\.ssh\id_ed25519") -and (Read-Host 'Make an SSH key for GitHub too? [y/N]') -eq 'y') {
    ssh-keygen -t ed25519 -C (git config --global user.email) -f "$HOME\.ssh\id_ed25519"
    gh ssh-key add "$HOME\.ssh\id_ed25519.pub" --title "$env:COMPUTERNAME (omawin)"
}
if (& "$HOME\omawin\bin\linux.ps1" -Running) {   # the same in Linux, when it's there and up
    wsl.exe -- bash -lc "git config --global user.name '$(git config --global user.name)'; git config --global user.email '$(git config --global user.email)'; gh auth status >/dev/null 2>&1 || gh auth login --web; gh auth setup-git"
}
```

### N10. `omawin reset <config>`: back to omawin's default, yours kept (S)

- **Omarchy:** `bin/omarchy-refresh-config` (copies the default over the user's, backing it up), `refresh-hyprland`, `refresh-tmux`, `refresh-herdr`, `reinstall-configs`, Update › Config.
- **Why on Windows:** omawin's configs are the repo's own files (junctions), so a broken edit to `glazewm/config.yaml` or `noctty/config.ghostty` means knowing git. `omawin update` stashes local changes, which is right for updates but not a way to undo one file.
- **How:** `omawin reset <path|name>` (names: `glazewm`, `terminal`, `nvim`, `starship`, `profile`, `bar`, `menu`, `items`): copies the current file to `<path>.backup-<timestamp>` (AGENTS.md: moved aside, never removed), then `git -C ~/omawin checkout -- <path>`, then the matching restart (`omawin restart glazewm` …). `--all` does every tracked config with local changes, listing them first and asking once. Never touches the account folder or anything untracked. Menu: Update › Config › each. Home: script.

```powershell
# bin/reset.ps1: one of omawin's configs back to the repo's version; yours is kept beside it.
param([Parameter(Mandatory)][string]$What, [switch]$All)
$repo = "$HOME\omawin"
$names = @{ glazewm = 'glazewm/config.yaml'; terminal = 'noctty/config.ghostty'; starship = 'starship/starship.toml'
            profile = 'powershell/Microsoft.PowerShell_profile.ps1'; menu = 'launcher/menu.txt'; items = 'launcher/items.txt' }
$path = if ($names[$What]) { $names[$What] } else { $What }
if (-not (git -C $repo diff --name-only -- $path)) { "$path has no changes of yours."; return }
$full = Join-Path $repo $path
Copy-Item $full "$full.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
git -C $repo checkout -- $path
"$path is omawin's again; yours is beside it (.backup-…)."
if ($path -like 'glazewm/*') { & omawin.exe restart glazewm }
```

### N11. Share: the Windows share sheet and LocalSend (S)

- **Omarchy:** `bin/omarchy-menu-share` (`clipboard|file|folder`, LocalSend headless send), Trigger › Share (Super+Ctrl+S), `manual/22-guis.md` LocalSend.
- **Why on Windows:** getting a link, a log or a screenshot onto your phone or another PC. Windows has Nearby Share and Phone Link behind its share sheet, but no keyboard way in.
- **How:** `omawin share clipboard|file [path…]|folder [path]`: with no path, `omawin pick --file` (N7). Sends through the **Windows share sheet** for the menu app's window (`IDataTransferManagerInterop::ShowShareUIForWindow`, documented, no admin), which offers Nearby Share, Phone Link, mail and installed apps; with LocalSend installed (`LocalSend.LocalSend`, Install › Service row, new), a "LocalSend" choice hands it the files (check: whether LocalSend's Windows build takes files on its command line). Clipboard mode writes the text to a temp `.txt`. Finder action (6.1) "Share". Home: core (the share sheet needs a window: the menu app).

### N12. Tailscale in the bar, and Taildrop (M)

- **Omarchy:** `shell/plugins/panels/tailscale` (status, connection switching, machine browsing, copy), `bin/omarchy-tailscale-send`, `-receive`, `installed-service-tailscale`.
- **Why on Windows:** developers on a tailnet reach their other machines (a Linux box, a Mac, a home server) by name all day; Tailscale's own tray menu is mouse-only and shows no addresses to copy.
- **How:** a first-party plugin (roadmap 4.2/4.3 shape): a widget command printing JSON lines from `tailscale.exe status --json` (connected, exit node, peers online), a panel listing machines with copy-name / copy-IP / SSH-to-it (`omawin launch terminal -- ssh <host>`) / open `http://<host>:<port>` actions, exit node switch (`tailscale set --exit-node=`), and `omawin tailscale send <machine> [files]` (`tailscale file cp`) / `receive` (`tailscale file get --wait %USERPROFILE%\Downloads`, a toast per file). Shown only when Tailscale is installed (5.8 condition). No admin (the Tailscale service is already running). Home: first-party plugin.

### N13. herdr's keys in the Shortcuts page (S)

- **Omarchy:** `bin/omarchy-menu-herdr-keybindings` (Super+Ctrl+K), `menu-tmux-keybindings` (Super+Alt+K).
- **Why on Windows:** herdr is omawin's multiplexer for every project; its keys are the ones a new user forgets.
- **How:** roadmap 5.4 builds the Shortcuts page from `glazewm/config.yaml`; add a herdr section read from herdr's config (`herdr/` in the repo, its bindings with their comments), filtered by a `herdr` tag word. Home: core (`Pages.cs`).

### N14. Bar on/off, full-screen desktop, gaps (S)

- **Omarchy:** `bin/omarchy-toggle-bar` (Super+Shift+Space), `omarchy-toggle-fullscreen-desktop`, `omarchy-hyprland-window-gaps-toggle`, `omarchy-bar` (position and background), Appearance › Menu Bar.
- **Why on Windows:** screen sharing, recording and small laptop screens want every pixel; today hiding the bar means `omawin restart` games.
- **How:** `omawin toggle bar` (the native bar's window hidden and its `SHAppBarMessage` space released; `%LOCALAPPDATA%\launcher\bar-off`), `omawin toggle gaps` (GlazeWM's `gaps` rendered as 0 by `themes/render.ps1` from a flag, then reload), `omawin toggle fullscreen-desktop` (both). Appearance rows for the bar's background (opaque / translucent) and edge (top / bottom) once 4.5 moves `bar.json` to the account folder. Home: core (bar) + script (render flag).

### N15. Smaller ones, each S

| Item | Omarchy | On Windows | Home |
|---|---|---|---|
| `omawin files here` | `launch-nautilus-cwd` | the finder (or Explorer with `--explorer`) at the focused terminal's folder, the folder `terminal here` already works out | core (`omawin.cs`) |
| `omawin audio next-output` | `audio-output-switch` (Shift+Mute) | the Audio page's default-device setter, cycling the active outputs, mute kept | core (`Audio.cs`) |
| `omawin window close-all` | `hyprland-window-close-all` | GlazeWM's IPC window list, `close` on each (apps can still ask to save); System › Restart / Shut down offer it first | core |
| `omawin input next` | `input-method cycle` (Super+I) | Win+Space was Windows' layout switch and omawin took it; `WM_INPUTLANGCHANGEREQUEST` with `HKL_NEXT` to the foreground window. Key: maintainer's call | core (`omawin.cs`) |
| System › Hibernate | `system.hibernate` row | shown when hibernate is on (`powercfg /a`, 5.8 condition); `shutdown /h` | data (`menu.txt`) |
| `omawin plugin validate <folder>` | `plugin-validate` | the manifest checked against omawin's schema (4.1), errors with line numbers | script, with 4.5 |
| `omawin dev migration <what>` | `dev-add-migration` | writes `migrations/<today>-<what>.ps1` from a template (safe twice, new-PC safe, PS 5.1) | script |
| web app protocol handlers | `webapp-handler-hey`, `-zoom` | a web app may claim `mailto:` or `zoommtg:` (HKCU `Software\Classes` + `RegisteredApplications`); Windows makes you pick it once in Default apps, opened on that page | script, with 5.3 |
| `omawin world-clock` | `panels/elsewhen` | cities in the clock's right-click popup (`TimeZoneInfo`, no network), from `bar.json` settings | core (bar) or first-party plugin |
| OpenSSH server (opt-in admin) | `setup-security-sshd` | `Add-WindowsCapability OpenSSH.Server`, the service, a firewall rule for private networks, a key from GitHub (`https://github.com/<you>.keys`) into `authorized_keys`; in the one admin prompt (2.9), never by default | install/admin.ps1 option |
| launch-or-install | `launch-1password`, `launch-signal`, `launch-spotify` | a launcher item whose program is missing offers its Install row instead of failing | core (with 5.8) |
| more Install rows | `install-gaming-*`, `install-service-nordvpn`, `-sunshine` | winget rows: `HeroicGamesLauncher.HeroicGamesLauncher`, `Blizzard.BattleNet`, `Nvidia.GeForceNow`, `Libretro.RetroArch`, `Mojang.MinecraftLauncher`, `NordSecurity.NordVPN`, `LizardByte.Sunshine` (check each id) | data (`menu.txt`) |
| `omawin transcode` | `transcode` (Super+Ctrl+.) | a picture to jpg/png at high/medium/low, a video to mp4/gif at 4k/1080p/720p, written beside the original; ffmpeg (the music preset has it) and ImageMagick (`ImageMagick.ImageMagick`, a winget row); a finder action (6.1) | script |
| `update-stay-awake` | `update-stay-awake` | `omawin toggle stay-awake` on for the length of `omawin update` | script (`bin/update.ps1`) |

Considered and left out (new, but not worth the code): ASCII art (`ascii`, `transcode-ascii`), background intros and video wallpapers (memory, 8K), branding text/images, hotkey notices for time/battery (the bar shows both), the remote-session indicator, the disk speed test, the OEM key printer, the dev UI gallery, theme sync to remote herdr machines, Hermes / OpenClaw / T3 Code installers, the single-window square aspect and tiled-fullscreen modes, `restart-app`, `update-analyze-logs` (until updates have logs).

---

## Top 10 for a developer

Ranked by how often a developer would feel it, against what it costs.

| # | Item | Size | Why first |
|---|---|---|---|
| 1 | **N1. Drop-down console** (`omawin console`) | M | a terminal one key away and gone again, all day; rides on 5.7's scratchpad |
| 2 | **N2. Hooks** (`omawin hook`) | S | lets anyone extend omawin without editing the repo; unblocks theme/update customizations now, before plugins |
| 3 | **N9. `omawin setup git`** | S | the first thing every developer does on a new PC; ends with `git push` working in Windows and Linux |
| 4 | **N3. Crash watch + agent diagnosis** | M | turns silent crashes into one click to an answer; builds on 8.15 and 5.5 |
| 5 | **N5. The theme in every app** (browser, Claude Code, OpenCode, lazygit, Helix, Alacritty) | M | omawin's identity; apps it installs itself still clash today |
| 6 | **N4. `omawin debug`** (+ `omawin version`) | S | a public project's bug reports need it; also the agent's first input when something breaks |
| 7 | **N7. `omawin pick`** | S-M | every script and plugin needs a question asked well; makes 4.8 script commands useful |
| 8 | **N6. Agent accounts** (and Codex usage) | M | heavy agent users juggle subscriptions daily; the usage reader already exists |
| 9 | **N10. `omawin reset <config>`** | S | a safe undo for live configs, without git knowledge |
| 10 | **N8. `omawin display`** (laptop screen, mirror, scale) | S / M | docking and presenting without Win+P's flyout and Settings |

Next after these: N12 Tailscale, N14 bar and gaps toggles, N13 herdr keys, N11 Share, then N15's rows. Two mission-todo-only items rank with this list and need roadmap items first: **screen recording** (between 3 and 4) and **the font switcher** (between 6 and 7).
