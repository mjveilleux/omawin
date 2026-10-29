#!/usr/bin/env bash
# omawin's Linux in WSL (the wsl preset: wsl/install.ps1 runs this): the tools, omawin's Neovim
# config, herdr for Linux projects, the agents, and the same prompt as Windows. Arch (the
# default) or Ubuntu, told apart by /etc/os-release. Run from the repo under /mnt/c, in parts:
#
#   setup.sh root <user>            as root: packages, the Linux user (sudo without a password)
#   setup.sh user [agent ...]       as the user: links, herdr, the agents (claude, opencode)
#   setup.sh update                 as root: Arch's packages up to date (omawin update)
#
# Arch takes its packages from Omarchy's repositories, as Omarchy itself does: Omarchy's own
# ([omarchy]: herdr, its keyring) and its copy of Arch's, which moves forward a tested set at a
# time (the stable channel), so an update doesn't break things. No AUR.
#
# Safe to run again: what's there is kept, what's missing is added.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
arch="$(uname -m)"
distro="$(. /etc/os-release && echo "$ID")"
say() { printf '  %s\n' "$*"; }

# The latest release asset's URL from GitHub, by the end of its name.
latest_asset() {
  curl -fsSL "https://api.github.com/repos/$1/releases/latest" |
    grep -o '"browser_download_url": *"[^"]*'"$2"'"' | head -1 | sed 's/.*"\(https[^"]*\)"/\1/'
}

# --- Arch ----------------------------------------------------------------------------------------
# Omarchy's signing key (its bin/omarchy-update-keyring); omarchy-keyring then keeps it current.
omarchy_key=40DFB630FF42BCFFB047046CF0134EE680CAC571

# pacman's stderr without its "already installed" lines (--needed says one per package).
quiet_skips() { grep -v ' is up to date -- skipping$' >&2 || true; }

arch_repos() {
  # pacman's own keys: the image makes them at its first start (/usr/lib/wsl/first-setup.sh),
  # which may not have happened yet.
  if [ ! -f /etc/pacman.d/gnupg/trustdb.gpg ]; then
    say "pacman's keys"
    pacman-key --init > /dev/null 2>&1
    pacman-key --populate archlinux > /dev/null 2>&1
  fi
  # Omarchy's repositories (its default/pacman/pacman-stable.conf and mirrorlist-stable, less
  # multilib, which WSL has no use for). Arch's own files are kept as *.arch.
  if ! grep -q '^\[omarchy\]' /etc/pacman.conf; then
    say "Omarchy's package repositories (stable)"
    [ -e /etc/pacman.conf.arch ] || cp /etc/pacman.conf /etc/pacman.conf.arch
    [ -e /etc/pacman.d/mirrorlist.arch ] || cp /etc/pacman.d/mirrorlist /etc/pacman.d/mirrorlist.arch
    cat > /etc/pacman.conf <<'EOF'
# Written by omawin (wsl/setup.sh), after Omarchy's default/pacman/pacman-stable.conf.
# Arch's own is /etc/pacman.conf.arch.
[options]
HoldPkg = pacman glibc
Architecture = auto
Color
VerbosePkgLists
NoProgressBar
CheckSpace
ParallelDownloads = 5
DownloadUser = alpm
SigLevel = Required DatabaseOptional
LocalFileSigLevel = Optional

[omarchy]
Server = https://pkgs.omarchy.org/stable/$arch

[core]
Include = /etc/pacman.d/mirrorlist

[extra]
Include = /etc/pacman.d/mirrorlist
EOF
    cat > /etc/pacman.d/mirrorlist <<'EOF'
# Omarchy's copy of Arch's repositories, the stable channel (written by omawin, wsl/setup.sh).
Server = https://stable-mirror.omarchy.org/$repo/os/$arch
EOF
  fi
  if ! pacman-key --list-keys "$omarchy_key" > /dev/null 2>&1; then
    say "Omarchy's signing key"
    pacman-key --recv-keys "$omarchy_key" --keyserver keys.openpgp.org > /dev/null 2>&1
    pacman-key --lsign-key "$omarchy_key" > /dev/null 2>&1
  fi
}

arch_root() {
  arch_repos
  say "packages"
  # The keyrings first: an image older than a maintainer's new key can't check the rest.
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm > /dev/null
  # base-devel: treesitter compiles its parsers; nodejs/npm and python-pip: mason's npm and pip
  # tools (prettier, sqlfluff); the rest is the shell's (bash/bashrc: starship, zoxide, fzf, eza,
  # bat), LazyVim's, and what Windows' core has too (lazygit, gh, fastfetch, btop). herdr is
  # Omarchy's.
  pacman -S --needed --noconfirm base-devel sudo which less man-db openssh curl wget git unzip zip \
    ripgrep fd fzf jq bat eza zoxide starship lazygit github-cli fastfetch btop \
    neovim tree-sitter-cli nodejs npm python python-pip herdr > /dev/null 2> >(quiet_skips)
}

