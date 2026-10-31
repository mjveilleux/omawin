# Linux as the default shell: for existing PCs too, and which side do projects open on?

**Context.** Roadmap 3.4 makes Linux the default shell: `bin/defaults.txt` now has a `shell` kind with `linux` first, so once the picked distro is registered, Win+Enter, Alt+Enter, the dock's terminal and `omawin launch terminal` open Linux. That includes PCs that already have omawin's Linux (the `wsl` preset): after the update their next terminal is Linux. The key itself is unchanged, and `omawin default shell windows` (System menu › Setup › Defaults › Shell) brings PowerShell back. Separately, the plan said projects open in Linux by default too (a Windows folder through `/mnt/c`); git over `/mnt/c` is slow in big repos (the roadmap's own risk), and the launcher already marks each repo's side.

**Options.**
- **Linux for everyone with a Linux** (as built): new and existing PCs alike; the update's summary and the manual say how to go back.
- **Linux for new installs only:** a migration writes `defaults\shell = windows` on PCs that had omawin before this change, so nothing moves under an existing user.
- **Projects:** on the side the folder is on (as built), or Linux for every project (the plan's first sketch).

**Default meanwhile:** Linux for everyone with omawin's Linux registered; PowerShell, silently, without one. Projects open on the side their folder is on (`--wsl` / `--windows` override). Commands run in a terminal (`-- <command>`) are Windows programs unless `--wsl`.
