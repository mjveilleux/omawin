<#
.SYNOPSIS
    omawin profile ram: what's using the memory and the processor, the whole PC, omawin's own first.

.DESCRIPTION
    One snapshot of every process, grouped into apps: an app's helper processes count as the app
    (WebView2 browsers such as the Suno window, Copilot, Slack…; the shells inside a
    terminal don't, they're their own). Apps are then grouped into what you'd recognise
    (omawin, the WSL virtual machine, browsers, web-view apps, Defender, Windows Search, the
    Windows shell, Windows' services, the PC maker's software, the kernel, your other apps), so
    every megabyte in use has a place (roadmap 8K).

    The first line splits the PC's memory the way Task Manager's memory bar does:
      in use    what programs and the kernel hold: the number to get down
      cached    the standby list, recently used files Windows keeps in RAM and hands back the
                moment anything asks: free memory, not waste, so nobody chases it
      modified  pages waiting to be written to disk before they can be reused
      free      untouched

    Per app:
      Memory   the private working set: RAM only this app holds, what Task Manager's Memory shows.
               This is the number that matters: shared DLLs and file cache aren't in it.
      Commit   what it has asked Windows for (RAM plus page file); far above Memory means
               memory paged out or reserved and never touched.
      CPU      share of all cores over -Seconds; an idle desktop's parts should be near 0.

    omawin's parts are listed against their idle budgets (below). The always-on desktop (the
    window manager, the shell with its bar, the terminal, herdr, the small helpers) has one
    budget for all of it, 600 MB; agents, dictation and music start when you use them and are
    counted apart.

    -Seconds  how long to watch the processor (default 5)
    -Quick    no processor sample: memory only, about a second (omawin check uses it)
    -Top      how many other apps to list (default 15)
    -Json     the whole result as JSON, for agents (the profile-ram skill reads it)
    -Save     add this run to %LOCALAPPDATA%\launcher\ram-profile.tsv, and show what changed
              since the last saved run (groups, in use and cached too: before and after for
              every change made to slim the PC down)

    Over a budget is a finding, not an error: look at why before raising it.
#>
param([int]$Seconds = 5, [int]$Top = 15, [switch]$Json, [switch]$Save, [switch]$Quick)

$ErrorActionPreference = 'Stop'
if ($Quick) { $Seconds = 0 }

# omawin's parts: process names (no .exe) -> the part, its idle budget (Memory MB, CPU %), and whether
# it's part of the always-on desktop (the mission's 600 MB) or started when you use it. PerProcess:
# the budget is for each process (one agent each), not the part's total.
$Parts = [ordered]@{
    'window manager (GlazeWM)'     = @{ Names = 'glazewm', 'glazewm-watcher'; MB = 80; Cpu = 1; Desktop = $true }
    'shell (menu app and bar)'     = @{ Names = 'SystemMenu'; MB = 200; Cpu = 1; Desktop = $true }
    'terminal (noctty)'            = @{ Names = 'noctty', 'noctty.com'; MB = 150; Cpu = 1; Desktop = $true }
    'agent sessions (herdr)'       = @{ Names = 'herdr'; MB = 100; Cpu = 1; Desktop = $true }
    'theme-watch'                  = @{ Names = 'theme-watch'; MB = 30; Cpu = 0.5; Desktop = $true }
    'omawin listen'                = @{ Names = 'omawin-listen', 'omawin'; MB = 30; Cpu = 0.5; Desktop = $true }
    'agents (on demand)'           = @{ Names = 'opencode', 'claude', 'codex'; MB = 800; Cpu = 2; Desktop = $false; PerProcess = $true }
    'dictation (on demand)'        = @{ Names = 'handy'; MB = 300; Cpu = 1; Desktop = $false }
    'music player (while playing)' = @{ Names = 'omawin-music', 'mpv'; MB = 250; Cpu = 3; Desktop = $false }
}
$DesktopBudgetMB = 600
$PartOf = @{}
foreach ($p in $Parts.Keys) { foreach ($n in $Parts[$p].Names) { $PartOf[$n] = $p } }

