# Zebar is gone: the bar is the menu app's own (docs/native-bar.md), and omawin removes what it put
# in for Zebar. Safe on a new PC (none of it is there) and twice.
#
#   - Zebar stopped in this session, and the program uninstalled when it's this account's (winget,
#     per-user). One installed for the whole PC needs admin to remove, which an update never asks
#     for: said here, for Install > Remove (or `winget uninstall glzr-io.zebar` from an admin shell).
#   - The bar's choice (%LOCALAPPDATA%\launcher\bar): there's one bar now.
#   - ~/.glzr/zebar, when it's omawin's link into the repo (a folder of yours stays).
#   - What Zebar's setup generated in the checkout's zebar/ folder (its theme.css, its built
#     client, WebView caches), and the folder once empty.
#   - The helper exes only Zebar used (cpu-cores, win-keys, app-icons, theme-watch in ~/.local/bin).
#   - ZEBAR_ASSET_PORT (this account's Zebar port).
$repo = Split-Path $PSScriptRoot

Get-Process zebar -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId |
    Stop-Process -Force -ErrorAction SilentlyContinue

Remove-Item "$env:LOCALAPPDATA\launcher\bar" -Force -ErrorAction SilentlyContinue

$link = Get-Item "$HOME\.glzr\zebar" -Force -ErrorAction SilentlyContinue
if ($link -and $link.LinkType -and "$($link.Target)" -like "$repo*") {
    # The link only: [IO.Directory]::Delete on a junction removes the link, never what it points to.
    [IO.Directory]::Delete($link.FullName)
    'removed ~/.glzr/zebar (the link to the old bar)'
}

$pack = Join-Path $repo 'zebar'
if (Test-Path $pack) {
    foreach ($generated in 'tokyo-islands\theme.css', 'tokyo-islands\lib', 'tokyo-islands\node_modules') {
        Remove-Item (Join-Path $pack $generated) -Recurse -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem $pack -Directory -Filter 'tmp-*' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    # Empty folders up from the bottom; anything left is yours and stays.
    Get-ChildItem $pack -Directory -Recurse -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Force -ErrorAction SilentlyContinue) } | Remove-Item -Force -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem $pack -Force -ErrorAction SilentlyContinue)) { Remove-Item $pack -Force -ErrorAction SilentlyContinue }
}

foreach ($exe in 'cpu-cores', 'win-keys', 'app-icons', 'theme-watch') {
    Get-Process $exe -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item "$HOME\.local\bin\$exe.exe" -Force -ErrorAction SilentlyContinue
}

if ([Environment]::GetEnvironmentVariable('ZEBAR_ASSET_PORT', 'User')) { [Environment]::SetEnvironmentVariable('ZEBAR_ASSET_PORT', $null, 'User') }

if (Get-Command winget -ErrorAction SilentlyContinue) {
    & { $ErrorActionPreference = 'Continue'; winget pin remove --id glzr-io.zebar --exact *> $null }
    & { $ErrorActionPreference = 'Continue'; winget list --id glzr-io.zebar --exact --scope user --accept-source-agreements *> $null }
    if ($LASTEXITCODE -eq 0) {
        & { $ErrorActionPreference = 'Continue'; winget uninstall --id glzr-io.zebar --exact --scope user --silent --disable-interactivity *> $null }
        if ($LASTEXITCODE -eq 0) { 'uninstalled Zebar (this account''s)' } else { "! couldn't uninstall Zebar (winget exited $LASTEXITCODE): Install > Remove" }
    }
}
if (Test-Path "$env:ProgramFiles\glzr.io\Zebar\zebar.exe") {
    '! Zebar is still installed for the whole PC; removing it takes admin: Install > Remove, or `winget uninstall glzr-io.zebar` in an admin terminal'
}
