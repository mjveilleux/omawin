# install.ps1 step: the core winget packages and the chosen presets' (packages.ps1), other
# installers, the Nerd Font. Skipped with -LinkOnly.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Stamp and the package lists
# (packages.ps1) are in scope. bin/preset.ps1 dot-sources it with $SoftwareFunctionsOnly set, to
# install one preset with the same functions.

# Installs $Ids with winget, up to $Width at once, each with its output captured (to the log, and
# its last lines on a failure). Shows one line of progress while they run (a spinner in the
# fancy face, a line per package in the plain one). Returns the ones that failed.
function Invoke-WingetBatch([string]$Winget, [string[]]$Ids, [int]$Width) {
    $queue = New-Object Collections.Generic.Queue[string]
    foreach ($id in $Ids) { $queue.Enqueue($id) }
    $total = $queue.Count
    if (-not $total) { return }
    $running = @{}
    $failed = @()
    $done = 0
    $t0 = Get-Date
    $frame = 0
    if ($UI.Fancy) { [Console]::Out.Write($UI.C.HideCursor) }
    try {
        while ($queue.Count -or $running.Count) {
            while ($queue.Count -and $running.Count -lt $Width) {
                $id = $queue.Dequeue()
                $wingetArgs = @('install', '--id', $id, '--exact', '--silent', '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
                if ($WingetOverrides[$id]) { $wingetArgs += '--override', $WingetOverrides[$id] }
                $psi = New-Object Diagnostics.ProcessStartInfo
                $psi.FileName = $Winget
                $psi.Arguments = (@($wingetArgs | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
                $psi.UseShellExecute = $false
                $psi.RedirectStandardOutput = $true
                $psi.RedirectStandardError = $true
                $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
                $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
                $p = [Diagnostics.Process]::Start($psi)
                $running[$id] = @{ P = $p; Out = $p.StandardOutput.ReadToEndAsync(); Err = $p.StandardError.ReadToEndAsync(); Start = Get-Date }
                if (-not $UI.Fancy) { Write-Note "installing $id" }
            }
            foreach ($id in @($running.Keys)) {
                $r = $running[$id]
                if (-not $r.P.HasExited) { continue }
                $r.P.WaitForExit()
                $output = $r.Out.Result + $r.Err.Result
                $seconds = ((Get-Date) - $r.Start).TotalSeconds
                Write-Log ("    $ winget install --id $id  (exit $($r.P.ExitCode), $([int]$seconds) s)")
                $running.Remove($id)
                $done++
                if ($UI.Fancy) { [Console]::Out.Write($UI.C.Clear) }
                if ($r.P.ExitCode -eq 0) { Write-Done $id $seconds }
                else {
                    if ($output.Trim()) { Write-Log ($output.Trim() -replace "`r", '') }
                    $failed += [pscustomobject]@{ Id = $id; ExitCode = $r.P.ExitCode; Output = $output }
                }
            }
            if ($UI.Fancy -and $running.Count) {
                $names = (@($running.Keys) | Sort-Object) -join ', '
                $room = [Math]::Max(10, $UI.Width - 40)
                if ($names.Length -gt $room) { $names = $names.Substring(0, $room - 3) + '...' }
                $label = '{0}/{1} done, {2} at once  {3}' -f $done, $total, $running.Count, $names
                [Console]::Out.Write(("{0}      {1}{2}{3} {4}  {5}{6}{3}" -f $UI.C.Clear, $UI.C.Accent, $UI.G.Spin[$frame % $UI.G.Spin.Count], $UI.C.Reset, $label, $UI.C.Dim, (Format-Duration ((Get-Date) - $t0).TotalSeconds)))
                $frame++
            }
            Start-Sleep -Milliseconds 100
        }
    } finally { if ($UI.Fancy) { [Console]::Out.Write($UI.C.Clear + $UI.C.ShowCursor) } }
    $failed
}

function Install-WingetPackages([string[]]$Ids) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Warn 'winget not found. Install "App Installer" from the Microsoft Store, then re-run.'
        return
    }
    $winget = (Get-Command winget).Source

    # What's already here, in one call (winget export lists every installed package it knows),
    # instead of a `winget list` per package (1-2 s each). If that fails, each is asked after all.
    $installed = $null
    $export = Join-Path $env:TEMP "omawin-winget-$PID.json"
    try {
        $null = & $winget export --output $export --accept-source-agreements --disable-interactivity 2>$null
        if (Test-Path $export) {
            $installed = @{}
            foreach ($source in (Get-Content $export -Raw | ConvertFrom-Json).Sources) {
                foreach ($package in $source.Packages) { $installed[$package.PackageIdentifier] = $true }
            }
        }
    } catch { $installed = $null } finally { Remove-Item $export -ErrorAction SilentlyContinue }

    $todo = New-Object Collections.Generic.List[string]
    foreach ($id in $Ids) {
        $command = $WingetCommands[$id]
        if ($command -and (Get-Command $command -ErrorAction SilentlyContinue)) { Write-Ok "$id ($command)"; continue }
        if ($installed) { $listed = $installed.ContainsKey($id) }
        else {
            winget list --id $id --exact --accept-source-agreements *> $null
            $listed = $LASTEXITCODE -eq 0
        }
        if ($listed) { Write-Ok $id } else { $todo.Add($id) }
    }

    # Several at once: downloads and portable tools overlap. Windows runs one MSI installer at a
    # time, so one that finds another in progress fails; anything that failed gets one more go at
    # the end, alone, before it counts as failed.
    $failed = @(Invoke-WingetBatch $winget @($todo) 4)
    if ($failed) { $failed = @(Invoke-WingetBatch $winget @($failed | ForEach-Object Id) 1) }
    foreach ($f in $failed) { Write-Warn "winget install $($f.Id) exited with $($f.ExitCode)"; Write-OutputTail $f.Output }
    Write-OkCount
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