arch_update() {
  # Only an Arch omawin set up (Omarchy's repositories in): someone's own distro is theirs.
  grep -q '^\[omarchy\]' /etc/pacman.conf || { say "not omawin's Arch: left alone"; return; }
  arch_repos
  pacman -Sy --needed --noconfirm archlinux-keyring omarchy-keyring > /dev/null 2> >(quiet_skips)
  pacman -Su --noconfirm
}

# --- Ubuntu --------------------------------------------------------------------------------------
ubuntu_root() {
  case "$arch" in
    x86_64) nvim_arch=x86_64; ts_arch=x64 ;;
    aarch64) nvim_arch=arm64; ts_arch=arm64 ;;
    *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
  esac
  export DEBIAN_FRONTEND=noninteractive
  say "packages"
  apt-get update -qq
  # The same tools as Arch's (above), by Ubuntu's names.
  apt-get install -y -qq build-essential curl wget git unzip zip ripgrep fd-find fzf jq bat eza zoxide \
    starship lazygit gh fastfetch btop nodejs npm python3 python3-venv python3-pip > /dev/null
  # Ubuntu names these fdfind and batcat
  [ -e /usr/local/bin/fd ] || ln -s "$(command -v fdfind)" /usr/local/bin/fd
  [ -e /usr/local/bin/bat ] || ln -s "$(command -v batcat)" /usr/local/bin/bat

  # Neovim from its releases: the same version as Windows' (winget's), newer than Ubuntu's.
  if ! /usr/local/bin/nvim --version > /dev/null 2>&1 || [ -n "${OMAWIN_UPDATE:-}" ]; then
    say "Neovim"
    curl -fsSL "https://github.com/neovim/neovim/releases/latest/download/nvim-linux-$nvim_arch.tar.gz" | tar -xz -C /opt
    rm -rf /opt/nvim && mv "/opt/nvim-linux-$nvim_arch" /opt/nvim
    ln -sf /opt/nvim/bin/nvim /usr/local/bin/nvim
  fi
  # tree-sitter's CLI builds the parsers (LazyVim's nvim-treesitter); Ubuntu's is too old.
  if ! command -v tree-sitter > /dev/null || [ -n "${OMAWIN_UPDATE:-}" ]; then
    say "tree-sitter"
    curl -fsSL "$(latest_asset tree-sitter/tree-sitter "tree-sitter-linux-$ts_arch.gz")" | gunzip > /usr/local/bin/tree-sitter
    chmod +x /usr/local/bin/tree-sitter
  fi
}

# --- both ----------------------------------------------------------------------------------------
root_part() {
  local user="$1"
  case "$distro" in
    arch) arch_root ;;
    ubuntu) ubuntu_root ;;
    *) echo "omawin sets up Arch or Ubuntu, not '$distro'" >&2; exit 1 ;;
  esac

  # The Linux user, made WSL's default (it applies from WSL's next start).
  if ! id "$user" > /dev/null 2>&1; then
    say "user $user"
    useradd -m -s /bin/bash -G "$([ "$distro" = arch ] && echo wheel || echo sudo)" "$user"
  fi
  # sudo without a password: Windows reaches root here without one anyway (wsl -u root), so a
  # password would guard nothing, and the install never stops to ask for one.
  local sudoers=/etc/sudoers.d/omawin
  if [ ! -f "$sudoers" ] || ! grep -q "^$user " "$sudoers"; then
    printf '%s ALL=(ALL:ALL) NOPASSWD: ALL\n' "$user" > "$sudoers.new"
    chmod 440 "$sudoers.new"
    if visudo -cqf "$sudoers.new"; then mv "$sudoers.new" "$sudoers"; else rm -f "$sudoers.new"; echo "sudoers line refused" >&2; fi
  fi
  # omawin set this distro up: wsl/install.ps1 leaves one without it alone (it's yours).
  [ -f /etc/omawin ] || printf 'Set up by omawin (wsl/setup.sh), %s.\n' "$(date +%Y-%m-%d)" > /etc/omawin
  if ! grep -q "^default=$user\$" /etc/wsl.conf 2>/dev/null; then
    if grep -q '^\[user\]' /etc/wsl.conf 2>/dev/null; then
      sed -i "/^\[user\]/,/^\[/ s/^default=.*/default=$user/" /etc/wsl.conf
      grep -q "^default=$user\$" /etc/wsl.conf || sed -i "/^\[user\]/a default=$user" /etc/wsl.conf
    else
      printf '\n[user]\ndefault=%s\n' "$user" >> /etc/wsl.conf
    fi
  fi
}

