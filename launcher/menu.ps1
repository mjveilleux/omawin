# The system menu's jobs. The menu itself is native (menu-app/, Win+Alt+Space): it reads
# menu.txt and does its run, omawin, edit, @page and themes rows itself. This does the rest:
#   menu.ps1 -Run <id>          a task, install or removal row, in a "menu-task" window
#   menu.ps1 -RefreshInstalled  rebuild installed.txt (the app runs this in the background)
# and, inside a menu-task window (menu.txt rows start them), two jobs that need more than a line:
#   menu.ps1 -SetWallpaper      pick an image and make it the wallpaper
#   menu.ps1 -SearchPackage     search winget for anything and install it
#   menu.ps1 -Install "<source>:<id>;..."   the launcher's Packages page: install those, one
#                                after another (sources: winget, choco, scoop)
#   menu.ps1 -InstallManager winget|choco|scoop   the manager itself, when it isn't here
#
# Install rows are winget (or Chocolatey) packages. Which ones this PC already has comes from
# a list refreshed in the background (`winget export` takes ~20s, too slow to run
# while the menu is open), kept in %LOCALAPPDATA%\launcher\installed.txt and updated after
# each install or removal. Those rows show a ✓, and Remove lists exactly them.
#
# Longer jobs (installs, updates) run in their own small terminal window titled "menu-task",
# so the menu is free again right away and you can watch them.
param([string]$Run, [switch]$RefreshInstalled, [switch]$SetWallpaper, [switch]$SearchPackage, [string]$Install,
    [ValidateSet('', 'winget', 'choco', 'scoop')][string]$InstallManager = '')

$MenuFile = Join-Path $PSScriptRoot 'menu.txt'
$InstalledFile = Join-Path $env:LOCALAPPDATA 'launcher\installed.txt'
$TaskDir = Join-Path $env:LOCALAPPDATA 'launcher\tasks'

# --- what's installed ---------------------------------------------------------------------
# installed.txt: one "winget:<id>", "choco:<id>" or "scoop:<app>" per line, lowercase.
function Update-InstalledList {
    $json = Join-Path ([IO.Path]::GetTempPath()) "launcher-winget-$PID.json"
    winget export -o $json --accept-source-agreements *> $null
    if (-not (Test-Path $json)) { return }
    $ids = @((Get-Content $json -Raw | ConvertFrom-Json).Sources.Packages.PackageIdentifier | ForEach-Object { "winget:$_".ToLower() })
    Remove-Item $json -ErrorAction SilentlyContinue
    if (Get-Command choco -ErrorAction SilentlyContinue) {
        $ids += @(choco list --limit-output 2>$null | ForEach-Object { 'choco:' + ($_ -split '\|')[0].ToLower() })
    }
    # Scoop's apps are its folders (scoop list is slow and prints a table).
    $scoopApps = Join-Path $(if ($env:SCOOP) { $env:SCOOP } else { Join-Path $HOME 'scoop' }) 'apps'
    if (Test-Path $scoopApps) {
        $ids += @(Get-ChildItem $scoopApps -Directory | Where-Object Name -ne 'scoop' | ForEach-Object { "scoop:$($_.Name.ToLower())" })
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $InstalledFile) | Out-Null
    Set-Content $InstalledFile $ids -Encoding utf8
}

function Get-InstalledList {
    $set = [Collections.Generic.HashSet[string]]::new()
    if (Test-Path $InstalledFile) { foreach ($line in Get-Content $InstalledFile) { $null = $set.Add($line.Trim()) } }
    , $set
}

