<#
.SYNOPSIS
    omawin profile login: how long after you signed in each part of the desktop was up.

.DESCRIPTION
    Counts from when Windows started this sign-in's shell (sihost.exe). Two sources:

      processes   when GlazeWM, the bar, the menu app, the terminal and the agent started (always)
      marks       the timing marks omawin.exe and the menu app write to
                  %LOCALAPPDATA%\launcher\trace.log while trace-on exists: GlazeWM started, the
                  bar's server answering, the menu app ready

    -Arm turns the marks on for the next sign-in (and keeps them on: -Disarm turns them off).
    Sign out and back in, then run omawin profile login.
#>
param([switch]$Arm, [switch]$Disarm)

$state = Join-Path $env:LOCALAPPDATA 'launcher'
$traceOn = Join-Path $state 'trace-on'
$traceLog = Join-Path $state 'trace.log'
if ($Arm) { New-Item -ItemType Directory -Force $state | Out-Null; New-Item -ItemType File -Force $traceOn | Out-Null; 'Marks on. Sign out and back in, then run: omawin profile login'; return }
if ($Disarm) { Remove-Item $traceOn -ErrorAction SilentlyContinue; 'Marks off.'; return }

$session = (Get-Process -Id $PID).SessionId
function Mine($name) { @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session) }
$signIn = (Mine sihost | Sort-Object StartTime | Select-Object -First 1).StartTime
if (-not $signIn) { throw "can't tell when this sign-in started (no sihost.exe in this session)" }

$rows = [Collections.Generic.List[object]]::new()
function Add($what, [datetime]$at, $from) { if ($at) { $rows.Add([pscustomobject]@{ Seconds = [math]::Round(($at - $signIn).TotalSeconds, 1); What = $what; From = $from }) } }

foreach ($p in @(
        @{ Name = 'explorer'; What = 'taskbar (Explorer)' },
        @{ Name = 'glazewm'; What = 'GlazeWM' },
        @{ Name = 'zebar'; What = 'bar (Zebar)' },
        @{ Name = 'SystemMenu'; What = 'menu app' },
        @{ Name = 'noctty'; What = 'terminal (in the background)' },
        @{ Name = 'herdr'; What = 'agent session' })) {
    # The first one this sign-in: a later restart starts a newer one, which says nothing of login.
    $first = Mine $p.Name | Where-Object { $_.StartTime -ge $signIn } | Sort-Object StartTime | Select-Object -First 1
    if ($first) { Add $p.What $first.StartTime 'process' }
}

# Marks are QueryPerformanceCounter values: turned into clock times from the counter now.
if (Test-Path $traceLog) {
    $freq = [Diagnostics.Stopwatch]::Frequency
    $nowTicks = [Diagnostics.Stopwatch]::GetTimestamp(); $now = Get-Date
    $labels = @{ 'omawin explorer-ready' = 'taskbar ready (omawin)'; 'omawin glazewm-started' = 'GlazeWM started (omawin)'
        'omawin zebar-started' = 'bar started (omawin)'; 'omawin zebar-serving' = 'bar up: its widgets load'; 'menu ready' = 'menu app ready' }
    $seen = @{}
    foreach ($line in Get-Content $traceLog) {
        $label, $ticks = $line -split "`t"
        if (-not $labels.ContainsKey($label) -or $seen[$label]) { continue }
        $at = $now.AddSeconds(-(($nowTicks - [long]$ticks) / $freq))
        # The first of each this sign-in; later ones are restarts.
        if ($at -ge $signIn) { Add $labels[$label] $at 'mark'; $seen[$label] = $true }
    }
}

"Signed in at $($signIn.ToString('HH:mm:ss')); seconds after:"
$rows | Sort-Object Seconds | Format-Table -AutoSize
if (-not (Test-Path $traceOn)) { "(Only processes: omawin profile login -Arm adds the marks from the next sign-in.)" }
