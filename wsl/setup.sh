#!/bin/bash
# omawin's Linux in WSL (the wsl preset: wsl/install.ps1 runs this): Omarchy's terminal layer, the
# way an Omarchy user has it, plus what's omawin's (its Neovim config, herdr, the agents, the theme,
# the bridges to Windows). Arch (the default) or Ubuntu, told apart by /etc/os-release. Run from
# the repo under /mnt/c, in parts:
#
#   setup.sh root <user>            as root: packages, the Linux user, /etc/wsl.conf, Docker
#   setup.sh user [agent ...]       as the user: links, herdr, the agents, then refresh
#   setup.sh refresh                as the user: the shell blocks, omawin's copies (wrappers, units,
#                                   Omarchy's configs) and Linux's migrations
#   setup.sh update                 as root: packages (Arch or Ubuntu), then refresh as the user
#                                   (omawin update)
#
# Arch takes its packages from Omarchy's repositories, as Omarchy itself does: Omarchy's own
# ([omarchy]: herdr, its keyring) and its copy of Arch's, which moves forward a tested set at a
# time (the stable channel), so an update doesn't break things. Which packages: Omarchy's own base
# list, at a pinned commit, less what needs a desktop (wsl/omarchy-skip.txt). No AUR.
#
# Windows' folders come in through WSLENV (wsl/install.ps1, install/other.ps1), already as Linux
# paths: OMAWIN_STATE (%LOCALAPPDATA%\launcher), OMAWIN_ACCOUNT (this account's folder,
# docs/work.md), USERPROFILE and LOCALAPPDATA. Without them (CI's containers) what needs Windows
# is skipped.
#
# Safe to run again: what's there is kept, what's missing is added. A distro omawin didn't set up
# (no /etc/omawin) is never updated.

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
arch="$(uname -m)"
distro="$(. /etc/os-release && echo "$ID")"
say() { printf '  %s\n' "$*"; }

# omawin's own root-owned folder in Linux: Omarchy's files at the pin, the wanted package list.
omawin_share=/usr/share/omawin

# Omarchy's files Linux uses, at a pinned commit of the fork: its base package list, its bash
# defaults (default/bash) and the configs of terminal tools omawin doesn't configure itself. Bumped
# by the "following Omarchy" sync (plans/roadmap.md), after CI's Linux setup passes with it.
omarchy_repo=https://github.com/mjveilleux/omarchy
omarchy_commit=7f4b1dd22cf256c074e71c7a2f56dc387033768c
omarchy_dir=$omawin_share/omarchy

# A GitHub project's latest release asset, by its name: GitHub's own redirect, rather than its API,
# which allows few unauthenticated calls (a shared address soon runs out).
latest_asset() { printf 'https://github.com/%s/releases/latest/download/%s' "$1" "$2"; }

# Names in a package list file: comments and blanks out, CRLF tolerated (a checkout on Windows).
list_names() { tr -d '\r' < "$1" | sed 's/#.*//' | awk 'NF { print $1 }' | sort -u; }

# --- Omarchy's files -----------------------------------------------------------------------------
# A sparse, blob-less fetch of the one commit: Omarchy's themes carry big pictures never wanted here.
omarchy_files() {
  [[ $(git -C "$omarchy_dir" rev-parse HEAD 2> /dev/null) == "$omarchy_commit" ]] && return 0
  say "Omarchy's package list and shell defaults (${omarchy_commit:0:7})"
  local new="$omarchy_dir.new"
  rm -rf "$new"
  mkdir -p "$new"
  git -C "$new" init -q
  git -C "$new" remote add origin "$omarchy_repo"
  git -C "$new" sparse-checkout set --no-cone /install/omarchy-base.packages /default/bash/ \
    /config/btop/ /config/lazygit/ /config/tmux/
  git -C "$new" fetch -q --depth 1 --filter=blob:none origin "$omarchy_commit"
  git -C "$new" checkout -q FETCH_HEAD
  rm -rf "$omarchy_dir"
  mv "$new" "$omarchy_dir"
}

# Omarchy's base list less wsl/omarchy-skip.txt.
omarchy_packages() {
  comm -23 <(list_names "$omarchy_dir/install/omarchy-base.packages") <(list_names "$repo/wsl/omarchy-skip.txt")
}

# --- Arch ----------------------------------------------------------------------------------------
# Omarchy's signing key (its bin/omarchy-update-keyring); omarchy-keyring then keeps it current.
omarchy_key=40DFB630FF42BCFFB047046CF0134EE680CAC571

# pacman's stderr without its "already installed" lines (--needed says one per package).
quiet_skips() { grep -v ' is up to date -- skipping$' >&2 || true; }

