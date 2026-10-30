# install.ps1 step, the first: Windows' ads, suggestions and the background extras (Copilot,
# Visual Studio's update downloads, the PC maker's upsells) off before anything else is installed
# or started, so none of it comes back on with them. Your part only, no admin; the rest (Lenovo
# Now, McAfee, the Windows policy, Visual Studio's downloads) is `omawin no-ads`, which asks.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'no ads'
Invoke-InstallItem 'Windows' 'ads and suggestions off' { & "$Repo\bin\no-ads.ps1" -UserOnly }
