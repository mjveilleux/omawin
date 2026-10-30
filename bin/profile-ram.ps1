<#
.SYNOPSIS
    omawin profile ram: what's using the memory and the processor, by app, omawin's own first.

.DESCRIPTION
    One snapshot of every process, grouped into apps: an app's helper processes count as the app
    (WebView2 browsers such as the Suno window, Copilot, Slack…; the shells inside a
    terminal don't, they're their own). omawin's parts are listed first, each against a budget
    (below), then everything else, biggest first.

      Memory   the private working set: RAM only this app holds, what Task Manager's Memory shows.
               This is the number that matters: shared DLLs and file cache aren't in it.
      Commit   what it has asked Windows for (RAM plus page file); far above Memory means
               memory paged out or reserved and never touched.
      CPU      share of all cores over -Seconds; an idle desktop's parts should be near 0.

    -Seconds  how long to watch the processor (default 5)
    -Top      how many other apps to list (default 15)
    -Json     the whole result as JSON, for agents (the profile-ram skill reads it)
    -Save     add this run to %LOCALAPPDATA%\launcher\ram-profile.tsv, and show what changed
              since the last saved run

    The budgets are what each part should stay under on an idle desktop. Over one is a finding,
    not an error: look at why before raising it.
#>
param([int]$Seconds = 5, [int]$Top = 15, [switch]$Json, [switch]$Save)

$ErrorActionPreference = 'Stop'

# omawin's parts: process names (no .exe) -> the part, and its idle budget (Memory MB, CPU %).
$Parts = [ordered]@{
    'window manager (GlazeWM)' = @{ Names = 'glazewm', 'glazewm-watcher'; MB = 80; Cpu = 1 }
    'theme-watch'              = @{ Names = 'theme-watch'; MB = 30; Cpu = 0.5 }
    'menu app (launcher)'      = @{ Names = 'SystemMenu'; MB = 250; Cpu = 1 }
    'music player'             = @{ Names = 'omawin-music', 'mpv'; MB = 250; Cpu = 3 }
    'terminal (noctty)'        = @{ Names = 'noctty', 'noctty.com'; MB = 300; Cpu = 1 }
    'agent sessions (herdr)'   = @{ Names = 'herdr'; MB = 150; Cpu = 1 }
    'agent (OpenCode)'         = @{ Names = 'opencode'; MB = 800; Cpu = 2 }
    'dictation (Handy)'        = @{ Names = 'handy'; MB = 300; Cpu = 1 }
    'omawin listen'            = @{ Names = 'omawin-listen', 'omawin'; MB = 30; Cpu = 0.5 }
}
$PartOf = @{}
foreach ($p in $Parts.Keys) { foreach ($n in $Parts[$p].Names) { $PartOf[$n] = $p } }

# Helpers that belong to whoever started them: a WebView2 or Chromium child is its host app's.
$Helpers = 'msedgewebview2', 'crashpad_handler', 'conhost'

$procs = @{}
foreach ($p in Get-CimInstance Win32_Process) {
    $procs[[int]$p.ProcessId] = [pscustomobject]@{
        Pid = [int]$p.ProcessId; Parent = [int]$p.ParentProcessId
        Name = [IO.Path]::GetFileNameWithoutExtension($p.Name); Created = $p.CreationDate
    }
}
# The private working set, per process (one query; Task Manager's Memory column).
$ws = @{}
foreach ($r in Get-CimInstance Win32_PerfRawData_PerfProc_Process) {
    if ($r.IDProcess) { $ws[[int]$r.IDProcess] = [double]$r.WorkingSetPrivate }
}

# The app a process counts toward: its own name, unless it's a helper, then its host's.
function AppOf($proc) {
    $seen = 0
    $p = $proc
    while ($p -and $Helpers -contains $p.Name -and $seen -lt 10) {
        $up = $procs[$p.Parent]
        # A parent id Windows reused for a newer process isn't the parent.
        if (-not $up -or ($up.Created -and $p.Created -and $up.Created -gt $p.Created)) { break }
        $p = $up
        $seen++
    }
    if ($Helpers -contains $p.Name) { return $proc.Name }
    return $p.Name
}

# The processor: two readings, -Seconds apart.
function CpuTimes {
    $t = @{}
    foreach ($g in Get-Process) {
        try { $t[$g.Id] = @{ Cpu = $g.TotalProcessorTime.TotalMilliseconds; Commit = $g.PrivateMemorySize64 } } catch { }
    }
    $t
}
$a = CpuTimes
$t0 = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$b = CpuTimes
$wall = $t0.Elapsed.TotalMilliseconds * [Environment]::ProcessorCount

$apps = @{}
foreach ($proc in $procs.Values) {
    if ($proc.Pid -le 4) { continue } # Idle and System
    $app = AppOf $proc
    if (-not $apps[$app]) {
        $apps[$app] = [pscustomobject]@{ App = $app; Part = $PartOf[$app]; Procs = 0; MemoryMB = 0.0; CommitMB = 0.0; Cpu = 0.0 }
    }
    $e = $apps[$app]
    $e.Procs++
    $e.MemoryMB += ($ws[$proc.Pid] ?? 0) / 1MB
    if ($b[$proc.Pid]) {
        $e.CommitMB += $b[$proc.Pid].Commit / 1MB
        if ($a[$proc.Pid]) { $e.Cpu += [math]::Max(0, $b[$proc.Pid].Cpu - $a[$proc.Pid].Cpu) / $wall * 100 }
    }
}

