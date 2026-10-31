<#
.SYNOPSIS
    Themes beyond Omarchy's own, like Omarchy's `omarchy theme install`: by name from the
    community index, or from any git link.

.DESCRIPTION
    An installed theme is a git clone in %LOCALAPPDATA%\launcher\themes\sources\<name>, turned
    into omawin's theme files beside it (%LOCALAPPDATA%\launcher\themes, the same layout as this
    repo's themes/; convert-theme.ps1). The menu, the launcher, Neovim and set-theme.ps1 look
    there first, so an installed theme with the name of one of Omarchy's replaces it, as in
    Omarchy. Like Omarchy, nothing that would run code is taken from an installed theme (its
    Neovim Lua, its VS Code extension); see convert-theme.ps1.

    The community index (%LOCALAPPDATA%\launcher\community\index.json: every theme
    omarchy.org/themes lists, read by CI at a pinned commit, src/community-themes; the menu app
    fetches it a minute after it starts, then daily) gives each theme a name, its palette and
    the commit to install.

        .\theme.ps1 add <name>           a community theme by name, at the commit the index read
        .\theme.ps1 add <git link>       any theme from its repo (asks for a name or link if left out)
        .\theme.ps1 install ...          the same as add (the older name)
        .\theme.ps1 search <words>       community themes matching every word (name, author, light,
                                         dark), with their repos
        .\theme.ps1 preview <name>       a community theme across the desktop for a moment, nothing
                                         installed or remembered; `preview off` puts yours back
        .\theme.ps1 update [name]        every installed theme (or one): shows what changed in its
                                         repo, asks (in a terminal; -Yes doesn't), updates, converts
                                         again, and applies the current one again if it changed
        .\theme.ps1 remove [name]        remove one (asks which if left out)
        .\theme.ps1 list                 every theme: Omarchy's (built in) and installed ones, with
                                         where each installed one came from
        .\theme.ps1 browse               Omarchy's page of community themes

    `omawin theme add|install|search|preview|update|remove|list|browse` run this. A link is the
    theme's repo, as https://github.com/<someone>/omarchy-<name>-theme or git@github.com:<someone>/...;
    the theme's name comes from it, without "omarchy-" and "-theme".
#>
param(
    [Parameter(Position = 0)][ValidateSet('add', 'install', 'search', 'preview', 'update', 'remove', 'list', 'browse')][string]$Action = 'list',
    [Parameter(Position = 1)][string]$Target,
    [Parameter(ValueFromRemainingArguments)][string[]]$More,
    [switch]$Yes
)
$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:LOCALAPPDATA 'launcher\themes'
$Sources = Join-Path $Root 'sources'
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$CatalogUrl = 'https://omarchy.org/themes/'
# A community theme shown for a moment (preview): its name, written by the menu app as you move
# across the picker (or by `preview <name>`), and the theme files made for it, kept apart from
# installed themes so it never shows up as one.
$PreviewFile = Join-Path $State 'theme-preview'
$PreviewDir = Join-Path $State 'community-preview'

function Get-CurrentTheme {
    $name = Get-Content "$State\theme" -Raw -ErrorAction SilentlyContinue
    if ($name) { $name.Trim() } else { 'starship' }
}

function Get-InstalledThemes {
    @(Get-ChildItem $Sources -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path "$($_.FullName)\.git" } | ForEach-Object Name)
}

# The theme's name from its link, the way Omarchy names it, held to the characters a theme
# name needs (it becomes a folder name and an argument): $null if it gives none. The community
# index names its themes with the same rule (shell/menu-app/Community.cs, CommunityList.ThemeName).
function Get-ThemeName([string]$Url) {
    $path = $Url.TrimEnd('/')
    # scp-style git@host:path: what's after the colon.
    if ($path -notmatch '://' -and $path -match '^[^/]*:') { $path = $path.Substring($path.IndexOf(':') + 1) }
    $base = ($path -split '[/\\]')[-1] -replace '\.git$', ''
    $name = ($base -replace '^omarchy-', '' -replace '-theme$', '').ToLowerInvariant()
    if ($name -cmatch '^[a-z0-9_][a-z0-9._+-]*$' -and $name -notmatch '\.\.') { $name } else { $null }
}

# A link git fetches over the network, and nothing else: no options (a leading -), no local
# paths, no transport helpers (ext::, fd::).
function Test-GitUrl([string]$Url) {
    $Url -match '^(https://|http://|ssh://|git://)[^\s]+$' -or $Url -match '^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s:]+$'
}

