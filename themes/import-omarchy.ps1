<#
.SYNOPSIS
    Makes this repo's themes from Omarchy's own (github.com/basecamp/omarchy).

.DESCRIPTION
    Brings Omarchy's checkout up to date, copies in the templates its theme files are made from
    (themes/templates/omarchy: Neovim, VS Code, btop), and turns each of its themes into this
    repo's theme files with convert-theme.ps1. Backgrounds aren't copied (they're large): the
    theme lists their URLs, pinned to the Omarchy commit used, and set-theme.ps1 downloads one
    the first time it's needed. Says which themes are new and which Omarchy no longer has
    (those are left alone).

        .\import-omarchy.ps1                    from ~/reference/omarchy (cloned if missing)
        .\import-omarchy.ps1 -Omarchy <path>

    For a theme that isn't one of Omarchy's own, use `omawin theme install <git link>`.
    tokyo-night.json is kept as written by hand (it's the original palette of this setup);
    only its list of backgrounds is filled in.
#>
param([string]$Omarchy = "$HOME\reference\omarchy")
$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$Omarchy\themes")) {
    git clone --depth 1 https://github.com/basecamp/omarchy.git $Omarchy
} else {
    git -C $Omarchy pull --ff-only --quiet
}
$commit = git -C $Omarchy rev-parse HEAD
$raw = "https://raw.githubusercontent.com/basecamp/omarchy/$commit/themes"

New-Item -ItemType Directory -Force "$PSScriptRoot\templates\omarchy" | Out-Null
foreach ($template in 'neovim.lua.tpl', 'vscode-theme.json.tpl', 'btop.theme.tpl') {
    Copy-Item "$Omarchy\default\themed\$template" "$PSScriptRoot\templates\omarchy\$template" -Force
}

$before = @(Get-ChildItem "$PSScriptRoot\colors\*.toml" | ForEach-Object BaseName)
$names = @()
foreach ($dir in Get-ChildItem "$Omarchy\themes" -Directory) {
    $name = $dir.Name
    $backgrounds = @(Get-ChildItem "$($dir.FullName)\backgrounds" -File -ErrorAction SilentlyContinue |
            Where-Object Name -ne 'omarchy.webp' | Sort-Object Name | ForEach-Object { "$raw/$name/backgrounds/$($_.Name)" })
    & "$PSScriptRoot\convert-theme.ps1" -Source $dir.FullName -Name $name -Out $PSScriptRoot `
        -Backgrounds $backgrounds -KeepJson:($name -eq 'tokyo-night')
    $names += $name
    Write-Host ("  {0}{1}" -f $name, $(if ($before -notcontains $name) { '  (new)' }))
}

$gone = $before | Where-Object { $names -notcontains $_ }
if ($gone) { Write-Host "  no longer in Omarchy (left as they are): $($gone -join ', ')" }
Write-Host "  $($names.Count) themes from Omarchy $($commit.Substring(0, 7))"
