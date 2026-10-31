# The omawin command

Everything the setup can do has a name: `omawin <group> <name>`. The keys, the system menu and the dock use these names, and you can type them in any terminal:

```
omawin                         what there is (same as omawin help)
omawin theme                   the theme commands
omawin capture text            text from the screen, as if you'd pressed Win+Shift+T
omawin theme set gruvbox       words after the command are passed on
omawin theme set nord -Background 2
omawin update                  pull the latest setup, relink, run new migrations; shows what's new
omawin check                   is everything in place and running?
omawin --version               the release this omawin came from ((dev) if built here)
```

The groups:
- `launcher`, `files`, `menu`: the windows
- `capture`: screenshot, text
- `theme`: set, next-background, import, lockscreen
- `terminal here`, `workspace rename`
- `launch`: terminal, project, editor (in your default terminal and editor)
- `default`: what omawin opens things with (`omawin default terminal wt`, `omawin default editor code`)
- `music`: the music, podcast and radio player in the bar: open, add, list, toggle, next, prev
- `restart`: glazewm, menu, music
- `setting`: Windows' own settings, read and changed without opening Settings: `omawin setting list [area]`, `get <id>`, `set <id> <value>`, `toggle <id>`, `undo` ([the system menu](05-system-menu.md#windows-settings) has them too); `settings save` / `settings apply` carry them to a new PC, `settings admin` applies the ones that need admin in one prompt
- `reports index` (needs a reports script in `~/omawin-account`, see [docs/work.md](../docs/work.md))
- your own settings ([docs/work.md](../docs/work.md)): `shared` (the PC's shared folder: where, who can change it, its backup), `shared backup`, `account new`, `claude sync`
- the setup itself: `update` (and `update terminal`), `uninstall` (all of omawin out of this account: it shows what it will do first), `migrate`, `profile terminal`, `profile menu`, `profile login`, `profile ram`, `check`, `path` (your PATH: what's broken in it, and which `node` / `python` / `git` runs), `install`, `no-ads`, `start` (and `start terminal`, `start bar`, `start glazewm`)

The list is `bin/commands.txt`, one line per command. To add your own, add a line there: no rebuild needed.

## Packages

`omawin pkg` is one package command over winget, Scoop, Chocolatey and mise, like Omarchy's `omarchy-pkg-add` over pacman. You name the program; omawin picks the manager, runs it (its own output shows as it goes) and remembers where the program came from, so removing and updating go back to the same place.

```
omawin pkg install ripgrep go          by name: ripgrep through winget, go through mise
omawin pkg install fd --from scoop     that manager, not omawin's pick
omawin pkg install jq --machine        for every account on the PC (one admin prompt)
omawin pkg install btop --linux        in your Linux in WSL (pacman in Arch, apt in Ubuntu)
omawin pkg remove go                   through the manager that put it in
omawin pkg update                      everything omawin put in, each where it came from
omawin pkg update --all                everything every manager has
omawin pkg list --refresh              what's installed, by any manager, and where from
omawin pkg list --updates              ... and what has an update waiting
omawin pkg info ripgrep                which managers have it, which copy is here and which runs
omawin pkg move fd --to scoop          take it from another manager instead (and keep it there)
omawin pkg prefer scoop winget         your order of managers
```

- **Which manager:** `--from` if you give it; else your pin for that program (`omawin pkg move` sets it); else omawin's own pick for it (languages through mise); else your order (`omawin pkg prefer`, winget first by default); the first one on the PC that has it wins.
- **One name for one thing:** `default/packages/names.txt` maps a name (`ripgrep`) to each manager's id (`BurntSushi.ripgrep.MSVC` in winget, `ripgrep` in Scoop). A name that isn't there is looked up in each manager's catalogue: an exact match installs, otherwise you pick from what was found. A manager's own id works too (`omawin pkg install Microsoft.PowerToys`), and so does `scoop:fd`.
- **No admin unless you ask for it:** winget installs for your account alone (`--scope user`); `--machine`, and anything Chocolatey does (it always installs for the whole PC), asks once for the whole command, after saying what will change. `omawin pkg update` never asks on its own: Chocolatey's updates wait for `omawin pkg update --from choco`.
- **Yours stays yours:** `omawin pkg list --refresh` adds what you installed outside omawin, marked "(yours)"; `omawin pkg update` leaves those alone unless you name them or say `--all`. The tools bundle's programs (omawin's core) aren't removed: `omawin pkg move <name> --to winget` puts your own copy first on the PATH instead.
- **Where it's kept:** `%LOCALAPPDATA%\launcher\packages.tsv` (what's installed and where from), `pkg-prefer.txt` (your order), `pkg-pins.tsv` (your pins).