# What omawin adds on top of Omarchy's list, each for a reason Omarchy doesn't have:
#   nodejs npm python python-pip   mason's npm and pip tools (prettier, sqlfluff) in Neovim
#   github-cli                     Windows' core has gh: the same on both sides
#   openssh wget zip which sudo    what a WSL image may lack and setup.sh or the shell uses
arch_extras=(nodejs npm python python-pip github-cli openssh wget zip which sudo curl)

arch_repos() {
  # pacman's own keys, with the local signing key that vouches for the others: the image makes
  # them at its first start (/usr/lib/wsl/first-setup.sh), which may not have happened yet (and a
  # container's image ships without the signing key).
  if [[ -z $(gpg --homedir /etc/pacman.d/gnupg --list-secret-keys 2> /dev/null) ]]; then
    say "pacman's keys"
    pacman-key --init > /dev/null 2>&1
    pacman-key --populate archlinux > /dev/null 2>&1
  fi
  # Omarchy's repositories (its default/pacman/pacman-stable.conf and mirrorlist-stable, less
  # multilib, which WSL has no use for). Arch's own files are kept as *.arch.
  if ! grep -q '^\[omarchy\]' /etc/pacman.conf; then
    say "Omarchy's package repositories (stable)"
    [[ -e /etc/pacman.conf.arch ]] || cp /etc/pacman.conf /etc/pacman.conf.arch
    [[ -e /etc/pacman.d/mirrorlist.arch ]] || cp /etc/pacman.d/mirrorlist /etc/pacman.d/mirrorlist.arch
    # What the image set for the machine it runs on is kept: a container's image turns off the
    # download sandbox and the free-space check (neither works there) and leaves docs out.
    local machine checkspace=CheckSpace
    machine="$(grep -E '^[[:space:]]*(DisableSandbox[A-Za-z]*|NoExtract)\b' /etc/pacman.conf.arch || true)"
    grep -qE '^[[:space:]]*CheckSpace' /etc/pacman.conf.arch || checkspace='#CheckSpace'
    [[ -z $machine ]] || machine="# The image's own (/etc/pacman.conf.arch):"$'\n'"$machine"$'\n'
    cat > /etc/pacman.conf << EOF
# Written by omawin (wsl/setup.sh), after Omarchy's default/pacman/pacman-stable.conf.
# Arch's own is /etc/pacman.conf.arch.
[options]
HoldPkg = pacman glibc
Architecture = auto
Color
VerbosePkgLists
NoProgressBar
$checkspace
ParallelDownloads = 5
DownloadUser = alpm
SigLevel = Required DatabaseOptional
LocalFileSigLevel = Optional

${machine}[omarchy]
Server = https://pkgs.omarchy.org/stable/\$arch

[core]
Include = /etc/pacman.d/mirrorlist

[extra]
Include = /etc/pacman.d/mirrorlist
EOF
    cat > /etc/pacman.d/mirrorlist << 'EOF'
# Omarchy's copy of Arch's repositories, the stable channel (written by omawin, wsl/setup.sh).
Server = https://stable-mirror.omarchy.org/$repo/os/$arch
EOF
  fi
  if ! pacman-key --list-keys "$omarchy_key" > /dev/null 2>&1; then
    say "Omarchy's signing key"
    # Over HTTPS with curl rather than gpg's keyserver protocol: curl goes through a proxy (a
    # company network's, which WSL's autoProxy passes on), gpg's dirmngr doesn't.
    local key
    key="$(mktemp)"
    if ! curl -fsSL "https://keys.openpgp.org/vks/v1/by-fingerprint/$omarchy_key" -o "$key" ||
      ! pacman-key --add "$key" > /dev/null 2>&1; then
      rm -f "$key"
      echo "couldn't fetch Omarchy's signing key ($omarchy_key) from keys.openpgp.org; is the network up?" >&2
      exit 1
    fi
    rm -f "$key"
    pacman-key --lsign-key "$omarchy_key" > /dev/null 2>&1
  fi
}

