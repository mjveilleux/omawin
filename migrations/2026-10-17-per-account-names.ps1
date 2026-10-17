# Several Windows accounts can each run omawin now, so what was one per PC is one per account:
# the sign-in and lock screen tasks carry the account's name ("omawin GlazeWM (<user>)"), and
# each account gets its own GlazeWM and Zebar ports once the forks are in (bin/ports.ps1;
# `omawin install -Step desktop` puts the forks in, with two admin prompts). This account's old
# tasks are re-registered under the new names; another account's are left alone.
# Safe on a new PC (no old tasks) and twice.
$me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$myName = [Environment]::UserName
foreach ($old in 'omawin GlazeWM', 'omawin lock screen') {
    $task = Get-ScheduledTask -TaskName $old -TaskPath '\' -ErrorAction SilentlyContinue
    if (-not $task) { continue }
    $owner = "$($task.Principal.UserId)"
    if ($owner -ne $me -and $owner -ne $myName -and $owner -notlike "*\$myName") { continue }
    $new = "$old ($myName)"
    if (-not (Get-ScheduledTask -TaskName $new -ErrorAction SilentlyContinue)) {
        Register-ScheduledTask -TaskName $new -Xml (Export-ScheduledTask -TaskName $old -TaskPath '\') | Out-Null
    }
    # One made from an admin window can only be removed as admin: left, and said so.
    try { Unregister-ScheduledTask -TaskName $old -TaskPath '\' -Confirm:$false -ErrorAction Stop; "task '$old' is '$new' now" }
    catch { "task '$new' made; the old '$old' was made as admin: delete it in Task Scheduler (as admin)" }
}
& (Join-Path (Split-Path $PSScriptRoot) 'bin\ports.ps1') -Set