# --- the rows -----------------------------------------------------------------------------
# The same rows as the app's MenuModel.Load (menu-app/MenuModel.cs), so -Run finds remove.* too.
function Get-MenuEntries {
    $entries = [ordered]@{}
    foreach ($line in Get-Content $MenuFile -Encoding utf8) {
        if (-not $line.Trim() -or $line.TrimStart().StartsWith('#')) { continue }
        $id, $icon, $label, $action = ($line -split '\|', 4).Trim()
        $confirm = $action -and $action.StartsWith('!')
        if ($confirm) { $action = $action.Substring(1).Trim() }
        $package = if ($action -match '^(winget|choco)\s+(\S+)$') { "$($Matches[1]):$($Matches[2])".ToLower() }
        $entries[$id] = [pscustomobject]@{ Id = $id; Icon = $icon; Label = $label; Action = $action; Confirm = $confirm; Package = $package }
    }
    # Remove: every installed package from Install, under the same submenus.
    $installed = Get-InstalledList
    foreach ($e in @($entries.Values)) {
        if (-not $e.Id.StartsWith('install.') -or -not $e.Package -or -not $installed.Contains($e.Package)) { continue }
        $parts = $e.Id.Split('.')
        for ($i = 2; $i -lt $parts.Count; $i++) {
            # The submenus on the way down (remove.development for install.development.rust).
            $from = $entries[($parts[0..($i - 1)] -join '.')]
            $to = 'remove.' + ($parts[1..($i - 1)] -join '.')
            if (-not $entries.Contains($to)) {
                $entries[$to] = [pscustomobject]@{ Id = $to; Icon = $from.Icon; Label = $from.Label; Action = $null; Confirm = $false; Package = $null }
            }
        }
        $kind, $packageId = $e.Action -split '\s+', 2
        $entries['remove.' + ($parts[1..($parts.Count - 1)] -join '.')] = [pscustomobject]@{
            Id = 'remove.' + ($parts[1..($parts.Count - 1)] -join '.'); Icon = $e.Icon; Label = $e.Label
            Action = "un$kind $packageId"; Confirm = $true; Package = $null
        }
    }
    $entries
}

# The kinds the app hands over (the rest it does itself, see the top).
function Invoke-MenuAction($Entry) {
    $kind, $rest = $Entry.Action -split '\s+', 2
    # The generated scripts quote the path in '...': a ' in it (a user name like O'Brien) doubled.
    $installed = $InstalledFile.Replace("'", "''")
    switch ($kind) {
        'task' { Start-MenuTask $Entry.Label $rest }
        'winget' {
            Start-MenuTask "Install $($Entry.Label)" @"
winget install --id $rest --exact --accept-source-agreements --accept-package-agreements
if (`$LASTEXITCODE -eq 0) { Add-Content '$installed' 'winget:$($rest.ToLower())' }
"@
        }
        'unwinget' {
            Start-MenuTask "Remove $($Entry.Label)" @"
winget uninstall --id $rest --exact
if (`$LASTEXITCODE -eq 0) { (Get-Content '$installed') -ne 'winget:$($rest.ToLower())' | Set-Content '$installed' }
"@
        }
        # Chocolatey needs admin: Windows asks first.
        'choco' {
            Start-MenuTask "Install $($Entry.Label)" @"
Start-Process choco -Verb RunAs -Wait -ArgumentList 'install', '$rest', '-y'
if (choco list --limit-output --exact '$rest') { Add-Content '$installed' 'choco:$($rest.ToLower())' }
"@
        }
        'unchoco' {
            Start-MenuTask "Remove $($Entry.Label)" @"
Start-Process choco -Verb RunAs -Wait -ArgumentList 'uninstall', '$rest', '-y'
if (-not (choco list --limit-output --exact '$rest')) { (Get-Content '$installed') -ne 'choco:$($rest.ToLower())' | Set-Content '$installed' }
"@
        }
    }
}

