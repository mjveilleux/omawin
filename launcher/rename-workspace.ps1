# Alt+N (omawin workspace rename): name the current workspace (shown in the bar). Just a
# name box: type a name and press Enter; Enter on an empty box clears it back to the number;
# Esc cancels. Runs in a small terminal titled 'rename', which a GlazeWM window rule floats.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$Host.UI.RawUI.WindowTitle = 'rename'

. (Join-Path $PSScriptRoot 'common.ps1')

# The workspace this prompt was opened on (it floats on top of it, so it's the focused one).
$workspaces = (glazewm query workspaces | ConvertFrom-Json).data.workspaces
$ws = $workspaces | Where-Object hasFocus | Select-Object -First 1
if (-not $ws) { $ws = $workspaces | Where-Object isDisplayed | Select-Object -First 1 }
$current = if ($ws.displayName -and $ws.displayName -ne $ws.name) { $ws.displayName } else { '' }

$header = if ($current) { "now: $current   ·   enter on empty: clear" } else { 'no name yet' }
$typed = Read-Name "workspace $($ws.name)" $header
if ($null -eq $typed) { exit 0 } # Esc: leave it as it is

# Same rule as the bar's right-click rename: letters, digits, - _ . (spaces become dashes).
$label = ($typed -replace '\s+', '-') -replace '[^\w.-]', ''
if (-not $label) { $label = $ws.name } # empty: back to just the number
glazewm command update-workspace-config --workspace $ws.name --display-name $label | Out-Null
exit 0
