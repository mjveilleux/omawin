<#
.SYNOPSIS
    A dry run of the installer's look: pretend steps and packages, with pauses. Installs nothing.

.DESCRIPTION
    Walks install/ui.ps1 and install/board.ps1 (what install.ps1 shows) through a whole install,
    so the look can be seen and tuned without touching the PC: the questions, the one admin
    prompt (nothing is asked; it's approved after a pause), the package grid with the admin
    step's packages alongside, every step of install/steps.txt, the finish screen. Logs to
    %TEMP%\omawin-install-demo.log, and keeps its step times apart from the real ones.

        install\demo-ui.ps1                  the screen (the line-by-line face in a small console)
        install\demo-ui.ps1 -Fail            a step that fails, and the screen that follows
        install\demo-ui.ps1 -Speed 2         twice as fast
        $env:OMAWIN_BOARD = 0; install\demo-ui.ps1    the line-by-line face
        $env:OMAWIN_PLAIN = 1; install\demo-ui.ps1    the plain face (redirected output, logs)
#>
param(
    [switch]$Fail,
    [double]$Speed = 1
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
. "$PSScriptRoot\ui.ps1"
$UI.Log = Join-Path $env:TEMP 'omawin-install-demo.log'
$UI.Times = Join-Path $env:TEMP 'omawin-install-demo-times.txt'
Remove-Item $UI.Log -ErrorAction SilentlyContinue
Initialize-InstallUI 'demo-ui.ps1'

function Wait-Demo([double]$Seconds) { Start-Sleep -Milliseconds ([int](1000 * $Seconds / $Speed)) }
function Get-PackageName([string]$Id) { $p = $Id -split '\.'; if ($p.Count -lt 2) { return $Id }; $n = ($p[1..($p.Count - 1)] -join '.') -replace '\.(MSVC|LTS)$'; if ($n -in 'cli', 'app') { $p[0] } else { $n } }
# Each step's lines, said one after another.
# ">heading", "!warning", "+Group|name|state" (an item: run for a moment, then that state), else a note.
function Invoke-DemoLines([string[]]$Lines) {
    foreach ($l in $Lines) {
        if ($l.StartsWith('>')) { Write-Step $l.Substring(1) }
        elseif ($l.StartsWith('!')) { Write-Warn $l.Substring(1) }
        elseif ($l.StartsWith('+')) {
            $f = $l.Substring(1) -split '\|'
            if ($f[2] -ne 'have') { Add-InstallItem $f[0] $f[1] $f[1] 'run'; Wait-Demo 0.6 }
            Add-InstallItem $f[0] $f[1] $f[1] $f[2]
            Wait-Demo 0.15
            continue
        }
        else { Write-Note $l }
        Wait-Demo 0.4
    }
}

$steps = foreach ($line in Get-Content "$PSScriptRoot\steps.txt") {
    if ($line -match '^\s*(#|$)') { continue }
    $f = @($line -split '\|' | ForEach-Object { $_.Trim() })
    [pscustomobject]@{ Name = $f[0]; Summary = $f[1]; Seconds = [double]$f[2] }
}
$UI.SaveTimes = $true
foreach ($s in $steps) { Add-InstallStep $s.Name $s.Summary $(if ($s.Name -eq 'software') { 30 } else { 4 }) }
Add-InstallStep 'migrations' 'clear away what older versions left' 2
Start-InstallBoard 'demo' 'nothing is installed'
Write-InstallBanner 'demo' 'nothing is installed'
Write-InstallPlan

try {
    Send-Board 'enter' 'before it starts'
    Write-Step 'before it starts'
    Invoke-DemoLines @('presets: none (omawin preset list)', 'ok  git user.name You', 'what needs admin ...')

    Add-InstallStep 'admin' 'what every account shares, after one prompt' -First
    Show-AdminRequest @('9 programs for the whole PC|PowerShell, Git, gh, GlazeWM, Zebar ...', "omawin's GlazeWM and Zebar|signed on this PC")
    Wait-Demo 3
    Close-AdminRequest $true
    $adminStarted = Get-Date

    $lines = @{
        'no-ads'  = @('ok  Windows suggestions and promos off', '+Windows|ads and suggestions off|done', 'ok  Copilot off the taskbar', '+Windows|Copilot off the taskbar|done')
        'shell'   = @('>PowerShell profile', 'wrote Documents\PowerShell\profile.ps1', '+Settings|profile.ps1|done', '+Settings|Microsoft.PowerShell_profile.ps1|done', '>Windows Terminal', 'seeded its settings', '+Settings|Windows Terminal|done', '>noctty', '+Settings|config.ghostty|done', '>starship', 'ok  STARSHIP_CONFIG', '+Settings|starship prompt|done', '+Settings|fastfetch|done', '+Settings|herdr config|done', '>git', '+Settings|git config|done', '!no git email for this account yet: omawin account new sets it', '>Git Bash', '+Settings|.bashrc|done', '+Settings|.bash_profile|done')
        'desktop' = @('>GlazeWM + Zebar', 'ok  ~/.glzr/glazewm/config.yaml rendered from glazewm/config.yaml', '+Settings|GlazeWM config|done', '+Settings|zebar|done', '+Desktop|GlazeWM at sign-in|done', "GlazeWM fork v3.10.1-omawin.8 (signed on this PC)", "+Desktop|omawin's GlazeWM|done", 'Zebar fork v3.3.1-omawin.2', "+Desktop|omawin's Zebar|done", "ports for this account: GlazeWM 24002, Zebar 24003", "+Desktop|this account's ports|done", '>Handy (dictation on Win+H)', 'configured Handy', '+Desktop|dictation (Win+H)|done')
        'helpers' = @('>omawin and the helpers', 'omawin.exe and the bar helpers from release v0.1.5', '+Tools|omawin.exe|done', '+Tools|btop|done', '>noctty', '+Tools|noctty|done', '>Win+Alt+Space system menu', 'from release v0.1.5', '+Tools|system menu|done', '>Music player (the bar)', 'from release v0.1.5', '+Tools|music player|done')
        'look'    = @('>Windows look', 'theme: starship', '+Desktop|theme|done', 'ok  lock screen rotates on unlock', '+Desktop|lock screen|done', 'taskbar set to auto-hide', '+Desktop|taskbar hides|done')
        'other'   = @('>WSL', 'wrote ~/.wslconfig', '+Settings|.wslconfig|done', '>Claude Code', 'ok  rules: 2 folder(s) imported', 'ok  settings merged', '+Settings|Claude Code|done')
    }
    foreach ($s in $steps) {
        Enter-InstallStep $s.Name
        if ($s.Name -eq 'software') {
            Write-Step 'winget packages'
            $all = @(@('Microsoft.PowerShell', 9, 1), @('Microsoft.WindowsTerminal', 4, 0), @('Git.Git', 0, 1), @('GitHub.cli', 5, 1), @('Starship.Starship', 3, 0),
                @('ajeetdsouza.zoxide', 2, 0), @('junegunn.fzf', 2, 0), @('sharkdp.fd', 2, 0), @('BurntSushi.ripgrep.MSVC', 3, 0), @('eza-community.eza', 2, 0),
                @('sharkdp.bat', 3, 0), @('jqlang.jq', 1, 0), @('JesseDuffield.lazygit', 3, 0), @('Fastfetch-cli.Fastfetch', 3, 0), @('glzr-io.glazewm', 5, 1),
                @('glzr-io.zebar', 4, 1), @('aristocratos.btop4win', 3, 0), @('Oven-sh.Bun', 4, 0), @('Neovim.Neovim', 5, 1), @('Neovide.Neovide', 4, 1),
                @('tree-sitter.tree-sitter-cli', 2, 0), @('zig.zig', 7, 0), @('OpenJS.NodeJS.LTS', 5, 1), @('astral-sh.uv', 2, 0), @('Microsoft.DotNet.DesktopRuntime.9', 6, 1))
            $queue = New-Object Collections.ArrayList
            foreach ($p in $all) {
                if ($p[1] -eq 0) { Set-BoardPackage $p[0] 'have'; Write-Ok $p[0]; continue }
                Set-BoardPackage $p[0] 'wait' -Admin:([bool]$p[2])
                [void]$queue.Add(@{ Id = $p[0]; Secs = $p[1] / $Speed; Admin = [bool]$p[2]; State = 'wait' })
            }
            # Two lanes, as in install.ps1: this account's four at once, the admin step's one at a time.
            while (@($queue | Where-Object State -ne 'done').Count) {
                foreach ($lane in @(@{ Admin = $false; Width = 4 }, @{ Admin = $true; Width = 1 })) {
                    $mine = @($queue | Where-Object { $_.Admin -eq $lane.Admin })
                    foreach ($q in @($mine | Where-Object State -eq 'run')) {
                        if (((Get-Date) - $q.Start).TotalSeconds -ge $q.Secs) { $q.State = 'done'; Set-BoardPackage $q.Id 'done'; Write-Done $q.Id $q.Secs }
                    }
                    $free = $lane.Width - @($mine | Where-Object State -eq 'run').Count
                    foreach ($q in @($mine | Where-Object State -eq 'wait' | Select-Object -First ([Math]::Max(0, $free)))) { $q.State = 'run'; $q.Start = Get-Date; Set-BoardPackage $q.Id 'run' }
                }
                $left = @($queue | Where-Object { $_.Admin -and $_.State -ne 'done' }).Count
                Set-BoardPackageNote $(if ($left) { "$left left in the admin step" })
                Start-Sleep -Milliseconds 100
            }
            Set-BoardPackageNote $null
            Write-OkCount
            Invoke-DemoLines @('>other tools', '+Tools|herdr|done', '>JetBrainsMono Nerd Font', '+Tools|JetBrainsMono Nerd Font|done', 'installed (restart Windows Terminal to pick it up)', '>Neovim and Neovide', 'linked ~/AppData/Local/nvim -> ~/omawin/nvim', '+Settings|nvim|done', '+Settings|neovide|done', '+Settings|.Rprofile|done')
        } else {
            Invoke-DemoLines $lines[$s.Name]
            if ($Fail -and $s.Name -eq 'desktop') {
                Exit-InstallStep 'the GlazeWM sign-in task could not be created: Access is denied.'
                Write-InstallSummary
                return
            }
        }
        Exit-InstallStep
    }
    Enter-InstallStep 'migrations'
    Write-Step 'migrations'
    Write-Note 'migrations: nothing new'
    Wait-Demo 0.4
    Exit-InstallStep
    $a = Get-InstallStep 'admin'
    $a.State = 'done'; $a.Seconds = ((Get-Date) - $adminStarted).TotalSeconds
    Write-InstallSummary
} finally {
    Close-InstallUI
}
