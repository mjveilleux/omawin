# Moves your own settings to where they live since 2026-10-17 (docs/work.md):
#   ~/omawin-me   (yours in every account)  ->  C:\ProgramData\omawin\shared, one per PC, which
#                                               every account reads and may change
#   ~/omawin-work (this account's)          ->  ~/omawin-account
# Each old name is left as a link to the new place, so anything still pointing there keeps working.
#
# A folder something has open can't be renamed, and Claude Code keeps its skill folders open
# while it runs: then that folder stays where it is and is still used from there
# (bin/user-layer.ps1), and the move is tried again at the next sign-in (omawin.exe runs this
# before anything else starts) or the next run. Never deletes anything: an old shared copy is
# renamed aside, and only when the shared folder has everything in it. Safe on a new PC and any
# number of times. Runs under Windows PowerShell 5.1 too.
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format yyyyMMdd-HHmmss

function Say([string]$Text) { if (-not $Quiet) { $Text } }
function Test-Link([string]$Path) { (Test-Path $Path) -and ((Get-Item $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) }
function Set-Link([string]$Old, [string]$New) {
    if ((Test-Path $New) -and -not (Test-Path $Old)) { New-Item -ItemType Junction -Path $Old -Target $New | Out-Null }
}
# Renamed in place, or $false when something has it open.
function Rename-Aside([string]$Path, [string]$Name) {
    try { Rename-Item $Path $Name; $true } catch { $false }
}
# The files in $Old that $New lacks or has older (robocopy's list mode copies nothing).
function Get-Missing([string]$Old, [string]$New) {
    @(& robocopy.exe $Old $New /L /E /XO /XX /NJH /NJS /NDL /NC /NS /NP /R:0 /W:0 /XD .git 2>$null |
        ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
# A variable still naming the old default folder means nothing but "the default" now.
function Clear-OldVariable([string]$Name, [string]$Old) {
    $set = [Environment]::GetEnvironmentVariable($Name, 'User')
    if ($set -and $set.TrimEnd('\') -eq $Old) {
        [Environment]::SetEnvironmentVariable($Name, $null, 'User')
        Remove-Item "env:$Name" -ErrorAction SilentlyContinue
        Say "cleared $Name (it named the old $Old)"
    }
}

Clear-OldVariable 'OMAWIN_SHARED_LAYER' (Join-Path $HOME 'omawin-me')
Clear-OldVariable 'OMAWIN_USER_LAYER' (Join-Path $HOME 'omawin-work')

# --- shared --------------------------------------------------------------------------------------
if (-not $env:OMAWIN_SHARED_LAYER) {
    $new = Join-Path $env:ProgramData 'omawin\shared'
    $old = Join-Path $HOME 'omawin-me'
    if ((Test-Path $old) -and -not (Test-Link $old)) {
        if (-not (Test-Path $new)) {
            # Copied, not moved: a folder can't be renamed into another one with git's read-only
            # files in it, and a move that stops halfway leaves it split. Copied under another
            # name first and renamed once whole, so a copy that fails is never taken for the
            # shared folder. The copy takes the shared folder's permissions (/COPY:DAT: no ACLs).
            $partial = "$new.partial-$stamp"
            New-Item -ItemType Directory -Force (Split-Path $new) | Out-Null
            & robocopy.exe $old $partial /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "couldn't copy ~/omawin-me to $new (robocopy $LASTEXITCODE; the part copied is in $partial)" }
            Rename-Item $partial (Split-Path $new -Leaf)
            Say "copied  ~/omawin-me to $new (shared by every account)"
        }
        # The shared folder is the one used from here; the old copy is kept, out of the way, once
        # nothing in it is missing from the shared folder (another account's copy may hold a
        # skill this one's never had: that's for you to move across, not for this to guess).
        $missing = Get-Missing $old $new
        if ($missing) {
            Say "kept    ~/omawin-me where it is: $($missing.Count) file(s) in it aren't in $new, such as $($missing[0]). Copy what you want across; it's set aside once nothing is missing"
        } elseif (Rename-Aside $old "omawin-me.old-$stamp") {
            Say "kept    the old ~/omawin-me as ~/omawin-me.old-$($stamp): delete it once everything works"
        } else {
            Say "later   ~/omawin-me is open in a program (Claude Code?); it's set aside at the next sign-in. $new is used now"
        }
    }
    & (Join-Path $PSScriptRoot 'shared.ps1') -Init -Quiet:$Quiet
    Set-Link $old $new
}

# --- this account --------------------------------------------------------------------------------
if (-not $env:OMAWIN_USER_LAYER) {
    $new = Join-Path $HOME 'omawin-account'
    $old = Join-Path $HOME 'omawin-work'
    if ((Test-Path $old) -and -not (Test-Link $old)) {
        if (Test-Path $new) { Say "both    ~/omawin-work and ~/omawin-account are folders: ~/omawin-account is used; move what you need from ~/omawin-work" }
        elseif (Rename-Aside $old 'omawin-account') { Say 'moved   ~/omawin-work to ~/omawin-account' }
        else { Say 'later   ~/omawin-work is open in a program (Claude Code?); it moves to ~/omawin-account at the next sign-in, and is used where it is until then' }
    }
    Set-Link $old $new
    # ~/.gitconfig includes the account's git settings by path (install/shell.ps1): the new one.
    if ((Test-Link $old) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $ErrorActionPreference = 'Continue'
        $was = ("$old\git\gitconfig" -replace '\\', '/')
        if (@(git config --global --get-all include.path 2>$null) -contains $was) {
            git config --global --replace-all include.path ("$new\git\gitconfig" -replace '\\', '/') ([regex]::Escape($was)) 2>$null
            Say 'ok      ~/.gitconfig includes ~/omawin-account/git/gitconfig'
        }
        $ErrorActionPreference = 'Stop'
    }
}
