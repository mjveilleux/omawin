<#
.SYNOPSIS
    Health check: is everything this repo sets up actually in place and working?

.DESCRIPTION
    Read-only; changes nothing. Checks the tools on PATH, the config links, the startup
    entries, the helper exes (omawin.exe too), that the migrations have run, that every
    script in the repo parses, the menu and command lists, and that GlazeWM, the bar and the
    launcher are running. Prints one line per check and a summary; exits 1 if
    anything failed, so it can gate a test run.

        .\check.ps1            everything
        .\check.ps1 -Quiet     only the problems

    Run it after install.ps1, after a big change, or when something feels off.
#>
param([switch]$Quiet)

$Repo = Split-Path $PSScriptRoot
# PATH as a new shell would see it (this one may predate the latest installs).
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$script:failed = 0; $script:warned = 0

function Pass([string]$What, [string]$Detail = '') { if (-not $Quiet) { Write-Host "  ok    $What" -NoNewline; Write-Host "  $Detail" -ForegroundColor DarkGray } }
function Fail([string]$What, [string]$Detail = '') { $script:failed++; Write-Host "  FAIL  $What" -ForegroundColor Red -NoNewline; Write-Host "  $Detail" -ForegroundColor DarkGray }
function Warn([string]$What, [string]$Detail = '') { $script:warned++; Write-Host "  warn  $What" -ForegroundColor Yellow -NoNewline; Write-Host "  $Detail" -ForegroundColor DarkGray }
function Section([string]$Name) { if (-not $Quiet) { Write-Host "`n$Name" -ForegroundColor Cyan } }

# A junction (or symlink) at $Path pointing into the repo at $Target.
function Test-Link([string]$Path, [string]$Target, [string]$Name) {
    $item = Get-Item $Path -Force -ErrorAction SilentlyContinue
    if (-not $item) { Fail $Name "missing: $Path" }
    elseif (-not $item.LinkType) { Fail $Name "not a link: $Path" }
    elseif ((@($item.Target)[0] -replace '\\$', '') -ne ($Target -replace '\\$', '')) { Fail $Name "points at $(@($item.Target)[0])" }
    else { Pass $Name "$Path -> $Target" }
}

