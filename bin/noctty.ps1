<#
.SYNOPSIS
    noctty (Ghostty's terminal core in a Windows app), into %LOCALAPPDATA%\Programs\noctty
    (omawin update terminal; install.ps1 sets it up the first time).

.DESCRIPTION
    Takes the latest release's portable ZIP from our fork's GitHub releases
    (github.com/mjveilleux/noctty: upstream amanthanvi/noctty plus the theme-colored tab strip and
    spare shells; scripts/omawin-release.ps1 there publishes one), checks it against the
    SHA256SUMS published with it, and
    unpacks it per-user (no admin). Its own updater is off (noctty/config.ghostty); this is the
    update. A running noctty is stopped first and started again in the background.

    The fork's builds are unsigned, so the checksum is the check.
#>
param([switch]$IfMissing)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'Programs\noctty'
if ($IfMissing -and (Test-Path "$dir\noctty.exe")) { Write-Host "  ok  noctty ($dir)"; return }

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
$release = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/noctty/releases/latest' -UseBasicParsing
$zip = $release.assets | Where-Object name -like 'noctty-*-windows-x64-portable.zip' | Select-Object -First 1
$sums = $release.assets | Where-Object name -eq 'SHA256SUMS-windows-x64.txt' | Select-Object -First 1
if (-not $zip -or -not $sums) { throw "noctty $($release.tag_name): no x64 portable ZIP or checksums in the release" }

$work = Join-Path $env:TEMP "noctty-$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    Invoke-WebRequest $zip.browser_download_url -OutFile "$work\noctty.zip" -UseBasicParsing
    Invoke-WebRequest $sums.browser_download_url -OutFile "$work\sums.txt" -UseBasicParsing
    $want = (Get-Content "$work\sums.txt" | Where-Object { $_ -match [regex]::Escape($zip.name) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
    $have = (Get-FileHash "$work\noctty.zip" -Algorithm SHA256).Hash
    if (-not $want -or $have -ne $want.ToUpper()) { throw "$($zip.name) doesn't match its published SHA-256" }
    Expand-Archive "$work\noctty.zip" "$work\x" -Force
    $unpacked = Get-ChildItem "$work\x" -Recurse -Filter 'noctty.exe' | Select-Object -First 1
    if (-not $unpacked) { throw 'no noctty.exe in the ZIP' }

    $running = [bool](Get-Process noctty -ErrorAction SilentlyContinue)
    Get-Process noctty -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force (Split-Path $dir) | Out-Null
    Move-Item $unpacked.Directory.FullName $dir
    Write-Host "  noctty $($release.tag_name) -> $dir"
    if ($running) { & "$env:USERPROFILE\.local\bin\omawin.exe" start terminal }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
