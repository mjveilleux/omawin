# The Linux in WSL became a choice (omawin default linux, bin/defaults.txt), Arch by default; it
# was always Ubuntu. A PC whose account has Ubuntu and has picked nothing keeps Ubuntu, so its
# terminals and projects open where its files are. Safe on a new PC (no Ubuntu, or already
# picked: nothing to do) and twice.
$pick = "$env:LOCALAPPDATA\launcher\defaults\linux"
if (Test-Path $pick) { return }
$distros = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.GetValue('DistributionName') })
if ($distros -notcontains 'Ubuntu') { return }
New-Item -ItemType Directory -Force -Path (Split-Path $pick) | Out-Null
Set-Content $pick 'ubuntu' -NoNewline
'Linux in WSL: Ubuntu, as before (omawin default linux arch to switch)'