# pacman -S of every name it can: a name the repositories don't have (renamed upstream, or only in
# the AUR) would stop the whole transaction, so it's said, left out, and the rest goes in. Prints
# the names that went in (or were there already).
pacman_wanted() {
  local names=("$@") out missing
  while ((${#names[@]})); do
    if out="$(pacman -S --needed --noconfirm "${names[@]}" 2>&1 > /dev/null)"; then
      quiet_skips <<< "$out"
      printf '%s\n' "${names[@]}"
      return 0
    fi
    missing="$(sed -n 's/^error: target not found: //p' <<< "$out" | sort -u)"
    if [[ -z $missing ]]; then
      printf '%s\n' "$out" >&2
      return 1
    fi
    say "not in the repositories, left out: ${missing//$'\n'/ }" >&2
    mapfile -t names < <(comm -23 <(printf '%s\n' "${names[@]}" | sort -u) <(printf '%s\n' "$missing"))
  done
}

arch_packages() {
  omarchy_files
  local wanted
  mapfile -t wanted < <({ omarchy_packages; printf '%s\n' "${arch_extras[@]}"; } | sort -u)
  # What check.sh compares against: the names that went in.
  pacman_wanted "${wanted[@]}" > "$omawin_share/packages.txt.new"
  mv "$omawin_share/packages.txt.new" "$omawin_share/packages.txt"
  desktop_guard
}

# A dependency that pulls a desktop in (roadmap 3.2's risk): said, never removed.
desktop_guard() {
  local pulled
  pulled="$(pacman -Qq hyprland quickshell sddm uwsm plymouth 2> /dev/null || true)"
  [[ -z $pulled ]] || say "warning: desktop packages came in as dependencies: ${pulled//$'\n'/ } (pactree -r <name> says why)"
}

arch_root() {
  arch_repos
  say "packages (Omarchy's terminal layer)"
  # The keyrings first (an image older than a maintainer's new key can't check the rest), and git
  # for Omarchy's files.
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring git > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm > /dev/null
  arch_packages
}

arch_update() {
  arch_repos
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm
  # What a newer pin added since.
  arch_packages
}

# --- Ubuntu --------------------------------------------------------------------------------------
# Omarchy's terminal tools by Ubuntu's names where 24.04's archive has them (and they're new
# enough); the rest from mise, as the user (ubuntu_mise_tools).
ubuntu_apt=(build-essential sudo curl wget git unzip zip gpg ca-certificates ripgrep fd-find jq bat
  eza zoxide gh btop tmux socat inotify-tools less man-db plocate whois bash-completion imagemagick
  ffmpeg nodejs npm python3 python3-venv python3-pip)
# Too old or missing in 24.04: fzf (0.44; `fzf --bash` needs 0.48), starship, lazygit, fastfetch,
# gum, usage.
ubuntu_mise=(fzf starship lazygit fastfetch gum usage)

ubuntu_root() {
  local nvim_arch ts_arch
  case "$arch" in
    x86_64) nvim_arch=x86_64 ts_arch=x64 ;;
    aarch64) nvim_arch=arm64 ts_arch=arm64 ;;
    *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
  esac
  export DEBIAN_FRONTEND=noninteractive
  say "packages"
  apt-get update -qq
  apt-get install -y -qq "${ubuntu_apt[@]}" > /dev/null
  # Ubuntu names these fdfind and batcat
  [[ -e /usr/local/bin/fd ]] || ln -s "$(command -v fdfind)" /usr/local/bin/fd
  [[ -e /usr/local/bin/bat ]] || ln -s "$(command -v batcat)" /usr/local/bin/bat
  ubuntu_mise_repo
  ubuntu_docker
  omarchy_files

  # Neovim from its releases: the same version as Windows' (winget's), newer than Ubuntu's.
  if ! /usr/local/bin/nvim --version > /dev/null 2>&1 || [[ -n ${OMAWIN_UPDATE:-} ]]; then
    say "Neovim"
    curl -fsSL "https://github.com/neovim/neovim/releases/latest/download/nvim-linux-$nvim_arch.tar.gz" | tar -xz -C /opt
    rm -rf /opt/nvim && mv "/opt/nvim-linux-$nvim_arch" /opt/nvim
    ln -sf /opt/nvim/bin/nvim /usr/local/bin/nvim
  fi
  # tree-sitter's CLI builds the parsers (LazyVim's nvim-treesitter); Ubuntu's is too old.
  if ! command -v tree-sitter > /dev/null || [[ -n ${OMAWIN_UPDATE:-} ]]; then
    say "tree-sitter"
    curl -fsSL "$(latest_asset tree-sitter/tree-sitter "tree-sitter-linux-$ts_arch.gz")" | gunzip > /usr/local/bin/tree-sitter.new
    mv /usr/local/bin/tree-sitter.new /usr/local/bin/tree-sitter
    chmod +x /usr/local/bin/tree-sitter
  fi
}

# mise from its apt repository (mise.jdx.dev/installing-mise.html), as Omarchy has it from pacman.
ubuntu_mise_repo() {
  command -v mise > /dev/null && return 0
  say "mise"
  install -dm 755 /etc/apt/keyrings
  curl -fsSL https://mise.jdx.dev/gpg-key.pub | gpg --dearmor --yes -o /etc/apt/keyrings/mise-archive-keyring.gpg
  echo "deb [signed-by=/etc/apt/keyrings/mise-archive-keyring.gpg arch=$(dpkg --print-architecture)] https://mise.jdx.dev/deb stable main" \
    > /etc/apt/sources.list.d/mise.list
  apt-get update -qq
  apt-get install -y -qq mise > /dev/null
}

# Docker Engine from Docker's apt repository (docs.docker.com/engine/install/ubuntu): Omarchy's
# docker, docker-buildx and docker-compose.
ubuntu_docker() {
  command -v docker > /dev/null && return 0
  say "Docker Engine"
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

# As the user: what apt lacks, from mise (global tools, on the PATH through mise's shims). Each on
# its own, so one that fails doesn't keep the others out.
ubuntu_mise_tools() {
  command -v mise > /dev/null || return 0
  local tool
  for tool in "${ubuntu_mise[@]}"; do
    mise which "$tool" > /dev/null 2>&1 && continue
    say "$tool (mise)"
    mise use -g -q "$tool@latest" > /dev/null 2>&1 || say "mise couldn't install $tool; omawin update tries again"
  done
}

# --- the system: wsl.conf, Docker, the user --------------------------------------------------------
# key=value under [section] of an ini file: replaced where it is, else added at the end of its
# section, else in a new section at the end.
ini_set() {
  local file="$1" section="$2" key="$3" value="$4"
  touch "$file"
  awk -v s="[$section]" -v k="$key" -v v="$value" '
    function trim(x) { sub(/^[ \t]+/, "", x); sub(/[ \t\r]+$/, "", x); return x }
    trim($0) == s { insec = 1; seen = 1; print; next }
    /^[ \t]*\[/ { if (insec && !done) { print k "=" v; done = 1 } insec = 0 }
    insec && index($0, "=") && trim(substr($0, 1, index($0, "=") - 1)) == k { if (!done) { print k "=" v; done = 1 } next }
    { print }
    END { if (!done) { if (!insec) { if (NR) print ""; print s } print k "=" v } }
  ' "$file" > "$file.new" && cat "$file.new" > "$file" && rm -f "$file.new"
}

# WSL's settings for this distro. Each can be overridden by your account folder's wsl/wsl.conf
# (docs/work.md): the same ini format, applied after these.
wsl_conf() {
  local user="$1" file="${2:-/etc/wsl.conf}"
  ini_set "$file" boot systemd true                # Docker and the timers below run under systemd
  ini_set "$file" interop appendWindowsPath false  # Linux's tools win in Linux; ~/.local/bin wraps what's needed from Windows
  ini_set "$file" user default "$user"             # the user WSL starts in
  local over="${OMAWIN_ACCOUNT:-}/wsl/wsl.conf" section="" line key value
  [[ -n ${OMAWIN_ACCOUNT:-} && -f $over ]] || return 0
  while IFS= read -r line || [[ -n $line ]]; do
    line="${line%$'\r'}"
    case "$line" in
      \[*\]) section="${line#[}" section="${section%]}" ;;
      '' | \#* | \;*) ;;
      *=*)
        key="$(xargs <<< "${line%%=*}")" value="$(xargs <<< "${line#*=}")"
        [[ -n $section && -n $key ]] && ini_set "$file" "$section" "$key" "$value" ;;
    esac
  done < "$over"
}

# The distro's default user (wsl.conf [user] default).
wsl_user() {
  awk -F= '/^[ \t]*\[/ { u = ($0 ~ /^[ \t]*\[user\]/); next }
    u && $1 ~ /^[ \t]*default[ \t]*$/ { gsub(/[ \t\r]/, "", $2); print $2 }' "${1:-/etc/wsl.conf}" 2> /dev/null
}

# Docker Engine for the user. Omarchy keeps its user out of the docker group (install/config/
# docker.sh: the group is root without a password). Here root is already that (wsl -u root needs
# no password, and the user has sudo without one), so the group guards nothing and `docker` just
# works. Started on its first use, as Omarchy does (docker.socket).
docker_root() {
  local user="$1"
  getent group docker > /dev/null || groupadd -r docker
  id -nG "$user" | tr ' ' '\n' | grep -qx docker || usermod -aG docker "$user"
  # Enabling only writes links, so it works before systemd runs (the distro's first start).
  systemctl enable docker.socket > /dev/null 2>&1 || true
}

# The user's systemd manager runs from the distro's start, not only while a terminal is open: the
# file index and the SSH agent bridge (user units, refresh_part) are up when Windows asks.
linger() { mkdir -p /var/lib/systemd/linger && touch "/var/lib/systemd/linger/$1"; }

root_part() {
  local user="$1"
  mkdir -p "$omawin_share"
  case "$distro" in
    arch) arch_root ;;
    ubuntu) ubuntu_root ;;
    *) echo "omawin sets up Arch or Ubuntu, not '$distro'" >&2; exit 1 ;;
  esac

  # The Linux user, made WSL's default (it applies from WSL's next start).
  if ! id "$user" > /dev/null 2>&1; then
    say "user $user"
    useradd -m -s /bin/bash -G "$([[ $distro == arch ]] && echo wheel || echo sudo)" "$user"
  fi
  # sudo without a password: Windows reaches root here without one anyway (wsl -u root), so a
  # password would guard nothing, and the install never stops to ask for one.
  local sudoers=/etc/sudoers.d/omawin
  if [[ ! -f $sudoers ]] || ! grep -q "^$user " "$sudoers"; then
    printf '%s ALL=(ALL:ALL) NOPASSWD: ALL\n' "$user" > "$sudoers.new"
    chmod 440 "$sudoers.new"
    if visudo -cqf "$sudoers.new"; then mv "$sudoers.new" "$sudoers"; else rm -f "$sudoers.new"; echo "sudoers line refused" >&2; fi
  fi
  docker_root "$user"
  linger "$user"
  # omawin set this distro up: wsl/install.ps1 and `omawin update` leave one without it alone
  # (it's yours), and `omawin linux reset` refuses it.
  [[ -f /etc/omawin ]] || printf 'Set up by omawin (wsl/setup.sh), %s.\n' "$(date +%Y-%m-%d)" > /etc/omawin
  wsl_conf "$user"
}

