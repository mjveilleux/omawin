<#
.SYNOPSIS
    The current theme's colors into the configs that can't read them by themselves, the way
    Omarchy renders its templates on a theme switch (omawin restart glazewm runs it too).

.DESCRIPTION
    Every theme is one palette, themes/colors/<name>.toml (Omarchy's own colors.toml), and the
    pick is %LOCALAPPDATA%\launcher\theme. Most apps read those directly (Neovim, the
    menu app) or get files outside this repo (RStudio, VS Code, btop). These can't, so they get
    a copy filled in from a template, outside what git tracks, and nothing tracked ever changes
    on a theme switch:

      glazewm/config.yaml                   ->  ~/.glzr/glazewm/config.yaml (what GlazeWM reads)
                                                plus your account folder's glazewm/config.yaml
                                                (keybindings, window_rules: Merge-GlazeUser)
      themes/templates/noctty.ghostty       ->  %LOCALAPPDATA%\launcher\theme.ghostty (noctty includes it)
      themes/templates/windows-terminal.json ->  Windows Terminal's fragment folder: the scheme
                                                "omawin", which settings.json uses
      (themes/<name>.json)                  ->  %LOCALAPPDATA%\launcher\bar-background.txt (the
                                                colour of the strip that holds the bar's place at
                                                sign-in)

    Placeholders are Omarchy's: {{ accent }}, {{ background }}, {{ bright_red }}, ... (the keys of
    colors.toml). Edit the templates, never the copies; then run this (or omawin restart glazewm).

        .\render.ps1                  the current theme
        .\render.ps1 -Name nord       another one (set-theme.ps1 passes the new pick)
        .\render.ps1 -Reload          and reload GlazeWM's config

    Runs under Windows PowerShell 5.1 too (install.ps1).
#>
param([string]$Name, [switch]$Reload)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$State = Join-Path $env:LOCALAPPDATA 'launcher'
if (-not $Name) { $Name = if (Test-Path "$State\theme") { (Get-Content "$State\theme" -Raw).Trim() } else { 'starship' } }
# An installed theme's files (%LOCALAPPDATA%\launcher\themes, theme.ps1) before Omarchy's own here.
function Find-ThemeFile([string]$Relative) {
    $mine = Join-Path $State "themes\$Relative"
    if (Test-Path $mine) { $mine } else { Join-Path $PSScriptRoot $Relative }
}
$palette = Find-ThemeFile "colors\$Name.toml"
if (-not (Test-Path $palette)) { $palette = "$PSScriptRoot\colors\gruvbox.toml" }

