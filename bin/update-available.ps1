<#
.SYNOPSIS
    How many omawin updates are waiting: the bar's update arrow
    asks every 6 hours, as Omarchy's omarchy-update-available does.

.DESCRIPTION
    Fetches this checkout's branch quietly and prints how many commits it's behind (0 when it
    isn't, or when there's no network or no upstream). Changes nothing in the checkout.
#>
$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot
$upstream = git -C $repo rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null
if ($LASTEXITCODE -ne 0 -or -not $upstream) { 0; return }
git -C $repo fetch --quiet 2>$null
if ($LASTEXITCODE -ne 0) { 0; return }
[int](config/git -C $repo rev-list --count "HEAD..$upstream" 2>$null)
