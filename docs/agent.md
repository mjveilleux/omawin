# The omawin agent (Ctrl+Win+H)

Ask the desktop for something in a sentence and omawin's agent does it: "add Hardcore History to podcasts", "make the clock 12-hour", "open an issue: the dock forgets its windows after sleep". It runs in this repo, so AGENTS.md and the docs are its context, and `omawin.exe` (every action has a command) is its main tool. Said with Ctrl+Win+H, it works in the open in herdr on workspace 9 and may edit omawin (quick changes); typed with `omawin agent ask`, it works in the background and answers in one line.

## How it works

```
login ─> GlazeWM's startup commands ─> omawin.exe agent start (agent/start.ps1)
   the herdr session "omawin-agent", headless; its tab "opencode" runs agent/server.ps1, which
   runs OpenCode's background service there (and takes it over from a hidden one a client started)
Ctrl+Win+H ─> omawin.exe agent listen ─> omawin-listen.exe (agent/omawin-listen.cs, native: no wait)
   records the microphone; nothing opens; the bar's pill turns teal, "omawin is listening"
   (voice-meter prints 2 while omawin-listen.exe has the mic)
Ctrl+Win+H again ─> a second omawin-listen.exe signals the first and exits; the first writes
   request.wav and starts agent/listen.ps1 -Wav, hidden:
   1. handy --transcribe-file: Handy's model turns it into words (~2 s to load, then fast)
   2. herdr session "omawin-agent": started as at login if it isn't running; a new tab in this
      repo running agent/live.ps1 with the words
   3. a terminal attached to the session, if none is open; a GlazeWM rule moves it to workspace 9
live.ps1 ─> the backend's Invoke-AgentLive: OpenCode runs it as agent "omawin-live" (the allow
   list, plus file edits: config.json live.edit), a client of the service in the "opencode" tab,
   then opens OpenCode on the same session
omawin agent open ─> agent/open.ps1: the session's window, and workspace 9
omawin agent ask <request> ─> agent/ask.ps1: the backend's Invoke-AgentBackend, agent "omawin",
   no edits, the one-line answer in %LOCALAPPDATA%\launcher\agent\state
```

**Why its own recorder, not Handy's key.** Handy types whatever it hears into the focused window (its `paste_method` is one setting for every key), and this is for the agent, not that window. So `omawin-listen.exe` records and Handy only transcribes the file (`handy --transcribe-file … --json`), with the same model and nothing typed anywhere. A press shorter than 0.25 s is dropped; `omawin agent cancel` drops a recording; one left on stops by itself after 3 minutes.

## Backends and models (`agent/config.json`)

```json
{
  "backend": "opencode",
  "backends": { "opencode": { "model": "opencode/deepseek-v4.1-flash" }, "claude": { "model": "" } },
  "allow": { "commands": ["omawin.exe", "omawin-music", "yt-dlp", "gh issue", "..."], "read": true, "web": true }
}
```

- **`backend`** picks `agent/backends/<name>.ps1`; each backend's block holds its own settings, such as the model. Your user layer's `agent/config.json` ([work.md](work.md)), if there is one, goes on top: `{"backend": "claude"}` switches backend, `{"backends": {"opencode": {"model": "opencode/glm-5.3-flash"}}}` switches model.
- **`allow`** is what the agent may do, the same for every backend: these commands (and their arguments), reading files, the web. Everything else is refused, never asked: nobody is there to answer, so the agent says what it needed in its answer.
- **A backend** is one function, `Invoke-AgentBackend`: it turns `allow` into its own rules, runs the request with the system prompt, calls `OnStep` for each tool call (the bar's "working: …") and returns the answer, the cost and the command that carries on with the run in a terminal. `ask.ps1` does the rest: the config, the prompt (agent/prompt.md plus what `allow` lets it do), the state, the history.

**OpenCode** (`backends/opencode.ps1`, the default). OpenCode v2 keeps a background service running; `opencode run` is only a client of it, so a request starts in about a second. Left alone, a client starts that service hidden whenever it finds none; here it runs in the open instead, in the herdr session's "opencode" tab (`agent/server.ps1`), where you can see it and restart it (Ctrl+C: it starts again). `opencode serve --service` exits at once when a service is already up, so `server.ps1` stops a hidden one and takes over, a few tries at most, since open OpenCode windows restart one as soon as it's gone; it then waits rather than fights. The service reads its agents from the project, so the agent `omawin` (the prompt, `allow` as permission rules) is written to `.opencode/opencode.json` here (git-ignored) whenever it changes. What we learned:

- The model has to go in with `-m`: an agent's own `model` is ignored.
- OpenCode's free models (`*-free`) refuse anything but OpenCode's own agents ("free tier can only be used from within OpenCode"), so the default is a cheap Zen model: `opencode/deepseek-v4.1-flash`, about $0.002 a request.
- Its shell tool is `shell`, and each part of a chained command is checked on its own, so the prompt asks for one plain command at a time.

**Claude Code** (`backends/claude.ps1`): `claude -p` with the prompt appended, `--permission-mode dontAsk` and `allow` as `--allowedTools`. About $0.20 a request.

## State (`%LOCALAPPDATA%\launcher\agent\`)

- `state`: one JSON line for the bar: `working` / `done` / `failed`, the step or answer, the request, the backend.
- `runs\<time>-<backend>.jsonl`: the backend's whole stream.
- `history.jsonl`: one line per request: the answer, the seconds, the cost and `resume`, the command that carries on with it (`opencode -s <session>`, `claude --resume <session>`, in this repo).

## Rules it works by (agent/prompt.md)

Never drive the desktop, nothing private in an issue, the user layer's README, never commit unless asked. In the background it never edits the repo and ends with one line for the bar; live (Ctrl+Win+H) it makes quick changes here following AGENTS.md, and hands bigger work to a worktree with its own session (`omawin.exe launch terminal --dir … -- claude "<task>"`). `common.ps1` adds the part for each mode.

## Steps

1. Done: `omawin agent ask <request>`, in the background.
2. Done: Ctrl+Win+H: `omawin-listen.exe`, the bar's "omawin is listening", Handy's transcription, the herdr session on workspace 9.
3. The bar showing the agent working and its answer (the `state` file has them; `voice-meter` could pass them on).
4. Done: OpenCode's service at login, in the herdr session (`omawin agent start`), and `omawin agent open`.
5. The music player ducking while omawin listens (`music/Dictation.cs` knows only Handy).
