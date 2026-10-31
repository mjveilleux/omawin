# One Search: what's still the maintainer's to decide

**Question:** [plans/one-search.md](../plans/one-search.md) left four questions open. One Search is built (roadmap 1C) with a default for each; which should change?

**Context:** the window is `shell/menu-app/MainWindow.Search.cs`; the home's places are `shell/menu-app/Search/Home.cs`.

**Options and the defaults built:**
1. **Pinned items on the home** (Ctrl+P on any result). Options: fixed tiles only; or a row of your pins in the home's place for them. **Default:** fixed tiles only, no Ctrl+P. Your own tiles come from `items.txt` (its first item is the big tile, the next four the row under it), which already lets you choose; a pins row would move Frequent down, so it waits for a decision.
2. **Grid views inside filters.** Options: Themes as a grid, everything else a list; or Apps as a grid with nothing typed too. **Default:** Themes is the theme picker's grid; Apps is a list (most used first), which scans faster with a thousand apps.
3. **Window size.** Options: fixed; or growing with the results. **Default:** fixed per showing: the finder's width (980) and the taller of the home and the finder's height, so it never resizes under you (a resized window shows a black band for a frame). Audio and the VPN page keep the narrow window.
4. **Previews on narrow screens.** Options: hide the preview below a width; or show it under the list. **Default:** always beside the list (the window is 980 wide, which fits every screen omawin targets); revisit if a small laptop shows a cramped list.
