# Assembles a release: names the built files the way install expects, zips what is a folder, and
# writes SHA256SUMS.txt beside them (the manifest bin/release.ps1 verifies against).
#
# One place decides the asset names, so CI (.github/workflows/release.yml) and a locally built
# fake release (docs/releases.md, scratch) are laid out identically:
#   omawin-helpers-<tag>.zip                 omawin.exe and the bar's helpers (any architecture)
#   SystemMenu-<tag>-win-<arch>.zip          the menu app, unpacked into %LOCALAPPDATA%\system-menu
#   omawin-music-<tag>-win-<arch>.exe        the music player, into ~/.local/bin
#   omawin-tools.txt, omawin-tool-<name>.zip the tools bundle (build/bundle), as built
#   SHA256SUMS.txt                           `<sha256>  <file>` per asset
#
# The input layout is what the jobs produce:
#   <InDir>\helpers\*.exe          build/helpers.ps1 -OutDir <InDir>\helpers
#   <InDir>\menu-<arch>\           shell/menu-app/build.ps1 -BuildOnly -OutDir <InDir>\menu-<arch>
#   <InDir>\music-<arch>\omawin-music.exe   src/music/build.ps1 -BuildOnly -OutDir <InDir>\music-<arch>
#
#   .\build\release.ps1 -Tag v0.1.0 -InDir scratch\built -OutDir scratch\release
#   .\build\release.ps1 -Tag v0.1.0 -InDir out -OutDir dist -Arch x64   (one architecture)
#   .\build\release.ps1 -Tag v0.1.0 -InDir out -OutDir dist -NoManifest   (pack)
#   ...sign the files...  .\build\release.ps1 -Tag v0.1.0 -OutDir dist -OnlyManifest
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$InDir,                # the built pieces; not needed with -OnlyManifest
    [Parameter(Mandatory)][string]$OutDir,
    [string[]]$Arch = @('x64', 'arm64'),
    # Signing sits between the two: the ZIPs are built, signed, and only then hashed, so the
    # manifest describes the signed bytes (docs/releases.md).
    [switch]$NoManifest,           # pack only
    [switch]$OnlyManifest          # hash what is in -OutDir, pack nothing
)
$ErrorActionPreference = 'Stop'
if ($InDir -and -not [IO.Path]::IsPathRooted($InDir)) { $InDir = Join-Path (Get-Location).Path $InDir }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Add-Zip([string]$Dir, [string]$Name) {
    $zip = Join-Path $OutDir $Name
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    # The folder's contents, not the folder: the ZIP unpacks straight into place.
    Compress-Archive -Path (Join-Path $Dir '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "  $Name  ($((Get-Item $zip).Length) bytes)"
}
function Add-File([string]$Path, [string]$Name) {
    Copy-Item -LiteralPath $Path -Destination (Join-Path $OutDir $Name) -Force
    Write-Host "  $Name  ($((Get-Item (Join-Path $OutDir $Name)).Length) bytes)"
}

# The C# 5 helpers: architecture-neutral IL (csc's default), the same files on x64 and ARM64.
if (-not $OnlyManifest) {
    if (-not $InDir) { throw '-InDir is needed to pack a release (the built pieces)' }
    $helpers = Get-ChildItem (Join-Path $InDir 'helpers') -Filter *.exe -ErrorAction SilentlyContinue
    if (-not $helpers) { throw "no helper exes in $InDir\helpers (build/helpers.ps1 -OutDir)" }
    Add-Zip (Join-Path $InDir 'helpers') "omawin-helpers-$Tag.zip"

    foreach ($a in $Arch) {
        $menu = Join-Path $InDir "menu-$a"
        if (Test-Path (Join-Path $menu 'SystemMenu.exe')) { Add-Zip $menu "SystemMenu-$Tag-win-$a.zip" }
        else { Write-Warning "no menu app for win-$a ($menu): left out of this release" }

        $music = Join-Path $InDir "music-$a\omawin-music.exe"
        if (Test-Path $music) { Add-File $music "omawin-music-$Tag-win-$a.exe" }
        else { Write-Warning "no music player for win-$a ($music): left out of this release" }
    }

    # The tools bundle (build/bundle): its zips and list, published as they are.
    $tools = Join-Path $InDir 'tools'
    if (Test-Path (Join-Path $tools 'omawin-tools.txt')) { Get-ChildItem $tools -File | ForEach-Object { Add-File $_.FullName $_.Name } }
    else { Write-Warning "no tools bundle ($tools): left out of this release" }
    # Neovim's prebuilt treesitter parsers (release.yml's parsers job): one more bundle entry,
    # merged into Neovim's data folder (no PATH, nothing of yours moved aside).
    $parsers = Join-Path $InDir 'parsers\omawin-tool-nvim-parsers.zip'
    $list = Join-Path $OutDir 'omawin-tools.txt'
    if ((Test-Path $parsers) -and (Test-Path $list)) {
        Add-File $parsers 'omawin-tool-nvim-parsers.zip'
        $version = (Get-Content (Join-Path $InDir 'parsers\version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
        $sha = (Get-FileHash $parsers -Algorithm SHA256).Hash.ToLowerInvariant()
        $row = @('nvim-parsers', $(if ($version) { $version } else { 'unknown' }), 'omawin-tool-nvim-parsers.zip', $sha, '-', '', 'nvim-data\site', 'merge') -join "`t"
        $kept = @(Get-Content $list | Where-Object { $_ -notlike "nvim-parsers`t*" })
        [IO.File]::WriteAllLines($list, [string[]]($kept + $row))
    } elseif (Test-Path $list) { Write-Warning "no prebuilt Neovim parsers ($parsers): left out of this release" }
    # The theme picker's pictures (release.yml's thumbs job): merged into the folder the menu app
    # keeps them in, not waited for (later).
    $thumbs = Join-Path $InDir 'thumbs\omawin-tool-theme-thumbs.zip'
    if ((Test-Path $thumbs) -and (Test-Path $list)) {
        Add-File $thumbs 'omawin-tool-theme-thumbs.zip'
        $version = (Get-Content (Join-Path $InDir 'thumbs\version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
        $sha = (Get-FileHash $thumbs -Algorithm SHA256).Hash.ToLowerInvariant()
        $row = @('theme-thumbs', $(if ($version) { $version } else { 'unknown' }), 'omawin-tool-theme-thumbs.zip', $sha, '-', '', 'launcher\thumbnails', 'merge later') -join "`t"
        $kept = @(Get-Content $list | Where-Object { $_ -notlike "theme-thumbs`t*" })
        [IO.File]::WriteAllLines($list, [string[]]($kept + $row))
    } elseif (Test-Path $list) { Write-Warning "no theme pictures ($thumbs): left out of this release" }
    # The plugins' QML host (release.yml's qs-host job): optional, so only a PC that turns a plugin
    # on downloads it (install/software.ps1); in %LOCALAPPDATA%\omawin\tools\qs-host, not on the PATH.
    $qs = Join-Path $InDir 'qs-host\omawin-tool-qs-host.zip'
    if ((Test-Path $qs) -and (Test-Path $list)) {
        Add-File $qs 'omawin-tool-qs-host.zip'
        $version = (Get-Content (Join-Path $InDir 'qs-host\version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
        $sha = (Get-FileHash $qs -Algorithm SHA256).Hash.ToLowerInvariant()
        $row = @('qs-host', $(if ($version) { $version } else { 'unknown' }), 'omawin-tool-qs-host.zip', $sha, '-', '', '', 'optional') -join "`t"
        $kept = @(Get-Content $list | Where-Object { $_ -notlike "qs-host`t*" })
        [IO.File]::WriteAllLines($list, [string[]]($kept + $row))
    } elseif (Test-Path $list) { Write-Warning "no plugin host ($qs): left out of this release" }
}

if (-not $NoManifest) {
    # The hashes published with the release: what bin/release.ps1 checks a download against
    # (the fork releases' format, SHA256SUMS-windows-x64.txt, is the same idea).
    $lines = @(Get-ChildItem $OutDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name |
        ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" })
    if (-not $lines) { throw "nothing in $OutDir to publish" }
    Set-Content -LiteralPath (Join-Path $OutDir 'SHA256SUMS.txt') -Value $lines
    Write-Host "  SHA256SUMS.txt  ($($lines.Count) files)"
}
