# Install in under a minute (plan)

**Status: planned** ([roadmap.md](roadmap.md), phase 2). The sub-plan for one goal: from `irm … | iex` on a clean Windows 11 PC to a working omawin desktop **in under one minute**, then **less: under 30, under 15, and under 10 seconds as the stretch**; everything else finishing in the background while you already use the desktop. And afterwards, every tool still updatable on its own through winget, Scoop or Chocolatey if you want.

## What "installed" means at the minute

At 60 seconds you have: GlazeWM tiling, the bar, the launcher and system menu on their keys, noctty on Win+Enter, the default theme everywhere, the Nerd Font. That's the desktop. Not yet (they arrive in the background, each with progress in the bar): the command-line tools beyond the essentials, Neovim's full setup, your presets, WSL and Arch, the machine-wide (admin) parts, Windows' tune-up.

## Where the time goes today

From the code (`install.ps1`, `install/steps.txt`, `install/software.ps1`, `bin/install-binaries.ps1`), roughly, on a new PC:

| Part | Today | Why it's slow |
|---|---|---|
| Bootstrap: winget's Git, a `git clone` | ~30-60 s | an installer for Git before anything else |
| Questions | waits for you | before anything happens |
| `software`: core winget packages, 4 at a time | ~150 s (`steps.txt`'s guess) | winget's per-package overhead (source update, manifest, installer, its own checks), MSI / EXE installers |
| `helpers`: three release downloads | ~25 s | each resolves the tag and manifest again; one after another |
| `shell`, `desktop`, `look`, `other` | ~25 s | PowerShell 5.1 work, one file at a time |
| The desktop | after you sign out and in | nothing starts it |

**Step 0 of this plan is to replace these guesses with numbers**: `install-times.txt` per step (exists), plus per-package times, from a CI run and a Windows Sandbox run.

## What Omarchy does (and we copy)

From Omarchy's code (`docs/file-layout.md`, `install/`):
1. **Everything comes with the installer.** The ISO carries an offline mirror of every package; installing is copying.
2. **Configs are packages**, seeded into a new account from `/etc/skel`: no per-file setup.
3. **What depends on the machine waits** for the first boot (a queue of deferred steps, retried until done).
4. **A fresh install runs no migrations**: they're marked done.
5. Downloads in parallel.

## The plan, by workstream

### A. Measure everything (first)

- **CI install job** (`.github/workflows/install.yml`, roadmap 2.1): bootstrap from the branch on a fresh Windows runner, every step and every download timed into the job summary; fails over budget.
- **Windows Sandbox run** (`omawin sandbox test`, roadmap 7.1): the honest clean PC, timed the same way.
- **Budgets per step** (the table in C below) checked in CI, so a slower change is caught when it's made.
- Bandwidth noted with each run; results reported at 20, 100 and 500 Mbit/s (throttled runs), since download dominates.

### B. The omawin bundle (our offline mirror)

One prebuilt download per release and architecture, built by CI, verified by `SHA256SUMS.txt`, everything portable (no installers):

- **Stage 1, the desktop (~30-60 MB target):** `omawin.exe`; the menu app (Native AOT, self-contained, so no .NET runtime to install); the GlazeWM fork (per-user build); noctty; the Nerd Font; the default theme's rendered files and first background; the repo's configs at that tag (no `git clone`).
- **Stage 2, the tools (~150-200 MB target):** the command-line core as single exes (ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, gh, uv, mise), **MinGit** (Git for Windows' portable build), **Neovim with LazyVim's plugins already installed** at `lazy-lock.json`'s versions, PowerShell 7 (its zip).
- Stage 2 downloads **in the background** once the desktop is up; its tools appear as they land.
- **Format:** whichever Windows' own `tar.exe` unpacks fastest on Windows 10 and 11 (zip vs `.tar.gz`, and zstd where `tar.exe` supports it): to measure (explore list).
- **Built by** the release workflow from pinned versions (one manifest in the repo listing each tool, its version, its upstream URL and hash), bumped by a scheduled job that opens a PR when upstreams release.
- **Licences:** each tool's licence and NOTICE in the bundle (all are MIT, Apache, BSD, MPL or similar; checked per tool).

### C. Download and unpack

- **`curl.exe`** (built into Windows 10 1803+ and 11) instead of Windows PowerShell 5.1's `Invoke-WebRequest`, which is many times slower: stage 1 as one file, stage 2 in parallel parts (`curl --parallel`), resumable (`-C -`).
- **`tar.exe`** (built in) to unpack, instead of `Expand-Archive`.
- Into `%LOCALAPPDATA%\omawin\<tag>\` with a **`current` junction**: nothing needs admin; an update is a new folder and a junction flip; a rollback is flipping back.
- Files fetched by `curl.exe` carry no mark of the web, so no "this file came from the internet" prompts; executables are signed in CI ([release-signing.md](release-signing.md)) so SmartScreen and Defender's reputation checks pass quickly. Defender scanning many new files on first run is measured (explore list).

**The budget** (100 Mbit/s, an SSD):

| Step | Target |
|---|---|
| `irm … \| iex` to `bootstrap.ps1` running | 2 s |
| Stage 1 download (~50 MB) | 4-5 s |
| Verify and unpack | 2-3 s |
| Seed configs, PATH, font, sign-in task | 3-5 s |
| Start GlazeWM, the bar, the menu app | 2-3 s |
| **Desktop up** | **~15-20 s** |
| Stage 2 in the background | ~20-30 s more |

That leaves headroom under the minute on slower lines (20 Mbit/s: stage 1 in ~20 s, desktop in ~35 s).

### D. Seed configs in one pass

- The bundle carries the configs; one PowerShell 5.1 function writes them all (Omarchy's `/etc/skel` idea), moving any existing file aside (`<path>.backup-<timestamp>`, AGENTS.md) — no step-by-step scripts on the critical path.
- Product vs user config (mission-todo track 1): seeded **copies** the user owns, with omawin's defaults in the bundle, instead of junctions into a git checkout; `omawin update` merges new defaults the way migrations do.
- PATH, the font's per-user registration, the sign-in task and the environment variables (`STARSHIP_CONFIG`, `HERDR_CONFIG_PATH`, …) in the same pass.

### E. Start the desktop at once

`omawin start` right after seeding: GlazeWM, then the bar and the menu app (`omawin.exe`'s painted strip covers the bar's first frames, as at sign-in). The taskbar auto-hides in the same pass. No sign-out.

### F. No questions before the desktop

Omakase: the defaults are good. Every question moves to a **welcome page** in the launcher, open on the new desktop: presets, which Linux (Arch preselected), git name and email, Dev Drive, the recommended Windows tune-up (8G), and the one admin prompt's list. Answering starts the background work; ignoring it keeps the defaults. `bootstrap.ps1 -Presets data,go -Linux arch -Yes` still answers everything up front, for scripts and agents.

### G. The background queue (Omarchy's deferred steps)

- A queue file (`%LOCALAPPDATA%\launcher\install-queue.txt`) of steps, run by a hidden worker after the desktop is up: stage 2's tools, the one admin prompt (the forks' signed machine-wide install with `uiAccess`, WSL, whole-PC packages), presets (winget, mise), WSL and Arch, Neovim's Mason tools, no-ads and the tune-up, migrations for anything a later release added.
- Progress in the bar (an install indicator with a count), details on a launcher page, a toast at the end.
- A failed step stays queued and retries at the next sign-in (as Omarchy's queue does across boots), with the reason on the page.
- Steps already done on the PC are skipped by checking, not by trusting a list (so a reinstall is fast too).

### H. Fast updates too

- `omawin update` downloads the new release's **file manifest** (path, size, hash per file), fetches only files that changed (from the release's per-file assets, or a delta archive built by CI against the last few releases), assembles the new version folder with hard links for unchanged files, runs the new migrations, flips `current`, and restarts the desktop's parts. Target: a typical update in a few seconds.
- Rollback: `omawin update --rollback` flips to the previous folder (kept until the next update).

### I. Package managers stay in charge when you want them

*Now part of `omawin pkg` ([roadmap.md](roadmap.md), phase 2B): `omawin tools source` and `omawin tools handover` below become `omawin pkg move` and `omawin pkg prefer`, and the bundle is one of `omawin pkg`'s backends.*

The bundle's copies are a fast start, never a lock-in. Anything can still be installed, updated or replaced through **winget, Scoop or Chocolatey** (or mise), individually, at any time.

- **Your package manager's copy wins.** omawin's bundled tools sit at the **end** of the user PATH (`%LOCALAPPDATA%\omawin\current\tools`). A tool you install with winget (its links folder), Scoop (its shims) or Chocolatey (on the system PATH, which Windows puts first anyway) is found before omawin's, with no configuration. mise's shims, when you use mise for a tool, likewise come before the bundle.
- **`omawin tools`** lists every tool omawin knows: its version, **which copy runs** and where it came from (bundle, winget, Scoop, Chocolatey, mise), and other copies that are shadowed. The PATH doctor (roadmap 8.35) uses the same data.
- **Pick a source per tool, or for all:** `omawin tools source <tool> winget|scoop|choco|mise|bundle`, or `omawin tools handover winget` for every tool at once: installs each through that manager (in the background queue), then takes the bundle's copy off the PATH (kept, not deleted, so switching back is instant). The bundle manifest records each tool's winget, Scoop and Chocolatey ids so this needs no lookup.
- **Updating:** `omawin update` updates the bundle's tools (only those still coming from the bundle); Update › All apps (roadmap 5.9) runs `winget upgrade --all`, `scoop update *` and `choco upgrade all` for theirs; `omawin tools update` does both and says what changed.
- **The Packages page** (exists: winget, Scoop and Chocolatey search and install) shows, for a tool omawin bundles, that a bundled copy exists and that installing it here will take over.
- **omawin itself on the package managers:** publish omawin to winget (`winget install omawin`), a Scoop bucket and Chocolatey, each installing the same bundle; `winget upgrade` then updates omawin like anything else. (A channel, not a replacement for `omawin update`, which also runs migrations; the package's post-install calls it.)

### J. Offline and repeat installs

- `install.ps1 -Bundle <path>`: install from a saved bundle (a USB stick, a share), no internet for the desktop (Omarchy's ISO, for Windows). Stage 2 and presets queue until there's a connection.
- A second Windows account on the same PC reuses the version folder (machine-wide `%ProgramData%\omawin\<tag>` when the first install had admin) and only seeds its own configs: seconds.

### K. No admin

The minute has **no admin prompt**, and neither does any update ([roadmap.md](roadmap.md), 2.9): the per-user GlazeWM build, per-user packages and the bundle need none; the few admin things (the `uiAccess` GlazeWM, Virtual Machine Platform for WSL when it's off, machine-wide settings) are opt-in from the welcome page, batched into one prompt at a moment you choose. CI runs the install and the update as a standard user and fails on any attempt to elevate.

## Milestones

| Milestone | Desktop up in | What it takes |
|---|---|---|
| **M0: baseline** | today's number, measured | A (CI and Sandbox timing) |
| **M1: under a minute** | < 60 s at 100 Mbit/s | B (one bundle, stage 1 + 2 together), C, E, F (questions moved), G |
| **M2: under 30 seconds** | < 30 s | stage 1 split out, D (one-pass seeding), signed binaries |
| **M3: under 15 seconds** | < 15 s | stage 1 trimmed to the bone (measured file by file), fewer, larger files; H's delta updates for the same speed on every update |
| **M4: under 10 seconds (stretch)** | < 10 s | stage 1 split again: **1a** is only what draws the desktop and catches keys (`omawin.exe`, GlazeWM, the theme's files: a few MB), started the moment it lands, with the bar's painted strip in place; **1b** (the menu app, noctty, the font) follows seconds later, so the first key press finds it there. Compression tuned for unpacking speed (zstd where `tar.exe` has it), the bundle served from GitHub's release CDN with a fallback mirror |

## Things to explore

| To explore | Decides |
|---|---|
| Today's real numbers per step and per package (M0) | where to cut first |
| Stage 1's and stage 2's real sizes (MinGit, Neovim with plugins, the AOT menu app) | the budget, and what moves between stages |
| Windows' `tar.exe`: formats and speed on Windows 10 and 11 (zip, gzip, zstd) | the bundle's format |
| Defender's first-run scanning cost on ~hundreds of new files, signed vs unsigned | whether to merge files, sign everything, or both |
| The per-user GlazeWM without `uiAccess`: what it can't manage until the signed install lands | how early the background admin step runs |
| Whether the menu app still needs the .NET desktop runtime as AOT (roadmap 2.7) | whether the runtime leaves the critical path entirely |
| winget, Scoop and Chocolatey ids for every bundled tool, and their PATH placement | `omawin tools handover` |
| Publishing omawin to winget, Scoop and Chocolatey: their rules for a tool that installs per user and runs its own updater | the extra install channels |

## Risks

- **A slow connection** sets the floor: stage 1 is kept small for this reason, and the welcome page says how long stage 2 will take.
- **SmartScreen and Defender** on unsigned or new executables: CI signing (release-signing.md) is a prerequisite for M2.
- **Two copies of a tool** (bundle and a package manager) confusing people: `omawin tools` always says which runs and why; handover makes it one.
- **Portable copies drifting** from upstream: the scheduled bump job; `omawin tools` flags a bundled tool more than a release behind.
- **Seeded copies vs live configs**: the product / user config split must be designed with `omawin update`'s merge (mission-todo track 1), or a user's edits get overwritten.
