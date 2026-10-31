# Shared by the few terminal pieces left: rename-workspace.ps1 (Alt+N), ~/omawin-account's reports
# script (importing a report) and menu.ps1's jobs that run in a "menu-task" window (wallpaper, winget search):
# the fzf look in the theme's colors.

$esc = [char]27
$reset = "$esc[0m"

# --- colors -------------------------------------------------------------------------------
# The theme picked in the system menu (themes/<name>.json), read each time one of these starts;
# Starship (the default) when none was picked yet or its file is gone. No colors of its own.
$ThemeName = (Get-Content (Join-Path $env:LOCALAPPDATA 'launcher\theme') -ErrorAction SilentlyContinue) ?? 'starship'
# An installed theme's file (themes/theme.ps1) before Omarchy's own.
$ThemeFile = Join-Path $env:LOCALAPPDATA "launcher\themes\$ThemeName.json"
# The repo's themes: two folders up (default\launcher), or one when this runs through the link an
# older menu app reads (~/omawin/launcher, migrations/2026-10-31-repo-layout.ps1).
$themesDir = @("$PSScriptRoot\..\..\themes", "$PSScriptRoot\..\themes") | Where-Object { Test-Path "$_\colors" } | Select-Object -First 1
if (-not (Test-Path $ThemeFile)) { $ThemeFile = Join-Path $themesDir "$ThemeName.json" }
if (-not (Test-Path $ThemeFile)) { $ThemeFile = Join-Path $themesDir 'gruvbox.json' }
$theme = Get-Content $ThemeFile -Raw | ConvertFrom-Json
$Palette = @{
    fg = $theme.foreground; fgDim = $theme.muted; comment = $theme.comment; line = $theme.border
    blue = $theme.accent; purple = $theme.purple; cyan = $theme.cyan; selection = $theme.selection
    aurora = @($theme.gradient) # left to right
}

function Fg([string]$Hex) {
    $r, $g, $b = 1, 3, 5 | ForEach-Object { [Convert]::ToInt32($Hex.Substring($_, 2), 16) }
    "$esc[38;2;$r;$g;${b}m"
}
$dim = Fg $Palette.comment

# --- fzf ----------------------------------------------------------------------------------
$fzfColors = @(
    "fg:$($Palette.fgDim)", "fg+:$($Palette.fg):bold", 'bg:-1', "bg+:$($Palette.selection)", 'gutter:-1',
    "hl:$($Palette.cyan)", "hl+:$($Palette.cyan):bold", "query:$($Palette.fg):bold", "prompt:$($Palette.purple)", "pointer:$($Palette.blue)",
    "info:$($Palette.comment)", "header:$($Palette.comment)", "border:$($Palette.line)", "list-border:$($Palette.line)",
    "input-border:$($Palette.blue)", "label:$($Palette.comment)", "input-label:$($Palette.purple):bold", "list-label:$($Palette.comment)"
) -join ','

# Show `Lines` (display<TAB>value) in fzf; return the chosen value, or $null on Esc.
#   -Label  title on the search box        -ListLabel  title on the list box (the keys)
function Select-Item([string[]]$Lines, [string]$Label, [string]$ListLabel) {
    $fzfArgs = @(
        '--ansi', '--delimiter', "`t", '--with-nth', '1', '--layout', 'reverse', '--height', '100%',
        '--border', 'none', '--input-border', 'rounded', '--list-border', 'rounded',
        '--input-label', " $Label ", '--margin', '0,1', '--no-scrollbar', '--info', 'hidden',
        '--highlight-line', '--pointer', '▌', '--prompt', "$([char]::ConvertFromUtf32(0xF0349))  ",
        '--color', $fzfColors
    )
    if ($ListLabel) { $fzfArgs += '--list-label', " $ListLabel ", '--list-label-pos', '-3:bottom' }
    $out = @($Lines | fzf @fzfArgs)
    if (-not $out[0]) { return $null }
    return ($out[0] -split "`t", 2)[1]
}

# A one-line name box: fzf with nothing to pick from is a nicely styled text box, and
# --print-query hands back what was typed. Returns the text ('' for Enter on an empty box), or
# $null on Esc (fzf's exit code 130).
function Read-Name([string]$Label, [string]$Header, [string]$Default) {
    $fzfArgs = @(
        '--layout', 'reverse', '--height', '100%', '--border', 'none',
        '--input-border', 'rounded', '--input-label', " $Label ", '--margin', '1,2',
        '--no-scrollbar', '--info', 'hidden', '--no-separator', '--print-query',
        '--prompt', "$([char]::ConvertFromUtf32(0xF03EB))  ", '--header', $Header, '--header-first',
        '--color', $fzfColors
    )
    if ($Default) { $fzfArgs += '--query', $Default }
    $typed = @(@() | fzf @fzfArgs)
    if ($LASTEXITCODE -eq 130) { return $null }
    "$($typed[0])".Trim()
}
