<#
.SYNOPSIS
    omawin.exe and the bar's helpers, the menu app and omawin-music: a release's prebuilt files
    when there is one for this PC, otherwise built here.

.DESCRIPTION
    install.ps1's helpers step runs this (install/helpers.ps1). Per piece, in this order:

      1. what's already here and current (an exe newer than its source, the menu app newer than
         menu-app\, the player newer than music\) is kept, so a release never replaces code built
         from a newer checkout;
      2. else the release's prebuilt file for this PC's architecture, downloaded and checked
         against the SHA-256 manifest published with it (bin/release.ps1: a mismatch is refused);
      3. else built here from this checkout (build/helpers.ps1, menu-app\build.ps1,
         music\build.ps1) - a dev checkout, offline, no release, no GitHub sign-in, no .NET SDK.

    Step 2 is the point: a new PC gets omawin.exe, the helpers, the menu app and the music player
    without the .NET SDK (a 1 GB dependency) or a compile. The SDK stays in the core
    (packages.ps1) as step 3's fallback.

    -Release pins a tag (default: the newest release); OMAWIN_RELEASE does the same. -NoDownload
    builds, -NoBuild never builds (OMAWIN_PREBUILT=0|1 for a whole install). -BaseUrl points at a
    mirror or a locally built fake release instead of GitHub (docs/releases.md).

    -BinDir and -MenuDir are where the files land (~/.local/bin and %LOCALAPPDATA%\system-menu,
    the same as install.ps1 uses). Point them at a scratch folder to exercise this without
    touching this PC's copies: build/test-release.ps1 does exactly that.

.EXAMPLE
    .\install-binaries.ps1
    .\install-binaries.ps1 -BinDir ..\scratch\bin -MenuDir ..\scratch\menu -NoBuild
#>
param(
    [string]$BinDir = "$HOME\.local\bin",
    [string]$MenuDir = "$env:LOCALAPPDATA\system-menu",
    [string]$Release,               # a release tag; the default is the newest release
    [string]$BaseUrl,               # a mirror / a locally built fake release (bin/release.ps1)
    [string]$StateFile = "$env:LOCALAPPDATA\launcher\release.txt",  # the release this PC is on
    # Which piece: install/helpers.ps1 keeps its step headings and calls this once per piece.
    [ValidateSet('all', 'helpers', 'menu', 'music')][string]$What = 'all',
    [switch]$NoDownload,            # always build
    [switch]$NoBuild,               # never build: a release or nothing (tests, building with no SDK)
    [switch]$NoStart                # don't stop or start the menu app / the music player (CI, tests)
)
$ErrorActionPreference = 'Stop'