# Turns an installed theme's clone into theme files. $true if it worked.
function Convert-Installed([string]$Name) {
    try {
        & "$PSScriptRoot\convert-theme.ps1" -Source (Join-Path $Sources $Name) -Name $Name -Out $Root -Untrusted
        $true
    } catch {
        Write-Host "  $Name`: $($_.Exception.Message)" -ForegroundColor Red
        $false
    }
}

# Removes an installed theme's files (not its clone).
function Remove-ThemeFiles([string]$Name) {
    foreach ($file in "$Name.json", "colors\$Name.toml", "nvim\$Name.lua", "btop\$Name.theme",
        "vscode\$Name.json", "vscode\$Name.color-theme.json") {
        Remove-Item (Join-Path $Root $file) -ErrorAction SilentlyContinue
    }
}

# --- the community index ------------------------------------------------------------------------
function Get-CommunityIndex {
    $file = Join-Path $State 'community\index.json'
    if (-not (Test-Path $file)) { throw "No community theme index yet: the menu app fetches it a minute after it starts (it needs the network). Or install from a link: omawin theme add <git link>." }
    Get-Content $file -Raw | ConvertFrom-Json
}

function Get-CommunityTheme([string]$Name) {
    $t = @((Get-CommunityIndex).themes | Where-Object { $_.name -eq $Name -or $_.title -eq $Name }) | Select-Object -First 1
    if (-not $t) { throw "No community theme '$Name' (omawin theme search <words>)." }
    $t
}

# Every word in its name, title, author or repo, or "light" / "dark" for its mode: as the
# picker searches (CommunityList.Matches).
function Test-CommunityMatch($Theme, [string[]]$Words) {
    $text = "$($Theme.name) $($Theme.title) $($Theme.author) $($Theme.repo)"
    @($Words | Where-Object { $text.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0 -and $Theme.mode -ne $_ }).Count -eq 0
}

# --- installing ---------------------------------------------------------------------------------
# Clones a theme's repo (at a commit, when the community index names one), converts it and
# applies it.
function Install-Theme([string]$Url, [string]$Commit) {
    if (-not (Test-GitUrl $Url)) { throw "'$Url' isn't a git link (https://... or git@host:...)." }
    $name = Get-ThemeName $Url
    if (-not $name) { throw "'$Url' doesn't give a usable theme name." }
    if ($Commit -and $Commit -notmatch '^[0-9a-f]{40}$') { throw "'$Commit' isn't a commit." }

    # Cloned aside first, so a failed clone leaves an installed version as it was.
    New-Item -ItemType Directory -Force $Sources | Out-Null
    $clone = Join-Path $Sources $name
    $incoming = "$clone.new"
    if (Test-Path $incoming) { Remove-Item $incoming -Recurse -Force }
    if ($Commit) {
        # The commit the index read (GitHub serves any commit by its id), with origin set up as a
        # clone's is, so `theme update` fetches from it later.
        git init --quiet $incoming
        git -C $incoming remote add origin -- $Url
        git -C $incoming fetch --depth 1 --quiet origin $Commit
        if ($LASTEXITCODE -eq 0) { git -C $incoming checkout --quiet --detach FETCH_HEAD }
    } else { git clone --depth 1 --quiet -- $Url $incoming }
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $incoming -Recurse -Force -ErrorAction SilentlyContinue
        throw "Couldn't clone $Url$(if ($Commit) { " at $($Commit.Substring(0, 7))" })."
    }
    if (-not ((Test-Path "$incoming\colors.toml") -or (Test-Path "$incoming\alacritty.toml"))) {
        Remove-Item $incoming -Recurse -Force
        throw "$Url isn't an Omarchy theme (it has no colors.toml or alacritty.toml)."
    }
    if (Test-Path $clone) { Remove-Item $clone -Recurse -Force }
    Move-Item $incoming $clone
    if (-not (Convert-Installed $name)) { exit 1 }
    Write-Host "  installed   $name$(if ($Commit) { " ($($Commit.Substring(0, 7)))" })"
    # A preview of it (or of another) ends here: set-theme.ps1 applies the real thing over it.
    Remove-Item $PreviewFile -ErrorAction SilentlyContinue
    Remove-Item "$PreviewDir\applied" -ErrorAction SilentlyContinue
    & "$PSScriptRoot\set-theme.ps1" $name
}

