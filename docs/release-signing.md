# Release signing (designed, off by default)

Windows warns about any program it hasn't seen before, and a downloaded release is exactly that: the first run of `omawin.exe` or `SystemMenu.exe` shows "Windows protected your PC" until the file gets reputation. Signing with a certificate Windows trusts is what replaces that with a publisher name.

The step exists in [`.github/workflows/release.yml`](../.github/workflows/release.yml) and is **off until `CODESIGN` is `true`**. Nothing below is wired to a real account: the values are placeholders, and no secret is in the repo.

## Why not the self-signed certificate the fork install makes

`install/glazewm-fork.ps1` makes a certificate on your PC and trusts it there: fine for something built on the machine that runs it, useless for a file someone else downloads. A release needs a certificate from a CA that Windows already trusts.

## The plan: Azure Trusted Signing

Azure Trusted Signing (now "Artifact Signing") is the cheapest route for a small project: a Microsoft-issued certificate from a certificate profile, no USB token, sign from CI. Requirements:

- an Azure subscription (the account costs about $10 a month, in addition to per-signature usage),
- a Trusted Signing account and a certificate profile in one of its regions,
- **identity validation** of the publisher, which is the slow part: an organization (with a verifiable legal presence) gets a Public Trust profile; an individual can get an Individual profile, which is issued under the person's name and reviewed by hand.

SmartScreen reputation still starts at zero for a new certificate: the warnings fade as downloads accumulate, over weeks. Signing is what makes that possible, not what makes it instant.

## What the step does

In the `release` job, after the artifacts are downloaded and **before** they are packed, so the ZIPs contain signed binaries:

- `vars.CODESIGN_ENDPOINT`: the Trusted Signing account's regional endpoint,
- `vars.CODESIGN_ACCOUNT`: the account name,
- `vars.CODESIGN_PROFILE`: the certificate profile name,
- `secrets.AZURE_TENANT_ID`, `secrets.AZURE_CLIENT_ID`, `secrets.AZURE_CLIENT_SECRET`: an app registration allowed to sign with that profile.

The action is `azure/trusted-signing-action`, pinned by commit SHA like the others. Two things to check when turning it on, since they are the account's setup, not the workflow's:

- **Authentication.** The three secrets above are the simplest; workload identity federation (OIDC: an `azure/login` step with `id-token: write`, no secret at all) is better where it's set up. Either way, the values live in the repository's variables and secrets, never in the repo.
- **What gets signed.** `files-folder: out` with `files-folder-recurse: true` and `files-folder-filter: exe` — every exe of ours (the helpers, `SystemMenu.exe`, `omawin-music.exe`). The framework's DLLs are Microsoft's and stay as they are; `fff_c.dll` is another project's build, so it is left unsigned too.

The order matters: pack → sign would sign nothing, and a manifest written before signing would describe bytes that then change. So it is: artifacts → **sign** → pack → `SHA256SUMS.txt` → publish. `build/release.ps1 -NoManifest` and `-OnlyManifest` exist for exactly this split.

## Turning it on

1. Create the Azure Trusted Signing account and certificate profile; finish identity validation.
2. Register an app with permission to sign with that profile; note the tenant, client id and secret.
3. Repository → Settings → Secrets and variables → Actions:
   - variables: `CODESIGN_ENDPOINT`, `CODESIGN_ACCOUNT`, `CODESIGN_PROFILE`, and `CODESIGN` = `true`,
   - secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.
4. Tag a version, then check a downloaded file: `Get-AuthenticodeSignature <file>` should say `Valid`, with the publisher name in `SignerCertificate.Subject`.

Unsetting `CODESIGN` returns to unsigned releases: the workflow, the manifest and the install path are the same either way.

## What this does not cover

- The GlazeWM and noctty forks: their own repositories, their own releases and certificates.
- The ZIP files themselves (an unsigned container; what matters is what's inside), and `SHA256SUMS.txt`.
- arm64 assets until the ARM64 jobs run: whatever a release holds is what gets signed.