# Everything that isn't omawin's: process name -> group. Anything not listed is grouped by its
# exe's company when that's a hardware maker's or a preinstalled suite's, else under "apps".
$Groups = [ordered]@{
    'WSL virtual machine' = 'vmmem', 'vmmemWSL', 'vmcompute', 'wslservice', 'wslhost', 'wslrelay', 'wsl'
    'browsers'            = 'msedge', 'chrome', 'firefox', 'brave', 'vivaldi', 'opera', 'arc', 'zen'
    'web-view apps'       = 'olk', 'ms-teams', 'Widgets', 'WidgetService', 'WidgetBoard', 'PhoneExperienceHost', 'WebViewHost', 'Copilot', 'M365Copilot', 'Slack', 'Discord', 'Code', 'Spotify', 'Notion', 'Obsidian'
    'Defender (keep)'     = 'MsMpEng', 'MpDefenderCoreService', 'NisSrv', 'SecurityHealthService', 'SecurityHealthSystray', 'smartscreen'
    'Windows Search'      = 'SearchIndexer', 'SearchHost', 'SearchProtocolHost', 'SearchFilterHost'
    'Windows shell'       = 'explorer', 'dwm', 'StartMenuExperienceHost', 'ShellExperienceHost', 'ShellHost', 'TextInputHost', 'ctfmon', 'sihost', 'taskhostw', 'RuntimeBroker', 'ApplicationFrameHost', 'LockApp', 'SystemSettings', 'CrossDeviceResume'
    'OneDrive'            = 'OneDrive', 'OneDrive.Sync.Service', 'FileCoAuth'
    'Windows services'    = 'svchost', 'lsass', 'services', 'wininit', 'winlogon', 'csrss', 'smss', 'fontdrvhost', 'spoolsv', 'audiodg', 'WmiPrvSE', 'dllhost', 'WUDFHost', 'dasHost', 'MoUsoCoreWorker', 'TiWorker', 'TrustedInstaller', 'msiexec'
    'kernel'              = 'System', 'Registry', 'Secure System', 'Memory Compression'
}
$GroupOf = @{}
foreach ($g in $Groups.Keys) { foreach ($n in $Groups[$g]) { $GroupOf[$n] = $g } }
$Oem = 'Lenovo|Dell|HP Inc|Hewlett|ASUSTeK|ASUS|Acer|Micro-Star|MSI|Samsung|Razer|Logitech|Realtek|Intel|NVIDIA|Advanced Micro Devices|Synaptics|Dolby|Waves|Killer|Rivet|McAfee|Norton|Gen Digital'
# A svchost process this big is shown by the services it hosts (one svchost is many services).
$SvchostNamedMB = 30

$procs = @{}
foreach ($p in Get-CimInstance Win32_Process) {
    $procs[[int]$p.ProcessId] = [pscustomobject]@{
        Pid = [int]$p.ProcessId; Parent = [int]$p.ParentProcessId
        Name = [IO.Path]::GetFileNameWithoutExtension($p.Name); Created = $p.CreationDate; Path = $p.ExecutablePath
    }
}
# The private working set, per process (one query; Task Manager's Memory column).
$ws = @{}
foreach ($r in Get-CimInstance Win32_PerfRawData_PerfProc_Process) {
    if ($r.IDProcess) { $ws[[int]$r.IDProcess] = [double]$r.WorkingSetPrivate }
}
# Which services each process hosts, for svchost.
$servicesIn = @{}
foreach ($s in Get-CimInstance Win32_Service -Filter "State='Running'") {
    if ($s.ProcessId) { $servicesIn[[int]$s.ProcessId] += @($s.Name) }
}

# Company of an exe, cached by path (protected processes give no path, and no company).
$companyOf = @{}
function CompanyOf([string]$path) {
    if (-not $path) { return $null }
    if (-not $companyOf.ContainsKey($path)) { $companyOf[$path] = try { (Get-Item -LiteralPath $path).VersionInfo.CompanyName } catch { $null } }
    $companyOf[$path]
}

