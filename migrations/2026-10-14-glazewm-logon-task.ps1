# GlazeWM started from the registry's Run list, which Windows launches late (47 s after
# sign-in, measured). Start it from a scheduled task at sign-in instead, and turn off Windows'
# start-up delay for the rest of the Run list (install/login-start.ps1).
& (Join-Path (Split-Path $PSScriptRoot) 'install\login-start.ps1')
