# Does the agents panel fit on its first open? (needs a test at the keyboard)

**The question:** after a bar restart, does the first click on the agents robot show the whole panel?

**Context:** the first open showed only the top strip (the bar's window grew to 90 px; the second open, to about 300 px). The cause: `draw()` in `zebar/tokyo-islands/widgets/agents.js` returned early while the panel was closed, and the click draws just before `show()`, so `fitWindow()` measured an empty box. Fixed in a844637: the panel is always filled. Not tested on screen: the fix was made while another account was the one on screen, and clicks only reach the account that is.

**To test:** `omawin update` (it restarts the bar), wait about 14 s, click the robot. The first open should be as tall as the second.

**Default meanwhile:** the fix is in main.