# omawin's parts, several process names each, against their budgets.
$parts = foreach ($name in $Parts.Keys) {
    $mine = @($apps.Values | Where-Object Part -eq $name)
    if (-not $mine) { continue }
    $mem = ($mine | Measure-Object MemoryMB -Sum).Sum
    $cpu = ($mine | Measure-Object Cpu -Sum).Sum
    $over = @()
    if ($mem -gt $Parts[$name].MB) { $over += "memory over $($Parts[$name].MB) MB" }
    if ($cpu -gt $Parts[$name].Cpu) { $over += "CPU over $($Parts[$name].Cpu)%" }
    [pscustomobject]@{
        Part = $name; Procs = [int]($mine | Measure-Object Procs -Sum).Sum
        MemoryMB = [math]::Round($mem); CommitMB = [math]::Round(($mine | Measure-Object CommitMB -Sum).Sum)
        Cpu = [math]::Round($cpu, 1); BudgetMB = $Parts[$name].MB; BudgetCpu = $Parts[$name].Cpu
        Over = $over -join ', '
    }
}
$others = $apps.Values | Where-Object { -not $_.Part } | Sort-Object MemoryMB -Descending | Select-Object -First $Top |
    ForEach-Object { [pscustomobject]@{ App = $_.App; Procs = $_.Procs; MemoryMB = [math]::Round($_.MemoryMB); CommitMB = [math]::Round($_.CommitMB); Cpu = [math]::Round($_.Cpu, 1) } }
$busy = $apps.Values | Where-Object { $_.Cpu -ge 1 } | Sort-Object Cpu -Descending | Select-Object -First 8 |
    ForEach-Object { [pscustomobject]@{ App = $_.App; Cpu = [math]::Round($_.Cpu, 1) } }

$os = Get-CimInstance Win32_OperatingSystem
$totalMB = $os.TotalVisibleMemorySize / 1KB
$freeMB = $os.FreePhysicalMemory / 1KB
$compressed = ($apps.Values | Where-Object App -eq 'Memory Compression' | Measure-Object MemoryMB -Sum).Sum
$system = [pscustomobject]@{
    TotalMB = [math]::Round($totalMB); UsedMB = [math]::Round($totalMB - $freeMB); FreeMB = [math]::Round($freeMB)
    CommitMB = [math]::Round(($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) / 1KB)
    CommitLimitMB = [math]::Round($os.TotalVirtualMemorySize / 1KB)
    CompressedMB = [math]::Round($compressed ?? 0)
    OmawinMB = [math]::Round(($parts | Measure-Object MemoryMB -Sum).Sum)
    OmawinCpu = [math]::Round(($parts | Measure-Object Cpu -Sum).Sum, 1)
    Seconds = $Seconds; At = (Get-Date).ToString('s')
}

# History: one row per part and per top app, to compare runs.
$history = Join-Path $env:LOCALAPPDATA 'launcher\ram-profile.tsv'
$changes = @()
if ($Save) {
    $last = @{}
    if (Test-Path $history) {
        $rows = Get-Content $history | ConvertFrom-Csv -Delimiter "`t" -Header At, Name, MemoryMB, Cpu
        $lastAt = ($rows | Select-Object -Last 1).At
        foreach ($r in $rows | Where-Object At -eq $lastAt) { $last[$r.Name] = $r }
    }
    $now = @($parts | ForEach-Object { @{ Name = $_.Part; MemoryMB = $_.MemoryMB; Cpu = $_.Cpu } }) +
        @($others | ForEach-Object { @{ Name = $_.App; MemoryMB = $_.MemoryMB; Cpu = $_.Cpu } }) +
        @(@{ Name = '(used)'; MemoryMB = $system.UsedMB; Cpu = 0 })
    New-Item -ItemType Directory -Force (Split-Path $history) | Out-Null
    Add-Content $history ($now | ForEach-Object { "$($system.At)`t$($_.Name)`t$($_.MemoryMB)`t$($_.Cpu)" })
    foreach ($n in $now) {
        $was = $last[$n.Name]
        if ($was -and [math]::Abs($n.MemoryMB - [double]$was.MemoryMB) -ge 25) {
            $changes += [pscustomobject]@{ Name = $n.Name; WasMB = [double]$was.MemoryMB; NowMB = $n.MemoryMB; Since = $was.At }
        }
    }
}

if ($Json) {
    [pscustomobject]@{ System = $system; Omawin = $parts; Others = $others; Busy = $busy; Changes = $changes } | ConvertTo-Json -Depth 4
    return
}

"RAM  {0:N1} of {1:N1} GB used, {2:N1} GB free; committed {3:N1} of {4:N1} GB; compressed {5} MB" -f ($system.UsedMB / 1KB), ($system.TotalMB / 1KB), ($system.FreeMB / 1KB), ($system.CommitMB / 1KB), ($system.CommitLimitMB / 1KB), $system.CompressedMB
"omawin  $($system.OmawinMB) MB, $($system.OmawinCpu)% CPU over $Seconds s"
""
"omawin's parts (budget: idle)"
$parts | Format-Table Part, Procs, MemoryMB, CommitMB, Cpu, BudgetMB, Over -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
""
"Everything else, by memory"
$others | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
if ($busy) {
    ""
    "Busy over the last $Seconds s (1% or more of all cores)"
    $busy | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
}
if ($Save) {
    ""
    if ($changes) {
        "Changed since the last saved run ($($changes[0].Since))"
        $changes | Format-Table Name, WasMB, NowMB -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
    } else { "Saved to $history" }
}
