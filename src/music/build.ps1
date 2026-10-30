# Builds omawin-music (the music player behind the bar's record) into ~/.local/bin as one exe.
# Needs the .NET 9 SDK (or a release's exe is downloaded instead: bin/install-binaries.ps1). Safe
# to run again: the running player is stopped first. The bar starts its feed (the same exe) again
# within seconds, so the old exe is renamed aside rather than waited for (a file in use can still
# be renamed); old ones are cleared at a later build.
#
#   .\build.ps1                                       what install.ps1 -LinkOnly does
#   .\build.ps1 -OutDir ..\..\scratch\bin -BuildOnly      build only, touch no running app (CI)
#
# CI (.github/workflows/release.yml) runs this same script for each architecture.
param(
    [string]$OutDir = (Join-Path $HOME '.local\bin'),
    [ValidateSet('x64', 'arm64')][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    # Build only: don't stop a running player, don't rename the old exe aside. For CI and for a
    # build into a scratch folder, which must leave the running music alone.
    [switch]$BuildOnly,
    # The release this build is from (omawin-music --version); CI passes the tag.
    [string]$Release
)
$ErrorActionPreference = 'Stop'
# Absolute before anything changes directory (the dotnet call runs from the project folder).
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }
$out = $OutDir
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'omawin-music.exe'
$old = $null
if (-not $BuildOnly) {
    Get-ChildItem $out -Filter 'omawin-music.exe.old-*' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    Get-Process omawin-music -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
    # The player's mpv (Mpv.cs) would play on without it.
    Get-CimInstance Win32_Process -Filter "Name='mpv.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId -and $_.CommandLine -like '*omawin-mpv*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (Test-Path $exe) {
        $old = Join-Path $out "omawin-music.exe.old-$(Get-Date -Format yyyyMMddHHmmss)"
        Rename-Item $exe $old
    }
}
$publish = @('-c', 'Release', '-o', $out, '-nologo', '-v', 'q', '-p:DebugType=None')
if ($Arch -ne 'x64') { $publish += "-p:RuntimeIdentifier=win-$Arch" }
if ($Release) { $publish += "-p:OmawinRelease=$Release" }
$ok = $false
try {
    # From the project folder, so the repo's global.json (the pinned SDK) applies.
    Push-Location $PSScriptRoot
    try { dotnet publish "$PSScriptRoot\Music.csproj" @publish }
    finally { Pop-Location }
    $ok = $LASTEXITCODE -eq 0 -and (Test-Path $exe)
} finally {
    # Put the old exe back, so a failed build still leaves a working player and bar feed.
    if (-not $ok -and $old -and -not (Test-Path $exe)) { Rename-Item $old $exe }
}
if (-not $ok) { throw 'build failed (see above)' }
Write-Host "  built $exe"
