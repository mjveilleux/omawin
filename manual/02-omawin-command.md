# The omawin command

Everything the setup can do has a name: `omawin <group> <name>`. The keys, the system menu and the dock use these names, and you can type them in any terminal:

```
omawin                         what there is (same as omawin help)
omawin theme                   the theme commands
omawin capture text            text from the screen, as if you'd pressed Win+Shift+T
omawin theme set gruvbox       words after the command are passed on
omawin theme set nord -Background 2
omawin update                  pull the latest setup, relink, run new migrations
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
- `restart`: zebar, glazewm, menu, music
- `reports index` (needs `~/omawin-work`, see [docs/work.md](../docs/work.md))
- the setup itself: `update` (and `update terminal`), `migrate`, `profile terminal`, `profile ram`, `check`, `install`, `no-ads`, `start` (and `start terminal`, `start glazewm`)

The list is `bin/commands.txt`, one line per command. To add your own, add a line there: no rebuild needed.
