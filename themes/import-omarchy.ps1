<#
.SYNOPSIS
    Makes this repo's themes (themes/*.json) from Omarchy's (github.com/basecamp/omarchy).

.DESCRIPTION
    Each Omarchy theme has a colors.toml (its palette), a neovim.lua (the Neovim colorscheme to
    use) and a folder of backgrounds. This turns each one into themes/<name>.json, the format
    the system menu, set-theme.ps1, Zebar, GlazeWM, Windows Terminal and the launcher read,
    copies its neovim.lua to themes/nvim/<name>.lua, and adds a Windows Terminal color scheme
    for it. Backgrounds aren't copied (they're large): the theme lists their URLs, pinned to the
    Omarchy commit used, and set-theme.ps1 downloads one the first time it's needed.

        .\import-omarchy.ps1                    from ~/reference/omarchy (cloned if missing)
        .\import-omarchy.ps1 -Omarchy <path>

    tokyo-night.json is kept as written by hand (it's the original palette of this setup);
    only its list of backgrounds is filled in.
#>
param([string]$Omarchy = "$HOME\reference\omarchy")
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot

if (-not (Test-Path "$Omarchy\themes")) {
    git clone --depth 1 https://github.com/basecamp/omarchy.git $Omarchy
}
$commit = git -C $Omarchy rev-parse HEAD
$raw = "https://raw.githubusercontent.com/basecamp/omarchy/$commit/themes"

# colors.toml is flat: key = "value" lines.
function Read-Colors([string]$File) {
    $colors = @{}
    foreach ($line in Get-Content $File) {
        if ($line -match '^\s*([a-z_]+)\s*=\s*"([^"]*)"') { $colors[$Matches[1]] = $Matches[2] }
    }
    $colors
}

New-Item -ItemType Directory -Force "$PSScriptRoot\nvim" | Out-Null
$schemes = @()
foreach ($dir in Get-ChildItem "$Omarchy\themes" -Directory) {
    $name = $dir.Name
    $c = Read-Colors "$($dir.FullName)\colors.toml"
    $title = (Get-Culture).TextInfo.ToTitleCase(($name -replace '-', ' '))
    $backgrounds = @(Get-ChildItem "$($dir.FullName)\backgrounds" -File -ErrorAction SilentlyContinue |
            Where-Object Name -ne 'omarchy.webp' | Sort-Object Name | ForEach-Object { "$raw/$name/backgrounds/$($_.Name)" })
    $orange = if ($c.orange) { $c.orange } else { $c.bright_yellow }

    # Windows Terminal scheme, named after the theme.
    $schemes += [ordered]@{
        name = $title; background = $c.background; foreground = $c.foreground
        cursorColor = $c.accent; selectionBackground = $c.selection
        black = $c.darker_background; red = $c.red; green = $c.green; yellow = $c.yellow
        blue = $c.blue; purple = $c.magenta; cyan = $c.cyan; white = $c.foreground
        brightBlack = $c.dark_foreground; brightRed = $c.bright_red; brightGreen = $c.bright_green
        brightYellow = $c.bright_yellow; brightBlue = $c.bright_blue; brightPurple = $c.bright_magenta
        brightCyan = $c.bright_cyan; brightWhite = $c.bright_foreground
    }

    # The palette itself, as Omarchy writes it: the source every app's colors come from.
    New-Item -ItemType Directory -Force "$PSScriptRoot\colors" | Out-Null
    Copy-Item "$($dir.FullName)\colors.toml" "$PSScriptRoot\colors\$name.toml" -Force

    $nvim = $name
    if (Test-Path "$($dir.FullName)\neovim.lua") {
        Copy-Item "$($dir.FullName)\neovim.lua" "$PSScriptRoot\nvim\$name.lua" -Force
    } else {
        # No colorscheme of its own: Omarchy fills its aether.nvim template with the theme's
        # colors (default/themed/neovim.lua.tpl); so does this. Keys a theme leaves out fall
        # back the way Omarchy's do.
        $fill = @{ orange = $orange; brown = $c.yellow; selection_foreground = $c.foreground; selection_background = $c.selection }
        $spec = [regex]::Replace((Get-Content "$Omarchy\default\themed\neovim.lua.tpl" -Raw), '\{\{\s*(\w+)\s*\}\}', {
                param($m) $key = $m.Groups[1].Value
                if ($c[$key]) { $c[$key] } elseif ($fill[$key]) { $fill[$key] } else { $c.foreground } })
        Set-Content "$PSScriptRoot\nvim\$name.lua" $spec -Encoding utf8 -NoNewline
    }

    # VS Code / Positron: the theme's own extension when it names one (vscode.json: its
    # extension id and theme label), and for every theme a color theme made from Omarchy's
    # template (default/themed/vscode-theme.json.tpl), used when there's no extension or it
    # can't be installed. set-theme.ps1 picks between them.
    New-Item -ItemType Directory -Force "$PSScriptRoot\vscode" | Out-Null
    if (Test-Path "$($dir.FullName)\vscode.json") {
        Copy-Item "$($dir.FullName)\vscode.json" "$PSScriptRoot\vscode\$name.json" -Force
    } else { Remove-Item "$PSScriptRoot\vscode\$name.json" -ErrorAction SilentlyContinue }
    $vsFill = @{ orange = $orange; theme_type = $(if ($c.mode -eq 'light') { 'light' } else { 'dark' })
        selection_foreground = $c.foreground; selection_background = $c.selection }
    $vs = [regex]::Replace((Get-Content "$Omarchy\default\themed\vscode-theme.json.tpl" -Raw), '\{\{\s*([^}]+?)\s*\}\}', {
            param($m) $expr = $m.Groups[1].Value
            if ($expr -match '^mix\s+(\w+)\s+(\w+)\s+([\d.]+)%?$') {
                # A blended toward B by P%.
                $a, $b, $p = $c[$Matches[1]], $c[$Matches[2]], ([double]$Matches[3] / 100)
                '#' + (-join (1, 3, 5 | ForEach-Object {
                            $x = [Convert]::ToInt32($a.Substring($_, 2), 16); $y = [Convert]::ToInt32($b.Substring($_, 2), 16)
                            '{0:x2}' -f [int][Math]::Round($x + ($y - $x) * $p) }))
            } elseif ($c[$expr]) { $c[$expr] } elseif ($vsFill[$expr]) { $vsFill[$expr] } else { $c.foreground } })
    Set-Content "$PSScriptRoot\vscode\$name.color-theme.json" $vs -Encoding utf8 -NoNewline

    # btop (btop4win, the task manager): the theme's btop.theme, or Omarchy's template filled
    # with its colors.
    New-Item -ItemType Directory -Force "$PSScriptRoot\btop" | Out-Null
    if (Test-Path "$($dir.FullName)\btop.theme") {
        Copy-Item "$($dir.FullName)\btop.theme" "$PSScriptRoot\btop\$name.theme" -Force
    } else {
        $btop = [regex]::Replace((Get-Content "$Omarchy\default\themed\btop.theme.tpl" -Raw), '\{\{\s*(\w+)\s*\}\}', {
                param($m) if ($c[$m.Groups[1].Value]) { $c[$m.Groups[1].Value] } else { $c.foreground } })
        Set-Content "$PSScriptRoot\btop\$name.theme" $btop -Encoding utf8 -NoNewline
    }

    if ($name -eq 'tokyo-night') {
        # Hand-made palette; just its backgrounds.
        $theme = Get-Content "$PSScriptRoot\tokyo-night.json" -Raw | ConvertFrom-Json -AsHashtable
        $theme.backgrounds = $backgrounds
        $theme.nvimSpec = $nvim
    } else {
        $theme = [ordered]@{
            name = $title; mode = $c.mode
            background = $c.background; surface = $c.lighter_background; selection = $c.selection
            border = $c.muted; foreground = $c.bright_foreground; muted = $c.foreground
            comment = $c.dark_foreground; accent = $c.accent; purple = $c.bright_magenta
            cyan = $c.bright_cyan; teal = $c.cyan; green = $c.green; yellow = $c.yellow
            orange = $orange; red = $c.red
            gradient = @($c.bright_magenta, $c.accent, $c.bright_cyan, $c.cyan, $c.green)
            terminal = $title; nvimSpec = $nvim
            backgrounds = $backgrounds
        }
    }
    $theme | ConvertTo-Json -Depth 4 | Set-Content "$PSScriptRoot\$name.json" -Encoding utf8
    Write-Host "  $name"
}

Write-Host "  $($schemes.Count) themes from Omarchy $($commit.Substring(0, 7))"