# install.ps1's step output, when this runs inside an install (install/lib.ps1 dot-sources them).
if (-not (Get-Command Write-Note -ErrorAction SilentlyContinue)) {
    function Write-Note([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
    function Write-Warn([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }
}

$Repo = Split-Path $PSScriptRoot
$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
if (-not $Release) { $Release = if ($env:OMAWIN_RELEASE) { $env:OMAWIN_RELEASE } else { 'latest' } }
if ($env:OMAWIN_PREBUILT -eq '0') { $NoDownload = $true }
if ($env:OMAWIN_PREBUILT -eq '1') { $NoBuild = $true }
$WantHelpers = $What -in 'all', 'helpers'
$WantMenu = $What -in 'all', 'menu'
$WantMusic = $What -in 'all', 'music'
foreach ($d in $BinDir, $MenuDir) { New-Item -ItemType Directory -Force $d | Out-Null }
$work = Join-Path ([IO.Path]::GetTempPath()) "omawin-binaries-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

$reason = $null
$releaseTag = $null
$useRelease = $null   # $null: not decided yet (lazy: a run with nothing stale makes no API call)

function Test-UseRelease {
    # Whether this PC takes the release at all: decided once, on the first piece that needs it
    # (a run with nothing stale makes no API call). The release is the normal path - a new PC
    # shouldn't need the .NET SDK - and a local build is kept when it is newer than its sources
    # (Test-Current below), so a release never replaces code built from a newer checkout.
    if ($null -ne $script:useRelease) { return $script:useRelease }
    $script:useRelease = $false
    if ($NoDownload) { $script:reason = '-NoDownload'; return $false }
    try {
        $script:releaseTag = & "$PSScriptRoot\release.ps1" -ResolveTag -Tag $Release -BaseUrl $BaseUrl
        $script:useRelease = $true
    } catch {
        $script:reason = $_.Exception.Message
    }
    return $script:useRelease
}

function Get-ReleaseFile([string]$Pattern) {
    # One file out of the release, or $null with the reason in $script:reason (offline, no release
    # for this architecture, a hash that doesn't match, no GitHub sign-in, this checkout ahead of
    # the release, -NoDownload).
    if (-not (Test-UseRelease)) { return $null }
    try {
        return (& "$PSScriptRoot\release.ps1" -Asset $Pattern -OutDir $work -Tag $script:releaseTag -BaseUrl $BaseUrl -TagFile $StateFile |
            Select-Object -Last 1)
    } catch {
        $script:reason = $_.Exception.Message
        return $null
    }
}

# The same test install.ps1 -LinkOnly has always used: the exe is newer than what it is built from.
# A folder is read at its top level only, as install/helpers.ps1 did: a build's obj\ and bin\ are
# newer than the exe but aren't sources, and treating them as such would rebuild every time.
function Test-Current([string]$Exe, [string]$Source) {
    $e = Get-Item $Exe -ErrorAction SilentlyContinue
    if (-not $e) { return $false }
    $s = if (Test-Path $Source -PathType Container) {
        Get-ChildItem $Source -File | Sort-Object LastWriteTime | Select-Object -Last 1
    } else { Get-Item $Source }
    return $e.LastWriteTime -ge $s.LastWriteTime
}

try {
    # --- omawin.exe and the bar's helpers (~/.local/bin) ----------------------------------------
    if ($WantHelpers) {
        # The helpers' C# 5 sources (build/helpers.ps1 has the same list). Architecture-neutral
        # IL, so one ZIP covers x64 and ARM64.
        $helpers = @(
            @{ Exe = 'omawin'; Source = 'bin\omawin.cs' }
            @{ Exe = 'voice-meter'; Source = 'zebar\tokyo-islands\voice-meter.cs' }
            @{ Exe = 'cpu-cores'; Source = 'zebar\tokyo-islands\cpu-cores.cs' }
            @{ Exe = 'win-keys'; Source = 'zebar\tokyo-islands\win-keys.cs' }
            @{ Exe = 'app-icons'; Source = 'zebar\tokyo-islands\app-icons.cs' }
            @{ Exe = 'theme-watch'; Source = 'zebar\tokyo-islands\theme-watch.cs' }
            @{ Exe = 'omawin-listen'; Source = 'agent\omawin-listen.cs' }
        )
        $stale = @($helpers | Where-Object { -not (Test-Current (Join-Path $BinDir "$($_.Exe).exe") (Join-Path $Repo $_.Source)) })
        if (-not $stale) { Write-Note 'ok  omawin.exe and the helpers' }
        else {
            $zip = Get-ReleaseFile 'omawin-helpers-*.zip'
            if ($zip) {
                # A running helper (Zebar's voice-meter) locks its exe, and the archive can't
                # replace a locked file: those are moved aside first and keep running until the
                # bar restarts them.
                foreach ($h in $stale) {
                    $exe = Join-Path $BinDir "$($h.Exe).exe"
                    if (Test-Path $exe) {
                        Remove-Item "$exe.old" -Force -ErrorAction SilentlyContinue
                        try { Move-Item $exe "$exe.old" -ErrorAction Stop } catch { }
                    }
                }
                Expand-Archive -Path $zip -DestinationPath $BinDir -Force
                Write-Note 'omawin.exe and the helpers from the release'
            }
            elseif ($NoBuild) { Write-Warn "no release to download ($reason) and -NoBuild: left as they are" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "no release ($reason): building here" }
                try { & "$Repo\build\helpers.ps1" -OutDir $BinDir } catch { Write-Warn "the helpers didn't build: $_" }
            }
        }
    }

    # --- the menu app (%LOCALAPPDATA%\system-menu; the WinUI app on Win+Space) -------------------
    if ($WantMenu) {
        if (Test-Current (Join-Path $MenuDir 'SystemMenu.exe') (Join-Path $Repo 'menu-app')) { Write-Note 'ok  the system menu' }
        else {
            $zip = Get-ReleaseFile "SystemMenu-*-win-$Arch.zip"
            if ($zip) {
                if (-not $NoStart) { & "$Repo\menu-app\stop-menu.ps1" -Dir $MenuDir }
                Expand-Archive -Path $zip -DestinationPath $MenuDir -Force
                # Unpacked, not built: nothing started it (menu-app\build.ps1 does that itself).
                if (-not $NoStart -and -not (Get-Process SystemMenu -ErrorAction SilentlyContinue)) {
                    Start-Process (Join-Path $MenuDir 'SystemMenu.exe') -ArgumentList '--start-hidden'
                }
                Write-Note 'the system menu from the release'
            }
            elseif ($NoBuild) { Write-Warn "no system menu in a release ($reason) and -NoBuild: left as it is" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "no release ($reason): building here" }
                if (Get-Command dotnet -ErrorAction SilentlyContinue) {
                    try { & "$Repo\menu-app\build.ps1" -OutDir $MenuDir -BuildOnly:([bool]$NoStart) -Arch $Arch }
                    catch { Write-Warn "system menu build failed: $_" }
                } else { Write-Warn '.NET SDK not found; install Microsoft.DotNet.SDK.9, then run install.ps1 -LinkOnly' }
            }
        }
    }

    # --- omawin-music (one exe in ~/.local/bin) -------------------------------------------------
    if ($WantMusic) {
        if (Test-Current (Join-Path $BinDir 'omawin-music.exe') (Join-Path $Repo 'music')) { Write-Note 'ok  omawin-music.exe' }
        else {
            $fromRelease = Get-ReleaseFile "omawin-music-*-win-$Arch.exe"
            if ($fromRelease) {
                $dest = Join-Path $BinDir 'omawin-music.exe'
                if (-not $NoStart) {
                    # The bar restarts its feed (the same exe) within seconds, so the old one is
                    # renamed aside rather than waited for: a file in use can still be renamed.
                    Get-ChildItem $BinDir -Filter 'omawin-music.exe.old-*' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
                    Get-Process omawin-music -ErrorAction SilentlyContinue | Stop-Process -Force
                    if (Test-Path $dest) { Move-Item $dest "$dest.old-$(Get-Date -Format yyyyMMddHHmmss)" -Force }
                }
                Copy-Item -LiteralPath $fromRelease -Destination "$dest.part" -Force
                Move-Item -LiteralPath "$dest.part" -Destination $dest -Force
                Write-Note 'omawin-music.exe from the release'
            }
            elseif ($NoBuild) { Write-Warn "no music player in a release ($reason) and -NoBuild: left as it is" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "no release ($reason): building here" }
                if (Get-Command dotnet -ErrorAction SilentlyContinue) {
                    try { & "$Repo\music\build.ps1" -OutDir $BinDir -BuildOnly:([bool]$NoStart) -Arch $Arch }
                    catch { Write-Warn "music player build failed: $_" }
                } else { Write-Warn '.NET SDK not found: install Microsoft.DotNet.SDK.9, then run install.ps1 -LinkOnly' }
            }
        }
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