# A block of lines between markers in a file, replaced in place each run.
put_block() {
  local file="$1" text="$2"
  touch "$file"
  sed -i '/^# >>> omawin >>>$/,/^# <<< omawin <<<$/d' "$file"
  printf '# >>> omawin >>>\n%s\n# <<< omawin <<<\n' "$text" >> "$file"
}

user_part() {
  # From home, so git below isn't inside the checkout (a git worktree's .git points at a Windows
  # path Linux's git can't follow).
  cd ~
  mkdir -p ~/.config ~/.local/bin

  # omawin's Neovim config, the same files as Windows' (nvim/lua/omawin.lua finds Windows'
  # folders from here). Plugins and state stay Linux's own, in ~/.local/share/nvim.
  if [ "$(readlink ~/.config/nvim 2>/dev/null)" != "$repo/nvim" ]; then
    say "~/.config/nvim -> $repo/nvim"
    [ -e ~/.config/nvim ] && mv ~/.config/nvim ~/.config/nvim.backup-"$(date +%Y%m%d-%H%M%S)"
    ln -s "$repo/nvim" ~/.config/nvim
  fi

  # Login shells too (herdr's tabs are `bash -lc`): the configs and ~/.local/bin. bash reads
  # ~/.bash_profile instead of ~/.profile when there is one (Arch makes one).
  local login=~/.profile
  [ -f ~/.bash_profile ] && login=~/.bash_profile
  put_block "$login" "export OMAWIN=\"$repo\"
export STARSHIP_CONFIG=\"\$OMAWIN/starship/starship.toml\" HERDR_CONFIG_PATH=\"\$OMAWIN/herdr/config.toml\"
case \":\$PATH:\" in *\":\$HOME/.local/bin:\"*) ;; *) PATH=\"\$HOME/.local/bin:\$PATH\" ;; esac"
  # Interactive shells: the same prompt, keys and colors as Windows (bash/bashrc). The repo is
  # named here too: ~/.profile loads ~/.bashrc before its own block has run.
  put_block ~/.bashrc "export OMAWIN=\"$repo\" STARSHIP_CONFIG=\"$repo/starship/starship.toml\"
[ -f \"\$OMAWIN/bash/bashrc\" ] && . \"\$OMAWIN/bash/bashrc\""

  # git: Windows' sign-in (Git Credential Manager), so a push works without signing in again;
  # a helper of your own stays.
  local gcm="/mnt/c/Program Files/Git/mingw64/bin/git-credential-manager.exe"
  if [ -x "$gcm" ] && [ -z "$(git config --global credential.helper || true)" ]; then
    git config --global credential.helper "$(printf '%q' "$gcm")"
  fi
  # and this account's identity (docs/work.md), once
  if [ -z "$(git config --global user.email || true)" ] && command -v git.exe > /dev/null; then
    for key in user.name user.email; do
      value="$(git.exe config --get "$key" | tr -d '\r' || true)"
      [ -n "$value" ] && git config --global "$key" "$value"
    done
  fi

  if [ ! -x ~/.local/bin/herdr ] && ! command -v herdr > /dev/null; then
    say "herdr"
    curl -fsSL https://herdr.dev/install.sh | sh > /dev/null
  fi
  for agent in "$@"; do
    case "$agent" in
      claude) command -v claude > /dev/null || [ -x ~/.local/bin/claude ] || { say "Claude Code"; curl -fsSL https://claude.ai/install.sh | bash > /dev/null; } ;;
      opencode) [ -x ~/.opencode/bin/opencode ] || { say "opencode"; curl -fsSL https://opencode.ai/install | bash > /dev/null 2>&1; } ;;
      *) say "no agent '$agent'" ;;
    esac
  done
  # opencode puts itself on the PATH in ~/.bashrc only, after its interactive-only line: linked
  # into ~/.local/bin, Neovim's agent split and herdr's tabs find it too.
  [ -x ~/.opencode/bin/opencode ] && ln -sf ~/.opencode/bin/opencode ~/.local/bin/opencode
}

case "${1:-}" in
  root) [ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }; root_part "${2:?the user name}" ;;
  user) shift; user_part "$@" ;;
  update)
    [ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
    if [ "$distro" = arch ]; then arch_update; else say "nothing to update in $distro"; fi ;;
  *) echo "setup.sh root <user> | setup.sh user [agent ...] | setup.sh update" >&2; exit 1 ;;
esac