# Helpers that belong to whoever started them: a WebView2 or Chromium child is its host app's.
$Helpers = 'msedgewebview2', 'crashpad_handler', 'conhost'

# The app a process counts toward: its own name, unless it's a helper, then its host's. A big
# svchost is named by its services, so "svchost" isn't one opaque pile.
function AppOf($proc) {
    if ($proc.Name -eq 'svchost' -and ($ws[$proc.Pid] ?? 0) / 1MB -ge $SvchostNamedMB -and $servicesIn[$proc.Pid]) {
        $names = @($servicesIn[$proc.Pid] | Sort-Object)
        return "svchost: $(($names | Select-Object -First 3) -join ', ')$(if ($names.Count -gt 3) { ' …' })"
    }
    $seen = 0
    $p = $proc
    while ($p -and $Helpers -contains $p.Name -and $seen -lt 10) {
        $up = $procs[$p.Parent]
        # A parent id Windows reused for a newer process isn't the parent.
        if (-not $up -or ($up.Created -and $p.Created -and $up.Created -gt $p.Created)) { break }
        $p = $up
        $seen++
    }
    if ($Helpers -contains $p.Name) { return $proc }
    return $p
}

function GroupFor([string]$name, [string]$path) {
    if ($PartOf[$name]) { return 'omawin' }
    if ($name -like 'svchost: *') { return 'Windows services' }
    if ($GroupOf[$name]) { return $GroupOf[$name] }
    $company = CompanyOf $path
    if ($company -and $company -match $Oem) { return "PC maker: $($Matches[0])" }
    'apps'
}

# The processor: two readings, -Seconds apart (none with -Quick).
function CpuTimes {
    $t = @{}
    foreach ($g in Get-Process) {
        try { $t[$g.Id] = @{ Cpu = $g.TotalProcessorTime.TotalMilliseconds; Commit = $g.PrivateMemorySize64 } } catch { }
    }
    $t
}
$a = CpuTimes
$t0 = [Diagnostics.Stopwatch]::StartNew()
if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds }
$b = if ($Seconds -gt 0) { CpuTimes } else { $a }
$wall = [math]::Max(1, $t0.Elapsed.TotalMilliseconds * [Environment]::ProcessorCount)

$apps = @{}
$agentProcs = @()
foreach ($proc in $procs.Values) {
    if ($proc.Pid -eq 0) { continue } # Idle: not memory anyone holds
    $owner = AppOf $proc
    $app = if ($owner -is [string]) { $owner } else { $owner.Name }
    $path = if ($owner -is [string]) { $proc.Path } else { $owner.Path }
    if (-not $apps[$app]) {
        $apps[$app] = [pscustomobject]@{ App = $app; Part = $PartOf[$app]; Group = (GroupFor $app $path); Procs = 0; MemoryMB = 0.0; CommitMB = 0.0; Cpu = 0.0 }
    }
    $e = $apps[$app]
    $e.Procs++
    $mb = ($ws[$proc.Pid] ?? 0) / 1MB
    $e.MemoryMB += $mb
    if ($b[$proc.Pid]) {
        $e.CommitMB += $b[$proc.Pid].Commit / 1MB
        if ($Seconds -gt 0 -and $a[$proc.Pid]) { $e.Cpu += [math]::Max(0, $b[$proc.Pid].Cpu - $a[$proc.Pid].Cpu) / $wall * 100 }
    }
    if ($e.Part -and $Parts[$e.Part].PerProcess -and $proc.Name -eq $app) { $agentProcs += [pscustomobject]@{ Name = $app; Pid = $proc.Pid; MemoryMB = $mb } }
}

