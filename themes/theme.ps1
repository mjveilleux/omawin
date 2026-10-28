<#
.SYNOPSIS
    Themes beyond Omarchy's own, like Omarchy's `omarchy theme install`: from a git link.

.DESCRIPTION
    An installed theme is a git clone in %LOCALAPPDATA%\launcher\themes\sources\<name>, turned
    into omawin's theme files beside it (%LOCALAPPDATA%\launcher\themes, the same layout as this
    repo's themes/; convert-theme.ps1). The menu, the launcher, Neovim and set-theme.ps1 look
    there first, so an installed theme with the name of one of Omarchy's replaces it, as in
    Omarchy. Like Omarchy, nothing that would run code is taken from an installed theme (its
    Neovim Lua, its VS Code extension); see convert-theme.ps1.

        .\theme.ps1 install <git link>   clone, convert and apply (asks for the link if left out)
        .\theme.ps1 update [name]        git pull every installed theme (or one), convert again,
                                         and apply the current one again if it changed
        .\theme.ps1 remove [name]        remove one (asks which if left out)
        .\theme.ps1 list                 every theme: Omarchy's (built in) and installed ones
        .\theme.ps1 browse               Omarchy's page of extra themes, to pick a link from

    `omawin theme install|update|remove|list|browse` run this. The link is the theme's repo, as
    https://github.com/<someone>/omarchy-<name>-theme or git@github.com:<someone>/...; the
    theme's name comes from it, without "omarchy-" and "-theme".
#>
param(
    [Parameter(Position = 0)][ValidateSet('install', 'update', 'remove', 'list', 'browse')][string]$Action = 'list',
    [Parameter(Position = 1)][string]$Target
)
$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:LOCALAPPDATA 'launcher\themes'
$Sources = Join-Path $Root 'sources'
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$CatalogUrl = 'https://omarchy.org/themes/'

function Get-CurrentTheme {
    $name = Get-Content "$State\theme" -Raw -ErrorAction SilentlyContinue
    if ($name) { $name.Trim() } else { 'starship' }
}

function Get-InstalledThemes {
    @(Get-ChildItem $Sources -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path "$($_.FullName)\.git" } | ForEach-Object Name)
}

# The theme's name from its link, the way Omarchy names it, held to the characters a theme
# name needs (it becomes a folder name and an argument): $null if it gives none.
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

switch ($Action) {
    'install' {
        if (-not $Target) {
            Write-Host "Extra themes: $CatalogUrl (omawin theme browse)"
            $Target = (Read-Host 'Git link of the theme').Trim()
            if (-not $Target) { exit 1 }
        }
        if (-not (Test-GitUrl $Target)) { throw "'$Target' isn't a git link (https://... or git@host:...)." }
        $name = Get-ThemeName $Target
        if (-not $name) { throw "'$Target' doesn't give a usable theme name." }

        # Cloned aside first, so a failed clone leaves an installed version as it was.
        New-Item -ItemType Directory -Force $Sources | Out-Null
        $clone = Join-Path $Sources $name
        $incoming = "$clone.new"
        if (Test-Path $incoming) { Remove-Item $incoming -Recurse -Force }
        git clone --depth 1 --quiet -- $Target $incoming
        if ($LASTEXITCODE -ne 0) {
            Remove-Item $incoming -Recurse -Force -ErrorAction SilentlyContinue
            throw "Couldn't clone $Target."
        }
        if (-not ((Test-Path "$incoming\colors.toml") -or (Test-Path "$incoming\alacritty.toml"))) {
            Remove-Item $incoming -Recurse -Force
            throw "$Target isn't an Omarchy theme (it has no colors.toml or alacritty.toml)."
        }
        if (Test-Path $clone) { Remove-Item $clone -Recurse -Force }
        Move-Item $incoming $clone
        if (-not (Convert-Installed $name)) { exit 1 }
        Write-Host "  installed   $name"
        & "$PSScriptRoot\set-theme.ps1" $name
    }

    'update' {
        $names = if ($Target) { @($Target) } else { Get-InstalledThemes }
        if (-not $names) { Write-Host 'No installed themes (omawin theme install <git link>).'; return }
        $current = Get-CurrentTheme
        $reapply = $false
        foreach ($name in $names) {
            $clone = Join-Path $Sources $name
            if (-not (Test-Path "$clone\.git")) { Write-Host "  $name`: not installed" -ForegroundColor Red; continue }
            $was = git -C $clone rev-parse HEAD
            git -C $clone pull --ff-only --quiet
            if ($LASTEXITCODE -ne 0) { Write-Host "  $name`: couldn't update" -ForegroundColor Red; continue }
            $now = git -C $clone rev-parse HEAD
            if ($was -eq $now) { Write-Host "  $name`: up to date"; continue }
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
            $kind = if ($installed -contains $name) { 'installed' } else { 'Omarchy' }
            $mark = if ($name -eq $current) { '*' } else { ' ' }
            Write-Host ("{0} {1,-22} {2}" -f $mark, $name, $kind)
        }
    }

    'browse' {
        Start-Process $CatalogUrl
    }
}
