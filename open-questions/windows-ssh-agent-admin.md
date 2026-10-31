# Windows' OpenSSH agent: turn it on in the admin step?

**Context.** Roadmap 3.9: Linux reaches Windows' SSH agent through npiperelay and socat (`wsl/setup.sh` `ssh_agent_bridge`, started by `config/bash/bashrc`), so one key works on both sides. 1Password's agent serves the same pipe and needs nothing. Windows' own `ssh-agent` service is disabled by default, and turning it on (`Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent`) takes admin.

**Options.**
- **An opt-in line in the one admin step** (`install/admin-batch.ps1`), shown with what it changes, when the user asks for SSH (a preset, or a row in Install).
- **Leave it to the user** (`omawin check` warns that the bridge has no agent behind it, with the command to run as admin).

**Default meanwhile:** nothing turns the service on. The bridge is set up either way; with no agent running, `ssh-add -l` says it can't connect and plain `ssh` still works with keys in `~/.ssh`.
