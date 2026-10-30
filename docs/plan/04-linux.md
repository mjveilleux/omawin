# 04: Linux in WSL, first class (Phase 3, 3B)

> **Untested sketch.** Nothing here has been compiled or run. It names real files and functions as of 2026-10-30 (`bin/omawin.cs`, `wsl/setup.sh`, `themes/render.ps1`, ...) and follows [AGENTS.md](../../AGENTS.md): `bin/omawin.cs` is C# 5 for the in-box `csc.exe` (no `?.`, no `$"..."`, no `out var`, no `=>` members, non-ASCII only as `\uXXXX`); install scripts and `bin/linux.ps1` run under Windows PowerShell 5.1; files under `bash/` and `wsl/` are LF. Anything uncertain is marked *(check)*; numbers to take on the PC are marked *(measure)*.

Roadmap: [roadmap.md](roadmap.md) Phase 3 (3.1-3.11) and 3B. Omarchy's source: `mjveilleux/omarchy` (the fork), pinned below.

## Files

| File | Change |
|---|---|
| `bin/defaults.txt` | new kind `shell` (`linux` first, `windows`) |
| `bin/omawin.cs` | `--windows`; `OnLinux` / `LinuxIsDefault` / `LinuxPath`; `LaunchTerminal`, `NocttyWindow`, `TerminalHere`, `LaunchProject`, `HerdrProject`, `LaunchEditor`, `Default`; `WarmLinux` from `TerminalWarm` |
| `bin/commands.txt` | `linux backup`, `linux reset`, `linux stop`; `launch terminal` / `launch project` summaries gain `--windows` |
| `bin/linux.ps1` (new) | backup / reset / stop (PS 5.1) |
| `wsl/setup.sh` | Omarchy's package list minus a skip list; Omarchy's bash; Docker; `/etc/wsl.conf`; wrappers; SSH agent bridge; file index; theme sync; Linux migrations; `update` for Ubuntu |
| `wsl/omarchy-skip.txt` (new) | what of Omarchy's base list Linux in WSL leaves out |
| `wsl/check.sh` (new) | Linux-side checks, read by `check.ps1` and CI |
| `wsl/bin/omawin-theme-sync`, `wsl/bin/omawin-index-files` (new) | copied to `~/.local/bin` |
| `wsl/migrations/` (new) | `<yyyy-mm-dd>-<what>.sh`, run by `setup.sh refresh` |
| `wsl/.wslconfig` | mirrored networking, DNS tunneling, firewall, sparse VHD, idle timeout |
| `wsl/install.ps1`, `install/other.ps1` | pass Windows paths to Linux (`WSLENV`); stop the distro when `wsl.conf` changed; `update` on Ubuntu too |
| `bash/bashrc` | Omarchy's defaults win in Linux; OSC 7 / 11 / title; side marks; SSH agent; `open` |
| `starship/starship.toml` | side mark first, coloured by side |
| `powershell/Microsoft.PowerShell_profile.ps1` | `OMAWIN_SIDE`, side mark, `pwsh · ` title prefix |
| `themes/render.ps1` | `linux_background` (colour mixing) and `%LOCALAPPDATA%\launcher\theme-linux.sh` |
| `themes/set-theme.ps1` | nudge a running Linux to sync its theme copies |
| `menu-app/FileSearch.cs` | Linux's index as one more root |
| `check.ps1` | runs `wsl/check.sh` when the distro is running |
| `.github/workflows/linux.yml` (new) | `setup.sh` in `archlinux:latest` and `ubuntu:24.04` |
| `.gitattributes` | `wsl/** text eol=lf` (then `wsl/*.ps1 text eol=crlf`): the units, scripts and skip list are read by Linux |

---

## 3.1 On by default

`install/software.ps1` `Initialize-Presets` already defaults the Linux question to Arch (Enter gives Arch and adds `wsl`). Left: don't offer it when virtualization is off.

```powershell
# install/lib.ps1: WSL 2 needs virtualization in the firmware. With Hyper-V (or WSL) already
# running, Windows itself is the hypervisor and the processor reports it off, so that counts too.
function Test-Virtualization {
    try {
        if ((Get-CimInstance Win32_ComputerSystem).HypervisorPresent) { return $true }
        return @(Get-CimInstance Win32_Processor | Where-Object { $_.VirtualizationFirmwareEnabled }).Count -gt 0
    } catch { return $true }  # can't tell: offer it, wsl --install says why if it fails
}
```

In `Initialize-Presets`, before the Linux question:

```powershell
        if (-not (Test-Virtualization)) {
            Write-Warn 'Linux in WSL needs virtualization, which is off in this PC''s firmware (UEFI/BIOS: Intel VT-x / AMD SVM). Turn it on, then: omawin preset add wsl'
            $answer = 'none'
        } else {
            $answer = (Read-InstallAnswer "Linux in WSL: ...").Trim().ToLower()   # as now
        }
```

---

## 3.2 Omarchy's terminal layer in Arch

### `wsl/omarchy-skip.txt` (new, LF)

Every name below is in `install/omarchy-base.packages` at the pinned commit. Anything not listed comes in.

```text
# What Linux in WSL leaves out of Omarchy's base package list (install/omarchy-base.packages,
# read by wsl/setup.sh at the commit it pins). Everything else Omarchy installs comes in, so a new
# terminal tool in Omarchy arrives with the next pin bump. One name per line; # comments.

# The desktop: Windows and omawin draw it.
hyprland
hyprland-guiutils
hyprland-preview-share-picker
hyprpicker
hyprsunset
quickshell
sddm
uwsm
plymouth
xdg-desktop-portal-gtk
xdg-desktop-portal-hyprland
xdg-terminal-exec
omarchy-hyprland-titlebars
omarchy-screensaver
owe
grim
slurp
wl-clipboard
wtype
gpu-screen-recorder
omasnap
tesseract
tesseract-data-eng
zbar
qt6-base
qt6-declarative
qt6-multimedia
qt6-svg
qt6-wayland
qt6-imageformats
python-gobject
gnome-themes-extra
yaru-icon-theme
gnome-keyring
libsecret
udiskie
gvfs-mtp
gvfs-nfs
gvfs-smb

# Hardware and the system under it: WSL's kernel, Windows' network, sound, printers and power.
alsa-utils
asdcontrol
avahi
nss-mdns
bluez
bluez-tools
bluez-utils
bolt
brightnessctl
ddcutil
cups
cups-filters
cups-pk-helper
system-config-printer
networkmanager
wireless-regdb
wireplumber
pamixer
rtkit
omarchy-power-profiles-daemon
kernel-modules-hook
tzupdate
dosfstools
exfatprogs
gnome-disk-utility
qemu-user-static-binfmt
# Windows' firewall (Hyper-V firewall, .wslconfig firewall=true) guards the VM.
ufw
ufw-docker

# Input methods: Windows' own.
fcitx5
fcitx5-chewing
fcitx5-chinese-addons
fcitx5-gtk
fcitx5-hangul
fcitx5-mozc
fcitx5-qt
fcitx5-table-extra

# GUI apps (Windows has its own; WSLg could run them, but omawin doesn't install them).
chromium
nautilus
nautilus-python
sushi
evince
imv
mpv
mpv-mpris
foot
obsidian
libreoffice-fresh
localsend
moonlight-qt
pinta
superwhisper-bin
# Omarchy's removable preinstalls (bin/omarchy-remove-preinstalls). (check: cliamp may be a TUI worth keeping)
aether
cliamp
hype
monologue
omacalc
omacut
omawrite
signed

# Fonts Windows draws (the terminal is a Windows program).
noto-fonts
noto-fonts-cjk
noto-fonts-emoji
ttf-ia-writer
ttf-jetbrains-mono-nerd-basic
woff2-font-awesome
ttfx

# Neovim is omawin's config (nvim/, shared with Windows), not Omarchy's.
omarchy-nvim

# No AUR.
yay
```

What comes in, at the pinned commit: base-devel, bash-completion, bat, btop, docker, docker-buildx, docker-compose, dotnet-runtime, eza, expac, fakeroot, fastfetch, fd, ffmpeg, fontconfig, fzf, git, gum, herdr, imagemagick, inetutils, inotify-tools, inxi, jq, lazygit, less, libvips, libyaml, lua51, luarocks, man-db, mariadb-libs, mise-bin, nvim, pacman-contrib, plocate, postgresql-libs, python-poetry-core, qrencode, ripgrep, ruby, socat, starship, tldr, tmux, tobi-try, tree-sitter-cli, unzip, usage, vi, whois, yt-dlp, zoxide, and the two unknowns `disktree-bin` and `gliff` *(check what they are; skip if GUI)*.

Tension in the roadmap: "a pinned commit" vs "arrives on the next `omawin update` with no change in omawin". The pin is one line in `setup.sh`; bumping it is part of the roadmap's "Always: following Omarchy" sync (a scheduled job can open the bump PR after CI below passes). Until then, a new Omarchy tool arrives with the next pin bump, not by itself.

### `wsl/setup.sh`: Omarchy's files at the pin

