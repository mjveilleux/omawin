# The bar

One strip across the top of the screen, as in Omarchy 4: the theme's background, 26 pixels tall. There is no dock: the launcher (Win+Space) and the system menu (Win+Alt+Space) take its place, and the Windows taskbar auto-hides (the very bottom edge or the Win key still shows it).

- **Left:**
  - **The menu**: Omarchy's logo. Click: the system menu; right-click: a terminal.
  - **Workspaces**: 1 to 5 always, and any others in use. The one on screen shows its layout instead of its number (a square: tiling; a triangle: its window floats); empty ones are dimmed. Click one to go there; click the one on screen to turn its tiling direction; the mouse wheel moves along them.
- **Middle:**
  - **Indicators**, only while they're on: a red, pulsing microphone while you dictate (Win+H) or omawin's agent listens (Ctrl+Win+H); a mug while the PC is kept awake (click it to stop). Hover the bar to see the mug faintly and click it to keep the PC awake.
  - **The clock**: "Saturday 22:58". Right-click for the date and week; click for Windows' calendar.
  - **An update arrow**, red, only while an omawin update is waiting (checked every 6 hours). Click it to update.
- **Right:**
  - **The record**: the music, podcast and radio player (below), while it plays.
  - **The tray**: an arrow; hover it and the tray's icons slide out beside it.
  - **Agents**: a little robot, red when you've used 90% of a Claude limit. Click it for your Claude Code limits (the 5-hour session, the week, and any per-model week), each with a meter and when it resets, and a line that turns through this week's numbers (tokens, today, the model you use most, your busiest day, prompts and sessions). It reads your Claude Code sign-in on this PC and asks Anthropic every 15 minutes; middle-click to check now, right-click to start Claude Code. `omawin` runs `bin/agent-usage.ps1 -Print` for the same in a terminal.
  - **Bluetooth**, **network** (wifi by strength, or ethernet), **sound** (the wheel changes the volume, right-click mutes, click for the sound page), **monitor** (the wheel changes the brightness), and **power** (the battery; right-click shows its percentage, click for sleep, restart and the rest). Bluetooth, network and monitor open Quick settings (Win+A).

The layout is `zebar/tokyo-islands/bar.json`: which widgets sit where, as Omarchy's `shell.json` does. Each widget is a file in `zebar/tokyo-islands/widgets/`.

## Music, podcasts and radio

The record at the right of the bar is a music, podcast and radio player. It wears the cover of what's playing and turns while anything plays; click it (or press **Win+M**) for the player.

- **Top:** the cover on a spinning record, the title, a strip of light that follows the sound, the progress bar (click to jump), and shuffle, back, play / pause, skip. Space plays / pauses and the arrow keys jump 10 s while it's open. A station is live: no progress bar, and `LIVE` where the time would be.
- **Music / Podcasts / Radio:** a switch. Each opens on a list: your playlists (newest added first), your shows (newest episode first), or your stations. Click a playlist or show for its tracks (click a track to play it; the arrow at the top goes back; double-click the name to rename it). Click a station to tune in. Right-click one in a list, then click it, to remove it. At the bottom, a box: paste a link and press Enter.
  - **Music** takes a YouTube playlist, a mix (a watch link with `&list=RD…`), or one long video; SoundCloud and Bandcamp work too.
  - **Podcasts** takes a YouTube playlist or show page, an RSS feed, or an Apple Podcasts link (Spotify keeps its podcasts to itself). A link that's none of these is refused with the reason. Episodes are newest first, and a show is read again for new ones when you open it after half an hour.
  - **Radio** takes a station's stream link (an `.mp3`, `.aac` or `.ogg` address: NTS, SomaFM and most stations publish one, and the box refuses a web page with that reason). The station names itself from what the stream says it is, so `NTS Radio`, `NTS Radio 2`. One station is one live stream: it plays until you stop it.
- Nothing is downloaded: the lists are saved in `Music\omawin` (podcasts in `Music\omawin\podcasts`, stations in `Music\omawin\radio`) and played as streams. YouTube mixes change each time YouTube makes them, so a mix is saved as it was when pasted.
- **Suno:** a fourth tab. Paste a Suno playlist or song link; it plays in Suno's own player, whose window opens beside the panel while you're on the Suno tab (click into it to browse Suno or sign in for your private playlists; Esc, the record or Win+M close the panel, and the music plays on). The panel's controls, the media keys and the quiet-while-you-dictate all work on it too. **Make a song**, at the top of the tab, opens Suno's create page in that window, made big: you make songs there with your Suno credits (signed in), and what's playing plays on. **Done making** puts it back small; add your new song's link, or a playlist it's in, to play it from the panel.
- **Stars:** under the artist, rate what's playing 1 to 5 (click a star, or press 1-5 while the panel is open; the same star again takes it away). 5 love it, 4 like it, 3 fine, 2 not for me (rarely played), 1 never again (skipped now, and left out of every playlist from then on). Skipping a track doesn't count against it. The stars are kept in `Music\omawin\ratings.tsv`, plain text you can edit.
- **Your station:** at the top of Music, **songs** or **jams** (the hour-long mixes): endless music picked from what you give 4 and 5 stars, plus what YouTube plays alongside it and your own playlists, never what you gave 1 star. Before you've rated anything, it starts from your playlists. `omawin music station [songs|jams]` from a terminal.
- **While you dictate** (Win+H), the music drops to 10% and comes back up gently when you stop.
- The media keys, the lock screen and Windows' volume flyout control it too. From a terminal: `omawin music add <url>`, `omawin music add podcasts <url>`, `omawin music add radio <url> [name]`, `omawin music list`, `omawin music toggle`, `next`, `prev`, `rate <1-5>`, `station`.

## Task manager

btop (btop4win, by btop's author): from the launcher, or `btop` in a shell. It follows the theme from its next start. Windows' own Task Manager is in the launcher too.

If the bar is missing or blank: `omawin restart zebar` (System menu › Update › Restart › Zebar).
