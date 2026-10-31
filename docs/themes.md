# Themes

Omarchy's themes, applied to everything at once. For picking one, see [manual/08-themes.md](../manual/08-themes.md).

## Files

| File | What it is |
|---|---|
| `themes/<name>.json` | a theme's palette, wallpapers and mode, imported from Omarchy |
| `themes/nvim/<name>.lua` | its Neovim colorscheme spec |
| `themes/btop/<name>.theme` | its btop theme |
| `themes/colors/<name>.toml` | its palette in Omarchy's own format, which `themes/render.ps1` fills the templates from (below) |
| `themes/vscode/<name>.json` | the theme's own VS Code extension, when Omarchy names one |
| `themes/vscode/<name>.color-theme.json` | otherwise, a VS Code theme made from the palette with Omarchy's template, installed as a local "Omarchy" extension |

`themes\convert-theme.ps1` makes these from one Omarchy theme folder, with Omarchy's own templates (`themes/templates/omarchy`) for the files a theme doesn't ship. It reads both of Omarchy's palette formats (named colors, or the older `color0`..`color15`) and, for the oldest themes, `alacritty.toml`, filling in missing colors the way Omarchy's `omarchy-theme-color` does, and writes the full palette to `colors/<name>.toml`. Only colors are taken from a palette: they're filled into GlazeWM's and the terminal's configs.

`themes\import-omarchy.ps1` (`omawin theme import`, for maintainers) updates an Omarchy checkout, copies its templates in and converts each of its themes into this repo, saying which are new. Rerun it when Omarchy adds themes.

## Installed themes

Like Omarchy's `omarchy theme install`, any theme made for Omarchy installs from its git link, and a community theme by its name (`themes\theme.ps1`: `omawin theme add|install|search|preview|update|remove|list|browse`; Style › Theme › Community, Style › Install a theme). Omarchy keeps installed themes in `~/.config/omarchy/themes`; here:

| Path | What it is |
|---|---|
| `%LOCALAPPDATA%\launcher\themes\sources\<name>` | the theme's git clone (`theme update` fetches its repo's default branch, shows the commits and files that changed, asks in a terminal, then fast-forwards) |
| `%LOCALAPPDATA%\launcher\themes\<name>.json`, `colors\`, `nvim\`, `btop\`, `vscode\` | its files, made by `convert-theme.ps1 -Untrusted`, in the same layout as `themes/` |

Every reader looks there first, then in `themes/` (`set-theme.ps1` and `render.ps1`'s `Find-ThemeFile`, the menu app's `MenuModel.ThemeFile`, `default/launcher/common.ps1`, `config/nvim/lua/live_theme.lua`), so an installed theme with one of Omarchy's names replaces it, as in Omarchy. Its wallpapers are used from the clone.

As Omarchy does with installed themes, nothing that runs code is taken from them: their `neovim.lua` is replaced by one made from the palette (Neovim runs Lua) and their `vscode.json` (an extension to install) is left out; `btop.theme` keeps only its `theme[...]` color lines. The theme's name comes from the link (`omarchy-` and `-theme` dropped) and is held to `[a-z0-9_][a-z0-9._+-]*`; the link must be a network git URL (no options, local paths or transport helpers).

## Community themes (the index)

The catalog is a web page (omarchy.org/themes, static HTML), so CI reads it once a week and publishes what the picker needs as a list a program can read (roadmap 1.10):

| Piece | What it does |
|---|---|
| `src/community-themes` (C#, SkiaSharp) | reads the page's cards (`shell/menu-app/Community.cs`, `CommunityList.Parse`) and `themes/community-extra.txt` (hand-kept repos); for each repo, at its default branch's current commit (GitHub's API; when that refuses, `git ls-remote` and raw files, without stars, licence or backgrounds): the palette from `colors.toml` or `alacritty.toml`, read and filled in exactly as `convert-theme.ps1` does (`ThemePalette`, unit-tested against every theme this repo ships), `light.mode`, the backgrounds' names, owner, licence and stars; a 320-pixel JPEG of omarchy.org's screenshot (else the repo's `preview.png`, else its first background). Writes `index.json` + `thumbs\` and `community-themes.zip` with its SHA-256 |
| `.github/workflows/community-themes.yml` | runs it every Monday and uploads both files to the release `community-themes` (a pre-release, never "latest", so `omawin update` never sees it; `release.yml`'s drop-old keeps it) |
| `shell/menu-app/CommunityThemes.cs` | fetches the checksum a minute after start-up and daily, and the zip when it changed (not on a metered connection once there's an index), checks it, unpacks it aside and swaps it into `%LOCALAPPDATA%\launcher\community` |
| `shell/menu-app/MainWindow.Community.cs` | the picker's Community chip (below) |
| `themes\theme.ps1` | `add <name>` installs at the index's commit (`git init` + `fetch --depth 1 origin <commit>`, origin kept for `update`), `search`, `preview` |

The index holds only data: names, the palette (each value a `#rrggbb`), raw URLs of pictures at the pinned commit. Installing re-reads the theme from its repo at that commit, through the same `convert-theme.ps1 -Untrusted` as any link.

