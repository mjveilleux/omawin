# fff's C library (file search in the launcher, FileSearch.cs): a pinned release, checked against
# the SHA-256 published with it, into lib\fff_c.dll (gitignored), which the build copies next to
# the exe. One place for the pin and the download: menu-app\build.ps1 (this PC) and CI
# (.github/workflows/release.yml) both run this.
#
#   .\fetch-fff.ps1            x64 (this PC's build)
#   .\fetch-fff.ps1 -Arch arm64
param([ValidateSet('x64', 'arm64')][string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'

$fffVersion = 'v0.11.0'
# x86_64 and aarch64 builds of the same release; both hashes from the .sha256 files published with it.
$fffSha256 = @{
    x64   = '549bc236d5b104c4364bd8bdd46a4abc8c0aa7e6409ed287263bbc42f94c56c8'
    arm64 = '7a3846701778a0ebaacc2b9ad5227e84cefa582c8feecd013e4d7dae9700a269'
}
$has = if ($Arch -eq 'arm64') { 'aarch64' } else { 'x86_64' }
$fff = Join-Path $PSScriptRoot 'lib\fff_c.dll'
$want = $fffSha256[$Arch]
if ((Test-Path $fff) -and (Get-FileHash $fff -Algorithm SHA256).Hash -eq $want) { return }  # the one we want

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
New-Item -ItemType Directory -Force (Split-Path $fff) | Out-Null
$url = "https://github.com/dmtrKovalenko/fff/releases/download/$fffVersion/c-lib-$has-pc-windows-msvc.dll"
Invoke-WebRequest $url -OutFile "$fff.part" -UseBasicParsing
if ((Get-FileHash "$fff.part" -Algorithm SHA256).Hash -ne $want) {
    Remove-Item "$fff.part"
    throw "fff_c.dll from $url doesn't match its pinned SHA-256"
}
Move-Item "$fff.part" $fff -Force
Write-Host "  fff $fffVersion ($Arch) -> $fff"
