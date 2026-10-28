# Window animations (omawin's GlazeWM fork) snapshot windows as they move; without this switch
# Windows outlines each one in yellow for a frame. install/desktop.ps1 sets it on new PCs.
& (Join-Path (Split-Path $PSScriptRoot) 'install\capture-borderless.ps1')
