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

`themes\import-omarchy.ps1` (`omawin theme import`) reads an Omarchy checkout and writes all of these. Rerun it for new Omarchy themes.

## Applying one

`themes\set-theme.ps1 <name> [-Background <part of a name>]` (`omawin theme set`; the system menu's Style > Theme runs it) does, in order (the colors first, so what you see changes within about a second; measured: the bar and dock ~0.9 s after the pick, ~0.5 s of it PowerShell starting):

1. **The menu app** repaints at once, before the script runs: it reads `%LOCALAPPDATA%\launcher\theme`.
2. The pick goes in `%LOCALAPPDATA%\launcher\theme` and `nvim-theme`. **Neovim / Neovide**: `nvim/lua/live_theme.lua` watches `nvim-theme` and switches colorscheme in every open instance; every theme's colorscheme plugin is installed.
3. **Dark or light mode**, for apps and the system.
4. **GlazeWM, noctty, Windows Terminal, Zebar**: `themes/render.ps1` writes their copies of the palette (below). Then, without waiting: GlazeWM reloads its config, and the running noctty is told to reload its own (`noctty +perform-action reload_config`), so open windows recolor and new windows and tabs start in the new theme. Windows Terminal watches only `settings.json`, not its fragments, so render.ps1 bumps that file's time (not its contents) and open windows reload. The bar and dock swap the new `theme.css` in (`zebar/tokyo-islands/theme.js`), told by `theme-watch.exe` (Windows' file change notifications, so nothing runs in between); Zebar isn't restarted (that took ~3 s, with the bar gone meanwhile). Polling the file every 250 ms instead cost ~4% of a core.
5. **Wallpaper**: Omarchy's backgrounds for the theme, downloaded once into `%LOCALAPPDATA%\launcher\backgrounds`.
6. **Lock screen**: the same picture (`themes/set-lockscreen.ps1`, in the background). The task "dotfiles lock screen" moves on to the theme's next background at each unlock.
7. **RStudio**: an editor theme made from the palette in `%APPDATA%\RStudio\themes`, picked in `rstudio-prefs.json`. An open RStudio switches within 2 s through `r/rprofile.R`, which needs the `rstudioapi` and `later` packages.
8. **btop**: picks up `themes/btop/<name>.theme` from its next start.
9. **VS Code and Positron**: the theme's extension (Positron's from Open VSX) or the generated one; `workbench.colorTheme` is set and open windows switch.

The shell itself has no colors of its own: PSReadLine, fzf, bat and starship use the terminal's 16 ANSI colors, so a shell started before a switch (noctty's spare shell) shows the new theme as soon as its terminal has it.

Known: noctty's project windows (`omawin launch project`) are separate noctty instances (`--single-instance=false`), which a reload request doesn't reach; they keep their colors until reopened.

`-NextBackground` (`omawin theme next-background`, Win+Ctrl+Space) moves to the current theme's next background.

## Templates: nothing in the repo changes on a switch

Like Omarchy, a theme is one palette and a switch only writes state outside the repo. Most apps read the pick and the palette themselves. Those that can't get a copy rendered from a template by `themes/render.ps1`, with Omarchy's placeholders (`{{ accent }}`, `{{ background }}`, `{{ bright_red }}`: the keys of `colors.toml`):

| Template (in the repo) | Rendered to (outside it) | Read by |
|---|---|---|
| `glazewm/config.yaml` | `~/.glzr/glazewm/config.yaml` | GlazeWM |
| `themes/templates/windows-terminal.json` | `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\omawin\theme.json` (the scheme `omawin`) | Windows Terminal (`settings.json` uses `"colorScheme": "omawin"`) |
| `themes/templates/noctty.ghostty` | `%LOCALAPPDATA%\launcher\theme.ghostty` | noctty (its config stub includes it) |
| (in `render.ps1`, from `themes/<name>.json`) | `zebar/tokyo-islands/theme.css` (gitignored: Zebar only serves its pack's folder) | the bar and dock (`styles.css` has no colors of its own) |

After editing `glazewm/config.yaml`, press Alt+Shift+R (`omawin restart glazewm`): it renders, then reloads. A config never holds a theme's color itself.

The default is **Starship on 1-starbase**, which `install.ps1` applies when no theme is set.
