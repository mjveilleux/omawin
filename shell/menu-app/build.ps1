# Builds the native system menu and launcher (Win+Alt+Space, Win+Space) and starts it in the
# background. Needs the .NET 9 SDK (install.ps1 installs it, or a release's ZIP is used instead:
# bin/install-binaries.ps1). Safe to run again: a running copy is stopped first, since Windows
# won't replace its files while it runs.
#
#   .\build.ps1                                       what install.ps1 -LinkOnly does
#   .\build.ps1 -OutDir ..\..\scratch\menu -BuildOnly     build only, touch no running app (CI)
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
    [string]$Release,
    # One native program (Native AOT): up in a fraction of the time, which counts at sign-in.
    # It's the build wherever Visual Studio's C++ tools are (its linker needs them); -Aot insists
    # on it (and fails without them), -Jit asks for the ordinary .NET build.
    [switch]$Aot,
    [switch]$Jit
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

# Native when the C++ tools for this architecture are here (vswhere says; the linker needs
# vswhere on the PATH too).
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$component = if ($Arch -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
$cpp = (Test-Path "$installer\vswhere.exe") -and [bool](& "$installer\vswhere.exe" -products * -requires $component -property installationPath 2>$null)
# -Aot means the native build or nothing: a silent JIT build would ship 0.7 s slower to start
# (CI passes it, so a runner image without the C++ tools fails the release instead).
if ($Aot -and $Jit) { throw '-Aot and -Jit: pick one' }
if ($Aot -and -not $cpp) { throw "-Aot needs Visual Studio's C++ tools ($component; vswhere found none): the native build can't link" }
$native = -not $Jit -and ($Aot -or $cpp)
if ($native) { $env:PATH = "$installer;$env:PATH" }
# The two builds leave different files: switching from one to the other starts the folder afresh.
$kind = if ($native) { 'native' } else { 'jit' }
$marker = Join-Path $out 'build-kind.txt'
if ((Test-Path "$out\SystemMenu.exe") -and -not ((Test-Path $marker) -and (Get-Content $marker -Raw).Trim() -eq $kind)) {
    Get-ChildItem $out -Force | Remove-Item -Recurse -Force
}

$publish = @('-c', 'Release', '-o', $out, '-nologo', '-v', 'q')
if ($Arch -ne 'x64') { $publish += @("-p:RuntimeIdentifier=win-$Arch", "-p:Platform=$Arch", "-p:Platforms=$Arch") }
if ($Release) { $publish += "-p:OmawinRelease=$Release" }
if ($native) { $publish += '-p:OmawinAot=true' }
# From the project folder, so the repo's global.json (the pinned SDK) applies.
Push-Location $PSScriptRoot
try { dotnet publish "$PSScriptRoot\SystemMenu.csproj" @publish }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'build failed (see above)' }
# The native build's debugging symbols (~90 MB) aren't needed to run it.
if ($native) { Remove-Item "$out\SystemMenu.pdb" -ErrorAction SilentlyContinue }
Set-Content $marker $kind -Encoding ASCII
if ($BuildOnly) { Write-Host "  built $out\SystemMenu.exe" }
else {
    Start-Process "$out\SystemMenu.exe" -ArgumentList '--start-hidden'
    Write-Host "  built $out\SystemMenu.exe (running in the background)"
}
