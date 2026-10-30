# Repo layout

Status: **done 2026-10-31.** The top level had grown to about 45 entries: entry scripts, app configs, compiled projects, data files and docs side by side. Omarchy keeps a dozen, each with one job. omawin now follows it.

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

## omawin's tree

```
omawin/
  README.md  AGENTS.md  CLAUDE.md  LICENSE  NOTICE  SECURITY.md  global.json
  bootstrap.ps1  install.ps1          the two entry points stay on top (the one-liner's URL)

  bin/                                the omawin command and what it runs
    omawin.cs  commands.txt  defaults.txt  check.ps1  no-ads.ps1  update.ps1 …

  config/                             app configs, live-linked into place
    glazewm/  nvim/  neovide/  noctty/  powershell/  windows-terminal/
    starship/  git/  bash/  herdr/  fastfetch/  claude/  r/

  default/                            omawin's own data
    launcher/                         menus, items, sites, keys, settings, and their scripts
    bar/                              bar.json (the layout), omarchy.ttf (the logo)

  shell/                              the desktop, after Omarchy's shell/
    menu-app/                         the native app: bar, launcher, menus, screenshots
    plugins/<id>/                     Omarchy's plugin format (manifest.json, QML)

  src/                                other compiled code, with its scripts
    cli/                              the C# command router
    music/                            the music service (mpv, yt-dlp, feeds)
    agent/                            omawin's agent (Ctrl+Win+H) and omawin-listen.cs
    bundle/                           the tools bundle builder

  install/                            steps, steps.txt, packages.ps1, tools.txt
  migrations/  build/  themes/  wsl/
  test/                               cli.Tests/, menu-app.Tests/
  docs/  manual/  plans/  open-questions/
```

Top level: 23 entries instead of 45, and each folder answers one question: a command (`bin/`), an app's config (`config/`), omawin's data (`default/`), the desktop (`shell/`), other compiled code (`src/`), setup (`install/`, `migrations/`), looks (`themes/`), words (`docs/`, `manual/`, `plans/`).

Where it differs from the first proposal: the agent's config and prompt stay with its scripts in `src/agent/` (one folder per part is easier to find than data split from code), there's no solution file (each project builds alone, as CI does), and `agents/skills/` waits until there are task guides to put in it (AGENTS.md holds them for now).

## How it moved without breaking a running PC

Apps read configs through links into `~/omawin`, so a moved folder breaks a link until it's relinked. `omawin update` pulls, then runs `install.ps1 -LinkOnly`, which relinks everything at its new place (stale links are replaced: `Set-Junction`), rewrites the stubs (PowerShell profile, Git Bash, noctty), resets `STARSHIP_CONFIG` and `HERDR_CONFIG_PATH`, adds git's include of `config/git/gitconfig`, and reruns WSL's setup (which relinks `~/.config/nvim`). Then `migrations/2026-10-31-repo-layout.ps1`:

- drops git's include of the old `git/gitconfig` path;
- links `~/omawin/launcher` and `~/omawin/bar` to their new places, for a menu app built before the move (taken from an older release on a PC with no .NET SDK). Ignored by git. A later migration removes them once a release from after the move is out.

The account and shared folders ([docs/work.md](../docs/work.md)) keep their own layout (`launcher/`, `nvim/`, `git/`, …): they're the user's, so nothing there moved.
