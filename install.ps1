<#
.SYNOPSIS
    Set up a Windows machine from this repo: install tools, the Nerd Font, and link configs.

.DESCRIPTION
    Safe to re-run; every step skips what's already done. Configs are linked, not copied,
    so editing them in place (nvim, $PROFILE, Windows Terminal's settings UI) edits this repo.
    Directory junctions are used because they need neither admin nor Developer Mode.
    Anything already at a target path is moved aside to <path>.backup-<timestamp>.

    Runs under Windows PowerShell 5.1 too, so it works on a fresh machine before pwsh exists.

    The steps are the files in install/, run in this order (-Step runs just one), then the
    migrations not run on this PC yet (bin/migrate.ps1: what older versions left behind).

.EXAMPLE
    .\install.ps1                # everything
    .\install.ps1 -LinkOnly      # everything but the software step: relink, rebuild, no installs
    .\install.ps1 -Step shell    # one step (install/shell.ps1)
#>
param(
    [switch]$LinkOnly,     # skip the software step (packages, presets, other installers, the font)
    [string]$Step          # run only install/<Step>.ps1
)

$ErrorActionPreference = 'Stop'
# Never as administrator (see bootstrap.ps1): it would set up whichever account the admin
# password belongs to, not the one signed in. The steps that need admin ask for it.
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This PowerShell runs as administrator ($env:USERNAME): run install.ps1 from a normal one; it asks for admin where it needs it." -ForegroundColor Yellow
    exit 1
}
$Repo = $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\packages.ps1"

$Steps = 'no-ads', 'software','neovim', 'shell', 'desktop', 'helpers', 'look', 'other'
. "$Repo\install\lib.ps1"
if ($Step) {
    if ($Steps -notcontains $Step) { throw "no step '$Step'; the steps: $($Steps -join ', ')" }
    . "$Repo\install\$Step.ps1"
    return
}
foreach ($name in $Steps) {
    if ($LinkOnly -and $name -eq 'software') { continue }
    . "$Repo\install\$name.ps1"
}

Write-Step 'migrations'
$global:LASTEXITCODE = 0  # not a stale one from a step's native program
& "$Repo\bin\migrate.ps1"
if ($LASTEXITCODE -ne 0) {
    # migrate.ps1 already said which one failed; the rest run on the next install or omawin migrate.
    Write-Warn 'a migration failed (above); fix it, then run: omawin migrate'
    exit 1
}

Write-Step 'done'
Write-Note 'Sign out and back in: the desktop (GlazeWM, the bar, the launcher) starts at sign-in.'
Write-Note 'Then Win+Enter for a terminal; the first nvim start installs its plugins. `omawin help` lists every command.'
