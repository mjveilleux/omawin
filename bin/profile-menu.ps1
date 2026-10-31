<#
.SYNOPSIS
    omawin profile menu: where the time goes from a menu key to the menu on screen, and from a
    key typed in the launcher to its list redrawn.

.DESCRIPTION
    Opens each surface -Runs times the way its key does (GlazeWM runs the key's command), and
    reads the marks every step writes to %LOCALAPPDATA%\launcher\trace.log while trace-on exists.
    Every mark is a QueryPerformanceCounter value, the one clock all processes share:

      driver   this script: the key (it asks GlazeWM's CLI to run the key's command)
      omawin   omawin.exe (bin/omawin.cs): created, running, wrote the request, signalled
      menu     the menu app: woke on the signal (or got GlazeWM's emit), handled it, the window
               shown, its next frame composed; typed, the list rebuilt, the list's next frame

    GlazeWM ignores simulated keys (AGENTS.md), so "the key" is GlazeWM being asked to run the
    key's command: that includes starting glazewm.exe's CLI, a little more than a real key costs.
    -Hand waits for you to press the keys yourself instead; those runs start at the first sign of
    the key (omawin.exe created, or the emit), since nothing can mark the key itself.

        omawin profile menu                      launcher, system menu, file finder; 10 runs each
        omawin profile menu -Surface launcher -Runs 20
        omawin profile menu -Typing              the launcher, typing "visual studio code"
        omawin profile menu -Hand -Surface launcher   3 runs, pressed by you

    -Direct runs omawin.exe <surface> itself rather than through GlazeWM, for a PC or a CI runner
    with no GlazeWM (.github/workflows/profile.yml): everything but GlazeWM's own share. -Json
    prints the report and its headline numbers (MenuProfile.Numbers) as JSON, for CI to judge.

    The numbers are worked out by src/cli/Profile/MenuProfile.cs (tested in test/cli.Tests),
    compiled here with Add-Type until the C# omawin ships (roadmap 2.11).
#>
param(
    [int]$Runs = 10,
    [ValidateSet('all', 'launcher', 'menu', 'files')][string]$Surface = 'all',
    [switch]$Typing,
    [switch]$Hand,
    [switch]$Direct,
    [switch]$Json,
    [string]$Word = 'visual studio code'
)
if ($Hand -and $Direct) { throw '-Hand and -Direct: pick one (you press the keys, or omawin.exe is run)' }
$ErrorActionPreference = 'Stop'
$state = Join-Path $env:LOCALAPPDATA 'launcher'
$traceOn = Join-Path $state 'trace-on'
$traceLog = Join-Path $state 'trace.log'
$freq = [Diagnostics.Stopwatch]::Frequency

function Mark([string]$Label) { [IO.File]::AppendAllText($traceLog, "driver $Label`t$([Diagnostics.Stopwatch]::GetTimestamp())`n") }
function Count([string]$Label) {
    if (-not (Test-Path $traceLog)) { return 0 }
    @([IO.File]::ReadAllLines($traceLog) | Where-Object { $_.StartsWith("$Label`t") -or $_.StartsWith("$Label ") }).Count
}
function WaitFor([string]$Label, [int]$Before, [int]$Ms = 3000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $Ms) { if ((Count $Label) -gt $Before) { return $true }; Start-Sleep -Milliseconds 5 }
    $false
}
# A request straight to the running menu app, as omawin.exe's Send does (no process started):
# the typing runs and the hiding between runs, which aren't what's being measured.
function Send-Request([string]$Request) {
    [IO.File]::WriteAllText((Join-Path $state 'system-menu-request'), $Request)
    $signal = [Threading.EventWaitHandle]::OpenExisting('Local\dotfiles-system-menu-signal')
    try { $null = $signal.Set() } finally { $signal.Dispose() }
}
# The key's command, the way GlazeWM's binding runs it: an emit when the rendered config uses
# them (roadmap 1.2: the menu app hears it from GlazeWM, no process), else omawin.exe.
$rendered = Get-Content (Join-Path $HOME '.glzr\glazewm\config.yaml') -Raw -ErrorAction SilentlyContinue
$viaEmit = -not $Direct -and "$rendered" -match 'emit omawin '
# -Direct: omawin.exe from here, as GlazeWM's shell-exec would start it (the PATH's, else the
# install's in ~/.local/bin); it exits once it has signalled the menu app.
$omawinExe = if ($Direct) {
    $found = Get-Command omawin.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $found.Source } else { Join-Path $HOME '.local\bin\omawin.exe' }
}
function Press([string]$Command) {
    if ($Direct) { & $omawinExe $Command | Out-Null }
    elseif ($viaEmit) { glazewm command emit omawin $Command | Out-Null }
    else { glazewm command shell-exec --hide-window omawin.exe $Command | Out-Null }
}

