<#
.SYNOPSIS
    omawin's presets: opt-in groups of tools on top of the core (install/packages.ps1).

.DESCRIPTION
    The presets this PC picked are kept in %LOCALAPPDATA%\launcher\presets.txt; install.ps1
    installs the core and those. Adding one installs it now; removing one only stops install.ps1
    (and omawin check) from asking for it: what it installed stays (Install > Remove removes apps).

        omawin preset list            every preset, the picked ones marked
        omawin preset add data        pick it and install it now
        omawin preset add data -NoInstall
        omawin preset remove data

    Plugins (shell/plugins/<id>, Omarchy's format) are picked the same way: a plugin's programs
    are a preset tagged with it (Plugin in install/packages.ps1), so picking the plugin picks that
    preset, and what's on is in presets.txt too. A plugin from the gallery (Omarchy's marketplace:
    %LOCALAPPDATA%\launcher\plugin-gallery\plugin-gallery.json, made by src/plugin-gallery) is
    downloaded at the commit its maintainers reviewed into %LOCALAPPDATA%\omawin\plugins\<id>
    when it's turned on, and removed again when it's turned off. Either way the bar reads
    %LOCALAPPDATA%\launcher\plugins.txt for what's on, and the QML host (src/qs-host) is installed
    with the first plugin.

        omawin plugin list            every plugin here, the ones on marked
        omawin plugin add music       (or omawin.music) turn it on and install its programs
        omawin plugin add b.omadoro   one from the gallery: downloaded, then turned on
        omawin plugin remove music
        omawin plugin gallery         the gallery page (the menu app): all of them, Enter turns one on or off
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'add', 'remove', 'plugins', 'plugin-add', 'plugin-remove', 'sync-plugins')][string]$Verb = 'list',
    [Parameter(Position = 1)][string]$Name,
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\install\packages.ps1"
. "$Repo\install\lib.ps1"

$chosen = @(Get-Presets)
$PluginsFile = Join-Path (Split-Path $PresetsFile) 'plugins.txt'
$GalleryFile = Join-Path (Split-Path $PresetsFile) 'plugin-gallery\plugin-gallery.json'
$DownloadedDir = Join-Path $env:LOCALAPPDATA 'omawin\plugins'
# A plugin id as a folder name (shell/menu-app/Plugins/GalleryIndex.cs checks the same).
$SafePluginId = '^[a-z0-9][a-z0-9_-]*(\.[a-z0-9][a-z0-9_-]*)*$'
$PresetPlugins = @($Presets.Keys | ForEach-Object { $Presets[$_].Plugin } | Where-Object { $_ })

# What's on now: plugins.txt's ids (the bar's list).
function Get-OnPlugins {
    @(Get-Content $PluginsFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -Unique)
}

# The gallery's index (a tools bundle entry; null before the first release that has it).
function Get-GalleryIndex {
    if (-not (Test-Path $GalleryFile)) { return $null }
    try { Get-Content $GalleryFile -Raw | ConvertFrom-Json } catch { Write-Warning "the plugin gallery's index can't be read ($GalleryFile): $_"; $null }
}

# The plugins that are on, for the bar (shell/menu-app/Bar/PluginSlot.cs): the ids of the picked
# presets tagged with one, then the rest that are on (from the gallery, or omawin's own without
# programs), kept as they are. The bar redraws when this file changes.
function Save-Plugins([string[]]$Names, [string[]]$Others = @(Get-OnPlugins | Where-Object { $PresetPlugins -notcontains $_ })) {
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    $ids = @($Names | Where-Object { $Presets.Contains($_) -and $Presets[$_].Plugin } | ForEach-Object { $Presets[$_].Plugin }) + @($Others)
    Set-Content $PluginsFile (@('# omawin plugins that are on (shell/plugins/<id>, or from the gallery); omawin plugin add|remove <id>') + @($ids | Select-Object -Unique)) -Encoding utf8
}

# The QML host, with the first plugin turned on (it isn't in the core install); plugins.txt
# written again after, so a bar that had nothing to draw them with draws them now.
function Install-PluginHost {
    $SoftwareFunctionsOnly = $true
    . "$Repo\install\software.ps1"
    Install-ToolBundle $null @('qs-host')
    Save-Plugins $chosen
}

