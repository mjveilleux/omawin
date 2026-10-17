# The user layer: ~/omawin-work

omawin holds nothing that's one person's: no name or email, no work shares or VPN, no paths that exist on one PC only. What's yours goes in your **user layer**, a folder of your own next to the repo, today `~/omawin-work` (keep it in a private git repo of your own if you like, and clone it there before running `install.ps1`). omawin picks up what it finds there and works without it; nothing in it is ever overwritten by an update.

**Never edit the omawin checkout itself** (`~/omawin`): it's the product, and `omawin update` replaces it. Everything you'd want to change has a place here instead. (If you do edit the checkout, an update sets your changes aside in a git stash and puts them back, or keeps them in the stash and tells you if they clash: [troubleshooting](../manual/12-troubleshooting.md).)

**Where it is:** `~/omawin-work`, or wherever the user environment variable `OMAWIN_USER_LAYER` points (set it, then sign out and in, or restart GlazeWM and the menu app). One definition per language reads it: `bin/user-layer.ps1` (install, `check.ps1`, `themes/render.ps1`, the PowerShell profiles, which also set `$OmawinUserLayer`), `UserLayer` in `bin/omawin.cs` (`{user}/` in `bin/commands.txt`), `MenuModel.Work` in the menu app, and `nvim/lua/config/lazy.lua`.

## One Windows account each: personal, a client, a job

Keeping work apart by Windows account (one for you, one per client or employer) is what the user layer is for. Three levels:

| Level | What | Where | Shared how |
|---|---|---|---|
| omawin | the desktop, the same everywhere | `~/omawin` in **each** account, all on `main` | a change is committed to `main`; each account takes it with `omawin update` |
| your shared layer | what's yours in every account: today Claude Code's (`claude/`, below) | `~/omawin-me` in each account (or `OMAWIN_SHARED_LAYER`), one private repo (`omawin-me`) cloned in each | push in one account, pull and `omawin claude sync` in the others |
| the account's user layer | that account's identity (its email), launcher items and sites, shares, VPN, reports, notes for agents | `~/omawin-work` in that account, kept in **its own private repo** (`omawin-personal`, `omawin-<client>`, `omawin-<employer>`) | it isn't |
| what apps write | settings apps keep, history, indexes | `%LOCALAPPDATA%` of that account | it isn't |

- **A repo per account, not a branch per account.** A clone fetches every branch, so branches would put each client's and employer's files in every account; separate repos keep them apart (and can live under different GitHub accounts when an employer wants its own). Nothing needs a shared history anyway: what's common goes into omawin.
- **Something you want in every account**: a preference anyone could want (a key, a default, a look) is a change to omawin, on `main`. Something that's yours only goes in the shared layer, when omawin reads that file from it (today: `claude/`); for anything else, in each account's layer that wants it, until the shared layer reads it too (`bin/user-layer.ps1 -Shared` is its one definition).
- **Claude Code** (`omawin claude sync`, `bin/claude-sync.ps1`; install runs it): each layer's `claude/CLAUDE.md` (your rules) is imported by `~/.claude/CLAUDE.md`, its `claude/skills/<name>/` are linked into `~/.claude/skills`, its `claude/settings.json` is merged into `~/.claude/settings.json` (the account's over the shared one's over what's there; `{home}` becomes the account's home folder), and the plugins those settings list are installed. A skill with a client's or employer's material (its style guide, its servers) goes in that account's layer, never the shared one. Not carried: sign-ins (sign in to Claude in each account, as whichever Claude account that work uses) and memory (Claude Code keeps it per project folder: it stays with the account the project is in).
- **omawin's own clone in each account**, not one shared: its files are live and agents edit them, so one account's experiment would change another's desktop mid-work, and Windows' per-account file ownership trips git ("dubious ownership"). Changes made in one account reach the others through GitHub.
- **Off limits per account**: each layer's `README.md` says what agents in that account must never touch (the employer's repos, a client's folders). Agents in one account can't read another account's files at all.
- **Memory**: each signed-in account runs its own omawin (~0.5 GB, `omawin profile ram`). Sign out of accounts you're not using rather than switching away.

