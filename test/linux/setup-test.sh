#!/bin/bash
# Linux in WSL's bash, without WSL or root: wsl/setup.sh's functions against a throwaway home, the
# shell (config/bash/bashrc) in an interactive bash, and the scripts wsl/bin puts in ~/.local/bin.
# What needs a real distro (packages, systemd, the user) runs in CI's containers
# (.github/workflows/linux.yml) and on a PC.
#
#   bash test/linux/setup-test.sh
set -uo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
passed=0 failed=0

ok() { passed=$((passed + 1)); }
bad() { failed=$((failed + 1)); printf 'FAIL %s\n' "$*" >&2; }
expect_eq() { if [[ $2 == "$3" ]]; then ok; else bad "$1: expected [$3], got [$2]"; fi; }
expect_has() { if [[ $2 == *"$3"* ]]; then ok; else bad "$1: [$3] not in [$2]"; fi; }
expect_not() { if [[ $2 != *"$3"* ]]; then ok; else bad "$1: [$3] should not be in [$2]"; fi; }

# --- wsl/setup.sh's functions ----------------------------------------------------------------------
export HOME="$tmp/home"
mkdir -p "$HOME"
# shellcheck source-path=SCRIPTDIR/../..
# shellcheck source=wsl/setup.sh
. "$repo/wsl/setup.sh"
say() { :; }   # quiet

# Package lists: comments, blanks and Windows line endings out; Omarchy's list less the skip list.
printf 'b\r\n# a comment\r\n\r\na  # trailing\r\nhyprland\r\nyay\r\n' > "$tmp/list"
expect_eq "list_names" "$(list_names "$tmp/list" | xargs)" "a b hyprland yay"
omarchy_dir="$tmp/omarchy"
mkdir -p "$omarchy_dir/install"
printf 'bat\nhyprland\nyay\nmise-bin\nchromium\ndocker\n' > "$omarchy_dir/install/omarchy-base.packages"
expect_eq "omarchy_packages leaves the skip list out" "$(omarchy_packages | xargs)" "bat docker mise-bin"

# Every name the skip list leaves out is one Omarchy's list has (a typo would leave a desktop
# package in). Checked against the pinned list when a checkout of it is at hand.
if [[ -f ${OMARCHY_CHECKOUT:-/nonexistent}/install/omarchy-base.packages ]]; then
  stray="$(comm -13 <(list_names "$OMARCHY_CHECKOUT/install/omarchy-base.packages") <(list_names "$repo/wsl/omarchy-skip.txt") | xargs)"
  expect_eq "every skipped name is in Omarchy's list" "$stray" ""
fi

# pacman_wanted: a name the repositories don't have is left out and the rest goes in. (pacman here
# is a stand-in that knows four packages and answers as pacman does.)
mkdir -p "$tmp/bin"
cat > "$tmp/bin/pacman" << 'EOF'
#!/bin/bash
known=" bat docker git nvim "
missing=0
for a in "$@"; do
  [[ $a == -* ]] && continue
  [[ $known == *" $a "* ]] || { echo "error: target not found: $a" >&2; missing=1; }
done
exit $missing
EOF
chmod +x "$tmp/bin/pacman"
went="$(PATH="$tmp/bin:$PATH" pacman_wanted bat nosuch docker gone-too nvim 2> /dev/null | xargs)"
expect_eq "pacman_wanted keeps what the repositories have" "$went" "bat docker nvim"

# ini_set: replaced where it is, added to its section, else a new section; idempotent.
conf="$tmp/wsl.conf"
printf '[automount]\noptions = "metadata"\n\n[user]\ndefault = old\n' > "$conf"
ini_set "$conf" user default me
ini_set "$conf" boot systemd true
ini_set "$conf" interop appendWindowsPath false
ini_set "$conf" boot systemd true
expect_eq "ini_set" "$(cat "$conf")" '[automount]
options = "metadata"

[user]
default=me

[boot]
systemd=true

[interop]
appendWindowsPath=false'
: > "$tmp/empty.conf"
ini_set "$tmp/empty.conf" boot systemd true
expect_eq "ini_set on an empty file" "$(cat "$tmp/empty.conf")" $'[boot]\nsystemd=true'
expect_eq "wsl_user" "$(wsl_user "$conf")" "me"