**Preview without installing.** The picker writes the theme it rests on into `%LOCALAPPDATA%\launcher\theme-preview` (or deletes it to end) and runs `omawin theme preview`, which applies whatever that file says, one at a time (a named mutex), so the last one asked for is what stays however fast you move. Applying: a `colors.toml` of the index's palette goes through `convert-theme.ps1` into `%LOCALAPPDATA%\launcher\community-preview` (apart from installed themes, so it never shows as one), then `set-theme.ps1 <name> -Preview -From <that folder>`: `render.ps1 -From` (GlazeWM, noctty, Windows Terminal), their reloads, and the wallpaper (downloaded into the preview folder). Nothing is remembered (`launcher\theme`, `background`), and Windows' mode, the lock screen, editors and btop are left alone. Ending runs `set-theme.ps1 -Preview`: your theme's colours and the background you were on. `community-preview\applied` names what's on screen; the menu app puts yours back at start-up if a preview was left there.

## Applying one

`themes\set-theme.ps1 <name> [-Background <part of a name>]` (`omawin theme set`; the system menu's Style > Theme runs it) does, in order (the colors first, so what you see changes within about a second; measured: the bar and dock ~0.9 s after the pick, ~0.5 s of it PowerShell starting):

1. **The menu app** repaints at once, before the script runs: it reads `%LOCALAPPDATA%\launcher\theme`.
2. The pick goes in `%LOCALAPPDATA%\launcher\theme` and `nvim-theme`. **Neovim / Neovide**: `config/nvim/lua/live_theme.lua` watches `nvim-theme` and switches colorscheme in every open instance; every theme's colorscheme plugin is installed.
3. **Dark or light mode**, for apps and the system.
4. **GlazeWM, noctty, Windows Terminal, the bar**: `themes/render.ps1` writes their copies of the palette (below). Then, without waiting: GlazeWM reloads its config, and the running noctty is told to reload its own (`noctty +perform-action reload_config`), so open windows recolor and new windows and tabs start in the new theme. Windows Terminal watches only `settings.json`, not its fragments, so render.ps1 bumps that file's time (not its contents) and open windows reload. The bar (the menu app) watches `%LOCALAPPDATA%\launcher\theme` (Windows' file change notifications, so nothing runs in between) and repaints in the new palette; the sign-in strip's colour is `bar-background.txt`, written by render.ps1.
5. **Wallpaper**: Omarchy's backgrounds for the theme, downloaded once into `%LOCALAPPDATA%\launcher\backgrounds`.
6. **Lock screen**: the same picture (`themes/set-lockscreen.ps1`, in the background). The task "dotfiles lock screen" moves on to the theme's next background at each unlock.
7. **RStudio**: an editor theme made from the palette in `%APPDATA%\RStudio\themes`, picked in `rstudio-prefs.json`. An open RStudio switches within 2 s through `config/r/rprofile.R`, which needs the `rstudioapi` and `later` packages.
8. **btop**: picks up `themes/btop/<name>.theme` from its next start.
9. **VS Code and Positron**: the theme's extension (Positron's from Open VSX) or the generated one; `workbench.colorTheme` is set and open windows switch.

The shell itself has no colors of its own: PSReadLine, fzf, bat and starship use the terminal's 16 ANSI colors, so a shell started before a switch (noctty's spare shell) shows the new theme as soon as its terminal has it.

Known: noctty's project windows (`omawin launch project`) are separate noctty instances (`--single-instance=false`), which a reload request doesn't reach; they keep their colors until reopened.

`-NextBackground` (`omawin theme next-background`, Win+Ctrl+Space) moves to the current theme's next background. `-Background <name>` with no theme named (`omawin theme background`, the background picker's Enter) sets one of the current theme's: its whole name first (`1-leaves`, not `11-leaves`), then any that contains it. Both change only the wallpaper and lock screen (steps 5-6), not the colors.

The background picker (System menu › Style › Backgrounds, `omawin theme backgrounds [theme]`; `MainWindow.Tiles.cs`) shows a theme's backgrounds from `%LOCALAPPDATA%\launcher\thumbnails\<theme>--<picture>.png`: 320 pixels wide, made for every background of every theme in CI (`src/thumbs`, ~80 pictures, ~6 MB) and shipped in the tools bundle as `theme-thumbs`; `ThemeThumbs.cs` makes one itself only when it's missing (a theme installed later). Both name them with `shell/menu-app/ThemePictures.cs`, which `src/thumbs` compiles too. The check comes from `%LOCALAPPDATA%\launcher\background`, the source set-theme.ps1 last set.

## Templates: nothing in the repo changes on a switch

Like Omarchy, a theme is one palette and a switch only writes state outside the repo. Most apps read the pick and the palette themselves. Those that can't get a copy rendered from a template by `themes/render.ps1`, with Omarchy's placeholders (`{{ accent }}`, `{{ background }}`, `{{ bright_red }}`: the keys of `colors.toml`):

| Template (in the repo) | Rendered to (outside it) | Read by |
|---|---|---|
| `config/glazewm/config.yaml` | `~/.glzr/glazewm/config.yaml` | GlazeWM |
| `themes/templates/windows-terminal.json` | `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\omawin\theme.json` (the scheme `omawin`) | Windows Terminal (`settings.json` uses `"colorScheme": "omawin"`) |
| `themes/templates/noctty.ghostty` | `%LOCALAPPDATA%\launcher\theme.ghostty` | noctty (its config stub includes it) |
| (in `render.ps1`, from `themes/<name>.json`) | `%LOCALAPPDATA%\launcher\bar-background.txt` | the strip `omawin start bar` paints at sign-in, before the bar is up |

After editing `config/glazewm/config.yaml`, press Alt+Shift+R (`omawin restart glazewm`): it renders, then reloads. A config never holds a theme's color itself.

The default is **Starship on 1-starbase**, which `install.ps1` applies when no theme is set.