### A new account

1. Make it: Settings › Accounts › Other users › Add account. A standard (not admin) account is fine; installs that need admin ask for an admin's password.
2. Sign in to it, then in Windows PowerShell run `bootstrap.ps1` ([install.md](install.md); the new account can't read another account's home folder, so put a copy where it can, such as `C:\Users\Public`). It signs in to GitHub, clones omawin, your shared layer (`omawin-me`) and the account's layer (it asks which repo, e.g. `omawin-personal`), then installs.
3. An account with no layer yet: Enter at that question, then `omawin user new -Repo omawin-<name>` makes one (asks this account's name and email for git), and `omawin install -LinkOnly` picks it up.

Tools already on the PC aren't downloaded again (some per-user ones are: they install per account).

What omawin reads from it:

| File | What omawin does with it |
|---|---|
| `git/gitconfig` | `~/.gitconfig` includes it after omawin's `git/gitconfig` (install, `shell` step): your `[user]` name and email, say. Without it, install asks for your name and email once and writes them to `~/.gitconfig` itself. |
| `claude/settings.json`, `claude/skills/`, `claude/CLAUDE.md` | Claude Code's settings (merged), skills (linked) and rules (imported), from this layer and the shared one: `omawin claude sync` (install's `other` step), above |
| `wsl/.wslconfig` | copied to `~/.wslconfig` instead of omawin's (install, `other` step), e.g. to cap WSL's memory |
| `launcher/items.txt` | adds its items to the launcher, after omawin's own (same format as `launcher/items.txt`) |
| `launcher/sites.txt` | adds its sites to launcher > Localhost, after omawin's (same format as `launcher/sites.txt`) |
| `launcher/keys.txt` | adds its lines to the Shortcuts page |
| `powershell/profile.ps1` | dot-sourced at the end of the PowerShell profile, in interactive shells: your shortcuts and aliases |
| `powershell/all-hosts.ps1` | dot-sourced by `powershell/profile.ps1` in every PowerShell host, scripts and agents too: tool paths and functions those need (a classic `sqlcmd`, say) |
| `glazewm/config.yaml` | added to GlazeWM's config each time it's rendered (`themes/render.ps1`: every theme switch, `omawin restart glazewm`, install). Top-level `keybindings:` and `window_rules:` lists, in GlazeWM's own format (theme placeholders like `{{ accent }}` work), go after omawin's. A key you bind replaces omawin's binding for it; an item with `commands: []` just frees its keys. Write bindings on one line, `bindings: ['alt+x']`. Anything else in the file is ignored with a warning, and a file render can't use is reported and left out, never costing you the desktop. Example below. |
| `nvim/lua/user/plugins/*.lua` | lazy.nvim plugin specs, loaded after omawin's (`nvim/lua/config/lazy.lua`): add plugins, or override any plugin's or LazyVim's `opts` and `keys`. The user layer's `nvim/` is on the runtimepath. |
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

What isn't here yet: menu rows (`launcher/menu.txt`), the bar's layout, noctty's and herdr's configs, starship. Change those in the checkout only if you're ready to resolve them after an update, or ask for a hook.

What apps write isn't in the user layer either; it's outside both: Windows Terminal's `settings.json` (its LocalState, seeded once from omawin's), Neovim's `lazy-lock.json` and `lazyvim.json` (`%LOCALAPPDATA%\nvim-data`), pwsh's `powershell.config.json` and modules (`Documents\PowerShell`).

The Reports page itself (menu-app: `Pages.cs` `Reports`, `MainWindow.Pages.cs`) is omawin's: a searchable list of `reports.tsv`, newest first. Each line is `<modified, unix time> TAB <size> TAB <tag> TAB <path>`; the tag colors the row. A reports script can use omawin's `launcher/common.ps1` for its terminal look.

Which presets you picked (`omawin preset`) isn't in the user layer: it's per PC, in `%LOCALAPPDATA%\launcher\presets.txt`.
