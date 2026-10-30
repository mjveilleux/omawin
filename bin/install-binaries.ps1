<#
.SYNOPSIS
    omawin.exe and the bar's helpers, the menu app and omawin-music: a release's prebuilt files
    when there is one for this PC, otherwise built here.

.DESCRIPTION
    install.ps1's helpers step runs this (install/helpers.ps1). Per piece, in this order:

      1. what's already here and current is kept: put in from the same sources as this checkout's
         (their git ids, recorded in binaries.txt next to the state file; before there's a record,
         or with local edits, an exe newer than its sources);
      2. else the release's prebuilt file for this PC's architecture, downloaded and checked
         against the SHA-256 manifest published with it (bin/release.ps1: a mismatch is refused),
         when it was built from this checkout's sources (the tag's files against these). A
         checkout ahead of the release builds instead: the helpers always (csc.exe is in Windows),
         the menu app and the player when the .NET SDK is already here (else the release, and
         the checkout's changes come with the next one);
      3. else built here from this checkout (build/helpers.ps1, menu-app\build.ps1,
         music\build.ps1) - a dev checkout, offline, no release, no GitHub sign-in, no .NET SDK.

    Step 2 is the point: a new PC gets omawin.exe, the helpers, the menu app and the music player
    without the .NET SDK (a 1 GB dependency) or a compile. The SDK is installed only
    for step 3, when it comes to that (Install-DotNetSdk); the core has the runtime.

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
# OMAWIN_RELEASE_URL: a mirror or a locally built release for a whole install (the tools bundle reads it too).
if (-not $BaseUrl -and $env:OMAWIN_RELEASE_URL) { $BaseUrl = $env:OMAWIN_RELEASE_URL }
if ($env:OMAWIN_PREBUILT -eq '0') { $NoDownload = $true }
if ($env:OMAWIN_PREBUILT -eq '1') { $NoBuild = $true }
# The .NET SDK, for a build here: only now, when one is needed (the core has just the runtime).
# A zip unpacked with Windows' tar.exe (in Windows since 10 1803): Expand-Archive is many
# times slower on a folder of many files (the menu app's 38 MB). It's the fallback.
function Expand-Zip([string]$Zip, [string]$Dest) {
    New-Item -ItemType Directory -Force $Dest | Out-Null
    $tar = "$env:WINDIR\System32\tar.exe"
    if (Test-Path $tar) { & $tar -xf $Zip -C $Dest; if ($LASTEXITCODE -eq 0) { return } }
    Expand-Archive -Path $Zip -DestinationPath $Dest -Force
}

function Install-DotNetSdk {
    if (Test-DotNetSdk) { return $true }
    Write-Note 'installing the .NET 9 SDK to build here (about 1 GB, once)'
    winget install --id Microsoft.DotNet.SDK.9 --exact --silent --accept-source-agreements --accept-package-agreements | Out-Null
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
}

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

# What each piece was last put in from: its sources' git ids, a line each in binaries.txt next to
# the state file. Dates can't say that: a pull gives the sources new dates (so every piece looked
# stale and the release came down, even one built from older code), and files unpacked from a
# release keep the release's dates.
$sourcesFile = Join-Path (Split-Path $StateFile) 'binaries.txt'
$installedFrom = @{}
if (Test-Path $sourcesFile) {
    foreach ($line in Get-Content $sourcesFile) { $k, $v = $line -split "`t", 2; if ($v) { $installedFrom[$k] = $v } }
}
function Save-Installed([string]$Piece, [string]$Value) {
    if (-not $Value) { return }
    $installedFrom[$Piece] = $Value
    New-Item -ItemType Directory -Force (Split-Path $sourcesFile) | Out-Null
    Set-Content -LiteralPath $sourcesFile -Value @($installedFrom.Keys | Sort-Object | ForEach-Object { "$_`t$($installedFrom[$_])" })
}
function Invoke-RepoGit {
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & git.exe -C $Repo @args 2>$null } finally { $ErrorActionPreference = $saved }
}
# The committed sources' ids, or $null when git can't say (no git, not a checkout, local edits).
function Get-SourceKey([string[]]$Paths) {
    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { return $null }
    $ids = foreach ($p in $Paths) {
        $id = Invoke-RepoGit rev-parse "HEAD:$p"
        if ($LASTEXITCODE -ne 0 -or -not $id) { return $null }
        $id
    }
    if (Invoke-RepoGit status --porcelain -- @Paths) { return $null }
    return ($ids -join ',')
}
# Current: put in from these same sources. "<key>|<tag>" is a release taken although the checkout
# is ahead of it (it couldn't be built here): current until there's a newer release.
# Nothing recorded yet (put in before this kept a record) or local edits: the dates decide.
function Test-Installed([string]$Piece, [string]$Key, [scriptblock]$ByDate) {
    $was = $installedFrom[$Piece]
    if ($Key -and $was) {
        if ($was -eq $Key) { return $true }
        if ($was.StartsWith("$Key|")) {
            if (-not (Test-UseRelease)) { return $true }
            return ($was -eq "$Key|$script:releaseTag")
        }
        return $false
    }
    if (& $ByDate) { Save-Installed $Piece $Key; return $true }
    return $false
}
# Whether the release was built from this checkout's sources for these paths (its tag against the
# files here). $true when git can't tell: a test's fake release, a tag that isn't here.
function Test-ReleaseHas([string[]]$Paths) {
    $tag = $script:releaseTag
    if (-not $tag -or $tag -eq 'latest' -or -not (Get-Command git.exe -ErrorAction SilentlyContinue)) { return $true }
    $null = Invoke-RepoGit rev-parse --verify --quiet "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { $null = Invoke-RepoGit fetch --quiet origin "refs/tags/${tag}:refs/tags/$tag" }
    $null = Invoke-RepoGit rev-parse --verify --quiet "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { return $true }
    $null = Invoke-RepoGit diff --quiet "refs/tags/$tag" -- @Paths
    return ($LASTEXITCODE -eq 0)
}
# A piece's release file, unless this checkout's sources differ from the release's and they can be
# built here ($CanBuild): then $null, and the caller builds. $script:sameAsRelease says which.
function Get-PieceRelease([string]$Pattern, [string[]]$Paths, [bool]$CanBuild) {
    if (-not (Test-UseRelease)) { return $null }
    $script:sameAsRelease = Test-ReleaseHas $Paths
    if (-not $script:sameAsRelease -and $CanBuild) { $script:reason = "this checkout's code is newer than $script:releaseTag"; return $null }
    return (Get-ReleaseFile $Pattern)
}
function Test-DotNetSdk { [bool]((Get-Command dotnet -ErrorAction SilentlyContinue) -and (& dotnet --list-sdks 2>$null | Select-String '^9\.')) }
# What to record for a piece taken from the release.
function Get-ReleaseKey([string]$Key) { if ($script:sameAsRelease) { $Key } elseif ($Key) { "$Key|$script:releaseTag" } }
$sameAsRelease = $true

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
        $paths = @($helpers | ForEach-Object { $_.Source -replace '\\', '/' })
        $key = Get-SourceKey $paths
        $current = Test-Installed 'helpers' $key {
            -not @($helpers | Where-Object { -not (Test-Current (Join-Path $BinDir "$($_.Exe).exe") (Join-Path $Repo $_.Source)) })
        }
        if ($current) { Write-Note 'ok  omawin.exe and the helpers' }
        else {
            # csc.exe is in Windows, so a checkout ahead of the release is always built here.
            $zip = Get-PieceRelease 'omawin-helpers-*.zip' $paths (-not $NoBuild)
            if ($zip) {
                # Unpacked aside, then put in one file at a time. A running helper (the bar's
                # theme-watch, voice-meter) locks its exe: it's moved aside, and keeps running until
                # the bar restarts it. One that can't be moved stays as it is, for the next update;
                # unpacking straight over the folder stopped at such a file, halfway, with
                # omawin.exe already moved aside and no new one written.
                $unpacked = Join-Path $work 'helpers'
                Expand-Zip $zip $unpacked
                $kept = @()
                foreach ($file in Get-ChildItem $unpacked -File) {
                    $dest = Join-Path $BinDir $file.Name
                    try {
                        if (Test-Path $dest) {
                            Remove-Item "$dest.old" -Force -ErrorAction SilentlyContinue
                            Move-Item $dest "$dest.old" -Force -ErrorAction Stop
                        }
                        Copy-Item $file.FullName $dest -Force -ErrorAction Stop
                    } catch {
                        # Never leave one missing (omawin.exe is every key and the sign-in start).
                        if (-not (Test-Path $dest) -and (Test-Path "$dest.old")) { Move-Item "$dest.old" $dest }
                        $kept += $file.Name
                    }
                }
                if ($kept) { Write-Note "omawin.exe and the helpers from the release; $($kept -join ', ') in use, so the current one stays until the next update" }
                else {
                    Write-Note 'omawin.exe and the helpers from the release'
                    Save-Installed 'helpers' (Get-ReleaseKey $key)
                }
            }
            elseif ($NoBuild) { Write-Warn "no release to download ($reason) and -NoBuild: left as they are" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "building here: $reason" }
                try { & "$Repo\build\helpers.ps1" -OutDir $BinDir; Save-Installed 'helpers' $key } catch { Write-Warn "the helpers didn't build: $_" }
            }
        }
    }

    # --- the menu app (%LOCALAPPDATA%\system-menu; the WinUI app on Win+Space) -------------------
    if ($WantMenu) {
        $key = Get-SourceKey @('menu-app')
        if (Test-Installed 'menu' $key { Test-Current (Join-Path $MenuDir 'SystemMenu.exe') (Join-Path $Repo 'menu-app') }) { Write-Note 'ok  the system menu' }
        else {
            # Built here only when the .NET SDK (1 GB) is already on the PC; else the release, and
            # this checkout's changes come with the next one.
            $zip = Get-PieceRelease "SystemMenu-*-win-$Arch.zip" @('menu-app') (-not $NoBuild -and (Test-DotNetSdk))
            if ($zip) {
                if (-not $NoStart) { & "$Repo\menu-app\stop-menu.ps1" -Dir $MenuDir }
                Expand-Zip $zip $MenuDir
                # Unpacked, not built: nothing started it (menu-app\build.ps1 does that itself).
                # This account's: another account signed in has its own, and counting it left this
                # one stopped (stop-menu.ps1 stops only this account's).
                if (-not $NoStart -and -not (Get-Process SystemMenu -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId)) {
                    Start-Process (Join-Path $MenuDir 'SystemMenu.exe') -ArgumentList '--start-hidden'
                }
                Write-Note 'the system menu from the release'
                Save-Installed 'menu' (Get-ReleaseKey $key)
            }
            elseif ($NoBuild) { Write-Warn "no system menu in a release ($reason) and -NoBuild: left as it is" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "building here: $reason" }
                if (Install-DotNetSdk) {
                    try { & "$Repo\menu-app\build.ps1" -OutDir $MenuDir -BuildOnly:([bool]$NoStart) -Arch $Arch; Save-Installed 'menu' $key }
                    catch { Write-Warn "system menu build failed: $_" }
                } else { Write-Warn '.NET SDK did not install; winget install Microsoft.DotNet.SDK.9, then install.ps1 -LinkOnly' }
            }
        }
    }

    # --- omawin-music (one exe in ~/.local/bin) -------------------------------------------------
    if ($WantMusic) {
        $key = Get-SourceKey @('music')
        if (Test-Installed 'music' $key { Test-Current (Join-Path $BinDir 'omawin-music.exe') (Join-Path $Repo 'music') }) { Write-Note 'ok  omawin-music.exe' }
        else {
            # As the menu: built here only with the .NET SDK already on the PC.
            $fromRelease = Get-PieceRelease "omawin-music-*-win-$Arch.exe" @('music') (-not $NoBuild -and (Test-DotNetSdk))
            if ($fromRelease) {
                $dest = Join-Path $BinDir 'omawin-music.exe'
                if (-not $NoStart) {
                    # The bar restarts its feed (the same exe) within seconds, so the old one is
                    # renamed aside rather than waited for: a file in use can still be renamed.
                    Get-ChildItem $BinDir -Filter 'omawin-music.exe.old-*' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
                    Get-Process omawin-music -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
                    if (Test-Path $dest) { Move-Item $dest "$dest.old-$(Get-Date -Format yyyyMMddHHmmss)" -Force }
                }
                Copy-Item -LiteralPath $fromRelease -Destination "$dest.part" -Force
                Move-Item -LiteralPath "$dest.part" -Destination $dest -Force
                Write-Note 'omawin-music.exe from the release'
                Save-Installed 'music' (Get-ReleaseKey $key)
            }
            elseif ($NoBuild) { Write-Warn "no music player in a release ($reason) and -NoBuild: left as it is" }
            else {
                if ($reason -and $reason -ne '-NoDownload') { Write-Note "building here: $reason" }
                if (Install-DotNetSdk) {
                    try { & "$Repo\music\build.ps1" -OutDir $BinDir -BuildOnly:([bool]$NoStart) -Arch $Arch; Save-Installed 'music' $key }
                    catch { Write-Warn "music player build failed: $_" }
                } else { Write-Warn '.NET SDK did not install; winget install Microsoft.DotNet.SDK.9, then install.ps1 -LinkOnly' }
            }
        }
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
