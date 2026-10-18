# install.ps1 step: the core winget packages and the chosen presets' (packages.ps1), other
# installers, the Nerd Font, then Neovim's and Neovide's config. -LinkOnly skips the installs and
# keeps the config.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Stamp and the package lists
# (packages.ps1) are in scope. install.ps1 (to plan the admin step) and bin/preset.ps1 (to install
# one preset) dot-source it with $SoftwareFunctionsOnly set, for the functions alone.

# The winget packages installed on this PC, from one `winget export` (every installed package it
# knows) rather than a `winget list` per package (1-2 s each). Asked once per install; $null if
# winget can't say, and then each package is asked after all. -Fresh asks again.
function Get-WingetInstalled([switch]$Fresh) {
    if ($script:WingetInstalledDone -and -not $Fresh) { return $script:WingetInstalled }
    $script:WingetInstalledDone = $true
    $script:WingetInstalled = $null
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) { return $null }
    $export = Join-Path $env:TEMP "omawin-winget-$PID.json"
    try {
        $null = & $winget.Source export --output $export --accept-source-agreements --disable-interactivity 2>$null
        if (Test-Path $export) {
            $found = @{}
            foreach ($source in (Get-Content $export -Raw | ConvertFrom-Json).Sources) {
                foreach ($package in $source.Packages) { $found[$package.PackageIdentifier] = $true }
            }
            $script:WingetInstalled = $found
        }
    } catch { } finally { Remove-Item $export -ErrorAction SilentlyContinue }
    $script:WingetInstalled
}

# Installed: its command is on the PATH ($WingetCommands: however it got there), or winget has it.
function Test-PackageInstalled([string]$Id, $Installed) {
    $command = $WingetCommands[$Id]
    if ($command -and (Get-Command $command -ErrorAction SilentlyContinue)) { return $true }
    if ($Installed) { return $Installed.ContainsKey($Id) }
    & { $ErrorActionPreference = 'Continue'; winget list --id $Id --exact --accept-source-agreements *> $null }
    $LASTEXITCODE -eq 0
}

# A package's short name for the screen: its id without the publisher (Git.Git -> Git).
function Get-PackageName([string]$Id) {
    $parts = $Id -split '\.'
    if ($parts.Count -lt 2) { return $Id }
    $name = ($parts[1..($parts.Count - 1)] -join '.') -replace '\.(MSVC|LTS)$'
    # A name that says nothing on its own (GitHub.cli) keeps its publisher.
    if ($name -in 'cli', 'app') { $parts[0] } else { $name }
}

# Installs $Ids with winget, up to $Width at once, each with its output captured (to the log, and
# its last lines on a failure). $Tick runs between looks (the admin step's progress). Returns the
# ones that failed.
function Invoke-WingetBatch([string]$Winget, [string[]]$Ids, [int]$Width, [scriptblock]$Tick) {
    $queue = New-Object Collections.Generic.Queue[string]
    foreach ($id in $Ids) { $queue.Enqueue($id) }
    $total = $queue.Count
    if (-not $total) { return }
    $running = @{}
    $failed = @()
    $done = 0
    $t0 = Get-Date
    $frame = 0
    $line = $UI.Fancy -and -not $UI.Board
    if ($line) { [Console]::Out.Write($UI.C.HideCursor) }
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
                Set-BoardPackage $id 'run'
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
                if ($line) { [Console]::Out.Write($UI.C.Clear) }
                # 0x8A15002B / 0x8A150061: winget found it installed after all (its export can miss one);
                # 0x8A150109: installed, Windows wants a restart.
                if ($r.P.ExitCode -in 0, -1978335189, -1978335135, -1978334967) { Set-BoardPackage $id 'done'; Write-Done $id $seconds }
                else {
                    if ($output.Trim()) { Write-Log ($output.Trim() -replace "`r", '') }
                    Set-BoardPackage $id 'wait'
                    $failed += [pscustomobject]@{ Id = $id; ExitCode = $r.P.ExitCode; Output = $output }
                }
            }
            if ($Tick) { & $Tick }
            if ($line -and $running.Count) {
                $names = (@($running.Keys) | Sort-Object) -join ', '
                $room = [Math]::Max(10, $UI.Width - 40)
                if ($names.Length -gt $room) { $names = $names.Substring(0, $room - 3) + '...' }
                $label = '{0}/{1} done, {2} at once  {3}' -f $done, $total, $running.Count, $names
                [Console]::Out.Write(("{0}      {1}{2}{3} {4}  {5}{6}{3}" -f $UI.C.Clear, $UI.C.Accent, $UI.G.Spin[$frame % $UI.G.Spin.Count], $UI.C.Reset, $label, $UI.C.Dim, (Format-Duration ((Get-Date) - $t0).TotalSeconds)))
                $frame++
            }
            Start-Sleep -Milliseconds 100
        }
    } finally { if ($line) { [Console]::Out.Write($UI.C.Clear + $UI.C.ShowCursor) } }
    $failed
}

