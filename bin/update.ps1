<#
.SYNOPSIS
    omawin update: this setup, up to date. Pulls the repo, then relinks and rebuilds what
    changed (install.ps1 -LinkOnly, which runs the new migrations last).

.DESCRIPTION
    Never stops because something in the checkout was changed by hand (your own changes belong in
    your account folder, docs/work.md, but an update mustn't break when they aren't):

    - Local changes (and new files) are put in a stash named "omawin update <time>", the update
      is pulled (fast-forward only), and the stash is put back. If it doesn't go back cleanly,
      the checkout is left exactly as the update made it, the stash is kept, and you're told its
      name and how to get your changes back. The update carries on.
    - Local commits the update can't fast-forward over are replayed on top of it (rebase). If
      that conflicts, they're kept on a branch "omawin-local/<time>", and the checkout moves
      to the update.
    - The repo is never left in the middle of a merge, rebase or stash: each is finished or undone.
    - No network, no upstream, a detached checkout: said, and the relink runs on what's here.

    Then install.ps1 -LinkOnly (relink, rebuild, new migrations) and yt-dlp's upgrade, as always.

        omawin update                 the update
        omawin update -DryRun         says what it would do; changes nothing (it does fetch)
        omawin update -SkipInstall    the git part only (for testing in a throwaway clone)

    Runs under Windows PowerShell 5.1 too.
#>
param([switch]$DryRun, [switch]$SkipInstall)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

