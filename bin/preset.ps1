<#
.SYNOPSIS
    omawin's presets: opt-in groups of tools on top of the core (install/packages.ps1).

.DESCRIPTION
    The presets this PC picked are kept in %LOCALAPPDATA%\launcher\presets.txt; install.ps1
    installs the core and those. Adding one installs it now; removing one only stops install.ps1
    (and omawin check) from asking for it: what it installed stays (Install > Remove removes apps).

        omawin preset list            every preset, the picked ones marked
        omawin preset add data        pick it and install it now
        omawin preset add data -NoInstall
        omawin preset remove data

    Plugins (shell/plugins/<id>, Omarchy's format) are picked the same way: a plugin's programs
    are a preset tagged with it (Plugin in install/packages.ps1), so picking the plugin picks that
    preset, and what's on is in presets.txt too.

        omawin plugin list            every plugin, the ones on marked
        omawin plugin add music       (or omawin.music) turn it on and install its programs
        omawin plugin remove music
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'add', 'remove', 'plugins', 'plugin-add', 'plugin-remove', 'sync-plugins')][string]$Verb = 'list',
    [Parameter(Position = 1)][string]$Name,
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\install\packages.ps1"
. "$Repo\install\lib.ps1"

$chosen = @(Get-Presets)

# --- plugins: the presets tagged with one, by the plugin's id or its last part ---
if ($Verb -eq 'plugins') {
    foreach ($dir in Get-ChildItem "$Repo\shell\plugins" -Directory -ErrorAction SilentlyContinue) {
        $m = Get-Content (Join-Path $dir.FullName 'manifest.json') -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue
        if (-not $m) { continue }
        $preset = @($Presets.Keys | Where-Object { $Presets[$_].Plugin -eq $m.id }) | Select-Object -First 1
        $mark = if ($preset -and $chosen -contains $preset) { '*' } else { ' ' }
        Write-Host ("  {0} {1,-15} {2}" -f $mark, $m.id, $m.description)
    }
    Write-Host "`n  * on: its programs are installed. omawin plugin add|remove <id>. It shows in the bar once omawin's QML host runs plugins (roadmap 4.9)." -ForegroundColor DarkGray
    return
}
if ($Verb -in 'plugin-add', 'plugin-remove') {
    $wanted = if ($Name -and $Name -notlike '*.*') { "omawin.$Name" } else { $Name }
    $preset = @($Presets.Keys | Where-Object { $Presets[$_].Plugin -and $Presets[$_].Plugin -eq $wanted }) | Select-Object -First 1
    if (-not $preset) { throw "no plugin '$Name'; the plugins: $((@($Presets.Keys | Where-Object { $Presets[$_].Plugin } | ForEach-Object { $Presets[$_].Plugin })) -join ', ') (omawin plugin list)" }
    $Verb = $Verb -replace '^plugin-'
    $Name = $preset
}
function Save-Presets([string[]]$Names) {
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    Set-Content $PresetsFile (@('# omawin presets on this PC (install/packages.ps1); omawin preset add|remove <name>') + $Names) -Encoding utf8
    Save-Plugins $Names
}
# The plugins that are on, for the bar (shell/menu-app/Bar/PluginSlot.cs): the ids of the picked
# presets tagged with one.
function Save-Plugins([string[]]$Names) {
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    $ids = @($Names | Where-Object { $Presets.Contains($_) -and $Presets[$_].Plugin } | ForEach-Object { $Presets[$_].Plugin })
    Set-Content (Join-Path (Split-Path $PresetsFile) 'plugins.txt') (@('# omawin plugins that are on (shell/plugins/<id>); omawin plugin add|remove <id>') + $ids) -Encoding utf8
}

# plugins.txt written again from presets.txt (a migration: PCs that picked a plugin's preset first).
if ($Verb -eq 'sync-plugins') { Save-Plugins $chosen; return }

if ($Verb -eq 'list') {
    foreach ($preset in @($Presets.Keys | Where-Object { -not $Presets[$_].Plugin })) {
        $mark = if ($chosen -contains $preset) { '*' } else { ' ' }
        Write-Host ("  {0} {1,-15} {2}" -f $mark, $preset, $Presets[$preset].Summary)
    }
    Write-Host "`n  * picked on this PC ($PresetsFile). omawin preset add|remove <name>; plugins: omawin plugin list" -ForegroundColor DarkGray
    return
}

if (-not $Name) { throw "omawin preset $Verb <name>; the presets: $($Presets.Keys -join ', ')" }
if (-not $Presets.Contains($Name)) { throw "no preset '$Name'; the presets: $($Presets.Keys -join ', ')" }

if ($Verb -eq 'remove') {
    if ($chosen -notcontains $Name) { Write-Host "  ok  $Name isn't picked"; return }
    Save-Presets @($chosen | Where-Object { $_ -ne $Name })
    Write-Host "  removed $Name from the presets (what it installed stays)"
    return
}

if ($chosen -notcontains $Name) { Save-Presets (@($chosen) + $Name); Write-Host "  added $Name to the presets" }
else { Write-Host "  ok  $Name is picked" }
if ($NoInstall) { return }
$SoftwareFunctionsOnly = $true
. "$Repo\install\software.ps1"
Write-Step "preset $Name"
Install-WingetPackages @($Presets[$Name].Winget | Where-Object { $_ })

# WSL itself, for the wsl preset, when the PC hasn't got it: one admin prompt (the install's admin
# step with WSL alone, install/admin-batch.ps1), then the Linux, which needs none. Just put in,
# WSL works after a restart, and the Linux finishes at the next sign-in (wsl/install.ps1).
$WslRestart = $false
function Get-WslRestart { $script:WslRestart }
if ($Name -eq 'wsl' -and -not (Test-WslInstalled)) {
    Write-Note 'WSL takes admin: Windows asks once'
    $status = Join-Path $env:TEMP "omawin-admin-$Stamp.txt"
    Set-Content $status '' -NoNewline
    try {
        $null = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\install\admin-batch.ps1`"", '-Status', "`"$status`"", '-Wsl'
    } catch { throw "WSL takes admin, and the prompt was declined (omawin preset add wsl asks again)" }
    $f = @("$(Get-Content $status | Where-Object { $_ -like "wsl`t*" } | Select-Object -Last 1)" -split "`t")
    Remove-Item $status -ErrorAction SilentlyContinue
    if ($f[0] -ne 'wsl' -or $f[1] -ne 'done') { throw "WSL didn't install$(if ($f.Count -gt 2 -and $f[2]) { ": $($f[2])" })" }
    $WslRestart = $f.Count -gt 2 -and $f[2] -eq 'restart'
}
Install-Extras @($Presets[$Name].Extras | Where-Object { $_ })
# A plugin's QML runs in omawin's host (src/qs-host): from the release's tools bundle, with the
# first plugin turned on (it isn't in the core install).
if ($Presets[$Name].Plugin) { Install-ToolBundle $null @('qs-host') }
