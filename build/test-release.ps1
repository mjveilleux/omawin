# Proves the release path (docs/releases.md) without touching this PC's installed copies:
#   - builds the pieces into scratch\built (the same commands CI runs),
#   - packs a fake release from them (build/release.ps1: the assets and SHA256SUMS.txt),
#   - installs from that release into scratch\ (bin/install-binaries.ps1 -NoBuild -NoStart),
#   - the installed binaries run, the second run is a no-op,
#   - a corrupted asset and a name the manifest doesn't list are both refused, with nothing left,
#   - a mirror with no release falls back to building here.
# Nothing outside scratch\ is written (-StateFile is redirected too); the menu app and the music
# player are never stopped or started (-NoStart), so the running desktop is left alone.
#
#   pwsh -File build\test-release.ps1                  the full run
#   pwsh -File build\test-release.ps1 -SkipBuild       use what scratch\ already has
#   pwsh -File build\test-release.ps1 -Http            also serve the release over http
param(
    [switch]$SkipBuild,        # don't build the pieces: use what scratch\built already has
    [switch]$SkipFallback,     # skip the "no release: build here" case (it builds)
    [switch]$Http,             # also test a mirror served by python -m http.server
    [string]$Tag = 'v0.1.0',
    [string[]]$Arch = @('x64', 'arm64')
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Scratch = Join-Path $Repo 'scratch'
$Built = Join-Path $Scratch 'built'
$Release = Join-Path $Scratch 'release'
$failed = 0
$server = $null

function Say([string]$Text) { Write-Host "`n$Text" -ForegroundColor Cyan }
function Check([string]$Name, [scriptblock]$Body) {
    try { $r = & $Body; if ($r -eq $false) { throw 'failed' }; Write-Host "  ok    $Name" -ForegroundColor Green }
    catch { Write-Host "  FAIL  $Name" -ForegroundColor Red; Write-Host "        $_" -ForegroundColor Red; $script:failed++ }
}
function Refused([scriptblock]$Body, [string]$Want) {
    # The call must throw, and say why (Refused: for Check's $false-means-fail above).
    try { & $Body | Out-Null } catch { if ($_.Exception.Message -like "*$Want*") { return $true } ; throw "refused with the wrong reason: $($_.Exception.Message)" }
    throw "was NOT refused (expected: $Want)"
}
function Get-PeMachine([string]$Path) {
    # 'MZ' at 0, the PE header's offset at 0x3C, the machine word just past 'PE\0\0'.
    $fs = [IO.File]::OpenRead($Path)
    try {
        $br = New-Object IO.BinaryReader($fs)
        $fs.Position = 0x3C
        $fs.Position = $br.ReadInt32() + 4
        switch ($br.ReadUInt16()) { 0x8664 { 'x64' } 0xAA64 { 'arm64' } 0x14c { 'IL' } default { 'unknown' } }
    } finally { $fs.Dispose() }
}

try {
    # --- 1. the pieces, as CI builds them -------------------------------------------------------
    if (-not $SkipBuild) {
        Say 'building the pieces (what CI runs, into scratch\built)'
        New-Item -ItemType Directory -Force "$Built\helpers" | Out-Null
        & "$Repo\build\helpers.ps1" -OutDir "$Built\helpers" -Release $Tag
        foreach ($a in $Arch) {
            & "$Repo\shell\menu-app\build.ps1" -OutDir "$Built\menu-$a" -BuildOnly -Release $Tag -Arch $a
            & "$Repo\src\music\build.ps1" -OutDir "$Built\music-$a" -BuildOnly -Release $Tag -Arch $a
            & "$Repo\src\windows\build.ps1" -OutDir "$Built\windows-$a" -BuildOnly -Release $Tag -Arch $a
        }
    } elseif (-not (Test-Path "$Built\helpers\omawin.exe")) { throw "-SkipBuild: nothing built yet in $Built (run without it once)" }

    # --- 2. the fake release --------------------------------------------------------------------
    Say "packing the fake release ($Tag) into scratch\release"
    Remove-Item -Recurse -Force $Release -ErrorAction SilentlyContinue
    # The workflow's order: pack, (sign), then the manifest - so a signature can sit in between and
    # the hashes still describe the files that are published.
    # -AllowJit: this PC may not have the C++ tools for the native build; CI insists on it.
    & "$Repo\build\release.ps1" -Tag $Tag -InDir $Built -OutDir $Release -Arch $Arch -NoManifest -AllowJit
    & "$Repo\build\release.ps1" -Tag $Tag -OutDir $Release -OnlyManifest
    Check 'a manifest with every asset' {
        $names = (Get-Content "$Release\SHA256SUMS.txt") | ForEach-Object { ($_ -split '\s+')[1] }
        foreach ($want in @("omawin-helpers-$Tag.zip") + ($Arch | ForEach-Object { "SystemMenu-$Tag-win-$_.zip", "omawin-music-$Tag-win-$_.exe", "omawin-windows-$Tag-win-$_.exe" })) {
            if ($names -notcontains $want) { throw "no $want in SHA256SUMS.txt" }
        }
        $true
    }

    # --- 3. install from it (download path only: -NoBuild, and -NoStart: nothing is touched) -----
    $into = Join-Path $Scratch 'installed'
    Remove-Item -Recurse -Force $into -ErrorAction SilentlyContinue
    Say "installing from the release into $into"
    & "$Repo\bin\install-binaries.ps1" -BaseUrl $Release -BinDir "$into\bin" -MenuDir "$into\menu" `
        -StateFile "$into\release.txt" -NoBuild -NoStart -Release $Tag
    Check 'the helpers, the menu app and the player are all there' {
        foreach ($f in 'omawin.exe', 'omawin-music.exe', 'omawin-windows.exe') { if (-not (Test-Path "$into\bin\$f")) { throw "no $f" } }
        if (-not (Test-Path "$into\menu\SystemMenu.exe")) { throw 'no SystemMenu.exe' }
        if (-not (Test-Path "$into\menu\fff_c.dll")) { throw 'no fff_c.dll (file search)' }
        $true
    }
    Check 'the architecture of this PC was picked: its own assets, not the other ones' {
        foreach ($f in "$into\menu\SystemMenu.exe", "$into\menu\fff_c.dll", "$into\bin\omawin-music.exe") {
            $m = Get-PeMachine $f
            $want = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
            if ($m -ne $want) { throw "$([IO.Path]::GetFileName($f)) is $m, not $want" }
        }
        # And the release holds the other one too (the matrix built it), so the two differ.
        $other = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'x64' } else { 'arm64' }
        if ($Arch -contains $other -and (Get-PeMachine "$Built\menu-$other\SystemMenu.exe") -ne $other) {
            throw "the release's $other menu app is not $other"
        }
        $true
    }
    Check "the release this PC is on is recorded ($Tag)" { (Get-Content "$into\release.txt" -Raw).Trim() -eq $Tag }
    Check 'the downloaded omawin.exe answers: omawin help and --version' {
        $env:DOTFILES = $Repo
        $v = & "$into\bin\omawin.exe" --version
        if ($v -ne "omawin $Tag") { throw "--version said '$v'" }
        $null = & "$into\bin\omawin.exe" help
        if ($LASTEXITCODE -ne 0) { throw "omawin help exited $LASTEXITCODE" }
        $true
    }
    Check 'the downloaded omawin-music.exe answers --version' {
        $v = & "$into\bin\omawin-music.exe" --version
        if ($v -ne "omawin-music $Tag") { throw "--version said '$v'" }
        $true
    }
    Check 'the downloaded omawin-windows.exe answers --version' {
        $v = & "$into\bin\omawin-windows.exe" --version
        if ($v -ne "omawin-windows $Tag") { throw "--version said '$v'" }
        $true
    }
    Check "the downloaded menu app carries the release ($Tag)" {
        $v = (Get-Item "$into\menu\SystemMenu.exe").VersionInfo.ProductVersion
        if ($v -ne $Tag) { throw "SystemMenu.exe says '$v'" }
        $true
    }

    # --- 4. a second run changes nothing (nothing is downloaded twice) ---------------------------
    Say 'running it again: everything is current'
    $again = & "$Repo\bin\install-binaries.ps1" -BaseUrl $Release -BinDir "$into\bin" -MenuDir "$into\menu" `
        -StateFile "$into\release.txt" -NoBuild -NoStart -Release $Tag 6>&1 | Out-String
    Check 'nothing is downloaded again' {
        if ($again -match 'got ') { throw "downloaded again: $again" }
        $true
    }

    # --- 5. a corrupted asset is refused --------------------------------------------------------
    $bad = Join-Path $Scratch 'mirror-bad'
    Remove-Item -Recurse -Force $bad, (Join-Path $Scratch 'refused') -ErrorAction SilentlyContinue
    Copy-Item $Release $bad -Recurse
    Add-Content -Path (Join-Path $bad "omawin-helpers-$Tag.zip") -Value 'x' -NoNewline
    Say 'a corrupted asset (one byte more than the release has)'
    Check 'the hash mismatch is refused' {
        Refused -Want "doesn't match its published SHA-256" -Body {
            & "$Repo\bin\release.ps1" -BaseUrl $bad -OutDir (Join-Path $Scratch 'refused') -Asset "omawin-helpers-*.zip"
        }
    }
    Check 'nothing from a refused download is left behind' {
        if (Get-ChildItem (Join-Path $Scratch 'refused') -File -ErrorAction SilentlyContinue) { throw 'a file was left' }
        $true
    }
    Check 'install-binaries falls back rather than install a refused file' {
        $out = & "$Repo\bin\install-binaries.ps1" -BaseUrl $bad -BinDir (Join-Path $Scratch 'refused\bin') `
            -MenuDir (Join-Path $Scratch 'refused\menu') -StateFile (Join-Path $Scratch 'refused\release.txt') `
            -NoBuild -NoStart -Release $Tag 6>&1 | Out-String
        if ($out -notmatch 'no release .*SHA-256') { throw "didn't report the mismatch: $out" }
        $true
    }

    # --- 6. a name the manifest doesn't have ----------------------------------------------------
    Say 'asking for a file the release does not have'
    Check 'it is refused, and the release list is shown' {
        Refused -Want 'the release has no file matching' -Body {
            & "$Repo\bin\release.ps1" -BaseUrl $Release -OutDir (Join-Path $Scratch 'refused') -Asset 'omawin-nothing-*.zip'
        }
    }

    # --- 7. a mirror with no release: build here ------------------------------------------------
    if (-not $SkipFallback) {
        Say 'a mirror with no release at all: it builds here instead'
        $fb = Join-Path $Scratch 'fallback'
        Remove-Item -Recurse -Force $fb -ErrorAction SilentlyContinue
        $env:DOTNET_NOLOGO = '1'
        & "$Repo\bin\install-binaries.ps1" -BaseUrl (Join-Path $Scratch 'nowhere') -BinDir "$fb\bin" -MenuDir "$fb\menu" `
            -StateFile "$fb\release.txt" -NoStart
        Check 'the fallback built omawin.exe, the menu app and the player here' {
            foreach ($f in "$fb\bin\omawin.exe", "$fb\menu\SystemMenu.exe", "$fb\bin\omawin-music.exe") {
                if (-not (Test-Path $f)) { throw "no $f" }
            }
            $true
        }
        Check 'a built (not downloaded) omawin.exe says (dev)' {
            $env:DOTFILES = $Repo
            $v = & "$fb\bin\omawin.exe" --version
            if ($v -ne 'omawin (dev)') { throw "--version said '$v'" }
            $true
        }
    }

    # --- 8. the same over http (python -m http.server) ------------------------------------------
    if ($Http) {
        Say 'serving the release over http'
        $port = 8642
        $server = Start-Process python -ArgumentList '-m', 'http.server', $port, '--directory', $Release -PassThru -WindowStyle Hidden
        foreach ($i in 1..20) { Start-Sleep -Milliseconds 250; try { $null = Invoke-WebRequest "http://127.0.0.1:$port/SHA256SUMS.txt" -UseBasicParsing; break } catch { } }
        $into2 = Join-Path $Scratch 'installed-http'
        Remove-Item -Recurse -Force $into2 -ErrorAction SilentlyContinue
        Check 'it installs from an http mirror, hash-checked' {
            & "$Repo\bin\install-binaries.ps1" -BaseUrl "http://127.0.0.1:$port" -BinDir "$into2\bin" -MenuDir "$into2\menu" `
                -StateFile "$into2\release.txt" -Tag $Tag -NoBuild -NoStart -Release $Tag
            if (-not (Test-Path "$into2\bin\omawin.exe")) { throw 'nothing installed' }
            $true
        }
        Check 'the download over http is the release file (same hash as the folder copy)' {
            (Get-FileHash "$into2\bin\omawin-music.exe" -Algorithm SHA256).Hash -eq
                (Get-FileHash (Join-Path $Release "omawin-music-$Tag-win-x64.exe") -Algorithm SHA256).Hash
        }
    }
} finally {
    if ($server) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}

if ($failed) { Write-Host "`n$failed check(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "`nall checks passed" -ForegroundColor Green
