# A menu key while the menu app isn't listening

**Question:** with our GlazeWM fork, Win+Space, Win+Shift+V, Win+A and Win+F are `emit omawin <command>` (roadmap 1.2): GlazeWM tells the menu app over its connection, with no process started. If the menu app isn't running (crashed, being rebuilt by `omawin restart menu`), nobody hears the key and nothing happens. Should GlazeWM fall back to starting something?

**Context:** before, each key ran `omawin.exe`, which starts the menu app when it isn't running. Now the menu app is started at sign-in by GlazeWM (`omawin start`), reconnects within a second of coming back, and `omawin check` warns ("menu keys") when the fork emits and nothing listens. The fork knows whether anyone is subscribed to `custom` when it emits.

**Options:**
1. Nothing: the key does nothing until the menu app is back.
2. `emit --or-exec <command>` in the fork: when no subscriber is listening, GlazeWM runs the command instead (`emit omawin launcher --or-exec 'omawin.exe launcher'`), so a dead menu app is started by the next key.
3. The menu app restarting itself (a watchdog), so it's never gone for long.

**Default (done):** 1. Revisit if the PC ever shows a dead key; option 2 is a small change in the fork (`IpcServer` knows its subscriptions) and the renderer would write it.