# Run PowerShell in a new small terminal window, in the default terminal (GlazeWM floats
# windows titled "menu-task"), which stays open at the end until a key is pressed.
function Start-MenuTask([string]$Title, [string]$Script) {
    New-Item -ItemType Directory -Force -Path $TaskDir | Out-Null
    Get-ChildItem $TaskDir -Filter '*.ps1' | Where-Object LastWriteTime -lt (Get-Date).AddDays(-1) | Remove-Item -ErrorAction SilentlyContinue
    $file = Join-Path $TaskDir "$(Get-Date -Format 'yyyyMMdd-HHmmss').ps1"
    Set-Content $file -Encoding utf8 @"
`$Host.UI.RawUI.WindowTitle = 'menu-task'
Write-Host "``n  $($Title.Replace('"', '`"'))``n" -ForegroundColor Cyan
try {
$Script
} catch { Write-Host "`$_" -ForegroundColor Red }
Write-Host "``n  done · press any key to close" -ForegroundColor DarkGray
`$null = [Console]::ReadKey(`$true)
"@
    & omawin.exe launch terminal --title menu-task -- pwsh -NoLogo -File $file
}

# --- the Packages page's installs ------------------------------------------------------------
# What each manager's ids look like (the app checks the same): nothing else reaches a command.
$SafeId = '^[A-Za-z0-9][A-Za-z0-9._+-]*(/[A-Za-z0-9][A-Za-z0-9._+-]*)?$'