update_part() {
  # Only a Linux omawin set up: someone's own distro is theirs.
  [[ -f /etc/omawin ]] || { say "not omawin's Linux: left alone"; return 0; }
  mkdir -p "$omawin_share"
  case "$distro" in
    arch) arch_update ;;
    ubuntu) ubuntu_update ;;
    *) say "nothing to update in $distro"; return 0 ;;
  esac
  local user
  user="$(wsl_user /etc/wsl.conf)"
  [[ -n $user ]] && id "$user" > /dev/null 2>&1 || return 0
  # What a newer omawin sets up as root, for a Linux set up before it (wsl.conf applies from the
  # distro's next start).
  docker_root "$user"
  linger "$user"
  wsl_conf "$user"
  # Windows' folders, passed on (runuser -l starts from a clean environment).
  runuser -l "$user" -c "$(printf 'OMAWIN_STATE=%q OMAWIN_ACCOUNT=%q USERPROFILE=%q LOCALAPPDATA=%q bash %q refresh' \
    "${OMAWIN_STATE:-}" "${OMAWIN_ACCOUNT:-}" "${USERPROFILE:-}" "${LOCALAPPDATA:-}" "$repo/wsl/setup.sh")"
}

# --- the user ------------------------------------------------------------------------------------
# A block of lines between markers in a file, replaced in place each run.
put_block() {
  local file="$1" text="$2"
  touch "$file"
  sed -i '/^# >>> omawin >>>$/,/^# <<< omawin <<<$/d' "$file"
  printf '# >>> omawin >>>\n%s\n# <<< omawin <<<\n' "$text" >> "$file"
}

