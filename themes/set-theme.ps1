<#
.SYNOPSIS
    Switch the whole desktop to a theme, like Omarchy's theme picker.

.DESCRIPTION
    themes/<name>.json (made from Omarchy's themes by import-omarchy.ps1) is applied to:
      wallpaper        the theme's first background (downloaded once, kept in
                       %LOCALAPPDATA%\launcher\backgrounds)
      lock screen      the same picture (set-lockscreen.ps1), so signing in matches
      Windows          dark or light mode, to match
      GlazeWM, noctty, Windows Terminal, Zebar
                       their copies of the palette (render.ps1); GlazeWM reloads, noctty is told
                       to reload its config, Windows Terminal and the bar and dock notice
      Neovim, Neovide  the colorscheme, live in running ones too (themes/nvim/<name>.lua)
      RStudio          an editor theme made from the palette (%APPDATA%\RStudio\themes)
      VS Code, Positron  the theme's own extension (installed the first time), or one made
                       from the palette; running windows switch at once
      launcher, menu   their colors (the menu app repaints; it reads the name below)
      btop             the theme's btop.theme, from its next start

        .\set-theme.ps1 gruvbox
        .\set-theme.ps1 gruvbox -Background village-square   on that background (part of its name)
        .\set-theme.ps1 -NextBackground     the current theme's next background (Win+Ctrl+Space)

    `omawin theme set` and `omawin theme next-background` run this.

    The name is remembered in %LOCALAPPDATA%\launcher\theme. Gruvbox (on its village square
    background) is the default, what install.ps1 applies. Nothing tracked by git changes.
    The colors go first, so they change within about a second; the wallpaper and the editors
    follow.
