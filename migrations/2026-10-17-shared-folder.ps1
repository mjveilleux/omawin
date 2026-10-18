# Your own settings moved: ~/omawin-me to the PC's shared folder (C:\ProgramData\omawin\shared),
# ~/omawin-work to ~/omawin-account (docs/work.md). bin/move-settings.ps1 does it, and finishes
# at the next sign-in what a running program kept it from doing now. Then Claude's links point
# into the new places. Safe on a new PC (an empty shared folder is made) and twice.
$Repo = Split-Path $PSScriptRoot
& "$Repo\bin\move-settings.ps1"
& "$Repo\bin\claude-sync.ps1" | Where-Object { $_ -notmatch '^ok' }