```bash
# Omarchy's files Linux uses, at a pinned commit of the fork: its base package list, its bash
# defaults (default/bash) and the configs of terminal tools omawin doesn't configure itself.
# A sparse, blob-less clone: Omarchy's themes carry big pictures we never want here.
omarchy_repo=https://github.com/mjveilleux/omarchy
omarchy_commit=7f4b1dd22cf256c074e71c7a2f56dc387033768c  # bumped by the "following Omarchy" sync (check: pushed to the fork)
omarchy_dir=/usr/share/omawin/omarchy

omarchy_files() {
  [ "$(git -C "$omarchy_dir" rev-parse HEAD 2>/dev/null)" = "$omarchy_commit" ] && return 0
  say "Omarchy's package list and shell defaults (${omarchy_commit:0:7})"
  local new="$omarchy_dir.new"
  rm -rf "$new"
  mkdir -p "$new"
  git -C "$new" init -q
  git -C "$new" remote add origin "$omarchy_repo"
  git -C "$new" sparse-checkout set --no-cone /install/omarchy-base.packages /default/bash/ /config/btop/ /config/lazygit/ /config/tmux/
  git -C "$new" fetch -q --depth 1 --filter=blob:none origin "$omarchy_commit"
  git -C "$new" checkout -q FETCH_HEAD
  rm -rf "$omarchy_dir"
  mv "$new" "$omarchy_dir"
}

# Names in a package list file: comments and blanks out, CRLF tolerated (a checkout on Windows).
list_names() { tr -d '\r' < "$1" | sed 's/#.*//' | awk 'NF { print $1 }' | sort -u; }

# Omarchy's base list minus wsl/omarchy-skip.txt.
omarchy_packages() {
  comm -23 <(list_names "$omarchy_dir/install/omarchy-base.packages") <(list_names "$repo/wsl/omarchy-skip.txt")
}

# What omawin adds on top, each for a reason Omarchy doesn't have:
#   nodejs npm python-pip  mason's npm and pip tools (prettier, sqlfluff) (check: mise instead, as Omarchy's default tools do)
#   github-cli             Windows' core has gh; the same on both sides
#   openssh wget zip which sudo curl
arch_extras="nodejs npm python python-pip github-cli openssh wget zip which sudo curl"

arch_packages() {
  omarchy_files
  local available wanted missing
  available="$(pacman -Slq | sort -u)"
  wanted="$(comm -12 <(omarchy_packages) <(printf '%s\n' "$available"))"
  # A name Omarchy's list has but the repositories don't (renamed, or AUR-only) would stop the
  # whole transaction: left out and said.
  missing="$(comm -23 <(omarchy_packages) <(printf '%s\n' "$available"))"
  [ -z "$missing" ] || say "not in the repositories, left out: $(echo $missing)"
  printf '%s\n' $wanted $arch_extras | sort -u > /usr/share/omawin/packages.txt  # what check.sh compares against
  # shellcheck disable=SC2046
  pacman -S --needed --noconfirm $(cat /usr/share/omawin/packages.txt) > /dev/null 2> >(quiet_skips)
  desktop_guard
}

# A dependency that pulls a desktop in (roadmap 3.2 risk): said, never removed.
desktop_guard() {
  local pulled
  pulled="$(pacman -Qq hyprland quickshell sddm uwsm plymouth 2>/dev/null || true)"
  [ -z "$pulled" ] || say "warning: desktop packages came in as dependencies: $(echo $pulled) (pactree -r <name> says why)"
}

arch_root() {
  arch_repos
  say "packages"
  mkdir -p /usr/share/omawin
  # The keyrings first (an image older than a maintainer's key can't check the rest), and git for
  # Omarchy's files.
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring git > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm > /dev/null
  arch_packages
}

arch_update() {
  grep -q '^\[omarchy\]' /etc/pacman.conf || { say "not omawin's Arch: left alone"; return; }
  arch_repos
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm
  arch_packages  # what the pin added since
}
```

`/usr/share/omawin` holds omawin's root-owned files in Linux (Omarchy's checkout, the wanted list). It's omawin's own folder, so `rm -rf` of the checkout inside it is fine.

### Omarchy's bash defaults, then omawin's

`default/bash/rc` does `: "${OMARCHY_PATH:=/usr/share/omarchy}"` and sources `envs shell aliases functions init` and the inputrc. Pointing `OMARCHY_PATH` at the checkout works: `envs` only sources `env-bootstrap` from `/usr/share/omarchy` when it exists (it doesn't here), so our value stays. `completions` looks for an `omarchy` command and returns quietly without one. What `env-bootstrap` would have added (mise shims and `~/.local/bin` on login shells) goes in omawin's login block.

`setup.sh` `user_part`, the two `put_block` calls become:

```bash
  local login=~/.profile
  [ -f ~/.bash_profile ] && login=~/.bash_profile
  # Login shells (herdr's tabs are `bash -lc`, wsl.exe --exec bash -lc): the paths. OMAWIN_STATE is
  # Windows' %LOCALAPPDATA%\launcher as Linux sees it (wsl/install.ps1 passes it through WSLENV).
  put_block "$login" "export OMAWIN=\"$repo\" OMAWIN_STATE=\"${OMAWIN_STATE:-}\"
export STARSHIP_CONFIG=\"\$OMAWIN/starship/starship.toml\" HERDR_CONFIG_PATH=\"\$OMAWIN/herdr/config.toml\"
# Omarchy's env-bootstrap does these for its login shells; its file isn't at /usr/share/omarchy here.
case \":\$PATH:\" in *\":\$HOME/.local/share/mise/shims:\"*) ;; *) PATH=\"\$PATH:\$HOME/.local/share/mise/shims\" ;; esac
case \":\$PATH:\" in *\":\$HOME/.local/bin:\"*) ;; *) PATH=\"\$HOME/.local/bin:\$PATH\" ;; esac"

  # Interactive shells: Omarchy's defaults (default/bash/rc), then omawin's on top (bash/bashrc).
  # EDITOR and BROWSER first: Omarchy's envs default them to its launchers, which aren't here.
  put_block ~/.bashrc "export OMAWIN=\"$repo\" OMAWIN_STATE=\"${OMAWIN_STATE:-}\" STARSHIP_CONFIG=\"$repo/starship/starship.toml\"
export EDITOR=nvim BROWSER=\"\$HOME/.local/bin/browser\"
export OMARCHY_PATH=/usr/share/omawin/omarchy
[ -f \"\$OMARCHY_PATH/default/bash/rc\" ] && . \"\$OMARCHY_PATH/default/bash/rc\"
[ -f \"\$OMAWIN/bash/bashrc\" ] && . \"\$OMAWIN/bash/bashrc\""
```

`bash/bashrc` (shared with Git Bash): where Omarchy's defaults are loaded, Omarchy wins, so omawin's own tool init, aliases, keys and history only run without them (Git Bash):

```diff
 [[ $- == *i* ]] || return  # scripts and tools get a plain shell

+# In Linux, Omarchy's defaults (default/bash, sourced just before this) already set up starship,
+# zoxide, fzf, eza, the history and the keys; where they and omawin disagree, Omarchy wins. Git
+# Bash has no Omarchy, so it gets omawin's own below.
+if [[ -z ${OMARCHY_PATH:-} ]]; then
 shell_cache="$HOME/.cache/bash"
 update-shell-cache() {
 ...
-HISTSIZE=10000
+HISTSIZE=10000
+fi
+
+# Both: fzf in the terminal's 16 colours follows the theme (Omarchy sets no FZF_DEFAULT_OPTS).
+export FZF_DEFAULT_OPTS='--height 40% --layout reverse --border rounded --info inline-right --color=16,border:8,info:8,gutter:-1'
```

(Move the `FZF_*` and `BAT_THEME` exports out of the guarded part; the rest of the file goes inside it. The 3B and 3.9 section below is appended after.)

Ubuntu: Omarchy's `init` sources `/usr/share/fzf/key-bindings.bash`, which Ubuntu keeps elsewhere; the bashrc's Linux section adds `[[ $(type -t __fzf_history__) == function ]] || eval "$(fzf --bash)"` (fzf ≥ 0.48, from mise below).

### Omarchy's configs (btop, lazygit, tmux)

Omarchy's `refresh-config` pattern: copied once; a file that's there (yours, or copied before) is kept.

```bash
omarchy_configs() {
  local from=/usr/share/omawin/omarchy/config tool
  for tool in btop lazygit tmux; do
    [ -d "$from/$tool" ] && [ ! -e ~/.config/$tool ] || continue
    cp -r "$from/$tool" ~/.config/$tool
    # btop: a Windows reason to differ. Omarchy paints the theme's background (theme_background =
    # true); here the terminal's own shows through, so Linux's tint (3B) stays visible, as btop4win
    # on Windows does (themes/set-theme.ps1).
    [ "$tool" = btop ] && sed -i 's/^theme_background = .*/theme_background = false/' ~/.config/btop/btop.conf
  done
}
```

Lazygit: Omarchy's `config/lazygit/config.yml` is empty and Omarchy has no lazygit template (`default/themed/` has none), so lazygit follows the terminal's ANSI colours on both systems; 3.8 has nothing more to do for it.

### Ubuntu: apt, then mise for what apt lacks

```bash
ubuntu_root() {
  case "$arch" in
    x86_64) nvim_arch=x86_64; ts_arch=x64 ;;
    aarch64) nvim_arch=arm64; ts_arch=arm64 ;;
    *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
  esac
  export DEBIAN_FRONTEND=noninteractive
  say "packages"
  apt-get update -qq
  # Omarchy's terminal tools by Ubuntu's names, where 24.04 has them; the rest from mise (user part).
  apt-get install -y -qq build-essential sudo curl wget git unzip zip gpg ca-certificates \
    ripgrep fd-find jq bat eza zoxide gh btop tmux socat inotify-tools less man-db plocate whois \
    bash-completion imagemagick ffmpeg tldr nodejs npm python3 python3-venv python3-pip > /dev/null
  [ -e /usr/local/bin/fd ] || ln -s "$(command -v fdfind)" /usr/local/bin/fd
  [ -e /usr/local/bin/bat ] || ln -s "$(command -v batcat)" /usr/local/bin/bat
  ubuntu_mise
  ubuntu_docker
  # Neovim and tree-sitter from their releases (as now) ...
}

# mise from its apt repository (mise.jdx.dev/installing-mise.html).
ubuntu_mise() {
  command -v mise > /dev/null && return 0
  install -dm 755 /etc/apt/keyrings
  curl -fsSL https://mise.jdx.dev/gpg-key.pub | gpg --dearmor -o /etc/apt/keyrings/mise-archive-keyring.gpg
  echo "deb [signed-by=/etc/apt/keyrings/mise-archive-keyring.gpg arch=$(dpkg --print-architecture)] https://mise.jdx.dev/deb stable main" \
    > /etc/apt/sources.list.d/mise.list
  apt-get update -qq && apt-get install -y -qq mise > /dev/null
}

# Docker Engine from Docker's apt repository (docs.docker.com/engine/install/ubuntu).
ubuntu_docker() {
  command -v docker > /dev/null && return 0
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin > /dev/null
}

ubuntu_update() {
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get upgrade -y -qq
  OMAWIN_UPDATE=1 ubuntu_root
}
```

User side, in `user_part` after the links (Ubuntu only): tools 24.04's apt lacks or has too old *(check each against 24.04's archive)*:

```bash
  if [ "$distro" = ubuntu ]; then
    say "mise tools (what Ubuntu's apt lacks)"
    mise use -g -y starship lazygit fzf gum usage fastfetch > /dev/null  # (check: aqua names; -y flag)
  fi
```

---

## 3.3 The VM and `/etc/wsl.conf`

### `wsl/.wslconfig`

```ini
# WSL 2 VM settings (all distros). Copied to %UserProfile%\.wslconfig by install.ps1, unless your
# account folder has its own wsl/.wslconfig (docs/work.md), which is copied instead.
# Applies the next time WSL starts (`wsl --shutdown`, or after everything in the distro closes).
# Reference: https://learn.microsoft.com/windows/wsl/wsl-config

# memory is left at WSL's default, half of this PC's RAM. To cap it lower, put a copy of this file
# with `memory=6GB` (say) under [wsl2] in your account folder.

[wsl2]
# localhost both ways (a server in Linux is localhost:port for Windows' browser and the other way
# round) and VPNs behave. A VPN client that breaks with it: networkingMode=nat in your account's copy.
networkingMode=mirrored
# DNS answered by Windows, so a VPN's names resolve in Linux too.
dnsTunneling=true
# Windows' (Hyper-V) firewall rules apply to the VM (Omarchy's ufw is left out: wsl/omarchy-skip.txt).
firewall=true
# Windows' proxy settings in Linux.
autoProxy=true
# The VM stays up this long (ms) after its last process; the keeper (omawin start terminal, 3.5)
# holds the distro itself while Linux is the default shell. (check: key name and unit)
vmIdleTimeout=600000

[experimental]
# Hand cached memory back to Windows right away (the current default, made explicit).
autoMemoryReclaim=dropCache
# The disk image shrinks as files are deleted.
sparseVhd=true
# Mirrored networking: Windows apps reach a Linux server on the PC's own addresses too (check).
hostAddressLoopback=true
```

