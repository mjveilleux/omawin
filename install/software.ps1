# install.ps1 step: the core winget packages and the chosen presets' (packages.ps1), other
# installers, the Nerd Font. Skipped with -LinkOnly.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Stamp and the package lists
# (packages.ps1) are in scope. bin/preset.ps1 dot-sources it with $SoftwareFunctionsOnly set, to
# install one preset with the same functions.

function Install-WingetPackages([string[]]$Ids) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Warn 'winget not found. Install "App Installer" from the Microsoft Store, then re-run.'
        return
    }
    foreach ($id in $Ids) {
        $command = $WingetCommands[$id]
        if ($command -and (Get-Command $command -ErrorAction SilentlyContinue)) { Write-Note "ok  $id ($command)"; continue }
        winget list --id $id --exact --accept-source-agreements *> $null
        if ($LASTEXITCODE -eq 0) { Write-Note "ok  $id"; continue }
        Write-Note "installing $id"
        $wingetArgs = @('install', '--id', $id, '--exact', '--silent', '--accept-source-agreements', '--accept-package-agreements')
        if ($WingetOverrides[$id]) { $wingetArgs += '--override', $WingetOverrides[$id] }
        winget @wingetArgs
        if ($LASTEXITCODE -ne 0) { Write-Warn "winget install $id exited with $LASTEXITCODE" }
    }
    # Bun's winget package leaves bun.exe in its own folder, off the PATH: put that folder on it.
    $bun = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter bun.exe -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($bun -and (Add-UserPath $bun.DirectoryName)) { Write-Note "bun on the PATH: $($bun.DirectoryName)" }
    Update-SessionPath
}

function Install-Extras([string[]]$Names) {
    foreach ($name in $Names) {
        if (Get-Command $name -ErrorAction SilentlyContinue) { Write-Note "ok  $name"; continue }
        Write-Note "installing $name"
        try { & $ExtraInstalls[$name] } catch { Write-Warn "$name failed: $_" }
        Update-SessionPath
    }
}

# The presets this PC picked, minus names packages.ps1 no longer has (said once, not fatal).
function Get-ChosenPresets {
    foreach ($name in Get-Presets) {
        if ($Presets.Contains($name)) { $name } else { Write-Warn "preset '$name' (in $PresetsFile) doesn't exist any more; omawin preset remove $name" }
    }
}

# First install: no presets file yet. Ask which presets (a person is there), else start with none.
function Initialize-Presets {
    if (Test-Path $PresetsFile) { return }
    $picked = @()
    if (Test-Interactive) {
        Write-Host '    Presets (opt-in groups on top of the core):' -ForegroundColor DarkGray
        foreach ($name in $Presets.Keys) { Write-Host ("      {0,-15} {1}" -f $name, $Presets[$name].Summary) -ForegroundColor DarkGray }
        $answer = Read-Host '    Which ones? (names separated by spaces, Enter for none; omawin preset add <name> later)'
        $picked = @($answer -split '[\s,]+' | Where-Object { $_ } | ForEach-Object {
                if ($Presets.Contains($_)) { $_ } else { Write-Warn "no preset '$_'" } })
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    Set-Content $PresetsFile (@('# omawin presets on this PC (packages.ps1); omawin preset add|remove <name>') + $picked) -Encoding utf8
    Write-Note "presets: $(if ($picked) { $picked -join ', ' } else { 'none (omawin preset list)' })"
}

function Install-NerdFont {
    Write-Step "$NerdFont Nerd Font"
    $fontKey = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
    $installed = (Get-ItemProperty $fontKey -ErrorAction SilentlyContinue).PSObject.Properties.Name |
        Where-Object { $_ -like "$NerdFont*Nerd Font*" -or $_ -like "$NerdFont NF*" }
    $machine = Get-ChildItem "$env:WINDIR\Fonts" -Filter "$($NerdFont)NerdFont*" -ErrorAction SilentlyContinue
    if ($installed -or $machine) { Write-Note 'ok  already installed'; return }

    $tmp = Join-Path $env:TEMP "nerdfont-$Stamp"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp "$NerdFont.zip"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-WebRequest "https://github.com/ryanoasis/nerd-fonts/releases/latest/download/$NerdFont.zip" -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $tmp -Force

    # Per-user install: no admin needed.
    $fontDir = "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"
    New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
    Add-Type -AssemblyName System.Drawing
    foreach ($ttf in Get-ChildItem $tmp -Filter '*.ttf') {
        $dest = Join-Path $fontDir $ttf.Name
        Copy-Item $ttf.FullName $dest -Force
        $fc = New-Object System.Drawing.Text.PrivateFontCollection
        $fc.AddFontFile($dest)
        $face = $fc.Families[0].Name
        $style = [IO.Path]::GetFileNameWithoutExtension($ttf.Name) -replace '^.*-', ''
        New-ItemProperty -Path $fontKey -Name "$face $style (TrueType)" -Value $dest -PropertyType String -Force | Out-Null
        $fc.Dispose()
    }
    Remove-Item $tmp -Recurse -Force
    Write-Note 'installed (restart Windows Terminal to pick it up)'
}

if (-not $SoftwareFunctionsOnly) {
    Write-Step 'winget packages (core)'
    Install-WingetPackages $WingetPackages
    Write-Step 'presets'
    Initialize-Presets
    $chosen = @(Get-ChosenPresets)
    foreach ($name in $chosen) {
        Write-Note "-- $name"
        Install-WingetPackages @($Presets[$name].Winget | Where-Object { $_ })
    }
    Write-Step 'other tools'
    $extras = @($CoreExtras) + @(foreach ($name in $chosen) { $Presets[$name].Extras })
    Install-Extras @($extras | Where-Object { $_ } | Select-Object -Unique)
    Install-NerdFont
}
