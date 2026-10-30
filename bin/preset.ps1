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
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'add', 'remove')][string]$Verb = 'list',
    [Parameter(Position = 1)][string]$Name,
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\install\packages.ps1"
. "$Repo\install\lib.ps1"

$chosen = @(Get-Presets)
function Save-Presets([string[]]$Names) {
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    Set-Content $PresetsFile (@('# omawin presets on this PC (install/packages.ps1); omawin preset add|remove <name>') + $Names) -Encoding utf8
}

if ($Verb -eq 'list') {
    foreach ($preset in $Presets.Keys) {
        $mark = if ($chosen -contains $preset) { '*' } else { ' ' }
        Write-Host ("  {0} {1,-15} {2}" -f $mark, $preset, $Presets[$preset].Summary)
    }
    Write-Host "`n  * picked on this PC ($PresetsFile). omawin preset add|remove <name>" -ForegroundColor DarkGray
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
