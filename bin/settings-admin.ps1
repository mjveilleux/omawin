<#
.SYNOPSIS
    The one elevated run for the Windows settings that need admin (roadmap 1B, 2.9;
    plans/02-settings-and-one-search.md §8).

.DESCRIPTION
    Started by the menu app with one UAC prompt (Verb runas, after it has shown exactly what will
    change), with a plan the non-elevated side wrote, one line per change:

        <id> TAB <verb> TAB <target> TAB <type or arguments> TAB <value>

    Only fixed verbs, each checked here again: machine-wide registry values (HKLM\SOFTWARE or
    HKLM\SYSTEM, DWORD or string), the policies of the account that asked (HKCU\Software\Policies,
    written to its hive by -User, its SID: this run may be another account), and a closed list of
    builtins (hibernation, sudo, a few Windows features, the search indexer's service, the SSH
    agent, UTF-8 code pages, Paint's AI, firewall rules for dev runtimes), and the PC's
    environment variables (omawin env set -Machine) and a Dev Drive (omawin dev-drive create).
    Nothing in the plan is
    ever run as a command. Writes "<id> TAB <done|fail|skip> TAB <reason>" per line to -Result,
    then exits: nothing stays elevated.

    First, a restore point (roadmap 8.42): Windows' own undo for everything below, made only
    here, inside a run you approved. With System Protection off it's skipped ("restore-point
    skip"), never turned on: that's your choice.

    Windows PowerShell 5.1 on purpose (AGENTS.md): it's always there, and an elevated run of a
    per-user pwsh.exe could be swapped by anything that can write to the user's folder.
#>
param(
    [Parameter(Mandatory = $true)][string]$Plan,
    [Parameter(Mandatory = $true)][string]$Result,
    [string]$User
)
$ErrorActionPreference = 'Stop'

# The features omawin's catalog offers; anything else in a plan is refused.
$features = @('VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux', 'Containers-DisposableClientVM', 'Microsoft-Hyper-V-All')
# sudo for Windows' modes, as its Enabled value (what `sudo config` writes).
$sudoModes = @{ 'off' = 0; 'forceNewWindow' = 1; 'disableInput' = 2; 'normal' = 3 }
# Services the catalog's `builtin service` lines may set (system.search-indexer), and the modes.
$services = @('WSearch')
$serviceModes = @('auto', 'delayed', 'manual', 'disabled')
# Paint's AI policies (look.ai-paint): set to 1 to turn each off, deleted to give it back.
$paintAi = @('DisableImageCreator', 'DisableCocreator', 'DisableGenerativeFill')
# The dev runtimes a firewall rule may be made for (developer.firewall-dev), by program name.
$runtimes = @('node', 'bun', 'python', 'dotnet', 'go')

function Say([string]$Id, [string]$State, [string]$Text = '') {
    [IO.File]::AppendAllText($Result, "$Id`t$State`t$($Text -replace '[\r\n\t]+', ' ')" + [Environment]::NewLine)
}

# A registry value under an open root key: '-' deletes it, else a DWORD or a string.
function Set-Value($Root, [string]$Path, [string]$Name, [string]$Type, [string]$Value) {
    $key = $Root.CreateSubKey($Path)
    try {
        if ($Value -eq '-') { $key.DeleteValue($Name, $false) }
        elseif ($Type -eq 'dword') {
            $number = [uint32]0
            if (-not [uint32]::TryParse($Value, [ref]$number)) { throw "not a number: $Value" }
            $key.SetValue($Name, [BitConverter]::ToInt32([BitConverter]::GetBytes($number), 0), [Microsoft.Win32.RegistryValueKind]::DWord)
        }
        elseif ($Type -eq 'sz') { $key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String) }
        else { throw "value type $Type" }
    }
    finally { $key.Close() }
}

function Set-ServiceMode([string]$Name, [string]$Mode) {
    if ($Mode -notin $serviceModes) { throw "start mode $Mode" }
    $null = Get-Service -Name $Name -ErrorAction Stop
    switch ($Mode) {
        'delayed'  { & "$env:SystemRoot\System32\sc.exe" config $Name start= delayed-auto | Out-Null; if ($LASTEXITCODE) { throw "sc.exe exited $LASTEXITCODE" } }
        'auto'     { Set-Service $Name -StartupType Automatic }
        'manual'   { Set-Service $Name -StartupType Manual }
        'disabled' { Set-Service $Name -StartupType Disabled; Stop-Service $Name -Force -ErrorAction SilentlyContinue }
    }
    if ($Mode -in 'auto', 'delayed') { Start-Service $Name -ErrorAction SilentlyContinue }
}

$lines = @([IO.File]::ReadAllLines($Plan) | Where-Object { ($_ -split "`t").Count -eq 5 })

