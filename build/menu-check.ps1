<#
.SYNOPSIS
    Builds the menu app and walks through the launcher's Packages page on a Windows desktop,
    recording the screen: for .github/workflows/menu-check.yml, on a CI runner, never this PC.

.DESCRIPTION
    Starts the built SystemMenu.exe off this checkout (DOTFILES), then drives it with its own
    walkthrough switches (--launcher packages, --type, --key), a screenshot after each step in
    <Out>\shots, and the whole screen recorded to <Out>\frames as JPEGs named by milliseconds
    since the start (events.txt says when each step began), for a video. The menu app's log
    is copied to <Out>\system-menu.log.
#>
param([string]$Out = 'out\menu-check')
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Out = (New-Item -ItemType Directory -Force (Join-Path $Repo $Out)).FullName
New-Item -ItemType Directory -Force "$Out\frames", "$Out\shots" | Out-Null
$env:DOTFILES = $Repo

# winget's catalog on the PC (bin/pkg-index.ps1 reads it), as on a PC that has run winget once.
try { winget source update --disable-interactivity *> $null } catch { Write-Host "winget source update: $_" }

& "$Repo\menu-app\build.ps1" -OutDir "$Out\app" -BuildOnly
$exe = "$Out\app\SystemMenu.exe"

$start = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
function Write-Event([string]$Text) {
    $ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - $start
    "$ms`t$Text" | Add-Content "$Out\events.txt"
    Write-Host "[$ms ms] $Text"
}

# The recorder runs in its own process so captures keep their pace while this one waits.
$recorder = Join-Path $Out 'record.ps1'
@'
param([string]$Dir, [long]$Start, [int]$Seconds)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$params = New-Object System.Drawing.Imaging.EncoderParameters 1
$params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 75L
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$end = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
while ([DateTimeOffset]::UtcNow -lt $end) {
    $ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - $Start
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $Dir ('{0:D6}.jpg' -f $ms)), $codec, $params)
    Start-Sleep -Milliseconds 40
}
'@ | Set-Content $recorder -Encoding utf8

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$shot = 0
function Save-Shot([string]$Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $script:shot++
    $bitmap.Save((Join-Path "$Out\shots" ('{0:D2}-{1}.png' -f $script:shot, $Name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

# One step: hand the running app a request (a second copy passes it on and exits), wait, look.
function Step([string]$Text, [string]$Shot, [double]$Seconds, [string[]]$Arguments) {
    Write-Event $Text
    Start-Process $exe -ArgumentList $Arguments -Wait
    Start-Sleep -Milliseconds ([int]($Seconds * 1000))
    Save-Shot $Shot
}

Start-Process $exe -ArgumentList '--start-hidden'
Start-Sleep -Seconds 6
$rec = Start-Process pwsh -ArgumentList '-NoProfile', '-File', "`"$recorder`"", "`"$Out\frames`"", $start, 60 -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1

Step 'open Packages (winget)' 'winget' 8 '--launcher', 'packages'
Step 'type: vscode' 'winget-vscode' 3 '--type', 'vscode'
Step 'down: the next one, its details' 'winget-details' 4 '--key', 'down'
Step 'ctrl+space: pick it' 'winget-picked' 2 '--key', 'pick'
Step 'tab: Chocolatey' 'choco' 2 '--key', 'tab'
Step 'type: git' 'choco-git' 6 '--type', 'git'
Step 'tab: Scoop' 'scoop' 3 '--key', 'tab'
Step 'type: 7zip' 'scoop-7zip' 3 '--type', '7zip'
Write-Event 'done'

$rec | Stop-Process -ErrorAction SilentlyContinue
Start-Process $exe -ArgumentList '--quit' -Wait
Copy-Item "$env:LOCALAPPDATA\launcher\system-menu.log" $Out -ErrorAction SilentlyContinue
