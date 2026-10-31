# Releases (prebuilt binaries)

A new PC shouldn't need the .NET SDK (a 1 GB dependency) or a compile to get `omawin.exe`, the bar's helpers, the menu app and the music player. So GitHub Actions builds them when a version is tagged, publishes them with the hashes, and `install.ps1` downloads the ones for that PC, checking each against the published hash before it is used. No release, no network, no GitHub sign-in: it builds here instead, exactly as before.

The build is not a second recipe. Every CI job runs the same script this PC runs — [`build/helpers.ps1`](../build/helpers.ps1), [`shell/menu-app/build.ps1`](../shell/menu-app/build.ps1), [`src/music/build.ps1`](../src/music/build.ps1), [`src/windows/build.ps1`](../src/windows/build.ps1) — with the tag added for the version stamp, so a release is the local build. The SDK is pinned once, in [`global.json`](../global.json) (9.0, any feature band), for CI and for this PC.

## What a release holds

[`build/release.ps1`](../build/release.ps1) is the one place the names live:

| File | What | Where install puts it |
|---|---|---|
| `omawin-helpers-<tag>.zip` | `omawin.exe`, `voice-meter`, `cpu-cores`, `win-keys`, `app-icons`, `theme-watch` | `~/.local/bin` |
| `SystemMenu-<tag>-win-x64.zip` | the menu app (WinUI 3), unpacked | `%LOCALAPPDATA%\system-menu` |
| `SystemMenu-<tag>-win-arm64.zip` | the same for ARM64, once ARM64 CI runs | `%LOCALAPPDATA%\system-menu` |
| `omawin-music-<tag>-win-x64.exe` | the music player, one exe | `~/.local/bin` |
| `omawin-music-<tag>-win-arm64.exe` | the same for ARM64 | `~/.local/bin` |
| `omawin-windows-<tag>-win-x64.exe` | Phase 8's fixes (`omawin who-has`, `health`, `startup`, `top`, `hosts`, `dns`, `proxy`, `connections`, `wifi share`, `clip`, `updates`, `disk`, `keyboard`, `pip`, `display`, …), one exe | `~/.local/bin` |
| `omawin-windows-<tag>-win-arm64.exe` | the same for ARM64 | `~/.local/bin` |
| `SHA256SUMS.txt` | `<sha256>  <file>` per asset: what a download is checked against | (never copied) |

The helpers are C# 5 from the in-box compiler: architecture-neutral IL, the same file on x64 and ARM64, so their ZIP is in every release. The menu app and the player are per architecture.

## Where it is built

[`.github/workflows/release.yml`](../.github/workflows/release.yml):

- **pushing a `v*` tag** builds and publishes a release. That is the only way a release is made.
- **a manual run** builds the same things and keeps them as workflow artifacts, so the workflow can be exercised (or a build checked) without tagging.

`meta` fixes the version, then `helpers` (in-box `csc`), and `menu`/`music` (a matrix over x64, and ARM64 when `vars.ARM64_RUNNERS` is `true`) build; `release` packs, and publishes with `gh release create --verify-tag`.

Actions are pinned by commit SHA — a tag can be moved, a SHA can't. Repo variables switch the parts that depend on the account or the plan:

| Variable | Default | It turns on |
|---|---|---|
| `ARM64_RUNNERS` | off | the `windows-11-arm` jobs. GitHub's ARM64 Windows runners are free for **public** repositories only; a release is x64-only until this is on |
| `ATTEST` | off | [artifact attestations](https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations) (provenance) for the assets. Needs a plan that has them |
| `CODESIGN` | off | signing, [docs/release-signing.md](release-signing.md) (needs an Azure account and a certificate profile) |

An ARM64 PC with an x64-only release builds its own menu app and player (a release with no `-win-arm64` asset is not an error: it falls back). Its helpers still download: they're architecture-neutral.

## What install does with it

[`bin/install-binaries.ps1`](../bin/install-binaries.ps1) runs from `install.ps1`'s helpers step, once per piece ([`install/helpers.ps1`](../install/helpers.ps1)). In order:

1. **what's here and current** is kept: an exe newer than its source, the menu app newer than `menu-app\`, the player newer than `music\`. So a release never replaces code you built from a newer checkout.
2. **the release's file for this PC**: `bin/release.ps1` resolves the release (newest, or `-Release <tag>`/`OMAWIN_RELEASE`), downloads the asset whose name matches, and refuses anything whose SHA-256 doesn't match `SHA256SUMS.txt`. A refusal is not an error: it falls through to 3.
3. **a build here**: a dev checkout, offline, no release, no GitHub sign-in, no SDK. `build/helpers.ps1`, `shell\menu-app\build.ps1`, `src\music\build.ps1`.

`-NoDownload` (or `OMAWIN_PREBUILT=0`) always builds; `-NoBuild` (`OMAWIN_PREBUILT=1`) never builds. The release this PC is on is recorded in `%LOCALAPPDATA%\launcher\release.txt`.

Downloads need no sign-in. `bin/release.ps1` still uses a token when there is one (`gh auth token`, `GH_TOKEN`/`GITHUB_TOKEN`), which raises GitHub's limit of 60 API calls an hour. A fork sets `OMAWIN_REPO` (or `-Repo`).

