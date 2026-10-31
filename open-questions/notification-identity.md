# Package identity without Developer Mode: how should omawin's sparse package be signed?

**Question.** The notification history (roadmap 8.22) needs package identity, which omawin gets from a sparse package (`shell/menu-app/identity/AppxManifest.xml`). Windows registers an *unsigned* manifest only with Developer Mode on. How should a PC without Developer Mode get it?

**Context.**
- Built: `omawin notifications identity on` copies the manifest to `%LOCALAPPDATA%\launcher\identity\`, draws its logos, and registers it for this account with the menu app's folder as its external location (`PackageManager.RegisterPackageByUriAsync`, `DeveloperMode = true`): no admin. `app.manifest`'s `<msix>` element ties `SystemMenu.exe` to it; until it's registered that element does nothing. Without Developer Mode it stops and says so; nothing changes.
- Developer Mode is a machine setting (admin, catalog line `developer.dev-mode`), which many developers have on anyway, but it shouldn't be the only way.
- The other ways all need a signature Windows trusts:
  1. **Signed in CI** with the release certificate (docs/release-signing.md, Azure Trusted Signing): `makeappx pack /d identity /p omawin-shell.msix /nv`, then sign; the PC adds it with `Add-AppxPackage -Path omawin-shell.msix -ExternalLocation <menu folder>`, per user, no admin. But the manifest's `Publisher` must equal the certificate's subject exactly, and that subject is the publisher's legal name: the repo can't carry it (AGENTS.md: nothing personal). CI would write it into both manifests at build time from a repository variable (`CODESIGN_SUBJECT`), so local builds stay `CN=omawin`.
  2. **A self-signed certificate made on the PC**, trusted in `LocalMachine\TrustedPeople` inside the one admin prompt (as `install/glazewm-fork.ps1` does for the fork). Works offline, but adds a machine-wide trust for a key that sits in this account's store.
  3. **Windows 11's unsigned packages** (`Publisher` with the `OID.2.25.311729368913984317654407730594956997722=1` attribute, registered with `AllowUnsigned`): **(check)** which Windows builds allow it without admin, and whether `userNotificationListener` is allowed in an unsigned package.
- Also to check on a PC (🖥): that `%LOCALAPPDATA%\launcher` still resolves to the real folder with identity (`mediumIL` + `unvirtualizedResources` should mean no redirection), that `NotificationChanged` fires for a desktop app with identity (if not, the history reads the list every 10 seconds, already built), and the Copilot-key provider registration this identity would also allow (8.23).

**Options.** 1, 2 or 3 above (1 is the clean one once releases are signed).

**Default taken:** Developer Mode only, for now; the page and `omawin notifications` say what's missing. When release signing is turned on, option 1 is the next step: a CI job packs and signs `omawin-shell.msix` with the publisher from the repository variable, and `identity on` adds that instead of the loose manifest when Developer Mode is off.

Delete this file once answered.
