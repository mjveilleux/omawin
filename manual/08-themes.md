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

From a terminal:

```
omawin theme set gruvbox                        a theme
omawin theme set gruvbox -Background village    on a background (part of its name)
omawin theme next-background
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
