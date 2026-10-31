# How the desktop starts at sign-in, fast. Run by install/desktop.ps1 and a migration; safe to
# run again. No admin.
#
# GlazeWM (which then starts the bar, the menu app and the terminal, config/glazewm/config.yaml) used to
# start from the registry's Run list. Windows launches that list late on purpose, from Explorer,
# after the PC has gone idle: GlazeWM came up 47 s after sign-in, Handy 78 s (measured
# 2026-10-14). A scheduled task at sign-in starts it straight away instead, and Windows' own
# start-up delay for the Run list is turned off, so what's left there (Handy, OneDrive, ...)
# doesn't wait either.

$ErrorActionPreference = 'Stop'
$glazewm = "$env:ProgramFiles\glzr.io\GlazeWM\glazewm.exe"
# Else the per-user copy from the release's tools bundle (install/software.ps1).
if (-not (Test-Path $glazewm)) { $glazewm = "$env:LOCALAPPDATA\omawin\tools\glazewm\glazewm.exe" }
# Who's signed in, from Windows itself (USERNAME can be something else in a service's shell).
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
# Task names are one list for the whole PC: each account's carries its name.
$taskName = "omawin GlazeWM ($([Environment]::UserName))"
if (Test-Path $glazewm) {
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    # Through omawin.exe ("omawin start glazewm", bin/omawin.cs), in a console nobody sees. It
    # starts GlazeWM through the shell (its manifest asks for uiAccess, and a plain start fails
    # with 0x800702E4) and again if it dies at start. Our fork (install/glazewm-fork.ps1) starts
    # at once; the official build waits for Explorer's taskbar first, since it crashes if it
    # starts before (the task fires the moment you sign in, seconds before Explorer).
    $omawin = "$HOME\.local\bin\omawin.exe"
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" `
        -Argument "--headless `"$omawin`" start glazewm" -WorkingDirectory $HOME
    # Normal priority (tasks default to below normal) and no time limit: it runs all session.
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -Priority 4 -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    $null = Register-ScheduledTask -TaskName $taskName -Trigger $trigger -Action $action -Settings $settings `
        -Principal $principal -Description 'GlazeWM at sign-in, without waiting for the Run list (omawin install/login-start.ps1)' -Force
    if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) { throw 'the sign-in task did not register' }
    # One start only, now that the task is there: the Run entry would launch a second copy later.
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if ((Get-ItemProperty $run -ErrorAction SilentlyContinue).GlazeWM) { Remove-ItemProperty $run -Name GlazeWM }
    "ok  GlazeWM starts at sign-in (scheduled task '$taskName')"
} else { 'GlazeWM not installed; re-run after installing it' }

# Windows' start-up delay for the Run list: none, and no waiting for the PC to go idle first.
$serialize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize'
if (-not (Test-Path $serialize)) { New-Item -Path $serialize -Force | Out-Null }
Set-ItemProperty $serialize -Name StartupDelayInMSec -Value 0 -Type DWord
Set-ItemProperty $serialize -Name WaitForIdleState -Value 0 -Type DWord
'ok  start-up apps launch without Windows'' delay'