# wsl_conf: omawin's settings, then the account folder's wsl/wsl.conf over them (CRLF, comments).
mkdir -p "$tmp/account/wsl"
printf '# mine\r\n[interop]\r\nappendWindowsPath = true\r\n[automount]\r\nroot = /win/\r\n' > "$tmp/account/wsl/wsl.conf"
: > "$tmp/wsl2.conf"
OMAWIN_ACCOUNT="$tmp/account" wsl_conf me "$tmp/wsl2.conf"
expect_has "wsl_conf: systemd" "$(cat "$tmp/wsl2.conf")" "systemd=true"
expect_has "wsl_conf: the account's override wins" "$(cat "$tmp/wsl2.conf")" "appendWindowsPath=true"
expect_not "wsl_conf: no CR carried in" "$(cat "$tmp/wsl2.conf")" $'\r'
expect_has "wsl_conf: a new section from the account" "$(cat "$tmp/wsl2.conf")" $'[automount]\nroot=/win/'
expect_eq "wsl_conf: the user" "$(wsl_user "$tmp/wsl2.conf")" "me"

# The shell blocks: one each, replaced in place (an old block naming bash/bashrc, from before the
# repo's configs moved to config/, goes), what was there before them kept.
printf 'mine=1\n# >>> omawin >>>\n[ -f "$OMAWIN/bash/bashrc" ] && . "$OMAWIN/bash/bashrc"\n# <<< omawin <<<\n' > ~/.bashrc
touch ~/.bash_profile
OMAWIN_STATE="/mnt/c/Users/A Person/AppData/Local/launcher" shell_blocks
OMAWIN_STATE="/mnt/c/Users/A Person/AppData/Local/launcher" shell_blocks
expect_eq "one bashrc block" "$(grep -c '^# >>> omawin >>>$' ~/.bashrc)" "1"
expect_has "the bashrc block sources config/bash/bashrc" "$(cat ~/.bashrc)" '$OMAWIN/config/bash/bashrc'
expect_not "the old path is gone" "$(cat ~/.bashrc)" '$OMAWIN/bash/bashrc'
expect_has "what was there stays" "$(head -1 ~/.bashrc)" "mine=1"
expect_has "the login block goes in ~/.bash_profile" "$(cat ~/.bash_profile)" "STARSHIP_CONFIG"
state_seen="$(bash -c '. ~/.bash_profile; printf %s "$OMAWIN_STATE|$OMAWIN|$STARSHIP_CONFIG"')"
expect_eq "the login block's values survive spaces" "$state_seen" "/mnt/c/Users/A Person/AppData/Local/launcher|$repo|$repo/config/starship/starship.toml"

# Wrappers: valid scripts; omawin's only when Windows' home is known; a path with spaces kept whole.
mkdir -p ~/.local/bin
USERPROFILE="/mnt/c/Users/A Person" LOCALAPPDATA="/mnt/c/Users/A Person/AppData/Local" wrappers
for w in explorer browser clip omawin code; do
  if [[ -x ~/.local/bin/$w ]] && bash -n ~/.local/bin/$w; then ok; else bad "wrapper $w"; fi
done
expect_has "omawin's wrapper names omawin.exe" "$(cat ~/.local/bin/omawin)" 'A\ Person/.local/bin/omawin.exe'
rm ~/.local/bin/omawin
wrappers
if [[ -e ~/.local/bin/omawin ]]; then bad "no omawin wrapper without USERPROFILE"; else ok; fi

expect_eq "systemd_env escapes" "$(systemd_env X 'a "b" 50%\c')" '"X=a \"b\" 50%%\\c"'