#>
param([string]$Name, [string]$Background, [switch]$NextBackground)
$ErrorActionPreference = 'Stop'
# Runs under Windows PowerShell 5.1 too: install.ps1 (look step) applies the first theme before
# pwsh exists. These stand in for what only pwsh has.
function ConvertFrom-JsonHashtable([string]$Json) {
    # ConvertFrom-Json -AsHashtable (pwsh 6+); on 5.1, objects become ordered hashtables by hand.
    if ($PSVersionTable.PSVersion.Major -ge 6) { return ($Json | ConvertFrom-Json -AsHashtable) }
    function ConvertTo-Hash($Value) {
        if ($Value -is [Management.Automation.PSCustomObject]) {
            $h = [ordered]@{}
            foreach ($p in $Value.PSObject.Properties) { $h[$p.Name] = ConvertTo-Hash $p.Value }
            return $h
        }
        if ($Value -is [array]) { return , @($Value | ForEach-Object { ConvertTo-Hash $_ }) }
        return $Value
    }
    ConvertTo-Hash ($Json | ConvertFrom-Json)
}
function ConvertFrom-JsonKeepDates([string]$Json) {
    # pwsh 7.5+ turns date strings into DateTimes unless told not to (-DateKind String); 5.1
    # never does. Either way they're written back exactly as they were read.
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { return ($Json | ConvertFrom-Json -DateKind String) }
    $Json | ConvertFrom-Json
}
function Write-Utf8File([string]$Path, [string]$Text) {
    # Set-Content -Encoding utf8 writes a BOM on 5.1, which JSON readers can choke on.
    [IO.File]::WriteAllText($Path, $Text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
$Repo = Split-Path $PSScriptRoot
$State = Join-Path $env:LOCALAPPDATA 'launcher'
New-Item -ItemType Directory -Force $State | Out-Null

$current = Get-Content "$State\theme" -ErrorAction SilentlyContinue
if (-not $current) { $current = 'gruvbox' }
if ($NextBackground -or -not $Name) { $Name = $current }
$file = Join-Path $PSScriptRoot "$Name.json"
if (-not (Test-Path $file)) { throw "No theme '$Name' (themes/*.json)." }
$theme = Get-Content $file -Raw | ConvertFrom-Json

# --- the colors first: what you see change at once ----------------------------------------------
if (-not $NextBackground) {
    Set-Content "$State\theme" $Name -NoNewline
    Set-Content "$State\nvim-theme" $(if ($theme.nvimSpec) { $theme.nvimSpec } else { $Name }) -NoNewline

    # Windows dark / light.
    $light = [int]($theme.mode -eq 'light')
    $personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    Set-ItemProperty $personalize AppsUseLightTheme $light
    Set-ItemProperty $personalize SystemUsesLightTheme $light

    # GlazeWM's borders, noctty, Windows Terminal and Zebar: their copies of the palette, filled
    # in outside what git tracks (render.ps1). Zebar's bar and dock pick theme.css up by
    # themselves (zebar/tokyo-islands/theme.js), no restart; a running Windows Terminal is
    # nudged to reload (render.ps1).
    & "$PSScriptRoot\render.ps1" -Name $Name
    # Then, not waited for: GlazeWM reloads its config (~0.4 s), and noctty's running instance
    # (the one that opens every new window and tab) re-reads its config, so open windows
    # recolor and new ones start in the new theme; without that it keeps the colors it started
    # with. Its project windows (omawin launch project) are instances of their own and keep
    # theirs until they're reopened.
    function Start-Hidden([string]$Program, [string]$Arguments) {
        $info = [Diagnostics.ProcessStartInfo]::new($Program, $Arguments)
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $null = [Diagnostics.Process]::Start($info)
    }
    $glazewm = Get-Command glazewm -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($glazewm) { Start-Hidden $glazewm.Source 'command wm-reload-config' }
    $noctty = Join-Path $env:LOCALAPPDATA 'Programs\noctty\noctty.exe'
    if ((Test-Path $noctty) -and (Get-Process noctty -ErrorAction SilentlyContinue)) {
        Start-Hidden $noctty '+perform-action reload_config'
    }
}

# --- wallpaper ------------------------------------------------------------------------------
# Which background: the next one in the list, the one asked for (-Background), or the first
# for a newly picked theme.
$backgrounds = @($theme.backgrounds)
$index = 0
if ($Background) {
    for ($i = 0; $i -lt $backgrounds.Count; $i++) {
        if ((Split-Path $backgrounds[$i] -Leaf) -like "*$Background*") { $index = $i; break }
    }
}
if ($NextBackground) {
    $last = Get-Content "$State\background" -ErrorAction SilentlyContinue
    $index = ([Math]::Max(-1, [array]::IndexOf($backgrounds, $last)) + 1) % [Math]::Max(1, $backgrounds.Count)
}
if ($backgrounds) {
    $source = $backgrounds[$index]
    if ($source -match '^https?://') {
        $image = Join-Path $State "backgrounds\$Name\$(Split-Path $source -Leaf)"
        if (-not (Test-Path $image)) {
            New-Item -ItemType Directory -Force (Split-Path $image) | Out-Null
            Invoke-WebRequest $source -OutFile "$image.part" -UseBasicParsing
            Move-Item "$image.part" $image -Force
        }
    } else { $image = Join-Path $Repo $source }
    Add-Type -Namespace Theme -Name Desktop -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);'
    Set-ItemProperty 'HKCU:\Control Panel\Desktop' WallpaperStyle '10' # fill
    Set-ItemProperty 'HKCU:\Control Panel\Desktop' TileWallpaper '0'
    $null = [Theme.Desktop]::SystemParametersInfo(20, 0, $image, 3) # SPI_SETDESKWALLPAPER, save + tell apps
    Set-Content "$State\background" $source
    Write-Host "  wallpaper   $(Split-Path $image -Leaf)"
    # The lock screen (and sign-in screen) gets the same picture, so logging back in matches.
    # Windows PowerShell does it (WinRT), in the background: the first time, converting a 4K
    # WebP to JPEG takes a few seconds.
    # Unlocking then rotates it through the theme's other backgrounds, from this one.
    Set-Content "$State\lockscreen-background" $source
    Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\set-lockscreen.ps1`"", "`"$image`"")
    Write-Host "  lock screen (same picture)"
}
if ($NextBackground) { return }

# --- RStudio ------------------------------------------------------------------------------------
# An editor theme (.rstheme: CSS for its Ace editor; RStudio colors its panes to match) made
# from the palette, picked in rstudio-prefs.json. A running RStudio notices the change.
$rstudio = "$env:APPDATA\RStudio"
if (Test-Path $rstudio) {
    $t = $theme
    $dark = if ($t.mode -eq 'light') { 'FALSE' } else { 'TRUE' }
    $rsName = "Omarchy $($t.name)"
    $rs = @"
/* rs-theme-name: $rsName */
/* rs-theme-is-dark: $dark */
/* Written by omawin/themes/set-theme.ps1 from themes/$Name.json. */
.ace_editor, .ace_scroller, .rstudio-themes-flat.ace_editor_theme .profvis-flamegraph,
.rstudio-themes-flat.ace_editor_theme, .rstudio-themes-flat .ace_editor_theme {
  background-color: $($t.background); color: $($t.foreground);
}
.ace_gutter { background: $($t.background); color: $($t.comment); }
.ace_gutter-active-line { background: $($t.surface); color: $($t.foreground); }
.ace_print-margin { width: 1px; background: $($t.surface); }
.ace_cursor { color: $($t.accent); border-left-width: 2px; }
.ace_overwrite-cursors .ace_cursor { border-bottom: 2px solid $($t.accent); }
.ace_marker-layer .ace_selection { background: $($t.selection); }
.ace_marker-layer .ace_active-line { background: $($t.surface); }
.ace_marker-layer .ace_selected-word { border: 1px solid $($t.border); }
.ace_marker-layer .ace_bracket { margin: 0; border: 1px solid $($t.accent); }
.ace_marker-layer .ace_find_line, .ace_marker-layer .ace_step { background: $($t.selection); }
.ace_invisible, .ace_indent-guide { color: $($t.border); }
.ace_comment, .ace_comment.ace_doc { color: $($t.comment); font-style: italic; }
.ace_keyword, .ace_storage, .ace_meta.ace_tag { color: $($t.purple); }
.ace_keyword.ace_operator, .ace_paren { color: $($t.cyan); }
.ace_string, .ace_string.ace_regexp { color: $($t.green); }
.ace_constant.ace_numeric, .ace_constant.ace_language, .ace_constant.ace_character { color: $($t.orange); }
.ace_support.ace_function, .ace_entity.ace_name.ace_function, .ace_identifier.ace_support { color: $($t.accent); }
.ace_variable.ace_parameter, .ace_variable.ace_language { color: $($t.yellow); }
.ace_entity.ace_name.ace_tag, .ace_entity.ace_other.ace_attribute-name { color: $($t.teal); }
.ace_heading, .ace_markup.ace_heading { color: $($t.accent); font-weight: bold; }
.ace_markup.ace_list { color: $($t.orange); }
.ace_invalid, .ace_console_error { color: $($t.red); }
.ace_fold { background: $($t.accent); border-color: $($t.foreground); }
.ace_layer.ace_marker-layer .ace_foreign_line { background: $($t.surface); }
.ace_node-selector, .ace_comment-highlight { background: $($t.selection); }
"@
    New-Item -ItemType Directory -Force "$rstudio\themes" | Out-Null
    Write-Utf8File "$rstudio\themes\$Name.rstheme" $rs
    $prefsFile = "$rstudio\rstudio-prefs.json"
    $prefs = if (Test-Path $prefsFile) { ConvertFrom-JsonHashtable (Get-Content $prefsFile -Raw) } else { @{} }
    if ($null -eq $prefs) { $prefs = @{} }
    $prefs.editor_theme = $rsName
    Write-Utf8File $prefsFile ($prefs | ConvertTo-Json -Depth 20)
    Write-Host "  rstudio     $rsName"
}

# --- btop ---------------------------------------------------------------------------------------
# btop4win (the task manager, launcher > Task manager) keeps its config and themes next to its
# exe: the theme goes in as themes\omarchy.theme, and btop.conf picks it, with the terminal's
# own (matching) background showing through. It's read when btop starts.
$btopDir = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\aristocratos.btop4win*\btop4win" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($btopDir -and (Test-Path "$PSScriptRoot\btop\$Name.theme")) {
    New-Item -ItemType Directory -Force "$($btopDir.FullName)\themes" | Out-Null
    Copy-Item "$PSScriptRoot\btop\$Name.theme" "$($btopDir.FullName)\themes\omarchy.theme" -Force
    $conf = "$($btopDir.FullName)\btop.conf"
    if (Test-Path $conf) {
        $text = [IO.File]::ReadAllText($conf)
        $text = $text -replace '(?m)^color_theme = .*$', 'color_theme = "omarchy"'
        $text = $text -replace '(?m)^theme_background = .*$', 'theme_background = False'
        $text = $text -replace '(?m)^rounded_corners = .*$', 'rounded_corners = True'
        [IO.File]::WriteAllText($conf, $text, [Text.UTF8Encoding]::new($false))
    }
    Write-Host "  btop        omarchy.theme"
}

