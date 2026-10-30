<#
.SYNOPSIS
    Turns one Omarchy theme folder into omawin's theme files.

.DESCRIPTION
    An Omarchy theme is a folder: colors.toml (its palette), backgrounds/, and optionally
    neovim.lua, btop.theme and vscode.json. Older themes have only alacritty.toml, and older
    palettes use color0..color15 instead of names; both are read the way Omarchy reads them
    (omarchy-theme-color, omarchy-theme-colors-from-alacritty), missing colors filled in the same.

    It writes, under -Out (themes/ for Omarchy's own themes, %LOCALAPPDATA%\launcher\themes for
    installed ones; the same layout):
      <name>.json                   palette, wallpapers and mode: what the menu, set-theme.ps1,
                                    render.ps1 and the launcher read
      colors/<name>.toml            the full palette in Omarchy's names (render.ps1's placeholders)
      config/nvim/<name>.lua               the Neovim colorscheme spec
      btop/<name>.theme             the btop theme
      vscode/<name>.color-theme.json  a VS Code theme made from the palette
      vscode/<name>.json            the theme's own VS Code extension, when it names one

    -Untrusted is for themes installed from a git link: like Omarchy, nothing that would run
    code is taken from them. Their neovim.lua (Lua that Neovim runs) is replaced by one made from
    the palette, and their vscode.json (an extension to install) is left out. Every theme's
    colors must be colors (#rrggbb): they're filled into GlazeWM's and the terminal's configs,
    so anything else in colors.toml is dropped.

        .\convert-theme.ps1 -Source <folder> -Name nord -Out <folder> [-Untrusted]
            [-Backgrounds <urls>] [-KeepJson]

    -Backgrounds: the wallpapers' URLs (Omarchy's own themes: pinned to a commit, downloaded on
    first use); left out, the pictures in the folder's backgrounds/ are used where they are.
    -KeepJson: keep <name>.json's colors as they are (tokyo-night's are made by hand); only its
    backgrounds and nvimSpec are updated. Templates: themes/templates/omarchy (Omarchy's own,
    copied in by import-omarchy.ps1).
#>
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Out,
    [switch]$Untrusted,
    [string[]]$Backgrounds,
    [switch]$KeepJson
)
$ErrorActionPreference = 'Stop'
$Templates = Join-Path $PSScriptRoot 'templates\omarchy'

function Write-Utf8File([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

# --- the palette ----------------------------------------------------------------------------
# A color as #rrggbb, or $null: '#abc123', 'abc123', '0xabc123', '#abc123ff' (alpha dropped).
function ConvertTo-Hex([string]$Value) {
    if ($Value -match '^\s*(?:#|0x)?([0-9a-fA-F]{6})(?:[0-9a-fA-F]{2})?\s*$') { return '#' + $Matches[1].ToLower() }
    $null
}

# colors.toml: flat `key = "value"` lines (any quotes, or none; # comments). Only colors, and
# mode / theme_type as dark or light, are kept.
function Read-ColorsToml([string]$File) {
    $colors = @{}
    foreach ($line in [IO.File]::ReadAllLines($File)) {
        if ($line -notmatch '^\s*([A-Za-z0-9_-]+)\s*=\s*(.*)$') { continue }
        $key, $raw = $Matches[1], $Matches[2].Trim()
        $value = if ($raw -match '^["'']([^"'']*)["'']') { $Matches[1] } else { ($raw -split '\s+#')[0].Trim() }
        if ($key -in 'mode', 'theme_type') {
            if ($value -in 'dark', 'light') { $colors[$key] = $value }
        } elseif ($hex = ConvertTo-Hex $value) { $colors[$key] = $hex }
    }
    $colors
}

# alacritty.toml, for themes older than colors.toml: its [colors.*] tables, or dotted keys
# under [colors] (omarchy-theme-colors-from-alacritty). $null if it doesn't hold all eight
# normal colors.
function Read-AlacrittyColors([string]$File) {
    $found = @{}
    $section = ''
    foreach ($line in [IO.File]::ReadAllLines($File)) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[([^\]]+)\]\s*$') { $section = $Matches[1]; continue }
        if (-not $section -or $trimmed -match '^#' -or $trimmed -notmatch '^([^=]+?)\s*=\s*(.+)$') { continue }
        $key, $raw = $Matches[1].Trim(), $Matches[2].Trim()
        $value = if ($raw -match '^["'']([^"'']*)["'']') { $Matches[1] } else { ($raw -split '\s+#')[0] }
        if (-not ($hex = ConvertTo-Hex $value)) { continue }
        $path = "$section.$key"
        if (-not $found.ContainsKey($path)) { $found[$path] = $hex }
    }
    $names = 'black', 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan', 'white'
    $colors = @{}
    for ($i = 0; $i -lt 8; $i++) {
        $normal = $found["colors.normal.$($names[$i])"]
        if (-not $normal) { return $null }
        $colors["color$i"] = $normal
        $bright = $found["colors.bright.$($names[$i])"]
        $colors["color$($i + 8)"] = if ($bright) { $bright } else { $normal }
    }
    $colors.background = if ($found['colors.primary.background']) { $found['colors.primary.background'] } else { $colors.color0 }
    $colors.foreground = if ($found['colors.primary.foreground']) { $found['colors.primary.foreground'] } else { $colors.color7 }
    $colors.selection = if ($found['colors.selection.background']) { $found['colors.selection.background'] } else { $colors.foreground }
    $colors.accent = $colors.color4
    $colors
}

