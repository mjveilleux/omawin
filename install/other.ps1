# install.ps1 step: WSL's config (and on omawin update, Arch's packages), Claude Code's rules,
# skills, settings and plugins.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Repo and $UserLayer are in scope.
# Each file comes from your account folder when it has its own copy (docs/work.md), else from omawin.

Write-Step 'WSL'
# A file, not a folder, so it's copied rather than linked; re-run -LinkOnly after editing.
$wslConfig = "$HOME\.wslconfig"
$wslSource = if (Test-Path "$UserLayer\wsl\.wslconfig") { "$UserLayer\wsl\.wslconfig" } else { "$Repo\wsl\.wslconfig" }
$source = Get-Content $wslSource -Raw
if ((Test-Path $wslConfig) -and (Get-Content $wslConfig -Raw) -eq $source) { Write-Note 'ok  ~/.wslconfig'; Add-InstallItem 'Settings' 'wslconfig' '.wslconfig' 'have' }
else {
    Move-Aside $wslConfig
    Copy-Item $wslSource $wslConfig
    Write-Note "wrote ~/.wslconfig from $wslSource (applies next time WSL starts)"
    Add-InstallItem 'Settings' 'wslconfig' '.wslconfig' 'done'
}

# omawin update: Arch's packages too, from Omarchy's tested set (wsl/setup.sh update; as root,
# which WSL gives without a password). Only for the wsl preset's Arch, once it's there.
$linux = Get-LinuxPick
if ($Update -and (Get-Presets) -contains 'wsl' -and $linux.Name -eq 'arch' -and (Get-WslDistros) -contains $linux.Distro) {
    Invoke-InstallItem 'Programs' "$($linux.Label) packages" {
        $env:WSL_UTF8 = '1'
        $out = & { $ErrorActionPreference = 'Continue'; & wsl.exe -d $linux.Distro -u root --cd "$Repo\wsl" '--' bash ./setup.sh update 2>&1 }
        $code = $LASTEXITCODE
        Write-Log (($out | ForEach-Object { "$_" }) -join "`n")
        if ($code -ne 0) { throw "pacman stopped ($code): $(@($out | Where-Object { "$_".Trim() })[-1])" }
    }
}

Write-Step 'Claude Code'
# Your rules, skills, settings and plugins, from the shared folder and this account's
# (bin/claude-sync.ps1; Claude Code rewrites settings.json itself, so it's merged, not linked).
Invoke-InstallItem 'Settings' 'Claude Code' { & "$Repo\bin\claude-sync.ps1" }
