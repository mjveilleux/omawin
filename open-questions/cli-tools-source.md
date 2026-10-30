# Where should the command-line tools come from?

**Question:** to make install fast ([docs/roadmap.md](../docs/roadmap.md), phase 2), the ~12 portable command-line tools (ripgrep, fd, fzf, eza, bat, jq, lazygit, zoxide, starship, fastfetch, …) stop going through winget, which spends seconds per package before downloading. What fetches them instead?

**Options:**
1. **One bundle zip from our release workflow**: pinned versions, one download, checked against `SHA256SUMS.txt`, unpacked into `~/.local/bin`. Fastest and fully offline-cacheable; we redistribute the tools (all MIT / Apache / Unlicense; notices in the zip) and bump versions ourselves.
2. **mise** (`jdx.mise`) with its GitHub-release backends: parallel, pinned in a `mise.toml`, the same tool Omarchy uses and that the dev-env work (phase 4) needs anyway. One winget package (mise itself) plus GitHub's rate limit for anonymous downloads.
3. **Stay on winget**, more at once. Simplest; still the slowest.

**Default:** 2, since dev environments need mise anyway; fall back to 1 if GitHub's rate limit or mise's Windows support gets in the way on the clean-PC test.