# a blended toward b by $Amount (0..1).
function Get-Mix([string]$A, [string]$B, [double]$Amount) {
    $Amount = [Math]::Max(0, [Math]::Min(1, $Amount))
    '#' + (-join (1, 3, 5 | ForEach-Object {
                $x = [Convert]::ToInt32($A.Substring($_, 2), 16); $y = [Convert]::ToInt32($B.Substring($_, 2), 16)
                '{0:x2}' -f [int][Math]::Floor($x * (1 - $Amount) + $y * $Amount + 0.5) }))
}

# Every name Omarchy's templates use, filled in from what the theme has, in the order and with
# the fallbacks of omarchy-theme-color's resolve_theme_colors.
function Resolve-Palette([hashtable]$C, [string]$ThemeDir) {
    function Set-From([string]$Key, [string]$From) { if (-not $C[$Key] -and $C[$From]) { $C[$Key] = $C[$From] } }
    foreach ($pair in @(('background', 'bg'), ('dark_background', 'dark_bg'), ('darker_background', 'darker_bg'),
            ('lighter_background', 'lighter_bg'), ('foreground', 'fg'), ('dark_foreground', 'dark_fg'),
            ('light_foreground', 'light_fg'), ('bright_foreground', 'bright_fg'))) { Set-From $pair[0] $pair[1] }
    Set-From background color0
    Set-From foreground color7
    if (-not $C.background -or -not $C.foreground) { throw 'The palette has no background or foreground color.' }
    $C.color0 = $C.background
    $C.color7 = $C.foreground
    foreach ($pair in @(('red', 'color1'), ('green', 'color2'), ('yellow', 'color3'), ('blue', 'color4'),
            ('magenta', 'color5'), ('cyan', 'color6'), ('bright_red', 'color9'), ('bright_green', 'color10'),
            ('bright_yellow', 'color11'), ('bright_blue', 'color12'), ('bright_magenta', 'color13'),
            ('bright_cyan', 'color14'), ('magenta', 'purple'), ('bright_magenta', 'bright_purple'))) { Set-From $pair[0] $pair[1] }
    foreach ($key in 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan') {
        if (-not $C[$key]) { $C[$key] = $C.foreground }
    }
    Set-From light_foreground color7
    Set-From bright_foreground color15
    Set-From bright_foreground foreground
    Set-From lighter_background color0
    Set-From dark_foreground color8
    Set-From dark_foreground foreground
    Set-From muted color8
    Set-From muted dark_foreground
    if (-not $C.selection) {
        $C.selection = @($C.selection_background, $C.color8, $C.color0, $C.background) | Where-Object { $_ } | Select-Object -First 1
    }
    Set-From selection_background selection
    Set-From selection_foreground bright_foreground
    Set-From orange yellow
    if (-not $C.brown) { $C.brown = Get-Mix $C.orange '#000000' 0.5 }
    if (-not $C.dark_background) { $C.dark_background = Get-Mix $C.background '#000000' 0.25 }
    if (-not $C.darker_background) { $C.darker_background = Get-Mix $C.background '#000000' 0.5 }
    foreach ($key in 'red', 'yellow', 'green', 'cyan', 'blue', 'magenta') {
        if (-not $C["bright_$key"]) { $C["bright_$key"] = Get-Mix $C[$key] '#ffffff' 0.2 }
    }
    if (-not $C.accent) { $C.accent = $C.blue }
    if (-not $C.mode) { $C.mode = $C.theme_type }
    if (-not $C.mode) {
        $lum = (1, 3, 5 | ForEach-Object { [Convert]::ToInt32($C.background.Substring($_, 2), 16) } | Measure-Object -Sum).Sum
        $C.mode = if (Test-Path (Join-Path $ThemeDir 'light.mode')) { 'light' } elseif ($lum -gt 382) { 'light' } else { 'dark' }
    }
    $C.theme_type = $C.mode
    $C
}