### `/etc/wsl.conf` (setup.sh, root)

```bash
# key=value under [section] of an ini file: replaced where it is, else added at the end of its
# section, else a new section.
ini_set() {
  local file="$1" section="$2" key="$3" value="$4"
  touch "$file"
  awk -v s="[$section]" -v k="$key" -v v="$value" '
    $0 == s { insec = 1; print; next }
    /^\[/ { if (insec && !done) { print k "=" v; done = 1 } insec = 0 }
    insec && $0 ~ "^[ \t]*" k "[ \t]*=" { if (!done) { print k "=" v; done = 1 } next }
    { print }
    END { if (!done) { if (!insec) print "\n" s; print k "=" v } }
  ' "$file" > "$file.new" && mv "$file.new" "$file"
}

# WSL's settings for this distro. Each one can be overridden by your account folder's
# wsl/wsl.conf (docs/work.md): the same ini format, applied after these.
wsl_conf() {
  local user="$1" f=/etc/wsl.conf
  ini_set $f boot systemd true                 # Docker and timers run under systemd
  ini_set $f interop appendWindowsPath false   # Linux's tools win in Linux; ~/.local/bin wraps what's needed from Windows
  ini_set $f user default "$user"
  local over="${OMAWIN_ACCOUNT:-}/wsl/wsl.conf" section="" line key value
  [ -n "${OMAWIN_ACCOUNT:-}" ] && [ -f "$over" ] || return 0
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"
    case "$line" in
      \[*\]) section="${line#[}"; section="${section%]}" ;;
      '' | \#* | \;*) ;;
      *=*)
        key="$(echo "${line%%=*}" | xargs)"; value="$(echo "${line#*=}" | xargs)"
        [ -n "$section" ] && ini_set $f "$section" "$key" "$value" ;;
    esac
  done < "$over"
}
```

In `root_part`, replace the hand-written `[user]` block with `wsl_conf "$user"`, and add `docker_root "$user"` (3.6) and `loginctl_linger "$user"` (3.7).

### `wsl/install.ps1`

Pass Windows' paths through to Linux, and restart the distro whenever `wsl.conf` changed (systemd and interop apply only from a fresh start):

```powershell
# Windows' folders for Linux, translated to /mnt/c/... by WSL (/p): the launcher's state (the theme
# file, the file index), this account's folder (wsl.conf overrides), the home (omawin.exe) and
# LOCALAPPDATA (npiperelay). install/other.ps1 does the same before `setup.sh update`.
function Set-WslEnv {
    $env:OMAWIN_STATE = "$env:LOCALAPPDATA\launcher"
    $env:OMAWIN_ACCOUNT = & "$Repo\bin\user-layer.ps1"   # (check: prints a path even when the folder doesn't exist)
    $mine = 'OMAWIN_STATE/p', 'OMAWIN_ACCOUNT/p', 'USERPROFILE/p', 'LOCALAPPDATA/p'
    $have = @("$env:WSLENV" -split ':' | Where-Object { $_ -and ($mine -notcontains $_) })
    $env:WSLENV = (@($have) + $mine) -join ':'
}
Set-WslEnv
```

```diff
-$wasDefault = ((& wsl.exe -d $Distro -u root '--' cat /etc/wsl.conf 2>$null) -join "`n") -match "(?m)^default=$user\s*$"
+$confBefore = (& wsl.exe -d $Distro -u root '--' cat /etc/wsl.conf 2>$null) -join "`n"
 Invoke-Wsl -d $Distro -u root --cd "$Repo\wsl" '--' bash ./setup.sh root $user
-if (-not $wasDefault) { Invoke-Wsl --terminate $Distro }
+# wsl.conf (the user, systemd, interop) applies from WSL's next start: stopped only when it changed,
+# since stopping ends whatever runs in it.
+if (((& wsl.exe -d $Distro -u root '--' cat /etc/wsl.conf 2>$null) -join "`n") -ne $confBefore) { Invoke-Wsl --terminate $Distro }
```

### Wrappers in `~/.local/bin` (setup.sh, user)

`appendWindowsPath=false` takes Windows' programs off Linux's PATH; these bring back the ones people type. Paths are baked in at setup (from `WSLENV`) and rewritten on every `refresh`.

```bash
# A small script in ~/.local/bin (replaced each run).
put_wrapper() { cat > ~/.local/bin/"$1"; chmod +x ~/.local/bin/"$1"; }

wrappers() {
  local home_win="${USERPROFILE:-}" local_win="${LOCALAPPDATA:-}"
  put_wrapper code <<EOF
#!/bin/bash
# VS Code from Linux: its bin/code is a shell script that opens the folder over Remote - WSL.
for c in "$local_win/Programs/Microsoft VS Code/bin/code" "/mnt/c/Program Files/Microsoft VS Code/bin/code"; do
  [ -x "\$c" ] && exec "\$c" "\$@"
done
echo "code: VS Code isn't installed on Windows (omawin default editor code)" >&2
exit 1
EOF
  put_wrapper explorer <<'EOF'
#!/bin/bash
# File Explorer on a folder or file, Linux paths too (\\wsl.localhost\...). explorer.exe exits 1
# even when it worked.
/mnt/c/Windows/explorer.exe "$(wslpath -w "${1:-.}")"
exit 0
EOF
  put_wrapper clip <<'EOF'
#!/bin/bash
# Windows' clipboard. clip.exe reads UTF-16 when the text starts with its byte-order mark, so
# accents and emoji survive (check).
{ printf '\xff\xfe'; iconv -f utf-8 -t utf-16le; } | /mnt/c/Windows/System32/clip.exe
EOF
  put_wrapper browser <<'EOF'
#!/bin/bash
# A URL in Windows' default browser ($BROWSER, gh, Omarchy's `open`).
/mnt/c/Windows/explorer.exe "$1"
exit 0
EOF
  put_wrapper omawin <<EOF
#!/bin/bash
# omawin's commands from Linux (omawin theme set, omawin launch editor ...), through interop.
exec "$home_win/.local/bin/omawin.exe" "\$@"
EOF
}
```

---

## 3.4 The terminal opens in Linux (`bin/omawin.cs`, C# 5)

### `bin/defaults.txt`

```diff
 terminal | noctty | noctty (Ghostty for Windows) | bin/noctty.ps1 | noctty.exe
 terminal | wt | Windows Terminal | Microsoft.WindowsTerminal | wt.exe
 
+# The shell a new terminal opens: your Linux in WSL (the default; the distro is `omawin default
+# linux`), or PowerShell. Without a Linux yet, PowerShell, silently. --wsl and --windows override it
+# for one window.
+shell | linux | Linux (WSL) | wsl/install.ps1 | wsl.exe
+shell | windows | PowerShell | Microsoft.PowerShell | pwsh.exe
+
 editor | nvim | Neovim (in the terminal) | Neovim.Neovim | nvim.exe
