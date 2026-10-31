# Music (`omawin.music`)

omawin's music, podcast and radio player in the bar, written as a Quickshell plugin in Omarchy's format (`manifest.json`, QML), so the same plugin runs in Omarchy's shell and in omawin's QML host ([roadmap](../../../plans/roadmap.md) 4.6 and 4.9).

| File | What |
|---|---|
| `manifest.json` | Omarchy's plugin manifest: a `bar-widget`, its settings (show the title in the bar, its width) |
| `BarWidget.qml` | the bar: a glyph for what's playing and its title; click for the panel, right click to play or pause, the wheel or middle click to skip. It runs `omawin-music watch` and hands the panel what it says |
| `Panel.qml` | the panel: cover, title, position and seek, the controls, shuffle, your stars, the player's volume, and the playlists, podcasts and stations (Music, Podcasts, Radio) |
| `MusicModel.js` | the pure parts: reading the watch's lines, the tabs, the clock |

The player itself is `omawin-music` ([`src/music/`](../../../src/music/)): it plays through mpv and yt-dlp, keeps your library in `~/Music/omawin`, and takes every command the panel sends (`play <slug>`, `toggle`, `next`, `prev`, `seek <s>`, `rate <1-5>`, `shuffle`, `volume <0-1>`).

## What the watch says

`omawin-music watch` prints one line per change ([`src/music/Watch.cs`](../../../src/music/Watch.cs)):

- `S {json}`: the player's state (`running`, `state`, `live`, `kind`, `playlist`, `title`, `artist`, `thumb`, `stars`, `position`, `duration`, `shuffle`, `volume`, `error`)
- `P [json]`: the playlists, podcasts and stations
- `L <0-1>`: the speakers' level while something plays
- `O open` / `O close`: Win+M (`omawin music open`)

The widget writes `panel 1|0` and `focus 1|0` to its stdin, so Win+M closes an open panel or opens the focused monitor's.

## Installing it

`omawin plugin add music` (or Install › Plugins › Music) turns it on and installs the programs it needs: yt-dlp, mpv, the .NET runtime (the `music` preset in `install/packages.ps1`, tagged with this plugin). It shows in the bar once omawin's QML host runs plugins (roadmap 4.9.4); until then Win+M says the panel is on its way.
