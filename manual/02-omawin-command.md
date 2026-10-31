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
- `reports index` (needs a reports script in `~/omawin-account`, see [docs/work.md](../docs/work.md))
- your own settings ([docs/work.md](../docs/work.md)): `shared` (the PC's shared folder: where, who can change it, its backup), `shared backup`, `account new`, `claude sync`
- the setup itself: `update` (and `update terminal`), `uninstall` (all of omawin out of this account: it shows what it will do first), `migrate`, `profile terminal`, `profile menu`, `profile login`, `profile ram`, `check`, `path` (your PATH: what's broken in it, and which `node` / `python` / `git` runs), `install`, `no-ads`, `start` (and `start terminal`, `start bar`, `start glazewm`)

The list is `bin/commands.txt`, one line per command. To add your own, add a line there: no rebuild needed.
