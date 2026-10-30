# How should plugin panels be written?

**Question:** omawin plugins ([docs/roadmap.md](../docs/roadmap.md), phase 3) keep Omarchy's `manifest.json`. Bar widgets are commands that print JSON lines. What should a plugin's *panel* (a drop-down, a page) be written in?

**Context:** the menu app is Native AOT, so it can't load a plugin's .NET DLL. Omarchy's plugins are QML, which doesn't run here.

**Options:**
1. **Loose XAML** loaded by the shell (`XamlReader.Load`), bound to JSON state the plugin's command prints. Native look and theme brushes for free, nothing to compile; but XAML is verbose, and logic stays in the command.
2. **A small declarative format** (rows, toggles, sliders, lists in JSON) that the shell draws. Easiest to write and to keep consistent; limited.
3. **Out-of-process plugins**: their own exe with their own window. Anything goes, but each costs a process (~100 MB for WinUI) and won't feel like part of the shell.

**Default:** 1 for panels, with 2 as a shorthand for simple settings panels later. Commands for widgets and services either way.