# Linux migrations: each once, a marker after it worked, none after it failed.
repo_real="$repo"
repo="$tmp/fake-repo"
mkdir -p "$repo/wsl/migrations"
printf 'echo ran >> "$HOME/ran-a"\r\n' > "$repo/wsl/migrations/2026-11-01-a.sh"
printf 'false\n' > "$repo/wsl/migrations/2026-11-02-b.sh"
run_migrations
run_migrations
expect_eq "a migration runs once (CRLF fine)" "$(cat ~/ran-a)" "ran"
if [[ -f ~/.local/state/omawin/migrations/2026-11-01-a.sh ]]; then ok; else bad "marker for a"; fi
if [[ -f ~/.local/state/omawin/migrations/2026-11-02-b.sh ]]; then bad "no marker for a failed one"; else ok; fi
repo="$repo_real"

# Omarchy's configs: copied once; btop shows the terminal's background; yours kept.
mkdir -p "$omarchy_dir/config/btop" "$omarchy_dir/config/tmux" "$omarchy_dir/config/lazygit"
printf 'color_theme = "current"\ntheme_background = true\n' > "$omarchy_dir/config/btop/btop.conf"
printf 'set -g prefix C-Space\n' > "$omarchy_dir/config/tmux/tmux.conf"
mkdir -p ~/.config/lazygit && printf 'mine: true\n' > ~/.config/lazygit/config.yml
omarchy_configs
expect_has "btop's background is the terminal's" "$(cat ~/.config/btop/btop.conf)" "theme_background = false"
expect_has "tmux's config came" "$(cat ~/.config/tmux/tmux.conf)" "C-Space"
expect_eq "a config of yours is kept" "$(cat ~/.config/lazygit/config.yml)" "mine: true"

# User units: the index's environment, quoted.
WSL_DISTRO_NAME=archlinux OMAWIN_STATE="/mnt/c/Users/A Person/x" user_units
expect_has "the index unit's environment" "$(cat ~/.config/systemd/user/omawin-index-files.service)" 'Environment="OMAWIN_STATE=/mnt/c/Users/A Person/x" "WSL_DISTRO_NAME=archlinux"'
if [[ -L ~/.config/systemd/user/timers.target.wants/omawin-index-files.timer ]]; then ok; else bad "the timer is enabled"; fi

# --- wsl/bin -----------------------------------------------------------------------------------------
state="$tmp/state"
mkdir -p "$state"
printf "# a comment\r\nOMAWIN_THEME='nord'\r\nOMAWIN_LINUX_BG='#353d4a'\r\n" > "$state/theme-linux.sh"
OMAWIN_STATE="$state" OMAWIN="$repo" bash "$repo/wsl/bin/omawin-theme-sync"
expect_eq "theme sync: btop's theme" "$(cat ~/.config/btop/themes/current.theme)" "$(tr -d '\r' < "$repo/themes/btop/nord.theme")"
expect_eq "theme sync: the theme it synced" "$(cat ~/.config/omawin/theme)" "nord"

if command -v fd > /dev/null; then
  mkdir -p ~/src/app/node_modules/x ~/.cache/hidden
  printf 'hi' > ~/src/app/main.c
  printf 'x' > ~/src/app/node_modules/x/dep.js
  printf 'x' > ~/.cache/hidden/no
  WSL_DISTRO_NAME=archlinux OMAWIN_STATE="$state" bash "$repo/wsl/bin/omawin-index-files"
  index="$(cat "$state/linux-files.tsv")"
  expect_eq "index: its header" "$(head -1 "$state/linux-files.tsv")" "# archlinux	$HOME"
  expect_has "index: a file, its time and size" "$index" "$HOME/src/app/main.c	"
  expect_has "index: the size" "$(grep main.c <<< "$index" | cut -f3)" "2"
  expect_not "index: no node_modules" "$index" "dep.js"
  expect_not "index: no hidden folders" "$index" ".cache"
fi

# omawin-dev-env: what counts as installed (a mise version folder, not the alias links mise leaves
# behind), and the databases' containers. docker here is a stand-in with one database's container.
dev_env="$repo/wsl/bin/omawin-dev-env"
mise_dir="$tmp/mise"
mkdir -p "$mise_dir/installs/node/22.1.0" "$mise_dir/installs/go" "$tmp/dockerbin"
ln -s /nonexistent "$mise_dir/installs/go/latest"
cat > "$tmp/dockerbin/docker" << EOF
#!/bin/bash
case \$1 in
  info) exit 0 ;;
  ps) printf 'postgres18\nsomething-else\n' ;;
  *) echo "\$*" >> $(printf %q "$tmp/docker.log") ;;
