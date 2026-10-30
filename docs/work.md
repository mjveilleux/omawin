# Your own settings: the shared folder and the account's

omawin holds nothing that's one person's: no name or email, no work shares or VPN, no paths that exist on one PC only. What's yours goes in two folders of your own, outside the repo. omawin picks up what it finds there and works without them; nothing in them is ever overwritten by an update.

| Folder | What | Where | Who sees it |
|---|---|---|---|
| **shared** | what's yours in every Windows account: Claude Code's rules, skills and settings, launcher items, sites, shortcuts | `C:\ProgramData\omawin\shared`, one per PC (or `OMAWIN_SHARED_LAYER`) | every account on the PC reads it live; only the account that made it (your main one) may change it, since what's in it runs in every account |
| **this account's** | who you are in this account: git name and email, the employer's or client's launcher items and sites, shares, VPN, reports, notes for agents | `~/omawin-account` in that account (or `OMAWIN_USER_LAYER`) | that account only |

Sign-ins are in neither. Claude, GitHub, Slack and Gmail keep them inside each Windows account, so each account signs in as whichever account that work uses (a company's Claude, a client's GitHub), and two accounts may share one.

**Never edit the omawin checkout itself** (`~/omawin`): it's the product, and `omawin update` replaces it. Everything you'd want to change has a place in these folders instead. (If you do edit the checkout, an update sets your changes aside in a git stash and puts them back, or keeps them in the stash and tells you if they clash: [troubleshooting](../manual/12-troubleshooting.md).)

**Where they are:** one definition per language: `bin/user-layer.ps1` (`-Shared` for the shared one; install, `bin/check.ps1`, `themes/render.ps1`, the PowerShell profiles, which also set `$OmawinUserLayer`), `UserLayer` in `bin/omawin.cs` (`{user}/` in `bin/commands.txt`), `MenuModel.Work` and `MenuModel.Shared` in the menu app, and `nvim/lua/config/lazy.lua`. Set an environment variable, then sign out and in. Before 2026-10-17 they were `~/omawin-me` and `~/omawin-work`; the migration `2026-10-17-shared-folder` moved them and left links at the old names.

## What goes where

- **Shared**: what you'd be fine with any employer or client seeing in their account, since every account on the PC reads it. Never secrets (tokens, passwords) and never documents. `omawin check` warns about files that look like either.
- **This account's**: anything tied to one employer or client (their skills, style guides, servers, report paths), and your identity there.
- **A preference anyone could want** (a key, a default, a look): a change to omawin itself.
- **Turning a shared Claude skill off in one account**: its name in that account's `claude/skills-off.txt`.

## Several Windows accounts: personal, a client, a job