# The shell: the paths for login shells, then Omarchy's defaults with omawin's on top for
# interactive ones. Rewritten on every refresh, so a moved file or a new variable reaches an
# existing Linux with the next `omawin update`.
shell_blocks() {
  local state="${OMAWIN_STATE:-}"
  # Login shells too (herdr's tabs and wsl.exe's `bash -lc`): bash reads ~/.bash_profile instead of
  # ~/.profile when there is one (Arch makes one).
  local login=~/.profile
  [[ -f ~/.bash_profile ]] && login=~/.bash_profile
  put_block "$login" "export OMAWIN=$(printf %q "$repo") OMAWIN_STATE=$(printf %q "$state")
export STARSHIP_CONFIG=\"\$OMAWIN/config/starship/starship.toml\" HERDR_CONFIG_PATH=\"\$OMAWIN/config/herdr/config.toml\"
# Omarchy's env-bootstrap does these for its login shells; its file isn't at /usr/share/omarchy here.
case \":\$PATH:\" in *\":\$HOME/.local/share/mise/shims:\"*) ;; *) PATH=\"\$PATH:\$HOME/.local/share/mise/shims\" ;; esac
case \":\$PATH:\" in *\":\$HOME/.local/bin:\"*) ;; *) PATH=\"\$HOME/.local/bin:\$PATH\" ;; esac"

  # Interactive shells: Omarchy's defaults (default/bash/rc), then omawin's on top
  # (config/bash/bashrc). The repo is named here too: ~/.profile loads ~/.bashrc before its own
  # block has run. EDITOR and BROWSER first: Omarchy's envs default them to its launchers, which
  # aren't here.
  put_block ~/.bashrc "export OMAWIN=$(printf %q "$repo") OMAWIN_STATE=$(printf %q "$state")
export STARSHIP_CONFIG=\"\$OMAWIN/config/starship/starship.toml\" HERDR_CONFIG_PATH=\"\$OMAWIN/config/herdr/config.toml\"
export EDITOR=nvim BROWSER=\"\$HOME/.local/bin/browser\"
export OMARCHY_PATH=$omarchy_dir
[[ -f \$OMARCHY_PATH/default/bash/rc ]] && . \"\$OMARCHY_PATH/default/bash/rc\"
[[ -f \$OMAWIN/config/bash/bashrc ]] && . \"\$OMAWIN/config/bash/bashrc\""
}

