# Where your user layer is (docs/work.md): what's yours and not omawin's, in a folder of your own
# next to the repo. The one PowerShell definition of that path: install (install/lib.ps1),
# check.ps1, themes/render.ps1, update and the PowerShell profiles all ask this script.
# OMAWIN_USER_LAYER overrides it (bin/omawin.cs and the menu app's MenuModel.Work read it too).
# Runs under Windows PowerShell 5.1 too. Prints the path; it needn't exist.
#
# -Shared: your shared layer instead, what's yours in every Windows account (~/omawin-me, or
# OMAWIN_SHARED_LAYER), read beneath the account's. Today only its claude/ (bin/claude-sync.ps1).
param([switch]$Shared)
if ($Shared) {
    if ($env:OMAWIN_SHARED_LAYER) { $env:OMAWIN_SHARED_LAYER } else { Join-Path $HOME 'omawin-me' }
} elseif ($env:OMAWIN_USER_LAYER) { $env:OMAWIN_USER_LAYER } else { Join-Path $HOME 'omawin-work' }