# omawin's parts, several process names each, against their budgets.
$parts = foreach ($name in $Parts.Keys) {
    $mine = @($apps.Values | Where-Object Part -eq $name)
    if (-not $mine) { continue }
    $budget = $Parts[$name]
    $mem = ($mine | Measure-Object MemoryMB -Sum).Sum
    $cpu = ($mine | Measure-Object Cpu -Sum).Sum
    $over = @()
    if ($budget.PerProcess) {
        $big = @($agentProcs | Where-Object { $budget.Names -contains $_.Name -and $_.MemoryMB -gt $budget.MB })
        if ($big) { $over += "$($big.Count) over $($budget.MB) MB each" }
    } elseif ($mem -gt $budget.MB) { $over += "memory over $($budget.MB) MB" }
    if ($Seconds -gt 0 -and $cpu -gt $budget.Cpu) { $over += "CPU over $($budget.Cpu)%" }
    [pscustomobject]@{
        Part = $name; Procs = [int]($mine | Measure-Object Procs -Sum).Sum
        MemoryMB = [int][math]::Round($mem); CommitMB = [int][math]::Round(($mine | Measure-Object CommitMB -Sum).Sum)
        Cpu = [math]::Round($cpu, 1); BudgetMB = $budget.MB; BudgetCpu = $budget.Cpu
        Desktop = [bool]$budget.Desktop; Over = $over -join ', '
    }
}
$others = $apps.Values | Where-Object { -not $_.Part } | Sort-Object MemoryMB -Descending | Select-Object -First $Top |
    ForEach-Object { [pscustomobject]@{ App = $_.App; Group = $_.Group; Procs = $_.Procs; MemoryMB = [int][math]::Round($_.MemoryMB); CommitMB = [int][math]::Round($_.CommitMB); Cpu = [math]::Round($_.Cpu, 1) } }
$busy = $apps.Values | Where-Object { $_.Cpu -ge 1 } | Sort-Object Cpu -Descending | Select-Object -First 8 |
    ForEach-Object { [pscustomobject]@{ App = $_.App; Cpu = [math]::Round($_.Cpu, 1) } }
$groups = $apps.Values | Group-Object Group | ForEach-Object {
    [pscustomobject]@{
        Group = $_.Name; Procs = [int]($_.Group | Measure-Object Procs -Sum).Sum
        MemoryMB = [int][math]::Round(($_.Group | Measure-Object MemoryMB -Sum).Sum)
        Top = ($_.Group | Sort-Object MemoryMB -Descending | Select-Object -First 3 | ForEach-Object { "$($_.App) $([math]::Round($_.MemoryMB))" }) -join ', '
    }
} | Sort-Object MemoryMB -Descending

# The PC's memory, as Task Manager's bar splits it: in use | modified | standby (cached) | free.
$os = Get-CimInstance Win32_OperatingSystem
$totalB = [double]$os.TotalVisibleMemorySize * 1KB
$m = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
$standbyB = [double]$m.StandbyCacheCoreBytes + [double]$m.StandbyCacheNormalPriorityBytes + [double]$m.StandbyCacheReserveBytes
$freeB = [double]$m.FreeAndZeroPageListBytes
$modifiedB = [double]$m.ModifiedPageListBytes
if (-not $freeB -and -not $standbyB) {
    # The counters missing (a broken performance counter registry): free and cached can't be
    # told apart, so everything available counts as free, and in use is what's left.
    $freeB = [double]$os.FreePhysicalMemory * 1KB
}
$inUseB = $totalB - $standbyB - $freeB - $modifiedB
$kernel = [pscustomobject]@{
    NonPagedMB      = [math]::Round([double]$m.PoolNonpagedBytes / 1MB)     # drivers; a driver's leak shows here
    PagedResidentMB = [math]::Round([double]$m.PoolPagedResidentBytes / 1MB)
    DriversMB       = [math]::Round([double]$m.SystemDriverResidentBytes / 1MB)
    SystemCacheMB   = [math]::Round([double]$m.SystemCacheResidentBytes / 1MB)
}
$attributedMB = ($apps.Values | Measure-Object MemoryMB -Sum).Sum
$memory = [pscustomobject]@{
    TotalMB = [math]::Round($totalB / 1MB); InUseMB = [math]::Round($inUseB / 1MB); ModifiedMB = [math]::Round($modifiedB / 1MB)
    CachedMB = [math]::Round($standbyB / 1MB); FreeMB = [math]::Round($freeB / 1MB)
    # What the apps don't explain: shared pages (DLLs, mapped files), page tables, the kernel's own.
    UnattributedMB = [math]::Round($inUseB / 1MB - $attributedMB - $kernel.NonPagedMB - $kernel.PagedResidentMB - $kernel.DriversMB)
}
$compressed = ($apps.Values | Where-Object App -eq 'Memory Compression' | Measure-Object MemoryMB -Sum).Sum
$desktopMB = ($parts | Where-Object Desktop | Measure-Object MemoryMB -Sum).Sum
$uptime = (Get-Date) - $os.LastBootUpTime
$system = [pscustomobject]@{
    TotalMB = $memory.TotalMB; InUseMB = $memory.InUseMB; CachedMB = $memory.CachedMB; FreeMB = $memory.FreeMB
    CommitMB = [int][math]::Round(($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) / 1KB)
    CommitLimitMB = [math]::Round($os.TotalVirtualMemorySize / 1KB)
    CompressedMB = [math]::Round($compressed ?? 0)
    OmawinMB = [math]::Round(($parts | Measure-Object MemoryMB -Sum).Sum)
    OmawinCpu = [math]::Round(($parts | Measure-Object Cpu -Sum).Sum, 1)
    DesktopMB = [math]::Round($desktopMB ?? 0); DesktopBudgetMB = $DesktopBudgetMB
    UptimeMinutes = [math]::Round($uptime.TotalMinutes)
    Seconds = $Seconds; At = (Get-Date).ToString('s')
}

