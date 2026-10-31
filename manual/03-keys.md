# Keys

The full, searchable list is in the launcher: **Win+Space → Shortcuts** (`default/launcher/keys.txt`). The ones to remember:

| Keys | Does |
|---|---|
| `Win+Space` | launcher: your items + every installed app, Projects, Localhost, Clipboard, Shortcuts |
| `Win+Alt+Space` | system menu: themes, install / remove apps, update, configs, power |
| `Win+Enter` / `Alt+Enter` | terminal in the focused window's folder / in ~ |
| `Win+F` | file finder, with a preview (`Win+Shift+F` too) |
| `Win+Shift+S` | screenshot: drag a region (click: a window; Enter: everything) |
| `Win+Shift+T` | text from the screen: drag over it, its text is copied |
| `Win+Shift+V` | clipboard history (Enter pastes) |
| `Win+A` | audio: every speaker and microphone and what plays on each (↑ ↓: move, ← →: volume, Space: mute, Enter: use it / send an app to another speaker). Quick settings (Wi-Fi, Bluetooth, brightness, power mode…) is in the launcher and the system menu |
| `Win+M` | music, podcasts and radio: the player in the bar (Space plays / pauses) |
| `Win+Ctrl+Space` | next background for the current theme |
| `Win+H` | dictate: tap to start, tap again to stop |
| `Win+Arrows` / `Alt+H J K L` | focus a window; add `Shift` to move it |
| `Win+1..9` | go to workspace |
| `Win+Shift+1..9` | send the window to a workspace and go with it |
| `Win+Shift+Alt+1..9` | send the window to a workspace, stay here |
| `Alt+N` | name the current workspace |
| `Alt+F` / `Alt+Shift+Space` | fullscreen / float |
| `F11` / `Ctrl+Enter` | fullscreen the terminal (noctty) |
| `Alt+R` | resize mode (H J K L, Enter/Esc to leave) |
| `Alt+Shift+Q` | close the window |
| `Ctrl+Space`, then `1..9` | herdr (project windows): tab N; `Ctrl+Space c` new tab |
| `Alt+Shift+R` | reload GlazeWM |

Workspaces are plain numbers you use per project; chat apps (Teams, Slack, Discord, Outlook) open on 9. Small dialogs and the launcher float.

New windows go where Omarchy puts them: each takes half of the focused window, turning 90 degrees each time. Four terminals in a row give A on the left, B top right, and C beside D below B.

The keys live in `config/glazewm/config.yaml`. Each one runs an omawin command (see [The omawin command](02-omawin-command.md)). If you change a key, update `default/launcher/keys.txt` too.
