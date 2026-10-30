<#
.SYNOPSIS
    Updates packages the way each one came in: omawin pkg update [name].

.DESCRIPTION
    Every package omawin puts in is recorded with its source in %LOCALAPPDATA%\launcher\packages.tsv
    (install/lib.ps1, Set-PackageSource): the release's tools bundle, winget, Scoop, Chocolatey,
    mise, npm, or its own install script. This updates each one there, so nothing is updated twice
    or by the wrong manager:

        bundle   the newest release's copy, if its version differs (install/software.ps1)
        winget   winget upgrade --id <id>
        scoop    scoop update <name>
        choco    choco upgrade <id> (Chocolatey's own admin prompt, if it needs one)
        mise     mise upgrade <tool>
        npm      npm update --global <package>
        script   its install script again (herdr, Claude Code, ...: packages.ps1's ExtraInstalls)
        found    yours from somewhere omawin can't tell: left alone

        omawin pkg update             everything
        omawin pkg update nvim        one (its name or id)
        omawin pkg update -List       what's recorded, and where from
#>
param(
    [Parameter(Position = 0)][string]$Name,
    [switch]$List
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\packages.ps1"
. "$Repo\install\lib.ps1"
$SoftwareFunctionsOnly = $true
. "$Repo\install\software.ps1"

$rows = @((Get-PackageSources).Values)
if ($List) {
    foreach ($r in $rows) { Write-Host ("  {0,-34} {1,-7} {2,-16} {3}" -f $r.Name, $r.Source, $r.Version, $r.Where) }
    Write-Host "`n  $PackagesFile" -ForegroundColor DarkGray
    return
}
if ($Name) {
    $rows = @($rows | Where-Object { $_.Name -eq $Name -or $_.Id -eq $Name })
    if (-not $rows) { throw "omawin doesn't know where '$Name' came from (omawin pkg update -List)" }
}

# A native program, its output shown, its failure a warning (the rest still update).
function Invoke-Update([string]$What, [scriptblock]$Do) {
    Write-Step $What
    try {
        & { $ErrorActionPreference = 'Continue'; & $Do }
        if ($LASTEXITCODE) { Write-Warn "$What exited with $LASTEXITCODE" }
    } catch { Write-Warn "$($What): $($_.Exception.Message)" }
}

foreach ($group in $rows | Group-Object Source) {
    $items = @($group.Group)
    switch ($group.Name) {
        'bundle' {
            Write-Step 'tools bundle (the newest release)'
            Install-ToolBundle (Get-WingetInstalled) -Only @($items | ForEach-Object Id)
        }
        'winget' { foreach ($i in $items) { Invoke-Update "winget: $($i.Id)" { winget upgrade --id $i.Id --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity } } }
        'scoop' { foreach ($i in $items) { Invoke-Update "scoop: $($i.Id)" { scoop update $i.Id } } }
        'choco' { foreach ($i in $items) { Invoke-Update "choco: $($i.Id)" { choco upgrade $i.Id -y } } }
        'mise' { foreach ($i in $items) { Invoke-Update "mise: $($i.Id)" { mise upgrade $i.Id } } }
        'npm' { foreach ($i in $items) { Invoke-Update "npm: $($i.Id)" { npm update --global $i.Id } } }
        'script' {
            foreach ($i in $items) {
                if ($ExtraInstalls.Contains($i.Id)) { Invoke-Update "$($i.Id) (its installer)" $ExtraInstalls[$i.Id] }
                else { Write-Warn "$($i.Id): no install script for it in packages.ps1" }
            }
        }
        'found' { foreach ($i in $items) { Write-Note "$($i.Name): yours, from elsewhere; left alone" } }
        default { foreach ($i in $items) { Write-Warn "$($i.Name): unknown source '$($group.Name)'" } }
    }
}
Update-SessionPath
