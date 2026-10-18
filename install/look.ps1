# install.ps1 step: the theme, a clean desktop, no ads, the lock screen, the auto-hiding taskbar.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'Windows look'
# The theme (themes/set-theme.ps1) owns dark/light mode and the wallpaper: Starship, on
# its first background (Omarchy's starbase), until another is picked (system menu > Style > Theme).
if (Test-Path "$env:LOCALAPPDATA\launcher\theme") { Write-Note "ok  theme $(Get-Content "$env:LOCALAPPDATA\launcher\theme")"; Add-InstallItem 'Desktop' 'theme' 'theme' 'have' }
else { Invoke-InstallItem 'Desktop' 'theme' { & "$Repo\themes\set-theme.ps1" starship } }
# A clean desktop: no Recycle Bin icon (it's still in File Explorer). Files belong in
# Documents; the desktop is just the wallpaper.
$icons = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
if (-not (Test-Path $icons)) { New-Item $icons -Force | Out-Null }
Set-ItemProperty $icons -Name '{645FF040-5081-101B-9F08-00AA002F954E}' -Value 1 -Type DWord
# The lock screen rotates through the theme's backgrounds: a task for this user (no admin)
# runs themes/set-lockscreen.ps1 -Next on every unlock, headless (no console flash).
try {
    $user = "$env:USERDOMAIN\$env:USERNAME"
    $class = Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler -ClassName MSFT_TaskSessionStateChangeTrigger
    $unlock = New-CimInstance -CimClass $class -ClientOnly -Property @{ StateChange = 8; UserId = $user } # on unlock
    $action = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Repo\themes\set-lockscreen.ps1`" -Next"
    $taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    $null = Register-ScheduledTask -TaskName "omawin lock screen ($([Environment]::UserName))" -Trigger $unlock -Action $action -Settings $taskSettings -Principal $principal -Force `
        -Description "Rotates the lock screen through the theme's backgrounds on unlock (omawin/themes/set-lockscreen.ps1)."
    Write-Note "ok  lock screen rotates on unlock (task 'omawin lock screen ($([Environment]::UserName))')"
    Add-InstallItem 'Desktop' 'lock screen' 'lock screen' 'have'
} catch { Write-Warn "lock screen rotation task not made: $_"; Add-InstallItem 'Desktop' 'lock screen' 'lock screen' 'fail' }
# No taskbar, like Omarchy: it auto-hides (the Zebar bar has workspaces, apps and the
# tray); pushing the mouse to the bottom edge or pressing Win still brings it up. Set
# through the app-bar API, so it applies right away and is saved like the Settings toggle.
Add-Type -Namespace Dotfiles -Name AppBar -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public int left, top, right, bottom; public IntPtr lParam; }
[DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
'@
$bar = New-Object Dotfiles.AppBar+APPBARDATA
$bar.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($bar)
if (([uint64][Dotfiles.AppBar]::SHAppBarMessage(4, [ref]$bar)) -band 1) { Write-Note 'ok  taskbar auto-hides'; Add-InstallItem 'Desktop' 'taskbar' 'taskbar hides' 'have' } # ABM_GETSTATE
else {
    $bar.lParam = [IntPtr]1 # ABS_AUTOHIDE
    $null = [Dotfiles.AppBar]::SHAppBarMessage(10, [ref]$bar) # ABM_SETSTATE
    Write-Note 'taskbar set to auto-hide'
    Add-InstallItem 'Desktop' 'taskbar' 'taskbar hides' 'done'
}
