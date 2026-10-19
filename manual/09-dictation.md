# Dictation (Win+H)

**Win+H** starts local speech-to-text with [Handy](https://handy.computer); tap again to stop and the text is typed where you are (straight in, not pasted: the clipboard is left alone, and it works in terminals too). The bar's clock turns into a pill that ripples with your voice while it listens.

GlazeWM catches Win+H, so Windows' own voice typing popup (which can't be hidden or restyled) never opens. Handy shows no overlay and works as tap to toggle. Its own keys are parked on Ctrl+Alt+Shift+F9/F10, out of the way.

It needs a speech model: the install downloads Parakeet V3 (English, about 460 MB) unless you've picked one in Handy; to change it, open Handy from the tray. Everything stays on the PC. `omawin check` says when there's none, since Win+H then does nothing at all.

## Ask omawin (Ctrl+Win+H)

**Ctrl+Win+H** talks to omawin's own agent instead of typing. Nothing opens: the bar's pill turns teal and says **omawin is listening**. Say what you want ("add Hardcore History to podcasts", "make the clock 12-hour", "open an issue: the dock forgets its windows"), then press **Ctrl+Win+H** again. Your words go to the agent, which works in the open on **workspace 9**, in a herdr session called `omawin-agent`: a tab per request, where you can watch it and carry on talking to it. `omawin agent cancel` drops a request you've started.

The session starts at login, without a window, with OpenCode's server running in its first tab (**opencode**), so a request starts in a second. `omawin agent open` shows it on workspace 9 any time; the window also comes up there by itself with a request.

It makes quick changes to omawin itself (configs, keys, menu rows, small fixes), runs omawin's commands and the music player, opens issues, and starts a branch with its own session for bigger work. It runs on OpenCode with a cheap model; `agent/config.json` picks the service and the model. See [docs/agent.md](../docs/agent.md).

`omawin agent ask <request>`, typed in a terminal, does a request in the background instead and answers in one line.
