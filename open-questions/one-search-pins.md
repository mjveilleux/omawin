# One Search: what's still the maintainer's to decide

**Question:** [plans/one-search.md](../plans/one-search.md) left four questions open. One Search is built (roadmap 1C) with a default for each; which should change?

**Context:** the window is `shell/menu-app/MainWindow.Search.cs`; the home's places are `shell/menu-app/Search/Home.cs`.

**Options and the defaults built:**
1. **Pinned items on the home** (Ctrl+P on any result). Options: fixed tiles only; or a row of your pins in the home's place for them. **Default, built 2026-10-31:** omawin's tiles stay fixed and yours go in a **Pinned** row added *last*, under Frequent, so pinning never moves a place above it (guardrail 2): up to four, in the order pinned, Ctrl+P on a result (or Ctrl+K's *Pin to the home*) toggles, Ctrl+P on a pin's tile unpins; a full row says so rather than dropping one (`shell/menu-app/Search/Pins.cs`, `%LOCALAPPDATA%\launcher\pins.txt`). Apps, settings, commands, files, projects and themes can be pinned; windows and copies can't (they don't last). The alternative still open: pins *replacing* the "your next four items" row (items.txt's), which would keep the home one row shorter.
2. **Grid views inside filters.** Options: Themes as a grid, everything else a list; or Apps as a grid with nothing typed too. **Default:** Themes is the theme picker's grid; Apps is a list (most used first), which scans faster with a thousand apps.
3. **Window size.** Options: fixed; or growing with the results. **Default:** fixed per showing: the finder's width (980) and the taller of the home and the finder's height, so it never resizes under you (a resized window shows a black band for a frame). Audio and the VPN page keep the narrow window.
4. **Previews on narrow screens.** Options: hide the preview below a width; or show it under the list. **Default:** always beside the list (the window is 980 wide, which fits every screen omawin targets); revisit if a small laptop shows a cramped list.
