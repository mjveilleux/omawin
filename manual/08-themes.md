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

## More themes

Beyond Omarchy's own, any theme made for Omarchy installs from its git link, as in Omarchy: find one on [Omarchy's extra themes page](https://omarchy.org/themes/) (Style › More themes), then Style › Install a theme and paste its link, or:

```
omawin theme install https://github.com/<someone>/omarchy-<name>-theme
omawin theme update                             every installed theme, from its repo
omawin theme remove <name>
```

It's applied at once and shows in Style › Theme with the others, everywhere Omarchy's do. An installed theme with the name of one of Omarchy's replaces it until you remove it. An installed theme can't run anything: its colors and wallpapers are used, but its own Neovim and VS Code settings are made from its colors instead (Omarchy does the same).

Starship, on its starbase background, is the default; a new machine starts with it. How it works: [docs/themes.md](../docs/themes.md).
