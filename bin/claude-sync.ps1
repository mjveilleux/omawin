<#
.SYNOPSIS
    omawin claude sync: Claude Code's rules, skills, settings and plugins from the shared folder and this account's.

.DESCRIPTION
    Your Claude setup lives in your own folders (docs/work.md), so every Windows account gets it:
    the PC's shared folder's claude/ (C:\ProgramData\omawin\shared: yours in every account),
    then the account's own (~/omawin-account: that job's or client's), which wins where both say
    something. A shared skill named in the account's claude/skills-off.txt is left out there.

      claude/CLAUDE.md       your rules: ~/.claude/CLAUDE.md imports each layer's (@import, so an
                             edit in the layer is live). A ~/.claude/CLAUDE.md you wrote yourself
                             (without omawin's first line) is left alone.
      claude/skills/<name>/  linked into ~/.claude/skills (junctions: live). A real folder of the
                             same name already there is left alone and reported; links to a skill
                             no longer in a layer go. Claude.ai's synced skills aren't touched.
      claude/settings.json   merged into ~/.claude/settings.json: omawin's claude/settings.json
                             fills in what's missing, then the shared folder's and the account's
                             go on top. A key a layer sets wins;
                             lists are joined; keys only the live file has (what /config, herdr's
                             hook or a plugin install wrote) stay. {home} in a string becomes this
                             account's home folder.
      plugins                every marketplace in extraKnownMarketplaces and plugin in
                             enabledPlugins is installed (claude plugin), when it isn't yet.

    Never copied: sign-ins (.credentials.json: sign in in each account, maybe as a different
    Claude account), history, and memory (it's kept per project folder by Claude Code).

    install.ps1 runs this (the other step), and omawin.exe at every sign-in; run it after editing a layer's settings or adding a
    skill. Safe to run any number of times. Runs under Windows PowerShell 5.1 too.
#>
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$layers = @(
    @{ Name = 'shared'; Path = (& "$PSScriptRoot\user-layer.ps1" -Shared) },
    @{ Name = 'account'; Path = (& "$PSScriptRoot\user-layer.ps1") }
) | Where-Object { Test-Path (Join-Path $_.Path 'claude') }
$claudeDir = Join-Path $HOME '.claude'
New-Item -ItemType Directory -Force $claudeDir | Out-Null

# --- rules ---------------------------------------------------------------------------------
$Mark = '<!-- written by omawin (bin/claude-sync.ps1): your rules are in your layers -->'
$rules = Join-Path $claudeDir 'CLAUDE.md'
$imports = @($layers | Where-Object { Test-Path (Join-Path $_.Path 'claude\CLAUDE.md') } |
    ForEach-Object { '@' + ((Join-Path $_.Path 'claude\CLAUDE.md') -replace '\\', '/') })
if ((Test-Path $rules) -and (Get-Content $rules -TotalCount 1) -ne $Mark) {
    "kept    $rules (yours; add the layers' with: $($imports -join ' '))"
} elseif ($imports) {
    [IO.File]::WriteAllText($rules, (@($Mark) + $imports) -join "`n")
    "ok      rules: $($imports.Count) layer(s) imported into $rules"
} elseif (Test-Path $rules) { Remove-Item $rules; 'removed ~/.claude/CLAUDE.md (no layer has rules)' }

# --- skills --------------------------------------------------------------------------------
$skillsDir = Join-Path $claudeDir 'skills'
New-Item -ItemType Directory -Force $skillsDir | Out-Null
$want = [ordered]@{}
foreach ($layer in $layers) {
    $from = Join-Path $layer.Path 'claude\skills'
    if (Test-Path $from) { foreach ($d in Get-ChildItem $from -Directory) { $want[$d.Name] = $d.FullName } }
}
# Shared skills this account goes without: one name a line in its claude/skills-off.txt.
$off = Join-Path (& "$PSScriptRoot\user-layer.ps1") 'claude\skills-off.txt'
if (Test-Path $off) {
    foreach ($name in Get-Content $off | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }) { $want.Remove($name) }
}
foreach ($name in $want.Keys) {
    $link = Join-Path $skillsDir $name
    $item = Get-Item $link -Force -ErrorAction SilentlyContinue
    if ($item -and $item.LinkType -ne 'Junction') { "kept    skill $name (a folder of its own in ~/.claude/skills; move it into a layer to share it)"; continue }
    if ($item -and "$($item.Target)" -eq $want[$name]) { continue }
    if ($item) { [IO.Directory]::Delete($link) } # the junction only, not what it points to
    New-Item -ItemType Junction -Path $link -Target $want[$name] | Out-Null
    "ok      skill $name"
}
# Junctions into a layer whose skill is gone.
$roots = @($layers | ForEach-Object { (Join-Path $_.Path 'claude\skills') + '\' })
foreach ($item in Get-ChildItem $skillsDir -Force | Where-Object LinkType -eq 'Junction') {
    $target = "$($item.Target)"
    if ($want.Keys -notcontains $item.Name -and ($roots | Where-Object { $target.StartsWith($_, 'OrdinalIgnoreCase') })) {
        [IO.Directory]::Delete($item.FullName)
        "removed skill $($item.Name) (no longer in a layer, or off for this account)"
    }
}

# --- settings ------------------------------------------------------------------------------
function Read-Json([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $text = [IO.File]::ReadAllText($Path).Replace('{home}', ($HOME -replace '\\', '\\'))
    $text | ConvertFrom-Json
}
# $over onto $base: objects merge, lists join (no repeats), anything else $over's.
function Merge($base, $over) {
    if ($null -eq $base) { return $over }
    if ($over -is [Management.Automation.PSCustomObject] -and $base -is [Management.Automation.PSCustomObject]) {
        foreach ($p in $over.PSObject.Properties) {
            $mine = $base.PSObject.Properties[$p.Name]
            $value = if ($mine) { Merge $mine.Value $p.Value } else { $p.Value }
            $base | Add-Member -NotePropertyName $p.Name -NotePropertyValue $value -Force
        }
        return $base
    }
    if ($over -is [array] -and $base -is [array]) {
        $joined = [Collections.Generic.List[object]]::new()
        foreach ($v in @($base) + @($over)) {
            $key = $v | ConvertTo-Json -Compress -Depth 20
            if (-not ($joined | Where-Object { ($_ | ConvertTo-Json -Compress -Depth 20) -eq $key })) { $joined.Add($v) }
        }
        return , $joined.ToArray()
    }
    $over
}
$settingsFile = Join-Path $claudeDir 'settings.json'
# omawin's own defaults only fill in what the live file lacks (/config's choices stay); the
# layers' then win.
$settings = Read-Json "$Repo\claude\settings.json"
if ($null -eq $settings) { $settings = [pscustomobject]@{} }
$live = Read-Json $settingsFile
if ($live) { $settings = Merge $settings $live }
foreach ($source in @($layers | ForEach-Object { Join-Path $_.Path 'claude\settings.json' })) {
    $layerSettings = Read-Json $source
    if ($layerSettings) { $settings = Merge $settings $layerSettings }
}
$json = $settings | ConvertTo-Json -Depth 20
$old = if (Test-Path $settingsFile) { [IO.File]::ReadAllText($settingsFile) } else { '' }
if (($old | ConvertFrom-Json -ErrorAction SilentlyContinue | ConvertTo-Json -Depth 20) -ne $json) {
    if ($old) { Copy-Item $settingsFile "$settingsFile.before-sync" }
    [IO.File]::WriteAllText($settingsFile, $json)
    "ok      settings merged into $settingsFile (the previous one: settings.json.before-sync)"
} else { 'ok      settings already match the layers' }

# --- plugins -------------------------------------------------------------------------------
$claude = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claude) { 'Claude Code not installed: plugins are added on the next run'; return }
$known = Read-Json (Join-Path $claudeDir 'plugins\known_marketplaces.json')
$installed = Read-Json (Join-Path $claudeDir 'plugins\installed_plugins.json')
if ($settings.extraKnownMarketplaces) {
    foreach ($m in $settings.extraKnownMarketplaces.PSObject.Properties) {
        if ($known -and $known.PSObject.Properties[$m.Name]) { continue }
        $src = $m.Value.source
        $from = if ($src.repo) { $src.repo } elseif ($src.url) { $src.url } else { $src.path }
        & $claude.Source plugin marketplace add $from *> $null
        "ok      plugin marketplace $($m.Name)"
    }
}
if ($settings.enabledPlugins) {
    foreach ($p in $settings.enabledPlugins.PSObject.Properties | Where-Object Value -eq $true) {
        if ($installed -and $installed.plugins.PSObject.Properties[$p.Name]) { continue }
        & $claude.Source plugin install $p.Name *> $null
        "ok      plugin $($p.Name)"
    }
}