function Install-WingetPackages([string[]]$Ids) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Warn 'winget not found. Install "App Installer" from the Microsoft Store, then re-run.'
        return
    }
    $winget = (Get-Command winget).Source
    $installed = Get-WingetInstalled

    $todo = New-Object Collections.Generic.List[string]
    foreach ($id in $Ids) {
        if (Test-PackageInstalled $id $installed) { Set-BoardPackage $id 'have'; Write-Ok $id }
        else { Set-BoardPackage $id 'wait'; $todo.Add($id) }
    }

    # The whole-PC ones the admin step took (install/admin.ps1) install there, alongside these.
    $admin = @()
    if ($AdminBatch -and $AdminBatch.Plan.Packages) { $admin = @($todo | Where-Object { $AdminBatch.Plan.Packages -contains $_ }) }
    foreach ($id in $admin) { Set-BoardPackage $id 'wait' -Admin }
    $mine = @($todo | Where-Object { $admin -notcontains $_ })
    $seen = @{}
    $tick = {
        if (-not $admin) { return }
        Update-AdminStatus
        foreach ($id in $admin) {
            $state = Get-AdminState $id
            if ($seen[$id] -eq $state) { continue }
            $seen[$id] = $state
            switch ($state) {
                'run' { Set-BoardPackage $id 'run' -Admin }
                'done' { Set-BoardPackage $id 'done' -Admin; Write-Log "    done  $id (admin step)" }
            }
        }
        Set-BoardPackageNote $(if ($admin) { "$(@($admin | Where-Object { (Get-AdminState $_) -in 'wait', 'run' }).Count) left in the admin step" })
    }  # run from Invoke-WingetBatch and Wait-AdminItems, called from here: $admin and $seen are in reach

    # Several at once: downloads and portable tools overlap. Windows runs one MSI installer at a
    # time, so one that finds another in progress fails; anything that failed gets one more go at
    # the end, alone, before it counts as failed.
    $failed = @(Invoke-WingetBatch $winget $mine 4 $tick)
    if ($admin) { Wait-AdminItems $admin $tick }
    # The admin step couldn't: this account installs them, each with its own prompt.
    foreach ($id in $admin) {
        if ((Get-AdminState $id) -ne 'done') {
            Write-Log "    admin step: $id $(Get-AdminState $id) $(Get-AdminText $id)"
            $failed += [pscustomobject]@{ Id = $id; ExitCode = 'in the admin step'; Output = (Get-AdminText $id) }
        }
    }
    Set-BoardPackageNote $null
    if ($failed) { $failed = @(Invoke-WingetBatch $winget @($failed | ForEach-Object Id) 1) }
    foreach ($f in $failed) { Set-BoardPackage $f.Id 'fail'; Write-Warn "winget install $($f.Id) exited with $($f.ExitCode)"; Write-OutputTail $f.Output }
    Write-OkCount
    Update-SessionPath
    Add-ToolPaths @(Get-WantedTools)
}

# The commands the core and the chosen presets are for (packages.ps1's Tools); bun is the core's.
function Get-WantedTools {
    @('bun') + @(foreach ($name in Get-ChosenPresets) { $Presets[$name].Tools }) |
        Where-Object { $_ } | Select-Object -Unique
}

# A tool its package put off the PATH (bun's winget package in its own folder; shinchiro.mpv's
# installer in Program Files\MPV Player, for every account) is found where installers put things,
# and that folder goes on this account's PATH. Runs on every install and update, so a package that
# changes where it installs is caught on PCs that already have it. Nothing to do: no search.
function Add-ToolPaths([string[]]$Tools) {
    $added = $false
    foreach ($tool in $Tools) {
        if (Get-Command $tool -ErrorAction SilentlyContinue) { continue }
        $exe = @(
            foreach ($root in $env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs") {
                if ($root -and (Test-Path $root)) { Get-ChildItem $root -Filter "$tool.exe" -Recurse -Depth 2 -ErrorAction SilentlyContinue }
            }
            Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter "$tool.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue
        ) | Select-Object -First 1
        if ($exe -and (Add-UserPath $exe.DirectoryName)) { Write-Note "$tool on the PATH: $($exe.DirectoryName)"; $added = $true }
    }
    if ($added) { Update-SessionPath }
}