# A small script in ~/.local/bin, replaced each run.
put_wrapper() { cat > ~/.local/bin/"$1" && chmod +x ~/.local/bin/"$1"; }

# Windows' programs people type, brought back: interop's appendWindowsPath is off (wsl_conf), so
# Windows' PATH isn't Linux's. Paths are baked in from WSLENV at each refresh.
wrappers() {
  local home_win="${USERPROFILE:-}" local_win="${LOCALAPPDATA:-}"
  put_wrapper explorer << 'EOF'
#!/bin/bash
# File Explorer on a folder or file, Linux paths too (\\wsl.localhost\...). explorer.exe exits 1
# even when it worked. (Written by omawin's wsl/setup.sh.)
/mnt/c/Windows/explorer.exe "$(wslpath -w "${1:-.}")"
exit 0
EOF
  put_wrapper browser << 'EOF'
#!/bin/bash
# A URL in Windows' default browser ($BROWSER: gh, Omarchy's `open`). (Written by omawin's wsl/setup.sh.)
/mnt/c/Windows/explorer.exe "$1"
exit 0
EOF
  put_wrapper clip << 'EOF'
#!/bin/bash
# Text into Windows' clipboard. clip.exe reads UTF-16 when the text starts with its byte-order
# mark, so accents and emoji survive. (Written by omawin's wsl/setup.sh.)
{ printf '\xff\xfe'; iconv -f utf-8 -t utf-16le; } | /mnt/c/Windows/System32/clip.exe
EOF
  if [[ -n $home_win ]]; then
    put_wrapper omawin << EOF
#!/bin/bash
# omawin's commands from Linux (omawin theme set, omawin launch editor ...), through interop.
# (Written by omawin's wsl/setup.sh.)
exec $(printf %q "$home_win/.local/bin/omawin.exe") "\$@"
EOF
  fi
  put_wrapper code << EOF
#!/bin/bash
# VS Code from Linux: Windows' bin/code is a shell script that opens the folder over its WSL
# extension. (Written by omawin's wsl/setup.sh.)
for c in $(printf %q "$local_win/Programs/Microsoft VS Code/bin/code") "/mnt/c/Program Files/Microsoft VS Code/bin/code"; do
  [[ -x \$c ]] && exec "\$c" "\$@"
done
echo "code: VS Code isn't installed on Windows (omawin default editor code)" >&2
exit 1
EOF
}

# omawin's own scripts for Linux (wsl/bin), into ~/.local/bin.
linux_tools() {
  local tool
  for tool in "$repo"/wsl/bin/*; do
    [[ -f $tool ]] || continue
    tr -d '\r' < "$tool" > ~/.local/bin/"${tool##*/}"
    chmod 755 ~/.local/bin/"${tool##*/}"
  done
}

# Omarchy's configs for terminal tools omawin doesn't configure itself, copied once (its
# refresh-config pattern): a file that's there, yours or copied before, is kept.
omarchy_configs() {
  local from="$omarchy_dir/config" tool
  for tool in btop lazygit tmux; do
    [[ -d $from/$tool && ! -e ~/.config/$tool ]] || continue
    cp -r "$from/$tool" ~/.config/"$tool"
    # btop, a Windows reason to differ: Omarchy paints the theme's background; here the terminal's
    # own shows through, so Linux's tint (roadmap 3B) stays visible, as btop4win does on Windows
    # (themes/set-theme.ps1).
    if [[ $tool == btop ]]; then
      sed -i 's/^theme_background = .*/theme_background = false/' ~/.config/btop/btop.conf
    fi
  done
}

