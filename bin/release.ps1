<#
.SYNOPSIS
    A release's files, downloaded and checked against the SHA-256 manifest published with them.

.DESCRIPTION
    The other half of .github/workflows/release.yml (docs/releases.md): CI publishes
    omawin-helpers-<tag>.zip, SystemMenu-<tag>-win-<arch>.zip and omawin-music-<tag>-win-<arch>.exe
    with a SHA256SUMS.txt beside them; this fetches the ones asked for and refuses anything whose
    bytes don't match the manifest. A file already here that verifies is left alone (-Force
    downloads again).

    Two sources, so the path works on a private repo and can be tested offline:
      GitHub   the repo's releases through its API. A private repo needs a token: gh auth token
               (bootstrap.ps1 signs you in) or GH_TOKEN / GITHUB_TOKEN.
      -BaseUrl a folder or an http(s) URL holding the release's files (a mirror, or a locally
               built fake release: docs/releases.md). The manifest is the index there too.

    -Asset takes file-name patterns, like the fork downloads do (`SystemMenu-*-win-x64.zip`): the
    name isn't spelled out twice, and a missing file says so. Verified paths are printed, one per
    line, for the caller to use (bin/install-binaries.ps1).

.EXAMPLE
    .\release.ps1 -Asset 'SystemMenu-*-win-x64.zip' -OutDir $env:TEMP\x -Tag v0.1.0
    .\release.ps1 -Asset 'omawin-helpers-*.zip' -OutDir .\scratch\dl -BaseUrl http://127.0.0.1:8000
    .\release.ps1 -Tag latest -TagFile "$env:LOCALAPPDATA\launcher\release.txt"   (just the tag)
