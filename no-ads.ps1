<#
.SYNOPSIS
    No ads: Windows' own suggestions and promos off, and the PC maker's upsells gone.

.DESCRIPTION
    Two parts. Windows' debloat always runs; the OEM modules (Lenovo, McAfee, Adobe) each run
    only when that software is on this PC, so on a PC without it they touch nothing.

    Windows, for you (no admin; install.ps1 runs this part):
      - "suggestions": promoted apps installed silently, Start recommendations, tips and
        suggestions toasts, the welcome / "finish setting up your PC" screens, suggested content
        in Settings, lock screen fun facts, OneDrive promos in Explorer, account nags in Start;
        the advertising ID and tailored experiences.
    Windows, with admin (one Windows permission prompt; -Admin, or this script asks):
      - the "no consumer features" policy, which stops promoted apps being installed (Home
        editions may ignore some of it; the per-user switches above cover them).

    Modules, each only when found:
      - Lenovo: notifications from Vantage and its messaging plugin (its ads) off; Vantage no
        longer starts at login (it still opens when you start it; it's kept for battery limits,
        firmware updates and warranty). Admin: Lenovo Now uninstalled, its leftover shortcuts
        (Lenovo Subscription Marketplace too) and promo tasks removed, the Universal Device
        Client (runs the messaging / ads plugin) off. Lenovo Hotkeys (Fn keys, volume /
        brightness popups) and Vantage stay.
      - McAfee: its notifications off. Admin: McAfee Security Scan Plus and WebAdvisor
        uninstalled, their leftover shortcuts removed.
      - Adobe: Acrobat's sync no longer starts at login; Photoshop Express (preinstalled, runs
        in the background) removed. Admin: Adobe's updater only when Acrobat asks, its daily
        update task off.
    Safe to run again; the admin part asks only when there's something left for it to do.

        .\no-ads.ps1             your part, then asks for admin for the rest
        .\no-ads.ps1 -UserOnly   your part only (install.ps1)
#>
param([switch]$UserOnly, [switch]$Admin)
$ErrorActionPreference = 'Stop'

function Set-Dword([string]$Key, [string]$Name, [int]$Value) {
    if (-not (Test-Path $Key)) { New-Item -Path $Key -Force | Out-Null }
    Set-ItemProperty -Path $Key -Name $Name -Value $Value -Type DWord
}

function Get-Installed([string]$Name) {
    Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object DisplayName -EQ $Name | Select-Object -First 1
}

# Silent if the uninstaller allows it; McAfee's may still show their own window to confirm.
function Remove-Program([string]$Name, [string]$Silent) {
    $app = Get-Installed $Name
    if (-not $app) { Write-Host "  ok  $Name (not installed)"; return }
    # UninstallString is "C:\...\uninstall.exe" /args, or the same unquoted (spaces and all):
    # the program is up to the first .exe, the rest are its own arguments, kept.
    $command = $app.UninstallString.Trim()
    if ($command -match '^"([^"]+)"\s*(.*)$' -or $command -match '^(.+?\.exe)\s*(.*)$') { $exe, $own = $Matches[1], $Matches[2].Trim() }
    else { $exe, $own = $command, '' }
    Write-Host "  removing $Name"
    $quiet = "$own $Silent".Trim()
    if ($quiet) { Start-Process $exe -ArgumentList $quiet -Wait -ErrorAction SilentlyContinue }
    else { Start-Process $exe -Wait -ErrorAction SilentlyContinue }
    if (Get-Installed $Name) {
        Write-Host "    its uninstaller needs you: follow its window"
        if ($own) { Start-Process $exe -ArgumentList $own -Wait -ErrorAction SilentlyContinue }
        else { Start-Process $exe -Wait -ErrorAction SilentlyContinue }
    }
    Write-Host ("    " + $(if (Get-Installed $Name) { "still installed" } else { "removed" }))
}

function Remove-Leftovers([string[]]$Paths) {
    foreach ($path in $Paths) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force; Write-Host "  removed $path" }
    }
}

# Turns off notifications from the senders matching $Pattern (plus $Known ones), except those
# matching $Keep; returns the ids it turned off.
$Senders = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
function Disable-Senders([string]$Pattern, [string[]]$Known = @(), [string]$Keep = '^$') {
    $found = Get-ChildItem $Senders -ErrorAction SilentlyContinue | Where-Object PSChildName -Match $Pattern | ForEach-Object PSChildName
    foreach ($sender in @(@($Known) + @($found) | Where-Object { $_ } | Select-Object -Unique | Where-Object { $_ -notmatch $Keep })) {
        Set-Dword (Join-Path $Senders $sender) 'Enabled' 0
        $sender
    }
}