if (-not (Get-Process SystemMenu -ErrorAction SilentlyContinue)) { throw 'the menu app is not running (omawin start)' }
if ($Direct) { if (-not (Test-Path $omawinExe)) { throw "omawin.exe isn't on the PATH or in ~/.local/bin (build/helpers.ps1 builds it)" } }
elseif (-not (Get-Command glazewm -ErrorAction SilentlyContinue) -and -not $Hand) { throw "glazewm's CLI isn't on the PATH: the keys can't be run the way GlazeWM runs them (or -Direct: omawin.exe without it)" }
New-Item -ItemType Directory -Force $state | Out-Null
Set-Content $traceOn ''
Remove-Item $traceLog -ErrorAction SilentlyContinue
$surfaces = if ($Surface -eq 'all') { 'launcher', 'menu', 'files' } else { , $Surface }
$keys = @{ launcher = 'Win+Space'; menu = 'Win+Alt+Space'; files = 'Win+F' }
try {
    Send-Request '--hide'
    Start-Sleep -Milliseconds 300
    if ($Typing) {
        Press 'launcher'
        if (-not (WaitFor 'menu painted' 0)) { throw 'the launcher never showed (no first frame within 3 s)' }
        Start-Sleep -Milliseconds 400
        for ($run = 1; $run -le $Runs; $run++) {
            Send-Request '--type'; Start-Sleep -Milliseconds 250 # back to an empty search
            for ($i = 1; $i -le $Word.Length; $i++) {
                $typed = $Word.Substring(0, $i)
                # A trailing space searches the same as without it: the box changes, the list doesn't.
                if ($typed.EndsWith(' ')) { continue }
                $before = Count 'menu list-painted'
                Mark "type $typed"
                Mark 'key'
                Send-Request "--type $typed"
                if (-not (WaitFor 'menu list-painted' $before)) { Write-Warning "run ${run}, `"$typed`": the list wasn't redrawn within 3 s" }
                Start-Sleep -Milliseconds 60 # a fast typist
            }
        }
        Send-Request '--hide'
    }
    else {
        foreach ($s in $surfaces) {
            $count = if ($Hand) { [math]::Min($Runs, 3) } else { $Runs }
            Mark "surface $s"
            for ($run = 1; $run -le $count; $run++) {
                $before = Count 'menu painted'
                if ($Hand) {
                    Mark 'ready'
                    Write-Host "Press $($keys[$s]) now ($s, $run of $count)"
                    if (-not (WaitFor 'menu painted' $before 30000)) { Write-Warning "${s}: nothing within 30 s"; continue }
                }
                else {
                    Mark 'key'
                    Press $s
                    if (-not (WaitFor 'menu painted' $before)) { Write-Warning "$s run ${run}: no first frame within 3 s" }
                }
                Start-Sleep -Milliseconds 300
                Send-Request '--hide'
                Start-Sleep -Milliseconds 300
            }
        }
    }
} finally { Remove-Item $traceOn -ErrorAction SilentlyContinue }

# --- the numbers -------------------------------------------------------------------------------
if (-not ('Omawin.Cli.Profile.MenuProfile' -as [type])) {
    Add-Type -Path (Join-Path (Split-Path $PSScriptRoot) 'src\cli\Profile\MenuProfile.cs')
}
$lines = [string[]]@(if (Test-Path $traceLog) { [IO.File]::ReadAllLines($traceLog) })
$report = [Omawin.Cli.Profile.MenuProfile]::Report($lines, $freq, [bool]$Typing)
$scale = [math]::Round((Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name AppliedDPI -ErrorAction SilentlyContinue).AppliedDPI / 96 * 100)
$how = if ($Direct) { 'Keys run omawin.exe directly (no GlazeWM).' } elseif ($viaEmit) { 'Keys emit to the menu app (no omawin.exe).' } else { 'Keys run omawin.exe.' }
if ($Json) {
    [pscustomobject]@{
        Report  = $report
        Numbers = [Omawin.Cli.Profile.MenuProfile]::Numbers($lines, $freq, [bool]$Typing)
        Scale   = if ($scale) { $scale } else { $null }
        Keys    = $how
    } | ConvertTo-Json -Depth 4
    return
}
Write-Host $report
if ($scale) { Write-Host "Display scale $scale%. $how" }
