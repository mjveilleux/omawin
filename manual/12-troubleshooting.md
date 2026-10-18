# Troubleshooting

Start with `omawin check`: it lists anything missing, stale or not running.

- **Bar missing or blank:** `omawin restart zebar`.
- **Launcher, menu or screenshots stuck or out of date:** `omawin restart menu` rebuilds and restarts the menu app. Its errors go to `%LOCALAPPDATA%\launcher\system-menu.log`.
- **A key does nothing:** `omawin restart glazewm` reloads GlazeWM's config. Errors are in `~/.glzr/glazewm/errors.log`. GlazeWM ignores simulated keypresses, so test keys with real ones.
- **Something left over from an older setup:** `omawin migrate`.
- **The PC feels heavy:** `omawin profile ram` lists what holds the memory and the processor, by app (an app's hidden browser processes counted as the app's), omawin's parts first against what they should cost while idle. `-Save` keeps a history and shows what grew since last time. An agent asked to profile RAM uses the `profile-ram` skill (`.claude/skills/`), which reads the same numbers.
- **Terminals feel slow:** `omawin profile terminal` presses Win+Enter a few times on workspace 8 and prints where each millisecond went (`-Video file.mp4` records the screen too).
- **Terminal out of date or misbehaving:** `omawin update terminal` installs the latest release of our noctty fork (or upgrades Windows Terminal, if that's your default).
- **Lazy says "dubious ownership":** Neovim was run from an admin shell, so plugin folders became admin-owned. `git/gitconfig` trusts `~/AppData/Local/nvim-data/lazy/*`; better not to run nvim as admin.
- **Windows Terminal settings:** they're Terminal's own (in its LocalState), seeded once from omawin's. To start over from omawin's, delete `settings.json` there and run `omawin install -LinkOnly`.
- **`omawin update` said your local changes clash and are kept in a stash:** the update went through without them; nothing is lost. `git -C ~/omawin stash list` shows the stash (`omawin update <time>`); `git -C ~/omawin stash show -p --include-untracked stash@{0}` shows what's in it; `git -C ~/omawin stash pop stash@{0}` puts it back (fix the files it marks with conflicts, then `git add` them). Better: move the change to your account folder ([docs/work.md](../docs/work.md)), then `git -C ~/omawin stash drop stash@{0}`.
- **`omawin update` kept your commits on a branch `omawin-local/<time>`:** they clashed with the update. `git -C ~/omawin log omawin-local/<time>` shows them; take what you need into your account folder, then `git -C ~/omawin branch -D omawin-local/<time>`.
- **An older omawin refuses to update ("local changes? commit or stash them first"):** run `git -C ~/omawin stash`, then `omawin update`; from then on it stashes and restores by itself.
- **Something you changed is gone after an update:** look in `git -C ~/omawin stash list`, and in `%LOCALAPPDATA%\omawin-backup\` (what migrations moved aside).
- **RAM:**
  - Each Claude Code session is ~0.9 GB; a PowerShell tab is ~40 MB.
  - Stop idle herdr sessions (`herdr session stop <project>`).
  - `wsl --shutdown` when Ubuntu isn't needed.
