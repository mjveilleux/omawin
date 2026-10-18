# install.ps1 step: GlazeWM + Zebar (and GlazeWM at login), Handy dictation.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'GlazeWM + Zebar'
# GlazeWM reads ~/.glzr/glazewm/config.yaml, filled in from glazewm/config.yaml (a template)
# with the theme's colors; so is Windows Terminal's scheme (themes/render.ps1).
& "$Repo\themes\render.ps1"
Write-Note "ok  ~/.glzr/glazewm/config.yaml rendered from glazewm/config.yaml"
Set-Junction "$HOME\.glzr\zebar" "$Repo\zebar"
# Start GlazeWM at sign-in (it starts Zebar itself, see glazewm/config.yaml), straight away.
& "$Repo\install\login-start.ps1" | ForEach-Object { Write-Note $_ }
# Our GlazeWM fork, signed on this PC, which can start before Explorer (install/glazewm-fork.ps1),
# and our Zebar fork, the bar on this account's ports (install/zebar-fork.ps1). The admin step put
# them in (install/admin.ps1), or, when it didn't run, each asks for admin here. Without them the
# official ones stay: GlazeWM starts a few seconds later, one account's bar at a time.
$forkNames = @{ 'glazewm-fork' = 'GlazeWM'; 'zebar-fork' = 'Zebar' }
foreach ($fork in 'glazewm-fork', 'zebar-fork') {
    $name = $forkNames[$fork]
    if ($AdminBatch -and $AdminBatch.Plan.Forks -contains $fork) {
        if ((Get-AdminState $fork) -in 'wait', 'run') { Write-Note "omawin's $name from the admin step ..." }
        Wait-AdminItems @($fork)
        $text = Get-AdminText $fork
        switch (Get-AdminState $fork) {
            'done' { Write-Note ($text -replace '^installed\s+', '') }
            'skip' { Write-Note "official $name kept: $text" }
            default { Write-Warn "$name fork: $(if ($text) { $text } else { 'the admin step stopped' }) (the official $name stays)" }
        }
        $after = @{}
        if ($fork -eq 'glazewm-fork') { $after.Restart = $text -like 'installed*' }
        try { & "$Repo\install\$fork.ps1" -After @after } catch { Write-Log "    $fork -After: $_" }
    } else {
        $current = try { & "$Repo\install\$fork.ps1" -Check } catch { "skip  $_" }
        if ($current -ne 'needed') { Write-Note $current; continue }
        try { & "$Repo\install\$fork.ps1" | ForEach-Object { Write-Note $_ } } catch { Write-Warn "$name fork: $_ (the official $name stays)" }
    }
}
# This account's own ports (bin/ports.ps1), once both forks are in: other accounts signed in
# on the PC have theirs. They take effect at the next sign-in.
& "$Repo\bin\ports.ps1" -Set | ForEach-Object { Write-Note $_ }

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
    if (Test-Path $store) {
        $j = Get-Content $store -Raw | ConvertFrom-Json; $s = $j.settings
        $s.overlay_style = 'none'; $s.shortcut_activation = 'toggle'
        $s.bindings.transcribe.current_binding = 'ctrl+alt+shift+f9'
        $s.bindings.transcribe_with_post_process.current_binding = 'ctrl+alt+shift+f10'
        $s.autostart_enabled = $true; $s.start_hidden = $true
        $s.paste_method = 'direct'
        [IO.File]::WriteAllText($store, ($j | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
        Write-Note 'configured Handy; open it from the tray once to download a speech model'
    }
} elseif ((Get-Presets) -contains 'dictation') { Write-Warn 'Handy not installed; re-run after installing it' }
else { Write-Note 'skipped: not installed (omawin preset add dictation)' }
