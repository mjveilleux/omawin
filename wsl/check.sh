#!/bin/bash
# omawin check's Linux half (roadmap 3.10): run as root by bin/check.ps1 (wsl -u root, only while
# Linux runs) and by CI's containers (--ci: no WSL, no systemd). One line per check,
# tab-separated: ok|warn|fail, what, detail. Read-only. Exits 1 when something failed.
#
#   check.sh <user> [--ci]
set -uo pipefail

user="${1:?the user name}"
ci="${2:-}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
distro="$(. /etc/os-release && echo "$ID")"
failed=0

line() {
  printf '%s\t%s\t%s\n' "$1" "$2" "${3:-}"
  [[ $1 == fail ]] && failed=1
  return 0
}
# ok when the command succeeds, else the given level
check() {
  local level="$1" what="$2" good="$3" bad="$4"
  shift 4
  if "$@" > /dev/null 2>&1; then line ok "$what" "$good"; else line "$level" "$what" "$bad"; fi
}

if [[ ! -f /etc/omawin ]]; then
  line ok "Linux" "yours (omawin didn't set it up): not checked"
  exit 0
fi

if [[ $ci != --ci ]]; then
  if [[ $(ps -p 1 -o comm= 2> /dev/null) == systemd ]]; then
    line ok systemd "PID 1"
    state="$(systemctl is-system-running 2> /dev/null || true)"
    case "$state" in
      running) line ok "systemd state" "$state" ;;
      degraded) line warn "systemd state" "degraded: $(systemctl --failed --no-legend --plain 2> /dev/null | awk '{ print $1 }' | xargs)" ;;
      *) line warn "systemd state" "${state:-unknown}" ;;
    esac
  else
    line fail systemd "not running (wsl.conf [boot] systemd=true applies from the next start: omawin linux stop)"
  fi
  check fail "wsl.conf systemd" "on" "missing (omawin update)" grep -qx 'systemd=true' /etc/wsl.conf
  check warn "wsl.conf interop" "Windows' PATH left out (wrappers in ~/.local/bin)" "Windows' PATH is appended" grep -qx 'appendWindowsPath=false' /etc/wsl.conf
fi
check fail "default user" "$user" "not $user (omawin update)" grep -qx "default=$user" /etc/wsl.conf
check fail "user $user" "" "missing (wsl/install.ps1)" id "$user"

if [[ $distro == arch ]]; then
  check fail "Omarchy's repositories" "/etc/pacman.conf" "not in /etc/pacman.conf (omawin update)" grep -q '^\[omarchy\]' /etc/pacman.conf
  if [[ -f /usr/share/omawin/packages.txt ]]; then
    # pacman -T names what isn't satisfied, provided names included.
    mapfile -t wanted < /usr/share/omawin/packages.txt
    missing="$(pacman -T "${wanted[@]}" 2> /dev/null | xargs)"
    if [[ -z $missing ]]; then
      line ok "Omarchy's terminal packages" "${#wanted[@]} installed"
    else
      line fail "Omarchy's terminal packages" "missing: $missing (omawin update)"
    fi
  else
    line fail "Omarchy's terminal packages" "no list yet (omawin update)"
  fi
  pulled="$(pacman -Qq hyprland quickshell sddm uwsm plymouth 2> /dev/null | xargs)"
  if [[ -z $pulled ]]; then line ok "no desktop packages"; else line warn "no desktop packages" "$pulled came in (pactree -r <name> says why)"; fi
fi

if [[ -d /usr/share/omawin/omarchy/default/bash ]]; then
  line ok "Omarchy's shell defaults" "$(git -C /usr/share/omawin/omarchy rev-parse --short HEAD 2> /dev/null)"
else
  line fail "Omarchy's shell defaults" "missing (omawin update)"
fi

# The tools, as the user's login shell finds them (~/.local/bin, mise's shims).
for tool in git nvim starship zoxide fzf eza bat fd rg jq lazygit btop gum tmux herdr mise docker; do
  check fail "$tool" "" "not on $user's PATH (omawin update)" runuser -l "$user" -c "command -v $tool"
done
check fail "docker group" "$user is in it" "$user isn't in it (omawin update)" bash -c "id -nG $(printf %q "$user") | tr ' ' '\n' | grep -qx docker"
if [[ $ci != --ci ]]; then
  check fail "docker.socket" "enabled" "not enabled (omawin update)" systemctl is-enabled docker.socket
fi

check warn "boot doesn't wait for the network" "wait-online masked" "it may (omawin update)" test -L /etc/systemd/system/systemd-networkd-wait-online.service

home="$(getent passwd "$user" | cut -d: -f6)"
check fail "bashrc block" "" "missing (omawin update)" grep -q '^# >>> omawin >>>' "$home/.bashrc"
check fail "bashrc sources omawin" "config/bash/bashrc" "its block names an old path (omawin update)" grep -q 'config/bash/bashrc' "$home/.bashrc"

if [[ $ci != --ci ]]; then
  idx="$home/.cache/omawin/files.tsv"
  if [[ -f $idx && -n $(find "$idx" -mmin -60 2> /dev/null) ]]; then
    line ok "file index" "$(wc -l < "$idx") files"
  else
    line warn "file index" "missing or older than an hour (systemctl --user status omawin-index-files.timer)"
  fi
  if [[ -x $home/.local/bin/omawin-ssh-agent ]]; then
    line ok "SSH agent bridge" "omawin-ssh-agent in ~/.local/bin"
  else
    line warn "SSH agent bridge" "not set up (npiperelay missing: omawin update)"
  fi
fi

for f in "$repo"/wsl/migrations/*.sh; do
  [[ -f $f ]] || continue
  [[ -f $home/.local/state/omawin/migrations/${f##*/} ]] || line warn "migration $(basename "$f" .sh)" "not run yet (omawin update)"
done

exit $failed