$StartMenu = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
$Run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# --- OEM modules ------------------------------------------------------------------------------
# Found: is that software on this PC (no admin needed to tell). User: your part. Admin: the part
# that needs admin, run elevated. NeedsAdmin: is anything left for the admin part to do.
$Modules = [ordered]@{
    Lenovo = @{
        Found = {
            (Test-Path "$env:ProgramData\Lenovo") -or (Get-Installed 'Lenovo Now') -or
            (Get-AppxPackage -Name 'E046963F.LenovoCompanion' -ErrorAction SilentlyContinue) -or
            (Get-Service UDCService -ErrorAction SilentlyContinue)
        }
        User = {
            # Lenovo Hotkeys (E0469640.LenovoUtility) shows the volume / brightness popups: kept.
            $off = Disable-Senders 'Lenovo|E046963F' @('E046963F.LenovoCompanion_k1h2ywk1493x8!App', 'C:/ProgramData/Lenovo/Udc/Hosts/x64/MessagingPlugin.exe') 'LenovoUtility'
            # Their ids are app ids or program paths; say which app instead.
            $names = foreach ($sender in $off) {
                if ($sender -match 'LenovoCompanion') { 'Lenovo Vantage' }
                elseif ($sender -match 'MessagingPlugin') { "Lenovo's messaging plugin" }
                else { "Lenovo ($(Split-Path $sender -Leaf))" }
            }
            "ok  Lenovo notifications off: $(@($names | Select-Object -Unique) -join ', ')"
            if ((Get-ItemProperty $Run -ErrorAction SilentlyContinue).LenovoVantage) {
                Remove-ItemProperty $Run -Name LenovoVantage
                'Lenovo Vantage no longer starts at login'
            }
        }
        NeedsAdmin = {
            (Get-Installed 'Lenovo Now') -or (Test-Path "$StartMenu\Lenovo Now.lnk") -or (Test-Path "$StartMenu\Lenovo Subscription Marketplace.url") -or
            (Get-ScheduledTask -TaskPath '\Lenovo\' -ErrorAction SilentlyContinue | Where-Object TaskName -Match 'LenovoNow|Notification') -or
            ((Get-Service UDCService -ErrorAction SilentlyContinue) -and (Get-Service UDCService).StartType -ne 'Disabled')
        }
        Admin = {
            Remove-Program 'Lenovo Now' '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
            Remove-Leftovers "$StartMenu\Lenovo Now.lnk", "$StartMenu\Lenovo Subscription Marketplace.url"
            foreach ($task in Get-ScheduledTask -TaskPath '\Lenovo\' -ErrorAction SilentlyContinue | Where-Object TaskName -Match 'LenovoNow|Notification') {
                Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false
                Write-Host "  removed task $($task.TaskPath)$($task.TaskName)"
            }
            # The Universal Device Client runs the messaging / ads plugin: off. Vantage and the
            # Fn-key / thermal services stay.
            $svc = Get-Service UDCService -ErrorAction SilentlyContinue
            if ($svc) {
                if ($svc.Status -ne 'Stopped') { Stop-Service UDCService -Force -ErrorAction SilentlyContinue }
                Set-Service UDCService -StartupType Disabled
                Write-Host '  UDCService -> Disabled'
            }
            Get-Process MessagingPlugin -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
    McAfee = @{
        Found = {
            (Get-Installed 'McAfee Security Scan Plus') -or (Get-Installed 'WebAdvisor by McAfee') -or
            (Test-Path "$StartMenu\McAfee Security Scan Plus") -or
            (Get-ChildItem $Senders -ErrorAction SilentlyContinue | Where-Object PSChildName -Match 'McAfee|WebAdvisor|mc-')
        }
        User = {
            $off = Disable-Senders 'McAfee|WebAdvisor|mc-'
            $names = foreach ($sender in $off) { if ($sender -match 'WebAdvisor') { 'WebAdvisor' } else { 'McAfee' } }
            "ok  McAfee notifications off: $(@($names | Select-Object -Unique) -join ', ')"
        }
        NeedsAdmin = {
            (Get-Installed 'McAfee Security Scan Plus') -or (Get-Installed 'WebAdvisor by McAfee') -or
            (Test-Path "$StartMenu\McAfee Security Scan Plus") -or (Test-Path "$StartMenu\Startup\McAfee Security Scan Plus.lnk")
        }
        Admin = {
            Remove-Program 'McAfee Security Scan Plus' '/S'
            Remove-Program 'WebAdvisor by McAfee' '/s'
            Remove-Leftovers "$StartMenu\McAfee Security Scan Plus", "$StartMenu\Startup\McAfee Security Scan Plus.lnk"
        }
    }
    Adobe = @{
        Found = {
            (Get-ItemProperty $Run -ErrorAction SilentlyContinue).'Adobe Acrobat Synchronizer' -or
            (Get-AppxPackage -Name 'AdobeSystemsIncorporated.AdobePhotoshopExpress' -ErrorAction SilentlyContinue) -or
            (Get-Service AdobeARMservice -ErrorAction SilentlyContinue) -or
            (Get-ScheduledTask -TaskName 'Adobe Acrobat Update Task' -ErrorAction SilentlyContinue)
        }
        User = {
            # Acrobat's sync no longer starts at login: turned off the way Task Manager does it
            # (its "approved" flag; first byte 3 = disabled), so Acrobat itself is untouched.
            if ((Get-ItemProperty $Run -ErrorAction SilentlyContinue).'Adobe Acrobat Synchronizer') {
                $approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
                $flag = (Get-ItemProperty $approved -ErrorAction SilentlyContinue).'Adobe Acrobat Synchronizer'
                if (-not $flag -or $flag[0] -ne 3) {
                    if (-not (Test-Path $approved)) { New-Item -Path $approved -Force | Out-Null }
                    Set-ItemProperty $approved -Name 'Adobe Acrobat Synchronizer' -Value ([byte[]](@(3) + (@(0) * 11))) -Type Binary
                    'Adobe Acrobat sync no longer starts at login'
                }
            }
            $psx = Get-AppxPackage -Name 'AdobeSystemsIncorporated.AdobePhotoshopExpress' -ErrorAction SilentlyContinue
            if ($psx) { $psx | Remove-AppxPackage; 'removed Adobe Photoshop Express' }
            'ok  Adobe background starts off'
        }
        NeedsAdmin = {
            ((Get-Service AdobeARMservice -ErrorAction SilentlyContinue) -and (Get-Service AdobeARMservice).StartType -ne 'Manual') -or
            ((Get-ScheduledTask -TaskName 'Adobe Acrobat Update Task' -ErrorAction SilentlyContinue) -and (Get-ScheduledTask -TaskName 'Adobe Acrobat Update Task').State -ne 'Disabled')
        }
        Admin = {
            # Adobe's updater: only when Acrobat asks, and its daily update task off.
            if (Get-Service AdobeARMservice -ErrorAction SilentlyContinue) {
                Set-Service AdobeARMservice -StartupType Manual
                Write-Host '  AdobeARMservice -> Manual'
            }
            $task = Get-ScheduledTask -TaskName 'Adobe Acrobat Update Task' -ErrorAction SilentlyContinue
            if ($task -and $task.State -ne 'Disabled') { $task | Disable-ScheduledTask | Out-Null; Write-Host '  Adobe Acrobat Update Task -> off' }
        }
    }
    # Microsoft's Copilot app: off the taskbar, no start at login, and not left running out of
    # sight (it restarts itself in the background, ~70 MB). It still opens from Start.
    Copilot = @{
        Found = {
            (Test-Path "${env:ProgramFiles(x86)}\Microsoft\Copilot\Application\copilotapp.exe") -or
            (Get-AppxPackage -Name 'Microsoft.Copilot' -ErrorAction SilentlyContinue)
        }
        User = {
            # Settings > Personalization > Taskbar > Copilot.
            Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowCopilotButton' 0
            # Settings > Apps > Startup: any Copilot entry off (its "approved" flag, as Task Manager does).
            $approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
            foreach ($name in (Get-ItemProperty $Run -ErrorAction SilentlyContinue).PSObject.Properties.Name | Where-Object { $_ -match 'Copilot' }) {
                if (-not (Test-Path $approved)) { New-Item -Path $approved -Force | Out-Null }
                Set-ItemProperty $approved -Name $name -Value ([byte[]](@(3) + (@(0) * 11))) -Type Binary
            }
            # Running with no window: closed. An open Copilot window is left alone.
            $hidden = @(Get-Process copilotapp, copilotapphost -ErrorAction SilentlyContinue)
            if ($hidden -and -not ($hidden | Where-Object MainWindowHandle -ne 0)) { $hidden | Stop-Process -Force -ErrorAction SilentlyContinue }
            'ok  Copilot off the taskbar and out of the background'
        }
        NeedsAdmin = { $false }
        Admin = { }
    }
    # Visual Studio's installer downloads every update in the background as it comes out (~450
    # MB of memory and a core while it does). Off, as its own setting does (Installer > Update
    # settings > Automatically download updates); updates come when you open the Installer.
    VisualStudio = @{
        Found = { Test-Path "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vs_installer.exe" }
        User = {
            'ok  Visual Studio: updates download when you open its Installer' + $(if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\Setup' -ErrorAction SilentlyContinue).BackgroundDownloadDisabled -ne 1) { ' (with admin)' })
        }
        NeedsAdmin = { (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\Setup' -ErrorAction SilentlyContinue).BackgroundDownloadDisabled -ne 1 }
        Admin = {
            Set-Dword 'HKLM:\SOFTWARE\Microsoft\VisualStudio\Setup' 'BackgroundDownloadDisabled' 1
            Get-Process BackgroundDownload -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host '  background downloads -> off'
        }
    }
}
$Present = @($Modules.Keys | Where-Object { & $Modules[$_].Found })

$CloudContent = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
function Test-PolicySet {
    $p = Get-ItemProperty $CloudContent -ErrorAction SilentlyContinue
    $p.DisableWindowsConsumerFeatures -eq 1 -and $p.DisableSoftLanding -eq 1 -and $p.DisableConsumerAccountStateContent -eq 1
}

if (-not $Admin) {
    # This part's lines go to the output (not Write-Host), so install.ps1 can show them as its
    # own notes; run alone, they print as usual.
    $cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    foreach ($name in 'SilentInstalledAppsEnabled', 'SystemPaneSuggestionsEnabled', 'SoftLandingEnabled',
        'PreInstalledAppsEnabled', 'PreInstalledAppsEverEnabled', 'OemPreInstalledAppsEnabled',
        'FeatureManagementEnabled', 'RotatingLockScreenOverlayEnabled',
        'SubscribedContent-310093Enabled', # welcome experience after updates
        'SubscribedContent-338387Enabled', # lock screen fun facts and tips
        'SubscribedContent-338388Enabled', # suggestions in Start
        'SubscribedContent-338389Enabled', # tips and suggestions toasts
        'SubscribedContent-338393Enabled', 'SubscribedContent-353694Enabled', 'SubscribedContent-353696Enabled', # suggested content in Settings
        'SubscribedContent-353698Enabled', # suggestions in the timeline
        'SubscribedContent-88000326Enabled') { Set-Dword $cdm $name 0 }
    $advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    Set-Dword $advanced 'ShowSyncProviderNotifications' 0 # OneDrive / Microsoft 365 promos in Explorer
    Set-Dword $advanced 'Start_IrisRecommendations' 0      # tips, shortcuts and new apps in Start
    Set-Dword $advanced 'Start_AccountNotifications' 0     # account nags in Start
    Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement' 'ScoobeSystemSettingEnabled' 0 # "finish setting up"
    Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled' 0
    # Settings > Privacy > Search permissions > Show search highlights: Bing's promos in search.
    Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' 'IsDynamicSearchBoxEnabled' 0
    Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled' 0
    Set-Dword 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested' 'Enabled' 0
    'ok  Windows suggestions and promos off'

    foreach ($name in $Present) { & $Modules[$name].User }
    # Explorer reads the Start / Explorer switches when it starts; the rest apply now.
    if ($UserOnly) { return }

    $todo = @($Present | Where-Object { & $Modules[$_].NeedsAdmin })
    if (-not $todo -and (Test-PolicySet)) { Write-Host 'Nothing left that needs admin.'; return }
}

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    Write-Host "The rest needs admin ($(@(@($todo) + 'Windows policy') -join ', ')): approve the Windows prompt."
    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
    $p = Start-Process $shell -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Admin'
    exit $p.ExitCode
}

foreach ($name in $Present) {
    Write-Host $name
    & $Modules[$name].Admin
}

Write-Host 'Policy: no consumer features (promoted apps)'
Set-Dword $CloudContent 'DisableWindowsConsumerFeatures' 1
Set-Dword $CloudContent 'DisableSoftLanding' 1
Set-Dword $CloudContent 'DisableConsumerAccountStateContent' 1
Write-Host 'Done.'
Start-Sleep 3
