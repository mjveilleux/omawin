# Builds the compiled omawin (src/cli, roadmap 2.11) into ~/.local/bin as omawin-cli.exe, beside
# omawin.exe (bin/omawin.cs), which hands it the builtins that moved here (omawin pkg). Needs the
# .NET 9 SDK (or a release's exe is downloaded instead: bin/install-binaries.ps1).
#
#   .\build.ps1                                           what install.ps1 -LinkOnly does
#   .\build.ps1 -OutDir ..\..\scratch\bin -Arch arm64     another folder, another architecture
#
# One native program (Native AOT) where Visual Studio's C++ tools are (its linker needs them),
# as the release is built; elsewhere one self-contained file (no .NET runtime needed either),
# a little slower to start. CI (.github/workflows/release.yml) runs this same script.
param(
    [string]$OutDir = (Join-Path $HOME '.local\bin'),
    [ValidateSet('x64', 'arm64')][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    # The release this build is from (omawin-cli --version); CI passes the tag.
    [string]$Release,
    [switch]$Aot
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).Path $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Native when the C++ tools are here (vswhere says; the linker needs vswhere on the PATH too).
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$cpp = (Test-Path "$installer\vswhere.exe") -and [bool](& "$installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null)
$native = $Aot -or $cpp
if ($native) { $env:PATH = "$installer;$env:PATH" }

$work = Join-Path ([IO.Path]::GetTempPath()) "omawin-cli-build-$PID"
$publish = @('-c', 'Release', '-r', "win-$Arch", '-o', $work, '-nologo', '-v', 'q', '-p:DebugType=None')
if (-not $native) { $publish += @('-p:PublishAot=false', '-p:SelfContained=true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true') }
if ($Release) { $publish += "-p:InformationalVersion=$Release" }
try {
    # From the project folder, so the repo's global.json (the pinned SDK) applies.
    Push-Location $PSScriptRoot
    try { dotnet publish "$PSScriptRoot\Omawin.Cli.csproj" @publish }
    finally { Pop-Location }
    $built = Join-Path $work 'omawin.exe'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $built)) { throw 'build failed (see above)' }
    # A running omawin-cli (an install in another terminal) locks its exe, but it can be renamed:
    # the old one is moved aside and finishes what it's doing.
    $dest = Join-Path $OutDir 'omawin-cli.exe'
    if (Test-Path $dest) {
        Remove-Item "$dest.old" -Force -ErrorAction SilentlyContinue
        Move-Item $dest "$dest.old" -Force
    }
    Copy-Item $built $dest
    Write-Host "  built $dest ($(if ($native) { 'native' } else { 'single file' }))"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
