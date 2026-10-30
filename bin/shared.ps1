<#
.SYNOPSIS
    omawin shared: the PC's shared folder, what's yours in every Windows account (docs/work.md).

.DESCRIPTION
    C:\ProgramData\omawin\shared holds what's the same wherever you sign in on this PC: Claude
    rules and skills (claude/), launcher items, sites and shortcuts (launcher/). Every account
    reads it live; only the account that made it (its owner, your main one) may change it, since
    what's in it runs as every account, admins included. Nothing secret goes in it (no tokens,
    no client documents) and nothing an employer or client would mind another seeing: that goes
    in the account's own folder (~/omawin-account). Sign-ins are in neither.

    GitHub is optional: the folder can be backed up to a private repo under any GitHub account,
    and a new PC can start from that backup.

        omawin shared                    where it is, who can change it, its backup
        omawin shared -Init              make it (install does): every account reads it, this one
                                         alone changes it
        omawin shared -From owner/name   start it from a backup (a git URL works too); only into
                                         an empty or missing folder
        omawin shared backup             commit what changed and push it to its backup
        omawin shared backup -Repo name  the first time: make that private GitHub repo for it (gh)

    Runs under Windows PowerShell 5.1 too (bootstrap.ps1 and the migrations use it).
#>
param([string]$Action, [switch]$Init, [string]$From, [string]$Repo, [switch]$Quiet)
$ErrorActionPreference = 'Stop'
$dir = & (Join-Path $PSScriptRoot 'user-layer.ps1') -Shared
$default = Join-Path $env:ProgramData 'omawin\shared'
# The permissions below are only ever set on the PC's own folder: a folder set elsewhere
# (OMAWIN_SHARED_LAYER), or the old ~/omawin-me before it moves, is the owner's to look after,
# and opening its parent (a home folder, say) to every account would be a hole.
$ours = $dir -eq $default
$parent = Split-Path $dir

function Say([string]$Text) { if (-not $Quiet) { Write-Host $Text } }

# Every account reads it; only the account that made it (its owner: your main one) may change it.
# The shared Claude settings and launcher items run as every account that reads them, so a
# folder any account could write would let the least trusted account (a client's, say) plant
# something that runs as the most trusted one (an admin). So: inheritance off (ProgramData
# lets every account add files), full control for the owner, SYSTEM and Administrators, read
# for Users (S-1-5-32-545).
function Set-OwnerOnly([string]$Path) {
    # (Windows PowerShell turns a native program's stderr into an error under 'Stop'.)
    $ErrorActionPreference = 'Continue'
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $null = & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "*$($me):(OI)(CI)F" '*S-1-5-32-545:(OI)(CI)RX' /Q 2>&1
    if ($LASTEXITCODE -ne 0) { throw "couldn't set who may change $Path (icacls $LASTEXITCODE)" }
    # What's inside takes these (anything given its own permissions earlier loses them).
    if (Get-ChildItem $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1) {
        $null = & icacls.exe (Join-Path $Path '*') /reset /T /C /Q 2>&1
    }
}
# Whether any account but the owner may write there (Users, Authenticated Users, Everyone).
function Test-OthersWrite([string]$Path) {
    [bool]((Get-Acl $Path).Access | Where-Object {
        $(try { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { '' }) -in 'S-1-5-32-545', 'S-1-5-11', 'S-1-1-0' -and
        $_.AccessControlType -eq 'Allow' -and $_.FileSystemRights -match 'Write|Modify|FullControl|CreateFiles|AppendData' })
}
function Get-Owner([string]$Path) { (Get-Acl $Path).Owner }
function Test-Mine([string]$Path) {
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
    try { (Get-Acl $Path).GetOwner([Security.Principal.SecurityIdentifier]) -eq $me } catch { $false }
}
# Git refuses a repo another account owns ("dubious ownership") unless told it's fine, per account.
function Approve-Git {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return }
    $ErrorActionPreference = 'Continue'
    $path = $dir -replace '\\', '/'
    $known = @(git config --global --get-all safe.directory 2>$null)
    if ($known -notcontains $path) { git config --global --add safe.directory $path 2>$null }
}