```

`bin/commands.txt`:

```diff
-launch terminal | A terminal, in the default terminal: [--dir <folder>] [--title <title>] [--wsl: in your Linux] [-- <command>] | builtin launch-terminal
-launch project | A project window (launcher/project-tabs.txt): <folder> [--wsl] | builtin launch-project
+launch terminal | A terminal, in the default terminal and shell (omawin default shell): [--dir <folder>] [--title <title>] [--wsl | --windows] [-- <command>: a Windows program unless --wsl] | builtin launch-terminal
+launch project | A project window (launcher/project-tabs.txt), in the default shell: <folder> [--wsl | --windows] | builtin launch-project
```

### Which side

```csharp
    class Launch { public string Dir, Title; public bool Wsl, Windows; public string[] Command = new string[0]; }

    //   [--dir <folder>] [--title <title>] [--wsl | --windows] [-- <command> <args>]
    static Launch ParseLaunch(string[] args)
    {
        var l = new Launch();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--") { l.Command = args.Skip(i + 1).ToArray(); break; }
            if (args[i] == "--dir" && i + 1 < args.Length) l.Dir = args[++i];
            else if (args[i] == "--title" && i + 1 < args.Length) l.Title = args[++i];
            else if (args[i] == "--wsl") l.Wsl = true;
            else if (args[i] == "--windows") l.Windows = true;
            else if (l.Dir == null) l.Dir = args[i];
        }
        return l;
    }

    // Linux is the default shell (omawin default shell) and the picked distro is there.
    static bool LinuxIsDefault()
    {
        var shell = Current("shell");
        return shell != null && shell.Name == "linux" && DistroRegistered(Distro);
    }

    // Which side a launch opens on. A command given without --wsl is a Windows program (a key's
    // pwsh script, nvim on a Windows file, an install in a terminal), so only a plain shell or a
    // project follows the default. A Linux path is always Linux.
    static bool OnLinux(Launch l)
    {
        if (l.Windows) return false;
        if (l.Wsl) return true;
        if (l.Dir != null && (l.Dir.StartsWith("/") || l.Dir.StartsWith("~/") || l.Dir == "~")) return true;
        if (l.Command.Length > 0) return false;
        return LinuxIsDefault();
    }

    // A path as Linux sees it: C:\x\y -> /mnt/c/x/y (WSL's default automount root, check:
    // /etc/wsl.conf [automount] root), \\wsl.localhost\<distro>\home\u -> /home/u; a Linux path as is.
    static string LinuxPath(string path)
    {
        if (path == null || path.StartsWith("/") || path.StartsWith("~")) return path;
        var unc = System.Text.RegularExpressions.Regex.Match(path, @"^\\\\wsl(?:\.localhost|\$)\\[^\\]+(\\.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (unc.Success) return unc.Groups[1].Value.Length == 0 ? "/" : unc.Groups[1].Value.Replace('\\', '/');
        var drive = System.Text.RegularExpressions.Regex.Match(path, @"^([A-Za-z]):[\\/]?(.*)$");
        if (!drive.Success) return path;
        return ("/mnt/" + drive.Groups[1].Value.ToLowerInvariant() + "/" + drive.Groups[2].Value.Replace('\\', '/')).TrimEnd('/');
    }
```

### `LaunchTerminal`, `NocttyWindow`

```diff
     static int LaunchTerminal(string[] args)
     {
         var l = ParseLaunch(args);
+        l.Wsl = OnLinux(l);
         Choice terminal;
         var exe = Terminal(out terminal);
         ...
         if (l.Wsl)
         {
             a.AddRange(new[] { "wsl.exe", "-d", Distro });
-            if (l.Dir != null) { a.Add("--cd"); a.Add(l.Dir); }
+            // No folder: Linux's home, not the Windows folder WSL would otherwise translate.
+            a.Add("--cd"); a.Add(l.Dir ?? "~");
             if (l.Command.Length > 0) { a.Add("--"); a.AddRange(l.Command); }
         }
```

```diff
     static int NocttyWindow(string exe, Launch l)
     {
         var a = new List<string>();
+        // A Linux window is never a hand-off to the running instance with its spare pwsh: bash in a
+        // warm WSL starts in milliseconds; what makes it fast is WSL kept warm (WarmLinux).
         if (l.Command.Length == 0 && !l.Wsl)
         ...
         if (l.Wsl)
         {
             a.AddRange(new[] { "-e", "wsl.exe", "-d", Distro });
-            if (l.Dir != null) { a.Add("--cd"); a.Add(l.Dir); }
+            a.Add("--cd"); a.Add(l.Dir ?? "~");  // wsl --cd takes Windows folders too (C:\x -> /mnt/c/x)
             if (l.Command.Length > 0) { a.Add("--"); a.AddRange(l.Command); }
         }
```

(`--single-instance=false` and `--windows-spare-shell=false` stay as they are on that path: no spare shell for Linux.)

### `TerminalHere`

```diff
         if (process == "noctty")
         {
             ...
-            if (exe != null && terminal.Name == "noctty") return NocttyWindow(exe, new Launch { Dir = "inherit" });
+            // The fork's "inherit" opens its own shell (pwsh) in the folder of the window in front:
+            // only when PowerShell is the default. With Linux, the folder is asked for (+list-windows,
+            // ~70 ms) and a Linux window opens there. (Later: the fork takes "inherit" with a command.)
+            if (exe != null && terminal.Name == "noctty" && !LinuxIsDefault()) return NocttyWindow(exe, new Launch { Dir = "inherit" });
             if (exe != null && Capture(Path.Combine(Path.GetDirectoryName(exe), "noctty.com"), new List<string> { "+list-windows" }, out json) == 0)
             {
                 ...
                     if (folder.StartsWith("/")) return LaunchTerminal(new[] { "--wsl", "--dir", folder });
+                    // A Windows folder: Linux there when it's the default (LaunchTerminal decides).
                     if (Directory.Exists(folder)) return LaunchTerminal(new[] { "--dir", folder });
         ...
         if (process == "WindowsTerminal")
         {
             var text = new StringBuilder(512);
             GetWindowText(hwnd, text, text.Capacity);
-            var title = text.ToString().Trim();
+            var title = StripSide(text.ToString().Trim());
         ...
-        return LaunchTerminal(new[] { "--dir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
+        // Nothing with a folder in front: Linux's home, or Windows' (LaunchTerminal: no --dir).
+        if (LinuxIsDefault()) return LaunchTerminal(new string[0]);
+        return LaunchTerminal(new[] { "--dir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
     }
+
+    // A title without its side prefix (3B): "arch \u00B7 ~/src/app" -> "~/src/app",
+    // "pwsh \u00B7 omawin" -> "omawin" (the name the PowerShell profile recorded in dirs\).
+    static string StripSide(string title)
+    {
+        var m = System.Text.RegularExpressions.Regex.Match(title, "^[\\w.-]+ \u00B7 (.*)$");
+        return m.Success ? m.Groups[1].Value : title;
+    }
```

Explorer in front: `ExplorerFolder` gives `C:\...` (or `\\wsl.localhost\Arch\home\...` for a Linux folder, which `wsl --cd` doesn't take: pass `LinuxPath(folder)` when it starts with `\\wsl`):

```diff
         if (process == "explorer")
         {
             var folder = ExplorerFolder(hwnd);
+            if (folder != null && folder.StartsWith(@"\\wsl", StringComparison.OrdinalIgnoreCase)) return LaunchTerminal(new[] { "--wsl", "--dir", LinuxPath(folder) });
             if (folder != null && Directory.Exists(folder)) return LaunchTerminal(new[] { "--dir", folder });
         }
```

### `LaunchProject`, `HerdrProject`

```diff
     static int LaunchProject(string[] args)
     {
         var l = ParseLaunch(args);
-        if (l.Dir == null) return Fail("which folder? omawin launch project <folder> [--wsl]");
+        if (l.Dir == null) return Fail("which folder? omawin launch project <folder> [--wsl | --windows]");
+        l.Wsl = OnLinux(l);  // Linux by default, a Windows folder too (wsl --cd maps it); --windows overrides
```

```diff
-        if (System.Text.RegularExpressions.Regex.IsMatch(tabs, "\"tabs\":\\s*\\[\\s*\\]")) SetUpHerdrProject(call, l.Dir, session);
+        // herdr in Linux takes Linux paths (--cwd).
+        if (System.Text.RegularExpressions.Regex.IsMatch(tabs, "\"tabs\":\\s*\\[\\s*\\]")) SetUpHerdrProject(call, l.Wsl ? LinuxPath(l.Dir) : l.Dir, session);
```

New projects go in `~/src` in Linux: wherever a project is created (the launcher's new-project flow, if any; *(check)* `menu-app/Pages.cs`), the Linux default makes `wsl.exe -d <distro> -- mkdir -p ~/src/<name>` and opens `~/src/<name>` with `--wsl`.

### `LaunchEditor`: Linux paths

```diff
     static int LaunchEditor(string[] args)
     {
         if (args.Length == 0) return Fail("which file or folder? omawin launch editor <path>");
         var path = args[0];
+        // A Linux file (the finder's Linux results, 3.7): opened in Linux.
+        if (path.StartsWith("/"))
+        {
+            var parent = path.LastIndexOf('/') > 0 ? path.Substring(0, path.LastIndexOf('/')) : "/";
+            var linuxEditor = Current("editor");
+            var found = Find(linuxEditor.Program);
+            if (linuxEditor.Name == "code") return Detach("wsl.exe", new List<string> { "-d", Distro, "--cd", parent, "--", "code", path });  // the wrapper in ~/.local/bin (check: stays hidden)
+            if (linuxEditor.Name == "neovide" && found != null) return Detach(found, new List<string> { "--wsl", path });  // (check: neovide --wsl picks the default distro)
+            return LaunchTerminal(new[] { "--wsl", "--dir", parent, "--", "nvim", path });
+        }
         var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
```

### `Default`: the `shell` kind

```diff
-        var linux = args[0] == "linux";
-        Func<bool> installed = () => linux ? DistroRegistered(pick.Program) : Find(pick.Program) != null;
+        // A Linux is there when WSL has the distro; Linux as the shell, when the picked Linux is;
+        // anything else when its program is found.
+        var linux = args[0] == "linux";
+        var linuxShell = args[0] == "shell" && pick.Name == "linux";
+        Func<bool> installed = () => linux ? DistroRegistered(pick.Program)
+            : linuxShell ? DistroRegistered(Distro)
+            : Find(pick.Program) != null;
         ...
-                ? Start("pwsh", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, pick.Winget.Replace('/', '\\'))) + (linux ? " -Linux " + pick.Name : ""))
+                ? Start("pwsh", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, pick.Winget.Replace('/', '\\')))
+                    + (linux ? " -Linux " + pick.Name : linuxShell ? " -Linux " + Current("linux").Name : ""))
```

`omawin default shell windows` brings PowerShell back; `omawin default shell linux` sets the Linux up first if it isn't there.

### Windows Terminal, Install › Development

- Windows Terminal generates a profile per distro itself (its WSL profile generator; the GUID is derived from the distro name *(check)*). `windows-terminal/settings.json`'s `defaultProfile` stays PowerShell; `LaunchTerminal` passes `wsl.exe -d <distro>` explicitly, so the default profile doesn't matter for omawin's windows.
- Install › Development asks the side, Linux first; a Linux pick runs `wsl.exe -d <distro> -- bash -lc "mise use -g <tool>@latest"` (login shell: mise on PATH).
- `bin/profile-terminal.ps1` gains `-Wsl` (passes `--wsl` to the launches it times).

---

## 3.5 No cold start

`omawin start terminal` (GlazeWM's `startup_commands`, `TerminalWarm`) is where omawin keeps the terminal warm at sign-in; Linux is kept warm there too, before the noctty check (Windows Terminal users get it as well). (`omawin start` is the menu app's `--start-hidden`, not omawin.cs.)

```csharp
    // --- Linux kept warm -------------------------------------------------------------------------
    // A cold WSL takes ~8 s (the VM, then the distro and systemd). While Linux is the default
    // shell, one hidden process holds the distro up for the whole sign-in, so a Linux window opens
    // as fast as PowerShell's. It costs the VM's idle memory (docs/plan/04-linux.md, 3.5); a file
    // %LOCALAPPDATA%\launcher\linux-warm-off turns it off.
    static void WarmLinux()
    {
        if (!LinuxIsDefault() || File.Exists(Path.Combine(StateDir, "linux-warm-off"))) return;
        var pidFile = Path.Combine(StateDir, "linux-warm.pid");
        try
        {
            int pid;
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out pid)
                && Process.GetProcessById(pid).ProcessName.Equals("wsl", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch (ArgumentException) { }  // that process is gone
        catch (IOException) { }
        try
        {
            // `sleep infinity` in the distro: no shell, no files touched. (check: wsl.exe keeps the
            // instance while its stdin is closed; if not, StartQuietly's redirect goes for this one.)
            var keeper = StartQuietly(new ProcessStartInfo("wsl.exe", "-d " + Quote(Distro) + " --exec /bin/sleep infinity") { CreateNoWindow = true });
            File.WriteAllText(pidFile, keeper.Id.ToString());
            Trace("linux-warm");
        }
        catch (Exception) { }
    }

    static int TerminalWarm()
    {
        WarmLinux();
        Choice terminal;
        ...
```

`omawin linux stop` (`wsl --terminate`) ends the keeper with the distro; the next `omawin start terminal` (or sign-in) starts it again.

### The RAM trade-off *(measure)*

Taken on the PC, each after `wsl --shutdown` and 60 s settled. Inside Linux is exact; `VmmemWSL` from Windows may need admin to read *(check)*.

```powershell
# Idle VM held by the keeper: what keeping Linux warm costs.
wsl --shutdown; Start-Sleep 5
omawin start terminal; Start-Sleep 60
wsl -d archlinux -- free -m                        # used / buff/cache, from inside
Get-Process VmmemWSL, vmmem -ErrorAction SilentlyContinue |
    Select-Object Name, @{ n = 'MB'; e = { [int]($_.WorkingSet64 / 1MB) } }
# Cold vs warm open: the gain.
omawin profile terminal -Wsl -Runs 5               # warm
wsl --shutdown; omawin profile terminal -Wsl -Runs 1  # cold
```

| | Idle VM memory | First Linux window |
|---|---|---|
| keeper off, after `wsl --shutdown` | 0 MB | ~8 s (manual/04-launcher.md) |
| keeper on, systemd + Docker socket idle | *(measure; expect a few hundred MB)* | *(measure; target ≈ PowerShell's)* |
| keeper on, after a build (dropCache) | *(measure)* | |

`omawin profile ram` (the profile-ram skill's script) gets WSL's VM as its own line with a budget: the measured idle figure plus 20%.

---

## 3.6 Docker in Linux

Omarchy's list brings docker, docker-buildx and docker-compose (3.2); Omarchy enables `docker.socket` (`install/config/enable-services.sh`).

Omarchy deliberately does **not** add the user to the `docker` group (`install/config/docker.sh`: the group is passwordless root). In WSL that guard protects nothing: `wsl -u root` gives root without a password and setup.sh already gives the user sudo without one. That's the Windows reason to differ, written next to it:

```bash
# Docker Engine for the user. Omarchy keeps its user out of the docker group (install/config/
# docker.sh: the group is root without a password). Here root is already that (wsl -u root, and
# sudo without a password above), so the group guards nothing and `docker` just works.
docker_root() {
  local user="$1"
  getent group docker > /dev/null || groupadd -r docker
  id -nG "$user" | tr ' ' '\n' | grep -qx docker || usermod -aG docker "$user"
  # Started on first use, as Omarchy does. systemctl enable works before systemd runs (it only
  # writes links) (check in a fresh distro before its restart).
  systemctl enable docker.socket > /dev/null 2>&1 || true
}
```

Install › Development › Docker DB in Linux runs Omarchy's way (`docker run -d --restart unless-stopped -p 127.0.0.1:5432:5432 ...`) through `wsl.exe -d <distro> -- docker run ...`; mirrored networking makes `localhost:5432` reachable from Windows apps. Docker Desktop stays a choice, never installed by default.

---

## 3.7 Linux files in the finder

### `wsl/bin/omawin-index-files` (new, copied to `~/.local/bin`)

```bash
#!/bin/bash
# Linux's files for the finder (Win+F): fd over the home folder (honouring .gitignore, no hidden
# folders: .cache, .local and the like stay out) into ~/.cache/omawin/files.tsv, path, mtime and
# size per line. A copy goes to Windows' %LOCALAPPDATA%\launcher\linux-files.tsv, so the menu app
# reads one local file and never crawls \\wsl.localhost (9P is slow). Run by the
# omawin-index-files timer and path units (setup.sh).
set -euo pipefail
out="${XDG_CACHE_HOME:-$HOME/.cache}/omawin/files.tsv"
mkdir -p "${out%/*}"
cd ~
fd --type f --exclude node_modules --print0 . |
  xargs -0 -r stat --printf "$HOME/%n\t%Y\t%s\n" > "$out.tmp"
mv "$out.tmp" "$out"
if [ -n "${OMAWIN_STATE:-}" ] && [ -d "$OMAWIN_STATE" ]; then
  { printf '# %s\t%s\n' "${WSL_DISTRO_NAME:-linux}" "$HOME"; cat "$out"; } > "$OMAWIN_STATE/linux-files.tsv.tmp"
  mv -f "$OMAWIN_STATE/linux-files.tsv.tmp" "$OMAWIN_STATE/linux-files.tsv"
fi
```

### systemd user units (written by setup.sh, `refresh`)

A path unit can't watch a tree, so: on start, every 10 minutes, and when `~/src` itself changes (a clone, a new project). Recursive inotify (`inotify-tools` is in Omarchy's list) is the alternative if 10 minutes turns out too slow.

```bash
index_units() {
  local units=~/.config/systemd/user
  mkdir -p "$units/timers.target.wants" "$units/default.target.wants" ~/src
  cat > "$units/omawin-index-files.service" <<EOF
[Unit]
Description=Linux's files for omawin's finder (Win+F)

[Service]
Type=oneshot
Environment=OMAWIN_STATE=${OMAWIN_STATE:-} WSL_DISTRO_NAME=${WSL_DISTRO_NAME:-}
ExecStart=%h/.local/bin/omawin-index-files
Nice=10
IOSchedulingClass=idle
EOF
  cat > "$units/omawin-index-files.timer" <<'EOF'
[Timer]
OnStartupSec=30s
OnUnitActiveSec=10min

[Install]
WantedBy=timers.target
EOF
  cat > "$units/omawin-index-files.path" <<'EOF'
[Path]
PathChanged=%h/src
Unit=omawin-index-files.service

[Install]
WantedBy=default.target
EOF
  # Enabled by hand (links): `systemctl --user` needs the user's manager, which a `wsl -u` session
  # may not reach yet (check).
  ln -sf ../omawin-index-files.timer "$units/timers.target.wants/omawin-index-files.timer"
  ln -sf ../omawin-index-files.path "$units/default.target.wants/omawin-index-files.path"
  systemctl --user daemon-reload > /dev/null 2>&1 || true
}
```

Root part: the user's manager runs from boot, not only while a terminal is open:

```bash
loginctl_linger() { mkdir -p /var/lib/systemd/linger; touch "/var/lib/systemd/linger/$1"; }
```

### `menu-app/FileSearch.cs` (.NET 9, so modern C# is fine here)

```csharp
// Linux's files (docs/plan/04-linux.md 3.7): the index Linux keeps and copies to
// %LOCALAPPDATA%\launcher\linux-files.tsv (wsl/bin/omawin-index-files). One more root for the
// finder, under the distro's name; a hit's path is a Linux path, which `omawin launch editor`
// opens in Linux.
public static class LinuxFiles
{
    static string IndexFile => Path.Combine(MenuModel.StateDir, "linux-files.tsv");
    static readonly object gate = new();
    static DateTime loaded;
    static string label = "Linux";
    static (string Path, string Relative, long Time, long Size)[] rows = [];

    static void Refresh()
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(IndexFile); } catch { return; }
        lock (gate)
        {
            if (stamp == loaded) return;
            var lines = File.ReadAllLines(IndexFile);
            if (lines.Length == 0 || !lines[0].StartsWith("# ")) return;
            var head = lines[0][2..].Split('\t');
            label = Projects.Linux.Name == "arch" ? "Arch" : head[0];  // (check: the label the finder's chips use)
            string home = head.Length > 1 ? head[1] : "/";
            rows = [.. lines.Skip(1).Select(l => l.Split('\t')).Where(p => p.Length == 3)
                .Select(p => (p[0], p[0].StartsWith(home + "/") ? p[0][(home.Length + 1)..] : p[0],
                    long.TryParse(p[1], out var t) ? t : 0, long.TryParse(p[2], out var s) ? s : 0))];
            loaded = stamp;
        }
    }

    public static string Root => "linux:";
    public static string Label { get { Refresh(); return label; } }

    // The best matches by fzy's score on the path under home (Fuzzy.Match). (measure: a home of
    // 100k files must stay under a frame; if not, prefilter on the query's letters in order, or
    // keep the rows' lower-cased paths.)
    public static IEnumerable<FileSearch.Hit> Search(string query, int limit)
    {
        Refresh();
        if (query.Length == 0) return [];
        (string Path, string Relative, long Time, long Size)[] all;
        lock (gate) all = rows;
        return all.Select(r => (r, score: Fuzzy.Match(r.Relative, query)))
            .Where(x => x.score != Fuzzy.None)
            .OrderByDescending(x => x.score).Take(limit)
            .Select(x => new FileSearch.Hit(x.r.Path, x.r.Path[(x.r.Path.LastIndexOf('/') + 1)..], label,
                x.r.Relative.Contains('/') ? x.r.Relative[..x.r.Relative.LastIndexOf('/')] : "",
                false, x.r.Size, DateTimeOffset.FromUnixTimeSeconds(x.r.Time).LocalDateTime, null, false,
                (int)(x.score * 100)));  // (check: the scale FileSearch's re-ranking expects)
    }
}
```

In `FileSearch.Search`: when `scope` is null or `LinuxFiles.Root`, append `LinuxFiles.Search(query, limit)` before the re-ranking; `Scopes()` adds `(LinuxFiles.Root, LinuxFiles.Label)` when the file exists. In `MainWindow.xaml.cs` (`FileSearch.Opened`, line ~1289): skip `Opened` for a path starting with `/` (fff's frecency is Windows'), and open it with `MenuModel.Omawin("launch", "editor", hit.Path)`. The chip gets the distro's logo (Arch / Ubuntu glyph). Linux GUI apps (WSLg): check whether `AppsFolder.cs` already lists WSL's Start menu shortcuts.

---

## 3.8 The theme in Linux, live

Starship, fzf, bat, herdr and lazygit use the terminal's ANSI colours; Neovim follows already. btop is the one with its own file. Linux syncs its copies itself, at the first prompt after a switch (bashrc, 3B below), and `set-theme.ps1` nudges a running Linux right away.

### `wsl/bin/omawin-theme-sync` (new, copied to `~/.local/bin`)

```bash
#!/bin/bash
# The Windows side's theme into Linux's own copies: btop's theme (Omarchy's btop.conf picks
# "current"), then running btops reload (SIGUSR2, as omarchy-restart-btop does). Run by the bashrc
# when theme-linux.sh changed, and by themes/set-theme.ps1 while Linux runs.
set -u
state="${OMAWIN_STATE:-}"
[ -n "$state" ] && [ -f "$state/theme-linux.sh" ] || exit 0
. "$state/theme-linux.sh"
# An installed theme's file (%LOCALAPPDATA%\launcher\themes) before omawin's, as render.ps1 does.
for src in "$state/themes/btop/$OMAWIN_THEME.theme" "${OMAWIN:-}/themes/btop/$OMAWIN_THEME.theme"; do
  if [ -f "$src" ]; then
    mkdir -p ~/.config/btop/themes
    cp "$src" ~/.config/btop/themes/current.theme
    break
  fi
done
mkdir -p ~/.config/omawin
printf '%s\n' "$OMAWIN_THEME" > ~/.config/omawin/theme
pkill -SIGUSR2 -x btop || true
```

### `themes/set-theme.ps1`, after the btop section

```powershell
# --- Linux in WSL -------------------------------------------------------------------------------
# Its own copies (btop) sync at its next prompt by themselves (bash/bashrc); while it runs, it's
# told now. Never started for this: a stopped Linux catches up when it next starts.
$env:WSL_UTF8 = '1'
$running = @(& wsl.exe --list --running --quiet 2>$null | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
if ($running) {
    $distro = (Get-Content "$PSScriptRoot\..\bin\defaults.txt" | ForEach-Object { $p = @($_ -split '\|' | ForEach-Object { $_.Trim() }); if ($p.Count -ge 5 -and $p[0] -eq 'linux') { $p } } |
        Where-Object { $running -contains $_[4] } | Select-Object -First 1)
    if ($distro) {
        Start-Process wsl.exe -ArgumentList '-d', $distro[4], '--exec', 'bash', '-lc', 'omawin-theme-sync' -WindowStyle Hidden
        Write-Host "  linux       $($distro[4])"
    }
}
```

(The running distro is the one told, whatever the pick. `Select-Object` on nested arrays in 5.1 needs care *(check)*.)

---

## 3.9 Credentials

Git already uses Windows' Git Credential Manager from Linux. SSH: Windows' OpenSSH agent (or 1Password's, which serves the same pipe) reached from Linux through `npiperelay` (a Windows exe) and `socat` (in Omarchy's list; apt on Ubuntu).

Windows' own `ssh-agent` service is disabled by default and turning it on takes admin: that's an opt-in line in the one admin step (`install/admin-batch.ps1`: `Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent`), never its own prompt. 1Password's agent needs nothing.

```bash
# npiperelay on the Windows side (interop runs it), from its maintained fork's releases.
npiperelay() {
  [ -n "${LOCALAPPDATA:-}" ] || return 0   # not under WSL (CI)
  local dir="$LOCALAPPDATA/Programs/npiperelay"
  [ -f "$dir/npiperelay.exe" ] && return 0
  say "npiperelay (Windows' SSH agent in Linux)"
  local zip; zip="$(mktemp)"
  curl -fsSL "$(latest_asset albertony/npiperelay "windows_$( [ "$arch" = aarch64 ] && echo arm64 || echo amd64).zip")" -o "$zip"  # (check: asset names)
  mkdir -p "$dir"
  unzip -o -q -j "$zip" npiperelay.exe -d "$dir"
  rm -f "$zip"
}

# The bridge as a user service: a socket in Linux, Windows' agent pipe behind it.
ssh_agent_unit() {
  [ -n "${LOCALAPPDATA:-}" ] || return 0
  local units=~/.config/systemd/user
  mkdir -p "$units/default.target.wants"
  cat > "$units/omawin-ssh-agent.service" <<EOF
[Unit]
Description=Windows' SSH agent in Linux (npiperelay + socat)

[Service]
ExecStartPre=/bin/rm -f %t/omawin-ssh-agent.sock
ExecStart=/usr/bin/socat UNIX-LISTEN:%t/omawin-ssh-agent.sock,fork,umask=077 "EXEC:$LOCALAPPDATA/Programs/npiperelay/npiperelay.exe -ei -s //./pipe/openssh-ssh-agent,nofork"
Restart=on-failure

[Install]
WantedBy=default.target
EOF
  ln -sf ../omawin-ssh-agent.service "$units/default.target.wants/omawin-ssh-agent.service"
}
```

(The `EXEC:` argument with a space inside socat's address and systemd's quoting: *(check)*; the fallback is a two-line script in `~/.local/bin` that `ExecStart` runs.) The bashrc sets `SSH_AUTH_SOCK` (below). GPG likewise later, when there are keys (`gpg-agent` pipe via npiperelay `-ep -a`).

---

## 3.10 Kept, checked, recoverable

### `setup.sh`: entry points, `refresh`, migrations

```bash
#   setup.sh root <user>            as root: packages, the Linux user, wsl.conf, Docker
#   setup.sh user [agent ...]       as the user: links, herdr, the agents, then refresh
#   setup.sh refresh                as the user: omawin's copies (wrappers, units, Omarchy's configs) and Linux migrations
#   setup.sh update                 as root: packages (Arch or Ubuntu), then refresh as the user

# The distro's default user (wsl.conf [user] default).
wsl_user() {
  awk -F= '/^\[user\]/ { u = 1; next } /^\[/ { u = 0 } u && $1 ~ /^[ \t]*default[ \t]*$/ { gsub(/[ \t\r]/, "", $2); print $2 }' /etc/wsl.conf 2>/dev/null
}

# Linux-side migrations (wsl/migrations/<yyyy-mm-dd>-<what>.sh), Omarchy's pattern: each runs
# once, as the user (sudo is there without a password), and leaves a marker in
# ~/.local/state/omawin/migrations. Like omawin's Windows migrations they must be safe on a brand-new
# Linux and safe to run twice, so a new Linux runs them all.
run_migrations() {
  local dir="$repo/wsl/migrations" state=~/.local/state/omawin/migrations file name
  [ -d "$dir" ] || return 0
  mkdir -p "$state"
  for file in "$dir"/*.sh; do
    [ -f "$file" ] || continue
    name="$(basename "$file")"
    [ -f "$state/$name" ] && continue
    say "migration ${name%.sh}"
    if OMAWIN="$repo" bash -euo pipefail "$file"; then touch "$state/$name"; else say "migration ${name%.sh} failed; it runs again next time"; fi
  done
}

refresh_part() {
  cd ~
  mkdir -p ~/.local/bin ~/.config
  wrappers
  for tool in omawin-theme-sync omawin-index-files; do
    install -m 755 <(tr -d '\r' < "$repo/wsl/bin/$tool") ~/.local/bin/$tool
  done
  omarchy_configs
  index_units
  npiperelay
  ssh_agent_unit
  run_migrations
}

update_part() {
  [ -f /etc/omawin ] || { say "not omawin's Linux: left alone"; return; }
  case "$distro" in
    arch) arch_update ;;
    ubuntu) ubuntu_update ;;
  esac
  local user; user="$(wsl_user)"
  [ -n "$user" ] && runuser -l "$user" -c "OMAWIN_STATE='${OMAWIN_STATE:-}' LOCALAPPDATA='${LOCALAPPDATA:-}' USERPROFILE='${USERPROFILE:-}' bash '$repo/wsl/setup.sh' refresh"
}

case "${1:-}" in
  root) [ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }; root_part "${2:?the user name}" ;;
  user) shift; user_part "$@"; refresh_part ;;
  refresh) refresh_part ;;
  update) [ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }; update_part ;;
  *) echo "setup.sh root <user> | setup.sh user [agent ...] | setup.sh refresh | setup.sh update" >&2; exit 1 ;;
esac
```

### `install/other.ps1`: update for any omawin Linux

```diff
-# omawin update: Arch's packages too, from Omarchy's tested set (wsl/setup.sh update; as root,
-# which WSL gives without a password). Only for the wsl preset's Arch, once it's there.
+# omawin update: Linux's packages too (Arch from Omarchy's tested set, Ubuntu from apt), then its
+# copies and migrations (wsl/setup.sh update; as root, which WSL gives without a password). Only for
+# the wsl preset's Linux once it's there; setup.sh leaves a distro omawin didn't make alone.
 $linux = Get-LinuxPick
-if ($Update -and (Get-Presets) -contains 'wsl' -and $linux.Name -eq 'arch' -and (Get-WslDistros) -contains $linux.Distro) {
+if ($Update -and (Get-Presets) -contains 'wsl' -and (Get-WslDistros) -contains $linux.Distro) {
     Invoke-InstallItem 'Programs' "$($linux.Label) packages" {
         $env:WSL_UTF8 = '1'
+        Set-WslEnv   # install/lib.ps1 (the same as wsl/install.ps1's)
```

### `wsl/check.sh` (new)

Prints `ok|warn|fail<TAB>what<TAB>detail` lines for `check.ps1`; exits 1 on a fail (CI).

```bash
#!/usr/bin/env bash
# omawin check's Linux half: run as root by check.ps1 (wsl -u root) and by CI (--ci: no WSL, no
# systemd). One line per check: ok|warn|fail, what, detail (tab-separated). Read-only.
set -uo pipefail
user="${1:?the user name}"; ci="${2:-}"
failed=0
line() { printf '%s\t%s\t%s\n' "$1" "$2" "${3:-}"; [ "$1" = fail ] && failed=1; return 0; }
distro="$(. /etc/os-release && echo "$ID")"

if [ ! -f /etc/omawin ]; then line ok "Linux" "yours (omawin didn't set it up): not checked"; exit 0; fi

if [ -z "$ci" ]; then
  [ "$(ps -p 1 -o comm=)" = systemd ] && line ok systemd "PID 1" || line fail systemd "not running (wsl.conf [boot] systemd=true; wsl --terminate <distro>)"
  state="$(systemctl is-system-running 2>/dev/null || true)"
  case "$state" in running) line ok "systemd state" "$state" ;; degraded) line warn "systemd state" "degraded: $(systemctl --failed --no-legend --plain | awk '{print $1}' | xargs)" ;; *) line fail "systemd state" "$state" ;; esac
  grep -qx 'systemd=true' /etc/wsl.conf && line ok "wsl.conf systemd" || line fail "wsl.conf systemd" "missing"
  grep -qx 'appendWindowsPath=false' /etc/wsl.conf && line ok "wsl.conf interop" "Windows' PATH left out" || line warn "wsl.conf interop" "Windows' PATH is appended"
fi
grep -qx "default=$user" /etc/wsl.conf && line ok "default user" "$user" || line fail "default user" "not $user"
id "$user" > /dev/null 2>&1 && line ok "user $user" || line fail "user $user" "missing"

if [ "$distro" = arch ]; then
  grep -q '^\[omarchy\]' /etc/pacman.conf && line ok "Omarchy's repositories" "/etc/pacman.conf" || line fail "Omarchy's repositories" "not in /etc/pacman.conf (setup.sh root)"
  if [ -f /usr/share/omawin/packages.txt ]; then
    missing="$(comm -23 <(sort -u /usr/share/omawin/packages.txt) <(pacman -Qq | sort -u) | xargs)"
    [ -z "$missing" ] && line ok "Omarchy's terminal packages" "$(wc -l < /usr/share/omawin/packages.txt) installed" || line fail "Omarchy's terminal packages" "missing: $missing (omawin update)"
  else line fail "Omarchy's terminal packages" "no list yet (setup.sh root)"; fi
  pulled="$(pacman -Qq hyprland quickshell sddm 2>/dev/null | xargs)"
  [ -z "$pulled" ] && line ok "no desktop packages" || line warn "no desktop packages" "$pulled came in"
fi
for tool in git nvim starship zoxide fzf eza bat fd rg lazygit btop herdr mise docker; do
  runuser -l "$user" -c "command -v $tool" > /dev/null 2>&1 && line ok "$tool" || line fail "$tool" "not on the user's PATH"
done
id -nG "$user" | tr ' ' '\n' | grep -qx docker && line ok "docker group" || line fail "docker group" "$user isn't in it"
[ -z "$ci" ] && { systemctl is-enabled docker.socket > /dev/null 2>&1 && line ok "docker.socket" "enabled" || line fail "docker.socket" "not enabled"; }
home="$(getent passwd "$user" | cut -d: -f6)"
grep -q '^# >>> omawin >>>' "$home/.bashrc" && line ok "bashrc block" || line fail "bashrc block" "missing (setup.sh user)"
[ -d /usr/share/omawin/omarchy/default/bash ] && line ok "Omarchy's bash defaults" "$(git -C /usr/share/omawin/omarchy rev-parse --short HEAD 2>/dev/null)" || line fail "Omarchy's bash defaults" "missing"
if [ -z "$ci" ]; then
  idx="$home/.cache/omawin/files.tsv"
  if [ -f "$idx" ] && [ -n "$(find "$idx" -mmin -60)" ]; then line ok "file index" "$(wc -l < "$idx") files"; else line warn "file index" "missing or older than an hour (systemctl --user status omawin-index-files.timer)"; fi
  uid="$(id -u "$user")"
  [ -S "/run/user/$uid/omawin-ssh-agent.sock" ] && line ok "SSH agent bridge" || line warn "SSH agent bridge" "no socket (Windows' ssh-agent off, or the unit failed)"
fi
for f in "${OMAWIN:-/nonexistent}"/wsl/migrations/*.sh; do
  [ -f "$f" ] || continue
  [ -f "$home/.local/state/omawin/migrations/$(basename "$f")" ] || line warn "migration $(basename "$f" .sh)" "not run yet (omawin update)"
done
exit $failed
```

(`$OMAWIN` isn't set under `wsl -u root`; `check.ps1` runs it from `--cd "$Repo\wsl"`, so use `$(cd "$(dirname "$0")/.." && pwd)` as `setup.sh` does.)

### `check.ps1`, after the existing wsl preset check

```powershell
# Linux's own checks (wsl/check.sh), only while it runs: check never starts WSL (~8 s cold).
if ($chosen -contains 'wsl' -and $distros -contains $pick[4]) {
    $env:WSL_UTF8 = '1'
    Section "Linux ($($pick[4]))"
    $running = @(& wsl.exe --list --running --quiet 2>$null | ForEach-Object { "$_".Trim() }) -contains $pick[4]
    if (-not $running) { Warn 'Linux checks' "$($pick[4]) isn't running, so they were skipped (omawin start terminal keeps it warm when Linux is the default shell)" }
    else {
        # The Linux user, named as wsl/install.ps1 names it.
        $linuxUser = [Environment]::UserName.ToLower() -replace '[^a-z0-9_-]', ''
        if ($linuxUser -notmatch '^[a-z_]') { $linuxUser = "u$linuxUser" }
        $out = & { $ErrorActionPreference = 'Continue'; & wsl.exe -d $pick[4] -u root --cd "$Repo\wsl" -- bash ./check.sh $linuxUser 2>&1 }
        foreach ($line in $out) {
            $f = @("$line" -split "`t", 3)
            switch ($f[0]) {
                'ok' { Pass $f[1] $f[2] }
                'warn' { Warn $f[1] $f[2] }
                'fail' { Fail $f[1] $f[2] }
                default { if ("$line".Trim()) { Warn 'wsl/check.sh' "$line" } }
            }
        }
    }
}
```

### `bin/linux.ps1` (new, PS 5.1)

`bin/commands.txt`:

```text
linux backup | Your Linux in WSL exported to a dated file in ~/omawin-backups/linux (wsl --export); kept until you delete it | pwsh bin/linux.ps1 backup
linux reset | Back up, then replace omawin's Linux with a fresh one set up again, and offer to bring ~/src back; never a Linux omawin didn't make | pwsh bin/linux.ps1 reset
linux stop | Stop your Linux in WSL (and the keep-warm); it starts again with the next Linux terminal | pwsh bin/linux.ps1 stop
```

```powershell
<#
.SYNOPSIS
    omawin linux backup | reset | stop: your Linux in WSL, kept and recoverable (roadmap 3.10).

.DESCRIPTION
    backup  wsl --export to ~/omawin-backups/linux/<distro>-<date>.tar (never deleted by omawin)
    reset   a backup first; then, only for a distro omawin made (/etc/omawin) and only after you
            type its name: unregister it, install it fresh (wsl/install.ps1), and offer to put
            ~/src back from the backup
    stop    wsl --terminate (the keep-warm process ends with it)

    Runs under Windows PowerShell 5.1.
#>
param([Parameter(Position = 0)][ValidateSet('backup', 'reset', 'stop')][string]$Action = 'backup')
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$env:WSL_UTF8 = '1'
$State = "$env:LOCALAPPDATA\launcher"

# The pick (bin/defaults.txt's linux rows; the PC's pick, else the first), as wsl/install.ps1 reads it.
$choices = @(foreach ($line in Get-Content "$Repo\bin\defaults.txt") {
        $p = @($line -split '\|' | ForEach-Object { $_.Trim() })
        if ($p.Count -ge 5 -and $p[0] -eq 'linux') { [pscustomobject]@{ Name = $p[1]; Distro = $p[4] } }
    })
$picked = if (Test-Path "$State\defaults\linux") { "$(Get-Content "$State\defaults\linux" -TotalCount 1)".Trim() } else { '' }
$pick = @($choices | Where-Object Name -eq $picked) + $choices | Select-Object -First 1
$Distro = $pick.Distro
$have = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss' -ErrorAction SilentlyContinue | ForEach-Object { $_.GetValue('DistributionName') })
if ($have -notcontains $Distro) { throw "$Distro isn't installed for this account (omawin preset add wsl)" }

function Invoke-Wsl {
    $wslArgs = $args
    & { $ErrorActionPreference = 'Continue'; & wsl.exe @wslArgs 2>&1 } | ForEach-Object { if ("$_".Trim()) { "$_" } }
    if ($LASTEXITCODE -ne 0) { throw "wsl $($wslArgs -join ' ') failed ($LASTEXITCODE)" }
}

function Backup-Linux {
    $dir = Join-Path $HOME 'omawin-backups\linux'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $file = Join-Path $dir ('{0}-{1:yyyy-MM-dd-HHmm}.tar' -f $Distro, (Get-Date))
    "exporting $Distro to $file (a few minutes for a big one)"
    # A running distro exports as it is (check: consistency of files being written meanwhile).
    Invoke-Wsl --export $Distro $file
    if (-not (Test-Path $file) -or (Get-Item $file).Length -lt 1MB) { throw "the backup $file looks empty; nothing else was done" }
    "backed up: $file ($([int]((Get-Item $file).Length / 1MB)) MB)"
    $file
}

switch ($Action) {
    'stop' { Invoke-Wsl --terminate $Distro; Remove-Item "$State\linux-warm.pid" -ErrorAction SilentlyContinue; "$Distro stopped" }
    'backup' { Backup-Linux | Out-Host }
    'reset' {
        # Only a Linux omawin made (wsl/setup.sh writes /etc/omawin). Yours is never reset.
        & wsl.exe -d $Distro -u root -- test -f /etc/omawin
        if ($LASTEXITCODE -ne 0) { throw "$Distro wasn't set up by omawin (no /etc/omawin), so it's never reset; omawin linux backup still works" }
        $user = "$(& wsl.exe -d $Distro -u root -- sh -c "awk -F= '/^default=/{print `$2}' /etc/wsl.conf" 2>$null)".Trim()   # (check: 5.1 native quoting)
        Write-Host "This replaces $Distro with a fresh one. A backup is made first and kept." -ForegroundColor Yellow
        if ((Read-Host "Type $Distro to go ahead") -cne $Distro) { 'nothing changed'; return }
        $file = @(Backup-Linux)[-1]
        Invoke-Wsl --unregister $Distro
        & "$Repo\wsl\install.ps1" -Linux $pick.Name
        if ($user -and (Read-Host "Bring ~/src back from the backup? [Y/n]") -notmatch '^[nN]') {
            # wsl --export writes paths as ./home/<user>/... (check).
            $tar = '/mnt/' + $file.Substring(0, 1).ToLower() + ($file.Substring(2) -replace '\\', '/')
            Invoke-Wsl -d $Distro -u root --cd / -- tar -xpf $tar "./home/$user/src"
            '~/src is back'
        }
        "the backup stays at $file"
    }
}
```

The bar's Linux indicator (running, memory, click to stop) is a widget calling `omawin linux stop`; it belongs with the native bar work (Phase 1.6) and isn't sketched here.

---

## 3.11 Tests: `.github/workflows/linux.yml` (new)

```yaml
# wsl/setup.sh in containers: CI can't run WSL, but it can set up Arch and Ubuntu the way a new
# distro gets set up (setup.sh root, then user), and check the result (wsl/check.sh --ci). One run
# on a real PC per release covers what containers can't: systemd, interop, the keep-warm (🖥).
name: Linux setup

on:
  push:
    paths: ['wsl/**', 'bash/**', 'starship/**', '.github/workflows/linux.yml']
  pull_request:
    paths: ['wsl/**', 'bash/**', 'starship/**', '.github/workflows/linux.yml']
  workflow_dispatch:

jobs:
  setup:
    strategy:
      fail-fast: false
      matrix:
        image: ['archlinux:latest', 'ubuntu:24.04']
    runs-on: ubuntu-latest
    container: ${{ matrix.image }}
    steps:
      # No git in the images: checkout falls back to the REST API download, which is enough.
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - name: Container quirks
        run: |
          # pacman can't measure overlayfs free space (check)
          if [ -f /etc/arch-release ]; then sed -i 's/^CheckSpace/#CheckSpace/' /etc/pacman.conf; fi
          # The workspace must be readable by the user setup.sh makes.
          chmod -R a+rX "$GITHUB_WORKSPACE"
      - name: setup.sh root
        run: bash wsl/setup.sh root ci
      - name: setup.sh user
        run: runuser -l ci -c "bash '$GITHUB_WORKSPACE/wsl/setup.sh' user"
      - name: The shell starts with Omarchy's defaults and omawin's on top
        run: |
          runuser -l ci -c 'bash -ic "
            set -e
            type starship zoxide fzf eza > /dev/null
            [ \"\$OMARCHY_PATH\" = /usr/share/omawin/omarchy ]
            alias ls | grep -q eza
            [ \"\$EDITOR\" = nvim ]
          "' 2>&1 | tail -20
      - name: Run it again (safe to run twice)
        run: |
          bash wsl/setup.sh root ci
          runuser -l ci -c "bash '$GITHUB_WORKSPACE/wsl/setup.sh' user"
      - name: wsl/check.sh
        run: bash wsl/check.sh ci --ci
```

In CI `/etc/pacman.conf` gets Omarchy's (which has `CheckSpace` uncommented): the quirk step must run *after* `setup.sh root` too, or `arch_repos` must keep `CheckSpace` out when `OMAWIN_CI` is set *(check)*. The 3B prompt functions can't be checked in a container without a terminal; the bash -ic step covers that they load.

---

## 3B. Always know which side you're on

### 1. The background tints by side: `themes/render.ps1`

After the palette fallbacks, before any template is rendered (so templates may use `{{ linux_background }}` too, e.g. a later `theme-linux.ghostty` for the noctty fork):

```powershell
# --- which side you're on (roadmap 3B): Linux's background ---------------------------------------
# The theme's background mixed a little toward its accent, so Linux and PowerShell windows look
# different in every theme. Mixed less where the text would lose contrast (light themes darken
# toward their accent); toward the foreground when the accent is too close to the background to show.
function ConvertFrom-HexColor([string]$Hex) {
    $h = $Hex.Trim().TrimStart('#')
    if ($h.Length -eq 3) { $h = -join ($h.ToCharArray() | ForEach-Object { "$_$_" }) }
    if ($h.Length -lt 6) { throw "not a colour: $Hex" }
    , @(0, 2, 4 | ForEach-Object { [Convert]::ToInt32($h.Substring($_, 2), 16) })
}
function ConvertTo-HexColor([double[]]$Rgb) {
    '#' + (($Rgb | ForEach-Object { '{0:x2}' -f [int][Math]::Round([Math]::Max(0, [Math]::Min(255, $_))) }) -join '')
}
function Get-MixedColor([string]$From, [string]$To, [double]$Amount) {
    $a = ConvertFrom-HexColor $From
    $b = ConvertFrom-HexColor $To
    ConvertTo-HexColor @(0, 1, 2 | ForEach-Object { $a[$_] + ($b[$_] - $a[$_]) * $Amount })
}
# WCAG relative luminance and contrast ratio.
function Get-Luminance([string]$Hex) {
    $rgb = ConvertFrom-HexColor $Hex
    $l = @($rgb | ForEach-Object { $v = $_ / 255; if ($v -le 0.03928) { $v / 12.92 } else { [Math]::Pow(($v + 0.055) / 1.055, 2.4) } })
    0.2126 * $l[0] + 0.7152 * $l[1] + 0.0722 * $l[2]
}
function Get-Contrast([string]$A, [string]$B) {
    $x = Get-Luminance $A
    $y = Get-Luminance $B
    ([Math]::Max($x, $y) + 0.05) / ([Math]::Min($x, $y) + 0.05)
}
function Get-LinuxBackground([string]$Background, [string]$Accent, [string]$Foreground) {
    $toward = if ((Get-Contrast $Background $Accent) -lt 1.5) { $Foreground } else { $Accent }
    # The text keeps at least 90% of its contrast (capped at 7:1, WCAG AAA, so high-contrast themes
    # still get a visible tint).
    $floor = [Math]::Min((Get-Contrast $Foreground $Background), 7.0) * 0.9
    foreach ($amount in 0.08, 0.07, 0.06, 0.05, 0.04) {
        $tint = Get-MixedColor $Background $toward $amount
        if ((Get-Contrast $Foreground $tint) -ge $floor) { return $tint }
    }
    Get-MixedColor $Background $toward 0.03
}
$c['linux_background'] = Get-LinuxBackground $c['background'] $c['accent'] $c['foreground']
```

And after Windows Terminal's fragment:

```powershell
# Linux's shell reads this (bash/bashrc, through /mnt/c) and sets its terminal's background (OSC 11)
# at the next prompt. LF line endings: bash would keep a CR in every value.
$linuxSh = @(
    "# Written by themes/render.ps1 for `"$Name`"; don't edit (replaced on every switch). bash/bashrc reads it in Linux.",
    "OMAWIN_THEME='$Name'",
    "OMAWIN_THEME_MODE='$($c['mode'])'",
    "OMAWIN_LINUX_BG='$($c['linux_background'])'",
    "OMAWIN_WINDOWS_BG='$($c['background'])'"
) -join "`n"
Write-Text "$State\theme-linux.sh" ($linuxSh + "`n")
```

Check the result visually in a dark (tokyo-night), a light (catppuccin-latte, flexoki-light) and a high-contrast (white, vantablack) theme: the "Done when" screenshot. If 8% is too subtle on the PC, raise the list's first values; the contrast floor keeps text readable either way.

### 2-3. The prompt and the title: `bash/bashrc` (appended)

```bash
# --- which side you're on (docs/roadmap.md 3B), Windows' SSH agent and `open` --------------------
if [[ -n ${WSL_DISTRO_NAME:-} ]]; then
  export OMAWIN_SIDE=linux
  omawin_os=linux
  while IFS='=' read -r key value; do [[ $key == ID ]] && omawin_os=${value//\"/}; done < /etc/os-release
  # starship's side mark (starship.toml, env_var.OMAWIN_LINUX): the distro's Nerd Font logo.
  case $omawin_os in
    arch) export OMAWIN_LINUX=$'\uf303' ;;
    ubuntu) export OMAWIN_LINUX=$'\uf31b' ;;
    *) export OMAWIN_LINUX=$'\uf17c' ;;
  esac

  # The theme Windows rendered (themes/render.ps1 -> theme-linux.sh): read again when it's newer
  # than what this shell last saw, which costs one stat over /mnt/c per prompt (measure).
  omawin_theme_file=${OMAWIN_STATE:+$OMAWIN_STATE/theme-linux.sh}
  omawin_theme_seen=${XDG_RUNTIME_DIR:-/tmp}/omawin-theme.$$
  trap 'rm -f "$omawin_theme_seen"' EXIT
  _omawin_theme() {
    [[ -n $omawin_theme_file && $omawin_theme_file -nt $omawin_theme_seen ]] || return 0
    source "$omawin_theme_file"
    : > "$omawin_theme_seen"
    # Linux's own copies (btop), once per switch rather than once per shell.
    [[ -f ~/.config/omawin/theme && $(< ~/.config/omawin/theme) == "$OMAWIN_THEME" ]] || (omawin-theme-sync > /dev/null 2>&1 &)
  }

  # Each prompt: the folder (OSC 7: Win+Enter opens the next terminal here), the background (OSC 11:
  # Linux's tint, again every prompt since some TUIs reset it on exit) and the title (OSC 2:
  # "arch · ~/src/app", which the bar and Windows Terminal's tabs show).
  _omawin_prompt() {
    _omawin_theme
    local here=${PWD/#$HOME/\~} url=${PWD//%/%25}
    url=${url// /%20}
    printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$url"
    [[ -n ${OMAWIN_LINUX_BG:-} ]] && printf '\e]11;%s\e\\' "$OMAWIN_LINUX_BG"
    printf '\e]2;%s \u00b7 %s\a' "$omawin_os" "$here"
  }
  # Omarchy's init set starship up just before this; starship runs this before each prompt.
  if [[ $(type -t starship_precmd) == function ]]; then
    starship_precmd_user_func=_omawin_prompt
  else
    PROMPT_COMMAND="_omawin_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}"
  fi

  # Windows' SSH agent (or 1Password's), bridged by the omawin-ssh-agent user service (setup.sh).
  omawin_agent=${XDG_RUNTIME_DIR:-/run/user/$UID}/omawin-ssh-agent.sock
  [[ -z ${SSH_AUTH_SOCK:-} && -S $omawin_agent ]] && export SSH_AUTH_SOCK=$omawin_agent

  # Omarchy's `open` calls xdg-open, which has nothing to open with here: Windows opens it
  # (a Windows reason to differ).
  open() { local a; for a in "$@"; do if [[ -e $a ]]; then explorer "$a"; else browser "$a"; fi; done; }

  # Ubuntu: Omarchy's init looks for fzf's key bindings where Arch keeps them.
  [[ $(type -t __fzf_history__) == function ]] || eval "$(fzf --bash 2> /dev/null)"
else
  # Git Bash is the Windows side.
  export OMAWIN_SIDE=windows OMAWIN_WINDOWS=$'\uf17a'
fi
```

OSC 11 through herdr: open question in the roadmap's table. If herdr doesn't pass a pane's OSC 11 through, herdr's own pane background per side (its config) or a small herdr patch; the title and prompt still say which side.

### 2. `starship/starship.toml`

Starship's `os` module has one `style` for every OS, so it can't colour by side. Two `env_var` modules can: each is shown only where its variable is set (the bashrc in Linux, the PowerShell profile and Git Bash on Windows), each with its own colour, and nothing is spawned per prompt (which matters on Windows). The theme decides the actual colours (ANSI names).

```diff
-format = "[$directory$git_branch$git_status]($style)$character"
+# The side mark first (Arch / Ubuntu / Windows logo, coloured by side: docs/roadmap.md 3B).
+format = "${env_var.OMAWIN_LINUX}${env_var.OMAWIN_WINDOWS}[$directory$git_branch$git_status]($style)$character"
+
+[env_var.OMAWIN_LINUX]
+format = "[$env_value ]($style)"
+style = "bold green"
+
+[env_var.OMAWIN_WINDOWS]
+format = "[$env_value ]($style)"
+style = "bold blue"
```

The prompt character keeps the theme's cyan on both sides (starship has no per-variable style for `character`) *(check: if the mark alone isn't enough, the bashrc can set `STARSHIP_CONFIG` to a Linux copy rendered by `render.ps1` with a different `success_symbol` colour)*.

### 3. `powershell/Microsoft.PowerShell_profile.ps1`

```diff
+# Which side you're on (docs/roadmap.md 3B): starship's side mark (env_var.OMAWIN_WINDOWS) and
+# the title prefix.
+$env:OMAWIN_SIDE = 'windows'
+$env:OMAWIN_WINDOWS = [string][char]0xf17a
 function Invoke-Starship-PreCommand {
     ...
     $leaf = Split-Path -Leaf $PWD
-    $host.UI.RawUI.WindowTitle = $leaf  # tab title = current folder
+    # Tab title "pwsh · <folder>"; omawin's TerminalHere takes the prefix off before looking the
+    # folder name up in dirs\ (bin/omawin.cs StripSide).
+    $host.UI.RawUI.WindowTitle = "pwsh $([char]0xb7) $leaf"
```

### 4. The window border by side

GlazeWM fork work (window rules with a per-window border colour matching the title prefix, on top of `window_effects.*.border`); belongs with the fork items in [01-snappy.md](01-snappy.md) and isn't sketched here.

---

## Order

1. `.gitattributes`, `wsl/omarchy-skip.txt`, `setup.sh` (3.2, 3.3, 3.6), `wsl/check.sh`, the CI workflow: green in both containers before anything touches a PC.
2. `render.ps1` + bashrc + starship + profile (3B 1-3): one screenshot pair per theme kind.
3. `omawin.cs` (3.4) with `defaults.txt`; `WarmLinux` (3.5) and its measurement; then flip the default.
4. 3.7 index, 3.8 btop, 3.9 SSH, 3.10 backup/reset, `check.ps1`.