To tell a download from a build: the version stamp. `omawin --version` and `omawin-music --version` say the tag (`omawin v0.1.0`) or `(dev)` when it was built here, and every file carries it as file metadata too — `(Get-Item omawin.exe).VersionInfo.ProductVersion` for the helpers and the player, `SystemMenu.exe`'s for the menu app — so a downloaded file can be identified without running it.

## Checking a download by hand

```powershell
# what the release holds, and its hashes
gh release download v0.1.0 -R mjveilleux/omawin -p SHA256SUMS.txt -D $env:TEMP\omawin-rel
Get-Content $env:TEMP\omawin-rel\SHA256SUMS.txt

# fetch and verify one file (bin/release.ps1 does this, and this is the same thing by hand)
gh release download v0.1.0 -R mjveilleux/omawin -p 'SystemMenu-*-win-x64.zip' -D $env:TEMP\omawin-rel
Get-FileHash $env:TEMP\omawin-rel\SystemMenu-v0.1.0-win-x64.zip -Algorithm SHA256   # compare
```

`bin/release.ps1` also takes `-BaseUrl`: a folder or an http(s) URL holding a release's files, with its `SHA256SUMS.txt` as the index. That is a mirror, and it is how the release path is tested without publishing anything:

```powershell
# builds into scratch\built, packs scratch\release, installs from it into scratch\installed,
# and checks the refusal cases and the fallback. Writes only under scratch\
pwsh -File build\test-release.ps1
```## Animation checks

[`.github/workflows/animations.yml`](../.github/workflows/animations.yml) checks motion on a real Windows desktop (GitHub's Windows runners have one, and it can be recorded). It runs on pull requests and pushes that touch `shell/menu-app/`, `config/glazewm/`, `themes/render.ps1` or the check itself, and by hand (Actions › Animations › Run workflow), where `glazewm_ref` picks the GlazeWM fork's branch, tag or commit (default: its branch `claude/omawin-roadmap-devex-j1tmak`; a release tag once there is one). Three jobs, about 10 to 15 minutes in all:

- **`windows`: GlazeWM's window animations with omawin's config.** It builds the fork at that ref (cargo, cached), puts it where omawin looks for the tools bundle's copy with the features list its release ships (read from the fork's `scripts/omawin-release.ps1`), renders `config/glazewm/config.yaml` with `themes/render.ps1` exactly as a PC does (its startup commands emptied: they start `omawin.exe`, which isn't there), once with `%LOCALAPPDATA%\launcher\animations` on and once off, and runs the fork's `scripts/animation-check.ps1 -Config <rendered>` on each: Notepads open, one is closed through GlazeWM, one moves. It fails on an animation overlay left behind, a window not fully opaque or not where GlazeWM placed it, a close with no overlay (on), or any overlay at all (off).
- **`menu`: the menu app's motion** (roadmap 1.4). It builds the menu app (`build.ps1 -BuildOnly -Jit`: the ordinary .NET build, minutes quicker than the native one, and motion runs on the compositor either way) and runs [`src/motion-check`](../src/motion-check/), once with animations on and once off. The check starts its own menu app over a one-colour backdrop, opens the launcher the way Win+Space does (the request file and signal, as `omawin.exe` sends them) twice to warm up, then records the launcher's place at about 60 frames a second (GDI) while it opens and while Esc closes it. Each frame's level is how far it has gone from the screen without the menu (0) to the menu settled (1), which for a fade is its opacity. With animations on, the opening must settle within `Motion.Enter` plus the cascade's last tile (552 ms) and the closing within `Motion.Exit` (130 ms), each plus a 100 ms budget for a software-drawn runner, with frames seen in between; off, each must be one jump (at most one frame caught mid-draw). Windows' own Animation effects switch is turned on for both runs (off is omawin's toggle alone), and put back after.
- **`analysis`**: the frame analysis, unit-tested on Linux with made-up frames (`test/motion-check.Tests`).

Each job writes its result to the run's summary page and uploads its files: `window-animations` (each run's frames, `checks.txt`, `events.txt`, GlazeWM's logs and the config it ran) and `menu-motion` (`summary.md`, `levels.csv` with every frame's level, the PNG frames around each change, and `open-sheet.png` / `close-sheet.png`, every frame in order with its level as a bar: green arrived, red gone, amber on the way). The fork has its own copy of the window check (its `animation-check.yaml`, with its own test config, slowed, and videos).

## Limits, honestly

- **Not byte-for-byte reproducible.** CI and a local build run the same commands, but a .NET publish embeds timestamps, so two builds of the same commit differ in bytes. What is verified is that the file a PC installs is the file that release published (the manifest), not that any two builds match.
- **A release can be older than the checkout.** `install.ps1` runs on whatever commit it finds; when a piece isn't current it takes the release, which may be behind a checkout that is ahead of it. For a PC that isn't developing, that is the stable-behaving choice; `OMAWIN_PREBUILT=0` builds instead. `omawin update` following release tags (channels) is the next step in [mission-todo.md](mission-todo.md), track 1.
- **The ARM64 jobs need a public repo** (or paid ARM64 runners); until then ARM64 PCs need the SDK for the menu app and the player.
- **The GlazeWM and noctty forks are not in these releases**: they live in their own repositories, with their own releases and pinned hashes ([`install/glazewm-fork.ps1`](../install/glazewm-fork.ps1), [`bin/noctty.ps1`](../bin/noctty.ps1)).
