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
omawin theme import                             pick up new Omarchy themes
```

Starship, on its starbase background, is the default; a new machine starts with it. How it works: [docs/themes.md](../docs/themes.md).
