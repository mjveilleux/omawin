# install.ps1 step: GlazeWM + Zebar (and GlazeWM at login), Handy dictation.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'GlazeWM + Zebar'
# GlazeWM reads ~/.glzr/glazewm/config.yaml, filled in from glazewm/config.yaml (a template)
# with the theme's colors; so is Windows Terminal's scheme (themes/render.ps1).
& "$Repo\themes\render.ps1"
Write-Note "ok  ~/.glzr/glazewm/config.yaml rendered from glazewm/config.yaml"
Add-InstallItem 'Settings' 'glazewm-config' 'GlazeWM config' 'have'
Set-Junction "$HOME\.glzr\zebar" "$Repo\zebar"
# Zebar's client library, built here so the bar never fetches it at sign-in (zebar/build-client.ps1).
# PowerShell 7's, as every script of omawin's outside install/ (the software step put it in).
$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
$pwsh = if ($pwsh) { $pwsh.Source } else { "$env:ProgramFiles\PowerShell\7\pwsh.exe" }
if (Test-Path $pwsh) {
    $client = & $pwsh -NoLogo -NoProfile -File "$Repo\zebar\build-client.ps1" 2>&1 | Select-Object -Last 1
    Write-Note "$client  Zebar's client (zebar/tokyo-islands/lib)"
} else { Write-Note "    Zebar's client: no PowerShell 7 yet (the bar loads it from esm.sh)" }
# Start GlazeWM at sign-in (it starts Zebar itself, see glazewm/config.yaml), straight away.
Invoke-InstallItem 'Desktop' 'GlazeWM at sign-in' { & "$Repo\install\login-start.ps1" }
# Our GlazeWM fork, signed on this PC, which can start before Explorer (install/glazewm-fork.ps1),
# and our Zebar fork, the bar on this account's ports (install/zebar-fork.ps1). The admin step put
# them in (install/admin.ps1); declined there, they're left out (nothing asks after that prompt),
# and on its own (-Step desktop) each asks for admin here. Without them the official ones stay:
# GlazeWM starts a few seconds later, one account's bar at a time.
$forkNames = @{ 'glazewm-fork' = 'GlazeWM'; 'zebar-fork' = 'Zebar' }
foreach ($fork in 'glazewm-fork', 'zebar-fork') {
    $name = $forkNames[$fork]
    if ($AdminBatch -and $AdminBatch.Plan.Forks -contains $fork) {
        if ((Get-AdminState $fork) -in 'wait', 'run') { Write-Note "omawin's $name from the admin step ..." }
        Wait-AdminItems @($fork)
        $text = Get-AdminText $fork
        switch (Get-AdminState $fork) {
            'done' { Write-Note ($text -replace '^installed\s+', ''); Add-InstallItem 'Desktop' $fork "omawin's $name" $(if ($text -like 'installed*') { 'done' } else { 'have' }) -Admin }
            'skip' { Write-Note "official $name kept: $text"; Add-InstallItem 'Desktop' $fork "official $name" 'have' -Admin }
            default { Write-Warn "$name fork: $(if ($text) { $text } else { 'the admin step stopped' }) (the official $name stays)"; Add-InstallItem 'Desktop' $fork "omawin's $name" 'fail' -Admin }
        }
        $after = @{}
        if ($fork -eq 'glazewm-fork') { $after.Restart = $text -like 'installed*' }
        try { & "$Repo\install\$fork.ps1" -After @after } catch { Write-Log "    $fork -After: $_" }
    } else {
        $current = try { & "$Repo\install\$fork.ps1" -Check } catch { "skip  $_" }
        if ($current -ne 'needed') { Write-Note $current; Add-InstallItem 'Desktop' $fork $(if ($current -like 'ok*fork*') { "omawin's $name" } else { "official $name" }) 'have'; continue }
        if ($env:OMAWIN_NO_ADMIN) {
            Write-Note "official $name kept: omawin's takes admin (install.ps1 -Step desktop asks once)"
            Add-InstallItem 'Desktop' $fork "official $name" 'have'
            continue
        }
        Invoke-InstallItem 'Desktop' "omawin's $name" { & "$Repo\install\$fork.ps1" }
    }
}
# Window animations snapshot windows as they move: Windows' privacy switch for capturing without
# a border, on, or each window flashes a yellow outline (install/capture-borderless.ps1).
Invoke-InstallItem 'Desktop' 'window snapshots without a border' { & "$Repo\install\capture-borderless.ps1" }
# This account's own ports (bin/ports.ps1), once both forks are in: other accounts signed in
# on the PC have theirs. They take effect at the next sign-in.
Invoke-InstallItem 'Desktop' "this account's ports" { & "$Repo\bin\ports.ps1" -Set }

