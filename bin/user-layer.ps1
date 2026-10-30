# Where your own settings are (docs/work.md): the two folders next to omawin that hold what's
# yours and not omawin's. The one PowerShell definition of those paths: install (install/lib.ps1),
# bin/check.ps1, themes/render.ps1, update and the PowerShell profiles all ask this script. Runs under
# Windows PowerShell 5.1 too. Prints the path; it needn't exist.
#
#   (no switch)  this Windows account's folder: who you are here (git identity, the employer's or
#                client's launcher items, shares, VPN, reports). ~/omawin-account, or
#                OMAWIN_USER_LAYER (bin/omawin.cs and the menu app's MenuModel.Work read it too).
#   -Shared      the PC's shared folder: what's yours in every account (Claude rules and skills,
#                launcher items, sites). C:\ProgramData\omawin\shared, or OMAWIN_SHARED_LAYER.
#
# Before 2026-10-17 these were ~/omawin-work and ~/omawin-me; until the migration
# (migrations/2026-10-17-shared-folder.ps1) has moved them, the old one is used when it's all there is.
param([switch]$Shared)
if ($Shared) {
    if ($env:OMAWIN_SHARED_LAYER) { return $env:OMAWIN_SHARED_LAYER }
    $new = Join-Path $env:ProgramData 'omawin\shared'
    $old = Join-Path $HOME 'omawin-me'
} else {
    if ($env:OMAWIN_USER_LAYER) { return $env:OMAWIN_USER_LAYER }
    $new = Join-Path $HOME 'omawin-account'
    $old = Join-Path $HOME 'omawin-work'
}
if (-not (Test-Path $new) -and (Test-Path $old)) { $old } else { $new }
