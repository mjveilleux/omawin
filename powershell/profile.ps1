# Every PowerShell host runs this first, interactive or not (scripts, agents, editors), before
# Microsoft.PowerShell_profile.ps1 (which stops early when nobody's typing). Your account folder's
# powershell/all-hosts.ps1, if it has one, is loaded here: aliases and tool paths that scripts
# need too (docs/work.md). Keep it small: it runs on every pwsh start.
# $OmawinUserLayer: where that layer is (bin/user-layer.ps1; Microsoft.PowerShell_profile.ps1 uses it too).
$OmawinUserLayer = & "$(if ($env:DOTFILES) { $env:DOTFILES } else { "$HOME\omawin" })\bin\user-layer.ps1"
$allHosts = "$OmawinUserLayer\powershell\all-hosts.ps1"
if (Test-Path $allHosts) { . $allHosts }
Remove-Variable allHosts
