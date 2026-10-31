# The music player and the .NET runtime: self-contained, or the runtime per user?

**Context.** Roadmap 2.7: the .NET desktop runtime has left the core (the menu app is Native AOT, nothing else in a default install needs it). The music player (`src/music/`, `omawin-music.exe`) is still framework-dependent, so the `music` preset (the Music plugin's programs) brings `Microsoft.DotNet.DesktopRuntime.9` from winget, which installs for the whole PC: picking music raises the admin prompt (2.9). Every install downloads the player itself, preset or not (`bin/install-binaries.ps1 -What all`).

**Options.**
1. Publish the player self-contained (`<SelfContained>true</SelfContained>`, single file, compressed): no runtime, no prompt; the exe grows from a few MB to ~45-50 MB, which every install downloads unless the player is fetched only with the preset (and the bar's music cell then comes with the plugin).
2. Self-contained, and fetched only when the music preset or plugin is on: no cost for anyone else. Needs the bar to cope with no player (it does for the plugin path).
3. The runtime per user, no admin: Microsoft's `dotnet-install` layout in `%LOCALAPPDATA%\Microsoft\dotnet` as an optional bundle entry, with `DOTNET_ROOT` set for the account.
4. Leave it: the music preset is opt-in, and its one prompt is a choice you made.

**Default went with:** 4 for now (the music default itself is still open: [music-default.md](music-default.md)); 2 is the recommendation once that's settled.