# --- previewing ---------------------------------------------------------------------------------
# Puts the desktop in the state theme-preview asks for: that community theme's colours and
# wallpaper (set-theme.ps1 -Preview: GlazeWM, the terminals, the wallpaper; nothing remembered),
# or, with no preview asked for, yours back. One at a time (a mutex), and each reads the file
# once it has its turn, so however fast the picker moves, the last one asked for is what stays.
function Sync-Preview {
    $mutex = [Threading.Mutex]::new($false, 'Local\omawin-theme-preview')
    try {
        try { $null = $mutex.WaitOne() } catch [Threading.AbandonedMutexException] { } # one that died holding it
        $name = (Get-Content $PreviewFile -Raw -ErrorAction SilentlyContinue)
        $name = if ($name) { $name.Trim() }
        $applied = Join-Path $PreviewDir 'applied'
        $was = (Get-Content $applied -Raw -ErrorAction SilentlyContinue)
        $was = if ($was) { $was.Trim() }
        if ($name -eq $was) { return }
        if (-not $name) {
            Remove-Item $applied -ErrorAction SilentlyContinue
            & "$PSScriptRoot\set-theme.ps1" -Preview # your theme's colours and wallpaper
            return
        }
        $entry = Get-CommunityTheme $name
        # Its theme files, made the way installing makes them, from the index's palette: a
        # colors.toml of the palette's colours only (each checked as #rrggbb) for convert-theme.ps1.
        $src = Join-Path $PreviewDir 'source'
        if (Test-Path $src) { Remove-Item $src -Recurse -Force }
        New-Item -ItemType Directory -Force $src | Out-Null
        $lines = foreach ($p in $entry.palette.PSObject.Properties) {
            if ($p.Name -cmatch '^[a-z_]+$' -and ($p.Value -cmatch '^#[0-9a-f]{6}$' -or ($p.Name -eq 'mode' -and $p.Value -in 'dark', 'light'))) { "$($p.Name) = `"$($p.Value)`"" }
        }
        [IO.File]::WriteAllText((Join-Path $src 'colors.toml'), ($lines -join "`n") + "`n")
        $backgrounds = @($entry.backgrounds | Where-Object { $_ -match '^https://raw\.githubusercontent\.com/' })
        & "$PSScriptRoot\convert-theme.ps1" -Source $src -Name $entry.name -Out $PreviewDir -Untrusted -Backgrounds $backgrounds | Out-Null
        & "$PSScriptRoot\set-theme.ps1" $entry.name -Preview -From $PreviewDir
        Set-Content $applied $entry.name -NoNewline
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
}

switch ($Action) {
    { $_ -in 'add', 'install' } {
        if (-not $Target) {
            Write-Host "Community themes: omawin theme search <words>, or $CatalogUrl"
            $Target = (Read-Host 'Name or git link of the theme').Trim()
            if (-not $Target) { exit 1 }
        }
        if (Test-GitUrl $Target) { Install-Theme $Target }
        else {
            $entry = Get-CommunityTheme $Target
            if ($entry.repo -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$') { throw "The index's repo for '$Target' isn't one: $($entry.repo)" }
            Install-Theme "https://github.com/$($entry.repo)" $entry.commit
        }
    }

    'search' {
        $words = @(@($Target) + @($More) | Where-Object { $_ } | ForEach-Object { $_ -split '\s+' } | Where-Object { $_ })
        $installed = Get-InstalledThemes
        $found = @((Get-CommunityIndex).themes | Where-Object { Test-CommunityMatch $_ $words } |
                Sort-Object @{ Expression = { [int]$_.stars }; Descending = $true }, title)
        foreach ($t in $found) {
            $mark = if ($installed -contains $t.name) { '*' } else { ' ' }
            Write-Host ("{0} {1,-26} {2,-5} by {3,-20} {4,5}*  github.com/{5}" -f $mark, $t.name, $t.mode, $t.author, $t.stars, $t.repo)
        }
        Write-Host "  $($found.Count) found$(if ($found) { '; omawin theme add <name> installs one, omawin theme preview <name> shows it first' })"
    }

    'preview' {
        if ($Target -eq 'off') { Remove-Item $PreviewFile -ErrorAction SilentlyContinue }
        # One that isn't in the index fails here, before anything changes.
        elseif ($Target) { Set-Content $PreviewFile (Get-CommunityTheme $Target).name -NoNewline }
        # No name: what the menu app wrote in theme-preview, as it moves across the picker.
        Sync-Preview
    }

    'update' {
        $names = if ($Target) { @($Target) } else { Get-InstalledThemes }
        if (-not $names) { Write-Host 'No installed themes (omawin theme add <name or git link>).'; return }
        $current = Get-CurrentTheme
        # Asked only where someone can answer: a terminal that isn't fed from a pipe.
        $ask = -not $Yes -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected
        $reapply = $false
        foreach ($name in $names) {
            $clone = Join-Path $Sources $name
            if (-not (Test-Path "$clone\.git")) { Write-Host "  $name`: not installed" -ForegroundColor Red; continue }
            $was = git -C $clone rev-parse HEAD
            # The remote's default branch, whatever this clone is on (a branch, or the commit the
            # community index pinned).
            git -C $clone fetch --quiet origin HEAD
            if ($LASTEXITCODE -ne 0) { Write-Host "  $name`: couldn't reach its repo" -ForegroundColor Red; continue }
            $now = git -C $clone rev-parse FETCH_HEAD
            if ($was -eq $now) { Write-Host "  $name`: up to date"; continue }
            # What changed, before it's taken: the commits and the files.
            Write-Host "  $name`: $($was.Substring(0, 7)) -> $($now.Substring(0, 7))"
            git -C $clone --no-pager log --oneline --no-decorate -n 15 "$was..$now" | ForEach-Object { Write-Host "      $_" }
            git -C $clone --no-pager diff --stat "$was" "$now" | ForEach-Object { Write-Host "    $_" }
            if ($ask -and (Read-Host "  Update $name? [y/N]") -notmatch '^(y|yes)$') { Write-Host "  $name`: left as it was"; continue }
            git -C $clone merge --ff-only --quiet FETCH_HEAD
            if ($LASTEXITCODE -ne 0) { Write-Host "  $name`: couldn't update (its history was rewritten: omawin theme add it again)" -ForegroundColor Red; continue }
            if (Convert-Installed $name) {
                Write-Host "  $name`: updated"
                if ($name -eq $current) { $reapply = $true }
            }
        }
        if ($reapply) { & "$PSScriptRoot\set-theme.ps1" $current }
    }

    'remove' {
        $installed = Get-InstalledThemes
        if (-not $Target) {
            if (-not $installed) { Write-Host 'No installed themes.'; return }
            for ($i = 0; $i -lt $installed.Count; $i++) { Write-Host ("  {0}. {1}" -f ($i + 1), $installed[$i]) }
            $pick = Read-Host 'Remove which (number)'
            $index = 0
            if (-not [int]::TryParse($pick, [ref]$index) -or $index -lt 1 -or $index -gt $installed.Count) { exit 1 }
            $Target = $installed[$index - 1]
        }
        if ($installed -notcontains $Target) { throw "No installed theme '$Target' (omawin theme list)." }
        Remove-ThemeFiles $Target
        Remove-Item (Join-Path $Sources $Target) -Recurse -Force
        Write-Host "  removed     $Target"
        # The theme in use is gone: back to Omarchy's of that name, or the default.
        if ((Get-CurrentTheme) -eq $Target) {
            $fallback = if (Test-Path "$PSScriptRoot\$Target.json") { $Target } else { 'starship' }
            & "$PSScriptRoot\set-theme.ps1" $fallback
        }
    }

    'list' {
        $installed = Get-InstalledThemes
        $current = Get-CurrentTheme
        $builtIn = @(Get-ChildItem "$PSScriptRoot\*.json" | ForEach-Object BaseName)
        foreach ($name in @($builtIn + $installed | Sort-Object -Unique)) {
            $mark = if ($name -eq $current) { '*' } else { ' ' }
            # An installed theme's credit: the repo it came from (its author's).
            $kind = if ($installed -contains $name) {
                $origin = git -C (Join-Path $Sources $name) remote get-url origin 2>$null
                if ($origin) { 'installed  ' + ($origin -replace '^https://github\.com/', 'github.com/' -replace '\.git$', '') } else { 'installed' }
            } else { 'Omarchy' }
            Write-Host ("{0} {1,-22} {2}" -f $mark, $name, $kind)
        }
    }

    'browse' {
        Start-Process $CatalogUrl
    }
}