# Omarchy's template placeholders: {{ key }}, {{ key_strip }} (no #), {{ key_rgb }} (r,g,b),
# {{ mix a b 30% }}. An unknown one becomes the foreground color, as the importer always did.
function Expand-Template([string]$Template, [hashtable]$C) {
    [regex]::Replace($Template, '\{\{\s*([^}]+?)\s*\}\}', {
            param($m) $expr = $m.Groups[1].Value
            if ($expr -match '^mix\s+(\w+)\s+(\w+)\s+([\d.]+)%?$' -and $C[$Matches[1]] -and $C[$Matches[2]]) {
                $amount = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
                return Get-Mix $C[$Matches[1]] $C[$Matches[2]] $(if ($amount -gt 1) { $amount / 100 } else { $amount })
            }
            if ($C.ContainsKey($expr)) { return $C[$expr] }
            if ($expr -match '^(\w+)_strip$' -and $C[$Matches[1]]) { return $C[$Matches[1]].TrimStart('#') }
            if ($expr -match '^(\w+)_rgb$' -and $C[$Matches[1]]) {
                $hex = $C[$Matches[1]]
                return (1, 3, 5 | ForEach-Object { [Convert]::ToInt32($hex.Substring($_, 2), 16) }) -join ','
            }
            $C.foreground
        })
}

$Source = (Resolve-Path $Source).Path
$colorsFile = Join-Path $Source 'colors.toml'
$alacrittyFile = Join-Path $Source 'alacritty.toml'
$raw = if (Test-Path $colorsFile) { Read-ColorsToml $colorsFile }
elseif (Test-Path $alacrittyFile) { Read-AlacrittyColors $alacrittyFile }
if (-not $raw -or $raw.Count -eq 0) { throw "'$Name' has no palette: neither colors.toml nor a usable alacritty.toml." }
$c = Resolve-Palette $raw $Source
$title = (Get-Culture).TextInfo.ToTitleCase(($Name -replace '[-_]', ' '))