# systemd user units: the finder's file index (wsl/bin/omawin-index-files) on start, every 10
# minutes and when ~/src itself changes (a clone, a new project). Enabled by links rather than `systemctl --user enable`, which needs the user's manager, which a
# `wsl -u` session may not reach yet.
user_units() {
  local units=~/.config/systemd/user
  mkdir -p "$units/timers.target.wants" "$units/default.target.wants" ~/src
  cat > "$units/omawin-index-files.service" << EOF
[Unit]
Description=Linux's files for omawin's finder (Win+F)

[Service]
Type=oneshot
Environment=$(systemd_env OMAWIN_STATE "${OMAWIN_STATE:-}") $(systemd_env WSL_DISTRO_NAME "${WSL_DISTRO_NAME:-}")
ExecStart=%h/.local/bin/omawin-index-files
Nice=10
IOSchedulingClass=idle
EOF
  cat > "$units/omawin-index-files.timer" << 'EOF'
[Unit]
Description=Linux's files for omawin's finder, kept current

[Timer]
OnStartupSec=30s
OnUnitActiveSec=10min

[Install]
WantedBy=timers.target
EOF
  cat > "$units/omawin-index-files.path" << 'EOF'
[Unit]
Description=Linux's files for omawin's finder, when ~/src changes

[Path]
PathChanged=%h/src
Unit=omawin-index-files.service

[Install]
WantedBy=default.target
EOF
  ln -sf ../omawin-index-files.timer "$units/timers.target.wants/omawin-index-files.timer"
  ln -sf ../omawin-index-files.path "$units/default.target.wants/omawin-index-files.path"
  systemctl --user daemon-reload > /dev/null 2>&1 || true
}

# One assignment for a unit's Environment= line: quoted whole, with backslashes, quotes and
# systemd's specifiers (%) escaped.
systemd_env() {
  local v="$1=$2"
  v="${v//\\/\\\\}"
  v="${v//\"/\\\"}"
  printf '"%s"' "${v//%/%%}"
}

# Windows' SSH agent in Linux (roadmap 3.9): Windows' OpenSSH agent, or 1Password's, which serves
# the same pipe, so one sign-in covers both sides. npiperelay (a Windows program, run through
# interop, per user in %LOCALAPPDATA%\Programs\npiperelay, from its maintained fork's releases)
# talks to the pipe; socat (Omarchy's list) makes it a socket. config/bash/bashrc starts the bridge
# when its socket isn't there and points SSH_AUTH_SOCK at it. Started from a shell rather than a
# systemd unit: interop (running a Windows program) is reliably there in a session wsl.exe started.
ssh_agent_bridge() {
  [[ -n ${LOCALAPPDATA:-} ]] || return 0   # not under WSL (CI)
  local dir="$LOCALAPPDATA/Programs/npiperelay" zip url
  if [[ ! -f $dir/npiperelay.exe ]]; then
    url="$(latest_asset albertony/npiperelay "npiperelay_windows_$([[ $arch == aarch64 ]] && echo arm64 || echo amd64).zip")"
    say "npiperelay (Windows' SSH agent in Linux)"
    zip="$(mktemp)"
    if curl -fsSL "$url" -o "$zip"; then
      mkdir -p "$dir"
      unzip -o -q -j "$zip" '*npiperelay.exe' -d "$dir" || say "npiperelay: the download had no npiperelay.exe"
    fi
    rm -f "$zip"
    if [[ ! -f $dir/npiperelay.exe ]]; then
      say "npiperelay didn't download; the SSH agent bridge waits for the next omawin update"
      return 0
    fi
  fi
  # The relay as a path with no spaces (socat's EXEC address splits on them; Windows' user folder
  # may have one).
  put_wrapper omawin-npiperelay << EOF
#!/bin/bash
# Windows' SSH agent pipe on stdin/stdout, for omawin-ssh-agent. (Written by omawin's wsl/setup.sh.)
exec $(printf %q "$dir/npiperelay.exe") -ei -s //./pipe/openssh-ssh-agent
EOF
  put_wrapper omawin-ssh-agent << 'EOF'
#!/bin/bash
# Windows' SSH agent as a socket in Linux: socat listens on it, and each connection gets its own
# npiperelay to Windows' agent pipe. Started in the background by config/bash/bashrc when the socket
# isn't there. (Written by omawin's wsl/setup.sh.)
sock="${1:?the socket}"
rm -f "$sock"
exec socat "UNIX-LISTEN:$sock,fork,umask=077" "EXEC:$HOME/.local/bin/omawin-npiperelay,nofork"
EOF
}

