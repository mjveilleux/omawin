# Claude Code's rules, skills, settings and plugins now come from the layers (bin/claude-sync.ps1)
# instead of being seeded once: run it. Safe on a new PC (no layers: omawin's defaults fill in
# only what's missing) and twice.
& (Join-Path (Split-Path $PSScriptRoot) 'bin\claude-sync.ps1')