esac
EOF
chmod +x "$tmp/dockerbin/docker"
dev() { MISE_DATA_DIR="$mise_dir" PATH="$tmp/dockerbin:$PATH" bash "$dev_env" "$@"; }
expect_eq "dev-env: the installed ones" "$(dev check | xargs)" "docker-db node"
if dev check node; then ok; else bad "dev-env: node is installed"; fi
if dev check go; then bad "dev-env: a mise alias alone isn't Go"; else ok; fi
dev check nosuch 2> /dev/null; expect_eq "dev-env: an unknown name" "$?" "2"
if dev install docker-db PostgreSQL Nope > /dev/null 2>&1; then bad "dev-env: an unknown database fails"; else ok; fi
expect_eq "dev-env: nothing ran before the unknown database was found" "$(cat "$tmp/docker.log" 2> /dev/null)" ""
dev install docker-db Redis > /dev/null
expect_has "dev-env: Omarchy's Redis, on this PC only" "$(cat "$tmp/docker.log")" "run -d --restart unless-stopped -p 127.0.0.1:6379:6379 --name=redis redis:7"
dev remove docker-db PostgreSQL > /dev/null
expect_has "dev-env: a database removed with its data" "$(cat "$tmp/docker.log")" "rm --force --volumes postgres18"

# --- config/bash/bashrc in an interactive shell --------------------------------------------------------
os_id="$(. /etc/os-release && echo "$ID")"
mkdir -p "$tmp/run" ~/src/my\ app
cat > "$tmp/rc" << EOF
OMAWIN=$(printf %q "$repo")
OMAWIN_STATE=$(printf %q "$state")
. "\$OMAWIN/config/bash/bashrc"
EOF
run_shell() {
  (cd ~/src/my\ app && env -u SSH_AUTH_SOCK XDG_RUNTIME_DIR="$tmp/run" "$@" bash --noprofile --rcfile "$tmp/rc" -i -c \
    'printf "SIDE=%s LINUX=%s WINDOWS=%s\n" "$OMAWIN_SIDE" "${OMAWIN_LINUX:-}" "${OMAWIN_WINDOWS:-}"; type -t _omawin_prompt; _omawin_prompt' 2> /dev/null)
}
out="$(run_shell WSL_DISTRO_NAME=test)"
expect_has "Linux: the side" "$out" "SIDE=linux"
expect_has "Linux: a mark of its own" "$out" "LINUX="$'\xef'
expect_has "Linux: no Windows mark" "$out" "WINDOWS="$'\n'
expect_has "Linux: the prompt function" "$out" "function"
expect_has "Linux: OSC 7 with the folder, its space escaped" "$out" $'\e]7;file://'"${HOSTNAME:-localhost}$HOME/src/my%20app"$'\e\\'
expect_has "Linux: OSC 11 with the theme's Linux background" "$out" $'\e]11;#353d4a\e\\'
expect_has "Linux: the title says the side" "$out" $'\e]2;'"$os_id"$' \xc2\xb7 ~/src/my app\a'

# A theme switch on Windows reaches an open shell at its next prompt.
out="$(cd ~ && env XDG_RUNTIME_DIR="$tmp/run" WSL_DISTRO_NAME=test bash --noprofile --rcfile "$tmp/rc" -i -c "
  _omawin_prompt > /dev/null
  sleep 1.1
  printf \"OMAWIN_THEME='gruvbox'\nOMAWIN_LINUX_BG='#2f3332'\n\" > $(printf %q "$state/theme-linux.sh")
  _omawin_prompt" 2> /dev/null)"
expect_has "Linux: the new background at the next prompt" "$out" $'\e]11;#2f3332\e\\'

out="$(run_shell)"
expect_has "Git Bash: the Windows side" "$out" "SIDE=windows"
expect_has "Git Bash: Windows' mark" "$out" "WINDOWS="$'\xef\x85\xba'
expect_not "Git Bash: no prompt function" "$out" "function"

printf '%d passed, %d failed\n' "$passed" "$failed"
((failed == 0))
