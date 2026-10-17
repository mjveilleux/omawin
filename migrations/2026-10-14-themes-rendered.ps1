# Theme colors moved out of tracked files: glazewm/config.yaml became a template and Windows
# Terminal uses the scheme "omawin" from a fragment. Render both now, which also replaces the
# ~/.glzr/glazewm junction (to this repo) with the rendered config.
& (Join-Path (Split-Path $PSScriptRoot) 'themes\render.ps1') -Reload