| Level | Where | How a change spreads |
|---|---|---|
| omawin | `~/omawin` in **each** account, all on `main` | committed and pushed from one account; the others take it with `omawin update` |
| shared folder | one per PC, changed from its owner | at once: every account reads the same files (a new Claude skill or setting at the account's next sign-in, or `omawin claude sync`) |
| this account's folder | each account's home | it doesn't |
| what apps write | `%LOCALAPPDATA%` of each account | it doesn't |

- **omawin's own clone in each account**, not one shared: its files are live and agents edit them, so one account's experiment would change another's desktop mid-work. Change omawin in one account and update the others; an account whose GitHub sign-in can't push to omawin sends its changes as a pull request.
- **No forks, no profile repos.** A GitHub fork of a public repo is public, and accounts may or may not share a GitHub sign-in, so nothing depends on GitHub: the folders are the source. Each may be backed up to a private repo under any GitHub account: `omawin shared backup -Repo <name>` the first time, `omawin shared backup` after; `omawin account new -Repo <name>` for an account's.
- **Only the owner changes the shared folder.** Its Claude settings (plugins, hooks), skills and launcher items run as every account that reads them, admins included, so a folder any account could write would let the least trusted account (a client's, say) plant something that runs as the most trusted one. So the account that made it, your main one, changes it; the others read it. A shared skill wanted from another account is added from the owner's; `omawin check` warns if the folder is ever open to other accounts.
- **Different omawin versions in two accounts** read the same shared folder, so its layout only grows: nothing in it is renamed or removed without a fallback for older versions.
- **Off limits per account**: each account's folder has a `README.md` that says what agents in that account must never touch (the employer's repos, a client's folders). Agents in one account can't read another account's files at all.
- **Signed in at once**: each account runs its own desktop with nothing shared that can collide: its own GlazeWM port (`bin/ports.ps1`, through our GlazeWM fork; `omawin.exe` decides it at every sign-in from which GlazeWM is there, since another account may have changed it), its own OpenCode server (a free port and password of its own), its own pipe names (they carry the account's SID), its own scheduled tasks (named with the account), and scripts only ever stop that account's programs. Program Files is shared: a fork update there sets a running copy aside instead of stopping another account's.
- **Memory**: each signed-in account runs its own omawin (~0.5 GB, `omawin profile ram`). Sign out of accounts you're not using rather than switching away.
- **Telling accounts apart**: each keeps its own theme (`omawin theme`).

### A new account

1. Make it: Settings › Accounts › Other users › Add account. A standard (not admin) account is fine; installs that need admin ask for an admin's password.
2. Sign in to it, then in Windows PowerShell (not as administrator) run `bootstrap.ps1` ([install.md](install.md); the new account can't read another account's home folder, so put a copy where it can, such as `C:\Users\Public`). It finds the shared folder your main account made and reads it, asks whether this account's folder is in a private repo (Enter: start fresh), then installs, which asks your git name and email for this account.
3. Sign in to Claude, GitHub and the rest in their own apps, as this account's work needs.

On a brand-new PC the first account to install makes the shared folder, empty or from its backup (bootstrap asks), and is its owner: install on your main account first.

Tools already on the PC aren't downloaded again (some per-user ones are: they install per account).

To keep a fresh account's folder in a private repo: `omawin account new -Repo <name>` (asks your git name and email for this account), then `omawin install -LinkOnly`. `omawin shared` shows where the shared folder is, who can change it and its backup; on another PC, `omawin shared -From <owner/name>` starts it from that backup.

## What omawin reads from them

Read from this account's folder, and for the rows marked **shared** from the shared folder too (the account's after it):

| File | What omawin does with it |
|---|---|
| `git/gitconfig` | `~/.gitconfig` includes it after omawin's `config/git/gitconfig` (install, `shell` step): your `[user]` name and email, say. Without it, install asks for your name and email once and writes them to `~/.gitconfig` itself. |
| `claude/settings.json`, `claude/skills/`, `claude/CLAUDE.md` | **shared.** Claude Code (`omawin claude sync`, `bin/claude-sync.ps1`; install's `other` step and every sign-in): each folder's `CLAUDE.md` (your rules) is imported by `~/.claude/CLAUDE.md`, its `skills/<name>/` are linked into `~/.claude/skills`, its `settings.json` is merged into `~/.claude/settings.json` (the account's over the shared one's over what's there; `{home}` becomes the account's home folder), and the plugins those settings list are installed. Not carried: sign-ins and memory (Claude Code keeps it per project folder). |
| `claude/skills-off.txt` | account only: shared skills this account goes without, one name a line |
| `wsl/.wslconfig` | copied to `~/.wslconfig` instead of omawin's (install, `other` step), e.g. to cap WSL's memory |
| `launcher/items.txt` | **shared.** adds its items to the launcher, after omawin's own (same format as omawin's `default/launcher/items.txt`) |
| `launcher/sites.txt` | **shared.** adds its sites to launcher > Localhost, after omawin's (same format as omawin's `default/launcher/sites.txt`) |
| `launcher/keys.txt` | **shared.** adds its lines to the Shortcuts page |
| `powershell/profile.ps1` | dot-sourced at the end of the PowerShell profile, in interactive shells: your shortcuts and aliases |
| `powershell/all-hosts.ps1` | dot-sourced by `powershell/profile.ps1` in every PowerShell host, scripts and agents too: tool paths and functions those need (a classic `sqlcmd`, say) |
| `glazewm/config.yaml` | added to GlazeWM's config each time it's rendered (`themes/render.ps1`: every theme switch, `omawin restart glazewm`, install). Top-level `keybindings:` and `window_rules:` lists, in GlazeWM's own format (theme placeholders like `{{ accent }}` work), go after omawin's. A key you bind replaces omawin's binding for it; an item with `commands: []` just frees its keys. Write bindings on one line, `bindings: ['alt+x']`. Anything else in the file is ignored with a warning, and a file render can't use is reported and left out, never costing you the desktop. Example below. |
| `nvim/lua/user/plugins/*.lua` | lazy.nvim plugin specs, loaded after omawin's (`config/nvim/lua/config/lazy.lua`): add plugins, or override any plugin's or LazyVim's `opts` and `keys`. The account folder's `nvim/` is on the runtimepath. |
| `nvim/lua/user/init.lua` | runs once LazyVim is up (its `VeryLazy`, after LazyVim's own options and keymaps), so your `vim.opt` and `vim.keymap.set` lines win |
| `reports/reports.ps1` | the **Reports** page: `-Index` writes `%LOCALAPPDATA%\launcher\reports.tsv` (the menu app runs it hidden every 2 hours; `omawin reports index` runs it now), `-Import <file>` handles a pick, in a small terminal. Without it, the page and its item are hidden. |
| `README.md` | your notes; agents working on omawin read it, so say there what's off limits on your PC (work repos never to run git in, files never to commit) |

A GlazeWM example, `glazewm/config.yaml`:

```yaml
keybindings:
  # Alt+E opens the editor; Alt+M no longer minimizes.
  - commands: ['shell-exec --hide-window omawin.exe launch editor']
    bindings: ['alt+e']
  - commands: []
    bindings: ['alt+m']
window_rules:
  - commands: ['set-floating --centered']
    match:
      - window_process: { equals: 'KeePassXC' }
```

What isn't here yet: menu rows (`default/launcher/menu.txt`), the bar's layout, noctty's and herdr's configs, starship. Change those in the checkout only if you're ready to resolve them after an update, or ask for a hook.

What apps write isn't in either folder; it's outside both: Windows Terminal's `settings.json` (its LocalState, seeded once from omawin's), Neovim's `lazy-lock.json` and `lazyvim.json` (`%LOCALAPPDATA%\nvim-data`), pwsh's `powershell.config.json` and modules (`Documents\PowerShell`).

The Reports page itself (`shell/menu-app/`: `Pages.cs` `Reports`, `MainWindow.Pages.cs`) is omawin's: a searchable list of `reports.tsv`, newest first. Each line is `<modified, unix time> TAB <size> TAB <tag> TAB <path>`; the tag colors the row. A reports script can use omawin's `launcher/common.ps1` for its terminal look.

Which presets you picked (`omawin preset`) isn't in either folder: it's per account, in `%LOCALAPPDATA%\launcher\presets.txt`.
