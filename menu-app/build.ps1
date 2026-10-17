# Builds the native system menu and launcher (Win+Alt+Space, Win+Space) and starts it in the
# background. Needs the .NET 9 SDK (install.ps1 installs it, or a release's ZIP is used instead:
# bin/install-binaries.ps1). Safe to run again: a running copy is stopped first, since Windows
# won't replace its files while it runs.
#
#   .\build.ps1                                       what install.ps1 -LinkOnly does
#   .\build.ps1 -OutDir ..\scratch\menu -BuildOnly     build only, touch no running app (CI)
#
# CI (.github/workflows/release.yml) runs this same script for each architecture, so a release is
# this build: -Arch arm64 on an ARM64 runner, with -p:RuntimeIdentifier=win-arm64 underneath.
param(
    [string]$OutDir = (Join-Path $env:LOCALAPPDATA 'system-menu'),
    [ValidateSet('x64', 'arm64')][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    # Build only: don't stop a running copy, don't wait for locked files, don't start one. For CI
    # and for a build into a scratch folder, which must leave the running desktop alone.
    [switch]$BuildOnly,
    # The release this build is from (SystemMenu --version); CI passes the tag.
    [string]$Release
)
$ErrorActionPreference = 'Stop'

# Absolute before anything changes directory (the dotnet calls run from the project folder).
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }
$out = $OutDir
New-Item -ItemType Directory -Force $out | Out-Null

# fff's C library (file search in the launcher): a pinned release, checked against its SHA-256,
# kept in lib\ (gitignored) and copied next to the exe by the build below.
& "$PSScriptRoot\fetch-fff.ps1" -Arch $Arch

if (-not $BuildOnly) {
    # Stop the running copy and wait for its files: Windows won't replace them while it runs.
    & "$PSScriptRoot\stop-menu.ps1" -Dir $out
}

$publish = @('-c', 'Release', '-o', $out, '-nologo', '-v', 'q')
if ($Arch -ne 'x64') { $publish += @("-p:RuntimeIdentifier=win-$Arch", "-p:Platform=$Arch", "-p:Platforms=$Arch") }
if ($Release) { $publish += "-p:OmawinRelease=$Release" }
# From the project folder, so the repo's global.json (the pinned SDK) applies.
Push-Location $PSScriptRoot
try { dotnet publish "$PSScriptRoot\SystemMenu.csproj" @publish }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'build failed (see above)' }
if ($BuildOnly) { Write-Host "  built $out\SystemMenu.exe" }
else {
    Start-Process "$out\SystemMenu.exe" -ArgumentList '--start-hidden'
    Write-Host "  built $out\SystemMenu.exe (running in the background)"
}