# --- a restore point first (8.42) -----------------------------------------------------------------
# Windows makes at most one a day unless SystemRestorePointCreationFrequency (minutes) is 0: set
# for this call and put back as it was.
if ($lines.Count) {
    $restoreKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
    $old = (Get-ItemProperty $restoreKey -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
    try {
        if (-not (Test-Path $restoreKey)) { New-Item $restoreKey -Force | Out-Null }
        Set-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
        Checkpoint-Computer -Description "omawin: $($lines.Count) Windows setting$(if ($lines.Count -ne 1) { 's' })" -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
        Say 'restore-point' 'done'
    }
    catch { Say 'restore-point' 'skip' "no restore point (System Protection may be off): $($_.Exception.Message)" }
    finally {
        if ($null -eq $old) { Remove-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -Value $old -Type DWord }
    }
}

foreach ($line in $lines) {
    $id, $verb, $target, $arg, $value = $line -split "`t"
    $note = ''
    try {
        switch ($verb) {
            'reg' {
                if ($target -match '^HKLM\\(SOFTWARE|SYSTEM)\\[^\t]+\\[^\\]+$') {
                    $cut = $target.LastIndexOf('\')
                    Set-Value ([Microsoft.Win32.Registry]::LocalMachine) $target.Substring(5, $cut - 5) $target.Substring($cut + 1) $arg $value
                }
                elseif ($target -match '^HKCU\\Software\\Policies\\[^\t]+\\[^\\]+$') {
                    # The account that asked, by its SID; never this run's own HKCU.
                    # A local or domain account (S-1-5-21-…), or a Microsoft Entra one (S-1-12-1-…).
                    if ($User -notmatch '^S-1-(5-21|12-1)(-\d+){3,4}$') { throw 'no account to write its policies for' }
                    if (-not [Microsoft.Win32.Registry]::Users.OpenSubKey($User)) { throw "that account isn't signed in" }
                    $cut = $target.LastIndexOf('\')
                    Set-Value ([Microsoft.Win32.Registry]::Users) "$User\$($target.Substring(5, $cut - 5))" $target.Substring($cut + 1) $arg $value
                }
                else { throw "not a machine-wide setting or a policy: $target" }
            }
            'builtin' {
                switch ($target) {
                    'hibernate' {
                        if ($value -notin 'on', 'off') { throw "hibernate $value" }
                        & "$env:SystemRoot\System32\powercfg.exe" /hibernate $value | Out-Null
                        if ($LASTEXITCODE) { throw "powercfg exited $LASTEXITCODE" }
                    }
                    'sudo' {
                        if (-not $sudoModes.ContainsKey($value)) { throw "sudo mode $value" }
                        $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo')
                        try { $key.SetValue('Enabled', [int]$sudoModes[$value], [Microsoft.Win32.RegistryValueKind]::DWord) }
                        finally { $key.Close() }
                    }
                    'windows-feature' {
                        if ($arg -notin $features) { throw "feature $arg" }
                        if ($value -eq 'on') { Enable-WindowsOptionalFeature -Online -FeatureName $arg -All -NoRestart | Out-Null }
                        elseif ($value -eq 'off') { Disable-WindowsOptionalFeature -Online -FeatureName $arg -NoRestart | Out-Null }
                        else { throw "feature state $value" }
                    }
                    'service' {
                        # The value is the start mode, resolved from the line's on= / off= by the menu app.
                        if ($arg -notin $services) { throw "service $arg" }
                        Set-ServiceMode $arg $value
                    }
                    'ssh-agent' {
                        # On: starts with Windows, and now. Off: starts when something asks for it.
                        if ($value -notin 'on', 'off') { throw "ssh-agent $value" }
                        Set-ServiceMode 'ssh-agent' $(if ($value -eq 'on') { 'auto' } else { 'manual' })
                    }
                    'utf8' {
                        # On: the beta "Use Unicode UTF-8" (65001 for every code page). Off: the code
                        # pages of this PC's system locale, as Windows' own switch puts them back.
                        if ($value -notin 'on', 'off') { throw "utf8 $value" }
                        $ti = (Get-WinSystemLocale).TextInfo
                        $pages = if ($value -eq 'on') { @{ ACP = '65001'; OEMCP = '65001'; MACCP = '65001' } }
                                 else { @{ ACP = [string]$ti.ANSICodePage; OEMCP = [string]$ti.OEMCodePage; MACCP = [string]$ti.MacCodePage } }
                        foreach ($name in $pages.Keys) { Set-Value ([Microsoft.Win32.Registry]::LocalMachine) 'SYSTEM\CurrentControlSet\Control\Nls\CodePage' $name 'sz' $pages[$name] }
                    }
                    'ai-paint' {
                        if ($value -notin 'on', 'off') { throw "ai-paint $value" }
                        foreach ($name in $paintAi) {
                            Set-Value ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' $name 'dword' $(if ($value -eq 'off') { '1' } else { '-' })
                        }
                    }
                    'firewall-dev' {
                        # "<name>=<path>|…": each a dev runtime's own program, checked again here.
                        $made = 0
                        foreach ($pair in ($value -split '\|')) {
                            $name, $path = $pair -split '=', 2
                            if ($name -notin $runtimes) { throw "runtime $name" }
                            if ($path -notmatch '^[A-Za-z]:\\[^"<>|?*]+$' -or [IO.Path]::GetFileName($path) -ne "$name.exe") { throw "not $name's program: $path" }
                            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "not found: $path" }
                            $display = "omawin: $name on private networks ($path)"
                            if (-not (Get-NetFirewallRule -DisplayName $display -ErrorAction SilentlyContinue)) {
                                New-NetFirewallRule -DisplayName $display -Direction Inbound -Program $path -Action Allow -Profile Private | Out-Null
                                $made++
                            }
                        }
                        $note = "$made rule$(if ($made -ne 1) { 's' }) added"
                    }
                    default { throw "builtin $target" }
                }
            }
            'env' {
                # One of the PC's environment variables (omawin env set -Machine, roadmap 8.36):
                # "=<value>" sets it (kept unexpanded when it names another variable), "-" removes it.
                if ($target -notmatch '^[A-Za-z_(][A-Za-z0-9_().-]{0,254}$') { throw "variable name $target" }
                $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment', $true)
                try {
                    if ($value -eq '-') { $key.DeleteValue($target, $false) }
                    elseif ($value.StartsWith('=')) {
                        $text = $value.Substring(1)
                        $kind = if ($text.Contains('%')) { [Microsoft.Win32.RegistryValueKind]::ExpandString } else { [Microsoft.Win32.RegistryValueKind]::String }
                        $key.SetValue($target, $text, $kind)
                    }
                    else { throw "env value $value" }
                }
                finally { $key.Close() }
            }
            'devdrive' {
                # A Dev Drive (omawin dev-drive create, roadmap 8.32): <vhdx> TAB <GB> TAB <letter>. A
                # growing VHDX (diskpart: New-VHD needs Hyper-V's module, which Home hasn't got),
                # formatted as a Dev Drive, trusted (Defender's performance mode), and attached again
                # at every start-up by a task that runs once and exits.
                if ($target -notmatch '^[A-Za-z]:\\[^"''<>|?*]+\.vhdx$') { throw "VHDX path $target" }
                $gb = 0
                if (-not [int]::TryParse($arg, [ref]$gb) -or $gb -lt 50 -or $gb -gt 65536) { throw "size $arg GB: 50 or more" }
                if ($value -notmatch '^[D-Z]$') { throw "drive letter $value" }
                if (Test-Path "$($value):\") { throw "$($value): is in use" }
                if (Test-Path -LiteralPath $target) { throw "$target is already there" }
                New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
                $diskpart = Join-Path $env:TEMP "omawin-devdrive-$PID.txt"
                "create vdisk file=`"$target`" maximum=$($gb * 1024) type=expandable" | Set-Content $diskpart -Encoding ascii
                try { & "$env:SystemRoot\System32\diskpart.exe" /s $diskpart | Out-Null; if ($LASTEXITCODE) { throw "diskpart exited $LASTEXITCODE" } }
                finally { Remove-Item $diskpart -ErrorAction SilentlyContinue }
                $disk = Mount-DiskImage -ImagePath $target -PassThru | Get-Disk
                Initialize-Disk -Number $disk.Number -PartitionStyle GPT
                New-Partition -DiskNumber $disk.Number -UseMaximumSize -DriveLetter $value | Out-Null
                Format-Volume -DriveLetter $value -DevDrive -NewFileSystemLabel 'Dev' -Confirm:$false | Out-Null
                & "$env:SystemRoot\System32\fsutil.exe" devdrv trust "$($value):" | Out-Null
                $mount = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument "-NoProfile -NonInteractive -Command `"Mount-DiskImage -ImagePath '$target'`""
                Register-ScheduledTask -TaskName 'omawin Dev Drive' -TaskPath '\omawin\' -Action $mount -Trigger (New-ScheduledTaskTrigger -AtStartup) `
                    -Principal (New-ScheduledTaskPrincipal -UserId 'SYSTEM' -RunLevel Highest) -Force | Out-Null
                $note = "$($value): $gb GB"
            }
            default { throw "verb $verb" }
        }
        Say $id 'done' $note
    }
    catch { Say $id 'fail' $_.Exception.Message }
}
