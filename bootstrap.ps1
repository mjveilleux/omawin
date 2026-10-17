<#
.SYNOPSIS
    One command for a brand-new Windows PC: get omawin and run install.ps1.

.DESCRIPTION
    Meant to be run straight from the web, like `curl ... | bash` on Linux. From Windows
    PowerShell:

        irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex

    It needs nothing installed first:

      1. installs git with winget
      2. clones omawin to ~/omawin (or pulls it if it's already there)
      3. your layers, if you have them (docs/work.md), each asked for, Enter skips:
         this Windows account's user layer to ~/omawin-work, your shared one to ~/omawin-me.
         A private one opens Git's sign-in window.
      4. runs install.ps1, which sets up everything else

    Safe to run again. Runs under Windows PowerShell 5.1, which every Windows PC has.
    OMAWIN_REPO=<owner>/<name> installs a fork; OMAWIN_LOCAL=1 skips the clone and uses the copy
    already at ~/omawin (testing in a VM).
#>
$ErrorActionPreference = 'Stop'

# Never as administrator: in a standard account, "Run as administrator" with another account's
# password runs everything as that other account (its home folder, its settings), and the
# desktop it starts can't work in this one. The steps that need admin ask for it themselves.
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host ''
    Write-Host "  This PowerShell runs as administrator ($env:USERNAME). Close it, open PowerShell" -ForegroundColor Yellow
    Write-Host '  normally (Start, type PowerShell, Enter), and run the command again. The steps' -ForegroundColor Yellow
    Write-Host '  that need admin ask for it on their own.' -ForegroundColor Yellow
    return
}

$Repo = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' }
$Target = "$HOME\omawin"
$Local = $env:OMAWIN_LOCAL -eq '1' -or $env:DOTFILES_LOCAL -eq '1'

# A new PC only runs signed scripts; install.ps1 and the PowerShell profile are local files.
Set-ExecutionPolicy -Scope Process Bypass -Force
if ((Get-ExecutionPolicy -Scope CurrentUser) -notin 'RemoteSigned', 'Unrestricted', 'Bypass') {
    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force
}

function Write-Step([string]$Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }
function Update-SessionPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('Path', 'User')
}

Write-Step 'winget'
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    # New PCs get winget (App Installer) from the Store shortly after first sign-in; nudge it.
    Write-Host '    winget is not ready yet; asking Windows to register App Installer ...'
    try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe } catch { }
    Update-SessionPath
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is missing. Update "App Installer" in the Microsoft Store, then run this again.'
    }
}

Write-Step 'git'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    winget install --id Git.Git --exact --silent --accept-source-agreements --accept-package-agreements
    Update-SessionPath
} else { Write-Host '    ok  git' }

if ($Local) {
    Write-Step "using the copy at $Target (OMAWIN_LOCAL=1)"
} else {
    Write-Step "get $Repo"
    if (Test-Path "$Target\.git") { git -C $Target pull --ff-only }
    else { git clone "https://github.com/$Repo.git" $Target }
    if ($LASTEXITCODE -ne 0) { throw "couldn't get $Repo" }

    # The layers (docs/work.md), before install so it uses them. Optional: Enter skips.
    function Get-Layer([string]$Path, [string]$Question) {
        if (Test-Path "$Path\.git") { git -C $Path pull --ff-only; return }
        if (Test-Path $Path) { return }
        $answer = (Read-Host "    $Question (owner/name or a git URL; Enter: none)").Trim()
        if (-not $answer) { return }
        $url = if ($answer -match '^(https?|git@|ssh)') { $answer } else { "https://github.com/$answer.git" }
        Write-Step "get $answer"
        git clone $url $Path
        if ($LASTEXITCODE -ne 0) { Write-Host "    couldn't clone $answer; carrying on without it" -ForegroundColor Yellow }
    }
    Get-Layer "$HOME\omawin-work" "This account's user layer repo, if you have one"
    Get-Layer "$HOME\omawin-me" 'Your shared layer repo, if you have one'
}

Write-Step 'install.ps1'
& "$Target\install.ps1"