# The palette, with the fallbacks Omarchy's templates use for keys a theme leaves out.
$c = @{}
foreach ($line in Get-Content $palette) {
    if ($line -match '^\s*([a-z_]+)\s*=\s*"([^"]*)"') { $c[$Matches[1]] = $Matches[2] }
}
function Fallback([string]$Key, [string]$From) { if (-not $c[$Key]) { $c[$Key] = $c[$From] } }
Fallback 'selection' 'lighter_background'
Fallback 'darker_background' 'background'
Fallback 'dark_foreground' 'foreground'
Fallback 'bright_foreground' 'foreground'
Fallback 'orange' 'bright_yellow'
foreach ($k in 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan') { Fallback "bright_$k" $k }
$c['name'] = $Name

function Expand-Template([string]$Text, [string]$Source) {
    [regex]::Replace($Text, '\{\{\s*(\w+)\s*\}\}', {
            param($m)
            $key = $m.Groups[1].Value
            if ($c.ContainsKey($key)) { $c[$key] } else { throw "$Source uses {{ $key }}, which $Name's palette doesn't have" }
        })
}
function Write-Text([string]$Target, [string]$Text) {
    New-Item -ItemType Directory -Force (Split-Path $Target) | Out-Null
    [IO.File]::WriteAllText($Target, $Text, [Text.UTF8Encoding]::new($false))
}
function Write-Rendered([string]$Template, [string]$Target) {
    Write-Text $Target (Expand-Template ([IO.File]::ReadAllText($Template)) $Template)
}

# Your account folder's glazewm/config.yaml (docs/work.md) adds to omawin's GlazeWM config: its
# top-level keybindings: and window_rules: lists are appended to omawin's (the same YAML as
# GlazeWM's own, theme placeholders allowed). A key you bind there is first taken off omawin's
# binding for it, so yours replaces it; an item with commands: [] only frees its keys. The
# bindings must be written on one line, bindings: ['alt+x', ...]. Other top-level keys are ignored.
function Merge-GlazeUser([string]$Text, [string]$UserText, [string]$UserFile) {
    $nl = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = New-Object Collections.Generic.List[string]
    $lines.AddRange([string[]]($Text.TrimEnd() -split '\r?\n'))
    # An item is a line "  - ..." and the lines under it indented deeper.
    function Split-Items([string[]]$Body) {
        $items = @(); $item = $null
        foreach ($line in $Body) {
            if ($line -match '^  - ') { if ($item) { $items += , $item }; $item = @($line) }
            elseif ($item -and $line -match '^ {3,}\S') { $item += $line }
            else { throw "${UserFile}: can't place '$($line.Trim())'" }
        }
        if ($item) { $items += , $item }
        , $items
    }
    function Get-Keys([string[]]$Item) {
        foreach ($line in $Item) {
            if ($line -match '^\s*(- )?bindings:\s*\[(.*)\]') {
                $Matches[2] -split ',' | ForEach-Object { $_.Trim().Trim("'", '"').Trim().ToLowerInvariant() } | Where-Object { $_ }
            }
        }
    }
    # The user's file: top-level key -> its lines.
    $sections = [ordered]@{}; $current = $null
    foreach ($line in ($UserText -split '\r?\n')) {
        if ($line -match '^([A-Za-z_]+):\s*(#.*)?$') { $current = $Matches[1]; $sections[$current] = @(); continue }
        if ($line -match '^[A-Za-z_]') { $current = $null; Write-Warning "${UserFile}: ignored '$line' (only keybindings: and window_rules: lists are added)"; continue }
        if ($current) { $sections[$current] += $line }
    }
    foreach ($key in @($sections.Keys)) {
        if ($key -notin 'keybindings', 'window_rules') { Write-Warning "${UserFile}: ignored '${key}:' (only keybindings: and window_rules: are added)"; continue }
        $body = @($sections[$key] | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') })
        if (-not $body) { continue }
        # Indented the way omawin's items are: two spaces.
        $indent = $body[0].Length - $body[0].TrimStart().Length
        $body = @($body | ForEach-Object { if ($_.Length - $_.TrimStart().Length -ge $indent) { '  ' + $_.Substring($indent) } else { $_ } })
        $items = Split-Items $body
        $start = $lines.IndexOf("${key}:")
        if ($start -lt 0) { $lines.Add("${key}:"); $start = $lines.Count - 1 }
        if ($key -eq 'keybindings') {
            $taken = @($items | ForEach-Object { Get-Keys $_ })
            # omawin's items in this section, with the taken keys off; an item left with none goes.
            $end = $start + 1
            while ($end -lt $lines.Count -and $lines[$end] -notmatch '^[A-Za-z_]') { $end++ }
            $kept = New-Object Collections.Generic.List[string]
            $i = $start + 1
            while ($i -lt $end) {
                if ($lines[$i] -notmatch '^  - ') { $kept.Add($lines[$i]); $i++; continue }
                $j = $i + 1
                while ($j -lt $end -and $lines[$j] -match '^ {3,}\S') { $j++ }
                $item = @($lines.GetRange($i, $j - $i))
                $keys = @(Get-Keys $item)
                $left = @($keys | Where-Object { $taken -notcontains $_ })
                if (-not $keys -or $left.Count -eq $keys.Count) { $kept.AddRange([string[]]$item) }
                elseif ($left) {
                    $kept.AddRange([string[]]@($item | ForEach-Object {
                                if ($_ -match '^(\s*(- )?bindings:\s*)\[.*\](.*)$') { $Matches[1] + '[' + (($left | ForEach-Object { "'$_'" }) -join ', ') + ']' + $Matches[3] } else { $_ }
                            }))
                }
                $i = $j
            }
            $lines.RemoveRange($start + 1, $end - $start - 1)
            $lines.InsertRange($start + 1, $kept)
        }
        # Yours go at the end of the section, before the blank lines and comments that lead into the next.
        $end = $start + 1
        while ($end -lt $lines.Count -and $lines[$end] -notmatch '^[A-Za-z_]') { $end++ }
        while ($end -gt $start + 1 -and ($lines[$end - 1].Trim() -eq '' -or $lines[$end - 1].TrimStart().StartsWith('#'))) { $end-- }
        $add = @("  # From your account folder: $UserFile")
        foreach ($item in $items) {
            if (($item -join "`n") -match '(^|\n)\s*(- )?commands:\s*\[\s*\]') { continue }  # only frees its keys
            $add += $item
        }
        $lines.InsertRange($end, [string[]]$add)
    }
    ($lines -join $nl) + $nl
}

# GlazeWM: its whole config, from the template in this repo.
$glazeDir = Join-Path $HOME '.glzr\glazewm'
$glazeItem = Get-Item $glazeDir -Force -ErrorAction SilentlyContinue
# The old link to this repo: removed as a link only (Remove-Item could follow it into the repo).
if ($glazeItem -and $glazeItem.LinkType) { [IO.Directory]::Delete($glazeDir, $false) }
$glazeText = Expand-Template ([IO.File]::ReadAllText("$Repo\glazewm\config.yaml")) "$Repo\glazewm\config.yaml"
# Plus your account folder's keybindings and window rules (Merge-GlazeUser above). A mistake there
# never costs you the desktop: it's reported, and omawin's config is written without them.
$glazeUser = Join-Path (& "$Repo\bin\user-layer.ps1") 'glazewm\config.yaml'
if (Test-Path $glazeUser) {
    try { $glazeText = Merge-GlazeUser $glazeText (Expand-Template ([IO.File]::ReadAllText($glazeUser)) $glazeUser) $glazeUser }
    catch { Write-Warning "your GlazeWM additions weren't added: $_" }
}
Write-Text "$glazeDir\config.yaml" $glazeText

# noctty (the terminal): the colors file its config includes.
Write-Rendered "$PSScriptRoot\templates\noctty.ghostty" "$State\theme.ghostty"

# Windows Terminal: a fragment holding the scheme "omawin" (settings.json's colorScheme).
Write-Rendered "$PSScriptRoot\templates\windows-terminal.json" "$env:LOCALAPPDATA\Microsoft\Windows Terminal\Fragments\omawin\theme.json"
# A running Windows Terminal doesn't watch its fragments, only settings.json, and reloads
# (fragments included) when that file's time changes; its contents stay as they are.
if (Get-Process WindowsTerminal -ErrorAction SilentlyContinue) {
    # The Store build's, or the tools bundle's unpackaged one's.
    $wtSettings = Get-Item "$env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminal_*\LocalState\settings.json", "$env:LOCALAPPDATA\Microsoft\Windows Terminal\settings.json" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($wtSettings) { $wtSettings.LastWriteTime = Get-Date }
}

# The bar's colour for the strip that holds its place at sign-in until the bar is up (omawin.exe
# start bar): one line, outside the repo. The bar itself reads the theme live (menu-app).
$json = Find-ThemeFile "$Name.json"
if (-not (Test-Path $json)) { $json = "$PSScriptRoot\gruvbox.json" }
$t = [IO.File]::ReadAllText($json) | ConvertFrom-Json
$barColor = "$env:LOCALAPPDATA\launcher\bar-background.txt"
New-Item -ItemType Directory -Force (Split-Path $barColor) | Out-Null
[IO.File]::WriteAllText($barColor, "$($t.background)`n")

if ($Reload -and (Get-Command glazewm -ErrorAction SilentlyContinue)) { glazewm command wm-reload-config | Out-Null }