function Say([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
function Warn([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }
# git in the repo; its output, and $LASTEXITCODE for the caller. Its own messages (stderr) go
# along as text: with ErrorActionPreference Stop, PowerShell 5.1 would throw on the first one.
function Invoke-Git {
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & git.exe -C $Repo @args 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $saved }
}
function Test-GitPath([string]$Name) { Test-Path ([IO.Path]::Combine($Repo, ((Invoke-Git rev-parse --git-path $Name) -join ''))) }

# Put back the stash $Sha (made by this update). True if it went back cleanly (and was dropped).
# If not, whatever it half did is undone: the checkout is the update's, the stash stays.
function Restore-Stash([string]$Sha, [string]$Label) {
    # The new files it holds (a stash's third parent), to clear them again if it doesn't go back.
    $null = Invoke-Git rev-parse --verify --quiet "$Sha^3"
    $untracked = if ($LASTEXITCODE -eq 0) { @(Invoke-Git ls-tree -r --name-only "$Sha^3") } else { @() }
    $out = Invoke-Git stash apply --index $Sha
    if ($LASTEXITCODE -ne 0) { $out = Invoke-Git stash apply $Sha }  # without restoring what was staged
    if ($LASTEXITCODE -eq 0 -and -not (Invoke-Git ls-files --unmerged)) {
        $ref = Invoke-Git stash list --format='%gd %H' | Where-Object { $_.EndsWith(" $Sha") } | ForEach-Object { $_.Split(' ')[0] } | Select-Object -First 1
        if ($ref) { $null = Invoke-Git stash drop $ref }
        Say 'your local changes are back on top of the update'
        return $true
    }
    # Undo the half-applied stash: tracked files back to the update, and the untracked files it
    # brought back removed (they were all in the stash, so nothing is lost). Removed first, so
    # the reset brings back any the update itself has.
    foreach ($file in $untracked) {
        $path = Join-Path $Repo $file
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    $null = Invoke-Git reset --hard --quiet HEAD
    $ref = Invoke-Git stash list --format='%gd %H' | Where-Object { $_.EndsWith(" $Sha") } | ForEach-Object { $_.Split(' ')[0] } | Select-Object -First 1
    Warn "your local changes clash with the update, so they're kept aside in the stash ""$Label"" ($ref)."
    Warn "omawin is updated without them. To see them:   git -C $Repo stash show -p --include-untracked $ref"
    Warn "to put them back (fix the conflicts it marks): git -C $Repo stash pop $ref"
    Warn "better: move what you changed to your account folder (docs/work.md), then: git -C $Repo stash drop $ref"
    return $false
}

Write-Host "`n==> omawin update" -ForegroundColor Cyan
$updated = $false
$problems = 0

# Left in the middle of something by an earlier run or by hand: undo it first, it would block everything.
if (Test-GitPath 'MERGE_HEAD') { Warn 'an unfinished merge was in the way: undone (git merge --abort)'; if (-not $DryRun) { $null = Invoke-Git merge --abort } }
if ((Test-GitPath 'rebase-merge') -or (Test-GitPath 'rebase-apply')) { Warn 'an unfinished rebase was in the way: undone (git rebase --abort)'; if (-not $DryRun) { $null = Invoke-Git rebase --abort } }
if (Test-GitPath 'CHERRY_PICK_HEAD') { Warn 'an unfinished cherry-pick was in the way: undone'; if (-not $DryRun) { $null = Invoke-Git cherry-pick --abort } }
# Files still marked as conflicted can't be stashed: mark them resolved, conflict markers and all,
# so they go into the stash below like any other change.
$unmerged = @(Invoke-Git diff --name-only --diff-filter=U | Select-Object -Unique)
if ($unmerged) {
    Warn "files left with conflicts ($($unmerged -join ', ')): they go into the stash as they are"
    if (-not $DryRun) { $null = Invoke-Git add -- @unmerged }
}

$branch = (Invoke-Git symbolic-ref --quiet --short HEAD)
$upstream = if ($branch) { (Invoke-Git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null) } else { $null }
if (-not $branch) { Warn "the checkout is on no branch (detached); not pulling. To follow main again: git -C $Repo switch main" }
elseif ($LASTEXITCODE -ne 0 -or -not $upstream) { Warn "branch $branch follows no remote branch; not pulling"; $upstream = $null }

if ($upstream) {
    $null = Invoke-Git fetch --quiet
    if ($LASTEXITCODE -ne 0) { Warn "couldn't reach the remote (offline?); relinking what's here"; $upstream = $null }
}

if ($upstream) {
    $behind = [int](Invoke-Git rev-list --count "HEAD..$upstream")
    $ahead = [int](Invoke-Git rev-list --count "$upstream..HEAD")
    $dirty = @(Invoke-Git status --porcelain)
    if ($behind -eq 0) { Say "already up to date ($branch)" }
    elseif ($DryRun) {
        Say "would update $branch by $behind commit(s) from $upstream"
        if ($dirty) { Say "would set aside $($dirty.Count) local change(s) in a stash ""omawin update $Stamp"" and put them back after" }
        if ($ahead) { Say "would replay $ahead local commit(s) on top (or keep them on a branch omawin-local/...)" }
    } else {
        # 1. Local changes, new files included (ignored ones stay where they are), into a named stash.
        $stashSha = $null
        $label = "omawin update $Stamp"
        if ($dirty) {
            $out = Invoke-Git stash push --include-untracked --message $label
            if ($LASTEXITCODE -ne 0) { Warn "couldn't set your local changes aside: $($out -join ' ')"; $problems++ }
            else {
                $stashSha = (Invoke-Git rev-parse 'stash@{0}')
                Say "set aside $($dirty.Count) local change(s) in the stash ""$label"""
            }
        }
        if (-not $stashSha -and $dirty) {
            Warn 'not pulling, so nothing of yours is touched; relinking what is here'
        } else {
            # 2. The update: a fast-forward; with local commits, those replayed on top of it.
            $out = Invoke-Git merge --ff-only --quiet $upstream
            if ($LASTEXITCODE -eq 0) { $updated = $true }
            elseif ($ahead) {
                $out = Invoke-Git rebase --quiet $upstream
                if ($LASTEXITCODE -eq 0) { $updated = $true; Say "your $ahead local commit(s) are replayed on top of the update" }
                else {
                    $null = Invoke-Git rebase --abort
                    $keep = 'omawin-local/' + (Get-Date -Format 'yyyy-MM-dd-HHmmss')
                    $null = Invoke-Git branch $keep
                    if ($LASTEXITCODE -eq 0) {
                        $null = Invoke-Git reset --hard --quiet $upstream
                        $updated = $true
                        Warn "your $ahead local commit(s) clash with the update; they're kept on the branch $keep, and $branch is now the update"
                        Warn "to look: git -C $Repo log $keep   (move what you need to your account folder, docs/work.md)"
                    } else { Warn "couldn't keep your commits on a branch, so $branch stays as it was"; $problems++ }
                }
            } else { Warn "the update didn't apply: $($out -join ' ')"; $problems++ }
            if ($updated) { Say "updated $branch to $((Invoke-Git log -1 --format='%h %s') -join '')" }

            # 3. Your changes back on top.
            if ($stashSha -and -not (Restore-Stash $stashSha $label)) { $problems++ }
        }
    }
}

# Never leave it mid-operation, whatever happened above.
if (-not $DryRun) {
    if (Test-GitPath 'MERGE_HEAD') { $null = Invoke-Git merge --abort }
    if ((Test-GitPath 'rebase-merge') -or (Test-GitPath 'rebase-apply')) { $null = Invoke-Git rebase --abort }
}

if ($DryRun) { Say 'dry run: would now run install.ps1 -LinkOnly (relink, rebuild, new migrations)'; return }
if ($SkipInstall) { Say 'skipping install.ps1 -LinkOnly (-SkipInstall)'; if ($problems) { exit 1 } else { return } }

# The rest runs from the updated checkout: install.ps1 is read fresh from disk.
$global:LASTEXITCODE = 0
& "$Repo\install.ps1" -LinkOnly
$installCode = $LASTEXITCODE
# yt-dlp breaks whenever YouTube changes; the music player needs a current one.
winget upgrade --id yt-dlp.yt-dlp -e --silent --accept-package-agreements --accept-source-agreements | Out-Null
if ($problems) { Write-Host "`n    omawin is updated; see the notes above about your local changes." -ForegroundColor Yellow }
if ($installCode -ne 0) { exit $installCode }