# A plugin from the gallery: its folder at the reviewed commit, unless that's here already. An
# older download is replaced; a folder of that name that isn't one of omawin's downloads is moved
# aside, never removed.
function Get-GalleryPlugin($Entry) {
    if ($Entry.repo -notmatch '^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$' -or $Entry.repo -like '*..*' -or $Entry.commit -notmatch '^[0-9a-f]{40}$') {
        throw "the gallery's entry for $($Entry.id) isn't one omawin can download ($($Entry.repo) $($Entry.commit))"
    }
    $dest = Join-Path $DownloadedDir $Entry.id
    $marker = Join-Path $dest '.omawin-source'
    $source = "$($Entry.repo) $($Entry.commit)"
    if ((Test-Path $marker) -and (Get-Content $marker -Raw).Trim() -eq $source) { Write-Host "  ok  $($Entry.id) is downloaded"; return }
    $url = ($Entry.repo -replace '^https://github\.com/', 'https://codeload.github.com/') + "/zip/$($Entry.commit)"
    $work = Join-Path ([IO.Path]::GetTempPath()) "omawin-plugin-$PID-$Stamp"
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        Write-Host "  downloading $($Entry.id) from $($Entry.repo) ($($Entry.commit.Substring(0, 7)))"
        Invoke-WebRequest $url -OutFile (Join-Path $work 'plugin.zip') -UseBasicParsing
        Expand-Archive (Join-Path $work 'plugin.zip') (Join-Path $work 'x')
        $top = @(Get-ChildItem (Join-Path $work 'x') -Directory)
        if ($top.Count -ne 1 -or -not (Test-Path (Join-Path $top[0].FullName 'manifest.json'))) { throw "no manifest.json at the top of $($Entry.repo)" }
        $m = Get-Content (Join-Path $top[0].FullName 'manifest.json') -Raw | ConvertFrom-Json
        if ($m.id -ne $Entry.id) { throw "$($Entry.repo) is the plugin '$($m.id)', not '$($Entry.id)'" }
        Set-Content (Join-Path $top[0].FullName '.omawin-source') $source -Encoding utf8
        New-Item -ItemType Directory -Force $DownloadedDir | Out-Null
        if (Test-Path $marker) { Remove-Item -Recurse -Force $dest }
        elseif (Test-Path $dest) { Move-Aside $dest }
        Move-Item $top[0].FullName $dest
    } finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

# omawin plugin add <id>, for one without a preset: one of omawin's own, or one from the gallery
# (downloaded first). One the host can't run yet is refused, with what it needs.
function Add-Plugin([string]$Id) {
    if (-not (Test-Path "$Repo\shell\plugins\$Id\manifest.json")) {
        $entry = @((Get-GalleryIndex).plugins | Where-Object { $_.id -eq $Id }) | Select-Object -First 1
        if ($entry) {
            if ($entry.verdict -eq 'not-yet') { throw "$Id doesn't run in omawin yet: it needs $(@($entry.needs) -join ', ')" }
            Write-Host "`n  $($entry.name) by $($entry.author): $($entry.description)" -ForegroundColor Cyan
            Write-Host "  Its code comes from $($entry.repo), at the commit Omarchy's maintainers reviewed; it runs as you, unsandboxed." -ForegroundColor DarkGray
            if ($entry.verdict -eq 'partly') { Write-Host "  It calls Linux programs Windows hasn't got ($(@($entry.commands) -join ', ')): those parts won't work." -ForegroundColor Yellow }
            Get-GalleryPlugin $entry
        } elseif (-not (Test-Path (Join-Path $DownloadedDir "$Id\manifest.json"))) {
            throw "no plugin '$Id' here or in the gallery (omawin plugin list, omawin plugin gallery)"
        }
    }
    $on = @(Get-OnPlugins)
    if ($on -contains $Id) { Write-Host "  ok  $Id is on" }
    else { Save-Plugins $chosen (@($on | Where-Object { $PresetPlugins -notcontains $_ }) + $Id); Write-Host "  turned $Id on: it's in the bar on every monitor" }
    Install-PluginHost
}

# omawin plugin remove <id>, for one without a preset: off, and a gallery download removed (what
# omawin downloaded, marked by .omawin-source; anything else in that folder is left).
function Remove-Plugin([string]$Id) {
    $on = @(Get-OnPlugins)
    if ($on -contains $Id) { Save-Plugins $chosen @($on | Where-Object { $_ -ne $Id -and $PresetPlugins -notcontains $_ }); Write-Host "  turned $Id off" }
    else { Write-Host "  ok  $Id isn't on" }
    $dest = Join-Path $DownloadedDir $Id
    if (Test-Path (Join-Path $dest '.omawin-source')) { Remove-Item -Recurse -Force $dest; Write-Host "  removed its download ($dest)" }
}