function Initialize-Shared {
    if (-not $ours) {
        if (-not (Test-Path $dir)) { throw "no folder at $dir (OMAWIN_SHARED_LAYER): make it yourself, or unset the variable" }
        Approve-Git
        return
    }
    $made = -not (Test-Path $dir)
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory $parent | Out-Null }
    if ($made) { New-Item -ItemType Directory $dir | Out-Null }
    # Another account finds it made, and reads it: nothing to set up here.
    if (-not $made -and -not (Test-Mine $dir)) { Approve-Git; return }
    # Set by the owner when made, or when found open to others (as omawin made it for a few hours
    # on 2026-10-17, and as a copy into ProgramData starts out).
    if ($made -or (Test-OthersWrite $dir)) {
        Set-OwnerOnly $dir
        # That day's version also opened the folder above to every account.
        $ErrorActionPreference = 'Continue'
        $null = & icacls.exe $parent /remove:g '*S-1-5-32-545' /Q 2>&1
        $ErrorActionPreference = 'Stop'
    }
    foreach ($sub in 'claude\skills', 'launcher') {
        New-Item -ItemType Directory -Force (Join-Path $dir $sub) | Out-Null
    }
    $readme = Join-Path $dir 'README.md'
    if (-not (Test-Path $readme)) {
        [IO.File]::WriteAllText($readme, @"
# Shared omawin settings

What's yours in every Windows account on this PC (omawin's docs/work.md). Every account reads
this folder live; only the account that made it (its owner) may change it,
since what's here runs in every account.

- ``config/claude/CLAUDE.md``, ``config/claude/skills/``, ``config/claude/settings.json``: Claude Code's rules, skills and
  settings (omawin claude sync)
- ``default/launcher/items.txt``, ``sites.txt``, ``keys.txt``: launcher items, Localhost sites, Shortcuts lines

Never here: passwords, tokens, client documents, or anything one employer or client shouldn't
see in another's account. Those go in that account's ~/omawin-account.
"@.Replace("`r`n", "`n"))
    }
    Approve-Git
    if ($made) { Say "made    $dir (every account reads it; only this one may change it)" }
}

if ($From) {
    $ErrorActionPreference = 'Continue'   # git's progress goes to stderr; its exit code says how it went
    if (-not $ours) { throw "-From fills the PC's shared folder ($default) only; unset OMAWIN_SHARED_LAYER first" }
    # Only over nothing, or over what -Init alone made (its README and empty folders).
    $readme = Join-Path $dir 'README.md'
    if ((Test-Path $dir) -and (Get-ChildItem $dir -Recurse -File -Force | Where-Object FullName -ne $readme)) {
        throw "$dir isn't empty; its backup goes in only where there's nothing yet"
    }
    $url = if ($From -match '^[\w.-]+/[\w.-]+$') { "https://github.com/$From.git" } else { $From }
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory $parent | Out-Null }
    git clone -q $url $dir
    if ($LASTEXITCODE -ne 0) { throw "couldn't clone $url" }
    Initialize-Shared
    Say "ok      shared settings from $url, in $dir"
    return
}
if ($Init) { Initialize-Shared; return }

if ($Action -eq 'backup') {
    if (-not (Test-Path $dir)) { throw "no shared folder yet ($dir): omawin shared -Init" }
    if ($ours -and -not (Test-Mine $dir)) { throw "only $(Get-Owner $dir) may change the shared folder: back it up from that account" }
    $ErrorActionPreference = 'Continue'
    Approve-Git
    if (-not (Test-Path (Join-Path $dir '.git'))) { git -C $dir init -q -b main }
    git -C $dir add -A
    if (git -C $dir status --porcelain) {
        git -C $dir commit -q -m "Shared settings from $([Environment]::UserName)"
        Say 'ok      committed what changed'
    } else { Say 'ok      nothing changed' }
    if (-not (git -C $dir remote)) {
        if (-not $Repo) { Say 'no backup repo yet: omawin shared backup -Repo <name> makes a private one (gh)'; return }
        gh repo create $Repo --private --source $dir --push
        Say "ok      backed up to $Repo (private)"
        return
    }
    git -C $dir pull -q --rebase
    if ($LASTEXITCODE -ne 0) { throw "couldn't bring in the backup's changes (git -C $dir status)" }
    git -C $dir push -q
    if ($LASTEXITCODE -ne 0) { throw 'the push failed' }
    Say "ok      backed up to $(git -C $dir remote get-url origin)"
    return
}

# Status.
if (-not (Test-Path $dir)) { Say "no shared folder yet: $dir (omawin shared -Init)"; return }
$others = Test-OthersWrite $dir
$remote = if (Test-Path (Join-Path $dir '.git')) { git -c "safe.directory=$($dir -replace '\\','/')" -C $dir remote get-url origin 2>$null }
$skills = @(Get-ChildItem (Join-Path $dir 'claude\skills') -Directory -ErrorAction SilentlyContinue).Count
Say "Shared settings  $dir"
Say "  changed by     $(Get-Owner $dir)$(if ($others) { ', and other accounts too (omawin shared -Init from the owner closes that)' } else { ' only; every account reads it' })"
Say "  backup         $(if ($remote) { $remote } else { 'none (omawin shared backup -Repo <name>)' })"
Say "  Claude         $(if (Test-Path (Join-Path $dir 'claude\CLAUDE.md')) { 'rules, ' })$skills skill(s)"
Say "This account     $(& (Join-Path $PSScriptRoot 'user-layer.ps1'))"