function Install-Extras([string[]]$Names) {
    foreach ($name in $Names) { Add-InstallItem 'Tools' "tool:$name" $name 'wait' }
    foreach ($name in $Names) {
        if (Get-Command $name -ErrorAction SilentlyContinue) { Write-Note "ok  $name"; Add-InstallItem 'Tools' "tool:$name" $name 'have'; continue }
        Write-Note "installing $name"
        Add-InstallItem 'Tools' "tool:$name" $name 'run'
        try { & $ExtraInstalls[$name] | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } }; Add-InstallItem 'Tools' "tool:$name" $name 'done' }
        catch { Write-Warn "$name failed: $_"; Add-InstallItem 'Tools' "tool:$name" $name 'fail' }
        Update-SessionPath
    }
}

# The presets this PC picked, minus names packages.ps1 no longer has (said once, not fatal).
function Get-ChosenPresets {
    foreach ($name in Get-Presets) {
        if ($Presets.Contains($name)) { $name } else { Write-Warn "preset '$name' (in $PresetsFile) doesn't exist any more; omawin preset remove $name" }
    }
}

# Every winget id this install wants: the core and the chosen presets'.
function Get-WantedPackages {
    @($WingetPackages) + @(foreach ($name in Get-ChosenPresets) { $Presets[$name].Winget }) | Where-Object { $_ } | Select-Object -Unique
}

# First install: no presets file yet. Ask which presets (a person is there), else start with none.
# install.ps1 asks this before anything starts, with its other questions.
function Initialize-Presets {
    if (Test-Path $PresetsFile) { return }
    $picked = @()
    if (Test-Interactive) {
        Write-Note 'Presets: opt-in groups on top of the core'
        foreach ($name in $Presets.Keys) { Write-Note ("  {0,-15} {1}" -f $name, $Presets[$name].Summary) }
        $answer = Read-InstallAnswer 'Which presets? (names separated by spaces, Enter for none; omawin preset add <name> later)'
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
    $font = "$NerdFont Nerd Font"
    if ($installed -or $machine) { Write-Note 'ok  already installed'; Add-InstallItem 'Tools' 'font' $font 'have'; return }
    Add-InstallItem 'Tools' 'font' $font 'run'

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
    Add-InstallItem 'Tools' 'font' $font 'done'
}

if (-not $SoftwareFunctionsOnly) {
    if (-not $LinkOnly) {
        Write-Step 'winget packages'
        Initialize-Presets
        $chosen = @(Get-ChosenPresets)
        Install-WingetPackages @(Get-WantedPackages)
        Write-Step 'other tools'
        $extras = @($CoreExtras) + @(foreach ($name in $chosen) { $Presets[$name].Extras })
        Install-Extras @($extras | Where-Object { $_ } | Select-Object -Unique)
        Install-NerdFont
    } else {
        # An update installs nothing, but a tool a package moved off the PATH still goes back on.
        Add-ToolPaths @(Get-WantedTools)
    }

    Write-Step 'Neovim and Neovide'
    Set-Junction "$env:LOCALAPPDATA\nvim" "$Repo\nvim"
    # Neovide uses the same Neovim config; this is only its own settings (config.toml).
    Set-Junction "$env:APPDATA\neovide" "$Repo\neovide"

    Write-Step 'R / RStudio'
    # A one-line ~/.Rprofile (R's ~ is Documents) sourcing r/rprofile.R: RStudio follows the
    # desktop theme. An .Rprofile of your own (more than that one line) is left alone.
    $rprofile = Join-Path ([Environment]::GetFolderPath('MyDocuments')) '.Rprofile'
    $source = "source('$("$Repo\r\rprofile.R" -replace '\\', '/')')"
    $current = if (Test-Path $rprofile) { (Get-Content $rprofile -Raw).Trim() }
    if ($current -eq $source) { Write-Note "ok  $rprofile" }
    elseif (-not $current -or $current -match '^source\(.*rprofile\.R.*\)$') { Set-Content $rprofile $source -Encoding utf8; Write-Note "wrote $rprofile" }
    else { Write-Note "$rprofile is yours; add this line to it for themes: $source" }
}
