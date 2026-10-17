# The bar and dock

## Bar (top)

Three floating islands:

- **Left: workspaces**, with the icons of their apps. Right-click one to name it (or Alt+N).
- **Middle: now playing and the clock.** While the bar's own player plays, the title gives way to slow ribbons of light that swell with the sound. While you dictate, the clock becomes a glowing pill whose bars ripple out from the center with your voice.
- **Right:**
  - **The record**: the music, podcast and radio player (below).
  - **CPU**: its total; click it for a card with every core's load.
  - **RAM**: an icon and a ring that fills with usage; hover for used / free / total / page file.
  - **Network** and **battery**.
  - **The tray**: a round button with a count, which drops the tray down as a column of big icons; hover one for its status. Click outside or Esc closes it.

While a VPN is connected, **VPN** and the first word of its name show next to the wifi icon.

## Music, podcasts and radio

The record left of the CPU island is a music, podcast and radio player. It wears the cover of what's playing and turns while anything plays; click it (or press **Win+M**) for the player.

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

## Dock (bottom)

It takes the place of the Windows taskbar, which auto-hides: the mouse at the very bottom edge, or the Win key, still shows the real one. Three islands in the theme's colors:

- **Left:** Start, Search, File Explorer, Settings, Task manager (btop), the launcher and the system menu
- **Middle:** every open window as its app icon, grouped by workspace. The focused one is underlined; other workspaces' are dimmer. Click to go to it; hover for its title.
- **Right:** quick settings, notifications, show desktop

## Task manager

btop (btop4win, by btop's author): from the dock, the launcher, or `btop` in a shell. It follows the theme from its next start. Windows' own Task Manager is in the launcher too.

If the bar is missing or blank: `omawin restart zebar` (System menu › Update › Restart › Zebar).
