<#
.SYNOPSIS
    The preview pane of omawin pkg install / remove (fzf runs it for the package under the cursor).

.DESCRIPTION
    What the catalog says at once (bin/pkg-index.ps1: name, id, version, short name, tags,
    whether it's here), then winget's own page for it (publisher, description, homepage,
    license), which takes a second or two to come.

        .\pkg-preview.ps1 <id>
#>
param([Parameter(Mandatory)][string]$Id)
$e = [char]27
$list = Join-Path $env:LOCALAPPDATA 'launcher\winget-index.tsv'
$installed = Join-Path $env:LOCALAPPDATA 'launcher\installed.txt'
$row = $null
foreach ($line in [IO.File]::ReadLines($list)) { if ($line.StartsWith("$Id`t")) { $row = $line -split "`t"; break } }
if (-not $row) { "$Id"; return }
$here = (Test-Path $installed) -and (Select-String -Path $installed -SimpleMatch "winget:$($Id.ToLower())" -Quiet)
"$e[1m$($row[1])$e[0m  $e[2m$($row[3])$e[0m$(if ($here) { "  $e[32m$([char]0x2713) installed$e[0m" })"
"$e[2m$($row[0])$(if ($row[2]) { "  $([char]0x00B7)  $($row[2])" })$e[0m"
if ($row[4]) { "$e[2m$(($row[4] -split ', ' | Select-Object -First 12) -join ', ')$e[0m" }
''
# winget's page: the lines worth reading, without its own header and the installer details.
$ErrorActionPreference = 'Continue'
$keep = 'Publisher|Author|Description|Homepage|License|Release Notes Url|Moniker|Tags'
$show = winget show --id $Id --exact --accept-source-agreements --disable-interactivity 2>$null
$on = $false
foreach ($line in $show) {
    $text = "$line"
    if ($text -match '^Found ') { continue }
    if ($text -match '^(\w[\w ]+):') { $on = $Matches[1] -match "^($keep)$"; if ($Matches[1] -in 'Moniker', 'Tags') { $on = $false } }
    elseif ($text -notmatch '^\s') { $on = $false }
    if ($on) { $text }
}
