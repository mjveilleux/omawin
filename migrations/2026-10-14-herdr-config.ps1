# herdr's config moved into the repo (config/herdr/config.toml since 2026-10-31): point herdr at it
# with HERDR_CONFIG_PATH, as install/shell.ps1 does. Running herdr sessions pick it up when they
# next start.
[Environment]::SetEnvironmentVariable('HERDR_CONFIG_PATH', (Join-Path (Split-Path $PSScriptRoot) 'config\herdr\config.toml'), 'User')