# --- VS Code, Positron ------------------------------------------------------------------------------
# Like Omarchy: the theme's own extension when it names one (themes/vscode/<name>.json),
# installed the first time; otherwise (or if that install fails) a color theme made from the
# palette (themes/vscode/<name>.color-theme.json), as a small local extension "Omarchy". Then
# workbench.colorTheme in the editor's settings.json, which a running editor applies at once.
function Set-ColorThemeSetting([string]$File, [string]$Label) {
    # settings.json is JSONC (comments, trailing commas): edited in place, not reparsed.
    New-Item -ItemType Directory -Force (Split-Path $File) | Out-Null
    $text = if (Test-Path $File) { [IO.File]::ReadAllText($File) } else { "{`n}`n" }
    $value = $Label.Replace('\', '\\').Replace('"', '\"')
    if ($text -match '"workbench\.colorTheme"\s*:') {
        $text = [regex]::Replace($text, '("workbench\.colorTheme"\s*:\s*)"(?:[^"\\]|\\.)*"', { param($m) $m.Groups[1].Value + "`"$value`"" })
    } else {
        $text = ([regex]'\{').Replace($text, "{`n    `"workbench.colorTheme`": `"$value`",", 1)
    }
    [IO.File]::WriteAllText($File, $text, [Text.UTF8Encoding]::new($false))
}
function Test-ExtensionInstalled([string]$Extensions, [string]$Id) {
    $list = Get-Content "$Extensions\extensions.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
    $obsolete = Get-Content "$Extensions\.obsolete" -Raw -ErrorAction SilentlyContinue
    $gone = if ($obsolete) { ConvertFrom-JsonHashtable $obsolete }
    [bool]($list | Where-Object { $_.identifier.id -eq $Id -and (Test-Path "$Extensions\$($_.relativeLocation)") -and -not ($gone -and $gone[$_.relativeLocation]) })
}
function Install-GeneratedTheme([string]$Extensions, [string]$ThemeFile, [string]$Mode) {
    # A local extension (local.omarchy-theme). Its version follows the colors: the editor
    # caches a theme until its extension's version changes.
    $json = [IO.File]::ReadAllText($ThemeFile)
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = [BitConverter]::ToUInt32($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($json)), 0)
    $sha.Dispose()
    $version = "1.0.$hash"
    $dir = "$Extensions\omarchy-theme"
    New-Item -ItemType Directory -Force "$dir\themes" | Out-Null
    [IO.File]::WriteAllText("$dir\themes\omarchy-color-theme.json", $json, [Text.UTF8Encoding]::new($false))
    [ordered]@{
        name = 'omarchy-theme'; displayName = 'Omarchy'; description = 'Omarchy color theme (omawin)'
        publisher = 'local'; version = $version; engines = @{ vscode = '^1.70.0' }; categories = @('Themes')
        contributes = @{ themes = @(@{ label = 'Omarchy'; uiTheme = $(if ($Mode -eq 'light') { 'vs' } else { 'vs-dark' }); path = './themes/omarchy-color-theme.json'; _watch = $true }) }
    } | ConvertTo-Json -Depth 6 | ForEach-Object { Write-Utf8File "$dir\package.json" $_ }
    # Registered in the editor's list of installed extensions.
    $listFile = "$Extensions\extensions.json"
    $listJson = Get-Content $listFile -Raw -ErrorAction SilentlyContinue
    # Held in a variable first: 5.1's ConvertFrom-Json passes an array down the pipeline whole.
    $parsed = if ($listJson) { ConvertFrom-JsonKeepDates $listJson }
    $list = @($parsed | Where-Object { $_ -and $_.identifier.id -ne 'local.omarchy-theme' })
    $list += [ordered]@{
        identifier = @{ id = 'local.omarchy-theme' }; version = $version
        location = [ordered]@{ '$mid' = 1; path = '/' + $dir.Replace('\', '/'); scheme = 'file' }
        relativeLocation = 'omarchy-theme'
    }
    Write-Utf8File $listFile (ConvertTo-Json @($list) -Depth 10 -Compress)
}
$editors = @(
    @{ Name = 'vscode'; Cli = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd"; Settings = "$env:APPDATA\Code\User\settings.json"; Extensions = "$HOME\.vscode\extensions" }
    @{ Name = 'positron'; Cli = "$env:ProgramFiles\Positron\bin\positron.cmd"; Settings = "$env:APPDATA\Positron\User\settings.json"; Extensions = "$HOME\.positron\extensions" }
)
$descriptor = Get-Content "$PSScriptRoot\vscode\$Name.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
foreach ($e in $editors | Where-Object { Test-Path $_.Cli }) {
    New-Item -ItemType Directory -Force $e.Extensions | Out-Null
    $label = $null
    if ($descriptor.extension -match '^[\w.-]+$' -and $descriptor.name) {
        if (-not (Test-ExtensionInstalled $e.Extensions $descriptor.extension)) {
            & $e.Cli --install-extension $descriptor.extension 2>&1 | Out-Null
        }
        if (Test-ExtensionInstalled $e.Extensions $descriptor.extension) { $label = $descriptor.name }
    }
    if (-not $label -and (Test-Path "$PSScriptRoot\vscode\$Name.color-theme.json")) {
        Install-GeneratedTheme $e.Extensions "$PSScriptRoot\vscode\$Name.color-theme.json" $theme.mode
        $label = 'Omarchy'
    }
    if ($label) {
        Set-ColorThemeSetting $e.Settings $label
        Write-Host ("  {0,-11} {1}" -f $e.Name, $label)
    }
}

Write-Host "  theme       $($theme.name)"