function Get-InstallScript([string]$Source, [string]$Id) {
    $installed = $InstalledFile.Replace("'", "''")
    switch ($Source) {
        'winget' { @"
Write-Host '  winget  $Id' -ForegroundColor Cyan
winget install --id $Id --exact --accept-source-agreements --accept-package-agreements
if (`$LASTEXITCODE -eq 0) { Add-Content '$installed' 'winget:$($Id.ToLower())' }
"@ }
        # Chocolatey needs admin: Windows asks first.
        'choco' { @"
Write-Host '  Chocolatey  $Id' -ForegroundColor Cyan
Start-Process choco -Verb RunAs -Wait -ArgumentList 'install', '$Id', '-y'
if (choco list --limit-output --exact '$Id') { Add-Content '$installed' 'choco:$($Id.ToLower())' }
"@ }
        # Scoop: <bucket>/<app>, into your user folder, no admin.
        'scoop' {
            $app = ($Id -split '/')[-1]
            @"
Write-Host '  Scoop  $Id' -ForegroundColor Cyan
scoop install $Id
if (`$LASTEXITCODE -eq 0) { Add-Content '$installed' 'scoop:$($app.ToLower())' }
"@ }
    }
}

if ($Install) {
    $scripts = foreach ($key in $Install -split ';') {
        $source, $id = $key -split ':', 2
        if ($source -notin 'winget', 'choco', 'scoop' -or $id -notmatch $SafeId) { Write-Warning "skipped $key"; continue }
        Get-InstallScript $source $id
    }
    $count = @($scripts).Count
    if ($count) { Start-MenuTask $(if ($count -eq 1) { "Install $(($Install -split ':', 2)[1])" } else { "Install $count packages" }) ($scripts -join "`n") }
}

if ($InstallManager -eq 'winget') {
    # winget is Microsoft's App Installer. Most often it's on the PC but not yet registered for
    # this account (a new account, before the Store has run): registering it is enough. Else
    # Microsoft's WinGet module puts in the latest, with what it needs.
    Start-MenuTask 'Install winget' @'
Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction SilentlyContinue
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Install-Module Microsoft.WinGet.Client -Scope CurrentUser -Force -AllowClobber
    Repair-WinGetPackageManager -Latest -Force
}
if (Get-Command winget -ErrorAction SilentlyContinue) {
    Write-Host '  Open the launcher''s Packages page again: winget is there now.' -ForegroundColor DarkGray
} else { Write-Host '  winget is still missing: install App Installer from the Microsoft Store.' -ForegroundColor Yellow }
'@
}
if ($InstallManager -eq 'choco') {
    # From winget when it's here; else Chocolatey's own installer (chocolatey.org), as admin.
    Start-MenuTask 'Install Chocolatey' @'
if (Get-Command winget -ErrorAction SilentlyContinue) {
    winget install --id Chocolatey.Chocolatey --exact --accept-source-agreements --accept-package-agreements
} else {
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
        '[Net.ServicePointManager]::SecurityProtocol = 3072; Invoke-Expression ((New-Object Net.WebClient).DownloadString(''https://community.chocolatey.org/install.ps1''))'
}
Write-Host '  Open the launcher''s Packages page again: Chocolatey is there now.' -ForegroundColor DarkGray
'@
}
if ($InstallManager -eq 'scoop') {
    # Scoop's own installer (scoop.sh): into ~\scoop, no admin. Then its extras bucket, where
    # most desktop programs are.
    Start-MenuTask 'Install Scoop' @"
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression
`$env:PATH = "`$HOME\scoop\shims;`$env:PATH"
scoop install git
scoop bucket add extras
Write-Host '  Open the launcher''s Packages page again: Scoop is there now.' -ForegroundColor DarkGray
"@
}

# --- run on its own ------------------------------------------------------------------------
if ($RefreshInstalled) { Update-InstalledList }
if ($Run) {
    $entry = (Get-MenuEntries)[$Run]
    if ($entry) { Invoke-MenuAction $entry }
}

# --- jobs run from a menu-task window ------------------------------------------------------
if ($SetWallpaper) {
    . (Join-Path $PSScriptRoot 'common.ps1')
    # Your pictures, and the themes' backgrounds downloaded so far (set-theme.ps1 keeps them).
    $folders = [Environment]::GetFolderPath('MyPictures'), "$HOME\OneDrive\Pictures", "$HOME\Downloads", "$env:LOCALAPPDATA\launcher\backgrounds"
    $images = @($folders | Where-Object { Test-Path $_ } | Select-Object -Unique | ForEach-Object {
            Get-ChildItem $_ -Recurse -Depth 2 -File -Include *.jpg, *.jpeg, *.png, *.bmp, *.webp -ErrorAction SilentlyContinue
        } | Sort-Object LastWriteTime -Descending | ForEach-Object { "$($_.FullName -replace [regex]::Escape($HOME), '~')`t$($_.FullName)" })
    $image = Select-Item $images -Label '󰸉 wallpaper' -ListLabel 'enter: set · esc: cancel'
    if ($image) {
        Add-Type -Namespace Menu -Name Desktop -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);'
        Set-ItemProperty 'HKCU:\Control Panel\Desktop' WallpaperStyle '10' # fill
        Set-ItemProperty 'HKCU:\Control Panel\Desktop' TileWallpaper '0'
        $null = [Menu.Desktop]::SystemParametersInfo(20, 0, $image, 3)
        Write-Host "  wallpaper: $image"
    }
}

if ($SearchPackage) {
    $query = Read-Host '  search winget for'
    if (-not $query) { return }
    # winget prints a table; the Id column starts where its header does.
    $out = @(winget search $query --accept-source-agreements | ForEach-Object { "$_" })
    $head = [array]::FindIndex([string[]]$out, [Predicate[string]] { param($l) $l -match '^Name\s+Id\s' })
    if ($head -lt 0) { Write-Host '  nothing found'; return }
    $idAt = $out[$head].IndexOf(' Id ') + 1
    $versionAt = $out[$head].IndexOf(' Version ') + 1
    $rows = $out | Select-Object -Skip ($head + 2) | Where-Object { $_.Length -gt $versionAt } | ForEach-Object {
        "$_`t$($_.Substring($idAt, $versionAt - $idAt).Trim())"
    }
    . (Join-Path $PSScriptRoot 'common.ps1')
    $id = Select-Item @($rows) -Label "󰏓 $query" -ListLabel 'enter: install · esc: cancel'
    if ($id) {
        winget install --id $id --exact --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -eq 0) { Add-Content $InstalledFile "winget:$($id.ToLower())" }
    }
}