Write-Step 'Handy (dictation on Win+H)'
# GlazeWM runs `handy --toggle-transcription` on Win+H, so Handy must be on PATH.
$handyDir = "$env:LOCALAPPDATA\Handy"
if (Test-Path "$handyDir\handy.exe") {
    if (Add-UserPath '%LOCALAPPDATA%\Handy') { Write-Note 'added %LOCALAPPDATA%\Handy to PATH' }
    else { Write-Note 'ok  Handy on PATH' }
    # Settings: no overlay (the Zebar bar shows the listening animation), tap to toggle,
    # its own shortcuts parked out of the way, start hidden at login, and the text typed
    # straight in (paste method "direct") rather than pasted through the clipboard with
    # Ctrl+V, which terminals (noctty) leave to the program inside. Handy writes its
    # settings file on first launch, so launch it once if it isn't there yet.
    $store = "$env:APPDATA\com.pais.handy\settings_store.json"
    if (-not (Test-Path $store)) {
        Start-Process "$handyDir\handy.exe"
        for ($i = 0; $i -lt 40 -and -not (Test-Path $store); $i++) { Start-Sleep -Milliseconds 500 }
        Start-Sleep 2
        Get-Process handy -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
    }
    # A speech model: without one Win+H does nothing at all (Handy only logs "no model can
    # transcribe it"). With none picked yet, Parakeet V3, Handy's pick for English, fetched from
    # where Handy fetches it and checked against the SHA-256 Handy lists for it (about 460 MB,
    # once per account). One you picked in Handy is left alone.
    $models = "$env:APPDATA\com.pais.handy\models"
    $model = 'parakeet-tdt-0.6b-v3-int8'
    $picked = (Test-Path $store) -and (Get-Content $store -Raw | ConvertFrom-Json).settings.selected_model
    if (-not $picked -and -not (Test-Path "$models\$model\encoder-model.int8.onnx")) {
        Invoke-InstallItem 'Desktop' 'speech model (Parakeet V3)' {
            $archive = Join-Path $env:TEMP "omawin-$model.tar.gz"
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest 'https://blob.handy.computer/parakeet-v3-int8.tar.gz' -OutFile $archive -UseBasicParsing
            if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '43D37191602727524A7D8C6DA0EEF11C4BA24320F5B4730F1A2497BEFC2EFA77') {
                Remove-Item $archive; throw 'the download is not the one Handy lists (SHA-256 mismatch)'
            }
            New-Item -ItemType Directory -Force -Path $models | Out-Null
            # (the archive's ._* files are macOS's leftovers)
            & tar.exe -xzf $archive -C $models --exclude '._*'
            if ($LASTEXITCODE -ne 0) { throw "tar couldn't unpack $archive" }
            Remove-Item $archive
        }
    }
    if (Test-Path $store) {
        $j = Get-Content $store -Raw | ConvertFrom-Json; $s = $j.settings
        # Handy reads its settings when it starts: picking the model restarts a running one.
        $restart = $false
        if (-not $s.selected_model -and (Test-Path "$models\$model\encoder-model.int8.onnx")) {
            $s.selected_model = 'parakeet-tdt-0.6b-v3'
            $running = @(Get-Process handy -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId)
            if ($running) { $running | Stop-Process -Force; Start-Sleep 1; $restart = $true }
        }
        $s.overlay_style = 'none'; $s.shortcut_activation = 'toggle'
        $s.bindings.transcribe.current_binding = 'ctrl+alt+shift+f9'
        $s.bindings.transcribe_with_post_process.current_binding = 'ctrl+alt+shift+f10'
        $s.autostart_enabled = $true; $s.start_hidden = $true
        $s.paste_method = 'direct'
        [IO.File]::WriteAllText($store, ($j | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
        if ($restart) { Start-Process "$handyDir\handy.exe" }
        Write-Note "configured Handy$(if (-not $s.selected_model) { '; open it from the tray once to pick a speech model' })"
        Add-InstallItem 'Desktop' 'dictation' 'dictation (Win+H)' 'have'
    }
} elseif ((Get-Presets) -contains 'dictation') { Write-Warn 'Handy not installed; re-run after installing it' }
else { Write-Note 'skipped: not installed (omawin preset add dictation)' }