# Linux-side migrations (wsl/migrations/<yyyy-mm-dd>-<what>.sh), Omarchy's pattern: each runs once,
# as the user (sudo is there without a password), and leaves a marker in
# ~/.local/state/omawin/migrations. Like omawin's Windows migrations they must be safe on a
# brand-new Linux and safe to run twice, so a new Linux runs them all.
run_migrations() {
  local dir="$repo/wsl/migrations" state=~/.local/state/omawin/migrations file name
  [[ -d $dir ]] || return 0
  mkdir -p "$state"
  for file in "$dir"/*.sh; do
    [[ -f $file ]] || continue
    name="${file##*/}"
    [[ -f $state/$name ]] && continue
    say "migration ${name%.sh}"
    if OMAWIN="$repo" bash -euo pipefail <(tr -d '\r' < "$file"); then
      touch "$state/$name"
    else
      say "migration ${name%.sh} failed; it runs again next time"
    fi
  done
}

refresh_part() {
  cd ~ || return 1
  mkdir -p ~/.config ~/.local/bin
  shell_blocks
  wrappers
  linux_tools
  omarchy_configs
  user_units
  ssh_agent_bridge
  [[ $distro == ubuntu ]] && ubuntu_mise_tools
  run_migrations
}

user_part() {
  # From home, so git below isn't inside the checkout (a git worktree's .git points at a Windows
  # path Linux's git can't follow).
  cd ~ || return 1
  mkdir -p ~/.config ~/.local/bin

  # omawin's Neovim config, the same files as Windows' (config/nvim/lua/omawin.lua finds Windows'
  # folders from here). Plugins and state stay Linux's own, in ~/.local/share/nvim.
  if [[ $(readlink ~/.config/nvim 2> /dev/null) != "$repo/config/nvim" ]]; then
    # shellcheck disable=SC2088  # (said as written, not expanded)
    say "~/.config/nvim -> $repo/config/nvim"
    # An old link of ours (before the repo's configs moved to config/) is replaced; a folder of
    # yours is moved aside.
    if [[ -L ~/.config/nvim ]]; then
      rm ~/.config/nvim
    elif [[ -e ~/.config/nvim ]]; then
      mv ~/.config/nvim ~/.config/nvim.backup-"$(date +%Y%m%d-%H%M%S)"
    fi
    ln -s "$repo/config/nvim" ~/.config/nvim
  fi

  # git: Windows' sign-in (Git Credential Manager), so a push works without signing in again;
  # a helper of your own stays.
  local gcm="/mnt/c/Program Files/Git/mingw64/bin/git-credential-manager.exe" key value
  if [[ -x $gcm && -z $(git config --global credential.helper || true) ]]; then
    git config --global credential.helper "$(printf '%q' "$gcm")"
  fi
  # and this account's identity (docs/work.md), once
  if [[ -z $(git config --global user.email || true) ]] && command -v git.exe > /dev/null; then
    for key in user.name user.email; do
      value="$(git.exe config --get "$key" | tr -d '\r' || true)"
      [[ -n $value ]] && git config --global "$key" "$value"
    done
  fi

  if [[ ! -x ~/.local/bin/herdr ]] && ! command -v herdr > /dev/null; then
    say "herdr"
    curl -fsSL https://herdr.dev/install.sh | sh > /dev/null
  fi
  local agent
  for agent in "$@"; do
    case "$agent" in
      claude) command -v claude > /dev/null || [[ -x ~/.local/bin/claude ]] || { say "Claude Code"; curl -fsSL https://claude.ai/install.sh | bash > /dev/null; } ;;
      opencode) [[ -x ~/.opencode/bin/opencode ]] || { say "opencode"; curl -fsSL https://opencode.ai/install | bash > /dev/null 2>&1; } ;;
      *) say "no agent '$agent'" ;;
    esac
  done
  # opencode puts itself on the PATH in ~/.bashrc only, after its interactive-only line: linked
  # into ~/.local/bin, Neovim's agent split and herdr's tabs find it too.
  if [[ -x ~/.opencode/bin/opencode ]]; then ln -sf ~/.opencode/bin/opencode ~/.local/bin/opencode; fi
}

need_root() { [[ $(id -u) == 0 ]] || { echo "run as root" >&2; exit 1; }; }

main() {
  set -euo pipefail
  case "${1:-}" in
    root) need_root; root_part "${2:?the user name}" ;;
    user) shift; user_part "$@"; refresh_part ;;
    refresh) refresh_part ;;
    update) need_root; update_part ;;
    *) echo "setup.sh root <user> | setup.sh user [agent ...] | setup.sh refresh | setup.sh update" >&2; exit 1 ;;
  esac
}

# Sourced (test/linux/setup-test.sh), only the functions.
if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
