<#
.SYNOPSIS
    The one elevated run for the Windows settings that need admin (roadmap 1B, 2.9;
    plans/02-settings-and-one-search.md §8).

.DESCRIPTION
    Started by the menu app with one UAC prompt (Verb runas, after it has shown exactly what will
    change), with a plan the non-elevated side wrote, one line per change:

        <id> TAB <verb> TAB <target> TAB <type or arguments> TAB <value>

    Only fixed verbs, each checked here again: machine-wide registry values (HKLM\SOFTWARE or
    HKLM\SYSTEM, DWORD or string), and a closed list of builtins (hibernation, sudo, a few
    Windows features). Nothing in the plan is ever run as a command. Writes
    "<id> TAB <done|fail> TAB <reason>" per line to -Result, then exits: nothing stays elevated.

    Windows PowerShell 5.1 on purpose (AGENTS.md): it's always there, and an elevated run of a
    per-user pwsh.exe could be swapped by anything that can write to the user's folder.
#>
param(
    [Parameter(Mandatory = $true)][string]$Plan,
    [Parameter(Mandatory = $true)][string]$Result
)
$ErrorActionPreference = 'Stop'

# The features omawin's catalog offers; anything else in a plan is refused.
$features = @('VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux', 'Containers-DisposableClientVM', 'Microsoft-Hyper-V-All')
# sudo for Windows' modes, as its Enabled value (what `sudo config` writes).
$sudoModes = @{ 'off' = 0; 'forceNewWindow' = 1; 'disableInput' = 2; 'normal' = 3 }

function Say([string]$Id, [string]$State, [string]$Text = '') {
    [IO.File]::AppendAllText($Result, "$Id`t$State`t$($Text -replace '[\r\n\t]+', ' ')" + [Environment]::NewLine)
}

foreach ($line in [IO.File]::ReadAllLines($Plan)) {
    $p = $line -split "`t"
    if ($p.Count -ne 5) { continue }
    $id, $verb, $target, $arg, $value = $p
    try {
        switch ($verb) {
            'reg' {
                if ($target -notmatch '^HKLM\\(SOFTWARE|SYSTEM)\\[^\t]+\\[^\\]+$') { throw "not a machine-wide setting: $target" }
                $cut = $target.LastIndexOf('\')
                $path = $target.Substring(5, $cut - 5)
                $name = $target.Substring($cut + 1)
                $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($path)
                try {
                    if ($value -eq '-') { $key.DeleteValue($name, $false) }
                    elseif ($arg -eq 'dword') {
                        $number = [uint32]0
                        if (-not [uint32]::TryParse($value, [ref]$number)) { throw "not a number: $value" }
                        $key.SetValue($name, [BitConverter]::ToInt32([BitConverter]::GetBytes($number), 0), [Microsoft.Win32.RegistryValueKind]::DWord)
                    }
                    elseif ($arg -eq 'sz') { $key.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::String) }
                    else { throw "value type $arg" }
                }
                finally { $key.Close() }
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
                    default { throw "builtin $target" }
                }
            }
            default { throw "verb $verb" }
        }
        Say $id 'done'
    }
    catch { Say $id 'fail' $_.Exception.Message }
}