# colors/<name>.toml: the resolved palette, in Omarchy's names and order.
$order = 'mode', 'accent', 'selection', 'muted', '', 'background', 'dark_background', 'darker_background',
'lighter_background', '', 'foreground', 'dark_foreground', 'light_foreground', 'bright_foreground', '',
'red', 'yellow', 'orange', 'green', 'cyan', 'blue', 'magenta', 'brown', '',
'bright_red', 'bright_yellow', 'bright_green', 'bright_cyan', 'bright_blue', 'bright_magenta', '',
'selection_foreground', 'selection_background'
$toml = @("# $title, from Omarchy's colors.toml (themes/convert-theme.ps1: missing colors filled in as Omarchy does).")
foreach ($key in $order) { $toml += if ($key) { "$key = `"$($c[$key])`"" } else { '' } }
Write-Utf8File (Join-Path $Out "colors\$Name.toml") (($toml -join "`n") + "`n")

# config/nvim/<name>.lua: the theme's own colorscheme spec, or Omarchy's aether.nvim template.
$nvimOut = Join-Path $Out "nvim\$Name.lua"
$nvimOwn = Join-Path $Source 'neovim.lua'
if (-not $Untrusted -and (Test-Path $nvimOwn)) {
    Write-Utf8File $nvimOut ([IO.File]::ReadAllText($nvimOwn))
} else {
    Write-Utf8File $nvimOut (Expand-Template ([IO.File]::ReadAllText("$Templates\neovim.lua.tpl")) $c)
}

# vscode/: the theme's own extension (vscode.json: its id and theme label), and a color theme
# from the palette for when there's none or it can't be installed. set-theme.ps1 picks.
$vscodeOwn = Join-Path $Source 'vscode.json'
$vscodeOut = Join-Path $Out "vscode\$Name.json"
if (-not $Untrusted -and (Test-Path $vscodeOwn)) { Write-Utf8File $vscodeOut ([IO.File]::ReadAllText($vscodeOwn)) }
else { Remove-Item $vscodeOut -ErrorAction SilentlyContinue }
Write-Utf8File (Join-Path $Out "vscode\$Name.color-theme.json") (Expand-Template ([IO.File]::ReadAllText("$Templates\vscode-theme.json.tpl")) $c)

# btop/<name>.theme: the theme's own (only theme[...] color lines are kept from an installed
# one), or Omarchy's template.
$btopOwn = Join-Path $Source 'btop.theme'
$btopOut = Join-Path $Out "btop\$Name.theme"
if (Test-Path $btopOwn) {
    $lines = [IO.File]::ReadAllLines($btopOwn)
    if ($Untrusted) { $lines = $lines | Where-Object { $_ -match '^\s*(#.*)?$' -or $_ -match '^\s*theme\[[a-z_]+\]\s*=\s*"[#0-9a-fA-F ]*"\s*$' } }
    Write-Utf8File $btopOut (($lines -join "`n") + "`n")
} else {
    Write-Utf8File $btopOut (Expand-Template ([IO.File]::ReadAllText("$Templates\btop.theme.tpl")) $c)
}

# Wallpapers: the URLs given, or the pictures in the folder (not links pointing out of it).
if (-not $PSBoundParameters.ContainsKey('Backgrounds')) {
    $dir = Join-Path $Source 'backgrounds'
    $Backgrounds = @(Get-ChildItem $dir -File -ErrorAction SilentlyContinue |
            Where-Object { -not $_.LinkType -and $_.Extension -match '^\.(jpe?g|png|webp|bmp|gif)$' -and $_.Name -ne 'omarchy.webp' } |
            Sort-Object Name | ForEach-Object FullName)
}

# <name>.json: what the menu, set-theme.ps1, render.ps1 and the launcher read.
$jsonOut = Join-Path $Out "$Name.json"
if ($KeepJson -and (Test-Path $jsonOut)) {
    $theme = [IO.File]::ReadAllText($jsonOut) | ConvertFrom-Json -AsHashtable
    $theme.backgrounds = @($Backgrounds)
    $theme.nvimSpec = $Name
} else {
    $theme = [ordered]@{
        name = $title; mode = $c.mode
        background = $c.background; surface = $c.lighter_background; selection = $c.selection
        border = $c.muted; foreground = $c.bright_foreground; muted = $c.foreground
        comment = $c.dark_foreground; accent = $c.accent; purple = $c.bright_magenta
        cyan = $c.bright_cyan; teal = $c.cyan; green = $c.green; yellow = $c.yellow
        orange = $c.orange; red = $c.red
        gradient = @($c.bright_magenta, $c.accent, $c.bright_cyan, $c.cyan, $c.green)
        terminal = $title; nvimSpec = $Name
        backgrounds = @($Backgrounds)
    }
}
Write-Utf8File $jsonOut (($theme | ConvertTo-Json -Depth 4) + "`n")