#>
param(
    [string[]]$Asset,                    # file-name patterns in the release
    [string]$OutDir = '.',               # where they are written
    [string]$Tag = 'latest',             # latest, or a tag like v0.1.0 (a draft release needs its tag)
    [string]$BaseUrl,                    # a folder or http(s) URL with the release's files
    # The release's repo; a fork sets it here or in OMAWIN_REPO. Empty here so the env can win.
    [string]$Repo,
    [string]$TagFile,                    # write the resolved tag here (what this PC is on)
    [switch]$ResolveTag,                 # print the tag and stop (no downloads)
    [switch]$Force                       # download again even if a verified copy is here
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell draws Invoke-WebRequest's progress so often it slows a download several times over.
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
if (-not $Repo) { $Repo = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' } }
$Manifest = 'SHA256SUMS.txt'

function Note([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
function Warn([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }
# Absolute, and there before anything is written into it.
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }

function Get-Token {
    # The token for a private repo's release: the environment first (CI, a script), then gh's.
    if ($env:GH_TOKEN) { return $env:GH_TOKEN }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $t = (& gh auth token 2>$null | Select-Object -First 1)
        if ($t) { return $t.Trim() }
    }
    return $null
}
function New-Headers([string]$Token, [string]$Accept) {
    $h = @{ Accept = $Accept; 'User-Agent' = 'omawin' }
    if ($Token) { $h.Authorization = "Bearer $Token" }
    return $h
}

# One file out of the source, into $OutDir (as <name>.part, then moved: a half download never
# looks like a good file).
function Save-Entry([string]$Name, [string]$Url) {
    $dest = Join-Path $OutDir $Name
    $part = "$dest.part"
    try {
        # A GitHub asset's API address gives the file only when asked for it (Accept:
        # application/octet-stream); without that it answers with the asset's description.
        if ($Url -match '^https://api\.github\.com/') { Invoke-WebRequest -Uri $Url -OutFile $part -Headers $assetHeaders -UseBasicParsing | Out-Null }
        elseif ($Url -match '^[a-z]+://') { Invoke-WebRequest -Uri $Url -OutFile $part -UseBasicParsing | Out-Null }
        else { Copy-Item -LiteralPath $Url -Destination $part -Force }
        Move-Item -LiteralPath $part -Destination $dest -Force
    } finally {
        if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue }
    }
    return $dest
}

# --- what the release holds: its tag, and the manifest's `sha256  name` lines ----------------
if ($BaseUrl) {
    # A mirror: its manifest is the list of files (so a local folder needs no directory listing).
    $resolved = if ($Tag -eq 'latest') { $null } else { $Tag }
    $manifestUrl = if ($BaseUrl -match '^[a-z]+://') { "$($BaseUrl.TrimEnd('/'))/$Manifest" }
                   elseif (Test-Path -LiteralPath (Join-Path $BaseUrl $Manifest)) { Join-Path $BaseUrl $Manifest }
                   else { $null }
    if (-not $manifestUrl) { throw "no $Manifest at $BaseUrl (a mirror needs one, docs/releases.md)" }
} else {
    $token = Get-Token
    $headers = New-Headers $token 'application/vnd.github+json'
    $uri = if ($Tag -eq 'latest') { "https://api.github.com/repos/$Repo/releases/latest" }
           else { "https://api.github.com/repos/$Repo/releases/tags/$Tag" }
    try { $release = Invoke-RestMethod -Uri $uri -Headers $headers -UseBasicParsing }
    catch {
        $hint = if (-not $token) { ' (a private repo needs a token: gh auth login, or GH_TOKEN)' } else { '' }
        throw "no release '$Tag' in repo $Repo$hint - $($_.Exception.Message)"
    }
    $resolved = $release.tag_name
    $manifestAsset = $release.assets | Where-Object name -eq $Manifest | Select-Object -First 1
    if (-not $manifestAsset) { throw "release $resolved has no $Manifest (docs/releases.md)" }
    $manifestUrl = $manifestAsset.url
    $assetHeaders = New-Headers $token 'application/octet-stream'
    $token = $null  # not needed past here
}

if ($ResolveTag) { if ($resolved) { Write-Output $resolved } else { Write-Output 'latest' }; return }
if (-not $Asset) { throw '-Asset: the release file names to fetch (patterns)' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
# The manifest itself: through the API for GitHub (private repos), a plain fetch for a mirror.
$work = Join-Path ([IO.Path]::GetTempPath()) "omawin-release-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $manifestPath = Join-Path $work $Manifest
    if ($BaseUrl -and $manifestUrl -match '^[a-z]+://') { Invoke-WebRequest -Uri $manifestUrl -OutFile $manifestPath -UseBasicParsing | Out-Null }
    elseif ($BaseUrl) { Copy-Item -LiteralPath $manifestUrl -Destination $manifestPath -Force }
    else { Invoke-WebRequest -Uri $manifestUrl -OutFile $manifestPath -Headers $assetHeaders -UseBasicParsing | Out-Null }

    $sums = @{}
    foreach ($line in (Get-Content -LiteralPath $manifestPath)) {
        if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
            $sums[$Matches[2]] = $Matches[1].ToUpperInvariant()
        }
    }
    if (-not $sums.Count) { throw "$Manifest has no usable 'sha256  name' lines" }
    $names = @($sums.Keys)

    $wanted = @()
    foreach ($pattern in $Asset) {
        $match = @($names | Where-Object { $_ -like $pattern })
        if (-not $match) { throw "the release has no file matching '$pattern' (it has: $($names -join ', '))" }
        $wanted += $match
    }

    $files = @()
    foreach ($name in $wanted) {
        $dest = Join-Path $OutDir $name
        $here = Test-Path -LiteralPath $dest
        if ($here -and -not $Force -and (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -eq $sums[$name]) {
            Note "ok  $name"; $files += $dest; continue
        }
        $url = if ($BaseUrl -and $BaseUrl -match '^[a-z]+://') { "$($BaseUrl.TrimEnd('/'))/$name" }
               elseif ($BaseUrl) { Join-Path $BaseUrl $name }
               else { ($release.assets | Where-Object name -eq $name | Select-Object -First 1).url }
        if (-not $url) { throw "$name is in $Manifest but not in the release" }
        $dest = Save-Entry $name $url
        # The check before the file is ever used: the manifest is the release's own word for it.
        $have = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
        if ($have -ne $sums[$name]) {
            Remove-Item -LiteralPath $dest -Force
            throw "$name doesn't match its published SHA-256 (published $($sums[$name]), got $have): the download is not the release's file"
        }
        Note "got $name  ($($sums[$name].Substring(0, 12))...)"
        $files += $dest
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($TagFile -and $resolved) {
    New-Item -ItemType Directory -Force -Path (Split-Path $TagFile) | Out-Null
    Set-Content -LiteralPath $TagFile -Value $resolved -NoNewline
}
$files
