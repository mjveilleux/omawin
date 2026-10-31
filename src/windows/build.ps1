# Builds omawin-windows (Phase 8's fixes in a terminal: who-has, health, startup, top, hosts,
# connections, wifi share, clip) into ~/.local/bin as one exe. Needs the .NET 9 SDK (or a release's
# exe is downloaded instead: bin/install-binaries.ps1).
#
#   .\build.ps1                                        what install.ps1 -LinkOnly does
#   .\build.ps1 -OutDir ..\..\scratch\bin -BuildOnly   build only, into a scratch folder (CI)
#
# CI (.github/workflows/release.yml) runs this same script for each architecture.
param(
    [string]$OutDir = (Join-Path $HOME '.local\bin'),
    [ValidateSet('x64', 'arm64')][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    # Build only: leave an exe that's in use where it is. For CI and scratch builds.
    [switch]$BuildOnly,
    # The release this build is from (omawin-windows --version); CI passes the tag.
    [string]$Release
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$exe = Join-Path $OutDir 'omawin-windows.exe'
$old = $null
if (-not $BuildOnly) {
    # It only runs while a command does, but one may be open in a terminal (a question waiting):
    # a file in use can still be renamed, so the old one goes aside rather than failing the build.
    Get-ChildItem $OutDir -Filter 'omawin-windows.exe.old-*' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    if (Test-Path $exe) {
        $old = Join-Path $OutDir "omawin-windows.exe.old-$(Get-Date -Format yyyyMMddHHmmss)"
        Rename-Item $exe $old
    }
}
$publish = @('-c', 'Release', '-o', $OutDir, '-nologo', '-v', 'q', '-p:DebugType=None', "-p:RuntimeIdentifier=win-$Arch")
if ($Release) { $publish += "-p:OmawinRelease=$Release" }
$ok = $false
try {
    # From the project folder, so the repo's global.json (the pinned SDK) applies.
    Push-Location $PSScriptRoot
    try { dotnet publish "$PSScriptRoot\Windows.csproj" @publish }
    finally { Pop-Location }
    $ok = $LASTEXITCODE -eq 0 -and (Test-Path $exe)
} finally {
    # A failed build puts the old exe back, so the commands keep working.
    if (-not $ok -and $old -and -not (Test-Path $exe)) { Rename-Item $old $exe }
}
if (-not $ok) { throw 'build failed (see above)' }
Write-Host "  built $exe"
