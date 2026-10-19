#!/usr/bin/env bash
# omawin's Ubuntu in WSL (the wsl preset: wsl/install.ps1 runs this): the tools, omawin's Neovim
# config, herdr for Ubuntu projects, the agents, and the same prompt as Windows. Run from the repo
# under /mnt/c, in two parts:
#
#   setup.sh root <user>            as root: packages, Neovim, tree-sitter, the Linux user
#   setup.sh user [agent ...]       as the user: links, herdr, the agents (claude, opencode)
#
# Safe to run again: what's there is kept, what's missing is added.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
arch="$(uname -m)"
case "$arch" in
  x86_64) nvim_arch=x86_64; ts_arch=x64 ;;
  aarch64) nvim_arch=arm64; ts_arch=arm64 ;;
  *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
esac
say() { printf '  %s\n' "$*"; }

# The latest release asset's URL from GitHub, by the end of its name.
latest_asset() {
  curl -fsSL "https://api.github.com/repos/$1/releases/latest" |
    grep -o '"browser_download_url": *"[^"]*'"$2"'"' | head -1 | sed 's/.*"\(https[^"]*\)"/\1/'
}

root_part() {
  local user="$1"
  export DEBIAN_FRONTEND=noninteractive
  say "packages"
  apt-get update -qq
  # build-essential: treesitter compiles its parsers; nodejs/npm and python3-venv: mason's
  # npm and pip tools (prettier, sqlfluff); the rest is the shell's (bash/bashrc: starship, zoxide, fzf, eza, bat), LazyVim's, and
  # what Windows' core has too (lazygit, gh, fastfetch, btop).
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

  # The Linux user, made WSL's default (it applies from WSL's next start); its password is set
  # by `passwd`, asked for by install.ps1.
  if ! id "$user" > /dev/null 2>&1; then
    say "user $user"
    useradd -m -s /bin/bash -G sudo "$user"
  fi
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
  mkdir -p ~/.config ~/.local/bin

  # omawin's Neovim config, the same files as Windows' (nvim/lua/omawin.lua finds Windows'
  # folders from here). Plugins and state stay Linux's own, in ~/.local/share/nvim.
  if [ "$(readlink ~/.config/nvim 2>/dev/null)" != "$repo/nvim" ]; then
    say "~/.config/nvim -> $repo/nvim"
    [ -e ~/.config/nvim ] && mv ~/.config/nvim ~/.config/nvim.backup-"$(date +%Y%m%d-%H%M%S)"
    ln -s "$repo/nvim" ~/.config/nvim
  fi

  # Login shells too (herdr's tabs are `bash -lc`): the configs and ~/.local/bin.
  put_block ~/.profile "export OMAWIN=\"$repo\"
export STARSHIP_CONFIG=\"\$OMAWIN/starship/starship.toml\" HERDR_CONFIG_PATH=\"\$OMAWIN/herdr/config.toml\"
case \":\$PATH:\" in *\":\$HOME/.local/bin:\"*) ;; *) PATH=\"\$HOME/.local/bin:\$PATH\" ;; esac"
  # Interactive shells: the same prompt, keys and colors as Windows (bash/bashrc). The repo is
  # named here too: ~/.profile loads ~/.bashrc before its own block has run.
  put_block ~/.bashrc "export OMAWIN=\"$repo\" STARSHIP_CONFIG=\"$repo/starship/starship.toml\"
[ -f \"\$OMAWIN/bash/bashrc\" ] && . \"\$OMAWIN/bash/bashrc\""

  # git: Windows' sign-in (Git Credential Manager), so a push works without signing in again.
  local gcm="/mnt/c/Program Files/Git/mingw64/bin/git-credential-manager.exe"
  [ -x "$gcm" ] && git config --global credential.helper "$(printf '%q' "$gcm")"
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
  *) echo "setup.sh root <user> | setup.sh user [agent ...]" >&2; exit 1 ;;
esac
