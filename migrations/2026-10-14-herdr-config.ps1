# herdr's config moved into the repo (herdr/config.toml): point herdr at it with HERDR_CONFIG_PATH,
# as install/shell.ps1 does. Running herdr sessions pick it up when they next start.
[Environment]::SetEnvironmentVariable('HERDR_CONFIG_PATH', (Join-Path (Split-Path $PSScriptRoot) 'herdr\config.toml'), 'User')
