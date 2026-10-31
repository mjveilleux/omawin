# Themes

**System menu › Style › Theme**: Omarchy's themes, one pick for everything. Each tile shows the theme's first wallpaper and its colors. A theme sets:
- the wallpaper and the lock / sign-in screen (each unlock moves the lock screen to the theme's next background)
- dark or light mode
- the bar and dock, GlazeWM's borders, and the terminal's colors (noctty and Windows Terminal; open windows too), all within about a second
- the launcher and the menu
- Neovim and Neovide, which switch live, open ones included
- RStudio (within 2 s), VS Code and Positron
- btop, from its next start

**Win+Ctrl+Space** (or Style › Next background) cycles the theme's backgrounds.

**System menu › Style › Backgrounds** shows them all at once: a grid of your theme's wallpapers, the one on your desktop checked. Arrow keys move, Enter (or a click) sets one and leaves the grid open so you can try another; only the wallpaper and the lock screen change. Type to narrow it by the picture's name. **Tab** (Shift+Tab back) shows the next theme's wallpapers: picking one of those switches to that theme, on that wallpaper. Every picture comes with omawin, so the grid is complete as soon as it opens.

From a terminal:

```
omawin theme set gruvbox                        a theme
omawin theme set gruvbox -Background village    on a background (part of its name)
omawin theme next-background
omawin theme background village                one of your theme's backgrounds (part of its name)
omawin theme backgrounds                        the background picker (or: omawin theme backgrounds gruvbox)
omawin theme list                               every theme; * is the one in use
```

## Community themes

Beyond Omarchy's own, the community's: every theme on [omarchy.org/themes](https://omarchy.org/themes/), right in the picker. In **Style › Theme**, press **Tab** (or click **Community**): each theme's picture and colours, most popular first. Type to narrow them by name, by author, or by `light` / `dark`. The footer says whose it is, its licence and its repo.

Rest on one for a moment and your desktop takes it on: GlazeWM's borders, the terminals and the wallpaper, so you see it before you have it. Nothing is installed; moving on shows the next, and Tab back, Esc or closing the menu puts yours back. **Enter** installs it and keeps it (exactly the version omawin's list checked); **Ctrl+Enter** opens its repo.

The list is rebuilt every week and comes down in the background, a minute after the menu starts and then once a day; until it's there (a new PC offline), the Community chip says so.

From a terminal:

```
omawin theme search nord                        community themes matching every word (name, author, light, dark)
omawin theme preview <name>                     show one across the desktop; omawin theme preview off puts yours back
omawin theme add <name>                         install one by name
omawin theme add https://github.com/<someone>/omarchy-<name>-theme    any theme, from its link
omawin theme update                             every installed theme: shows what changed, asks, updates
omawin theme remove <name>
```

`omawin theme install` is the same as `add`. Style › Install a theme takes a name or a link too.

An installed theme is applied at once and shows in Style › Theme with the others, everywhere Omarchy's do; `omawin theme list` says which repo each came from. An installed theme with the name of one of Omarchy's replaces it until you remove it. An installed theme can't run anything: its colors and wallpapers are used, but its own Neovim and VS Code settings are made from its colors instead (Omarchy does the same).

Starship, on its starbase background, is the default; a new machine starts with it. How it works: [docs/themes.md](../docs/themes.md).
