# 03: install, update, packages (Phase 2, 2B, 2.9)

> **Uncompiled, untested sketch.** Nothing here has run. It names real files and functions as of 2026-10-30 and follows [AGENTS.md](../../AGENTS.md) (install scripts run under Windows PowerShell 5.1; no admin by default; never delete the user's files). Anything uncertain is marked *(check)*. Versions, URLs and hashes in examples are placeholders: the bump job fills the real ones. Build it, test it, fix this file where reality differs.

The *what* and *why*: [roadmap.md](../roadmap.md) Phase 2 (2.0-2.9), Phase 2B, Phase 7.1, and [install-speed.md](../install-speed.md). This file is the *how*.

## Contents

1. [The layout on disk](#1-the-layout-on-disk)
2. [The bundle: manifest, build, CI, version bumps](#2-the-bundle)
3. [The new bootstrap and first run](#3-the-new-bootstrap-and-first-run)
4. [The welcome page and the background queue](#4-the-welcome-page-and-the-background-queue)
5. [Delta updates and rollback](#5-delta-updates-and-rollback)
6. [`omawin pkg`](#6-omawin-pkg-phase-2b)
7. [No admin (2.9)](#7-no-admin-29)
8. [CI install timing (2.1) and the Windows Sandbox test (7.1)](#8-ci-install-timing-and-the-windows-sandbox-test)
9. [Order of work, files touched, open questions](#9-order-of-work-files-touched-open-questions)

---

## 1. The layout on disk

Everything omawin ships lives per user, no admin:

```
%LOCALAPPDATA%\omawin\
  v0.9.0\                      one version folder = the bundle of that tag, unpacked (never written in place)
    files.tsv                  the per-file manifest of this version (section 2.2)
    repo\                      the repo at the tag (configs, scripts): what ~/omawin points at   (stage 1a)
    bin\                       omawin.exe and the bar's helpers (1a), omawin-music.exe (1b)
    glazewm\                   glazewm.exe, glazewm-watcher.exe, cli\glazewm.exe: the per-user build, no uiAccess  (1a)
    seed\                      the default theme, pre-rendered; stub templates                    (1a)
    menu\                      SystemMenu.exe (Native AOT, self-contained)                        (1b)
    noctty\                    the terminal                                                       (1b)
    fonts\                     JetBrainsMono Nerd Font + fonts.tsv (registry names)               (1b)
    tools\                     rg fd fzf eza bat jq lazygit zoxide starship fastfetch gh uv mise ... (2)
    git\                       MinGit                                                             (2)
    nvim\                      Neovim                                                             (2)
    nvim-data\lazy\            LazyVim's plugins at lazy-lock.json's versions                     (2)
    pwsh\                      PowerShell 7 (its zip build)                                       (2)
    licenses\                  every bundled tool's licence and NOTICE                            (2)
  v0.8.3\                      the previous version, kept for rollback until the next update
  current  -> v0.9.0           junction: every path omawin writes anywhere goes through it
  previous.txt                 "v0.8.3": what `omawin update -Rollback` flips back to
  tools\                       hard links to current\tools\*.exe, one per tool the bundle still provides
                               (a tool moved to winget/Scoop/Chocolatey/mise loses its link: section 6.6)
  unlinked.txt                 the bundled tools you moved elsewhere
  downloads\                   curl.exe's files (resumable), cleared after a good install

~/omawin -> %LOCALAPPDATA%\omawin\current\repo     junction (a bundle install); a git checkout already
                                                   there is left alone and used instead (developer mode)
```

Why a junction at `~/omawin`: `bin/omawin.cs:29`, `menu-app/MenuModel.cs:37`, `check.ps1`, the PowerShell profile stubs and the Terminal profiles all expect the repo at `~/omawin` (or `DOTFILES`). The junction keeps every one of them working unchanged, and an update is one junction flip (`current`) that all of them follow.

**Developer mode** (a git checkout at `~/omawin`): bootstrap with `-Git` (today's clone path) or keep an existing checkout. `omawin update` stays `bin/update.ps1` (git), `install.ps1` stays as it is. The bundle's tools can still be had: `bin/bundle.ps1 -Stage 2` unpacks them beside it.

The user PATH, written once (section 3.4):

```
%LOCALAPPDATA%\omawin\current\bin ; <the user's own entries, their order kept> ;
%LOCALAPPDATA%\omawin\tools ; %LOCALAPPDATA%\omawin\current\git\cmd ; %LOCALAPPDATA%\omawin\current\nvim\bin ; %LOCALAPPDATA%\omawin\current\pwsh
```

omawin's own exes come first: nobody else ships those names. The bundled third-party tools come **last**, so a copy from winget (its `Links` folder), Scoop (its `shims`), Chocolatey (the machine PATH, which Windows puts before the user PATH anyway) or mise (shims, which `Initialize-Mise` prepends) always wins.

---

## 2. The bundle

### 2.1 `bundle/manifest.json`

One file lists every piece, its pinned version, where it comes from, its hash, its stage and what it's called in each package manager. omawin's own pieces (built by the release workflow's jobs) are listed with `"source": "artifact"`, so the bundle build reads one list.

```json
{
  "schema": 1,
  "stages": {
    "1a": { "summary": "draws the desktop and catches keys", "budgetMB": 8 },
    "1b": { "summary": "the menu app, noctty, the font", "budgetMB": 45 },
    "2":  { "summary": "command-line tools, Git, Neovim and its plugins, PowerShell 7", "budgetMB": 200 }
  },
  "triples": { "x64": "x86_64-pc-windows-msvc", "arm64": "aarch64-pc-windows-msvc" },
  "tools": [
    {
      "name": "omawin-helpers",
      "source": "artifact", "artifact": "helpers", "stage": "1a", "into": "bin",
      "pick": ["omawin.exe", "theme-watch.exe", "win-keys.exe", "app-icons.exe", "cpu-cores.exe", "voice-meter.exe", "omawin-listen.exe"]
    },
    { "name": "omawin-music", "source": "artifact", "artifact": "music-{arch}", "stage": "1b", "into": "bin", "pick": ["omawin-music.exe"] },
    { "name": "menu", "source": "artifact", "artifact": "menu-{arch}", "stage": "1b", "into": "menu", "pick": ["**"] },
    {
      "name": "glazewm",
      "version": "3.10.1-omawin.7", "stage": "1a", "license": "GPL-3.0",
      "hold": "follows the fork's omawin branch; bumped by the fork's release, not the weekly job",
      "github": { "repo": "mjveilleux/glazewm", "tag": "v{version}", "asset": "glazewm-{version}-windows-{arch}-user.zip" },
      "arch": {
        "x64": { "url": "https://github.com/mjveilleux/glazewm/releases/download/v3.10.1-omawin.7/glazewm-3.10.1-omawin.7-windows-x64-user.zip", "sha256": "<64 hex>" }
      },
      "into": "glazewm", "pick": ["**"],
      "ids": { "winget": "glzr-io.glazewm" }
    },
    {
      "name": "noctty",
      "version": "0.4.2", "stage": "1b", "license": "MIT",
      "github": { "repo": "mjveilleux/noctty", "tag": "v{version}", "asset": "noctty-{version}-windows-{arch}.zip" },
      "arch": { "x64": { "url": "<url>", "sha256": "<64 hex>" } },
      "into": "noctty", "pick": ["**"]
    },
    {
      "name": "jetbrainsmono-nerd-font",
      "version": "3.3.0", "stage": "1b", "license": "OFL-1.1",
      "github": { "repo": "ryanoasis/nerd-fonts", "tag": "v{version}", "asset": "JetBrainsMono.zip" },
      "any": { "url": "https://github.com/ryanoasis/nerd-fonts/releases/download/v3.3.0/JetBrainsMono.zip", "sha256": "<64 hex>" },
      "into": "fonts", "pick": ["JetBrainsMonoNerdFont-*.ttf"], "flatten": true,
      "ids": { "winget": "DEVCOM.JetBrainsMonoNerdFont", "scoop": "nerd-fonts/JetBrainsMono-NF", "choco": "nerd-fonts-jetbrainsmono" }
    },
    {
      "name": "ripgrep",
      "version": "14.1.1", "stage": "2", "license": "MIT OR Unlicense",
      "github": { "repo": "BurntSushi/ripgrep", "tag": "{version}", "asset": "ripgrep-{version}-{triple}.zip" },
      "arch": {
        "x64":   { "url": "https://github.com/BurntSushi/ripgrep/releases/download/14.1.1/ripgrep-14.1.1-x86_64-pc-windows-msvc.zip", "sha256": "<64 hex>" },
        "arm64": { "url": "https://github.com/BurntSushi/ripgrep/releases/download/14.1.1/ripgrep-14.1.1-aarch64-pc-windows-msvc.zip", "sha256": "<64 hex>" }
      },
      "into": "tools", "pick": ["*/rg.exe"], "flatten": true, "commands": ["rg"],
      "ids": { "winget": "BurntSushi.ripgrep.MSVC", "scoop": "ripgrep", "choco": "ripgrep", "mise": "ripgrep", "arch": "ripgrep" }
    },
    {
      "name": "fd",
      "version": "10.2.0", "stage": "2", "license": "MIT OR Apache-2.0",
      "github": { "repo": "sharkdp/fd", "tag": "v{version}", "asset": "fd-v{version}-{triple}.zip" },
      "arch": { "x64": { "url": "<url>", "sha256": "<64 hex>" }, "arm64": { "url": "<url>", "sha256": "<64 hex>" } },
      "into": "tools", "pick": ["*/fd.exe"], "flatten": true, "commands": ["fd"],
      "ids": { "winget": "sharkdp.fd", "scoop": "fd", "choco": "fd", "mise": "fd", "arch": "fd" }
    },
    {
      "name": "git",
      "version": "2.47.1", "stage": "2", "license": "GPL-2.0",
      "github": { "repo": "git-for-windows/git", "tag": "v{version}.windows.1", "asset": "MinGit-{version}-{bits}.zip" },
      "arch": {
        "x64":   { "url": "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip", "sha256": "<64 hex>" },
        "arm64": { "url": "<MinGit arm64 asset (check: its name)>", "sha256": "<64 hex>" }
      },
      "into": "git", "pick": ["**"], "path": "git\\cmd", "commands": ["git"],
      "ids": { "winget": "Git.Git", "scoop": "git", "choco": "git", "arch": "git" }
    },
    {
      "name": "neovim",
      "version": "0.11.4", "stage": "2", "license": "Apache-2.0 AND Vim",
      "github": { "repo": "neovim/neovim", "tag": "v{version}", "asset": "nvim-win64.zip" },
      "arch": {
        "x64":   { "url": "https://github.com/neovim/neovim/releases/download/v0.11.4/nvim-win64.zip", "sha256": "<64 hex>" },
        "arm64": { "url": "<nvim-win-arm64.zip (check: arm64 asset name)>", "sha256": "<64 hex>" }
      },
      "into": "nvim", "pick": ["nvim-win64/**"], "strip": 1, "path": "nvim\\bin", "commands": ["nvim"],
      "ids": { "winget": "Neovim.Neovim", "scoop": "neovim", "choco": "neovim", "mise": "neovim", "arch": "neovim" }
    },
    {
      "name": "mise",
      "version": "2025.10.0", "stage": "2", "license": "MIT",
      "github": { "repo": "jdx/mise", "tag": "v{version}", "asset": "mise-v{version}-windows-{arch}.zip" },
      "arch": { "x64": { "url": "<url>", "sha256": "<64 hex>" }, "arm64": { "url": "<url>", "sha256": "<64 hex>" } },
      "into": "tools", "pick": ["mise/bin/mise.exe"], "flatten": true, "commands": ["mise"],
      "ids": { "winget": "jdx.mise", "scoop": "mise", "choco": "mise" }
    },
    {
      "name": "pwsh",
      "version": "7.5.3", "stage": "2", "license": "MIT",
      "github": { "repo": "PowerShell/PowerShell", "tag": "v{version}", "asset": "PowerShell-{version}-win-{arch}.zip" },
      "arch": { "x64": { "url": "<url>", "sha256": "<64 hex>" }, "arm64": { "url": "<url>", "sha256": "<64 hex>" } },
      "into": "pwsh", "pick": ["**"], "path": "pwsh", "commands": ["pwsh"],
      "ids": { "winget": "Microsoft.PowerShell", "scoop": "pwsh", "choco": "powershell-core" }
    },
    { "name": "lazyvim-plugins", "source": "nvim-restore", "stage": "2", "into": "nvim-data", "note": "built in CI from nvim/lazy-lock.json (section 2.2); after neovim in this list" }
  ]
}
```

Rules the build enforces (and a CI lint step checks on every PR touching `bundle/`):
- every non-`artifact` entry has `version`, `license`, and per architecture it claims a URL and a 64-hex `sha256` (`any` for architecture-free files);
- the only placeholders are `{version}`, `{arch}`, `{triple}`, `{bits}` (x64 → `64-bit`, arm64 → `arm64`);
- a name here with `ids` must not also be a row in `packages/names.tsv` (section 6.2): one place per name;
- each stage's unpacked size stays under its `budgetMB` (a warning in the job summary until M2, then a failure).

The other CLI tools (fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, gh, uv, tree-sitter) are entries shaped like `ripgrep`. A tool with no arm64 build has no `arm64` key: the arm64 bundle leaves it out, the queue installs it with `omawin pkg`, and `omawin check` shows the gap (Omarchy's platform rule: fix upstream, keep the gap visible).

### 2.2 `build/bundle.ps1` (CI, pwsh 7)

Builds `omawin-<tag>-win-<arch>-1a.zip`, `-1b.zip`, `-2.zip`, `files-<arch>.tsv`, and delta zips against earlier releases. Runs in CI and locally (to test `bootstrap.ps1 -Bundle`).

```powershell
<#
.SYNOPSIS
    The omawin bundle for one architecture: stage zips, the per-file manifest, delta zips.
.EXAMPLE
    .\build\bundle.ps1 -Tag v0.9.0 -Arch x64 -Artifacts out -OutDir dist
    .\build\bundle.ps1 -Tag v0.9.0 -Arch x64 -Artifacts out -OutDir dist -DeltaFrom v0.8.3,v0.8.2 -PreviousDir prev
#>
param(
    [Parameter(Mandatory)][string]$Tag,
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [Parameter(Mandatory)][string]$Artifacts,   # the release jobs' artifacts: helpers\, menu-<arch>\, music-<arch>\
    [Parameter(Mandatory)][string]$OutDir,
    [string[]]$DeltaFrom = @(),                 # earlier tags to build delta zips from
    [string]$PreviousDir                        # their files-<arch>.tsv, as <PreviousDir>\<tag>\files-<arch>.tsv
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Repo = Split-Path $PSScriptRoot
$manifest = Get-Content "$Repo\bundle\manifest.json" -Raw | ConvertFrom-Json
$work = Join-Path ([IO.Path]::GetTempPath()) "omawin-bundle-$Arch"
$stageRoot = Join-Path $work 'stage'          # stage\1a\..., stage\1b\..., stage\2\...
$cache = Join-Path $work 'downloads'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stageRoot, $cache, $OutDir | Out-Null

# One upstream file, cached, refused unless its bytes match the manifest.
function Get-Upstream($tool) {
    $src = if ($tool.any) { $tool.any } else { $tool.arch.$Arch }
    if (-not $src) { return $null }   # no build for this architecture: left out, said in the summary
    $file = Join-Path $cache ([IO.Path]::GetFileName(([uri]$src.url).AbsolutePath))
    if (-not (Test-Path $file)) { curl.exe -fsSL --retry 3 -o $file $src.url; if ($LASTEXITCODE) { throw "download failed: $($src.url)" } }
    $have = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($have -ne $src.sha256.ToUpperInvariant()) { throw "$($tool.name): sha256 mismatch (manifest $($src.sha256), got $have)" }
    $file
}

# The files matching $Pick (globs relative to $From) into $To. -Flatten drops folders; -Strip n
# drops the first n path parts.
function Copy-Picked([string]$From, [string[]]$Pick, [string]$To, [switch]$Flatten, [int]$Strip) {
    foreach ($f in Get-ChildItem $From -Recurse -File) {
        $rel = [IO.Path]::GetRelativePath($From, $f.FullName).Replace('\', '/')
        if (-not ($Pick | Where-Object { $rel -like ($_ -replace '\*\*', '*') })) { continue }
        $out = if ($Flatten) { $f.Name } else { ($rel -split '/' | Select-Object -Skip $Strip) -join '\' }
        $dest = Join-Path $To $out
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Copy-Item $f.FullName $dest -Force
    }
}

$missing = @()
foreach ($tool in $manifest.tools) {
    $to = Join-Path $stageRoot "$($tool.stage)\$($tool.into)"
    switch ($tool.source) {
        'artifact' {
            $dir = Join-Path $Artifacts ($tool.artifact.Replace('{arch}', $Arch))
            if (-not (Test-Path $dir)) { $missing += $tool.name; continue }
            Copy-Picked $dir $tool.pick $to
        }
        'nvim-restore' {
            # LazyVim's plugins at nvim/lazy-lock.json's versions, so the first nvim opens ready.
            # Neovim honours XDG_* on Windows (check): its data lands in $xdg\data\nvim-data.
            $xdg = Join-Path $work 'xdg'
            New-Item -ItemType Directory -Force "$xdg\config", "$xdg\data" | Out-Null
            New-Item -ItemType Junction -Path "$xdg\config\nvim" -Target "$Repo\nvim" | Out-Null
            $nvim = Get-ChildItem "$stageRoot\2\nvim" -Recurse -Filter nvim.exe | Select-Object -First 1
            if (-not $nvim) { throw 'lazyvim-plugins must come after neovim in manifest.json' }
            $env:XDG_CONFIG_HOME = "$xdg\config"; $env:XDG_DATA_HOME = "$xdg\data"
            & $nvim.FullName --headless '+Lazy! restore' +qa
            if ($LASTEXITCODE) { throw 'nvim Lazy restore failed' }
            Remove-Item Env:XDG_CONFIG_HOME, Env:XDG_DATA_HOME
            Copy-Item "$xdg\data\nvim-data\lazy" (Join-Path $to 'lazy') -Recurse
        }
        default {
            $file = Get-Upstream $tool
            if (-not $file) { $missing += "$($tool.name) (no $Arch build)"; continue }
            $x = Join-Path $work "x\$($tool.name)"
            New-Item -ItemType Directory -Force $x | Out-Null
            if ($file -like '*.zip') { tar.exe -xf $file -C $x } else { Copy-Item $file $x }
            Copy-Picked $x $tool.pick $to -Flatten:([bool]$tool.flatten) -Strip ([int]$tool.strip)
            $d = "$stageRoot\2\licenses\$($tool.name)"
            foreach ($l in Get-ChildItem $x -Recurse -File | Where-Object Name -match '^(LICEN[CS]E|COPYING|NOTICE|UNLICENSE)') {
                New-Item -ItemType Directory -Force $d | Out-Null; Copy-Item $l.FullName $d
            }
        }
    }
}

# The repo at this tag (configs, scripts): git archive, so only tracked files ship.
$repoOut = "$stageRoot\1a\repo"
New-Item -ItemType Directory -Force $repoOut | Out-Null
git -C $Repo archive --format=tar -o "$work\repo.tar" HEAD
tar.exe -xf "$work\repo.tar" -C $repoOut

# The default theme, rendered once here, so the first start renders nothing (section 3.5).
# (check: themes/render.ps1 has no -OutDir/-Theme yet; add them, keeping today's default behaviour)
& "$Repo\themes\render.ps1" -OutDir "$stageRoot\1a\seed\rendered" -Theme 'starship'   # omawin's default theme (menu-app/MenuModel.cs:183)

# The font's registry names, worked out here once instead of on every PC (System.Drawing).
Add-Type -AssemblyName System.Drawing
$fontLines = foreach ($ttf in Get-ChildItem "$stageRoot\1b\fonts" -Filter *.ttf) {
    $fc = New-Object System.Drawing.Text.PrivateFontCollection
    $fc.AddFontFile($ttf.FullName)
    $style = [IO.Path]::GetFileNameWithoutExtension($ttf.Name) -replace '^.*-', ''
    "$($ttf.Name)`t$($fc.Families[0].Name) $style (TrueType)"
    $fc.Dispose()
}
Set-Content "$stageRoot\1b\fonts\fonts.tsv" $fontLines

# The per-file manifest: stage, size, sha256, path. TSV, not JSON: Windows PowerShell 5.1's
# ConvertFrom-Json takes seconds on tens of thousands of entries (the Neovim plugins).
$rows = foreach ($stage in '1a', '1b', '2') {
    $root = "$stageRoot\$stage"
    foreach ($f in Get-ChildItem $root -Recurse -File) {
        $rel = [IO.Path]::GetRelativePath($root, $f.FullName)
        "$stage`t$($f.Length)`t$((Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant())`t$rel"
    }
}
$filesTsv = Join-Path $OutDir "files-$Arch.tsv"
Set-Content $filesTsv (@("# omawin $Tag win-$Arch`: stage`tsize`tsha256`tpath") + $rows)
Copy-Item $filesTsv "$stageRoot\1a\files.tsv"       # each version folder carries its own (updates compare against it)

# The stage zips. "-C <dir> ." rather than "*": PowerShell hands a native program the * unexpanded.
# Deflate zip: Windows' tar.exe (bsdtar) writes it with -a and a .zip name, and every Windows
# 10/11 tar.exe reads it (check both; zstd isn't in every Windows build's tar.exe, check).
foreach ($stage in '1a', '1b', '2') {
    $zip = Join-Path $OutDir "omawin-$Tag-win-$Arch-$stage.zip"
    tar.exe -a -cf $zip -C "$stageRoot\$stage" .
    if ($LASTEXITCODE) { throw "tar failed for stage $stage" }
    "{0}  {1:N1} MB" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
}

# Delta zips: only the files whose hash differs from (or is absent in) an earlier release, laid
# out <stage>\<path>.
foreach ($old in $DeltaFrom) {
    $oldTsv = Join-Path $PreviousDir "$old\files-$Arch.tsv"
    if (-not (Test-Path $oldTsv)) { Write-Warning "no files-$Arch.tsv for ${old}: no delta from it"; continue }
    $had = @{}
    foreach ($l in Get-Content $oldTsv) { if ($l -notlike '#*') { $p = $l -split "`t"; $had["$($p[0])|$($p[3])"] = $p[2] } }
    $delta = Join-Path $work "delta-$old"
    New-Item -ItemType Directory -Force $delta | Out-Null
    foreach ($l in $rows) {
        $p = $l -split "`t"
        if ($had["$($p[0])|$($p[3])"] -eq $p[2]) { continue }
        $dest = Join-Path $delta "$($p[0])\$($p[3])"
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Copy-Item "$stageRoot\$($p[0])\$($p[3])" $dest
    }
    tar.exe -a -cf (Join-Path $OutDir "omawin-$Tag-win-$Arch-from-$old.zip") -C $delta .
}

if ($missing -and $env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY "left out of the $Arch bundle: $($missing -join ', ')" }
```

`SHA256SUMS.txt` stays `build/release.ps1 -OnlyManifest`'s job: it hashes every file in `dist`, so the bundle's zips and `files-<arch>.tsv` join it with no change to `bin/release.ps1`'s parser.

### 2.3 Release assets

| Asset | What |
|---|---|
| `omawin-<tag>-win-<arch>-1a.zip` | `bin\` (omawin.exe, helpers), `glazewm\`, `repo\`, `seed\`, `files.tsv` |
| `omawin-<tag>-win-<arch>-1b.zip` | `menu\`, `noctty\`, `fonts\`, `bin\omawin-music.exe` |
| `omawin-<tag>-win-<arch>-2.zip` | `tools\`, `git\`, `nvim\`, `nvim-data\`, `pwsh\`, `licenses\` |
| `omawin-<tag>-win-<arch>-from-<old>.zip` | the files changed since `<old>`, as `<stage>\<path>` (the last three releases) |
| `files-<arch>.tsv` | `stage<TAB>size<TAB>sha256<TAB>path` for every file of the version |
| `SHA256SUMS.txt` | as today, now listing the above too |
| today's `omawin-helpers-*.zip`, `SystemMenu-*.zip`, `omawin-music-*.exe` | kept for git-checkout installs (`bin/install-binaries.ps1`) |

GitHub caps the assets of one release *(check: 1000)*, and tens of thousands of files can't be single assets anyway: changed files travel in the delta zips.

### 2.4 `.github/workflows/release.yml`: the `bundle` job

Added between the build jobs and `release`. Signing moves into it (before anything is zipped), so the bundle holds signed omawin binaries; third-party exes keep their own signatures, the filter below signs only omawin's.

```yaml
  # The omawin bundle: stage zips, files-<arch>.tsv, delta zips from the last three releases.
  bundle:
    needs: [meta, helpers, menu, music]
    strategy:
      fail-fast: false
      matrix:
        arch: ${{ fromJSON(vars.ARM64_RUNNERS == 'true' && '["x64","arm64"]' || '["x64"]') }}
    runs-on: ${{ matrix.arch == 'arm64' && 'windows-11-arm' || 'windows-latest' }}
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4
        with:
          path: out

      - name: Sign omawin's exes (repo variable CODESIGN)
        if: vars.CODESIGN == 'true'
        uses: azure/trusted-signing-action@1d365fec12862c4aa68fcac418143d73f0cea293 # v0
        with:
          endpoint: ${{ vars.CODESIGN_ENDPOINT }}
          signing-account-name: ${{ vars.CODESIGN_ACCOUNT }}
          certificate-profile-name: ${{ vars.CODESIGN_PROFILE }}
          azure-tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          azure-client-id: ${{ secrets.AZURE_CLIENT_ID }}
          azure-client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}
          files-folder: out
          files-folder-recurse: true
          files-folder-filter: exe
          file-digest: SHA256
          timestamp-rfc3161: http://timestamp.acs.microsoft.com
          timestamp-digest: SHA256

      - name: The last three releases' file lists (for delta zips)
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $tags = @(gh release list --repo '${{ github.repository }}' --limit 3 --json tagName --jq '.[].tagName')
          foreach ($t in $tags) {
            New-Item -ItemType Directory -Force "prev\$t" | Out-Null
            gh release download $t --repo '${{ github.repository }}' --pattern 'files-${{ matrix.arch }}.tsv' --dir "prev\$t"
          }
          "DELTA_FROM=$($tags -join ',')" | Add-Content $env:GITHUB_ENV

      - name: Build the bundle
        shell: pwsh
        run: |
          $from = @($env:DELTA_FROM -split ',' | Where-Object { $_ })
          .\build\bundle.ps1 -Tag '${{ needs.meta.outputs.version }}' -Arch ${{ matrix.arch }} `
            -Artifacts out -OutDir dist-bundle -DeltaFrom $from -PreviousDir prev

      - uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: bundle-${{ matrix.arch }}
          path: dist-bundle
          if-no-files-found: error
```

In the `release` job: `needs: [meta, helpers, menu, music, bundle]`, and one step after "Pack", before "The manifest the installer verifies against":

```yaml
      - name: The bundle's files join the release
        shell: pwsh
        run: Get-ChildItem out -Directory -Filter 'bundle-*' | ForEach-Object { Copy-Item "$($_.FullName)\*" dist }
```

(`build/release.ps1 -NoManifest` reads only `helpers\`, `menu-<arch>\`, `music-<arch>\`, so the `bundle-*` folders in `out` don't disturb it. The release job's own sign step stays for the legacy assets.)

The fork's per-user GlazeWM must be released before the bundle can pin it: section 7.1.

### 2.5 The scheduled version bump: `.github/workflows/bundle-bump.yml`

Weekly and by hand: for every manifest entry with a `github` field (and no `hold`), the newest release, its asset per arch, the hash; a PR when anything moved. It never merges by itself.

```yaml
name: Bundle bump
on:
  schedule:
    - cron: '17 5 * * 1'      # Mondays 05:17 UTC
  workflow_dispatch:
permissions:
  contents: write
  pull-requests: write
jobs:
  bump:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - name: Look for new upstream releases
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: .\build\bundle-bump.ps1 -Manifest bundle\manifest.json -Summary $env:GITHUB_STEP_SUMMARY
      - name: Open a PR
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          if (-not (git status --porcelain bundle/manifest.json)) { 'nothing new'; return }
          $branch = "bundle-bump/$(Get-Date -Format yyyy-MM-dd)"
          git config user.name 'github-actions[bot]'
          git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
          git switch -c $branch
          git commit -am 'Bundle: newer upstream versions'
          git push -u origin $branch
          gh pr create --title 'Bundle: newer upstream versions' --body-file bump-notes.md --base main
```

(A PR opened with `GITHUB_TOKEN` doesn't trigger other workflows; to run the install job (section 8.1) on it, open it with a GitHub App token or run that job by hand *(check: the repo's preference)*.)

`build/bundle-bump.ps1`:

```powershell
param([string]$Manifest = 'bundle\manifest.json', [string]$Summary)
$ErrorActionPreference = 'Stop'
$m = Get-Content $Manifest -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'omawin-bump' }
$bits = @{ x64 = '64-bit'; arm64 = 'arm64' }
$notes = @('| tool | from | to |', '|---|---|---|')

foreach ($tool in $m.tools | Where-Object { $_.github -and $_.version -and -not $_.hold }) {
    $rel = Invoke-RestMethod "https://api.github.com/repos/$($tool.github.repo)/releases/latest" -Headers $headers
    # The version out of the tag, by the manifest's own tag pattern ("v{version}", "v{version}.windows.1").
    $rx = '^' + [regex]::Escape($tool.github.tag).Replace('\{version}', '(?<v>.+?)') + '$'
    if ($rel.tag_name -notmatch $rx) { Write-Warning "$($tool.name): tag $($rel.tag_name) doesn't fit $($tool.github.tag)"; continue }
    $new = $Matches.v
    if ($new -eq $tool.version) { continue }
    $targets = if ($tool.any) { @('any') } else { @($tool.arch.PSObject.Properties.Name) }
    $updated = @{}
    foreach ($a in $targets) {
        $asset = $tool.github.asset.Replace('{version}', $new).Replace('{arch}', $a).Replace('{triple}', "$($m.triples.$a)").Replace('{bits}', "$($bits[$a])")
        $hit = $rel.assets | Where-Object name -eq $asset | Select-Object -First 1
        if (-not $hit) { Write-Warning "$($tool.name) $new has no ${asset}: kept at $($tool.version)"; $updated = $null; break }
        $tmp = Join-Path $env:RUNNER_TEMP $asset
        curl.exe -fsSL -o $tmp $hit.browser_download_url
        $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        # GitHub publishes a digest per asset (check: the "digest" field, "sha256:<hex>"); when it's
        # there it must agree with what was downloaded.
        if ($hit.digest -and $hit.digest -ne "sha256:$hash") { throw "${asset}: digest mismatch" }
        $updated[$a] = @{ url = $hit.browser_download_url; sha256 = $hash }
        Remove-Item $tmp
    }
    if (-not $updated) { continue }
    $notes += "| $($tool.name) | $($tool.version) | $new |"
    $tool.version = $new
    foreach ($a in $updated.Keys) { if ($a -eq 'any') { $tool.any = $updated[$a] } else { $tool.arch.$a = $updated[$a] } }
}
# Depth: tools[].arch.x64.url is four deep; ConvertTo-Json's default (2) would flatten it.
$m | ConvertTo-Json -Depth 10 | Set-Content $Manifest -Encoding utf8NoBOM
$notes | Set-Content bump-notes.md
if ($Summary) { $notes | Add-Content $Summary }
```

---

## 3. The new bootstrap and first run

Two files, so the one fetched from the web stays small and the rest is versioned with the bundle:

- **`bootstrap.ps1`** (fetched by `irm … | iex`): download, verify, unpack, `current` junction, `~/omawin` junction, then hand over to the unpacked `repo\install\first-run.ps1`. Today's git path stays behind `-Git` (developers, forks without a bundle).
- **`install/first-run.ps1`** (in the bundle): seed configs in one pass, PATH, fonts, sign-in task, start the desktop, mark migrations done, write the queue, start the queue worker, open the welcome page. Also what `-Bundle <path>` (offline) runs, and what `omawin install` runs on a bundle install.

Both run under Windows PowerShell 5.1, no admin, and stay **ASCII only** (5.1 may read a file in the ANSI code page; glyphs come from code points, as today).

### 3.1 How it's called

```powershell
# The one line (defaults; the welcome page asks the rest on the desktop):
irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex

# With answers (scripts, agents, CI): a script block, since `| iex` can't take parameters.
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1))) -Presets data,go -Linux arch -Yes

# Offline, from a saved bundle (a USB stick, a share, CI's built bundle):
.\bootstrap.ps1 -Bundle E:\omawin-v0.9.0

# Developers: today's clone + install.ps1
.\bootstrap.ps1 -Git
```

Environment equivalents for wrappers that can only set variables: `OMAWIN_PRESETS=data,go`, `OMAWIN_LINUX=arch`, `OMAWIN_YES=1`, `OMAWIN_BUNDLE=<path>`, `OMAWIN_RELEASE=<tag>`, `OMAWIN_REPO=<owner/name>` (exists), `OMAWIN_PLAIN=1` (exists).

### 3.2 `bootstrap.ps1`

```powershell
<#
.SYNOPSIS
    One command for a brand-new Windows PC: omawin's bundle, unpacked and started. No admin.
.DESCRIPTION
        irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex
    1. the release's SHA256SUMS.txt, then stage 1a and 1b with curl.exe (parallel, resumable)
    2. each checked against SHA256SUMS.txt, unpacked with tar.exe into %LOCALAPPDATA%\omawin\<tag>
    3. %LOCALAPPDATA%\omawin\current -> <tag>, ~/omawin -> current\repo
    4. repo\install\first-run.ps1: configs, PATH, font, sign-in task, the desktop, the queue
    Stage 2 (tools, Git, Neovim) downloads in the background once the desktop is up.
    Runs under Windows PowerShell 5.1. ASCII only.
#>
param(
    [string[]]$Presets,                 # -Presets data,go ; -Presets none
    [string]$Linux,                     # arch | ubuntu | none
    [switch]$Yes,                       # no welcome page: the defaults (or these flags) start the queue now
    [string]$Bundle,                    # a folder with the release's files: no download
    [string]$Tag,                       # a release tag (default: the latest)
    [switch]$Git,                       # today's path: clone ~/omawin and run install.ps1
    [switch]$NoStart                    # CI: no interactive desktop to start
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$clock = [Diagnostics.Stopwatch]::StartNew()

# Never as administrator (unchanged from today's bootstrap.ps1, lines 28-37). The one exception:
# Windows Sandbox's own account, which is an administrator and is thrown away (section 8.2).
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin -and -not ($env:OMAWIN_TEST_ALLOW_ADMIN -eq '1' -and $env:USERNAME -eq 'WDAGUtilityAccount')) {
    Write-Host '  This PowerShell runs as administrator. Open PowerShell normally and run the command again.' -ForegroundColor Yellow
    return
}

if (-not $Presets -and $env:OMAWIN_PRESETS) { $Presets = $env:OMAWIN_PRESETS -split ',' }
if (-not $Linux -and $env:OMAWIN_LINUX) { $Linux = $env:OMAWIN_LINUX }
if (-not $Yes -and $env:OMAWIN_YES -eq '1') { $Yes = [switch]$true }
if (-not $Bundle -and $env:OMAWIN_BUNDLE) { $Bundle = $env:OMAWIN_BUNDLE }
if (-not $Tag) { $Tag = if ($env:OMAWIN_RELEASE) { $env:OMAWIN_RELEASE } else { 'latest' } }
$RepoName = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' }

Set-ExecutionPolicy -Scope Process Bypass -Force
if ((Get-ExecutionPolicy -Scope CurrentUser) -notin 'RemoteSigned', 'Unrestricted', 'Bypass') {
    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force
}

# (Today's banner and Write-Step / Write-Note / Complete-Step, lines 49-130, stay as they are:
#  the step names change to "download", "unpack", "set up", "start".)

if ($Git -or $env:OMAWIN_LOCAL -eq '1') {
    # Today's path, unchanged: winget's Git, clone or pull ~/omawin, your settings, install.ps1.
    Invoke-GitBootstrap      # today's lines 131-205 moved into this function, word for word
    return
}

$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$Root = Join-Path $env:LOCALAPPDATA 'omawin'
$Downloads = Join-Path $Root 'downloads'
$Curl = Join-Path $env:SystemRoot 'System32\curl.exe'
$Tar = Join-Path $env:SystemRoot 'System32\tar.exe'
New-Item -ItemType Directory -Force -Path $Downloads | Out-Null
$Times = New-Object Collections.Generic.List[string]
function Mark([string]$Step) {
    $Times.Add(('bundle {0} {1}' -f $Step, [Math]::Round($clock.Elapsed.TotalSeconds, 1).ToString([Globalization.CultureInfo]::InvariantCulture)))
}

# curl.exe and tar.exe are in Windows 10 1803+ and 11. Without them (older Windows, a stripped
# image), the git path still works.
foreach ($t in $Curl, $Tar) { if (-not (Test-Path $t)) { throw "$t is missing (Windows 10 1803 or newer has it); try: bootstrap.ps1 -Git" } }

# --- 1. what the release holds -----------------------------------------------------------------
# No GitHub API: releases/latest/download/<name> redirects to the newest release's file, so no
# rate limit (60 API calls an hour per IP) and no JSON to parse.
$Base = if ($Bundle) { (Resolve-Path $Bundle).Path }
        elseif ($Tag -eq 'latest') { "https://github.com/$RepoName/releases/latest/download" }
        else { "https://github.com/$RepoName/releases/download/$Tag" }
$sumsFile = Join-Path $Downloads 'SHA256SUMS.txt'
if ($Bundle) { Copy-Item (Join-Path $Base 'SHA256SUMS.txt') $sumsFile -Force }
else {
    & $Curl -fsSL --retry 3 -o $sumsFile "$Base/SHA256SUMS.txt"
    if ($LASTEXITCODE -ne 0) { throw "couldn't fetch $Base/SHA256SUMS.txt (curl exit $LASTEXITCODE)" }
}
$Sums = @{}
foreach ($line in Get-Content $sumsFile) {
    if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') { $Sums[$Matches[2]] = $Matches[1].ToUpperInvariant() }
}
# The tag, from the stage 1a file's name (tags can hold dashes: the greedy group takes them).
$first = @($Sums.Keys | Where-Object { $_ -match "^omawin-(.+)-win-$Arch-1a\.zip$" })
if (-not $first) { throw "this release has no omawin bundle for win-$Arch; try: bootstrap.ps1 -Git" }
$null = $first[0] -match "^omawin-(.+)-win-$Arch-1a\.zip$"
$Tag = $Matches[1]
$Version = Join-Path $Root $Tag
Mark 'resolve'

# --- 2. download stage 1a and 1b in parallel, resumable, then verify ---------------------------
function Test-Sum([string]$Path, [string]$Name) {
    (Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Sums[$Name]
}
function Get-BundleFiles([string[]]$Names) {
    $todo = @($Names | Where-Object { -not (Test-Sum (Join-Path $Downloads $_) $_) })
    if (-not $todo) { return }
    if ($Bundle) {
        foreach ($n in $todo) { Copy-Item (Join-Path $Base $n) (Join-Path $Downloads $n) -Force }
    } else {
        # -C - resumes a partial file; --parallel fetches them together. A file that's already
        # whole but wrong is removed first (resuming it would only append).
        # ($args is PowerShell's own automatic variable: not used as a name here.)
        $curlArgs = @('-fL', '--retry', '3', '--retry-delay', '1', '--parallel', '--parallel-max', '4', '-C', '-')
        foreach ($n in $todo) {
            $curlArgs += @('-o', (Join-Path $Downloads $n), "$Base/$n")
        }
        & $Curl @curlArgs
        if ($LASTEXITCODE -ne 0) {
            # 33: the server won't resume; 416: range past the end. Once more, from the start.
            foreach ($n in $todo) { Remove-Item (Join-Path $Downloads $n) -ErrorAction SilentlyContinue }
            $curlArgs = @('-fL', '--retry', '3', '--parallel', '--parallel-max', '4')
            foreach ($n in $todo) { $curlArgs += @('-o', (Join-Path $Downloads $n), "$Base/$n") }
            & $Curl @curlArgs
            if ($LASTEXITCODE -ne 0) { throw "download failed (curl exit $LASTEXITCODE); run the command again to resume" }
        }
    }
    foreach ($n in $todo) {
        if (-not (Test-Sum (Join-Path $Downloads $n) $n)) {
            Remove-Item (Join-Path $Downloads $n) -Force
            throw "$n doesn't match its published SHA-256: not the release's file (run again to retry)"
        }
    }
}
$stage1 = @("omawin-$Tag-win-$Arch-1a.zip", "omawin-$Tag-win-$Arch-1b.zip")
Get-BundleFiles $stage1
Mark 'download'

# --- 3. unpack into <tag>.new, then rename: a half-unpacked folder never looks finished ---------
if (-not (Test-Path (Join-Path $Version 'files.tsv'))) {
    $staging = "$Version.new"
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    foreach ($z in $stage1) {
        & $Tar -xf (Join-Path $Downloads $z) -C $staging
        if ($LASTEXITCODE -ne 0) { throw "tar couldn't unpack $z" }
    }
    if (Test-Path $Version) { Remove-Item $Version -Recurse -Force }   # an earlier failed run of this same tag
    Rename-Item $staging (Split-Path $Version -Leaf)
}
Mark 'unpack'

# --- 4. the junctions ---------------------------------------------------------------------------
function Set-Link([string]$Link, [string]$Target) {
    $item = Get-Item -LiteralPath $Link -Force -ErrorAction SilentlyContinue
    if ($item -and $item.LinkType -eq 'Junction' -and @($item.Target)[0].TrimEnd('\') -eq $Target.TrimEnd('\')) { return }
    if ($item -and $item.LinkType) { [IO.Directory]::Delete($Link) }        # the junction only, never its target
    elseif ($item) { Move-Item -LiteralPath $Link "$Link.backup-$(Get-Date -Format yyyyMMdd-HHmmss)" }
    New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
}
Set-Link (Join-Path $Root 'current') $Version
$home_ = Join-Path $HOME 'omawin'
if (Test-Path (Join-Path $home_ '.git')) {
    Write-Note "~/omawin is a git checkout: it's used as it is (developer mode); the bundle's repo isn't linked"
} else {
    Set-Link $home_ (Join-Path $Root 'current\repo')
}
Mark 'link'

# --- 5. the rest is versioned with the bundle ---------------------------------------------------
$firstRun = Join-Path $Root 'current\repo\install\first-run.ps1'
$params = @{ Tag = $Tag; Arch = $Arch; Base = $Base; Times = $Times.ToArray(); Elapsed = $clock.Elapsed.TotalSeconds }
if ($Presets) { $params.Presets = $Presets }
if ($Linux) { $params.Linux = $Linux }
if ($Yes) { $params.Yes = $true }
if ($NoStart) { $params.NoStart = $true }
if ($Bundle) { $params.Offline = $true }
& $firstRun @params
```

*(check)* curl's exit on resuming an already complete file: recent curl treats a 416 on `-C -` as done; older builds return 33. The fallback above covers both. The first `Test-Sum` pass skips whole, correct files, so a rerun never resumes a finished one.

### 3.3 `install/first-run.ps1`

```powershell
<#
.SYNOPSIS
    A bundle install's first run: configs, PATH, font, sign-in task, the desktop, the queue.
.DESCRIPTION
    Run by bootstrap.ps1 right after unpacking (and by `omawin install` on a bundle install).
    Everything is per user; nothing asks for admin. Safe to run again: each part checks first.
    Windows PowerShell 5.1. ASCII only.
#>
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Arch = 'x64',
    [string]$Base,                  # where stage 2 comes from (URL or folder)
    [string[]]$Presets,
    [string]$Linux,
    [switch]$Yes,
    [switch]$NoStart,
    [switch]$Offline,
    [string[]]$Times = @(),
    [double]$Elapsed = 0
)
$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:LOCALAPPDATA 'omawin'
$Current = Join-Path $Root 'current'
$Repo = Join-Path $Current 'repo'
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force -Path $State | Out-Null
$clock = [Diagnostics.Stopwatch]::StartNew()
$timeLines = New-Object Collections.Generic.List[string]
foreach ($t in $Times) { $timeLines.Add($t) }
function Mark([string]$Step) {
    $timeLines.Add(('bundle {0} {1}' -f $Step, [Math]::Round($Elapsed + $clock.Elapsed.TotalSeconds, 1).ToString([Globalization.CultureInfo]::InvariantCulture)))
}
. "$Repo\install\seed.ps1"          # Install-Seed, Set-OmawinPath, Install-BundleFonts (section 3.4-3.6)
. "$Repo\install\queue-lib.ps1"     # Add-QueueStep, Start-QueueWorker (section 4)

Install-Seed -Repo $Repo -Current $Current -Stamp $Stamp
Mark 'seed'
Set-OmawinPath -Root $Root
Mark 'path'
Install-BundleFonts -Dir (Join-Path $Current 'fonts')
Mark 'font'

# The sign-in task: GlazeWM (the per-user build) through omawin.exe, at every sign-in.
$task = & "$Repo\install\login-start.ps1" -Omawin (Join-Path $Current 'bin\omawin.exe') -PassThru
Mark 'sign-in task'

# A fresh install runs no migrations: they're all marked done (Omarchy's omarchy-provision-user).
# A reinstall over an older omawin keeps its list: the queue runs the new ones.
$done = Join-Path $State 'migrations.txt'
if (-not (Test-Path $done)) {
    Get-ChildItem "$Repo\migrations" -Filter *.ps1 | Sort-Object Name | ForEach-Object BaseName | Set-Content $done
} else { Add-QueueStep 'migrate' 'migrate' 'what older versions left behind' }
Set-Content (Join-Path $State 'release.txt') $Tag -NoNewline

# The background queue (section 4). Stage 2 first: the tools appear as they land.
Add-QueueStep 'stage2' "stage2 $Tag $Arch $(if ($Offline) { 'offline' })" 'Command-line tools, Git, Neovim'
Add-QueueStep 'nvim' 'nvim-restore' "Neovim's plugins"
foreach ($s in 'no-ads', 'look', 'other') { Add-QueueStep "step:$s" "step $s" $s }
if ($Yes -or $Presets -or $Linux) {
    # Answers given up front (scripts, agents): no welcome page, the work starts now.
    foreach ($p in @($Presets | Where-Object { $_ -and $_ -ne 'none' })) { Add-QueueStep "preset:$p" "preset $p" $p }
    $pick = if ($Linux) { $Linux } else { 'arch' }
    if ($pick -ne 'none') { Add-QueueStep "linux:$pick" "linux $pick" "Linux ($pick) in WSL" }
    Set-Content (Join-Path $State 'welcome.txt') 'answered' -NoNewline
} else {
    Set-Content (Join-Path $State 'welcome.txt') 'pending' -NoNewline
}
Mark 'queue'

if (-not $NoStart) {
    # Start the desktop now, the way sign-in does: the task runs `omawin start glazewm`, which
    # starts the menu app (and the native bar) alongside GlazeWM.
    if ($task) { Start-ScheduledTask -TaskName $task }
    else { Start-Process -FilePath (Join-Path $Current 'bin\omawin.exe') -ArgumentList 'start', 'glazewm' -WindowStyle Hidden }
    $deadline = (Get-Date).AddSeconds(10)
    $session = (Get-Process -Id $PID).SessionId
    while ((Get-Date) -lt $deadline -and -not (Get-Process glazewm -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)) { Start-Sleep -Milliseconds 100 }
    Mark 'desktop'
    # The fonts are in this session's table now (omawin.exe fonts-load, section 3.6); the welcome
    # page opens on the new desktop unless everything was answered.
    if (-not ($Yes -or $Presets -or $Linux)) { & (Join-Path $Current 'bin\omawin.exe') launcher welcome }
}
Start-QueueWorker -Repo $Repo
Mark 'done'
Add-Content (Join-Path $State 'install-times.txt') $timeLines
```

Step names in `install-times.txt` are prefixed `bundle` so they don't collide with `install.ps1`'s `full`/`link` lines (the format stays `<mode> <step> <seconds>`, `install/ui.ps1:237`; here the seconds are cumulative since `irm`, which is what the budget table measures).

### 3.4 `install/seed.ps1`: configs in one pass, and the PATH

Today the configs are set up step by step (`install/shell.ps1`, `install/desktop.ps1`, `install/software.ps1`) with `Set-Junction`, `Set-FileContent` and `[Environment]::SetEnvironmentVariable`. One table replaces the scattered calls, read by first-run **and** by those steps (so there's one list):

`install/seed.txt`:

```
# What omawin puts in your account, in one pass (install/seed.ps1). install/first-run.ps1 and the
# install steps (shell, desktop, software) both read this list.
#   <kind> | <target> | <source>
# kinds:  junction  a folder link into the repo (configs read live)
#         stub      a small file written from <source> with {placeholders} filled in; an existing
#                   file that isn't ours is moved aside (<path>.backup-<stamp>)
#         copy      a file copied once if missing (the user owns it after that)
#         env       a user environment variable
# placeholders: {repo} {current} {home} {local} {appdata} {docs}
junction | {local}\nvim                                          | {repo}\nvim
junction | {appdata}\neovide                                     | {repo}\neovide
junction | {home}\.config\fastfetch                              | {repo}\fastfetch
stub     | {docs}\PowerShell\Microsoft.PowerShell_profile.ps1    | {repo}\install\seed\pwsh-profile.ps1
stub     | {docs}\WindowsPowerShell\Microsoft.PowerShell_profile.ps1 | {repo}\install\seed\pwsh-profile.ps1
stub     | {local}\noctty\config.ghostty                         | {repo}\install\seed\noctty.ghostty
stub     | {home}\.bashrc                                        | {repo}\install\seed\bashrc
copy     | {home}\.glzr\glazewm\config.yaml                      | {current}\seed\rendered\glazewm.yaml
copy     | {local}\launcher\theme.ghostty                        | {current}\seed\rendered\theme.ghostty
copy     | {local}\launcher\theme                                | {current}\seed\rendered\theme
copy     | {local}\Microsoft\Windows Terminal\Fragments\omawin\theme.json | {current}\seed\rendered\wt-theme.json
env      | STARSHIP_CONFIG                                       | {repo}\starship\starship.toml
env      | HERDR_CONFIG_PATH                                     | {repo}\herdr\config.toml
```

(The exact rows come from today's `install/shell.ps1:20,74,79,88,96,130-132`, `install/desktop.ps1:9`, `install/software.ps1`'s nvim/neovide links; check each against the code when building this.)

`install/seed.ps1`:

```powershell
# One pass over install/seed.txt, and the PATH in one write. Windows PowerShell 5.1.

function Expand-SeedPath([string]$s, [string]$Repo, [string]$Current) {
    $s.Replace('{repo}', $Repo).Replace('{current}', $Current).Replace('{home}', $HOME).
      Replace('{local}', $env:LOCALAPPDATA).Replace('{appdata}', $env:APPDATA).
      Replace('{docs}', [Environment]::GetFolderPath('MyDocuments'))
}

function Install-Seed([string]$Repo, [string]$Current, [string]$Stamp) {
    $changed = 0
    foreach ($line in [IO.File]::ReadAllLines("$Repo\install\seed.txt")) {
        if ($line -match '^\s*(#|$)') { continue }
        $f = @($line -split '\|' | ForEach-Object { $_.Trim() })
        $kind = $f[0]; $target = Expand-SeedPath $f[1] $Repo $Current; $source = Expand-SeedPath $f[2] $Repo $Current
        switch ($kind) {
            'junction' {
                $item = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
                if ($item -and $item.LinkType -and @($item.Target)[0].TrimEnd('\') -eq $source.TrimEnd('\')) { continue }
                if ($item -and $item.LinkType) { [IO.Directory]::Delete($target) }
                elseif ($item) { Move-Item -LiteralPath $target "$target.backup-$Stamp" }
                [void][IO.Directory]::CreateDirectory((Split-Path $target))
                New-Item -ItemType Junction -Path $target -Target $source | Out-Null
                $changed++
            }
            'stub' {
                $content = Expand-SeedPath ([IO.File]::ReadAllText($source)) $Repo $Current
                $old = if (Test-Path -LiteralPath $target) { [IO.File]::ReadAllText($target) } else { $null }
                if ($old -eq $content) { continue }
                # Ours: same first line, or an older omawin stub ("# Managed by"), as Set-FileContent decides today.
                $ours = $old -and ($old.StartsWith(($content -split "`n")[0]) -or $old.StartsWith('# Managed by '))
                if ($old -and -not $ours) { Move-Item -LiteralPath $target "$target.backup-$Stamp" }
                [void][IO.Directory]::CreateDirectory((Split-Path $target))
                [IO.File]::WriteAllText($target, $content)
                $changed++
            }
            'copy' {
                if (Test-Path -LiteralPath $target) { continue }        # the user's from now on
                [void][IO.Directory]::CreateDirectory((Split-Path $target))
                Copy-Item -LiteralPath $source $target
                $changed++
            }
            'env' {
                # Written straight to the registry; one WM_SETTINGCHANGE at the end (Set-OmawinPath).
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
                if ($key.GetValue($f[1]) -ne $source) { $key.SetValue($f[1], $source, 'String'); $changed++ }
                $key.Close()
                [Environment]::SetEnvironmentVariable($f[1], $source, 'Process')
            }
        }
    }
    $changed
}

# The user PATH in one write: omawin's own exes first, the bundled third-party tools last, so a
# package manager's copy always wins. Raw REG_EXPAND_SZ, as Add-UserPath (install/lib.ps1) does.
function Set-OmawinPath([string]$Root) {
    $first = @('%LOCALAPPDATA%\omawin\current\bin')
    $last = @('%LOCALAPPDATA%\omawin\tools', '%LOCALAPPDATA%\omawin\current\git\cmd',
              '%LOCALAPPDATA%\omawin\current\nvim\bin', '%LOCALAPPDATA%\omawin\current\pwsh')
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    $raw = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    $norm = { param($p) [Environment]::ExpandEnvironmentVariables($p).TrimEnd('\').ToLowerInvariant() }
    $ours = @($first + $last | ForEach-Object { & $norm $_ })
    # The user's own entries, in their order, without ours (wherever they were) and without blanks.
    $theirs = @($raw -split ';' | Where-Object { $_ -and ($ours -notcontains (& $norm $_)) })
    $new = (@($first) + $theirs + @($last)) -join ';'
    if ($new -ne $raw) { $key.SetValue('Path', $new, 'ExpandString') }
    $key.Close()
    $env:Path = [Environment]::ExpandEnvironmentVariables($new) + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
    # One WM_SETTINGCHANGE for everything above (Explorer re-reads the environment), the same
    # nudge Add-UserPath uses today.
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', '1', 'User')
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', $null, 'User')
}
```

`Add-UserPath -Prepend` for `~/.local/bin` (today's `install/helpers.ps1`) stays for git installs only. On a bundle install a migration moves omawin's own exes out of `~/.local/bin` aside (never anything else there), or the old `omawin.exe` would win.

### 3.5 The theme on first start

`themes/render.ps1` fills templates from the palette. First start uses the CI-rendered default theme (`seed\rendered\`, copied once by the `copy` rows above), so nothing renders on the critical path. `render.ps1` needs a `-OutDir` and `-Theme` for CI *(check: today it writes straight to the live paths; add the parameters, keep the default behaviour)*. The first `omawin theme` switch renders as today.

### 3.6 Fonts per user, no admin, no sign-out

The registry names come from `fonts\fonts.tsv` (CI worked them out, section 2.2), so no `System.Drawing` load per font on the PC.

```powershell
function Install-BundleFonts([string]$Dir) {
    $fontDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
    [void][IO.Directory]::CreateDirectory($fontDir)
    $key = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    $have = (Get-ItemProperty $key -ErrorAction SilentlyContinue)
    $added = 0
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $Dir 'fonts.tsv'))) {
        $file, $name = $line -split "`t", 2
        $dest = Join-Path $fontDir $file
        if (-not (Test-Path $dest)) { Copy-Item (Join-Path $Dir $file) $dest }
        if ($have.$name -ne $dest) { New-ItemProperty -Path $key -Name $name -Value $dest -PropertyType String -Force | Out-Null; $added++ }
    }
    # Load them into this session now, so the bar, the menu app and noctty draw glyphs without a
    # sign-out: omawin.exe's builtin (C# 5), not Add-Type (compiling costs ~1 s in 5.1).
    if ($added) { & (Join-Path $env:LOCALAPPDATA 'omawin\current\bin\omawin.exe') fonts-load $fontDir }
}
```

In `bin/omawin.cs` (C# 5: no `$""`, no `?.`, no expression-bodied members), a builtin `fonts-load <dir>`:

```csharp
[DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceW(string file);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

// Per-user fonts (HKCU) are loaded at sign-in; ones registered after that are added to this
// session's font table here, and every window is told (WM_FONTCHANGE), so nothing needs a sign-out.
static int FontsLoad(string dir)
{
    int n = 0;
    foreach (var f in Directory.GetFiles(dir, "*.ttf")) if (AddFontResourceW(f) > 0) n++;
    IntPtr ignored;
    SendMessageTimeoutW(new IntPtr(0xffff), 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out ignored);
    Console.WriteLine("loaded " + n + " fonts");
    return 0;
}
```

*(check)* whether processes started after the HKCU registration see the font without `AddFontResource` (today's install says "restart Windows Terminal", which suggests yes); the builtin covers the running ones either way.

### 3.7 The sign-in task: `install/login-start.ps1`, per user

Today it registers only when `Program Files\glzr.io\GlazeWM\glazewm.exe` exists and points at `~/.local/bin/omawin.exe`. Changed: it takes the `omawin.exe` path, doesn't need GlazeWM in Program Files (omawin.exe finds the per-user build, section 7.2), and falls back to the `Run` key if Task Scheduler refuses.

```powershell
param([string]$Omawin = "$HOME\.local\bin\omawin.exe", [switch]$PassThru)
$ErrorActionPreference = 'Stop'
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$taskName = "omawin GlazeWM ($([Environment]::UserName))"
$registered = $false
try {
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" `
        -Argument "--headless `"$Omawin`" start glazewm" -WorkingDirectory $HOME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -Priority 4 -MultipleInstances IgnoreNew
    # Limited + Interactive for this account only: a standard user may register this (check on a
    # standard account; the CI job in section 8.1 proves it).
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    $null = Register-ScheduledTask -TaskName $taskName -Trigger $trigger -Action $action -Settings $settings `
        -Principal $principal -Description 'GlazeWM at sign-in (omawin install/login-start.ps1)' -Force
    $registered = [bool](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
} catch { }
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($registered) {
    if ((Get-ItemProperty $run -ErrorAction SilentlyContinue).omawin) { Remove-ItemProperty $run -Name omawin }
    if ((Get-ItemProperty $run -ErrorAction SilentlyContinue).GlazeWM) { Remove-ItemProperty $run -Name GlazeWM }
} else {
    # Slower (Windows starts the Run list late) but never refused.
    Set-ItemProperty $run -Name omawin -Value "`"$env:SystemRoot\System32\conhost.exe`" --headless `"$Omawin`" start glazewm"
}
# (Today's StartupDelayInMSec / WaitForIdleState lines stay here, unchanged.)
if ($PassThru) { if ($registered) { $taskName } else { $null } }
else { if ($registered) { "ok  GlazeWM starts at sign-in (scheduled task '$taskName')" } else { 'ok  GlazeWM starts at sign-in (Run key; the task was refused)' } }
```

`install/desktop.ps1:15` keeps calling it with no arguments (git installs: `~/.local/bin`).

---

## 4. The welcome page and the background queue

### 4.1 Files

| File | Written by | Read by | Format |
|---|---|---|---|
| `%LOCALAPPDATA%\launcher\install-queue.txt` | anyone (`Add-QueueStep`, under a mutex) | the worker | pending steps only, one per line: `<id> \| <verb and arguments> \| <label>` |
| `%LOCALAPPDATA%\launcher\install-progress.log` | the worker (append only) | the Install page, `omawin check` | `<iso time>\t<id>\t<run\|done\|fail\|skip\|blocked>\t<tries>\t<text>` |
| `%LOCALAPPDATA%\launcher\install-progress.txt` | the worker (whole file, atomic rename) | the bar (a `FileSystemWatcher`, no polling) | `key\tvalue` lines, below |
| `%LOCALAPPDATA%\launcher\welcome.txt` | first run, the welcome page | first run, `omawin.exe` at sign-in | `pending` or `answered` |
| `%LOCALAPPDATA%\launcher\admin-pending.txt` | the worker, `omawin pkg` | the welcome page, the bar | one admin verb per line (section 7.4), waiting for you to say when |

`install-queue.txt`:

```
# omawin's background install (bin/install-queue.ps1 runs it; finished steps leave this file).
#   <id> | <verb> [arguments] | <label in the bar and on the Install page>
stage2      | stage2 v0.9.0 x64       | Command-line tools, Git, Neovim
nvim        | nvim-restore            | Neovim's plugins
step:no-ads | step no-ads             | Windows' ads and suggestions off
step:look   | step look               | The taskbar and the lock screen
preset:go   | preset go               | Go
linux:arch  | linux arch              | Arch Linux in WSL
```

`install-progress.txt` (what the bar reads; rewritten whole after every change):

```
state	running
total	6
done	2
failed	0
current	preset:go	Go
updated	2026-10-30T10:42:13
```

`state` is `running`, `done` (everything finished: the bar shows it for a minute, then hides), `failed` (some steps failed; they're retried at the next sign-in), `waiting` (a step is blocked on your answer: the welcome page, an admin prompt you haven't approved).

### 4.2 Verbs

A fixed list, so a queue line can only ever run omawin's own steps:

| Verb | Runs | Already done when |
|---|---|---|
| `stage2 <tag> <arch> [offline]` | `bin/bundle.ps1 -Stage 2 -Tag <tag>` (download, verify, unpack into the version folder, relink `omawin\tools`) | `current\files.tsv`'s stage 2 files all present (size check) |
| `nvim-restore` | `nvim --headless "+Lazy! restore" +qa` (copies `current\nvim-data\lazy` into `%LOCALAPPDATA%\nvim-data\lazy` first when that's empty) | `nvim-data\lazy\lazy.nvim` exists and `lazy-lock.json` matches the repo's |
| `step <name>` | `powershell -File install.ps1 -Step <name>` with `OMAWIN_NO_ADMIN=1`, in a child process (a step that throws can't take the worker down) | the step's own checks (each step already skips what's done) |
| `preset <name>` | `bin/pkg.ps1 preset <name>` (section 6.8) | every package of the preset installed (`omawin pkg` says) |
| `pkg <names…>` | `bin/pkg.ps1 install <names…> --quiet` | each installed |
| `linux <arch\|ubuntu>` | `wsl/install.ps1` for that distro, after `Test-WslReady` (section 7.3); not ready → `blocked`, and `wsl-platform` goes into `admin-pending.txt` | the distro in `HKCU:\...\Lxss` |
| `migrate` | `bin/migrate.ps1` | nothing new to run |
| `git-identity` | writes the welcome page's name and email (`git config --global`) | `user.name` set |

Admin work is **never** a queue verb: it waits in `admin-pending.txt` for you to approve it (section 7.4).

### 4.3 `install/queue-lib.ps1`

```powershell
# The background queue's shared helpers (install/first-run.ps1, the welcome page's actions,
# bin/pkg.ps1, bin/install-queue.ps1). Windows PowerShell 5.1.
$QueueFile = Join-Path $env:LOCALAPPDATA 'launcher\install-queue.txt'
$QueueVerbs = @('stage2', 'nvim-restore', 'step', 'preset', 'pkg', 'linux', 'migrate', 'git-identity')

# A named mutex: the worker and anything adding steps never write the file at the same time.
function Use-QueueLock([scriptblock]$Do) {
    $m = New-Object Threading.Mutex($false, 'Local\omawin-install-queue-file')
    try { [void]$m.WaitOne(10000); & $Do } finally { $m.ReleaseMutex(); $m.Dispose() }
}

function Get-QueueSteps {
    if (-not (Test-Path $QueueFile)) { return @() }
    foreach ($line in [IO.File]::ReadAllLines($QueueFile)) {
        if ($line -match '^\s*(#|$)') { continue }
        $f = @($line -split '\|' | ForEach-Object { $_.Trim() })
        $words = @($f[1] -split '\s+')
        [pscustomobject]@{ Id = $f[0]; Verb = $words[0]; Args = @($words | Select-Object -Skip 1); Label = $f[2]; Line = $line }
    }
}

# Adds a step unless one with that id is already waiting. Refuses a verb that isn't on the list.
function Add-QueueStep([string]$Id, [string]$Command, [string]$Label) {
    $verb = ($Command -split '\s+')[0]
    if ($QueueVerbs -notcontains $verb) { throw "not a queue verb: $verb" }
    if ($Id -match '\|' -or $Command -match '\|' -or $Label -match '\|') { throw 'no | in a queue line' }
    Use-QueueLock {
        if (@(Get-QueueSteps | Where-Object Id -eq $Id)) { return }
        if (-not (Test-Path $QueueFile)) {
            [IO.File]::WriteAllText($QueueFile, "# omawin's background install (bin/install-queue.ps1 runs it; finished steps leave this file).`r`n")
        }
        [IO.File]::AppendAllText($QueueFile, "$Id | $Command | $Label`r`n")
    }
}

function Remove-QueueStep([string]$Id) {
    Use-QueueLock {
        $keep = @([IO.File]::ReadAllLines($QueueFile) | Where-Object { -not ($_ -match "^\s*$([regex]::Escape($Id))\s*\|") })
        [IO.File]::WriteAllLines($QueueFile, [string[]]$keep)
    }
}

# Starts the worker hidden, unless one is running (it holds Local\omawin-install-queue).
function Start-QueueWorker([string]$Repo, [switch]$AtSignIn) {
    $a = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$Repo\bin\install-queue.ps1`"")
    if ($AtSignIn) { $a += '-AtSignIn' }
    Start-Process powershell.exe -ArgumentList $a -WindowStyle Hidden
}
```

### 4.4 The worker: `bin/install-queue.ps1`

```powershell
<#
.SYNOPSIS
    omawin's background install: runs install-queue.txt's steps, one at a time, while you use the
    desktop. Progress for the bar in install-progress.txt; history in install-progress.log.
.DESCRIPTION
    Started by install/first-run.ps1, by the welcome page, and at sign-in by omawin.exe when steps
    are still waiting (a failed step stays queued: Omarchy's deferred steps, retried each boot).
    One worker at a time. Never asks for admin (OMAWIN_NO_ADMIN=1 for everything it runs).
    Windows PowerShell 5.1.
#>
param([switch]$AtSignIn, [int]$MaxTries = 5)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
. "$Repo\install\queue-lib.ps1"
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$Log = Join-Path $State 'install-progress.log'
$Progress = Join-Path $State 'install-progress.txt'
$env:OMAWIN_NO_ADMIN = '1'

$single = New-Object Threading.Mutex($false, 'Local\omawin-install-queue')
if (-not $single.WaitOne(0)) { return }        # another worker has it
try {
    if ($AtSignIn) { Start-Sleep -Seconds 20 }  # not in the rush of signing in (as the bar's update check)

    $tries = @{}                                # from the log: how often each id failed before
    if (Test-Path $Log) {
        foreach ($l in [IO.File]::ReadAllLines($Log)) { $p = $l -split "`t"; if ($p.Count -ge 4 -and $p[2] -eq 'fail') { $tries[$p[1]] = [int]$p[3] } }
    }
    $done = 0; $failed = 0
    $total = @(Get-QueueSteps).Count

    function Write-Progress2([string]$StateWord, $Step) {
        $lines = @("state`t$StateWord", "total`t$total", "done`t$done", "failed`t$failed")
        if ($Step) { $lines += "current`t$($Step.Id)`t$($Step.Label)" }
        $lines += "updated`t$((Get-Date).ToString('s'))"
        $tmp = "$Progress.tmp"
        [IO.File]::WriteAllLines($tmp, [string[]]$lines)
        # Replace, so the bar's watcher never reads half a file.
        if (Test-Path $Progress) { [IO.File]::Replace($tmp, $Progress, $null) } else { [IO.File]::Move($tmp, $Progress) }
    }
    function Write-Log2([string]$Id, [string]$What, [int]$Try, [string]$Text) {
        [IO.File]::AppendAllText($Log, "$((Get-Date).ToString('s'))`t$Id`t$What`t$Try`t$($Text -replace '[\r\n\t]+', ' ')`r`n")
    }

    # Runs a script in its own powershell.exe: its exit code is the answer, its output goes to the log.
    function Invoke-Child([string]$Script, [string[]]$Arguments, [int]$Minutes = 60) {
        $out = Join-Path $env:TEMP "omawin-queue-$PID.txt"
        $a = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$Script`"") + $Arguments
        $p = Start-Process powershell.exe -ArgumentList $a -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
        if (-not $p.WaitForExit($Minutes * 60000)) { try { $p.Kill() } catch { }; return @{ Ok = $false; Text = "gave up after $Minutes minutes" } }
        $text = (@(Get-Content $out, "$out.err" -ErrorAction SilentlyContinue) | Where-Object { $_.Trim() } | Select-Object -Last 1)
        Remove-Item $out, "$out.err" -ErrorAction SilentlyContinue
        @{ Ok = ($p.ExitCode -eq 0); Text = "$text" }
    }

    function Invoke-Step($s) {
        switch ($s.Verb) {
            'stage2'       { Invoke-Child "$Repo\bin\bundle.ps1" (@('-Stage', '2', '-Tag', $s.Args[0], '-Arch', $s.Args[1]) + $(if ($s.Args -contains 'offline') { @('-Offline') } else { @() })) }
            'nvim-restore' { Invoke-Child "$Repo\bin\nvim-restore.ps1" @() 30 }
            'step'         { Invoke-Child "$Repo\install.ps1" @('-Step', $s.Args[0]) }
            'preset'       { Invoke-Child "$Repo\bin\pkg.ps1" @('preset', $s.Args[0], '--quiet') }
            'pkg'          { Invoke-Child "$Repo\bin\pkg.ps1" (@('install') + $s.Args + @('--quiet')) }
            'linux'        {
                . "$Repo\install\wsl-ready.ps1"           # Test-WslReady (section 7.3)
                $ready = Test-WslReady
                if ($ready.NeedsAdmin) {
                    Add-Content (Join-Path $State 'admin-pending.txt') $ready.AdminVerbs
                    return @{ Ok = $false; Blocked = $true; Text = 'needs Windows'' Virtual Machine Platform: one admin prompt, when you choose (welcome page)' }
                }
                Invoke-Child "$Repo\wsl\install.ps1" @('-Linux', $s.Args[0])
            }
            'migrate'      { Invoke-Child "$Repo\bin\migrate.ps1" @() }
            'git-identity' { Invoke-Child "$Repo\bin\git-identity.ps1" @() 1 }
            default        { @{ Ok = $false; Text = "unknown verb $($s.Verb)" } }
        }
    }

    foreach ($s in @(Get-QueueSteps)) {
        $n = [int]$tries[$s.Id]
        if ($n -ge $MaxTries) { $failed++; Write-Log2 $s.Id 'skip' $n "failed $n times; Install page: retry"; continue }
        Write-Progress2 'running' $s
        Write-Log2 $s.Id 'run' $n ''
        $r = try { Invoke-Step $s } catch { @{ Ok = $false; Text = "$_" } }
        if ($r.Ok) {
            $done++; Remove-QueueStep $s.Id; Write-Log2 $s.Id 'done' $n $r.Text
        } elseif ($r.Blocked) {
            Write-Log2 $s.Id 'blocked' $n $r.Text           # stays queued, not counted as a failure
        } else {
            $failed++; Write-Log2 $s.Id 'fail' ($n + 1) $r.Text
        }
        Write-Progress2 'running' $null
    }
    $left = @(Get-QueueSteps).Count
    $final = if ($failed) { 'failed' } elseif ($left) { 'waiting' } else { 'done' }
    Write-Progress2 $final $null
    # The toast (roadmap 5.5) when it exists; until then the bar's indicator says it. (check)
    if ($final -eq 'done') { & "$env:LOCALAPPDATA\omawin\current\bin\omawin.exe" notify 'omawin is ready' 'Everything finished installing.' 2>$null }
} finally { $single.ReleaseMutex(); $single.Dispose() }
```

Steps run one at a time on purpose for a first version (winget runs one MSI at a time anyway). Measured later: `stage2` (network) and `step no-ads` (registry) can overlap; a `parallel` group column is the extension point.

**Retry at the next sign-in** goes through `omawin.exe`, which already runs at every sign-in. In `bin/omawin.cs`'s `StartGlazeWM` (C# 5), after GlazeWM is up:

```csharp
// The background install's steps still waiting (a failed one stays queued): start its worker,
// as Omarchy retries its deferred steps at every boot. One file read; no PowerShell when empty.
static void ResumeInstallQueue()
{
    try
    {
        var queue = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"launcher\install-queue.txt");
        if (!File.Exists(queue)) return;
        bool pending = false;
        foreach (var line in File.ReadAllLines(queue))
        {
            var t = line.Trim();
            if (t.Length > 0 && t[0] != '#') { pending = true; break; }
        }
        if (!pending) return;
        var script = Path.Combine(Repo, @"bin\install-queue.ps1");
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -AtSignIn")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Repo });
        Trace("install-queue-resumed");
    }
    catch (Exception) { }
}
```

Called as the last thing before `return 0;` in `StartGlazeWM` (after the `InSession("glazewm")` check succeeds).

### 4.5 The bar's install indicator

`menu-app/Bar/Bar.cs`: a watcher, no timer (AGENTS.md: event, not polling).

```csharp
// The background install (bin/install-queue.ps1 writes install-progress.txt whole, by rename).
public sealed record InstallProgress(string State, int Done, int Total, int Failed, string? Current);
static InstallProgress? install;
static FileSystemWatcher? installWatcher;

static void WatchInstall()
{
    var dir = MenuModel.StateDir;
    install = ReadInstall();
    installWatcher = new FileSystemWatcher(dir, "install-progress.txt")
    { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime, EnableRaisingEvents = true };
    FileSystemEventHandler changed = (_, _) => { install = ReadInstall(); ui?.TryEnqueue(Push); };
    installWatcher.Changed += changed;
    installWatcher.Created += changed;
    installWatcher.Renamed += (_, _) => { install = ReadInstall(); ui?.TryEnqueue(Push); };
}

static InstallProgress? ReadInstall()
{
    try
    {
        var kv = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(Path.Combine(MenuModel.StateDir, "install-progress.txt")))
        {
            var p = line.Split('\t', 2);
            if (p.Length == 2) kv[p[0]] = p[1];
        }
        int I(string k) => kv.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : 0;
        var current = kv.TryGetValue("current", out var c) ? c.Split('\t').Last() : null;
        return new InstallProgress(kv.GetValueOrDefault("state", "idle"), I("done"), I("total"), I("failed"), current);
    }
    catch (IOException) { return install; }      // mid-replace: keep the last good one
    catch (Exception) { return null; }
}
```

`BarData` gets an `InstallProgress? Install` field; `IndicatorsWidget` (`menu-app/Bar/Widgets.cs:217`) gets a third `Cell`: a download glyph with `done/total`, red when `failed > 0`, hidden when `state` is `done` for more than a minute or the file is absent; click opens `omawin launcher install` (the Install page: the log, per step, with *Retry* and *Skip*).

### 4.6 The welcome page

A launcher page `welcome` (`menu-app/Pages.cs` + `MainWindow.Pages.cs`, like `packages` and `quick`), opened by `omawin launcher welcome` on the first desktop. No new key. Rows:

| Row | Default | Writes |
|---|---|---|
| Presets (checkboxes from `packages.ps1`'s `$Presets`; read via `omawin preset list --tsv`) | none | `presets.txt`, then `preset <name>` queue steps |
| Linux: Arch / Ubuntu / none | Arch | `defaults\linux`, then `linux <name>` |
| Git name and email | from git if set | `git-identity` (values in `%LOCALAPPDATA%\launcher\welcome-git.txt`, read once and deleted) |
| Your settings repos (shared, account; today's bootstrap questions) | empty | `bin/shared.ps1 -From`, `git clone` into the account folder (queued as `step account`) *(check: today's bootstrap.ps1:162-202 becomes an install step `account`)* |
| Dev Drive, the tune-up (8G) | off | admin items: listed with a shield; choosing them adds to `admin-pending.txt` |
| *Start* | | appends the queue lines, `welcome.txt` = `answered`, starts the worker |
| *Admin: N changes* (only when `admin-pending.txt` has lines) | | the preview and the one prompt (section 7.4) |

The page's actions call commands, not scripts directly: a new hidden command row in `bin/commands.txt`:

```
welcome apply | The welcome page's answers: presets, Linux, git identity; starts the background install | pwsh bin/welcome-apply.ps1
```

`bin/welcome-apply.ps1 -Presets go,data -Linux arch -GitName 'A' -GitEmail 'a@b'` validates (preset names against `$Presets`, Linux against `bin/defaults.txt`), writes the files, calls `Add-QueueStep` per answer and `Start-QueueWorker`. The page is a form over that one command, so the same answers work from a terminal.

---

## 5. Delta updates and rollback

`omawin update` on a bundle install (no `.git` at `~/omawin`) runs `bin/update-bundle.ps1`; a git checkout keeps `bin/update.ps1`. `bin/commands.txt`'s `update` row points at a small dispatcher (`bin/update-dispatch.ps1`: `.git` there → `update.ps1`, else `update-bundle.ps1`, passing the arguments through).

### 5.1 `bin/update-bundle.ps1`

```powershell
<#
.SYNOPSIS
    omawin update for a bundle install: the next release, only the files that changed.
.DESCRIPTION
    1. the newest tag (releases/latest/download/SHA256SUMS.txt; no API)
    2. its files-<arch>.tsv against current\files.tsv
    3. a delta zip from this version when the release has one, else the stage zips with changes
    4. the new version folder assembled: unchanged files hard-linked from the current one, changed
       ones from the download, every changed file's hash checked
    5. current flipped to it, the new migrations run; a failing migration flips back
    6. the running parts restarted from the new folder (menu app, bar, GlazeWM)
    Never asks for admin: a pending admin change (a newer signed GlazeWM in Program Files) is
    written to admin-pending.txt and waits for you.
        omawin update               the update
        omawin update -Rollback     back to the previous version (kept until the next update)
        omawin update -Check        how many updates are waiting (the bar's arrow)
    Windows PowerShell 5.1.
#>
param([switch]$Rollback, [switch]$Check, [string]$Tag = 'latest', [string]$Base)
$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:LOCALAPPDATA 'omawin'
$CurrentLink = Join-Path $Root 'current'
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$Curl = Join-Path $env:SystemRoot 'System32\curl.exe'
$Tar = Join-Path $env:SystemRoot 'System32\tar.exe'
$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$RepoName = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' }
$now = (Get-Item $CurrentLink).Target | Select-Object -First 1
$nowTag = Split-Path $now -Leaf

# Hard links: unchanged files cost no space or copy time. Same volume (both under %LOCALAPPDATA%).
Add-Type -Namespace Omawin -Name Fs -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool CreateHardLinkW(string newFile, string existingFile, IntPtr security);
'@

function Set-Current([string]$Target) {
    # Not atomic on Windows (no rename over a junction): remove, create. The gap is milliseconds,
    # and nothing starts from current\ in it but a key press, which retries (omawin.exe). (check)
    $tmp = "$CurrentLink.new"
    if (Test-Path $tmp) { [IO.Directory]::Delete($tmp) }
    New-Item -ItemType Junction -Path $tmp -Target $Target | Out-Null
    [IO.Directory]::Delete($CurrentLink)
    [IO.Directory]::Move($tmp, $CurrentLink)
}

function Restart-Parts {
    # The menu app (and with it the bar), from the new folder; GlazeWM reloads the same way the
    # theme switch does. The exes of the old folder keep running until stopped: nothing is deleted.
    & "$CurrentLink\repo\menu-app\stop-menu.ps1" -Dir "$CurrentLink\menu"
    Start-Process "$CurrentLink\menu\SystemMenu.exe" -ArgumentList '--start-hidden'
    # GlazeWM: exit and let omawin.exe start the new build (the sign-in task's command).
    & "$CurrentLink\glazewm\cli\glazewm.exe" command wm-exit 2>$null      # (check: the exit command's name)
    Start-Process "$CurrentLink\bin\omawin.exe" -ArgumentList 'start', 'glazewm' -WindowStyle Hidden
}

if ($Rollback) {
    $prev = "$(Get-Content (Join-Path $Root 'previous.txt') -ErrorAction SilentlyContinue)".Trim()
    if (-not $prev -or -not (Test-Path (Join-Path $Root "$prev\files.tsv"))) { throw 'no previous version to go back to' }
    Set-Current (Join-Path $Root $prev)
    Set-Content (Join-Path $Root 'previous.txt') $nowTag -NoNewline
    Set-Content (Join-Path $State 'release.txt') $prev -NoNewline
    Restart-Parts
    "back on $prev (omawin update brings $nowTag again)"
    # Migrations aren't undone: each is safe to run again and leaves the PC working on either version.
    return
}

# --- 1. what's newest --------------------------------------------------------------------------
if (-not $Base) { $Base = if ($Tag -eq 'latest') { "https://github.com/$RepoName/releases/latest/download" } else { "https://github.com/$RepoName/releases/download/$Tag" } }
$work = Join-Path $Root 'downloads\update'
New-Item -ItemType Directory -Force $work | Out-Null
& $Curl -fsSL --retry 2 -o "$work\SHA256SUMS.txt" "$Base/SHA256SUMS.txt"
if ($LASTEXITCODE) { if ($Check) { 0; return }; throw "couldn't reach $Base (offline?)" }
$sums = @{}
foreach ($l in Get-Content "$work\SHA256SUMS.txt") { if ($l -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') { $sums[$Matches[2]] = $Matches[1].ToUpperInvariant() } }
$newTag = @($sums.Keys | ForEach-Object { if ($_ -match "^omawin-(.+)-win-$Arch-1a\.zip$") { $Matches[1] } }) | Select-Object -First 1
if (-not $newTag) { throw "no win-$Arch bundle in that release" }
if ($Check) { if ($newTag -ne $nowTag) { 1 } else { 0 }; return }
if ($newTag -eq $nowTag) { "already on $nowTag"; return }

function Get-Verified([string]$Name) {
    $dest = Join-Path $work $Name
    if ((Test-Path $dest) -and (Get-FileHash $dest -Algorithm SHA256).Hash -eq $sums[$Name]) { return $dest }
    & $Curl -fL --retry 3 -C - -o $dest "$Base/$Name"
    if ($LASTEXITCODE) { throw "download failed: $Name" }
    if ((Get-FileHash $dest -Algorithm SHA256).Hash -ne $sums[$Name]) { Remove-Item $dest; throw "$Name doesn't match its SHA-256" }
    $dest
}

# --- 2. compare the per-file manifests -----------------------------------------------------------
function Read-Files([string]$Path) {
    $t = @{}
    foreach ($l in [IO.File]::ReadAllLines($Path)) {
        if ($l.StartsWith('#')) { continue }
        $p = $l.Split("`t")
        $t[$p[3]] = @{ Stage = $p[0]; Size = [long]$p[1]; Hash = $p[2] }
    }
    $t
}
$old = Read-Files (Join-Path $now 'files.tsv')
$new = Read-Files (Get-Verified "files-$Arch.tsv")
$changed = @($new.Keys | Where-Object { -not $old.ContainsKey($_) -or $old[$_].Hash -ne $new[$_].Hash })
# Stage 2 not installed yet on this PC (its queue step still waiting): only stages 1a/1b now.
$haveStage2 = Test-Path (Join-Path $now 'tools')
"$($changed.Count) of $($new.Count) files changed ($nowTag -> $newTag)"

# --- 3. the changed files: a delta zip from this version, else the stage zips with changes -------
$from = Join-Path $work 'from'
Remove-Item $from -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $from | Out-Null
$delta = "omawin-$newTag-win-$Arch-from-$nowTag.zip"
if ($sums.ContainsKey($delta)) {
    & $Tar -xf (Get-Verified $delta) -C $from          # laid out <stage>\<path>
} else {
    $stages = @($changed | ForEach-Object { $new[$_].Stage } | Select-Object -Unique | Where-Object { $_ -ne '2' -or $haveStage2 })
    foreach ($s in $stages) {
        $d = Join-Path $from $s
        New-Item -ItemType Directory -Force $d | Out-Null
        & $Tar -xf (Get-Verified "omawin-$newTag-win-$Arch-$s.zip") -C $d
    }
}

# --- 4. assemble <newTag>.new: hard links for the unchanged, the download for the changed --------
$target = Join-Path $Root $newTag
$staging = "$target.new"
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
foreach ($path in $new.Keys) {
    $e = $new[$path]
    if ($e.Stage -eq '2' -and -not $haveStage2) { continue }
    $dest = Join-Path $staging $path
    [void][IO.Directory]::CreateDirectory((Split-Path $dest))
    if ($old.ContainsKey($path) -and $old[$path].Hash -eq $e.Hash) {
        if (-not [Omawin.Fs]::CreateHardLinkW($dest, (Join-Path $now $path), [IntPtr]::Zero)) { Copy-Item (Join-Path $now $path) $dest }
    } else {
        $src = Join-Path $from "$($e.Stage)\$path"
        if (-not (Test-Path $src)) { throw "the update has no $path" }
        # Moved, not copied: the extracted file is ours. Then checked against the manifest.
        Move-Item $src $dest
        if ((Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $e.Hash) { throw "$path doesn't match files-$Arch.tsv" }
    }
}
Copy-Item (Join-Path $work "files-$Arch.tsv") (Join-Path $staging 'files.tsv') -Force
if (Test-Path $target) { Remove-Item $target -Recurse -Force }    # a half-finished earlier try of the same tag
Rename-Item $staging (Split-Path $target -Leaf)

# --- 5. flip, migrate, or flip back --------------------------------------------------------------
Set-Current $target
$global:LASTEXITCODE = 0
& "$CurrentLink\repo\bin\migrate.ps1"
if ($LASTEXITCODE -ne 0) {
    Set-Current $now
    throw "a migration of $newTag failed (above); still on $nowTag. Fix it, then: omawin update"
}
Set-Content (Join-Path $Root 'previous.txt') $nowTag -NoNewline
Set-Content (Join-Path $State 'release.txt') $newTag -NoNewline
& "$CurrentLink\repo\install\first-run.ps1" -Tag $newTag -Arch $Arch -NoStart -RelinkOnly   # seed rows new in this version, PATH, tools links (check: add -RelinkOnly)
& "$CurrentLink\bin\bundle.ps1" -Relink                                                     # %LOCALAPPDATA%\omawin\tools hard links (section 6.6)
Restart-Parts

# --- 6. keep two versions: the new one and the one before (rollback). Older ones are omawin's
# own folders, not the user's files: removed, unless something still runs from them.
$keep = @($newTag, $nowTag)
foreach ($d in Get-ChildItem $Root -Directory | Where-Object { $_.Name -match '^v\d' -and $keep -notcontains $_.Name }) {
    try { Remove-Item $d.FullName -Recurse -Force } catch { }   # in use: removed next time
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
"updated $nowTag -> $newTag"
```

Notes:
- **Hard links share bytes.** A version folder is never written in place (configs the user edits live outside it; `copy` seed rows copy out once). `omawin check` warns if a version folder file's hash differs from its `files.tsv` (a cheap size check every time; the full hash with `-Deep`).
- **Running exes** of the old folder keep running after the flip: their files aren't touched (the new folder links to them, which a running image allows).
- The old `install.ps1 -LinkOnly -UpdateInfo` screen: `update-bundle.ps1` writes the same `from/to/commit/note` lines (`commit` lines from the release's notes, `gh`-free: a `CHANGES.txt` the release job adds to stage 1a's `repo\`) and calls the install screen for its finale *(check: worth it, or plain lines)*.
- The bar's update arrow: `bin/update-available.ps1` learns the bundle case: `update-bundle.ps1 -Check` (one small download, every 6 hours, no API).

---

## 6. `omawin pkg` (Phase 2B)

### 6.1 PowerShell, not C#

A PowerShell module (`bin/pkg/Pkg.psm1`) with a thin entry script (`bin/pkg.ps1`), **Windows PowerShell 5.1-compatible**. Why:
- every backend is a command-line program; the work is starting processes and reading their text: PowerShell's home ground;
- the install (5.1, before pwsh exists), the background queue's `preset`/`pkg` verbs and `packages.ps1` all need it, and they can dot-source or import a module but not call into the menu app;
- it's never on a key (it waits on winget for seconds anyway), so PowerShell's ~0.3 s start doesn't show (AGENTS.md: speed rule);
- the menu app's Packages page keeps its own fast search (`menu-app/Packages.cs`) and calls `omawin pkg install|remove|info` for actions, and reads `packages.tsv` for marks: no logic duplicated.

### 6.2 `packages/names.tsv`: one name per thing

Tab-separated; `-` = not in that backend. `prefer` is omawin's default backend for it (`bundle` only for names in `bundle/manifest.json`, which are not repeated here: that file's `ids` are read too).

```
# omawin pkg's names: one omawin name, each backend's id. Tab-separated; - where a backend lacks it.
# prefer: omawin's default backend for this name (your `omawin pkg prefer` order comes after a
# per-package pin, before the first backend that has it). Bundled tools are in bundle/manifest.json.
# kind: cli | gui | lang | font | lib
name	winget	scoop	choco	mise	arch	apt	command	kind	prefer
python	Python.Python.3.13	python	python	python	python	python3	python	lang	mise
node	OpenJS.NodeJS.LTS	nodejs-lts	nodejs-lts	node	nodejs	nodejs	node	lang	mise
pnpm	pnpm.pnpm	pnpm	pnpm	pnpm	pnpm	-	pnpm	cli	mise
go	GoLang.Go	go	golang	go	go	golang-go	go	lang	mise
elixir	Elixir.Elixir	elixir	elixir	elixir	elixir	elixir	elixir	lang	mise
erlang	Erlang.ErlangOTP	erlang	erlang	erlang	erlang	erlang	erl	lang	mise
bun	Oven-sh.Bun	bun	bun	bun	-	-	bun	lang	winget
zig	zig.zig	zig	zig	zig	zig	-	zig	lang	winget
duckdb	DuckDB.cli	duckdb	duckdb	duckdb	duckdb	-	duckdb	cli	winget
typst	Typst.Typst	typst	typst	typst	typst	-	typst	cli	winget
lazydocker	JesseDuffield.Lazydocker	lazydocker	lazydocker	lazydocker	lazydocker	-	lazydocker	cli	winget
make	ezwinports.make	make	make	-	make	make	make	cli	winget
cloc	AlDanial.Cloc	cloc	cloc	-	cloc	cloc	cloc	cli	winget
wget	JernejSimoncic.Wget	wget	wget	-	wget	wget	wget	cli	winget
gcc	BrechtSanders.WinLibs.POSIX.UCRT	gcc	mingw	-	gcc	gcc	gcc	cli	winget
yt-dlp	yt-dlp.yt-dlp	yt-dlp	yt-dlp	-	yt-dlp	yt-dlp	yt-dlp	cli	winget
mpv	shinchiro.mpv	mpv	mpvio	-	mpv	mpv	mpv	gui	winget
handy	cjpais.Handy	-	-	-	-	-	handy	gui	winget
neovide	Neovide.Neovide	neovide	neovide	-	neovide	-	neovide	gui	winget
windows-terminal	Microsoft.WindowsTerminal	windows-terminal	microsoft-windows-terminal	-	-	-	wt	gui	winget
vscode	Microsoft.VisualStudioCode	vscode	vscode	-	-	-	code	gui	winget
7zip	7zip.7zip	7zip	7zip	-	7zip	7zip	7z	cli	winget
docker-desktop	Docker.DockerDesktop	-	docker-desktop	-	-	-	docker	gui	winget
r	RProject.R	r	r.project	-	r	r-base	R	lang	winget
rstudio	Posit.RStudio	rstudio	r.studio	-	-	-	rstudio	gui	winget
sqlcmd	Microsoft.Sqlcmd	sqlcmd	sqlcmd	-	-	-	sqlcmd	cli	winget
azure-cli	Microsoft.AzureCLI	azure-cli	azure-cli	-	azure-cli	azure-cli	az	cli	winget
powerbi	Microsoft.PowerBI	-	powerbi	-	-	-	-	gui	winget
dax-studio	DaxStudio.DaxStudio	-	daxstudio	-	-	-	-	gui	winget
```

*(check every id: they're from memory and today's `packages.ps1`; the bump job's sibling can lint them weekly with `winget show --id`, `scoop info`, `choco info`.)*

Anything not in either file is found by search across backends (`winget search`, `scoop search`, `choco search`, `mise registry`); one hit → used; several → listed, you pick (or `--from`).

### 6.3 Where things are kept

| File | What |
|---|---|
| `%LOCALAPPDATA%\launcher\packages.tsv` | what's installed: `name backend id scope version by time` (`by` = `omawin` or `found`) |
| `%LOCALAPPDATA%\launcher\pkg-prefer.txt` | your backend order, one per line (default `winget`, `scoop`, `choco`) |
| `%LOCALAPPDATA%\launcher\pkg-pins.tsv` | `name<TAB>backend`: per-package choice (`omawin pkg move` writes it) |
| `%LOCALAPPDATA%\launcher\installed.txt` | today's `winget:<id>` marks for the Packages page: kept, now written from `packages.tsv` |

### 6.4 `bin/pkg/Pkg.psm1`

```powershell
# omawin pkg: one package command over winget, Scoop, Chocolatey, mise, the bundle and pacman in
# Arch. A thin layer: it picks a backend, runs it, reports the backend's own error as is, and
# records what it did. Windows PowerShell 5.1.

$Repo = Split-Path (Split-Path $PSScriptRoot)
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$Record = Join-Path $State 'packages.tsv'
$PrefsFile = Join-Path $State 'pkg-prefer.txt'
$PinsFile = Join-Path $State 'pkg-pins.tsv'

# --- names -----------------------------------------------------------------------------------
$script:Names = $null
function Get-PkgNames {
    if ($script:Names) { return $script:Names }
    $t = @{}
    $cols = $null
    foreach ($l in [IO.File]::ReadAllLines("$Repo\packages\names.tsv")) {
        if ($l -match '^\s*#' -or -not $l.Trim()) { continue }
        $f = $l.Split("`t")
        if (-not $cols) { $cols = $f; continue }
        $row = @{}
        for ($i = 0; $i -lt $cols.Count; $i++) { $row[$cols[$i]] = if ($i -lt $f.Count -and $f[$i] -ne '-') { $f[$i] } else { $null } }
        $t[$row.name] = $row
    }
    # Bundled tools: bundle/manifest.json's ids, prefer = bundle.
    $m = Get-Content "$Repo\bundle\manifest.json" -Raw | ConvertFrom-Json
    foreach ($tool in $m.tools) {
        if (-not ($tool.PSObject.Properties.Name -contains 'ids')) { continue }
        $row = @{ name = $tool.name; prefer = 'bundle'; kind = 'cli'; bundle = $tool.name
                  command = $(if ($tool.PSObject.Properties.Name -contains 'commands') { $tool.commands[0] } else { $tool.name }) }
        foreach ($k in 'winget', 'scoop', 'choco', 'mise', 'arch', 'apt') { $row[$k] = if ($tool.ids.PSObject.Properties.Name -contains $k) { $tool.ids.$k } else { $null } }
        $t[$tool.name] = $row
    }
    $script:Names = $t
    $t
}

# --- backends --------------------------------------------------------------------------------
# Each: Present (is it on this PC), Install/Remove/Update (an id; return @{ Ok; Code; Text; Restart; Admin }),
# List (installed: objects with Id, Version), Search, NeedsAdmin (scope -> bool).
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    # Output collected (5.1 throws on a native program's stderr under Stop: Continue here).
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $saved }
    @{ Code = $LASTEXITCODE; Lines = @($out) }
}
function Get-Tail($Lines) {
    # The last meaningful line, without winget's progress bars (block characters) and spinners.
    @($Lines | Where-Object { $_.Trim() -and $_ -notmatch ('[{0}-{1}]' -f [char]0x2580, [char]0x259F) -and $_ -notmatch '^\s*[-\\|/]\s*$' }) | Select-Object -Last 1
}

# winget's exit codes that matter (HRESULTs as Int32). From winget-cli's
# doc/windows/package-manager/winget/returnCodes.md (check each against the current list).
$WingetCodes = @{
    0           = @{ Ok = $true;  Text = 'installed' }
    -1978335189 = @{ Ok = $true;  Text = 'already up to date' }                    # 0x8A15002B UPDATE_NOT_APPLICABLE
    -1978335135 = @{ Ok = $true;  Text = 'already installed' }                     # 0x8A150061 PACKAGE_ALREADY_INSTALLED
    -1978334963 = @{ Ok = $true;  Text = 'already installed' }                     # 0x8A15010D INSTALL_ALREADY_INSTALLED
    -1978334967 = @{ Ok = $true;  Text = 'installed; a restart finishes it'; Restart = $true }  # 0x8A150109 REBOOT_REQUIRED_TO_FINISH
    -1978334966 = @{ Ok = $false; Text = 'Windows must restart before this can install'; Restart = $true }  # 0x8A15010A REBOOT_REQUIRED_TO_INSTALL
    -1978335212 = @{ Ok = $false; Text = 'no package with that id' }               # 0x8A150014 NO_APPLICATIONS_FOUND
    -1978335216 = @{ Ok = $false; Text = 'no installer for this scope or architecture'; Scope = $true }  # 0x8A150010 NO_APPLICABLE_INSTALLER (check)
    -1978334975 = @{ Ok = $false; Text = 'the program is running: close it and try again'; Retry = $true }  # 0x8A150101 PACKAGE_IN_USE
    -1978334974 = @{ Ok = $false; Text = 'another install is running'; Retry = $true }  # 0x8A150102 INSTALL_IN_PROGRESS
    -1978334973 = @{ Ok = $false; Text = 'a file is in use'; Retry = $true }        # 0x8A150103 FILE_IN_USE
    -1978334972 = @{ Ok = $false; Text = 'a dependency is missing' }               # 0x8A150104 MISSING_DEPENDENCY
    -1978334971 = @{ Ok = $false; Text = 'the disk is full' }                      # 0x8A150105 DISK_FULL
    -1978334969 = @{ Ok = $false; Text = 'no network'; Retry = $true }             # 0x8A150107 NO_NETWORK
    -1978334964 = @{ Ok = $false; Text = 'cancelled' }                             # 0x8A15010C CANCELLED_BY_USER
    -1978334962 = @{ Ok = $false; Text = 'a newer version is already installed' }  # 0x8A15010E DOWNGRADE
    -1978334961 = @{ Ok = $false; Text = 'blocked by a policy on this PC' }        # 0x8A15010F BLOCKED_BY_POLICY
}
function ConvertFrom-WingetResult($r) {
    $known = $WingetCodes[[int]$r.Code]
    $res = @{ Ok = $false; Code = $r.Code; Text = ''; Restart = $false; Retry = $false; Scope = $false }
    if ($known) { foreach ($k in $known.Keys) { $res[$k] = $known[$k] } }
    else { $res.Text = "winget exit 0x{0:X8}: {1}" -f $r.Code, (Get-Tail $r.Lines) }
    $res
}

$Backends = [ordered]@{}

$Backends.winget = @{
    Present = { [bool](Get-Command winget -ErrorAction SilentlyContinue) }
    Install = { param($Id, $Scope)
        $a = @('install', '--id', $Id, '--exact', '--silent', '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
        if ($Scope) { $a += @('--scope', $Scope) }
        $res = ConvertFrom-WingetResult (Invoke-Native winget $a)
        # Asked for per user and the package has no per-user installer: said, not silently machine-wide.
        if ($res.Scope -and $Scope -eq 'user') { $res.Text = 'only a whole-PC installer: omawin pkg install --machine (one admin prompt)'; $res.Admin = $true }
        $res }
    Remove  = { param($Id) ConvertFrom-WingetResult (Invoke-Native winget @('uninstall', '--id', $Id, '--exact', '--silent', '--accept-source-agreements', '--disable-interactivity')) }
    Update  = { param($Id)
        $a = if ($Id) { @('upgrade', '--id', $Id, '--exact') } else { @('upgrade', '--all') }
        ConvertFrom-WingetResult (Invoke-Native winget ($a + @('--silent', '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity'))) }
    List    = {
        # One export (as Get-WingetInstalled, install/software.ps1:11), not a list per package.
        $f = Join-Path $env:TEMP "omawin-pkg-export-$PID.json"
        $null = Invoke-Native winget @('export', '--output', $f, '--include-versions', '--accept-source-agreements', '--disable-interactivity')
        if (Test-Path $f) {
            foreach ($s in (Get-Content $f -Raw | ConvertFrom-Json).Sources) { foreach ($p in $s.Packages) { [pscustomobject]@{ Id = $p.PackageIdentifier; Version = $p.Version } } }
            Remove-Item $f
        } }
    Search  = { param($Q) (Invoke-Native winget @('search', $Q, '--source', 'winget', '--accept-source-agreements', '--disable-interactivity')).Lines }
    NeedsAdmin = { param($Scope) $Scope -eq 'machine' }
}

$Backends.scoop = @{
    # Scoop is per user and needs no admin; it's a PowerShell script (scoop.ps1 / scoop.cmd shim).
    Present = { [bool](Get-Command scoop -ErrorAction SilentlyContinue) }
    Install = { param($Id) $r = Invoke-Native scoop @('install', $Id); @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Remove  = { param($Id) $r = Invoke-Native scoop @('uninstall', $Id); @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Update  = { param($Id) $r = if ($Id) { Invoke-Native scoop @('update', $Id) } else { Invoke-Native scoop @('update', '*') }; @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    List    = {
        # `scoop export` prints JSON with apps[] (Name, Version, Source) in current Scoop (check).
        $j = (Invoke-Native scoop @('export')).Lines -join "`n"
        try { foreach ($a in ($j | ConvertFrom-Json).apps) { [pscustomobject]@{ Id = $a.Name; Version = $a.Version } } } catch { } }
    Search  = { param($Q) (Invoke-Native scoop @('search', $Q)).Lines }
    NeedsAdmin = { param($Scope) $Scope -eq 'machine' }     # scoop install --global needs admin
}

$Backends.choco = @{
    # Chocolatey installs machine-wide: always admin, so always through the one admin batch.
    Present = { [bool](Get-Command choco -ErrorAction SilentlyContinue) }
    Install = { param($Id) @{ Ok = $false; Admin = $true; AdminVerb = "choco-install $Id"; Text = 'Chocolatey installs for the whole PC: one admin prompt' } }
    Remove  = { param($Id) @{ Ok = $false; Admin = $true; AdminVerb = "choco-uninstall $Id"; Text = 'one admin prompt' } }
    Update  = { param($Id) @{ Ok = $false; Admin = $true; AdminVerb = $(if ($Id) { "choco-upgrade $Id" } else { 'choco-upgrade all' }); Text = 'one admin prompt' } }
    List    = {
        # Chocolatey 2: `choco list` is local only; -r gives "name|version".
        foreach ($l in (Invoke-Native choco @('list', '-r')).Lines) { $p = $l.Split('|'); if ($p.Count -eq 2) { [pscustomobject]@{ Id = $p[0]; Version = $p[1] } } } }
    Search  = { param($Q) (Invoke-Native choco @('search', $Q, '-r')).Lines }
    NeedsAdmin = { param($Scope) $true }
}

$Backends.mise = @{
    Present = { [bool](Get-Command mise -ErrorAction SilentlyContinue) }
    # `mise use --global <tool>@latest`: installs and pins in ~/.config/mise/config.toml.
    Install = { param($Id) $r = Invoke-Native mise @('use', '--global', "$Id@latest"); @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Remove  = { param($Id)
        $r = Invoke-Native mise @('unuse', '--global', $Id)          # (check: `mise unuse` exists in the pinned mise)
        @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Update  = { param($Id) $r = if ($Id) { Invoke-Native mise @('upgrade', $Id) } else { Invoke-Native mise @('upgrade') }; @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    List    = {
        $j = (Invoke-Native mise @('ls', '--json', '--global')).Lines -join "`n"
        try { $o = $j | ConvertFrom-Json; foreach ($p in $o.PSObject.Properties) { foreach ($v in @($p.Value)) { [pscustomobject]@{ Id = $p.Name; Version = $v.version } } } } catch { } }
    Search  = { param($Q) (Invoke-Native mise @('registry')).Lines | Where-Object { $_ -like "*$Q*" } }
    NeedsAdmin = { param($Scope) $false }
}

$Backends.bundle = @{
    # The omawin bundle's copies: installed by being in the version folder and linked into
    # %LOCALAPPDATA%\omawin\tools; "removing" one takes its link away (the copy stays, so moving
    # back is instant). Updates come with omawin update.
    Present = { Test-Path (Join-Path $env:LOCALAPPDATA 'omawin\current\files.tsv') }
    Install = { param($Id) $r = Invoke-Native powershell.exe @('-NoProfile', '-File', "$Repo\bin\bundle.ps1", '-Link', $Id); @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Remove  = { param($Id) $r = Invoke-Native powershell.exe @('-NoProfile', '-File', "$Repo\bin\bundle.ps1", '-Unlink', $Id); @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines) } }
    Update  = { param($Id) @{ Ok = $true; Text = 'the bundle updates with omawin update' } }
    List    = {
        $m = Get-Content "$Repo\bundle\manifest.json" -Raw | ConvertFrom-Json
        $linked = Join-Path $env:LOCALAPPDATA 'omawin\tools'
        foreach ($t in $m.tools | Where-Object { $_.PSObject.Properties.Name -contains 'commands' }) {
            $exe = Join-Path $linked "$($t.commands[0]).exe"
            $dirTool = $t.PSObject.Properties.Name -contains 'path'
            if ((Test-Path $exe) -or $dirTool) { [pscustomobject]@{ Id = $t.name; Version = $t.version } }
        } }
    Search  = { param($Q) @() }
    NeedsAdmin = { param($Scope) $false }
}

$Backends.pacman = @{
    # Linux packages in the Linux omawin opens (omawin default linux): pacman in Arch (Omarchy's
    # repositories, no AUR), apt in Ubuntu. sudo inside WSL is the distro's own business, not
    # Windows admin: the default WSL user has passwordless sudo after wsl/setup.sh (check).
    Present = { [bool](Get-WslDistroName) }
    Install = { param($Id) Invoke-Linux "pkg-install $Id" }
    Remove  = { param($Id) Invoke-Linux "pkg-remove $Id" }
    Update  = { param($Id) Invoke-Linux $(if ($Id) { "pkg-update $Id" } else { 'pkg-update' }) }
    List    = { foreach ($l in (Invoke-Linux 'pkg-list').Lines) { $p = $l -split '\s+'; if ($p.Count -ge 2) { [pscustomobject]@{ Id = $p[0]; Version = $p[1] } } } }
    Search  = { param($Q) (Invoke-Linux "pkg-search $Q").Lines }
    NeedsAdmin = { param($Scope) $false }
}
function Get-WslDistroName {
    # The pick (bin/defaults.txt + %LOCALAPPDATA%\launcher\defaults\linux), if it's installed (HKCU Lxss).
    . "$Repo\install\lib-linux.ps1"    # Get-LinuxPick, Get-WslDistros moved out of install/lib.ps1 so pkg can load them without ui.ps1 (check)
    $pick = Get-LinuxPick
    if ($pick -and (@(Get-WslDistros) -contains $pick.Distro)) { $pick.Distro }
}
function Invoke-Linux([string]$Command) {
    # One script inside Linux (wsl/omawin-pkg.sh) maps pkg-install/... onto pacman or apt, so the
    # quoting stays in one place: pacman -S --needed --noconfirm / apt-get install -y.
    $d = Get-WslDistroName
    $r = Invoke-Native wsl.exe @('-d', $d, '--', 'bash', '-lc', "~/.local/share/omawin/omawin-pkg.sh $Command")
    @{ Ok = ($r.Code -eq 0); Code = $r.Code; Text = (Get-Tail $r.Lines); Lines = $r.Lines }
}

# --- choosing a backend ----------------------------------------------------------------------
function Get-PkgPrefer {
    $p = @(Get-Content $PrefsFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($p) { $p } else { @('winget', 'scoop', 'choco') }
}
function Get-PkgPin([string]$Name) {
    foreach ($l in Get-Content $PinsFile -ErrorAction SilentlyContinue) { $f = $l.Split("`t"); if ($f[0] -eq $Name) { return $f[1] } }
}
function Get-BackendId([string]$Name, [string]$Backend) {
    $row = (Get-PkgNames)[$Name]
    if (-not $row) { return $null }
    $col = switch ($Backend) { 'pacman' { 'arch' } 'bundle' { 'bundle' } default { $Backend } }
    $row[$col]
}
# --from; a pin; omawin's default for the name; your order; the first backend that has it.
function Resolve-PkgBackend([string]$Name, [string]$From, [switch]$Linux) {
    if ($Linux) { return 'pacman' }
    $order = @()
    if ($From) { $order += $From }
    else {
        $pin = Get-PkgPin $Name; if ($pin) { $order += $pin }
        $row = (Get-PkgNames)[$Name]; if ($row -and $row.prefer) { $order += $row.prefer }
        $order += Get-PkgPrefer
        $order += @('winget', 'scoop', 'choco', 'mise')
    }
    foreach ($b in $order | Select-Object -Unique) {
        if (-not $Backends.Contains($b)) { throw "no backend '$b' (winget, scoop, choco, mise, bundle)" }
        if (-not (& $Backends[$b].Present)) { if ($From) { throw "$b isn't on this PC (omawin pkg install $b)" }; continue }
        if ((Get-PkgNames).ContainsKey($Name)) { if (Get-BackendId $Name $b) { return $b } else { continue } }
        return $b    # not a known name: the caller searches this backend
    }
    throw "no backend on this PC has $Name"
}

# --- the record -------------------------------------------------------------------------------
function Read-PkgRecord {
    if (-not (Test-Path $Record)) { return @() }
    foreach ($l in [IO.File]::ReadAllLines($Record)) {
        if ($l.StartsWith('#')) { continue }
        $f = $l.Split("`t")
        [pscustomobject]@{ Name = $f[0]; Backend = $f[1]; Id = $f[2]; Scope = $f[3]; Version = $f[4]; By = $f[5]; Time = $f[6] }
    }
}
function Write-PkgRecord($Rows) {
    $lines = @('# omawin pkg: name, backend, backend id, scope, version, installed by (omawin|found), when') +
        @($Rows | Sort-Object Name, Backend | ForEach-Object { ($_.Name, $_.Backend, $_.Id, $_.Scope, $_.Version, $_.By, $_.Time) -join "`t" })
    $tmp = "$Record.tmp"
    [IO.File]::WriteAllLines($tmp, [string[]]$lines)
    if (Test-Path $Record) { [IO.File]::Replace($tmp, $Record, $null) } else { [IO.File]::Move($tmp, $Record) }
    # The Packages page's marks (today's installed.txt format), from the same data.
    [IO.File]::WriteAllLines((Join-Path $State 'installed.txt'), [string[]]@($Rows | ForEach-Object { "$($_.Backend):$($_.Id.ToLower())" }))
}
function Set-PkgRecordRow([string]$Name, [string]$Backend, [string]$Id, [string]$Scope, [string]$Version, [string]$By) {
    $rows = @(Read-PkgRecord | Where-Object { -not ($_.Name -eq $Name -and $_.Backend -eq $Backend) })
    $rows += [pscustomobject]@{ Name = $Name; Backend = $Backend; Id = $Id; Scope = $Scope; Version = "$Version"; By = $By; Time = (Get-Date).ToString('s') }
    Write-PkgRecord $rows
}
function Remove-PkgRecordRow([string]$Name, [string]$Backend) {
    Write-PkgRecord @(Read-PkgRecord | Where-Object { -not ($_.Name -eq $Name -and $_.Backend -eq $Backend) })
}

# --- admin: collected, one prompt (section 7.4) ------------------------------------------------
$script:AdminVerbs = New-Object Collections.Generic.List[string]

# --- the commands -----------------------------------------------------------------------------
function Install-Pkg([string[]]$Names, [string]$From, [switch]$Linux, [ValidateSet('', 'user', 'machine')][string]$Scope = '', [switch]$Quiet) {
    $failed = 0
    foreach ($name in $Names) {
        $b = Resolve-PkgBackend $name $From -Linux:$Linux
        $id = Get-BackendId $name $b
        if (-not $id) { $id = Select-PkgFromSearch $name $b -Quiet:$Quiet; if (-not $id) { $failed++; continue } }
        # Per user unless asked: winget picks machine for some installers by default.
        $s = if ($Scope) { $Scope } elseif ($b -eq 'winget') { 'user' } else { '' }
        if (& $Backends[$b].NeedsAdmin $s) {
            $script:AdminVerbs.Add($(if ($b -eq 'choco') { "choco-install $id" } else { "winget-machine $id" }))
            Write-Host "  $name ($b $id): waits for the one admin prompt"
            continue
        }
        Write-Host "  installing $name ($b $id)"
        $r = & $Backends[$b].Install $id $s
        if ($r.Admin -and $r.AdminVerb) { $script:AdminVerbs.Add($r.AdminVerb); continue }
        if ($r.Ok) { Set-PkgRecordRow $name $b $id $(if ($s) { $s } else { 'user' }) '' 'omawin'; Write-Host "  ok  $name  $($r.Text)" }
        else { $failed++; Write-Host "  failed  $name ($b): $($r.Text)" -ForegroundColor Red }   # the backend's own words, as is
    }
    Complete-PkgAdmin -Quiet:$Quiet
    $failed
}

function Remove-Pkg([string[]]$Names) {
    $failed = 0
    foreach ($name in $Names) {
        $rows = @(Read-PkgRecord | Where-Object Name -eq $name)
        if (-not $rows) { Write-Host "  ${name}: not installed (omawin pkg list)"; $failed++; continue }
        foreach ($row in $rows) {
            $r = & $Backends[$row.Backend].Remove $row.Id
            if ($r.Admin) { $script:AdminVerbs.Add($r.AdminVerb); continue }
            if ($r.Ok) { Remove-PkgRecordRow $name $row.Backend; Write-Host "  removed $name ($($row.Backend))" }
            else { $failed++; Write-Host "  failed  $name ($($row.Backend)): $($r.Text)" -ForegroundColor Red }
        }
    }
    Complete-PkgAdmin
    $failed
}

function Update-Pkg([string]$Name) {
    if ($Name) {
        foreach ($row in @(Read-PkgRecord | Where-Object Name -eq $Name)) {
            $r = & $Backends[$row.Backend].Update $row.Id
            if ($r.Admin) { $script:AdminVerbs.Add($r.AdminVerb) } else { Write-Host "  $Name ($($row.Backend)): $($r.Text)" }
        }
    } else {
        # Everything, each through its own backend (Update > All apps, roadmap 5.9).
        foreach ($b in $Backends.Keys) {
            if (-not (& $Backends[$b].Present)) { continue }
            Write-Host "  $b ..."
            $r = & $Backends[$b].Update $null
            if ($r.Admin) { $script:AdminVerbs.Add($r.AdminVerb) } else { Write-Host "    $($r.Text)" }
        }
    }
    # Updates never prompt (2.9): what needs admin is written down for later.
    Complete-PkgAdmin -Defer
    Sync-PkgList | Out-Null
}

# Refreshes packages.tsv from each backend's own list, so what was installed outside omawin shows
# too and nothing drifts. Rows omawin installed keep their name; others are named by their id
# (or by names.tsv, reverse-mapped).
function Sync-PkgList {
    $names = Get-PkgNames
    $byId = @{}
    foreach ($n in $names.Values) { foreach ($b in 'winget', 'scoop', 'choco', 'mise', 'bundle') { $id = Get-BackendId $n.name $b; if ($id) { $byId["$b|$($id.ToLower())"] = $n.name } } }
    $old = @{}; foreach ($r in Read-PkgRecord) { $old["$($r.Backend)|$($r.Id.ToLower())"] = $r }
    $rows = @()
    foreach ($b in $Backends.Keys) {
        if ($b -eq 'pacman' -or -not (& $Backends[$b].Present)) { continue }
        foreach ($p in @(& $Backends[$b].List)) {
            $k = "$b|$($p.Id.ToLower())"
            $prev = $old[$k]
            $name = if ($prev) { $prev.Name } elseif ($byId[$k]) { $byId[$k] } else { $p.Id }
            $rows += [pscustomobject]@{ Name = $name; Backend = $b; Id = $p.Id; Scope = $(if ($prev) { $prev.Scope } else { '' }); Version = "$($p.Version)"
                                        By = $(if ($prev) { $prev.By } else { 'found' }); Time = $(if ($prev) { $prev.Time } else { (Get-Date).ToString('s') }) }
        }
    }
    Write-PkgRecord $rows
    $rows
}

# Which backends have it, at what versions, and which copy runs now (PATH order).
function Get-PkgInfo([string]$Name) {
    $row = (Get-PkgNames)[$Name]
    foreach ($b in $Backends.Keys) {
        $id = Get-BackendId $Name $b
        $here = @(Read-PkgRecord | Where-Object { $_.Name -eq $Name -and $_.Backend -eq $b })
        [pscustomobject]@{ Backend = $b; Id = $id; Installed = [bool]$here; Version = $(if ($here) { $here[0].Version }) }
    }
    if ($row -and $row.command) {
        $all = @(Get-Command $row.command -All -CommandType Application -ErrorAction SilentlyContinue)
        if ($all) {
            "runs: $($all[0].Source)"
            foreach ($o in $all | Select-Object -Skip 1) { "shadowed: $($o.Source)" }
        }
    }
}

# Moves a package to another backend: installs there, then takes the old copy away (the bundle's
# is unlinked, never deleted) and pins the choice.
function Move-Pkg([string]$Name, [string]$To) {
    $from = @(Read-PkgRecord | Where-Object Name -eq $Name | Where-Object Backend -ne $To)
    if ((Install-Pkg @($Name) -From $To) -ne 0) { throw "couldn't install $Name with $To; nothing else changed" }
    foreach ($row in $from) {
        $r = & $Backends[$row.Backend].Remove $row.Id
        if ($r.Ok) { Remove-PkgRecordRow $Name $row.Backend } else { Write-Host "  the $($row.Backend) copy stays: $($r.Text)" }
    }
    $pins = @(Get-Content $PinsFile -ErrorAction SilentlyContinue | Where-Object { -not $_.StartsWith("$Name`t") }) + "$Name`t$To"
    [IO.File]::WriteAllLines($PinsFile, [string[]]$pins)
    Complete-PkgAdmin
}

function Set-PkgPrefer([string[]]$Order) {
    foreach ($b in $Order) { if (-not $Backends.Contains($b) -or $b -in 'bundle', 'pacman') { throw "not a backend to prefer: $b (winget, scoop, choco, mise)" } }
    [IO.File]::WriteAllLines($PrefsFile, [string[]]$Order)
    if ($Order -contains 'choco') { Write-Host '  Chocolatey installs for the whole PC: each install asks for admin once (batched)' }
}

# Hands collected admin verbs to the one admin batch (section 7.4), or, for updates and the
# background queue, writes them to admin-pending.txt for later. Never prompts when -Defer or
# OMAWIN_NO_ADMIN is set.
function Complete-PkgAdmin([switch]$Defer, [switch]$Quiet) {
    if (-not $script:AdminVerbs.Count) { return }
    $verbs = @($script:AdminVerbs | Select-Object -Unique)
    $script:AdminVerbs.Clear()
    if ($Defer -or $Quiet -or $env:OMAWIN_NO_ADMIN) {
        Add-Content (Join-Path $State 'admin-pending.txt') $verbs
        Write-Host "  $($verbs.Count) change(s) need admin: waiting for you (omawin admin)"
        return
    }
    & "$Repo\bin\admin.ps1" -Verbs $verbs     # preview, one prompt, summary
}

function Select-PkgFromSearch([string]$Name, [string]$Backend, [switch]$Quiet) {
    $hits = @(& $Backends[$Backend].Search $Name)
    if ($Quiet) { Write-Host "  $Name isn't a name omawin knows; use omawin pkg install $Name --from <backend> with its id"; return $null }
    # Interactive: fzf over the backend's search lines (as bin/pkg-install.ps1 does today).
    $pick = $hits | fzf --prompt "$Backend > " --header "pick $Name"
    if ($pick) { ($pick -split '\s{2,}')[1] } else { $null }   # (check: column parsing per backend)
}

Export-ModuleMember -Function Install-Pkg, Remove-Pkg, Update-Pkg, Sync-PkgList, Get-PkgInfo, Move-Pkg, Set-PkgPrefer, Get-PkgNames, Read-PkgRecord
```

### 6.5 `bin/pkg.ps1`: the command

```powershell
<#
.SYNOPSIS
    omawin pkg: one package command over winget, Scoop, Chocolatey, mise, the bundle and Arch's pacman.
.DESCRIPTION
        omawin pkg install <name...> [--from winget|scoop|choco|mise] [--linux] [--machine|--user]
        omawin pkg remove <name...>
        omawin pkg update [name]
        omawin pkg list [--refresh]
        omawin pkg info <name>
        omawin pkg move <name> --to <backend>
        omawin pkg prefer <backend...>
        omawin pkg preset <name>            (the queue's verb: a preset's packages)
    With no name, install and remove open today's fuzzy pickers (bin/pkg-install.ps1, pkg-remove.ps1).
    Windows PowerShell 5.1 and 7.
#>
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
Import-Module "$PSScriptRoot\pkg\Pkg.psm1" -Force
$verb = $args[0]
$rest = @($args | Select-Object -Skip 1)
# GNU-style flags (omawin's commands take them that way: `omawin pkg install go --from mise`).
$from = $null; $to = $null; $linux = $false; $scope = ''; $quiet = $false; $refresh = $false; $names = @()
for ($i = 0; $i -lt $rest.Count; $i++) {
    switch ($rest[$i]) {
        '--from'    { $from = $rest[++$i] }
        '--to'      { $to = $rest[++$i] }
        '--linux'   { $linux = $true }
        '--machine' { $scope = 'machine' }
        '--user'    { $scope = 'user' }
        '--quiet'   { $quiet = $true }
        '--refresh' { $refresh = $true }
        default     { $names += $rest[$i] }
    }
}
switch ($verb) {
    'install' {
        if (-not $names) { & "$PSScriptRoot\pkg-install.ps1"; return }
        $failed = Install-Pkg $names -From $from -Linux:$linux -Scope $scope -Quiet:$quiet
        if ($failed) { exit 1 }
    }
    'remove'  { if (-not $names) { & "$PSScriptRoot\pkg-remove.ps1"; return }; if (Remove-Pkg $names) { exit 1 } }
    'update'  { Update-Pkg ($names | Select-Object -First 1) }
    'list'    {
        $rows = if ($refresh -or -not (Test-Path "$env:LOCALAPPDATA\launcher\packages.tsv")) { Sync-PkgList } else { Read-PkgRecord }
        $rows | Sort-Object Name | Format-Table Name, Backend, Id, Version, By -AutoSize
    }
    'info'    { Get-PkgInfo $names[0] | Format-Table -AutoSize }
    'move'    { if (-not $to) { throw 'omawin pkg move <name> --to <backend>' }; Move-Pkg $names[0] $to }
    'prefer'  { if ($names) { Set-PkgPrefer $names } else { Get-Content "$env:LOCALAPPDATA\launcher\pkg-prefer.txt" -ErrorAction SilentlyContinue } }
    'preset'  {
        . "$Repo\packages.ps1"
        $p = $Presets[$names[0]]
        if (-not $p) { throw "no preset $($names[0]) (omawin preset list)" }
        $failed = Install-Pkg @($p.Packages) -Quiet:$quiet
        foreach ($x in @($p.Extras)) { & $ExtraInstalls[$x] }     # herdr, claude, ... (their own installers, as today)
        if ($failed) { exit 1 }
    }
    default   { Get-Help $PSCommandPath -Detailed }
}
```

`bin/commands.txt` (the existing `pkg install` / `pkg remove` rows point at `bin/pkg.ps1` instead; `pkg find` and `pkg index` stay):

```
pkg install | Install by name with whichever manager omawin prefers (or --from winget|scoop|choco|mise, --linux); no name: the fuzzy picker | pwsh bin/pkg.ps1 install
pkg remove | Remove through the manager that installed it; no name: the fuzzy picker | pwsh bin/pkg.ps1 remove
pkg update | Update one package, or everything, each through its own manager (admin ones wait for one prompt) | pwsh bin/pkg.ps1 update
pkg list | Everything installed, by any manager, with version and source (--refresh asks each manager) | pwsh bin/pkg.ps1 list
pkg info | Which managers have a package, which copy runs now, which are shadowed | pwsh bin/pkg.ps1 info
pkg move | Move a package to another manager: omawin pkg move <name> --to winget|scoop|choco|mise|bundle | pwsh bin/pkg.ps1 move
pkg prefer | Your order of package managers: omawin pkg prefer scoop winget | pwsh bin/pkg.ps1 prefer
```

`pwsh` actions need PowerShell 7 on PATH; on a bundle install before stage 2 lands, `omawin.exe` runs `pwsh` actions with `powershell.exe` when `pwsh` isn't found *(check: every script these rows run is 5.1-safe; `bin/pkg*.ps1` must be)*.

### 6.6 The bundle backend's links: `bin/bundle.ps1`

`%LOCALAPPDATA%\omawin\tools` holds a hard link per bundled single-exe tool (into `current\tools`); `-Unlink rg` removes the link (the copy stays in the version folder), `-Link rg` puts it back, `-Relink` rebuilds them all after an update (skipping the unlinked ones, kept in `%LOCALAPPDATA%\omawin\unlinked.txt`). Folder tools (Git, Neovim, PowerShell) are unlinked by taking their PATH entry out (`Set-OmawinPath` reads `unlinked.txt` and leaves those entries out). `-Stage 2` is the queue's stage 2 (download, verify, unpack into the current version folder, `-Relink`).

```powershell
function Set-ToolLinks([string]$Root) {
    $tools = Join-Path $Root 'tools'
    $from = Join-Path $Root 'current\tools'
    $off = @(Get-Content (Join-Path $Root 'unlinked.txt') -ErrorAction SilentlyContinue)
    [void][IO.Directory]::CreateDirectory($tools)
    # Rebuilt each time: links into an old version folder go, the new ones come.
    foreach ($f in Get-ChildItem $tools -File) { try { $f.Delete() } catch { } }   # in use: the next relink retries
    foreach ($exe in Get-ChildItem $from -Filter *.exe) {
        if ($off -contains $exe.BaseName) { continue }
        $dest = Join-Path $tools $exe.Name
        if (-not (Test-Path $dest)) { [void][Omawin.Fs]::CreateHardLinkW($dest, $exe.FullName, [IntPtr]::Zero) }
    }
}
```

*(check)* a running tool's hard link can't be deleted (`rg` running in a terminal during an update): the old link stays until the next relink, which is harmless (same tool, older version).

### 6.7 Admin through pkg

`--machine` (winget), `scoop install --global` and every Chocolatey operation need admin. `omawin pkg` never asks per package: `Complete-PkgAdmin` hands the collected verbs to `bin/admin.ps1` (section 7.4: preview, one prompt, summary), or, in `update` and the queue, writes them to `admin-pending.txt`. `omawin pkg prefer choco` says up front that Chocolatey means a prompt per batch.

### 6.8 Presets through pkg

`packages.ps1`'s presets become lists of omawin names (`Packages`), with `Extras` unchanged:

```powershell
$Presets = [ordered]@{
    'python'     = @{ Summary = 'Python 3.13'; Packages = @('python'); Tools = @('python') }
    'node'       = @{ Summary = 'pnpm (Node.js itself is in the core)'; Packages = @('pnpm'); Tools = @('pnpm') }
    'go'         = @{ Summary = 'Go'; Packages = @('go'); Tools = @('go') }
    'elixir'     = @{ Summary = "Erlang and Elixir (Neovim's IEx helpers)"; Packages = @('erlang', 'elixir'); Tools = @('elixir') }
    'data'       = @{ Summary = 'DuckDB, harlequin, sqlcmd'; Packages = @('duckdb', 'sqlcmd'); Extras = @('harlequin'); Tools = @('duckdb', 'harlequin') }
    'cli-extras' = @{ Summary = 'make, cloc, wget'; Packages = @('make', 'cloc', 'wget'); Tools = @('make', 'cloc', 'wget') }
    'music'      = @{ Summary = "yt-dlp and mpv, for the bar's music player"; Packages = @('yt-dlp', 'mpv'); Tools = @('yt-dlp', 'mpv') }
    # ... the rest, the same way
}
```

Languages default to mise (`prefer` column), so `omawin preset add go` puts Go in through mise unless you said otherwise; `omawin pkg prefer winget` first, or `omawin pkg move go --to winget`, changes that. During the transition `install/software.ps1`'s `Get-WantedPackages` maps names back to winget ids through `names.tsv` (`Get-BackendId $n 'winget'`) so `install.ps1` on a git checkout keeps working until it calls `bin/pkg.ps1 preset` itself. `$WholePC` and `$WingetOverrides` move into `names.tsv`-adjacent data: `packages/winget-overrides.tsv` (`id<TAB>override`), read by the winget backend.

### 6.9 Tests (CI)

`.github/workflows/pkg.yml` on `windows-latest`, as a standard user (the same helper as section 8.1):
- `omawin pkg install jq --from winget` → `packages.tsv` has `jq winget jqlang.jq`; `remove` takes it out; exit codes;
- Scoop installed per user (`irm get.scoop.sh | iex` with `-RunAsAdmin` absent) then `install fd --from scoop`, `move fd --to winget`, `info fd` says which runs;
- `list --refresh` matches `winget export` and `scoop export` counts;
- Chocolatey in a separate admin job (it's admin by nature): `install` with `--quiet` writes `admin-pending.txt` and doesn't prompt.

Logic with real branches gets Pester tests with the backends stubbed at the `Invoke-Native` seam (`Resolve-PkgBackend`'s order, `ConvertFrom-WingetResult`, `Sync-PkgList`'s naming). Per the omarchy test rules: no tests for the names table itself.

---

## 7. No admin (2.9)

### 7.1 The per-user GlazeWM: two builds of the fork

**No Rust change is needed.** On the fork's `omawin` branch, `packages/wm/build.rs` already writes the manifest's `uiAccess` from a Cargo feature, off by default:

```rust
// packages/wm/build.rs (origin/omawin, as it is)
let ui_access = {
    #[cfg(feature = "ui_access")]
    { "true" }
    #[cfg(not(feature = "ui_access"))]
    { "false" }
};
// ... <requestedExecutionLevel level="asInvoker" uiAccess="{ui_access}" /> ...
```

and `packages/wm/Cargo.toml` declares `[features] ui_access = []`. Today `scripts/omawin-release.ps1` always builds `cargo build --release -p wm --features ui_access`. A build without the feature is the per-user GlazeWM: it starts from anywhere (no signature, no Program Files), and focus changes still work because `NativeWindow::focus` (`packages/wm-platform/src/platform_impl/windows/native_window.rs:285`) sends an input to its own process before `SetForegroundWindow` (the foreground-lock workaround). What it can't do: move or focus windows of programs running elevated (UIPI). That's the one loss, and the opt-in fixes it (7.4).

`scripts/omawin-release.ps1` in the fork builds both and names them apart:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Base = '3.10.1',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [switch]$NoPublish
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$target = @{ x64 = 'x86_64-pc-windows-msvc'; arm64 = 'aarch64-pc-windows-msvc' }[$Arch]
Push-Location $root
try {
    $env:VERSION_NUMBER = $Base
    $env:RUSTFLAGS = "--remap-path-prefix=$HOME=~ --remap-path-prefix=$root=glazewm"
    $out = Join-Path $root "target\omawin\$Version"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null

    # The watcher and the CLI are the same in both: no manifest feature of theirs.
    cargo build --release --locked --target $target -p wm-cli -p wm-watcher
    if ($LASTEXITCODE -ne 0) { throw 'cargo build (cli, watcher) failed' }

    # Two builds of the window manager: "user" (uiAccess=false: runs per user from
    # %LOCALAPPDATA%, no admin, omawin's default) and "uiaccess" (uiAccess=true: needs a trusted
    # signature and Program Files; the opt-in "manage admin windows too").
    foreach ($variant in 'user', 'uiaccess') {
        $features = if ($variant -eq 'uiaccess') { @('--features', 'ui_access') } else { @() }
        # build.rs only re-runs when its inputs change: the feature flip must rebuild wm.
        cargo build --release --locked --target $target -p wm @features
        if ($LASTEXITCODE -ne 0) { throw "cargo build ($variant) failed" }
        $stage = Join-Path $out "stage-$variant"
        New-Item -ItemType Directory -Force "$stage\cli" | Out-Null
        $bin = Join-Path $root "target\$target\release"
        Copy-Item "$bin\glazewm.exe" $stage
        Copy-Item "$bin\glazewm-watcher.exe" $stage
        Copy-Item "$bin\glazewm-cli.exe" "$stage\cli\glazewm.exe"
        $zipName = "glazewm-$Version-windows-$Arch-$variant.zip"
        Compress-Archive -Path "$stage\*" -DestinationPath (Join-Path $out $zipName)
    }
    # One sums file for both (install/glazewm-fork.ps1 reads SHA256SUMS-windows-<arch>.txt).
    $lines = Get-ChildItem $out -Filter '*.zip' | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
    Set-Content (Join-Path $out "SHA256SUMS-windows-$Arch.txt") $lines
    if ($NoPublish) { return $out }
    # ... gh release create as today, with both zips and the sums file
} finally { Pop-Location }
```

*(check)* whether Cargo rebuilds `wm` when only the feature set changes (it should: features are part of the fingerprint, and `build.rs` sees `CARGO_FEATURE_UI_ACCESS`); if not, `cargo clean -p wm` between the two.

Old releases' zip name (`glazewm-<v>-windows-x64.zip`) stays published for today's `install/glazewm-fork.ps1` until a migration moves PCs over.

The fork's CI, `.github/workflows/omawin-release.yml` (new in the fork; today the release is made by hand from a PC):

```yaml
name: omawin release
on:
  push:
    tags: ['v*-omawin.*']
permissions:
  contents: write
jobs:
  build:
    strategy:
      matrix:
        arch: [x64, arm64]
    runs-on: windows-latest          # cross-compiles arm64 (rustup target add) (check: the tray-icon vendor crate builds for aarch64)
    steps:
      - uses: actions/checkout@<sha> # v4  (pin to the same SHA omawin uses)
      - run: rustup target add ${{ matrix.arch == 'arm64' && 'aarch64-pc-windows-msvc' || 'x86_64-pc-windows-msvc' }}
      - shell: pwsh
        run: |
          $v = '${{ github.ref_name }}'.TrimStart('v')
          $base = ($v -split '-omawin')[0]
          .\scripts\omawin-release.ps1 -Version $v -Base $base -Arch ${{ matrix.arch }} -NoPublish
      # The uiaccess build signed in CI (Trusted Signing, as omawin's release.yml), so a PC never
      # makes and trusts its own certificate again. The user build is signed too (SmartScreen).
      - uses: azure/trusted-signing-action@<sha> # v0
        if: vars.CODESIGN == 'true'
        with:
          files-folder: target/omawin
          files-folder-recurse: true
          files-folder-filter: exe
          # (endpoint, account, profile, Azure ids: as omawin's release.yml)
      - shell: pwsh
        run: |
          $v = '${{ github.ref_name }}'.TrimStart('v')
          .\scripts\omawin-release.ps1 -Version $v -Arch ${{ matrix.arch }} -RepackOnly   # zip the signed exes, rewrite sums (check: add -RepackOnly)
      - uses: actions/upload-artifact@<sha> # v4
        with: { name: glazewm-${{ matrix.arch }}, path: target/omawin }
  publish:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@<sha> # v4
        with: { path: dist, merge-multiple: true }
      - env: { GH_TOKEN: '${{ github.token }}' }
        run: gh release create '${{ github.ref_name }}' --repo '${{ github.repository }}' --title 'GlazeWM ${{ github.ref_name }}' --notes 'omawin build: user (no uiAccess) and uiaccess (signed) variants' $(find dist -name '*.zip' -o -name 'SHA256SUMS-*.txt')
```

(Signing before zipping means the build step runs twice in the sketch; the cleaner shape is: build → upload exes → sign job → zip job. Either works.)

### 7.2 `omawin.exe` finds the right GlazeWM (`bin/omawin.cs`, C# 5)

`StartGlazeWM` (`bin/omawin.cs:719`) today looks only in `Program Files\glzr.io\GlazeWM` and treats "signed by `CN=omawin code signing`" as "our fork". New order: an opted-in `uiAccess` build in Program Files, else the per-user build in the current version folder, else the official one (git installs that haven't moved).

```csharp
// Which GlazeWM to start: the uiAccess build you opted into (Program Files, CI-signed), else the
// bundle's per-user build, else an official install. Fork: whether it can start before Explorer's
// taskbar exists (all omawin builds can; only the official one has to wait).
static string FindGlazeWM(out bool fork)
{
    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var uiAccess = Path.Combine(pf, @"omawin\glazewm\glazewm.exe");
    if (File.Exists(uiAccess) && SignedByOmawin(uiAccess)) { fork = true; return uiAccess; }
    var perUser = Path.Combine(local, @"omawin\current\glazewm\glazewm.exe");
    if (File.Exists(perUser)) { fork = true; return perUser; }
    var official = Path.Combine(pf, @"glzr.io\GlazeWM\glazewm.exe");
    fork = false;
    try { fork = X509Certificate.CreateFromSignedFile(official).Subject.StartsWith("CN=omawin code signing"); }
    catch (Exception) { }
    return File.Exists(official) ? official : null;
}

// The CI signature (release-signing.md): the publisher omawin's Trusted Signing profile issues
// (check: the exact subject once the certificate profile exists). Only a valid chain counts:
// WinVerifyTrust, through the managed X509Chain here as a first cut (check: revocation offline).
static bool SignedByOmawin(string file)
{
    try
    {
        var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
        if (cert.Subject.IndexOf("O=omawin", StringComparison.OrdinalIgnoreCase) < 0) return false;
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(cert);
    }
    catch (Exception) { return false; }
}
```

`StartGlazeWM` uses `FindGlazeWM(out fork)` in place of its first two lines; `UseShellExecute = true` stays (it works for both builds; the uiAccess one needs it). `SetPorts(fork)` and the rest are unchanged.

`packages.ps1`: `glzr-io.glazewm` and `glzr-io.zebar` leave `$WingetPackages` and `$WholePC` for bundle installs (Zebar is gone with 1.6 anyway). `install/glazewm-fork.ps1 -Check` answers `skip  per-user build in use (opt in: omawin admin glazewm)` when the per-user build exists, so `Get-AdminPlan` never puts it in a prompt by itself.

**The opt-in, "Manage admin windows too":** admin verb `glazewm-uiaccess <tag>` (7.4): downloads `glazewm-<v>-windows-<arch>-uiaccess.zip` from the fork's release, checks it against `SHA256SUMS-windows-<arch>.txt`, checks each exe's Authenticode signature (`Get-AuthenticodeSignature`: `Status -eq 'Valid'` and the expected subject), copies to `Program Files\omawin\glazewm\` (a new folder: winget's official package keeps `glzr.io\GlazeWM` to itself). No certificate is made on the PC. Later versions are offered on the welcome page / the bar's admin line, never installed by `omawin update` (which never prompts). Undo: verb `glazewm-uiaccess-remove` deletes that folder (omawin's own files), and the per-user build takes over at the next start.

Until CI signing exists ([release-signing.md](../release-signing.md)), the opt-in keeps today's path (`install/glazewm-fork.ps1 -Machine`: the PC's own certificate), still inside the one batch.

### 7.3 WSL: is Virtual Machine Platform on? (no admin)

`Get-WindowsOptionalFeature -Online` and `dism /online` need admin. A standard user can read the same through WMI/CIM *(check on a standard account; the CI job in 8.1 runs it)*:

`install/wsl-ready.ps1`:

```powershell
# Whether WSL can run here without an admin prompt, worked out without admin. Windows PowerShell 5.1.
# Returns: Platform (on/off/unknown), RestartPending, Firmware (on/off/unknown), WslPackage (bool),
# NeedsAdmin (bool), AdminVerbs (what the one prompt would do), Why (a sentence for the page).
function Test-WslReady {
    $r = [ordered]@{ Platform = 'unknown'; RestartPending = $false; Firmware = 'unknown'; WslPackage = $false; NeedsAdmin = $false; AdminVerbs = @(); Why = '' }

    # Win32_OptionalFeature.InstallState: 1 enabled, 2 disabled, 3 absent, 4 unknown.
    try {
        $f = Get-CimInstance -ClassName Win32_OptionalFeature -Filter "Name='VirtualMachinePlatform'" -ErrorAction Stop
        $r.Platform = switch ([int]$f.InstallState) { 1 { 'on' } 2 { 'off' } 3 { 'off' } default { 'unknown' } }
    } catch { }

    # Is a hypervisor running now? True once VMP (or Hyper-V) is on *and* Windows restarted since.
    $hyper = $null
    try { $hyper = [bool](Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).HypervisorPresent } catch { }
    if ($r.Platform -eq 'on' -and $hyper -eq $false) { $r.RestartPending = $true }
    if ($r.Platform -eq 'unknown' -and $hyper) { $r.Platform = 'on' }     # can't read the feature, but something runs a hypervisor

    # Virtualization in the firmware (BIOS/UEFI): reads false when a hypervisor already runs (it
    # hides the bit), so only asked when there's none. Off: no admin prompt can fix it.
    if (-not $hyper) {
        try { $r.Firmware = if ((Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1).VirtualizationFirmwareEnabled) { 'on' } else { 'off' } } catch { }
    } else { $r.Firmware = 'on' }

    # The WSL package itself (today's Test-WslInstalled, install/lib.ps1).
    $r.WslPackage = Test-Path "$env:ProgramFiles\WSL\wsl.exe"

    if ($r.Firmware -eq 'off') {
        $r.Why = "Virtualization is off in this PC's firmware (BIOS/UEFI): turn it on there, then pick Linux again."
    } elseif ($r.Platform -ne 'on') {
        $r.NeedsAdmin = $true
        $r.AdminVerbs += 'wsl-platform'
        if (-not $r.WslPackage) { $r.AdminVerbs += 'wsl-package' }
        $r.Why = "Linux needs Windows' Virtual Machine Platform turned on (one admin prompt, then a restart)."
    } elseif (-not $r.WslPackage) {
        # The WSL package is an MSI into Program Files: admin (check: whether the Store's per-user
        # package, winget 9P9TQF7MRM4R --source msstore, installs without a prompt on 24H2).
        $r.NeedsAdmin = $true
        $r.AdminVerbs += 'wsl-package'
        $r.Why = 'Linux needs the WSL package (one admin prompt; no restart, the platform is already on).'
    } elseif ($r.RestartPending) {
        $r.Why = 'Linux is ready after Windows restarts.'
    }
    [pscustomobject]$r
}
```

When everything's on (many PCs: Docker Desktop, an earlier WSL, a work image), `linux arch` in the queue installs the distro per user with **no prompt** (`wsl --install -d archlinux --no-launch` and `wsl/setup.sh`, as `wsl/install.ps1` does today). When it isn't, the welcome page shows `Why` and *Approve* (7.4); the queue step stays `blocked`, and runs by itself after the approval (and the restart).

### 7.4 The opt-in admin batch: preview → one prompt → fixed verbs → summary

Two halves, as today (`install/admin.ps1` asks, `install/admin-batch.ps1` runs elevated), generalised from "the install's packages, forks and WSL" to **a fixed list of verbs**:

| Verb | Says (preview) | Args | Restart | Undo |
|---|---|---|---|---|
| `winget-machine <id>` | Install `<name>` for every account on this PC | winget id | maybe | `winget uninstall` (admin) |
| `choco-install <id>` / `choco-uninstall <id>` / `choco-upgrade <id\|all>` | Chocolatey: install / remove / update `<id>` | choco id | maybe | the opposite verb |
| `wsl-platform` | Turn on Virtual Machine Platform (for Linux) | | yes | turn it off |
| `wsl-package` | Install WSL | | no | `wsl --uninstall` |
| `glazewm-uiaccess <tag>` | Let GlazeWM manage windows of programs you run as admin (omawin's signed GlazeWM in Program Files) | release tag | no | `glazewm-uiaccess-remove` |
| `glazewm-fork-legacy` | today's on-PC-signed fork (until CI signing) | | no | `install/glazewm-fork.ps1 -Restore` |
| `feature <name>` | Turn on `<Windows feature>` | one of: `Containers-DisposableClientVM` (Windows Sandbox), `Microsoft-Windows-Subsystem-Linux` | yes | turn it off |
| `setting <id>` | the settings catalog entry's own sentence (plans 02/07) | catalog id with `admin: true` | per entry | the entry's undo |
| `restore-point` | Make a restore point first | | no | |

Pending verbs live in `%LOCALAPPDATA%\launcher\admin-pending.txt` (written by the queue, `omawin pkg`, the settings page). Nothing prompts by itself: **you** say when (the welcome page's *Admin: N changes*, the bar's quiet shield, or `omawin admin`).

`bin/commands.txt`:

```
admin | What's waiting for admin, the one prompt to do it all, and what it changed: omawin admin [list|approve|forget] | pwsh bin/admin.ps1
```

`bin/admin.ps1` (the unelevated half; 5.1-safe, used by install too):

```powershell
<#
.SYNOPSIS
    omawin admin: everything waiting for admin, shown, then done after ONE Windows prompt.
.DESCRIPTION
        omawin admin                 list what's waiting and ask once
        omawin admin list            just the list
        omawin admin approve         no question (the welcome page's button already asked)
        omawin admin forget          clear the list
    -Verbs adds to the list first (omawin pkg, the settings page). Never runs during an update or
    the background queue (OMAWIN_NO_ADMIN=1): it only writes the list then.
#>
param([Parameter(Position = 0)][ValidateSet('ask', 'list', 'approve', 'forget')][string]$Do = 'ask', [string[]]$Verbs)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$Pending = Join-Path $State 'admin-pending.txt'
. "$Repo\install\admin-verbs.ps1"        # $AdminVerbs: name -> @{ Args = regex; Say = scriptblock; Restart = bool }

if ($Verbs) { Add-Content $Pending $Verbs }
$list = @(Get-Content $Pending -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
# Only what the elevated half will accept: anything else is dropped here and said.
$plan = @()
foreach ($line in $list) {
    $verb, $arg = $line -split '\s+', 2
    $v = $AdminVerbs[$verb]
    if (-not $v -or ($v.Args -and $arg -notmatch $v.Args) -or (-not $v.Args -and $arg)) { Write-Host "  dropped (not an admin verb omawin has): $line" -ForegroundColor Yellow; continue }
    $plan += [pscustomobject]@{ Line = $line; Verb = $verb; Arg = $arg; Say = (& $v.Say $arg); Restart = $v.Restart }
}
if ($Do -eq 'forget') { Remove-Item $Pending -ErrorAction SilentlyContinue; 'cleared'; return }
if (-not $plan) { 'nothing needs admin'; return }

# 1. The preview: exactly what will change, before Windows asks.
Write-Host "`n  One admin prompt will do this:" -ForegroundColor Cyan
foreach ($p in $plan) { Write-Host "    - $($p.Say)$(if ($p.Restart) { '  (needs a restart)' })" }
if ($Do -eq 'list') { return }
if ($env:OMAWIN_NO_ADMIN) { "  waiting: run omawin admin when you're ready"; return }
if ($Do -eq 'ask') {
    $a = Read-Host "`n  Go ahead? Windows asks for admin once. [y/N]"
    if ($a -notmatch '^(y|yes)$') { '  not now: nothing changed; omawin admin when you want it'; return }
}

# 2. One prompt: the elevated half gets the plan as its arguments (seen in UAC's details), not a
#    file anyone could change between the preview and the prompt.
$status = Join-Path $env:TEMP "omawin-admin-$PID.txt"
Set-Content $status '' -NoNewline
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((($plan | ForEach-Object Line) -join "`n")))
$arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\install\admin-batch.ps1`"", '-Status', "`"$status`"", '-Plan', $encoded)
try { $p = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments }
catch { "  declined: nothing changed ($($_.Exception.Message))"; return }

# 3. Wait, showing each line as it lands (same status format as today: item`tstate`ttext).
$seen = 0
while (-not $p.HasExited) {
    Start-Sleep -Milliseconds 300
    $lines = @(Get-Content $status -ErrorAction SilentlyContinue)
    foreach ($l in $lines | Select-Object -Skip $seen) { $f = $l -split "`t", 3; if ($f[1] -ne 'run') { Write-Host "    $($f[1])  $($f[0])  $($f[2])" } }
    $seen = $lines.Count
}

# 4. The summary, kept: what changed, what didn't, what needs a restart, how to undo.
$results = @(Get-Content $status | ForEach-Object { $f = $_ -split "`t", 3; [pscustomobject]@{ Item = $f[0]; State = $f[1]; Text = $f[2] } } | Where-Object State -ne 'run')
$done = @($results | Where-Object State -eq 'done' | ForEach-Object Item)
Add-Content (Join-Path $State 'admin-done.log') ($results | ForEach-Object { "$((Get-Date).ToString('s'))`t$($_.Item)`t$($_.State)`t$($_.Text)" })
Set-Content $Pending @($list | Where-Object { $done -notcontains $_ })      # what failed stays waiting
$restart = @($plan | Where-Object { $_.Restart -and $done -contains $_.Line })
"`n  $($done.Count) of $($plan.Count) done$(if ($restart) { '; restart Windows to finish' })"
Remove-Item $status -ErrorAction SilentlyContinue
# Steps blocked on these (linux in the queue) run again now.
& "$Repo\bin\install-queue.ps1"
```

`install/admin-verbs.ps1` (shared by both halves, so the preview and the elevated check agree):

```powershell
# omawin's admin verbs: the only things an elevated omawin process ever does. Both halves of the
# one prompt read this (bin/admin.ps1 to describe, install/admin-batch.ps1 to check and run).
$IdRx = '^[A-Za-z0-9][A-Za-z0-9._+-]*$'
$AdminVerbs = @{
    'winget-machine'   = @{ Args = $IdRx; Restart = $false; Say = { param($a) "Install $a for every account on this PC" } }
    'choco-install'    = @{ Args = $IdRx; Restart = $false; Say = { param($a) "Chocolatey: install $a (whole PC)" } }
    'choco-uninstall'  = @{ Args = $IdRx; Restart = $false; Say = { param($a) "Chocolatey: remove $a" } }
    'choco-upgrade'    = @{ Args = $IdRx; Restart = $false; Say = { param($a) if ($a -eq 'all') { 'Chocolatey: update everything' } else { "Chocolatey: update $a" } } }
    'wsl-platform'     = @{ Args = $null; Restart = $true;  Say = { param($a) "Turn on Windows' Virtual Machine Platform (for Linux)" } }
    'wsl-package'      = @{ Args = $null; Restart = $false; Say = { param($a) 'Install WSL' } }
    'glazewm-uiaccess' = @{ Args = '^v[0-9][0-9A-Za-z.\-]*$'; Restart = $false; Say = { param($a) "Let GlazeWM manage windows of programs you run as admin (omawin's signed GlazeWM $a in Program Files)" } }
    'glazewm-uiaccess-remove' = @{ Args = $null; Restart = $false; Say = { param($a) "Remove omawin's GlazeWM from Program Files (the per-user one takes over)" } }
    'glazewm-fork-legacy' = @{ Args = $null; Restart = $false; Say = { param($a) "omawin's GlazeWM, signed on this PC (until CI signing)" } }
    'feature'          = @{ Args = '^(Containers-DisposableClientVM|Microsoft-Windows-Subsystem-Linux)$'; Restart = $true; Say = { param($a) "Turn on the Windows feature $a" } }
    'restore-point'    = @{ Args = $null; Restart = $false; Say = { param($a) 'Make a restore point first' } }
}
```

(`setting <id>` joins when the settings catalog exists: its `Args` is the catalog's admin ids, read from the catalog file, and its `Say` the entry's sentence.)

`install/admin-batch.ps1` (the elevated half): today's parameters `-Packages`, `-Wsl`, `-Forks` become `-Plan <base64>`; today's three blocks become three of the verbs. The dispatch:

```powershell
param([Parameter(Mandatory)][string]$Status, [Parameter(Mandatory)][string]$Plan)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'admin-verbs.ps1')
# (Say: today's function, unchanged. Invoke-WingetMachine, Install-WslPackage, Set-InUseFile and
# Install-GlazeUiAccess, below in this sketch, go above this point in the real file: a script's
# functions must be defined before they're called.)

$lines = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Plan)) -split "`n"
# Checked again here: this process trusts nothing from the account that started it but these
# lines, and runs only what admin-verbs.ps1 names, with arguments of the shape it names.
$steps = foreach ($l in $lines) {
    $verb, $arg = $l.Trim() -split '\s+', 2
    $v = $AdminVerbs[$verb]
    if (-not $v -or ($v.Args -and $arg -notmatch $v.Args) -or (-not $v.Args -and $arg)) { Say $l 'skip' 'not an omawin admin verb'; continue }
    [pscustomobject]@{ Line = $l.Trim(); Verb = $verb; Arg = $arg }
}
# A restore point first when asked (only inside a batch you approved: 2.9).
foreach ($s in @($steps | Where-Object Verb -eq 'restore-point')) {
    Say $s.Line 'run'
    try { Checkpoint-Computer -Description 'omawin admin batch' -RestorePointType MODIFY_SETTINGS; Say $s.Line 'done' } catch { Say $s.Line 'fail' "$_" }
}
# winget machine installs together, three at a time (today's queue loop, lines 50-110, as a function).
$wingetIds = @($steps | Where-Object Verb -eq 'winget-machine' | ForEach-Object Arg)
if ($wingetIds) { Invoke-WingetMachine $wingetIds }       # Says "winget-machine <id>" per package, as today
foreach ($s in @($steps | Where-Object { $_.Verb -notin 'restore-point', 'winget-machine' })) {
    Say $s.Line 'run'
    try {
        $text = switch ($s.Verb) {
            'wsl-platform'     { $r = & "$env:SystemRoot\System32\dism.exe" /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart; if ($LASTEXITCODE -notin 0, 3010) { throw "dism exit $LASTEXITCODE" }; 'restart' }
            'wsl-package'      { Install-WslPackage }        # today's block 2 (wsl --install --no-distribution --web-download)
            'feature'          { & "$env:SystemRoot\System32\dism.exe" /online /enable-feature "/featurename:$($s.Arg)" /all /norestart | Out-Null; if ($LASTEXITCODE -notin 0, 3010) { throw "dism exit $LASTEXITCODE" }; 'restart' }
            'choco-install'    { choco install $s.Arg -y --no-progress | Out-Null; if ($LASTEXITCODE -notin 0, 1641, 3010) { throw "choco exit $LASTEXITCODE" } }
            'choco-uninstall'  { choco uninstall $s.Arg -y | Out-Null; if ($LASTEXITCODE) { throw "choco exit $LASTEXITCODE" } }
            'choco-upgrade'    { choco upgrade $s.Arg -y --no-progress | Out-Null; if ($LASTEXITCODE -notin 0, 1641, 3010) { throw "choco exit $LASTEXITCODE" } }
            'glazewm-uiaccess' { Install-GlazeUiAccess $s.Arg }   # below
            'glazewm-uiaccess-remove' { Remove-Item "$env:ProgramFiles\omawin\glazewm" -Recurse -Force; '' }
            'glazewm-fork-legacy' { & (Join-Path $PSScriptRoot 'glazewm-fork.ps1') -Machine -Out "$env:TEMP\fork-$PID.txt"; (Get-Content "$env:TEMP\fork-$PID.txt" -Raw) }
        }
        Say $s.Line 'done' "$text"
    } catch { Say $s.Line 'fail' "$_" }
}
Say 'end' 'done'

# The CI-signed uiAccess GlazeWM, into Program Files\omawin\glazewm. Verified twice: the release's
# SHA-256, then each exe's Authenticode signature (valid, omawin's publisher).
function Install-GlazeUiAccess([string]$Tag) {
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $v = $Tag.TrimStart('v')
    $base = "https://github.com/mjveilleux/glazewm/releases/download/$Tag"
    $work = Join-Path $env:TEMP "omawin-glaze-$PID"
    New-Item -ItemType Directory -Force $work | Out-Null
    $zip = "glazewm-$v-windows-$arch-uiaccess.zip"
    & "$env:SystemRoot\System32\curl.exe" -fsSL -o "$work\$zip" "$base/$zip"
    & "$env:SystemRoot\System32\curl.exe" -fsSL -o "$work\sums.txt" "$base/SHA256SUMS-windows-$arch.txt"
    $want = ((Get-Content "$work\sums.txt" | Where-Object { $_ -match [regex]::Escape($zip) }) -split '\s+')[0]
    if (-not $want -or (Get-FileHash "$work\$zip" -Algorithm SHA256).Hash -ne $want.ToUpper()) { throw "$zip doesn't match its SHA-256" }
    New-Item -ItemType Directory -Force "$work\x" | Out-Null
    & "$env:SystemRoot\System32\tar.exe" -xf "$work\$zip" -C "$work\x"
    foreach ($exe in Get-ChildItem "$work\x" -Recurse -Filter *.exe) {
        $sig = Get-AuthenticodeSignature $exe.FullName
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=omawin') { throw "$($exe.Name): not signed by omawin ($($sig.Status))" }   # (check: the subject)
    }
    $dest = "$env:ProgramFiles\omawin\glazewm"
    New-Item -ItemType Directory -Force $dest | Out-Null
    # In use by another account: renamed aside (today's Set-InUseFile, install/glazewm-fork.ps1:52).
    foreach ($f in Get-ChildItem "$work\x" -Recurse -File) { Set-InUseFile $f.FullName (Join-Path $dest $f.FullName.Substring("$work\x\".Length)) }
    Remove-Item $work -Recurse -Force
    "installed $Tag"
}
```

`install.ps1`'s own use: `Get-AdminPlan` returns verbs instead of `Packages/Forks/Wsl`; `Start-AdminBatch` becomes `bin/admin.ps1 approve` after install's own question; on a bundle install nothing calls it until you choose.

**An update never prompts:** `bin/update-bundle.ps1` and `bin/update.ps1` set `OMAWIN_NO_ADMIN=1` for everything they run (today `install.ps1` sets it after its prompt). A newer uiAccess GlazeWM is written to `admin-pending.txt` (`glazewm-uiaccess <new tag>`) and shown as a quiet line; the per-user build is used meanwhile only if the Program Files one is older than the bundle's *(check: or keep running the older uiAccess one until approved; decide on the PC)*.

### 7.5 CI: install and update as a standard user, failing on elevation

Three layers, cheapest first:

1. **A lint** (any runner, every PR): elevation appears only where it's allowed.

```yaml
  no-elevation-lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - name: Only bin/admin.ps1 and install/admin.ps1 may ask for admin
        run: |
          hits=$(git grep -n -i -E -- '-Verb[[:space:]]+RunAs|Verb[[:space:]]*=[[:space:]]*"runas"|ShellExecute.*runas' -- '*.ps1' '*.cs' \
                 ':!bin/admin.ps1' ':!install/admin.ps1' || true)
          if [ -n "$hits" ]; then echo "$hits"; echo '::error::elevation outside the one admin batch'; exit 1; fi
```

(`install/glazewm-fork.ps1` and `zebar-fork.ps1` elevate themselves today when run alone; they move behind `bin/admin.ps1` as part of this work, or join the allow-list with a comment saying why.)

2. **The run as a standard user** (`build/ci/as-user.ps1`, used by the install and update jobs in 8.1): the runner is an administrator; it makes a standard local user and runs the script as that user, in that user's own profile, through Task Scheduler (a batch logon with the user's password; no desktop).

```powershell
<#
.SYNOPSIS
    CI: runs a script as a fresh standard (non-admin) local user and fails if anything elevated.
#>
param([Parameter(Mandatory)][string]$Script, [string[]]$Arguments = @(), [int]$Minutes = 40, [string]$OutDir = 'ci-out')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$user = 'omawinci'
# One user per job: made on the first call, its password kept in the runner's temp for the next.
$pwFile = Join-Path $env:RUNNER_TEMP 'omawinci.pw'
if (-not (Get-LocalUser $user -ErrorAction SilentlyContinue)) {
    $pw = [Convert]::ToBase64String((1..24 | ForEach-Object { [byte](Get-Random -Max 256) })) + 'aA1!'
    & net.exe user $user $pw /add /y | Out-Null                     # in Users, not Administrators
    Set-Content $pwFile $pw
}
$pw = Get-Content $pwFile
if ((Get-LocalGroupMember Administrators).Name -match "\\$user$") { throw "$user is an administrator" }
# Batch logon for this user (Task Scheduler's "run whether signed in or not") (check: needed on
# windows-2025; Task Scheduler may grant it itself).
# ... secedit export / add SeBatchLogonRight / import, or the LSA call ...

# The watcher: every process start on the machine; consent.exe is UAC's prompt (it runs even when
# the prompt can't show), and an elevated child of the test user's processes is the other tell.
$elevations = [Collections.Concurrent.ConcurrentBag[string]]::new()
Register-CimIndicationEvent -ClassName Win32_ProcessStartTrace -SourceIdentifier omawin-procs -Action {
    $n = $Event.SourceEventArgs.NewEvent.ProcessName
    if ($n -in 'consent.exe') { $Event.MessageData.Add("$((Get-Date).ToString('s')) $n pid $($Event.SourceEventArgs.NewEvent.ProcessID)") }
} -MessageData $elevations | Out-Null

$log = Join-Path $OutDir 'run.log'
$cmd = "-NoProfile -ExecutionPolicy Bypass -Command `"& '$Script' $($Arguments -join ' ') *> '$log'; exit `$LASTEXITCODE`""
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $cmd
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes $Minutes)
Unregister-ScheduledTask -TaskName 'omawin-ci' -Confirm:$false -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName 'omawin-ci' -Action $action -Settings $settings -User "$env:COMPUTERNAME\$user" -Password $pw -RunLevel Limited | Out-Null
Start-ScheduledTask -TaskName 'omawin-ci'
do { Start-Sleep 5; $t = Get-ScheduledTask -TaskName 'omawin-ci' } while ($t.State -eq 'Running')
$result = (Get-ScheduledTaskInfo -TaskName 'omawin-ci').LastTaskResult
Unregister-Event omawin-procs

Get-Content $log -ErrorAction SilentlyContinue
# What the user's install wrote, for the job's artifacts and summary.
$local = "C:\Users\$user\AppData\Local\launcher"
Copy-Item "$local\install.log", "$local\install-times.txt", "$local\install-progress.log" $OutDir -ErrorAction SilentlyContinue
if ($elevations.Count) { $elevations | ForEach-Object { "::error::elevation: $_" }; throw 'something asked for admin' }
if (Select-String -Path "$OutDir\*.log" -Pattern 'admin step started' -Quiet) { throw 'the install started an admin batch' }
if ($result -ne 0) { throw "the script exited $result" }
```

(`Register-CimIndicationEvent` needs admin, which the runner has; the watched script doesn't.) *(check)* whether `consent.exe` starts for a `-Verb RunAs` from a non-interactive batch session (if not, layer 1 and the `admin step started` line are the net; a third tell is the Security log's event 4688 with `TokenElevationType` `%%1937` for the test user, available once process-creation auditing is turned on in the job: `auditpol /set /subcategory:"Process Creation" /success:enable`).

3. **The Sandbox run** (8.2) is interactive, so it sees a real UAC prompt if one appears (its screenshot shows it).

---

## 8. CI install timing and the Windows Sandbox test

### 8.1 `.github/workflows/install.yml` (2.1, and 2.9's standard-user check)

`release.yml` becomes callable, so the install job tests the branch's own build:

```yaml
# .github/workflows/release.yml: added to `on:`
  workflow_call:
    inputs:
      version: { type: string, default: v0.0.0-ci }
      publish: { type: boolean, default: false }
```

(`meta` reads `inputs.version`/`inputs.publish` already; under `workflow_call` `github.ref_type` is the caller's (a branch), so it takes the input. *(check)*)

`build/install-budget.txt`:

```
# Seconds since the start, by the end of each bundle step (install-times.txt's "bundle" lines),
# for a CI runner installing from a local bundle (no download time). The job fails past them.
# Online runs add the download: see the "online" job.
resolve    2
download   4
unpack    10
link      11
seed      14
path      15
font      16
sign-in   18
queue     19
done      20
```

```yaml
name: Install
# A fresh Windows runner installs the branch's bundle as a standard user, then updates to it from
# the latest release; both timed against build/install-budget.txt, both failing if anything asks
# for admin. Runners have no WSL (no nested virtualization) and no interactive desktop: the wsl
# preset and the desktop start are skipped, and the summary says so. Runner images come with Git,
# Node and PowerShell 7, so the Sandbox run (omawin sandbox test) stays the honest clean PC.
on:
  push:
    branches: [main]
  pull_request:
    paths: ['bootstrap.ps1', 'install/**', 'bundle/**', 'build/**', 'bin/update*.ps1', 'bin/install-queue.ps1', 'bin/pkg*.ps1', 'bin/bundle.ps1', '.github/workflows/install.yml']
  schedule:
    - cron: '43 3 * * *'
  workflow_dispatch:

permissions:
  contents: read
  actions: read       # the trend: earlier runs' install-times artifacts

jobs:
  build:
    uses: ./.github/workflows/release.yml
    with:
      version: v0.0.0-ci.${{ github.run_number }}
      publish: false

  install:
    needs: build
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4
        with:
          name: bundle-x64
          path: C:\bundle
      - name: The bundle's SHA256SUMS.txt (the release job would write it)
        shell: pwsh
        run: .\build\release.ps1 -Tag v0.0.0-ci.${{ github.run_number }} -OutDir C:\bundle -OnlyManifest
      - name: Process-creation auditing (the elevation check's third tell)
        shell: pwsh
        run: auditpol /set /subcategory:"Process Creation" /success:enable
      - name: Install as a standard user
        shell: pwsh
        run: |
          .\build\ci\as-user.ps1 -OutDir ci-out\install -Script "$pwd\bootstrap.ps1" `
            -Arguments '-Bundle', 'C:\bundle', '-Yes', '-Presets', 'none', '-Linux', 'none', '-NoStart'
      - name: The background queue, to the end, as the same user
        shell: pwsh
        run: .\build\ci\as-user.ps1 -OutDir ci-out\queue -Script 'C:\Users\omawinci\omawin\bin\install-queue.ps1' -Minutes 30   # through the ~/omawin junction
      - name: omawin check, as the same user
        shell: pwsh
        run: .\build\ci\as-user.ps1 -OutDir ci-out\check -Script 'C:\Users\omawinci\omawin\check.ps1' -Arguments '-NoDesktop'   # (check: add -NoDesktop: skips "GlazeWM running" and the like)
      - name: Times against the budget, and the trend
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: .\build\ci\install-summary.ps1 -Times ci-out\install\install-times.txt -Budget build\install-budget.txt -Workflow install.yml
      - uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        if: always()
        with:
          name: install-times
          path: ci-out

  update:
    needs: build
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4
        with: { name: bundle-x64, path: C:\bundle }
      - shell: pwsh
        run: .\build\release.ps1 -Tag v0.0.0-ci.${{ github.run_number }} -OutDir C:\bundle -OnlyManifest
      - name: The latest release, installed as a standard user (from GitHub)
        shell: pwsh
        run: .\build\ci\as-user.ps1 -OutDir ci-out\old -Script "$pwd\bootstrap.ps1" -Arguments '-Yes', '-Presets', 'none', '-Linux', 'none', '-NoStart'
      - name: Updated to this branch's bundle (curl reads file:// like https://)
        shell: pwsh
        run: .\build\ci\as-user.ps1 -OutDir ci-out\update -Script 'C:\Users\omawinci\omawin\bin\update-bundle.ps1' -Arguments '-Base', 'file:///C:/bundle'
      - name: Rolled back, and forward again
        shell: pwsh
        run: |
          .\build\ci\as-user.ps1 -OutDir ci-out\rollback -Script 'C:\Users\omawinci\omawin\bin\update-bundle.ps1' -Arguments '-Rollback'
          .\build\ci\as-user.ps1 -OutDir ci-out\forward -Script 'C:\Users\omawinci\omawin\bin\update-bundle.ps1' -Arguments '-Base', 'file:///C:/bundle'
      - uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        if: always()
        with: { name: update-log, path: ci-out }

  # The download from GitHub's CDN, nightly only: the number people feel.
  online:
    if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - shell: pwsh
        run: |
          .\build\ci\as-user.ps1 -OutDir ci-out -Script "$pwd\bootstrap.ps1" -Arguments '-Yes', '-Presets', 'none', '-Linux', 'none', '-NoStart'
          .\build\ci\install-summary.ps1 -Times ci-out\install-times.txt -Online
```

(Each `as-user.ps1` call in one job reuses the same user: the script creates it only when missing. The `install` job is skipped when `build` isn't needed for a docs-only PR, by the `paths` filter.)

`build/ci/install-summary.ps1`:

```powershell
param([Parameter(Mandatory)][string]$Times, [string]$Budget, [string]$Workflow, [switch]$Online)
$ErrorActionPreference = 'Stop'
$now = [ordered]@{}
foreach ($l in Get-Content $Times) { $p = $l -split '\s+'; if ($p[0] -eq 'bundle') { $now[$p[1]] = [double]$p[2] } }
$limit = @{}
if ($Budget) { foreach ($l in Get-Content $Budget) { if ($l -match '^\s*([\w-]+)\s+([\d.]+)') { $limit[$Matches[1]] = [double]$Matches[2] } } }

# The trend: the last ten successful runs on main (their install-times artifact).
$trend = @{}
if ($Workflow) {
    $runs = gh run list --workflow $Workflow --branch main --status success --limit 10 --json databaseId --jq '.[].databaseId'
    foreach ($id in $runs) {
        $d = Join-Path $env:RUNNER_TEMP "trend-$id"
        gh run download $id -n install-times -D $d 2>$null
        $f = Get-ChildItem $d -Recurse -Filter install-times.txt -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { foreach ($l in Get-Content $f.FullName) { $p = $l -split '\s+'; if ($p[0] -eq 'bundle') { $trend[$p[1]] += @([double]$p[2]) } } }
    }
}

$over = @()
$md = @("### Install times$(if ($Online) { ' (online, from GitHub)' })", '', '| step | seconds | budget | median of last 10 |', '|---|---:|---:|---:|')
foreach ($k in $now.Keys) {
    $med = if ($trend[$k]) { ($trend[$k] | Sort-Object)[[int][Math]::Floor($trend[$k].Count / 2)] } else { '' }
    $b = if ($limit.ContainsKey($k)) { $limit[$k] } else { '' }
    $flag = if ($b -and $now[$k] -gt $b) { ' (over)' } else { '' }
    if ($flag) { $over += $k }
    $md += "| $k | $($now[$k])$flag | $b | $med |"
}
$md += '', 'Skipped on runners: the desktop start (no interactive session), the wsl preset (no nested virtualization).'
$md | Add-Content $env:GITHUB_STEP_SUMMARY
if ($over) { throw "over budget: $($over -join ', ')" }
```

### 8.2 The Windows Sandbox test: `bin/sandbox-test.ps1` (7.1)

The new bootstrap needs no winget for the desktop, so the Sandbox needs no winget bootstrap unless presets are tested (then the `Microsoft.WinGet.Client` module's `Repair-WinGetPackageManager`, as winget-pkgs' `Tools/SandboxTest.ps1` does *(check: follow that script)*).

The Sandbox's own account (`WDAGUtilityAccount`) is an administrator *(check: and runs unelevated-by-UAC or not)*; `bootstrap.ps1` refuses admin, so the logon script sets `OMAWIN_TEST_ALLOW_ADMIN=1`, which `bootstrap.ps1` honours only for that account name. The no-admin guarantee is CI's job (7.5); the Sandbox proves the desktop comes up on a clean Windows, with a screenshot.

`bin/commands.txt`:

```
sandbox test | Install omawin in a clean Windows Sandbox and report the times, omawin check and a screenshot: omawin sandbox test [-Bundle <dir>] [-Release <tag>] [-Presets go,data] | pwsh bin/sandbox-test.ps1
```

```powershell
<#
.SYNOPSIS
    omawin sandbox test: a clean Windows (Windows Sandbox), omawin installed in it, the result here.
.DESCRIPTION
    Writes a .wsb: this repo mapped read-only, a bundle folder (optional) read-only, a writable out
    folder, networking on, vGPU off, 8 GB. Its logon command runs build\sandbox\logon.ps1, which
    installs, waits for the background queue, runs omawin check, screenshots the desktop and
    writes out\done.txt. This waits for that file, then prints the times and check's result.
    Windows 11 Pro, Enterprise or Education with Windows Sandbox on (omawin admin: feature
    Containers-DisposableClientVM). Not on Home.
#>
param([string]$Bundle, [string]$Release = 'latest', [string[]]$Presets = @('none'), [int]$TimeoutMinutes = 30)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot

$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
if ($edition -match '^Core') { throw "Windows Sandbox isn't available on Windows Home ($edition); CI's install job (.github/workflows/install.yml) is the clean test there" }
if (-not (Test-Path "$env:SystemRoot\System32\WindowsSandbox.exe")) {
    throw 'Windows Sandbox is off: omawin admin (it queues "feature Containers-DisposableClientVM"; one prompt and a restart)'
}

$run = Join-Path $env:LOCALAPPDATA "launcher\sandbox\$(Get-Date -Format yyyyMMdd-HHmmss)"
$out = Join-Path $run 'out'
New-Item -ItemType Directory -Force $out | Out-Null
# The settings for the logon script: a file, since a LogonCommand is one command line.
@{ Release = $Release; Presets = ($Presets -join ','); Bundle = [bool]$Bundle } | ConvertTo-Json | Set-Content "$out\settings.json"

$folders = @"
    <MappedFolder><HostFolder>$Repo</HostFolder><SandboxFolder>C:\omawin-src</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$out</HostFolder><SandboxFolder>C:\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
"@
if ($Bundle) { $folders += "    <MappedFolder><HostFolder>$((Resolve-Path $Bundle).Path)</HostFolder><SandboxFolder>C:\bundle</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>`n" }
$wsb = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Enable</Networking>
  <MemoryInMB>8192</MemoryInMB>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MappedFolders>
$folders  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File C:\omawin-src\build\sandbox\logon.ps1</Command>
  </LogonCommand>
</Configuration>
"@
$wsbFile = Join-Path $run 'omawin-test.wsb'
Set-Content $wsbFile $wsb -Encoding UTF8

# 24H2 and later: the wsb command line (check: `wsb start --config` takes the XML text, not a path).
# Before: open the .wsb file.
$started = Get-Date
$id = $null
if (Get-Command wsb -ErrorAction SilentlyContinue) { $id = (wsb start --config $wsb | Select-Object -Last 1) }
else { Start-Process $wsbFile }
"Windows Sandbox started$(if ($id) { " ($id)" }); waiting for $out\done.txt (up to $TimeoutMinutes min)"

while (-not (Test-Path "$out\done.txt")) {
    if (((Get-Date) - $started).TotalMinutes -gt $TimeoutMinutes) { throw "no result after $TimeoutMinutes minutes; what there is: $out" }
    Start-Sleep 5
}
"`nTimes (seconds since the start):"
Get-Content "$out\install-times.txt" -ErrorAction SilentlyContinue | Where-Object { $_ -like 'bundle *' } | ForEach-Object { "  $_" }
"`nomawin check:"
Get-Content "$out\check.txt" -ErrorAction SilentlyContinue | Select-Object -Last 3 | ForEach-Object { "  $_" }
"`nScreenshot: $out\desktop.png"
if ($id) { wsb stop --id $id }      # (check: the stop syntax)
$failed = Select-String -Path "$out\check.txt" -Pattern '^\s*FAIL' -Quiet
if ($failed) { exit 1 }
```

`build/sandbox/logon.ps1` (runs inside the Sandbox, Windows PowerShell 5.1):

```powershell
# Inside Windows Sandbox: install omawin as a new user would, then report to C:\out.
$ErrorActionPreference = 'Continue'
$out = 'C:\out'
Start-Transcript "$out\logon-transcript.txt"
$s = Get-Content "$out\settings.json" -Raw | ConvertFrom-Json
$env:OMAWIN_TEST_ALLOW_ADMIN = '1'      # the Sandbox's own account is an admin; bootstrap.ps1 allows it only here
$env:OMAWIN_PLAIN = '1'
$clock = [Diagnostics.Stopwatch]::StartNew()

# The bootstrap from the mapped repo (what's on disk), from a local bundle or the release.
$a = @{ Yes = $true; Presets = @($s.Presets -split ','); Linux = 'none' }
if ($s.Bundle) { $a.Bundle = 'C:\bundle' } else { $a.Tag = $s.Release }
& 'C:\omawin-src\bootstrap.ps1' @a *> "$out\bootstrap.txt"
"desktop-up $([Math]::Round($clock.Elapsed.TotalSeconds, 1))" | Add-Content "$out\sandbox-times.txt"

# The background queue to the end (the worker runs hidden; wait for its mutex to free).
$deadline = (Get-Date).AddMinutes(20)
do {
    Start-Sleep 5
    $m = $null
    $busy = [Threading.Mutex]::TryOpenExisting('Local\omawin-install-queue', [ref]$m)
    if ($m) { $m.Dispose() }
} while ($busy -and (Get-Date) -lt $deadline)
"queue-done $([Math]::Round($clock.Elapsed.TotalSeconds, 1))" | Add-Content "$out\sandbox-times.txt"

& "$HOME\omawin\check.ps1" *> "$out\check.txt"

# The desktop, as it looks now.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$b = [Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
$g = [Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
$bmp.Save("$out\desktop.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$state = "$env:LOCALAPPDATA\launcher"
Copy-Item "$state\install-times.txt", "$state\install.log", "$state\install-progress.log" $out -ErrorAction SilentlyContinue
Stop-Transcript
Set-Content "$out\done.txt" (Get-Date).ToString('s')
```

---

## 9. Order of work, files touched, open questions

### 9.1 Order (milestone "fast"; each line one sitting, mostly independent)

| # | Work | Proves | Needs the PC |
|---|---|---|---|
| 1 | 7.5 layer 1 (the lint) and `build/ci/as-user.ps1`; 8.1's job running **today's** `bootstrap.ps1 -Git` path as a standard user | M0: today's numbers, and every prompt today's install raises | no |
| 2 | 8.2 `omawin sandbox test` on today's install | the honest baseline | yes (Pro) |
| 3 | 7.1 the fork's two builds and CI; 7.2 `FindGlazeWM` | GlazeWM with no admin | yes: tiling, focus, an elevated window left alone |
| 4 | 2.1-2.4 the manifest, `build/bundle.ps1`, the `bundle` job (stage zips, `files.tsv`) | a bundle exists per release | no |
| 5 | 3.2-3.7 the new `bootstrap.ps1`, `install/first-run.ps1`, `install/seed.ps1` + `seed.txt`, `login-start.ps1 -Omawin` | desktop from a bundle, no admin | yes |
| 6 | 4 the queue, the worker, `omawin.exe`'s resume, the bar's indicator | stage 2, presets, retries in the background | yes (bar) |
| 7 | 7.3 `Test-WslReady`, 7.4 the verbs, `bin/admin.ps1`, `admin-batch.ps1 -Plan` | one prompt only when chosen | yes (UAC) |
| 8 | 4.6 the welcome page | questions after the desktop | yes |
| 9 | 5 delta updates; 8.1's update job | updates in seconds, rollback | no (CI), then yes |
| 10 | 6 `omawin pkg` (module, names, commands, presets through it) and its CI | Phase 2B | partly |
| 11 | 2.5 the bump job | pins stay current | no |
| 12 | M2-M4: stage 1a/1b split timing, signing, trimming | < 30, < 15, < 10 s | yes |

### 9.2 Files

| New | Changed |
|---|---|
| `bundle/manifest.json`, `build/bundle.ps1`, `build/bundle-bump.ps1`, `.github/workflows/bundle-bump.yml` | `.github/workflows/release.yml` (`bundle` job, `workflow_call`, signing moved) |
| `install/first-run.ps1`, `install/seed.ps1`, `install/seed.txt`, `install/seed/*` (stub templates) | `bootstrap.ps1` (bundle path; today's body behind `-Git`) |
| `install/queue-lib.ps1`, `bin/install-queue.ps1`, `bin/nvim-restore.ps1`, `bin/git-identity.ps1`, `bin/welcome-apply.ps1` | `install/login-start.ps1` (`-Omawin`, `-PassThru`, Run-key fallback) |
| `bin/bundle.ps1` (`-Stage 2`, `-Link`, `-Unlink`, `-Relink`), `bin/update-bundle.ps1`, `bin/update-dispatch.ps1` | `bin/omawin.cs` (`FindGlazeWM`, `ResumeInstallQueue`, `fonts-load`, `pwsh` → `powershell.exe` fallback) |
| `bin/pkg.ps1`, `bin/pkg/Pkg.psm1`, `packages/names.tsv`, `packages/winget-overrides.tsv`, `wsl/omawin-pkg.sh` | `bin/commands.txt` (`pkg …`, `admin`, `welcome apply`, `sandbox test`, `update`) |
| `install/wsl-ready.ps1`, `install/admin-verbs.ps1`, `bin/admin.ps1`, `install/lib-linux.ps1` | `install/admin-batch.ps1` (`-Plan`, verbs), `install/admin.ps1` (verbs) |
| `.github/workflows/install.yml`, `build/ci/as-user.ps1`, `build/ci/install-summary.ps1`, `build/install-budget.txt` | `packages.ps1` (presets as names; GlazeWM/Zebar out of the core for bundle installs) |
| `bin/sandbox-test.ps1`, `build/sandbox/logon.ps1` | `themes/render.ps1` (`-OutDir`, `-Theme`), `check.ps1` (`-NoDesktop`; bundle paths; the GlazeWM it finds), `bin/update-available.ps1` |
| Fork: `.github/workflows/omawin-release.yml` | Fork: `scripts/omawin-release.ps1` (two variants, `-Arch`) |
| `menu-app`: the `welcome` and `install` pages, the bar's install cell | `menu-app/Bar/Bar.cs`, `Bar/Widgets.cs` (`InstallProgress`) |
| `migrations/<date>-bundle-path.ps1`: on a bundle install, omawin's own exes in `~/.local/bin` moved aside (`.backup-<stamp>`), so `current\bin` wins | `docs/install.md`, `docs/architecture.md`, `manual/` (install, update, `omawin pkg`, `omawin admin`) |

### 9.3 Open questions (file each as `open-questions/<slug>.md` when its work starts; the default is what this plan does)

| Slug | Question | Default here |
|---|---|---|
| `bundle-repo-path` | `~/omawin` as a junction to `current\repo` (keeps every path working) or move everything to `DOTFILES`=`current\repo`? | the junction; a git checkout there is left alone (developer mode) |
| `seeded-vs-linked-configs` | install-speed.md D wants seeded copies the user owns, merged on update; today configs are junctions into the repo | junctions and stubs (today's model) in `seed.txt`; `copy` rows only for rendered theme files. The copy/merge model is mission-todo track 1's design, not this plan's |
| `pwsh-before-stage-2` | `pwsh` actions in `commands.txt` before stage 2 lands | `omawin.exe` falls back to `powershell.exe`; scripts on that path stay 5.1-safe |
| `bundle-format` | zip vs tar.gz vs zstd for Windows' `tar.exe` | deflate zip (readable everywhere); measure in M3 |
| `uiaccess-update` | a newer CI-signed uiAccess GlazeWM waits for approval: keep running the older one, or the newer per-user one meanwhile? | keep the older uiAccess one; a quiet line offers the new |
| `wsl-package-per-user` | can the WSL package go in without admin (Store, per user) when VMP is on? | no (one prompt with `wsl-package`) until measured |
| `bump-pr-token` | PRs from `GITHUB_TOKEN` don't trigger workflows | run the install job by hand on bump PRs; a GitHub App token later |
| `win32-optionalfeature-standard-user` | does `Win32_OptionalFeature` answer a standard user? | yes; `HypervisorPresent` as the fallback |
