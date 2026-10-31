# The tools bundle's heavy part, in a PowerShell of its own so the install goes on meanwhile
# (install/software.ps1, Start-ToolBundle): the zips downloaded all at once (curl --parallel),
# then each checked against the release's list and unpacked, side by side (tar.exe). A tool says
# it's done with <Done>\<name>.json (Name, Ok, Error, Seconds), so the install can take each one
# as it lands, and leave the ones marked later (zig) to finish after it.
#
#   -Plan    a JSON list: Name, Url (or none: Zip is already a file), Zip, Sha, Dir, Version, Merge,
#            Sfx (Zip is a 7-Zip self-extractor: run to unpack, not tar), Parts (a list of Url, Zip,
#            Sha: the tool's tar.zst parts, taken instead of Zip and unpacked at once, a tar each)
#   -Done    the folder for the done files; this script removes its own work files when it ends
param([Parameter(Mandatory)][string]$Plan, [Parameter(Mandatory)][string]$Done)
$ErrorActionPreference = 'Stop'
$tools = @(Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Split-Path $Plan

try {
    # Each file to fetch: a tool's parts when it has them, else its one file.
    $remote = @($tools | ForEach-Object { if ($_.Parts) { $_.Parts } else { $_ } } | Where-Object Url)
    if ($remote) {
        # Forward slashes: a backslash in a curl config's quotes is an escape.
        $config = "$Plan.curl.txt"
        $lines = foreach ($t in $remote) { "url = `"$($t.Url)`""; "output = `"$($t.Zip -replace '\\', '/')`"" }
        [IO.File]::WriteAllLines($config, [string[]]$lines)
        & "$env:WINDIR\System32\curl.exe" -fsSL --retry 3 --parallel --parallel-max 8 -K $config 2>$null
    }

    $tar = "$env:WINDIR\System32\tar.exe"
    $tools | ForEach-Object -ThrottleLimit 12 -Parallel {
        $t = $_
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $out = [ordered]@{ Name = $t.Name; Ok = $false; Error = $null; Seconds = 0 }
        try {
            foreach ($file in $(if ($t.Parts) { $t.Parts } else { $t })) {
                if (-not (Test-Path -LiteralPath $file.Zip)) { throw 'not downloaded' }
                if ((Get-FileHash -LiteralPath $file.Zip -Algorithm SHA256).Hash.ToLower() -ne $file.Sha) { throw "doesn't match the release's list" }
            }
            # merge: unpacked into a folder that has things of yours in it (Neovim's data folder):
            # added to it, never moved aside.
            if (-not $t.Merge -and (Test-Path -LiteralPath $t.Dir)) {
                # An older copy of ours: moved out of the way (a running one keeps its files), removed after.
                try { Rename-Item -LiteralPath $t.Dir -NewName "$(Split-Path $t.Dir -Leaf).old-$using:stamp" -ErrorAction Stop }
                catch { throw 'in use: close it and run install.ps1 again' }
            }
            New-Item -ItemType Directory -Force $t.Dir | Out-Null
            if ($t.Parts) {
                # The parts at once, a tar each: writing files is what takes the time (Windows
                # Defender looks at each one), and each tar writes its own share on its own core.
                $tarExe = $using:tar
                $tars = @($t.Parts | ForEach-Object { Start-Process -FilePath $tarExe -ArgumentList '-xf', "`"$($_.Zip)`"", '-C', "`"$($t.Dir)`"" -WindowStyle Hidden -PassThru })
                $tars | ForEach-Object { $_.WaitForExit() }
                if ($bad = $tars | Where-Object ExitCode) { throw "unpacking failed ($(@($bad)[0].ExitCode))" }
            } elseif ($t.Sfx) {
                # 7-Zip's self-extractor: -o the folder, -y no questions.
                $p = Start-Process -FilePath $t.Zip -ArgumentList "-o`"$($t.Dir)`"", '-y' -WindowStyle Hidden -Wait -PassThru
                if ($p.ExitCode) { throw "unpacking failed ($($p.ExitCode))" }
            } else {
                & $using:tar -xf $t.Zip -C $t.Dir
                if ($LASTEXITCODE) { throw "unpacking failed ($LASTEXITCODE)" }
            }
            # Git for Windows' portable build finishes its setup (Git Bash's /etc and /dev) with
            # post-install.bat, which removes itself when it's done.
            $postInstall = Join-Path $t.Dir 'post-install.bat'
            $gitBash = Join-Path $t.Dir 'git-bash.exe'
            if ((Test-Path -LiteralPath $postInstall) -and (Test-Path -LiteralPath $gitBash)) {
                Start-Process $gitBash -ArgumentList '--no-needs-console', '--hide', '--no-cd', '--command=post-install.bat' -WorkingDirectory $t.Dir -WindowStyle Hidden -Wait
            }
            Set-Content -LiteralPath (Join-Path $t.Dir '.omawin-version') "$($t.Version) $($t.Sha)"
            if (-not $t.Merge) {
                Get-ChildItem (Split-Path $t.Dir) -Filter "$(Split-Path $t.Dir -Leaf).old-*" -Directory -ErrorAction SilentlyContinue |
                    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            }
            $out.Ok = $true
        } catch { $out.Error = "$($_.Exception.Message)" }
        $out.Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1)
        # Written whole, then renamed: the install never reads half a file.
        $file = Join-Path $using:Done "$($t.Name).json"
        [pscustomobject]$out | ConvertTo-Json | Set-Content -LiteralPath "$file.tmp" -Encoding utf8
        Move-Item -LiteralPath "$file.tmp" -Destination $file -Force
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
