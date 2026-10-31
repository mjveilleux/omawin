# `omawin pkg update` with no name: what omawin put in, or everything?

**Question:** the roadmap (2B) says `omawin pkg update` updates "everything, each through its own backend", and Update › All apps (5.9) is meant to be it. Should a bare `omawin pkg update` also update programs you installed yourself, outside omawin (winget's, Scoop's, mise's own lists)?

**Context:** `bin/pkg-update.ps1`, which `omawin pkg update` replaces (now `src/cli/Pkg/`), updated only what omawin had recorded and left "found" programs alone (AGENTS.md: never take over what's the user's). `omawin pkg list --refresh` now records everything every manager has, so "everything" is one flag away.

**Options:**
1. Bare `update` = what omawin put in; `update --all` = every manager's update-all (`winget upgrade --all`, `scoop update *`, `mise upgrade`; Chocolatey's only with `--from choco`, since it asks for admin).
2. Bare `update` = everything every manager has.

**Default:** 1. Update › All apps (5.9) can call `omawin pkg update --all`.
