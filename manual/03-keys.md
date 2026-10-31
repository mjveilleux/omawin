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

## Caps Lock, the Copilot key and snippets

`omawin keyboard` (**System menu › This PC › Keyboard**) says what your keys do differently, all off until you choose:

- **Caps Lock** (search *Caps Lock key* in the launcher, or `omawin setting set input.caps-lock escape`): Escape, Control, or *Escape tapped / Control held* (tap it alone for Escape; hold it with another key and it's Control). Live, no restart, no admin. omawin sends the new key, so GlazeWM's own bindings don't see it as Control; for that, and for the sign-in screen and windows running as admin, use Windows' own remap: `omawin keyboard signin caps-escape` (or `caps-control`, `swap-caps-control`, `caps-off`), which needs admin once and a restart; `omawin keyboard signin off` puts back whatever was there before.
- **The Copilot key** (*The Copilot key opens*, `omawin setting set input.copilot-key launcher`): opens the launcher instead of Copilot. A plain F23 from a macro keyboard is left alone.
- **Text snippets**: lines in `~/omawin-account/snippets.txt` (and the shared folder's `snippets.txt`, for every account): a trigger, ` | `, then the text. Type the trigger anywhere and it's replaced:

  ```
  ;sig | Best,\nYour Name
  ;date | {date}
  ;ts | {date} {time}
  ;cb | > {clipboard}
  ```

  `\n` is a new line, `\t` a tab; `{date}`, `{time}` and `{clipboard}` (the newest copied text) are filled in. A trigger needs two characters at least; one that starts with a character you never type otherwise (`;`) never fires by accident. The file is read again when you save it. `omawin keyboard` lists them and warns about a trigger that can never fire because a shorter one fires first (`;d` before `;date`). Snippets can't type into a window running as admin (Windows doesn't let a normal program).

Nothing listens to your keys unless one of these is on, or Quick Look (below) while File Explorer is in front; with all of them off, omawin has no keyboard hook at all.

## Quick Look in File Explorer

Select a file in File Explorer and press **Space**: it opens in front, as on a Mac. Pictures, text and code are drawn by omawin, in your theme; a folder shows what's in it; Word, Excel, PowerPoint, PDFs and anything else your programs can preview in Explorer's own preview pane are drawn by that same preview (it runs in a process of its own, so a bad one can't take the bar down); anything else shows its thumbnail. The name, kind, size and date are on top.

- **Space** again or **Esc** closes it, and File Explorer has your keys again. Clicking elsewhere closes it too.
- **← / →** step through the other selected files.
- **Enter** opens the file in its app.

Space is only taken in Explorer's file list: renaming a file, the address bar, the search box and the folder tree get their Space as always, and so does Space while you're typing a name to jump to it (`my file`). With nothing selected, Space does what Explorer does. omawin listens only while File Explorer is the window in front. To turn it off: the setting **Quick Look: Space previews the selected file** (search *Quick Look*), or `omawin setting set explorer.quick-look off`.
