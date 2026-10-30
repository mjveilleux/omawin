# install.ps1 step: the rest of the tools bundle (bundle/tools.txt), which has been unpacking in
# the background since the install began (install/software.ps1, Start-ToolBundle), and the Nerd
# Font it brought. Late on purpose: only GlazeWM, Bun, noctty and Windows Terminal are needed
# sooner, and the steps that need them wait for those alone (Wait-BundleTools).
# Dot-sourced by install.ps1, so its functions (install/lib.ps1, install/software.ps1) are in scope.
if (-not $LinkOnly) {
    Complete-ToolBundle
    Install-NerdFont
}