# --- plugins: the presets tagged with one, by the plugin's id or its last part ---
if ($Verb -eq 'plugins') {
    $on = @(Get-OnPlugins)
    foreach ($dir in @(Get-ChildItem "$Repo\shell\plugins" -Directory -ErrorAction SilentlyContinue) + @(Get-ChildItem $DownloadedDir -Directory -ErrorAction SilentlyContinue)) {
        $m = Get-Content (Join-Path $dir.FullName 'manifest.json') -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue
        if (-not $m) { continue }
        $mark = if ($on -contains $m.id) { '*' } else { ' ' }
        Write-Host ("  {0} {1,-20} {2}" -f $mark, $m.id, $m.description)
    }
    $index = Get-GalleryIndex
    $more = if ($index) { "; $(@($index.plugins).Count) more in the gallery: omawin plugin gallery" } else { '' }
    Write-Host "`n  * on: in the bar on every monitor. omawin plugin add|remove <id>$more" -ForegroundColor DarkGray
    return
}
if ($Verb -in 'plugin-add', 'plugin-remove') {
    if (-not $Name) { throw "omawin plugin $($Verb -replace '^plugin-') <id>; omawin plugin list, or omawin plugin gallery" }
    $wanted = if ($Name -notlike '*.*') { "omawin.$Name" } else { $Name }
    $preset = @($Presets.Keys | Where-Object { $Presets[$_].Plugin -and $Presets[$_].Plugin -eq $wanted }) | Select-Object -First 1
    if ($preset) {
        $Verb = $Verb -replace '^plugin-'
        $Name = $preset
    } else {
        # Not one with programs of its own: one of omawin's without any, or one from the gallery.
        $id = if (Test-Path "$Repo\shell\plugins\$wanted\manifest.json") { $wanted } else { $Name }
        if ($id -cnotmatch $SafePluginId) { throw "no plugin '$Name' (omawin plugin list, omawin plugin gallery)" }
        if ($Verb -eq 'plugin-remove') { Remove-Plugin $id } else { Add-Plugin $id }
        return
    }
}
function Save-Presets([string[]]$Names) {
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    Set-Content $PresetsFile (@('# omawin presets on this PC (install/packages.ps1); omawin preset add|remove <name>') + $Names) -Encoding utf8
    Save-Plugins $Names
}

# plugins.txt written again from presets.txt (a migration: PCs that picked a plugin's preset first).
if ($Verb -eq 'sync-plugins') { Save-Plugins $chosen; return }

if ($Verb -eq 'list') {
    foreach ($preset in @($Presets.Keys | Where-Object { -not $Presets[$_].Plugin })) {
        $mark = if ($chosen -contains $preset) { '*' } else { ' ' }
        Write-Host ("  {0} {1,-15} {2}" -f $mark, $preset, $Presets[$preset].Summary)
    }
    Write-Host "`n  * picked on this PC ($PresetsFile). omawin preset add|remove <name>; plugins: omawin plugin list" -ForegroundColor DarkGray
    return
}

if (-not $Name) { throw "omawin preset $Verb <name>; the presets: $($Presets.Keys -join ', ')" }
if (-not $Presets.Contains($Name)) { throw "no preset '$Name'; the presets: $($Presets.Keys -join ', ')" }

if ($Verb -eq 'remove') {
    if ($chosen -notcontains $Name) { Write-Host "  ok  $Name isn't picked"; return }
    Save-Presets @($chosen | Where-Object { $_ -ne $Name })
    Write-Host "  removed $Name from the presets (what it installed stays)"
    return
}

if ($chosen -notcontains $Name) { Save-Presets (@($chosen) + $Name); Write-Host "  added $Name to the presets" }
else { Write-Host "  ok  $Name is picked" }
if ($NoInstall) { return }
$SoftwareFunctionsOnly = $true
. "$Repo\install\software.ps1"
Write-Step "preset $Name"
Install-WingetPackages @($Presets[$Name].Winget | Where-Object { $_ })

# WSL itself, for the wsl preset, when the PC hasn't got it: one admin prompt (the install's admin
# step with WSL alone, install/admin-batch.ps1), then the Linux, which needs none. Just put in,
# WSL works after a restart, and the Linux finishes at the next sign-in (wsl/install.ps1).
$WslRestart = $false
function Get-WslRestart { $script:WslRestart }
if ($Name -eq 'wsl' -and -not (Test-WslInstalled)) {
    Write-Note 'WSL takes admin: Windows asks once'
    $status = Join-Path $env:TEMP "omawin-admin-$Stamp.txt"
    Set-Content $status '' -NoNewline
    try {
        $null = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\install\admin-batch.ps1`"", '-Status', "`"$status`"", '-Wsl'
    } catch { throw "WSL takes admin, and the prompt was declined (omawin preset add wsl asks again)" }
    $f = @("$(Get-Content $status | Where-Object { $_ -like "wsl`t*" } | Select-Object -Last 1)" -split "`t")
    Remove-Item $status -ErrorAction SilentlyContinue
    if ($f[0] -ne 'wsl' -or $f[1] -ne 'done') { throw "WSL didn't install$(if ($f.Count -gt 2 -and $f[2]) { ": $($f[2])" })" }
    $WslRestart = $f.Count -gt 2 -and $f[2] -eq 'restart'
}
Install-Extras @($Presets[$Name].Extras | Where-Object { $_ })
# A plugin's QML runs in omawin's host (src/qs-host): from the release's tools bundle, with the
# first plugin turned on (it isn't in the core install).
if ($Presets[$Name].Plugin) { Install-ToolBundle $null @('qs-host'); Save-Plugins (Get-Presets) }
