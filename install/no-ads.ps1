# install.ps1 step, the first: Windows' ads, suggestions and the background extras (Copilot,
# Visual Studio's update downloads, the PC maker's upsells) off before anything else is installed
# or started, so none of it comes back on with them. Your part only, no admin; the rest (Lenovo
# Now, McAfee, the Windows policy, Visual Studio's downloads) is `omawin no-ads`, which asks.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

# install.ps1 starts it in the background at the start ($NoAdsEarly), so this step comes late and
# only collects it; run alone (-Step no-ads), it runs here.
Write-Step 'no ads'
Invoke-InstallItem 'Windows' 'ads and suggestions off' {
    if ($NoAdsEarly) {
        if (-not $NoAdsEarly.Process.WaitForExit(120000)) { $NoAdsEarly.Process.Kill(); throw 'still running after 2 minutes: stopped (omawin no-ads runs it again)' }
        Get-Content $NoAdsEarly.Out -ErrorAction SilentlyContinue
        $err = (Get-Content $NoAdsEarly.Err -Raw -ErrorAction SilentlyContinue)
        Remove-Item $NoAdsEarly.Out, $NoAdsEarly.Err -ErrorAction SilentlyContinue
        if ($NoAdsEarly.Process.ExitCode) { throw "exit $($NoAdsEarly.Process.ExitCode): $err" }
    } else { & "$Repo\bin\no-ads.ps1" -UserOnly }
}
