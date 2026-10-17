<#
.SYNOPSIS
    Sets the lock screen (and so the sign-in screen) picture to an image: the wallpaper, so
    logging back in shows the theme's background too. set-theme.ps1 runs it after every
    wallpaper change.

.DESCRIPTION
    Runs in Windows PowerShell 5.1 (powershell.exe): Windows' LockScreen API is WinRT, which
    PowerShell 7 can't load. No admin needed. The image is first made a JPEG (the lock screen
    doesn't take WebP, which most Omarchy backgrounds are), kept in
    %LOCALAPPDATA%\launcher\lockscreen\ under the image's own name so each is converted once.

        powershell -NoProfile -File set-lockscreen.ps1 <image>
        powershell -NoProfile -File set-lockscreen.ps1 -Next

    -Next rotates: the current theme's next background after the one on the lock screen now
    (downloaded if it isn't yet). A scheduled task ("dotfiles lock screen", install.ps1 makes
    it) runs that each time you unlock, so every lock shows another of the theme's pictures.
#>
param([string]$Image, [switch]$Next)
$ErrorActionPreference = 'Stop'

$state = Join-Path $env:LOCALAPPDATA 'launcher'
$dir = Join-Path $state 'lockscreen'
New-Item -ItemType Directory -Force $dir | Out-Null

if ($Next) {
    $name = if (Test-Path "$state\theme") { (Get-Content "$state\theme" -Raw).Trim() } else { 'gruvbox' }
    $theme = Get-Content (Join-Path $PSScriptRoot "$name.json") -Raw | ConvertFrom-Json
    $backgrounds = @($theme.backgrounds)
    if (-not $backgrounds) { return }
    $last = if (Test-Path "$state\lockscreen-background") { (Get-Content "$state\lockscreen-background" -Raw).Trim() } else { '' }
    $source = $backgrounds[([array]::IndexOf($backgrounds, $last) + 1) % $backgrounds.Count]
    if ($source -match '^https?://') {
        $Image = Join-Path $state "backgrounds\$name\$(Split-Path $source -Leaf)"
        if (-not (Test-Path $Image)) {
            New-Item -ItemType Directory -Force (Split-Path $Image) | Out-Null
            Invoke-WebRequest $source -OutFile "$Image.part" -UseBasicParsing
            Move-Item "$Image.part" $Image -Force
        }
    } else { $Image = Join-Path (Split-Path $PSScriptRoot) $source }
    Set-Content "$state\lockscreen-background" $source
} elseif (-not $Image) { throw 'set-lockscreen.ps1 <image>, or -Next' }
$jpeg = Join-Path $dir ([IO.Path]::GetFileNameWithoutExtension($Image) + '.jpg')
if (-not (Test-Path $jpeg)) {
    # WPF decodes with the Windows Imaging Component, which reads WebP on Windows 11.
    Add-Type -AssemblyName PresentationCore
    $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Resolve-Path $Image).Path,
        [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $encoder = New-Object Windows.Media.Imaging.JpegBitmapEncoder
    $encoder.QualityLevel = 92
    $encoder.Frames.Add($decoder.Frames[0])
    $stream = [IO.File]::Create("$jpeg.part")
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Move-Item "$jpeg.part" $jpeg -Force
}

# WinRT from PowerShell: its async calls are awaited through .NET tasks.
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
$forOperation = ($asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$forAction = ($asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.System.UserProfile.LockScreen, Windows.System.UserProfile, ContentType = WindowsRuntime]

$task = $forOperation.MakeGenericMethod([Windows.Storage.StorageFile]).Invoke($null, @([Windows.Storage.StorageFile]::GetFileFromPathAsync($jpeg)))
$file = $task.GetAwaiter().GetResult()
$null = $forAction.Invoke($null, @([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($file))).GetAwaiter().GetResult()
Write-Host "  lock screen $(Split-Path $jpeg -Leaf)"
