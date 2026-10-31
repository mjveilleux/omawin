# The bar now hosts plugins that are on (shell/menu-app/Bar/PluginSlot.cs), read from
# %LOCALAPPDATA%\launcher\plugins.txt, which `omawin plugin add|remove` writes. A PC that picked a
# plugin's preset before (the music player's) gets the file from its presets. Safe on a new PC
# (no presets: an empty list) and twice.
& (Join-Path (Split-Path $PSScriptRoot) 'bin\preset.ps1') sync-plugins
