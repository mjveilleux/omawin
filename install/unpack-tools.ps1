# The tools bundle's heavy part, in a PowerShell of its own so the install goes on meanwhile
# (install/software.ps1, Start-ToolBundle): the zips downloaded all at once (curl --parallel),
# then each checked against the release's list and unpacked, side by side (tar.exe). A tool says
# it's done with <Done>\<name>.json (Name, Ok, Error, Seconds), so the install can take each one
# as it lands, and leave the ones marked later (zig) to finish after it.
#
#   -Plan    a JSON list: Name, Url (or none: Zip is already a file), Zip, Sha, Dir, Version
#   -Done    the folder for the done files; this script removes its own work files when it ends
param([Parameter(Mandatory)][string]$Plan, [Parameter(Mandatory)][string]$Done)
$ErrorActionPreference = 'Stop'
$tools = @(Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Split-Path $Plan

try {
    $remote = @($tools | Where-Object Url)
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
            if (-not (Test-Path -LiteralPath $t.Zip)) { throw 'not downloaded' }
            if ((Get-FileHash -LiteralPath $t.Zip -Algorithm SHA256).Hash.ToLower() -ne $t.Sha) { throw "doesn't match the release's list" }
            if (Test-Path -LiteralPath $t.Dir) {
                # An older copy of ours: moved out of the way (a running one keeps its files), removed after.
                try { Rename-Item -LiteralPath $t.Dir -NewName "$(Split-Path $t.Dir -Leaf).old-$using:stamp" -ErrorAction Stop }
                catch { throw 'in use: close it and run install.ps1 again' }
            }
            New-Item -ItemType Directory -Force $t.Dir | Out-Null
            & $using:tar -xf $t.Zip -C $t.Dir
            if ($LASTEXITCODE) { throw "unpacking failed ($LASTEXITCODE)" }
            Set-Content -LiteralPath (Join-Path $t.Dir '.omawin-version') "$($t.Version) $($t.Sha)"
            Get-ChildItem (Split-Path $t.Dir) -Filter "$(Split-Path $t.Dir -Leaf).old-*" -Directory -ErrorAction SilentlyContinue |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
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
