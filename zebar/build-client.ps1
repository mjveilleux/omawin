# Builds Zebar's JavaScript client (the zebar package the bar's pages import) into one file,
# zebar/tokyo-islands/lib/zebar.js, with Bun. The bar then loads it from disk: at sign-in the
# network may not be up yet, and fetching it (seven files from esm.sh, ~40 more behind them)
# held the bar back. Zebar's client is GPL-3.0 and omawin is MIT, so the file is built on each
# PC (install/desktop.ps1) and never committed (.gitignore). Without it the pages fall back to
# esm.sh, as before.
#
# PowerShell 7 (the install starts it with pwsh, which its software step put in). Safe to run
# again: a build of this version is left alone.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$version = '3.3.1' # the client matching Zebar 3.3, as bar.html's fallback (esm.sh zebar@3.3)
$lib = Join-Path $PSScriptRoot 'tokyo-islands\lib'
$out = Join-Path $lib 'zebar.js'
$stamp = Join-Path $lib 'zebar.version'
if (-not $Force -and (Test-Path $out) -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $version)) { return 'ok' }
$bun = Get-Command bun -ErrorAction SilentlyContinue
if (-not $bun) { return 'skipped (no Bun yet: the bar loads the client from esm.sh)' }
$work = Join-Path ([IO.Path]::GetTempPath()) "omawin-zebar-client-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    Set-Content (Join-Path $work 'package.json') '{ "name": "omawin-zebar-client", "private": true }' -Encoding ASCII
    Set-Content (Join-Path $work 'entry.js') 'export * from "zebar";' -Encoding ASCII
    Push-Location $work
    try {
        & $bun.Source add "zebar@$version" --exact 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "bun add zebar@$version failed" }
        New-Item -ItemType Directory -Force $lib | Out-Null
        & $bun.Source build entry.js --outfile $out --minify --target browser 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'bun build failed' }
    }
    finally { Pop-Location }
    Set-Content $stamp $version -Encoding ASCII
    'built'
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
