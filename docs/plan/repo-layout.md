# Repo layout: a proposal

Status: **proposed 2026-10-30, not moved yet.** omawin's top level has grown to about 45 entries: entry scripts, app configs, compiled projects, data files and docs side by side. Omarchy keeps a dozen, each with one job. This is the tree we'd move to, after Omarchy's, and how to get there without breaking a running desktop.

## What Omarchy does

| Omarchy | Holds |
|---|---|
| `bin/` | every command (`omarchy-*`) |
| `config/` | the default configs copied to `~/.config/` (the user's to change) |
| `default/` | what Omarchy owns and the user doesn't edit: keybindings, `themed/*.tpl` templates, the menu definition |
| `shell/` | the desktop: `shell.qml`, `Commons/`, `Ui/`, `services/`, `plugins/<id>/` (bar, dock, menu, clipboard, agents…, each with `manifest.json`) |
| `install/` · `migrations/` | setup and one-time changes |
| `themes/` | one folder per theme |
| `docs/` · `manual/` · `agents/skills/` · `plans/` | reference · user guide · task procedures · plans |
| `test/` | test runners and suites |

## Proposed omawin tree

```
omawin/
  README.md  AGENTS.md  CLAUDE.md  LICENSE  NOTICE  SECURITY.md
  bootstrap.ps1  install.ps1          the two entry points stay on top (the one-liner's URL)
  global.json  omawin.sln             one solution for every C# project

  bin/                                the omawin command and what it runs
    omawin.cs  commands.txt  defaults.txt
    check.ps1  no-ads.ps1  …          (from the top level)

  config/                             app configs, live-linked into place
    glazewm/  nvim/  neovide/  noctty/  powershell/  windows-terminal/
    starship/  git/  bash/  herdr/  fastfetch/  claude/  r/

  default/                            omawin's own data, not edited by you
    launcher/                         items, menu, keys, sites, settings (from launcher/)
    bar/bar.json
    agent/                            config.json, prompt.md

  shell/                              the desktop, after Omarchy's shell/
    menu-app/                         the native app: bar, launcher, menus, screenshots
    plugins/                          one folder each, Omarchy's manifest.json
      music/                          the music player (QML), its service in src/music
      agents/                         the agents drop-down

  src/                                compiled code that isn't the shell
    cli/                              the C# command router
    bundle/                           the tools bundle builder (from build/bundle)
    music/                            the music service (mpv, yt-dlp, feeds)
    helpers/                          voice-meter.cs, screen.cs, omawin-listen.cs

  install/                            steps, steps.txt, packages.ps1, tools.txt (from bundle/)
  migrations/
  build/                              release.ps1, helpers.ps1 (CI's scripts)
  themes/                             colors/, backgrounds, render.ps1, import-omarchy.ps1
  wsl/

  test/                               cli.Tests/, menu-app.Tests/
  docs/  manual/  plans/              plans/ from docs/plan/
  agents/skills/                      task guides (AGENTS.md links them, as Omarchy's does)
  open-questions/
```

Top level: 20 entries instead of 45, and each folder answers one question: a command (`bin/`), a config you could change (`config/`), omawin's data (`default/`), the desktop (`shell/`), other compiled code (`src/`), setup (`install/`, `migrations/`), looks (`themes/`), words (`docs/`, `manual/`, `plans/`).

## Moving without breaking a running PC

Apps read configs through junctions into `~/omawin`, so a moved folder breaks a link until it's relinked. One migration per step, each safe twice:

1. **Configs to `config/`**: move the folders; `migrations/<date>-config-folder.ps1` repoints each junction and env var (`STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`, GlazeWM's template, the profile stubs, `~/.bashrc`) by running `install.ps1 -LinkOnly`. `omawin check` must stay at 0 failed.
2. **Data to `default/`**: the menu app reads paths from one place (`menu-app/Paths.cs`); move and update it; `bin/omawin.cs`' paths too.
3. **Code to `src/`, tests to `test/`**, one solution: CI paths only (release.yml, test.yml).
4. **`docs/plan/` to `plans/`**: links only.

Each step is one PR, run on a real PC (`omawin update`) before the next.

## Open

- `config/` vs Omarchy's copy-on-install: omawin links live (you get updates at once; your edits go in your account folder). We keep linking.
- `bootstrap.ps1` and `install.ps1` stay on top: the one-liner's URL and existing docs point at them.
