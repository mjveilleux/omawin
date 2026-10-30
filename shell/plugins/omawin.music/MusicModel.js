.pragma library

// What `omawin-music watch` prints, one line at a time (src/music/Watch.cs):
//
//   S {json}   the player's state: running, state (playing | paused | loading | stopped), live,
//              kind, playlist, playlistTitle, index, count, title, artist, thumb, stars,
//              position, duration, shuffle, volume, error
//   P [json]   the playlists and podcasts: kind, slug, title, count, thumb, updated, latest
//   L 0.123    the speakers' level while music plays (the bar's equalizer)
//   O open     Win+M (`omawin-music open`): open this bar's panel, or close it
//
// Pure functions, so they can be checked outside the shell.

// The panel's tabs. A playlist's kind (src/music/Library.cs) is music, suno, podcasts or radio;
// Suno's songs sit with the music.
var kinds = [
  { value: "music", label: "Music" },
  { value: "podcasts", label: "Podcasts" },
  { value: "radio", label: "Radio" }
]

// The tab a kind is shown under (a station plays from the music).
function tabOf(kind) {
  return kind === "podcasts" || kind === "radio" ? kind : "music"
}

// One line, as { type, value }; null for a line it doesn't know (a newer player's).
function parseLine(line) {
  var text = String(line || "")
  if (text.length < 3 || text.charAt(1) !== " ") return null
  var type = text.charAt(0)
  var rest = text.substring(2)
  try {
    if (type === "S") return { type: "state", value: JSON.parse(rest) }
    if (type === "P") return { type: "playlists", value: JSON.parse(rest) || [] }
  } catch (e) {
    return null
  }
  if (type === "L") {
    var level = parseFloat(rest)
    return isFinite(level) ? { type: "level", value: Math.max(0, Math.min(1, level)) } : null
  }
  if (type === "O") return rest === "open" || rest === "close" ? { type: "open", value: rest === "open" } : null
  return null
}

// The playlists under one tab, newest first (a podcast by its latest episode).
function ofKind(playlists, tab) {
  var list = (playlists || []).filter(function(p) { return p && tabOf(p.kind) === tab })
  list.sort(function(a, b) {
    var x = String(b.latest || b.updated || ""), y = String(a.latest || a.updated || "")
    return x < y ? -1 : x > y ? 1 : 0
  })
  return list
}

// 0:07, 3:42, 1:02:03.
function clock(seconds) {
  var s = Math.max(0, Math.floor(Number(seconds) || 0))
  var h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, r = s % 60
  var two = function(n) { return n < 10 ? "0" + n : String(n) }
  return h > 0 ? h + ":" + two(m) + ":" + two(r) : m + ":" + two(r)
}

// The bar's text: the title (and artist), or nothing when the player isn't running.
function barText(state) {
  if (!state || !state.running || state.state === "stopped" || !state.title) return ""
  return state.artist ? state.title + " · " + state.artist : state.title
}

// The bar's glyph (Nerd Font): what the player is doing.
function glyph(state) {
  if (!state || !state.running) return "\u{F075A}"          // nf-md-music_note
  if (state.state === "playing") return state.live ? "\u{F0439}" : "\u{F03E4}"  // radio / pause
  if (state.state === "loading") return "\u{F01D8}"       // nf-md-dots_horizontal
  return "\u{F040A}"                                      // nf-md-play
}
