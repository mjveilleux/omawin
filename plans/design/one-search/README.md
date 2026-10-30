# One Search mockup (source)

The clickable mockup of [One Search](../../one-search.md), kept in the repo so any session (or person) can read it without the private link ([omawin One Search](https://claude.ai/artifact/FCAA1kuXE7H6BjjfVzrN8z)).

- `mockup.dc.html`: the whole mockup in one file: the markup (search bar with the filter token, the filter tabs, the bento home, the result list with tags, the preview pane, the actions panel, the footer of keys) and, in the `<script type="text/x-dc">` block at the bottom, its logic in plain JavaScript: the sample items (`ITEMS`), the tags and their colours (`TAGS`), the per-tag caps (`CAPS`), the fuzzy matcher (`fuzzy`), the ranking and merging (`compute`), the prefixes (`/` files, `>` commands), Tab / arrow / Enter / Esc / Backspace / Ctrl+K / Ctrl+Z handling (`onKey`), and settings switching with undo.
- It's written in a design-canvas format (`<x-dc>`, `{{holes}}`, `<sc-for>`, `<sc-if>`), not WinUI: read it as the **reference for behaviour and look** (spacing, type, colours, states), not as code to port line by line. The real thing is C# / WinUI in `shell/menu-app/` ([one-search.md](../../one-search.md), "Building it").
- Colours are the Tokyo Night palette (`themes/tokyo-night.json`); in the menu app they come from the current theme.
