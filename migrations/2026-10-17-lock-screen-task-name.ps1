# The lock screen's task was still called "dotfiles lock screen", from before the rename: it's
# "omawin lock screen" now (install/look.ps1). The old one is re-registered under the new name.
# Safe on a new PC (no old task: nothing to do) and twice.
$old = Get-ScheduledTask -TaskName 'dotfiles lock screen' -ErrorAction SilentlyContinue
if (-not $old) { return }
if (-not (Get-ScheduledTask -TaskName 'omawin lock screen' -ErrorAction SilentlyContinue)) {
    $xml = Export-ScheduledTask -TaskName $old.TaskName -TaskPath $old.TaskPath
    Register-ScheduledTask -TaskName 'omawin lock screen' -Xml $xml | Out-Null
}
Unregister-ScheduledTask -TaskName $old.TaskName -TaskPath $old.TaskPath -Confirm:$false
'the lock screen task is "omawin lock screen" now'
