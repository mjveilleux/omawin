# Security

omawin runs on developers' PCs, which hold keys, tokens and work, so a security bug matters more than most.

## Reporting a vulnerability

Please don't open a public issue. Use GitHub's private reporting instead: the repository's **Security** tab, then **Report a vulnerability**. Say what you found, how to reproduce it, and what it lets someone do.

You'll get an answer as soon as possible. A fix ships in a release, and the report is credited unless you'd rather it wasn't.

## What omawin trusts

How each thing omawin downloads or runs is checked (the bootstrap, the release, packages, themes, plugins, your own folders) is in [docs/plan/09-for-everyone.md](docs/plan/09-for-everyone.md#1-security-model). In short:

- Install, update and everyday use never ask for admin; nothing elevated runs in the background.
- Releases are signed, and downloads are checked against the release's SHA-256 sums.
- A theme is data: palettes, pictures and colour files, never scripts or programs.
- Plugins are pinned to a commit; an update that asks for a new permission waits for your yes.
- omawin sends nothing anywhere on its own (no telemetry).
