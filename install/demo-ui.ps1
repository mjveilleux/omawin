<#
.SYNOPSIS
    A dry run of the installer's look: fake steps, sleeps, a stand-in for winget. Installs nothing.

.DESCRIPTION
    Walks install/ui.ps1 (what install.ps1 prints) through a whole install, so the look can be
    seen and tuned without touching the PC. The "winget" here is ping, so the spinner runs the
    same code it runs for a real package. Logs to %TEMP%\omawin-install-demo.log, not the real log.

        install\demo-ui.ps1                  the fancy face
        install\demo-ui.ps1 -Fail            a step that fails, and the screen that follows
        $env:OMAWIN_PLAIN = 1; install\demo-ui.ps1    the plain face (redirected output, logs)
#>
param(
    [switch]$Fail,
    [double]$Speed = 1    # 2 = twice as fast
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\ui.ps1"
$UI.Log = Join-Path $env:TEMP 'omawin-install-demo.log'
Remove-Item $UI.Log -ErrorAction SilentlyContinue
Initialize-InstallUI 'demo-ui.ps1'

function Wait-Demo([double]$Seconds) { Start-Sleep -Milliseconds ([int](1000 * $Seconds / $Speed)) }

# A pretend package: ping -n N takes N-1 seconds, standing in for winget.
function Install-DemoPackage([string]$Id, [int]$Seconds, [string]$Of) {
    $ping = "$env:WINDIR\System32\PING.EXE"
    if ($UI.Fancy) {
        $result = Invoke-NativeStatus $ping @('-n', [Math]::Max(2, [int]($Seconds / $Speed) + 1), '127.0.0.1') "installing  $Of  $Id"
        Write-Done $Id $result.Seconds
    } else {
        Write-Note "installing $Id"
        Wait-Demo $Seconds
    }
}

$steps = [ordered]@{
    'no-ads'     = "Windows' ads and suggestions off"
    'software'   = 'winget packages, your presets, the Nerd Font'
    'neovim'     = 'Neovim and Neovide config'
    'shell'      = 'PowerShell profile, terminals, prompt, git'
    'desktop'    = 'GlazeWM, Zebar, dictation'
    'helpers'    = 'omawin.exe, the menu app, the music player'
    'look'       = 'the theme, the lock screen, the taskbar'
    'other'      = 'WSL settings, Claude Code'
    'migrations' = 'clear away what older versions left'
}
Write-InstallBanner (Get-InstallVersion (Split-Path $PSScriptRoot)) 'demo: nothing is installed'
foreach ($name in $steps.Keys) { Add-InstallStep $name $steps[$name] }
Write-InstallPlan
Wait-Demo 1.5

Enter-InstallStep 'no-ads'
Write-Step 'no ads'
Write-Note 'suggestions and tips off'
Write-Note 'Copilot off the taskbar'
Wait-Demo 0.6
Exit-InstallStep

Enter-InstallStep 'software'
Write-Step 'winget packages (core)'
$ids = 'Microsoft.PowerShell', 'Git.Git', 'Starship.Starship', 'junegunn.fzf', 'Neovim.Neovim', 'glzr-io.glazewm', 'glzr-io.zebar', 'Oven-sh.Bun'
$i = 0
foreach ($id in $ids) {
    $i++
    if ($id -in 'Neovim.Neovim', 'glzr-io.zebar') { Install-DemoPackage $id 3 "$i/$($ids.Count)" }
    else { Write-Ok $id; Wait-Demo 0.15 }
}
Write-OkCount
Write-Step 'presets'
Write-Note 'presets: none (omawin preset list)'
Write-Step 'other tools'
Write-Note 'ok  herdr'
Write-Step 'JetBrainsMono Nerd Font'
Write-Note 'ok  already installed'
Exit-InstallStep

Enter-InstallStep 'neovim'
Write-Step 'Neovim'
Write-Note 'ok  C:\Users\you\AppData\Local\nvim'
Wait-Demo 0.4
Exit-InstallStep

Enter-InstallStep 'shell'
foreach ($sub in 'PowerShell profile', 'Windows Terminal', 'starship', 'git') {
    Write-Step $sub
    Write-Note "ok  $sub"
    Wait-Demo 0.3
}
Write-Warn "Windows Terminal's settings couldn't be linked (it's running); close it, then: install.ps1 -LinkOnly"
Exit-InstallStep

Enter-InstallStep 'desktop'
Write-Step 'GlazeWM + Zebar'
Write-Note 'linked C:\Users\you\.glzr -> C:\Users\you\omawin\glazewm'
Wait-Demo 0.6
if ($Fail) {
    Exit-InstallStep 'the GlazeWM sign-in task could not be created: Access is denied.'
    Write-InstallSummary
    Close-InstallUI
    return
}
Exit-InstallStep

Enter-InstallStep 'helpers'
Write-Step 'omawin and the helpers'
Install-DemoPackage 'menu app (release v0.1.0)' 2 'helpers'
Exit-InstallStep

foreach ($name in 'look', 'other') {
    Enter-InstallStep $name
    Write-Note 'ok  nothing to change'
    Wait-Demo 0.4
    Exit-InstallStep
}

Enter-InstallStep 'migrations'
Write-Step 'migrations'
Write-Note 'migrations: nothing new'
Exit-InstallStep

Write-InstallSummary
Close-InstallUI