# --- tools --------------------------------------------------------------------------------
Section 'tools on PATH'
# The core's, then those of the presets this PC picked (install/packages.ps1, omawin preset list).
. "$Repo\install\packages.ps1"
$UserLayer = & "$Repo\bin\user-layer.ps1"   # your account folder (docs/work.md)
$chosen = @(Get-Content "$env:LOCALAPPDATA\launcher\presets.txt" -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
$tools = @('pwsh', 'git', 'gh', 'wt', 'starship', 'zoxide', 'fzf', 'fd', 'rg', 'eza', 'bat', 'jq', 'lazygit',
'glazewm', 'nvim', 'node', 'uv', 'herdr',
'omawin', 'omawin-cli', 'voice-meter', 'omawin-music')
foreach ($name in $chosen) {
    if ($Presets.Contains($name)) { $tools += @($Presets[$name].Tools | Where-Object { $_ }) }
    else { Warn "preset $name" "picked, but install/packages.ps1 has no such preset (omawin preset remove $name)" }
}
foreach ($tool in $tools | Select-Object -Unique) {
    $cmd = Get-Command $tool -ErrorAction SilentlyContinue | Select-Object -First 1
    # Not on PATH, but where a by-hand install or winget's package folder put it (the music
    # player looks there for mpv too: src/music/Mpv.cs; shinchiro.mpv's installer puts it in Program
    # Files\MPV Player).
    $found = if ($cmd) { $cmd.Source } else {
        @("$env:LOCALAPPDATA\Programs\$tool\$tool.exe", "$env:ProgramFiles\MPV Player\$tool.exe") + @(Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter "$tool.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue | ForEach-Object FullName) |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($cmd) { Pass $tool $found } elseif ($found) { Pass $tool "$found (not on PATH)" } else { Fail $tool 'not found' }
}
# The dictation preset: Win+H does nothing until Handy has a speech model (install/desktop.ps1).
if ($chosen -contains 'dictation' -and (Test-Path "$env:APPDATA\com.pais.handy\settings_store.json")) {
    $handyModel = (Get-Content "$env:APPDATA\com.pais.handy\settings_store.json" -Raw | ConvertFrom-Json).settings.selected_model
    if ($handyModel) { Pass 'Handy speech model' $handyModel } else { Fail 'Handy speech model' 'none, so Win+H does nothing (install.ps1 -Step desktop)' }
}
# The wsl preset: this account's Linux, the one `omawin default linux` picked (bin/defaults.txt),
# in WSL's registry (so WSL isn't started to ask).
if ($chosen -contains 'wsl') {
    $linux = @(foreach ($line in Get-Content "$Repo\bin\defaults.txt") { $p = @($line -split '\|' | ForEach-Object { $_.Trim() }); if ($p.Count -ge 5 -and $p[0] -eq 'linux') { , $p } })
    $picked = "$(Get-Content "$env:LOCALAPPDATA\launcher\defaults\linux" -TotalCount 1 -ErrorAction SilentlyContinue)".Trim()
    $pick = @($linux | Where-Object { $_[1] -eq $picked }) + , $linux[0] | Select-Object -First 1
    $distros = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss' -ErrorAction SilentlyContinue | ForEach-Object { $_.GetValue('DistributionName') })
    $what = "$(($pick[2] -split ',')[0]) (WSL)"
    if ($distros -contains $pick[4]) { Pass $what "wsl -d $($pick[4])" }
    elseif (Test-Path "$env:LOCALAPPDATA\launcher\wsl-restart") { Warn $what 'WSL is in; it finishes setting up after Windows restarts' }
    else { Fail $what 'not installed for this account (omawin preset add wsl)' }
}
$font = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts', 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.PSObject.Properties.Name } | Where-Object { $_ -like 'JetBrainsMono*' })
if ($font) { Pass 'JetBrainsMono Nerd Font' "$(@($font).Count) faces" } else { Fail 'JetBrainsMono Nerd Font' 'not installed' }

# --- links --------------------------------------------------------------------------------
Section 'config links'
# omawin.exe, the Terminal profiles and the menu app look for the repo at ~/omawin.
if ($Repo -eq "$HOME\omawin") { Pass 'repo at ~/omawin' $Repo } else { Fail 'repo at ~/omawin' "it's at $Repo; omawin.exe and the Terminal profiles expect ~/omawin" }
Test-Link "$env:LOCALAPPDATA\nvim" "$Repo\config\nvim" 'Neovim'
Test-Link "$env:APPDATA\neovide" "$Repo\config\neovide" 'Neovide'
# Documents\PowerShell is a folder of its own whose profile stubs load the repo's (install/shell.ps1).
$psDir = "$([Environment]::GetFolderPath('MyDocuments'))\PowerShell"
$psItem = Get-Item $psDir -Force -ErrorAction SilentlyContinue
if ($psItem -and $psItem.LinkType) { Fail 'PowerShell profile' "$psDir is still a link into the repo (omawin migrate)" }
elseif ((Test-Path "$psDir\Microsoft.PowerShell_profile.ps1") -and (Get-Content "$psDir\Microsoft.PowerShell_profile.ps1" -Raw) -match [regex]::Escape("$Repo\config\powershell\Microsoft.PowerShell_profile.ps1")) { Pass 'PowerShell profile' "$psDir loads config/powershell/" }
else { Fail 'PowerShell profile' "$psDir\Microsoft.PowerShell_profile.ps1 doesn't load the repo's (install.ps1 -Step shell)" }
# Files apps write live outside the repo: Neovim's plugin lockfile (config/nvim/lua/config/lazy.lua).
if (Test-Path "$env:LOCALAPPDATA\nvim-data\lazy-lock.json") { Pass 'Neovim lockfile' "$env:LOCALAPPDATA\nvim-data\lazy-lock.json" }
else { Warn 'Neovim lockfile' 'not in nvim-data yet (the first nvim start seeds it)' }
# LazyVim's extras: each of config/nvim/lazyvim.json's is turned on in nvim-data's copy once, at the next
# nvim start (config/nvim/lua/config/lazy.lua), and noted as offered; one you then turned off is your pick.
# Warn about any of omawin's that's off and was never offered.
$extrasLive = "$env:LOCALAPPDATA\nvim-data\lazyvim.json"
if (Test-Path $extrasLive) {
    $have = @((Get-Content $extrasLive -Raw | ConvertFrom-Json).extras)
    $offered = @(Get-Content "$env:LOCALAPPDATA\nvim-data\omawin-extras-offered.txt" -ErrorAction SilentlyContinue)
    $missing = @((Get-Content "$Repo\config\nvim\lazyvim.json" -Raw | ConvertFrom-Json).extras | Where-Object { $have -notcontains $_ -and $offered -notcontains $_ } | ForEach-Object { $_ -replace '^lazyvim\.plugins\.extras\.', '' })
    if ($missing.Count) { Warn 'Neovim extras' "omawin's $($missing -join ', ') not on yet in $extrasLive (start nvim once)" }
    else { Pass 'Neovim extras' "$($have.Count) on" }
}
# GlazeWM's config and Windows Terminal's scheme: rendered from their templates (themes/render.ps1).
$glazeLive = "$HOME\.glzr\glazewm\config.yaml"
if (-not (Test-Path $glazeLive)) { Fail 'GlazeWM config' 'not rendered (omawin restart glazewm)' }
elseif ((Get-Content $glazeLive -Raw) -match '\{\{') { Fail 'GlazeWM config' "$glazeLive still has {{ placeholders }}" }
elseif ((Get-Item $glazeLive).LastWriteTime -lt (Get-Item "$Repo\config\glazewm\config.yaml").LastWriteTime) { Warn 'GlazeWM config' 'older than config/glazewm/config.yaml (omawin restart glazewm)' }
elseif ((Test-Path "$UserLayer\glazewm\config.yaml") -and (Get-Item $glazeLive).LastWriteTime -lt (Get-Item "$UserLayer\glazewm\config.yaml").LastWriteTime) { Warn 'GlazeWM config' "older than $UserLayer\glazewm\config.yaml (omawin restart glazewm)" }
else { Pass 'GlazeWM config' "$glazeLive (rendered)" }
if (Test-Path "$env:LOCALAPPDATA\Microsoft\Windows Terminal\Fragments\omawin\theme.json") { Pass 'Windows Terminal scheme' 'omawin (rendered)' }
else { Fail 'Windows Terminal scheme' 'not rendered (omawin restart glazewm)' }
Test-Link "$HOME\.config\fastfetch" "$Repo\config\fastfetch" 'fastfetch'
$nocttyStub = "$env:LOCALAPPDATA\noctty\config.ghostty"
if ((Test-Path $nocttyStub) -and (Get-Content $nocttyStub -Raw) -match [regex]::Escape("$Repo\config\noctty\config.ghostty")) { Pass 'noctty config' "$nocttyStub -> config/noctty/config.ghostty" }
else { Fail 'noctty config' "$nocttyStub doesn't include config/noctty/config.ghostty (install.ps1 -Step shell)" }
if (Test-Path "$env:LOCALAPPDATA\Programs\noctty\noctty.exe") { Pass 'noctty' "$env:LOCALAPPDATA\Programs\noctty" } else { Warn 'noctty' 'not installed (omawin update terminal)' }
# Defaults (omawin default): each pick is one bin/defaults.txt knows.
foreach ($file in Get-ChildItem "$env:LOCALAPPDATA\launcher\defaults" -File -ErrorAction SilentlyContinue) {
    $pick = (Get-Content $file.FullName -Raw).Trim()
    if (Select-String "$Repo\bin\defaults.txt" -Pattern "^$($file.Name) \| $([regex]::Escape($pick)) \|" -Quiet) { Pass "default $($file.Name)" $pick }
    else { Fail "default $($file.Name)" "'$pick' isn't in bin/defaults.txt (omawin default $($file.Name))" }
}
$wt = Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter 'Microsoft.WindowsTerminal_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
# Terminal's settings.json is Terminal's own, in LocalState, seeded once from config/windows-terminal/ (install/shell.ps1).
$wtMine = "$env:LOCALAPPDATA\Microsoft\Windows Terminal\settings.json"   # the tools bundle's unpackaged Terminal
if (-not $wt -and (Test-Path "$env:LOCALAPPDATA\omawin\tools\wt\WindowsTerminal.exe")) {
    if (Test-Path $wtMine) { Pass 'Windows Terminal' "$wtMine (its own)" } else { Warn 'Windows Terminal' 'no settings.json yet (install.ps1 -Step shell seeds it)' }
}
elseif (-not $wt) { Fail 'Windows Terminal' 'package folder not found' }
elseif ((Get-Item "$($wt.FullName)\LocalState" -Force -ErrorAction SilentlyContinue).LinkType) { Fail 'Windows Terminal' 'LocalState is still a link into the repo (omawin migrate)' }
elseif (Test-Path "$($wt.FullName)\LocalState\settings.json") { Pass 'Windows Terminal' "$($wt.FullName)\LocalState\settings.json (its own)" }
else { Warn 'Windows Terminal' 'no settings.json yet (install.ps1 -Step shell seeds it)' }
$starship = [Environment]::GetEnvironmentVariable('STARSHIP_CONFIG', 'User')
if ($starship -eq "$Repo\config\starship\starship.toml") { Pass 'STARSHIP_CONFIG' $starship } else { Fail 'STARSHIP_CONFIG' "is '$starship'" }
if (Get-Command git -ErrorAction SilentlyContinue) {
    $include = "$Repo\config\git\gitconfig" -replace '\\', '/'
    if (@(git config --global --get-all include.path) -contains $include) { Pass 'git include' $include } else { Fail 'git include' "~/.gitconfig doesn't include $include" }
    $gitName = git config --global --includes --get user.name
    if ($gitName) { Pass 'git identity' $gitName } else { Warn 'git identity' 'no user.name (install.ps1 -Step shell asks for it)' }
}
# ~/.wslconfig is your account folder's wsl/.wslconfig if it has one, else omawin's (install/other.ps1).
$wslSource = if (Test-Path "$UserLayer\wsl\.wslconfig") { "$UserLayer\wsl\.wslconfig" } else { "$Repo\wsl\.wslconfig" }
if ((Test-Path "$HOME\.wslconfig") -and (Get-Content "$HOME\.wslconfig" -Raw) -eq (Get-Content $wslSource -Raw)) { Pass '~/.wslconfig' $wslSource }
else { Warn '~/.wslconfig' "differs from $wslSource (run install.ps1 -LinkOnly)" }
# Your own settings (docs/work.md): the PC's shared folder, which every account reads and only its owner changes, and
# this account's. Both optional; the old ~/omawin-me and ~/omawin-work mean the move hasn't run.
$SharedLayer = & "$Repo\bin\user-layer.ps1" -Shared
if ($SharedLayer -eq (Join-Path $HOME 'omawin-me')) { Warn 'shared settings' "still in ~/omawin-me (omawin migrate moves it to $env:ProgramData\omawin\shared)" }
elseif (-not (Test-Path $SharedLayer)) { Warn 'shared settings' "no $SharedLayer (omawin shared -Init)" }
elseif ((Get-Acl $SharedLayer).Access | Where-Object { $(try { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { '' }) -in 'S-1-5-32-545', 'S-1-5-11', 'S-1-1-0' -and $_.AccessControlType -eq 'Allow' -and $_.FileSystemRights -match 'Write|Modify|FullControl|CreateFiles|AppendData' }) { Warn 'shared settings' "every account may change $($SharedLayer), and what's in it runs in every account: omawin shared -Init from its owner, $((Get-Acl $SharedLayer).Owner)" }
else {
    # Every account reads it: nothing secret or confidential belongs there.
    $risky = @(Get-ChildItem $SharedLayer -Recurse -File -Force -Include *.pbix, *.env, .credentials.json, *.pfx, *.key -ErrorAction SilentlyContinue | Where-Object FullName -notmatch '\\\.git\\')
    $oldShared = Join-Path $HOME 'omawin-me'
    $oldIsCopy = (Test-Path $oldShared) -and -not ((Get-Item $oldShared -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)
    if ($risky) { Warn 'shared settings' "every account can read $($risky[0].FullName): move it to this account's folder" }
    elseif ($oldIsCopy) { Warn 'shared settings' "an old ~/omawin-me is still here, unused: bin/move-settings.ps1 says whether it holds anything the shared folder lacks, and sets it aside once it doesn't" }
    else { Pass 'shared settings' $SharedLayer }
}
if ($UserLayer -eq (Join-Path $HOME 'omawin-work')) { Warn "this account's settings" 'still in ~/omawin-work: it moves to ~/omawin-account at the next sign-in (bin/move-settings.ps1; a running Claude Code keeps it open)' }
elseif ((Test-Path $UserLayer) -and (Test-Path "$HOME\omawin-work") -and -not ((Get-Item "$HOME\omawin-work" -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { Warn "this account's settings" "$UserLayer is used, but ~/omawin-work is a folder too: move what you need from it" }
elseif (Test-Path $UserLayer) { Pass "this account's settings" $UserLayer }
else { Pass "this account's settings" "none (optional: omawin account new)" }

# --- startup and helpers ------------------------------------------------------------------
Section 'startup and helpers'
# herdr reads its config from the repo (install/shell.ps1), and the config is valid.
$herdrConfig = [Environment]::GetEnvironmentVariable('HERDR_CONFIG_PATH', 'User')
if ($herdrConfig -ne "$Repo\config\herdr\config.toml") { Fail 'herdr config' "HERDR_CONFIG_PATH is '$herdrConfig', not this repo's config\herdr\config.toml (run install.ps1 -Step shell)" }
elseif (Get-Command herdr -ErrorAction SilentlyContinue) {
    $env:HERDR_CONFIG_PATH = $herdrConfig
    $herdrCheck = (herdr config check 2>&1 | Out-String).Trim()
    if ($herdrCheck -match 'ok') { Pass 'herdr config' $herdrConfig } else { Fail 'herdr config' $herdrCheck }
} else { Pass 'herdr config' "$herdrConfig (herdr not on PATH here)" }

# GlazeWM at sign-in: a scheduled task, started right away (install/login-start.ps1).
$task = Get-ScheduledTask -TaskName "omawin GlazeWM ($([Environment]::UserName))" -ErrorAction SilentlyContinue
if ($task -and $task.State -ne 'Disabled') { Pass 'GlazeWM at sign-in' "scheduled task omawin GlazeWM ($([Environment]::UserName))" } else { Fail 'GlazeWM at sign-in' 'no task (run install\login-start.ps1)' }
# GlazeWM itself: our fork, for this account from the tools bundle (the default: no admin), or
# signed on this PC for the whole PC (opt-in), which starts before Explorer; the official one
# waits for Explorer.
$wholePc = "$env:ProgramFiles\glzr.io\GlazeWM\glazewm.exe"
$mine = "$env:LOCALAPPDATA\omawin\tools\glazewm\glazewm.exe"
if (Test-Path $wholePc) {
    $gsig = Get-AuthenticodeSignature $wholePc -ErrorAction SilentlyContinue
    if ($gsig.Status -eq 'Valid' -and $gsig.SignerCertificate.Subject -like 'CN=omawin code signing*') { Pass 'GlazeWM build' 'our fork, signed on this PC' }
    elseif ($gsig.Status -eq 'Valid') { Warn 'GlazeWM build' 'official: waits for Explorer at sign-in (omawin glazewm admin)' }
    else { Fail 'GlazeWM build' 'glazewm.exe is not signed by a trusted certificate: GlazeWM gets no uiAccess (omawin glazewm admin, or omawin glazewm admin-off)' }
} elseif (Test-Path $mine) { Pass 'GlazeWM build' "our fork, for this account ($mine); the signed whole-PC copy, which also tiles programs running as admin, is opt-in: omawin glazewm admin" }
else { Fail 'GlazeWM build' 'not installed (install.ps1 -Step software)' }
# This account's port (bin/ports.ps1): its own once omawin's GlazeWM is in Program Files, which
# omawin.exe decides at every sign-in. A saved one that doesn't match what's installed now means
# another account changed Program Files since this one signed in.
$portsReady = $gsig -and $gsig.SignerCertificate -and $gsig.SignerCertificate.Subject -like 'CN=omawin code signing*' -and
    (Test-Path "$env:ProgramFiles\glzr.io\GlazeWM\cli\glazewm.official.exe")
$wantPorts = & "$Repo\bin\ports.ps1"
$havePort = [Environment]::GetEnvironmentVariable('GLAZEWM_IPC_PORT', 'User')
if ($portsReady -and $havePort -eq $wantPorts.GLAZEWM_IPC_PORT) { Pass 'ports' "this account's own: GlazeWM $($wantPorts.GLAZEWM_IPC_PORT)" }
elseif ($portsReady) { Warn 'ports' "still the shared 6123, though omawin's GlazeWM is in: sign out and back in (or omawin install -Step desktop)" }
elseif ($havePort) { Warn 'ports' "set to $havePort, but the official GlazeWM is back: sign out and back in" }
else { Pass 'ports' 'the official 6123 (omawin''s GlazeWM not in: one account''s desktop at a time)' }
# The menu keys come straight from GlazeWM where our fork knows emit (roadmap 1.2): then the menu
# app must be listening, or those keys do nothing until it's back. It holds this file while
# GlazeWM has accepted its subscription (shell/menu-app/Bar/GlazeWm.cs).
$glazeFeatures = @(& "$Repo\bin\glazewm-features.ps1")
if ($glazeFeatures -notcontains 'emit' -and (Test-Path $glazeLive) -and (Get-Content $glazeLive -Raw) -match "'emit ") {
    Fail 'menu keys' "GlazeWM's config uses emit, which this GlazeWM doesn't know (omawin restart glazewm writes it again)"
}
elseif ($glazeFeatures -contains 'emit') {
    $subFile = Join-Path $env:LOCALAPPDATA 'launcher\emit-subscribed'
    $subPid = 0
    if (Test-Path $subFile) { [void][int]::TryParse((Get-Content $subFile -Raw).Trim(), [ref]$subPid) }
    if ($subPid -and (Get-Process -Id $subPid -ErrorAction SilentlyContinue | Where-Object ProcessName -eq 'SystemMenu')) { Pass 'menu keys' 'straight from GlazeWM (emit): the menu app is listening' }
    elseif (Get-Process SystemMenu -ErrorAction SilentlyContinue) { Warn 'menu keys' "the menu app isn't subscribed to GlazeWM: Win+Space, Win+F and the launcher keys do nothing (omawin restart menu)" }
    else { Warn 'menu keys' "the menu app isn't running: Win+Space, Win+F and the launcher keys do nothing (omawin start)" }
}
$stuck = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3' -ErrorAction SilentlyContinue).Settings
if ($stuck -and ($stuck[8] -band 1)) { Pass 'taskbar auto-hides' } else { Warn 'taskbar auto-hides' 'off; install.ps1 turns it on' }
# No ads (bin/no-ads.ps1): Windows' suggestions off, and the PC maker's upsells gone. The OEM ones
# (Lenovo Now, McAfee) are flagged only when this PC has them at all.
$adware = Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
    Where-Object DisplayName -In 'McAfee Security Scan Plus', 'WebAdvisor by McAfee', 'Lenovo Now' | ForEach-Object DisplayName
if ($adware) { Warn 'no ads' "installed: $($adware -join ', ') (run bin/no-ads.ps1)" } else { Pass 'no ads' 'no OEM upsells installed' }
if ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -ErrorAction SilentlyContinue).SilentInstalledAppsEnabled -ne 0) {
    Warn 'Windows suggestions' 'on (run bin/no-ads.ps1 -UserOnly)'
} else { Pass 'Windows suggestions' 'off' }
$lockTask = Get-ScheduledTask -TaskName "omawin lock screen ($([Environment]::UserName))" -ErrorAction SilentlyContinue
if ($lockTask -and $lockTask.Actions[0].Arguments -match [regex]::Escape("$Repo\themes\set-lockscreen.ps1")) { Pass 'lock screen rotates on unlock' }
else { Warn 'lock screen rotates on unlock' 'no task for this account (install.ps1 makes it)' }
# Helper exes: present and built from the current source (the same list as install/helpers.ps1).
$helpers = [ordered]@{
    'omawin' = 'bin\omawin.cs'; 'voice-meter' = 'bin\voice-meter.cs'; 'omawin-path' = 'bin\path-doctor.cs'
}
# Current as install has it (bin/install-binaries.ps1): the sources' git ids it recorded match
# this checkout's. A pull gives the sources new dates, so the dates decide only without a
# record, or for a source edited here.
$recordedIds = @()
$binaries = "$env:LOCALAPPDATA\launcher\binaries.txt"
if (Test-Path $binaries) {
    $line = Get-Content $binaries | Where-Object { $_ -like "helpers`t*" } | Select-Object -First 1
    if ($line) { $recordedIds = @((($line -split "`t", 2)[1] -split '\|')[0] -split ',') }
}
foreach ($name in $helpers.Keys) {
    $exe = "$HOME\.local\bin\$name.exe"
    $src = Get-Item "$Repo\$($helpers[$name])" -ErrorAction SilentlyContinue
    $path = $helpers[$name] -replace '\\', '/'
    $id = if ($recordedIds -and -not (git -C $Repo status --porcelain -- $path 2>$null)) { git -C $Repo rev-parse "HEAD:$path" 2>$null }
    if (-not $src) { Fail "$name.exe" "its source $($helpers[$name]) is missing" }
    elseif (-not (Test-Path $exe)) { Fail "$name.exe" 'not built (run install.ps1 -LinkOnly)' }
    elseif ($id) {
        if ($recordedIds -contains $id) { Pass "$name.exe" } else { Warn "$name.exe" 'built from older source (run omawin update)' }
    }
    elseif ((Get-Item $exe).LastWriteTime -lt $src.LastWriteTime) { Warn "$name.exe" 'older than its source (run install.ps1 -LinkOnly)' }
    else { Pass "$name.exe" }
}
# omawin.exe reads its commands from ~/omawin (bin/commands.txt) at every run.
if (Get-Command omawin.exe -ErrorAction SilentlyContinue) {
    $null = omawin.exe help 2>&1
    if ($LASTEXITCODE -eq 0) { Pass 'omawin help' 'reads bin/commands.txt' } else { Fail 'omawin help' "exit $LASTEXITCODE (can't read bin/commands.txt?)" }
}
# Migrations (bin/migrate.ps1): every one in migrations/ has run on this PC.
$ran = @(Get-Content "$env:LOCALAPPDATA\launcher\migrations.txt" -ErrorAction SilentlyContinue)
$migrations = @(Get-ChildItem "$Repo\migrations" -Filter *.ps1 -ErrorAction SilentlyContinue | ForEach-Object BaseName)
$pending = @($migrations | Where-Object { $ran -notcontains $_ })
if ($pending) { Warn 'migrations' "not run yet: $($pending -join ', ') (run omawin migrate)" } else { Pass 'migrations' "all $($migrations.Count) run" }
# Native system menu (shell/menu-app/): built, and running in the background. This account's: another
# account signed in has its own.
$myMenu = Get-Process SystemMenu -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId
if (Test-Path "$Repo\shell\menu-app") {
    $menuExe = "$env:LOCALAPPDATA\system-menu\SystemMenu.exe"
    if (-not (Test-Path $menuExe)) { Fail 'system menu' 'not built (run shell\menu-app\build.ps1)' }
    elseif ($myMenu) { Pass 'system menu' $menuExe }
    else { Warn 'system menu' 'built but not running; Win+Alt+Space starts it' }
    # File search in the launcher (fff's C library, fetched by build.ps1).
    if (Test-Path "$env:LOCALAPPDATA\system-menu\fff_c.dll") { Pass 'launcher file search (fff)' }
    else { Warn 'launcher file search (fff)' 'fff_c.dll missing (run shell\menu-app\build.ps1)' }
}

# --- repo files ---------------------------------------------------------------------------
Section 'repo files'
$bad = @()
Get-ChildItem $Repo -Recurse -Include *.ps1 -File | Where-Object FullName -notmatch '\\(bin|obj|node_modules)\\' | ForEach-Object {
    $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors)
    if ($errors) { $bad += "$($_.FullName.Substring($Repo.Length + 1)): $($errors[0].Message)" }
}
if ($bad) { $bad | ForEach-Object { Fail 'PowerShell parse' $_ } } else { Pass 'PowerShell scripts parse' }
foreach ($json in 'config\windows-terminal\settings.json') {
    try { $null = Get-Content "$Repo\$json" -Raw | ConvertFrom-Json -ErrorAction Stop; Pass $json 'valid JSON' }
    catch { Fail $json "invalid JSON: $($_.Exception.Message)" }
}
# Menu rows: every row's parent exists, actions are known kinds.
$ids = @{}
$menuProblems = @()
foreach ($line in Get-Content "$Repo\default\launcher\menu.txt" -Encoding utf8) {
    if (-not $line.Trim() -or $line.TrimStart().StartsWith('#')) { continue }
    $id, $icon, $label, $action = ($line -split '\|', 4).Trim()
    if ($ids.ContainsKey($id)) { $menuProblems += "duplicate id $id" }
    $ids[$id] = $true
    if ($id.Contains('.') -and -not $ids.ContainsKey($id.Substring(0, $id.LastIndexOf('.')))) { $menuProblems += "$id has no parent row above it" }
    if (-not $label) { $menuProblems += "$id has no label" }
    if ($action -and $action.TrimStart('!').Trim() -notmatch '^(winget|choco|task|edit|run|omawin)\s|^@|^(themes|backgrounds)$') { $menuProblems += "$id has an unknown action" }
}
if ($menuProblems) { $menuProblems | ForEach-Object { Fail 'default/launcher/menu.txt' $_ } } else { Pass 'default/launcher/menu.txt' "$($ids.Count) rows" }
# omawin's commands: known action kinds, the scripts they run exist, and every
# `omawin.exe <command>` in GlazeWM's keys is one of them.
$commandNames = @()
$commandProblems = @()
foreach ($line in Get-Content "$Repo\bin\commands.txt" -Encoding utf8) {
    if (-not $line.Trim() -or $line.TrimStart().StartsWith('#')) { continue }
    $name, $summary, $action = ($line -split '\|', 3).Trim()
    $commandNames += $name
    if ($action -match '^pwsh\s+(\S+)') {
        $script = [IO.Path]::GetFullPath([IO.Path]::Combine($Repo, $Matches[1].Replace('{user}', $UserLayer)))
        if (Test-Path $script) { }
        # A script outside this repo (your account folder's, {user}/: docs/work.md) is there only on PCs that have it.
        elseif (-not $script.StartsWith("$Repo\", [StringComparison]::OrdinalIgnoreCase)) { Pass "omawin $name" "skipped ($($Matches[1]) isn't on this PC)" }
        else { $commandProblems += "$name runs $($Matches[1]), which is missing" }
    }
    elseif ($action -match '^builtin\s+(\S+)') {
        if ($Matches[1] -notin 'launch-terminal', 'launch-project', 'launch-editor', 'terminal-here', 'terminal-warm', 'start-glazewm', 'start-bar', 'update-terminal', 'default') { $commandProblems += "$name runs unknown builtin $($Matches[1])" }
    }
    elseif ($action -notmatch '^(app|run)\s') { $commandProblems += "$name has an unknown action" }
}
# (`emit omawin <command>` too: the same words, sent to the menu app instead, roadmap 1.2.)
foreach ($m in [regex]::Matches((Get-Content "$Repo\config\glazewm\config.yaml" -Raw), "(?:omawin\.exe|emit omawin) ([a-z -]+?)'")) {
    $words = $m.Groups[1].Value.Trim()
    if (-not ($commandNames | Where-Object { $words -eq $_ -or $words.StartsWith("$_ ") })) { $commandProblems += "config/glazewm/config.yaml runs unknown 'omawin $words'" }
}
if ($commandProblems) { $commandProblems | ForEach-Object { Fail 'bin/commands.txt' $_ } } else { Pass 'bin/commands.txt' "$($commandNames.Count) commands" }
# install.ps1's steps: each line of install/steps.txt has its file, and no file in install/ that
# looks like a step (it says so on its first line) is missing from the list.
$stepNames = @(Get-Content "$Repo\install\steps.txt" | Where-Object { $_ -notmatch '^\s*(#|$)' } | ForEach-Object { ($_ -split '\|')[0].Trim() })
$stepProblems = @($stepNames | Where-Object { -not (Test-Path "$Repo\install\$_.ps1") } | ForEach-Object { "$_ is listed but install/$_.ps1 is missing" })
$stepProblems += @(Get-ChildItem "$Repo\install" -Filter *.ps1 | Where-Object { (Get-Content $_.FullName -TotalCount 1) -like '# install.ps1 step*' -and $stepNames -notcontains $_.BaseName } |
    ForEach-Object { "install/$($_.Name) is a step that isn't listed, so it never runs" })
if ($stepProblems) { $stepProblems | ForEach-Object { Fail 'install/steps.txt' $_ } } else { Pass 'install/steps.txt' "$($stepNames.Count) steps" }
$dirty = @(git -C $Repo status --porcelain 2>$null)
if ($dirty) { Warn 'git' "$($dirty.Count) uncommitted change(s)" } else { Pass 'git' 'clean' }

# --- running ------------------------------------------------------------------------------
Section 'running now'
if (Get-Process glazewm -ErrorAction SilentlyContinue) { Pass 'glazewm' } else { Fail 'glazewm' 'not running' }
# The bar: the menu app's own (docs/native-bar.md), whose window is "omawin bar".
$bar = Get-Process SystemMenu -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'omawin bar' }
if ($bar) { Pass 'bar' 'the menu app''s' } else { Fail 'bar' 'not up (omawin restart menu)' }
# The launcher, file finder and system menu are one native app (shell/menu-app/), running hidden.
if ($myMenu) { Pass 'launcher / menu (Win+Space, Win+Alt+Space)' }
else { Warn 'launcher / menu (Win+Space, Win+Alt+Space)' 'not running; the first Win+Space starts it' }
# Only when it's running: with no GlazeWM to answer, `glazewm query` waits for one forever. And
# with a time limit, so a GlazeWM that's stuck shows here instead of stopping the check.
if ((Get-Command glazewm -ErrorAction SilentlyContinue) -and (Get-Process glazewm -ErrorAction SilentlyContinue)) {
    $query = Start-Job { glazewm query monitors 2>$null }
    $state = if (Wait-Job $query -Timeout 5) { Receive-Job $query | ConvertFrom-Json -ErrorAction SilentlyContinue }
    Remove-Job $query -Force
    if ($state.success) { Pass 'GlazeWM answers' "$(@($state.data.monitors).Count) monitor(s)" }
    else { Fail 'GlazeWM answers' 'no answer to glazewm query in 5 s' }
}
# The always-on desktop against its memory budget (roadmap 8K), and the drivers' pool, where a
# driver's leak shows. Only once the PC has been up 10 minutes: right after sign-in everything
# is still loading and the numbers mean nothing.
$ram = try { & "$Repo\bin\profile-ram.ps1" -Json -Quick | ConvertFrom-Json } catch { $null }
if (-not $ram) { Warn 'memory' "couldn't read it (omawin profile ram)" }
elseif ($ram.System.UptimeMinutes -lt 10) { Pass 'memory' "up $($ram.System.UptimeMinutes) min: measured after 10" }
else {
    $desktop = "omawin's desktop $($ram.System.DesktopMB) of $($ram.System.DesktopBudgetMB) MB; $([math]::Round($ram.Memory.InUseMB / 1KB, 1)) GB in use"
    if ($ram.System.DesktopMB -gt $ram.System.DesktopBudgetMB) { Warn 'memory' "$desktop (omawin profile ram says which part)" } else { Pass 'memory' $desktop }
    if ($ram.Kernel.NonPagedMB -gt 1024) { Warn 'drivers'' memory' "non-paged pool $($ram.Kernel.NonPagedMB) MB: a driver may be leaking (a driver update usually fixes it)" }
}

Write-Host ''
$summary = "$failed failed, $warned warning(s)"
if ($failed) { Write-Host "  $summary" -ForegroundColor Red; exit 1 }
Write-Host "  $summary" -ForegroundColor $(if ($warned) { 'Yellow' } else { 'Green' })
