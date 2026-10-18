<#
.SYNOPSIS
    omawin account new: start this Windows account's own folder (docs/work.md).

.DESCRIPTION
    Each Windows account has a folder of its own: who you are in that account and not omawin's
    (its git identity, the employer's or client's launcher items and sites, shortcuts, PowerShell
    additions, notes for agents). What's yours in every account goes in the PC's shared folder
    instead (omawin shared). This makes one at ~/omawin-account (or OMAWIN_USER_LAYER),
    from nothing personal: your name and email for git (asked), and empty files with a line
    saying what each is for. Then a git repo, and with -Repo, a private GitHub repo of that name
    to keep it in (gh, signed in as whoever this account uses).

    An account whose folder is kept in a repo already clones it instead:
        git clone https://github.com/<you>/<repo>.git $HOME\omawin-account

    Run it before install.ps1 on a new account and install uses it (no name / email question).
    Never overwrites: a file already there is kept. Runs under Windows PowerShell 5.1 too.

        omawin account new                        # asks for name and email
        omawin account new -Repo omawin-client    # and keeps it in a private GitHub repo
#>
param([string]$Name, [string]$Email, [string]$Repo)
$ErrorActionPreference = 'Stop'

$layer = & (Join-Path $PSScriptRoot 'user-layer.ps1')
New-Item -ItemType Directory -Force $layer | Out-Null
Write-Host "This account's folder: $layer"

function Seed([string]$Path, [string]$Text) {
    $file = Join-Path $layer $Path
    if (Test-Path $file) { Write-Host "  kept    $Path"; return }
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [IO.File]::WriteAllText($file, $Text.Replace("`r`n", "`n").TrimStart("`n"))
    Write-Host "  made    $Path"
}

if (-not (Test-Path (Join-Path $layer 'git\gitconfig'))) {
    if (-not $Name) { $Name = Read-Host 'Your name, for git commits in this account' }
    if (-not $Email) { $Email = Read-Host 'Your email for this account (work, client or personal)' }
}
Seed 'git\gitconfig' @"
# Included by ~/.gitconfig after omawin's git/gitconfig: this account's identity.
[user]
	name = $Name
	email = $Email
"@
Seed 'README.md' @"
# This account's omawin folder

Who you are in this account, and not omawin's (omawin's docs/work.md lists every file it reads
here). What's yours in every account goes in the PC's shared folder (omawin shared).
Agents working on omawin read this file: say here what's off limits in this account (repos
never to run git in, folders and files never to commit or send anywhere).

## Off limits

- (none yet)
"@
Seed 'launcher\items.txt' @"
# This account's launcher items, after omawin's (same format as omawin's launcher/items.txt).
"@
Seed 'launcher\sites.txt' @"
# This account's sites in launcher > Localhost, after omawin's (same format as launcher/sites.txt).
"@
Seed 'launcher\keys.txt' @"
# Lines added to the Shortcuts page (same format as omawin's launcher/keys.txt).
"@
Seed 'claude\skills-off.txt' @"
# Shared skills (the PC's shared folder, claude/skills) this account goes without: one name a line.
"@
Seed 'powershell\profile.ps1' @"
# Dot-sourced at the end of the PowerShell profile, in interactive shells: this account's
# shortcuts and aliases.
"@
Seed '.gitignore' @"
# Nothing secret goes in here: no passwords, tokens or client documents.
*.pbix
*.env
"@

if (-not (Test-Path (Join-Path $layer '.git'))) {
    git -C $layer init -q -b main
    git -C $layer add -A
    git -C $layer commit -q -m "This account's folder (omawin account new)"
    Write-Host '  a git repo, first commit made'
}
if ($Repo) {
    if (git -C $layer remote) { Write-Host "  kept    its remote: $(git -C $layer remote get-url origin)" }
    else {
        gh repo create $Repo --private --source $layer --push
        Write-Host "  kept in GitHub: $Repo (private)"
    }
}
Write-Host ''
Write-Host 'Next: install.ps1 (or omawin install -LinkOnly, when omawin is installed already) picks it up.'