# History: one row per part, group and top app, and the PC's in use and cached, to compare runs.
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
        @($groups | ForEach-Object { @{ Name = "[$($_.Group)]"; MemoryMB = $_.MemoryMB; Cpu = 0 } }) +
        @($others | ForEach-Object { @{ Name = $_.App; MemoryMB = $_.MemoryMB; Cpu = $_.Cpu } }) +
        @(@{ Name = '(in use)'; MemoryMB = $memory.InUseMB; Cpu = 0 }, @{ Name = '(cached)'; MemoryMB = $memory.CachedMB; Cpu = 0 },
          @{ Name = '(omawin desktop)'; MemoryMB = $system.DesktopMB; Cpu = 0 })
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
    [pscustomobject]@{ System = $system; Memory = $memory; Kernel = $kernel; Groups = $groups; Omawin = $parts; Others = $others; Busy = $busy; Changes = $changes } | ConvertTo-Json -Depth 4
    return
}

"RAM  {0:N1} GB in use of {1:N1} GB; {2:N1} GB cached (free when needed); {3:N1} GB free" -f ($memory.InUseMB / 1KB), ($memory.TotalMB / 1KB), ($memory.CachedMB / 1KB), ($memory.FreeMB / 1KB)
"     committed {0:N1} of {1:N1} GB; compressed {2} MB; kernel: non-paged pool {3} MB, drivers {4} MB" -f ($system.CommitMB / 1KB), ($system.CommitLimitMB / 1KB), $system.CompressedMB, $kernel.NonPagedMB, $kernel.DriversMB
"omawin desktop  $($system.DesktopMB) of $DesktopBudgetMB MB$(if ($system.DesktopMB -gt $DesktopBudgetMB) { ' (over)' }); all of omawin $($system.OmawinMB) MB$(if ($Seconds -gt 0) { ", $($system.OmawinCpu)% CPU over $Seconds s" }) (agents, dictation and music counted apart)"
""
"By group"
$groups | Format-Table Group, Procs, MemoryMB, Top -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
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
if ($memory.UnattributedMB -gt 0) {
    ""
    "$($memory.UnattributedMB) MB in use isn't any one app's: shared libraries and mapped files, page tables, the kernel's own."
}
if ($Save) {
    ""
    if ($changes) {
        "Changed since the last saved run ($($changes[0].Since))"
        $changes | Format-Table Name, WasMB, NowMB -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
    } else { "Saved to $history" }
}
