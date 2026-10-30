# Phase 1: snappy, how to build it

The implementation side of [roadmap.md](roadmap.md) Phase 1 (1.1-1.10). Read the roadmap item first for the why; this file is the how: files, code, checks.

**All code here is a sketch that hasn't been compiled or run.** It names real files and functions as of 2026-10-30 (line numbers drift: search for the function name). Anything uncertain about a Windows, WinUI or crate API is marked *(check)*. Languages, by where the code lives (AGENTS.md):

| Where | Language |
|---|---|
| `menu-app/` | C# 12, .NET 9, WinUI 3, **Native AOT safe**: no reflection, JSON through `Json.cs`'s source-generated context, COM by vtable (as `AppIcons.cs`) |
| `bin/omawin.cs` | **C# 5 only** (in-box `csc.exe`): no `$""`, `?.`, `=>` members, `nameof`, `out var` |
| GlazeWM fork (`mjveilleux/glazewm`, branch `omawin`) | Rust; doc comment on every fn; `anyhow` outside `wm-platform`, `crate::Result` inside; tests in `#[cfg(test)]` |
| `install/`, `migrations/`, `themes/render.ps1`, scripts install runs | PowerShell **5.1-compatible** |
| `.github/workflows/` | YAML, actions pinned by SHA as `release.yml` does |

Order (README.md): 1.1, 1.3, 1.8, 1.9 first; then 1.4, 1.2, 1.5, 1.6, 1.10. 1.7 any time after 1.1.

---

## 1.1 `omawin profile menu`

**Files**

| File | Change |
|---|---|
| `bin/omawin.cs` | trace marks in `Send` |
| `menu-app/App.xaml.cs` | `Program.Tracing`; marks in the signal loop and `HandleRequest`; a `--hide` request |
| `menu-app/MainWindow.xaml.cs` | marks in `Show`, after `appWindow.Show()`, on the next frame; typing marks around `Render` |
| `bin/profile-menu.ps1` | new: the driver |
| `bin/commands.txt` | `profile menu` row |
| `docs/plan/roadmap.md` | the baseline numbers under 1.1 once measured |

**`bin/omawin.cs`** (C# 5). `Trace(string)` already exists (search `static void Trace(string label)`). In `Send`:

```csharp
static int Send(string request)
{
    EventWaitHandle signal;
    if (EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out signal))
    {
        Trace("send");
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(Path.Combine(StateDir, "system-menu-request"), request);
        AllowSetForegroundWindow(-1); // let it take focus
        signal.Set();
        Trace("signalled");
        signal.Dispose();
        return 0;
    }
    // ... unchanged
}
```

**`menu-app/App.xaml.cs`**

```csharp
// In Program, beside Trace:
internal static bool Tracing => File.Exists(Path.Combine(Path.GetDirectoryName(RequestFile)!, "trace-on"));

// In Start's signal thread, first thing after the wake-up:
while (signal.WaitOne())
{
    Trace("request");
    // ... unchanged
}

// App.HandleRequest, first line, and a way for the driver to hide it between runs:
public void HandleRequest(string[] args)
{
    Program.Trace("handle " + (args.Length > 0 ? args[0] : "toggle"));
    if (window == null) return;
    if (args.Length > 0 && args[0] == "--hide") { window.HideForProfile(); return; }
    // ... unchanged
}
```

**`menu-app/MainWindow.xaml.cs`**

```csharp
// The frame after this one is composed: what `omawin profile menu` calls "painted".
// CompositionTarget.Rendering is raised on the UI thread once per frame, before it's drawn
// (check: whether the first Rendering after Show is the frame that reaches the screen, or one
// before it; compare with a 60 fps recording, as profile-terminal's -Video does).
void TraceNextFrame(string label)
{
    if (!Program.Tracing) return;
    EventHandler<object>? once = null;
    once = (_, _) => { CompositionTarget.Rendering -= once; Program.Trace(label); };
    CompositionTarget.Rendering += once;
}

// For the profiler only: hide now, no exit animation, no focus shuffle.
public void HideForProfile() => appWindow.Hide();

// In Show, after `appWindow.Show();`:
appWindow.Show();
Program.Trace("shown");
TraceNextFrame("painted");

// Typing: replace the constructor's TextChanged handler.
Input.TextChanged += (_, _) =>
{
    Program.Trace("typed");
    selected = 0;
    Render();
    Program.Trace("rendered");
    TraceNextFrame("list-painted");
};
```

**`bin/profile-menu.ps1`** (pwsh; not run by install, so 7+ is fine). Same structure as `bin/profile-terminal.ps1`: trace-on file, `Mark`, `label\tticks` lines, timelines per run, median.

```powershell
<#
.SYNOPSIS
    omawin profile menu: where the time goes from a menu key to the menu on screen, and from a
    key typed in the launcher to its list redrawn.

.DESCRIPTION
    Opens each surface -Runs times the way its key does (GlazeWM runs the key's command), and
    reads the marks every step writes to %LOCALAPPDATA%\launcher\trace.log while trace-on exists:

      driver   this script: the key
      omawin   omawin.exe (bin/omawin.cs): created, running, wrote the request, signalled
      menu     the menu app: woke on the signal (or got GlazeWM's emit), handled it, the window
               shown, the next frame composed; typed, rendered, the list's next frame

    GlazeWM ignores simulated keys (AGENTS.md), so "key" is GlazeWM being asked to run the
    key's command; -Hand waits for you to press Win+Space yourself instead, once, for the
    keyboard hook's share.

        omawin profile menu                      launcher, system menu, finder; 10 runs each
        omawin profile menu -Surface launcher -Runs 20
        omawin profile menu -Typing              the launcher, typing "visual studio code"
        omawin profile menu -Hand                one run, pressed by you
#>
param(
    [int]$Runs = 10,
    [ValidateSet('all', 'launcher', 'menu', 'files')][string]$Surface = 'all',
    [switch]$Typing,
    [switch]$Hand,
    [string]$Word = 'visual studio code'
)
$ErrorActionPreference = 'Stop'
$state = Join-Path $env:LOCALAPPDATA 'launcher'
$traceOn = Join-Path $state 'trace-on'
$traceLog = Join-Path $state 'trace.log'
$freq = [Diagnostics.Stopwatch]::Frequency

function Mark([string]$Label) { [IO.File]::AppendAllText($traceLog, "driver $Label`t$([Diagnostics.Stopwatch]::GetTimestamp())`n") }
function Count([string]$Prefix) { if (-not (Test-Path $traceLog)) { return 0 }; @((Get-Content $traceLog) | Where-Object { $_.StartsWith($Prefix) }).Count }
function WaitFor([string]$Prefix, [int]$Before, [int]$Ms = 3000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $Ms) { if ((Count $Prefix) -gt $Before) { return $true }; Start-Sleep -Milliseconds 5 }
    $false
}
# A request straight to the running menu app, as omawin.exe's Send does (no process started).
function Send-Request([string]$Request) {
    [IO.File]::WriteAllText((Join-Path $state 'system-menu-request'), $Request)
    $signal = [Threading.EventWaitHandle]::OpenExisting('Local\dotfiles-system-menu-signal')
    try { $null = $signal.Set() } finally { $signal.Dispose() }
}
# The key's command, the way GlazeWM's binding runs it: emit when the rendered config uses it
# (1.2), else shell-exec omawin.exe.
$rendered = Get-Content "$HOME\.glzr\glazewm\config.yaml" -Raw
$viaEmit = $rendered -match "'emit omawin launcher'"
function Press([string]$Command) {
    if ($viaEmit) { glazewm command emit omawin $Command | Out-Null }
    else { glazewm command shell-exec --hide-window omawin.exe $Command | Out-Null }
}

if (-not (Get-Process SystemMenu -ErrorAction SilentlyContinue)) { throw 'the menu app is not running (omawin start)' }
New-Item -ItemType Directory -Force $state | Out-Null
Set-Content $traceOn ''
Remove-Item $traceLog -ErrorAction SilentlyContinue
$surfaces = if ($Surface -eq 'all') { 'launcher', 'menu', 'files' } else { , $Surface }
try {
    if ($Typing) {
        Press 'launcher'; $null = WaitFor 'menu painted' 0; Start-Sleep -Milliseconds 400
        for ($run = 1; $run -le $Runs; $run++) {
            Send-Request '--type'; Start-Sleep -Milliseconds 200 # empty search
            for ($i = 1; $i -le $Word.Length; $i++) {
                $before = Count 'menu list-painted'
                Mark 'key'
                Send-Request ('--type ' + $Word.Substring(0, $i))
                $null = WaitFor 'menu list-painted' $before
                Start-Sleep -Milliseconds 60 # a fast typist
            }
        }
        Send-Request '--hide'
    }
    else {
        foreach ($s in $surfaces) {
            $count = if ($Hand) { 1 } else { $Runs }
            for ($run = 1; $run -le $count; $run++) {
                $before = Count 'menu painted'
                Mark "surface $s"
                if ($Hand) { Write-Host "Press the key for $s now"; Mark 'key' }
                else { Mark 'key'; Press $s }
                if (-not (WaitFor 'menu painted' $before)) { Write-Warning "$s run ${run}: no frame within 3 s" }
                Start-Sleep -Milliseconds 300
                Send-Request '--hide'
                Start-Sleep -Milliseconds 300
            }
        }
    }
} finally { Remove-Item $traceOn -ErrorAction SilentlyContinue }

# --- the numbers: as profile-terminal.ps1 (timelines from each 'driver key', median) -------------
$marks = foreach ($line in Get-Content $traceLog) {
    $label, $at = $line -split "`t"
    if ($at) { [pscustomobject]@{ Label = $label; At = [long]$at } }
}
$labels = if ($Typing) {
    [ordered]@{ 'menu request' = 'menu app woke'; 'menu typed' = 'text changed'; 'menu rendered' = 'list rebuilt'; 'menu list-painted' = 'list on screen' }
} else {
    [ordered]@{
        'omawin process-created' = 'omawin.exe created'; 'omawin send' = 'omawin: writing the request'; 'omawin signalled' = 'omawin: signalled'
        'menu request' = 'menu app woke (signal)'; 'menu emit omawin' = 'menu app got the emit'; 'menu handle' = 'request handled'
        'menu shown' = 'window shown'; 'menu painted' = 'first frame'
    }
}
# One table per surface (or one for typing), rows = labels, columns = runs, last = median;
# Median and Row are copied from profile-terminal.ps1. 'menu handle' matches 'menu handle --launcher' etc.
# ... (as profile-terminal.ps1: $timelines, Median, Row; label match by StartsWith)
```

(The tail is profile-terminal.ps1's `Median`/`Row`/timeline code, with labels matched by `StartsWith` so `menu handle --launcher` counts as `menu handle`, and one table per `driver surface <name>` mark.)

**`bin/commands.txt`**, after `profile terminal`:

```
profile menu | Measure a menu key to its first frame, per surface (-Surface launcher|menu|files, -Runs 10), or typing in the launcher (-Typing); -Hand for one run pressed by you | pwsh bin/profile-menu.ps1
```

**Done when:** `omawin profile menu` prints key → painted per surface and `-Typing` prints key → list-painted; the medians (launcher, menu, finder, typing; 100% and 150% scale) are written under roadmap 1.1 as the baseline.

---

## 1.2 Keys straight to the shell: `emit`

Three parts. The fork gets `emit <name> [args…]`, broadcast to IPC subscribers as a `custom` event. The menu app subscribes and resolves `omawin` events through `bin/commands.txt`'s `app` rows, so **the emitted words are omawin command words** (`emit omawin launcher clipboard`). That keeps one name for every action (AGENTS.md: every action has an omawin command) and makes the fallback for a GlazeWM without `emit` a plain text substitution.

### Fork (Rust)

**Files**

| File | Change |
|---|---|
| `packages/wm-common/src/app_command.rs` | `InvokeCommand::Emit`, `SubscribableEvent::Custom`, `InvokeCommand::runs_while_paused`, tests |
| `packages/wm-common/src/wm_event.rs` | `WmEvent::Custom` |
| `packages/wm/src/ipc_server.rs` | `process_event` arm |
| `packages/wm/src/wm.rs` | `run_command`: pause check, `Emit` arm |
| `packages/wm/src/wm_state.rs` | `emit_event`: `Custom` passes while paused |
| `packages/wm/src/user_config.rs` | `active_keybinding_configs`: emit bindings stay active while paused |
| `packages/wm-platform/src/lib.rs`, `platform_impl/windows/mod.rs` | `allow_any_foreground()` |
| `scripts/omawin-release.ps1` | ship `omawin-features.txt` in the release ZIP |

**`app_command.rs`**: in `InvokeCommand` (alphabetical, after `Close`):

```rust
  /// Broadcasts a custom event to IPC subscribers, without running
  /// anything (e.g. `emit omawin launcher` opens omawin's launcher).
  Emit {
    /// Name of the event; subscribers filter on it.
    #[clap(required = true)]
    name: String,

    /// Words passed along with the event.
    #[clap(trailing_var_arg = true, allow_hyphen_values = true)]
    args: Vec<String>,
  },
```

In `SubscribableEvent`, add `Custom` (serialized `custom` by `rename_all = "snake_case"`).

```rust
impl InvokeCommand {
  /// Whether the command runs while the WM is paused.
  ///
  /// Unpausing must stay possible, and `emit` only notifies IPC
  /// subscribers (it moves no window), so both pass.
  #[must_use]
  pub fn runs_while_paused(&self) -> bool {
    matches!(
      self,
      InvokeCommand::WmTogglePause | InvokeCommand::Emit { .. }
    )
  }
}

#[cfg(test)]
mod tests {
  use std::iter;

  use clap::Parser;

  use super::InvokeCommand;

  /// Parses a command the way the user config does (see `Deserialize`).
  fn parse(command: &str) -> InvokeCommand {
    InvokeCommand::try_parse_from(
      iter::once("").chain(command.split_whitespace()),
    )
    .expect("command should parse")
  }

  #[test]
  fn emit_takes_name_and_words() {
    assert_eq!(
      parse("emit omawin launcher clipboard"),
      InvokeCommand::Emit {
        name: "omawin".into(),
        args: vec!["launcher".into(), "clipboard".into()],
      }
    );
  }

  #[test]
  fn emit_passes_flags_through() {
    assert_eq!(
      parse("emit omawin --open install"),
      InvokeCommand::Emit {
        name: "omawin".into(),
        args: vec!["--open".into(), "install".into()],
      }
    );
  }

  #[test]
  fn emit_needs_a_name() {
    assert!(InvokeCommand::try_parse_from(["", "emit"]).is_err());
  }

  #[test]
  fn only_pause_and_emit_run_while_paused() {
    assert!(parse("wm-toggle-pause").runs_while_paused());
    assert!(parse("emit omawin launcher").runs_while_paused());
    assert!(!parse("close").runs_while_paused());
  }
}
```

(`.expect` in a test is fine; the "avoid `.unwrap()`" rule is for runtime code.)

**`wm_event.rs`**:

```rust
  /// A custom event from the `emit` command.
  Custom {
    name: String,
    args: Vec<String>,
  },
```

Serialized (enum attrs `tag = "eventType"`, `rename_all_fields = "camelCase"`): `{"eventType":"custom","name":"omawin","args":["launcher"]}`.

**`ipc_server.rs`**, `process_event`'s exhaustive match:

```rust
      WmEvent::Custom { .. } => SubscribableEvent::Custom,
```

**`wm_state.rs`**, `emit_event`:

```rust
  pub fn emit_event(&self, event: WmEvent) {
    if self.has_initialized
      && (!self.is_paused
        || matches!(
          event,
          WmEvent::PauseChanged { .. } | WmEvent::Custom { .. }
        ))
    {
      // ... unchanged
    }
  }
```

(Update its doc comment: custom events pass while paused, so keys bound to `emit` keep working.)

**`wm.rs`**, `run_command`:

```rust
    // No-op if WM is currently paused, except for commands that must
    // still work then.
    if state.is_paused && !command.runs_while_paused() {
      return Ok(());
    }
    // ...
      InvokeCommand::Emit { name, args } => {
        // The subscriber (omawin's menu app) usually brings a window to
        // the front next; it isn't the foreground process, so let it.
        #[cfg(target_os = "windows")]
        wm_platform::allow_any_foreground();

        state.emit_event(WmEvent::Custom {
          name: name.clone(),
          args: args.clone(),
        });

        Ok(())
      }
```

**`user_config.rs`**, `active_keybinding_configs`:

```rust
    source_configs.into_iter().filter(move |kb| {
      !is_paused || kb.commands.iter().any(InvokeCommand::runs_while_paused)
    })
```

(and its doc comment: "When paused, only the configs with a command that runs while paused…").

**`wm-platform`**: in `lib.rs` beside `set_hide_windows_on_show`:

```rust
/// Lets any process bring a window to the foreground once, as if it had
/// received the last input.
///
/// For handing focus on to another process that acts on a key the WM
/// received (e.g. an IPC subscriber opening a menu).
///
/// # Platform-specific
///
/// This function is only available on Windows.
#[cfg(target_os = "windows")]
pub fn allow_any_foreground() {
  platform_impl::allow_any_foreground();
}
```

In `platform_impl/windows/mod.rs`:

```rust
/// Calls `AllowSetForegroundWindow(ASFW_ANY)`; failure is only logged.
pub(crate) fn allow_any_foreground() {
  // SAFETY: Takes no pointers; fails harmlessly if this process may not
  // set the foreground window itself.
  if let Err(err) = unsafe {
    windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow(
      windows::Win32::UI::WindowsAndMessaging::ASFW_ANY,
    )
  } {
    tracing::debug!("AllowSetForegroundWindow failed: {err}");
  }
}
```

*(check)* the `windows` crate version's signature (`Result<()>` vs `BOOL`), and whether GlazeWM, holding a low-level keyboard hook, counts as "received the last input" for Windows' foreground rules. If not, the menu app's inherited uiAccess may suffice by itself (uiAccess processes may set the foreground; *(check)*). Measure on the PC: Win+Space with a terminal in front must give the launcher keyboard focus every time (20 tries).

**`omawin-features.txt`**: the fork says what it can do, so omawin never guesses from a version number (and 2.9's per-user GlazeWM finds it the same way). In `scripts/omawin-release.ps1`, written into the ZIP beside `glazewm.exe`:

```
emit
animations.enabled
animations.window_close
```

`install/glazewm-fork.ps1` adds `'omawin-features.txt'` to `$Files` (copied beside `glazewm.exe`; it's data, not signed).

### Menu app (C#)

**Files**

| File | Change |
|---|---|
| `menu-app/Bar/GlazeWm.cs` | a second subscription, `custom`; `Custom` event; `Start` idempotent; a subscribed marker file |
| `menu-app/OmawinCommands.cs` | new: `bin/commands.txt`'s rows, to turn command words into a request |
| `menu-app/App.xaml.cs` | wire `Custom` to `HandleRequest`; start `Bar.Wm` even when the bar isn't native (until 1.6 makes it always native) |
| `check.ps1` | warn when the fork emits and no menu app is subscribed |

**`Bar/GlazeWm.cs`**

```csharp
    // An `emit` from GlazeWM (our fork, roadmap 1.2): its name and words, on the socket's thread.
    public event Action<string, string[]>? Custom;
    int started;
    static string SubscribedFile => Path.Combine(MenuModel.StateDir, "emit-subscribed");

    // Once, however many callers (the bar, App.OnLaunched for the keys).
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) new Thread(Run) { IsBackground = true, Name = "glazewm" }.Start();
    }

    // In Run, after the existing `sub --events ...`: its own subscription, because the official
    // GlazeWM doesn't know `custom` and refuses the whole `sub` that names it.
    await SendAsync("sub --events custom");

    // In Run's catch / after the loop (disconnected): no longer listening.
    try { File.Delete(SubscribedFile); } catch { }

    // In Handle, before the refresh for an event_subscription:
    if (kind == "event_subscription")
    {
        if (root.TryGetProperty("data", out var ev) && ev.ValueKind == JsonValueKind.Object
            && ev.TryGetProperty("eventType", out var type) && type.GetString() == "custom")
        {
            var name = ev.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var words = ev.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(w => w.GetString() ?? "").ToArray() : [];
            Program.Trace("emit " + name);
            Custom?.Invoke(name, words);
            return; // nothing on screen changed: no refresh
        }
        // ... the existing refresh
    }
    // ... and for the reply to our custom subscription:
    if (kind == "client_response" && root.GetProperty("clientMessage").GetString() == "sub --events custom")
    {
        if (root.TryGetProperty("success", out var ok) && ok.GetBoolean())
            try { File.WriteAllText(SubscribedFile, Environment.ProcessId.ToString()); } catch { }
        return;
    }
```

**`menu-app/OmawinCommands.cs`** (new)

```csharp
namespace SystemMenu;

// bin/commands.txt's rows, so a key GlazeWM emits as omawin command words
// (`emit omawin launcher clipboard`) reaches HandleRequest exactly as `omawin launcher clipboard`
// would through omawin.exe (bin/omawin.cs: the longest command the words spell; an `app` row's
// request plus the words after it), without starting a process. Rows that aren't `app` are
// handed to omawin.exe, so any command can be bound to `emit omawin ...`.
public static class OmawinCommands
{
    static Dictionary<string, string> rows = [];
    static DateTime stamp;
    static string File => Path.Combine(MenuModel.Repo, "bin", "commands.txt");

    // The request for HandleRequest, or null when omawin.exe ran it (or it's no command).
    public static string[]? Request(string[] words)
    {
        var table = Load();
        for (int n = Math.Min(words.Length, 3); n > 0; n--)
        {
            if (!table.TryGetValue(string.Join(' ', words[..n]), out var action)) continue;
            if (!action.StartsWith("app ")) { MenuModel.Omawin(words); return null; }
            // As omawin.exe's Send: one string, split on spaces by the menu app's signal loop.
            var request = (action[4..].Trim() + " " + string.Join(' ', words[n..])).Trim();
            return request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        App.Log(new Exception("emit omawin: no command \"" + string.Join(' ', words) + "\""));
        return null;
    }

    // Read again only when the file changed (it's live: edited in the repo).
    static Dictionary<string, string> Load()
    {
        var now = System.IO.File.GetLastWriteTimeUtc(File);
        if (now == stamp) return rows;
        var table = new Dictionary<string, string>();
        try
        {
            foreach (var raw in System.IO.File.ReadAllLines(File))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split('|', 3);
                if (parts.Length == 3) table[parts[0].Trim()] = parts[2].Trim();
            }
            rows = table;
            stamp = now;
        }
        catch (IOException) { } // being written: the old table, read again next time
        return rows;
    }
}
```

**`App.xaml.cs`**, `OnLaunched`, inside the low-priority block after `window = new MainWindow();`:

```csharp
            // Keys GlazeWM sends straight here (`emit omawin <command>`, roadmap 1.2): no
            // omawin.exe, no request file. The same requests as the signal's.
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Bar.Wm.Custom += (name, words) =>
            {
                if (name != "omawin") return;
                var request = OmawinCommands.Request(words);
                if (request != null) queue.TryEnqueue(() => HandleRequest(request));
            };
            Bar.Wm.Start(); // already started when the native bar is up; once either way
```

**`check.ps1`** (beside the GlazeWM checks):

```powershell
# Menu keys come straight from GlazeWM when the fork knows emit (roadmap 1.2): the menu app must be
# listening, or those keys do nothing until it's back (omawin start / omawin restart menu).
$features = Get-GlazeFeatures   # the same helper render.ps1 uses (below); move it to bin/shared.ps1
if ($features -contains 'emit') {
    $sub = Join-Path $env:LOCALAPPDATA 'launcher\emit-subscribed'
    $subPid = if (Test-Path $sub) { [int](Get-Content $sub -Raw) } else { 0 }
    if ($subPid -and (Get-Process -Id $subPid -ErrorAction SilentlyContinue | Where-Object Name -eq 'SystemMenu')) { Pass 'menu keys' 'straight from GlazeWM (emit)' }
    else { Warn 'menu keys' "the menu app isn't listening to GlazeWM: the menu keys do nothing (omawin restart menu)" }
}
```

### Config and renderer

**`glazewm/config.yaml`**: the menu keys emit (keys unchanged; only the commands):

```yaml
  - commands: ['emit omawin launcher']
    bindings: ['lwin+space']
  - commands: ['emit omawin launcher clipboard']
    bindings: ['lwin+shift+v']
  - commands: ['emit omawin launcher audio']
    bindings: ['lwin+a']
  - commands: ['emit omawin menu']
    bindings: ['lwin+alt+space']
  - commands: ['emit omawin files']
    bindings: ['lwin+f', 'lwin+shift+f']
  - commands: ['emit omawin capture screenshot']
    bindings: ['lwin+shift+s']
  - commands: ['emit omawin capture text']
    bindings: ['lwin+shift+t']
```

(Win+Alt+Space stays until 1C retires it. Terminal keys stay `shell-exec omawin.exe`: they're omawin.exe builtins.) Update the comments above each binding: "GlazeWM tells the menu app directly (emit, our fork); without the fork, render.ps1 makes it `shell-exec omawin.exe`."

**`themes/render.ps1`**, after `Merge-GlazeUser` and before `Write-Text "$glazeDir\config.yaml"`:

```powershell
# What our GlazeWM fork can do, from the file its release ships beside glazewm.exe (none: the
# official GlazeWM, or a fork from before the file).
function Get-GlazeFeatures {
    foreach ($dir in "$env:ProgramFiles\glzr.io\GlazeWM", "$env:LOCALAPPDATA\Programs\glzr.io\GlazeWM") { # the second: 2.9's per-user GlazeWM (check its path against 03)
        $file = Join-Path $dir 'omawin-features.txt'
        if (Test-Path $file) { return @(Get-Content $file | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    }
    @()
}
$glazeFeatures = Get-GlazeFeatures
# Keys that reach the menu app straight from GlazeWM (emit, roadmap 1.2) need the fork; on any
# other GlazeWM the same keys start omawin.exe with the same words, as before. Done on the merged
# text, so your account folder's bindings may use emit too.
if ($glazeFeatures -notcontains 'emit') {
    $glazeText = [regex]::Replace($glazeText, "'emit omawin ([^']*)'", "'shell-exec --hide-window omawin.exe `$1'")
}
```

**Fallback decision (`--or-exec`):** not built. GlazeWM's `startup_commands` start the menu app, `omawin check` warns when it isn't subscribed, and `HandleRequest` already starts at the low-priority tick. Write `open-questions/emit-or-exec.md` (question, context, options: none / `emit --or-exec <command>` run when no subscriber / the menu app restarting itself; default: none) and revisit if the PC ever shows a dead key.

**Done when:** `omawin profile menu` shows no `omawin process-created` / `omawin send` marks and `menu emit omawin` within a few ms of the key; Win+Space opens the launcher right after `omawin restart menu` (the reconnect loop's 1 s at most); with `glazewm command wm-toggle-pause` on, Win+Space still opens it; the fork's `cargo test -p wm-common` passes.

**Risks:** foreground rights (above); a menu app that hangs keeps the socket open but answers nothing (the same as today's request file).

---

## 1.3 The Native AOT build for everyone

**Files:** `.github/workflows/release.yml` (menu job), `menu-app/build.ps1`, `build/release.ps1`.

**`menu-app/build.ps1`**: `-Aot` must mean AOT or fail, never a silent JIT build. After `$cpp` is computed:

```powershell
if ($Aot -and -not $cpp) {
    throw "-Aot needs Visual Studio's C++ tools (vswhere found none with Microsoft.VisualStudio.Component.VC.Tools.x86.x64): the native build can't link"
}
```

*(check)* on `windows-11-arm` the component that vswhere must find (`...VC.Tools.ARM64`); make the `-requires` component depend on `$Arch`.

**`release.yml`**, the `menu` job:

```yaml
      - name: Build (the same script this PC runs; native, or the job fails)
        shell: pwsh
        run: .\menu-app\build.ps1 -OutDir out\menu-${{ matrix.arch }} -BuildOnly -Aot -Arch ${{ matrix.arch }} -Release '${{ needs.meta.outputs.version }}'
      - name: Check it's the native build
        shell: pwsh
        run: |
          $kind = (Get-Content 'out\menu-${{ matrix.arch }}\build-kind.txt' -Raw).Trim()
          if ($kind -ne 'native') { throw "build-kind.txt says '$kind', not 'native'" }
          # A native build is one exe and no managed framework: no coreclr.dll beside it.
          if (Test-Path 'out\menu-${{ matrix.arch }}\coreclr.dll') { throw 'coreclr.dll present: this is the JIT build' }
```

*(check)* the AOT output folder has no `coreclr.dll` (WinUI's own DLLs remain).

**`build/release.ps1`**: refuse to pack a menu folder that isn't native (a second guard, so a local fake release can't either):

```powershell
        if (Test-Path (Join-Path $menu 'SystemMenu.exe')) {
            $kind = Get-Content (Join-Path $menu 'build-kind.txt') -Raw -ErrorAction SilentlyContinue
            if ("$kind".Trim() -ne 'native' -and -not $AllowJit) { throw "menu-$a is a '$("$kind".Trim())' build; releases ship the native one (-AllowJit for a test)" }
            Add-Zip $menu "SystemMenu-$Tag-win-$a.zip"
        }
```

(with `[switch]$AllowJit` in its `param`).

**Done when:** a `workflow_dispatch` run of Release passes both checks, and its `SystemMenu-*.zip` unpacks to a folder whose `build-kind.txt` is `native`.

---

## 1.4 Motion from one place

**Files**

| File | Change |
|---|---|
| `menu-app/Motion.cs` | new: tokens, `Enabled`, helpers |
| `menu-app/MainWindow.xaml.cs` | `Animate`, `Cascade`, `SlideIn`, `ScrollGlide` use `Motion`; exit animation in `HideMenu` / `Dismiss` |
| `menu-app/MainWindow.Tiles.cs` | tile transitions (`:467-469`) from `Motion` |
| `menu-app/Bar/Bar.cs` | `Arrive` on Composition via `Motion` |
| `menu-app/Bar/Widgets.cs` | the tray drawer's fade (`TrayWidget.Open`), the listening pulse (`:232`) stops when off |
| `bin/toggle-animations.ps1` | new: `omawin toggle animations` |
| `bin/commands.txt` | `toggle animations` row |
| `themes/render.ps1` | `{{ animations }}` for GlazeWM's `animations.enabled` (1.5) |
| `install/look.ps1` | `toggle-animations.ps1 default` once (off in a VM) |

**`menu-app/Motion.cs`**

```csharp
using System.Numerics;
using Microsoft.UI.Composition;

namespace SystemMenu;

// Omarchy's motion rules in one place (its agents/skills/motion.md): things arrive within 250 ms
// and leave in about two thirds of that, on ease-out-quint, never linear or ease-in. Every
// animation in the menu app takes its numbers from here. Off (`omawin toggle animations`, the file
// below, or Windows' own Settings > Accessibility > Animation effects) makes every one a jump to
// its end: Composition can't run a 0 ms animation, so the helpers set the end value instead.
public static class Motion
{
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(200);   // a window, a row, a level
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(130);    // ~2/3 of Enter
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);  // between rows arriving
    public static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(200);   // scrolling, a lifted tile
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);   // a colour change under the mouse
    public const float RisePx = 8f;   // how far an arriving thing rises
    public const float SlidePx = 44f; // a submenu sliding in
    static readonly Vector2 Quint1 = new(0.23f, 1f), Quint2 = new(0.32f, 1f); // cubic-bezier(0.23, 1, 0.32, 1)

    static string SettingFile => Path.Combine(MenuModel.StateDir, "animations");
    static bool fileOn = ReadFile();
    static readonly Windows.UI.ViewManagement.UISettings windows = new();
    static FileSystemWatcher? watcher;

    // Raised on the UI thread when Enabled may have changed.
    public static event Action? Changed;

    public static bool Enabled => fileOn && windows.AnimationsEnabled;

    // From App.OnLaunched, on the UI thread.
    public static void Start(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            watcher = new FileSystemWatcher(MenuModel.StateDir, "animations") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            FileSystemEventHandler again = (_, _) => ui.TryEnqueue(() => { fileOn = ReadFile(); Changed?.Invoke(); });
            watcher.Changed += again; watcher.Created += again; watcher.Deleted += again;
        }
        catch (Exception ex) { App.Log(ex); }
        // Windows' own switch (check: UISettings.AnimationsEnabledChanged exists from Windows 10 2004
        // and is raised on a worker thread).
        windows.AnimationsEnabledChanged += (_, _) => ui.TryEnqueue(() => Changed?.Invoke());
    }

    static bool ReadFile()
    {
        try { return !File.Exists(SettingFile) || File.ReadAllText(SettingFile).Trim() != "off"; }
        catch (IOException) { return true; }
    }

    public static CompositionEasingFunction EaseOut(Compositor c) => c.CreateCubicBezierEasingFunction(Quint1, Quint2);

    // Fades a visual in from 0, after a delay (rows arriving one after another).
    public static void FadeIn(Visual v, TimeSpan delay = default, TimeSpan? duration = null)
    {
        if (!Enabled) { v.StopAnimation("Opacity"); v.Opacity = 1; return; }
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, 0);
        a.InsertKeyFrame(1, 1, EaseOut(c));
        a.Duration = duration ?? Enter;
        a.DelayTime = delay;
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", a);
    }

    // Moves a visual's Translation from `from` to zero (ElementCompositionPreview.SetIsTranslationEnabled
    // must be on for a XAML element's visual).
    public static void MoveIn(Visual v, Vector3 from, TimeSpan delay = default, TimeSpan? duration = null)
    {
        if (!Enabled) { v.StopAnimation("Translation"); v.Properties.InsertVector3("Translation", Vector3.Zero); return; }
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, from);
        a.InsertKeyFrame(1, Vector3.Zero, EaseOut(c));
        a.Duration = duration ?? Enter;
        a.DelayTime = delay;
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Translation", a);
    }

    // Arrives: fades in and rises RisePx.
    public static void Arrive(Visual v, TimeSpan delay = default)
    {
        FadeIn(v, delay);
        MoveIn(v, new Vector3(0, RisePx, 0), delay);
    }

    // Leaves: fades out and sinks `dropPx`, then `done` (at once when off). Returns a handle whose
    // Cancel() stops it and puts the visual back; `done` then never runs.
    public static Leaving Leave(Visual v, float dropPx, Action done)
    {
        var leaving = new Leaving(v);
        if (!Enabled) { done(); return leaving; }
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, EaseOut(c));
        fade.Duration = Exit;
        var drop = c.CreateVector3KeyFrameAnimation();
        drop.InsertKeyFrame(1, new Vector3(0, dropPx, 0), EaseOut(c));
        drop.Duration = Exit;
        v.StartAnimation("Opacity", fade);
        v.StartAnimation("Translation", drop);
        batch.End();
        // Raised on the compositor's thread, the UI thread for XAML's compositor (check).
        batch.Completed += (_, _) => { if (!leaving.Cancelled) done(); };
        return leaving;
    }

    public sealed class Leaving(Visual visual)
    {
        public bool Cancelled { get; private set; }
        public void Cancel()
        {
            Cancelled = true;
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation");
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    // For XAML's implicit transitions (BrushTransition, Vector3Transition): 0 when off is allowed there.
    public static TimeSpan Or0(TimeSpan t) => Enabled ? t : TimeSpan.Zero;
}
```

*(check)* `Visual.Properties.InsertVector3("Translation", …)` sets the Translation that `SetIsTranslationEnabled` adds; if not, use `ElementCompositionPreview.GetElementVisual(el)` plus `UIElement.Translation = default` on the element.

`App.OnLaunched`: `Motion.Start(DispatcherQueue.GetForCurrentThread());` before `Bar.Start`.

**`MainWindow.xaml.cs`**: replace the scattered numbers.

```csharp
    // A small fade and rise as it appears.
    void Animate()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        Motion.Arrive(ElementCompositionPreview.GetElementVisual(Root));
    }

    // In SetUpMotion: ScrollGlide becomes Motion.Glide; the transition is rebuilt on Motion.Changed:
    //   ListLayer.TranslationTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) };
    //   Motion.Changed += () => ListLayer.TranslationTransition = new Vector3Transition { Duration = Motion.Or0(Motion.Glide) };
    // The hairline's drifting light (7 s, forever) is decoration: not started while !Motion.Enabled.

    // The first screenful of rows or tiles fades and rises in, Stagger apart.
    void Cascade()
    {
        int first = tileMode ? 0 : (int)Math.Round(scrollY / Pitch);
        int count = tileMode ? tiles.Count : used;
        for (int i = 0; i < count; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(tileMode ? tiles[i].Inner : pool[i].Slot);
            int step = (tileMode ? i : pool[i].Index) - first; // pool[i].Index: the list index (1.7)
            if (step < 0 || step > (tileMode ? 18 : MaxRows)) { visual.Opacity = 1; continue; }
            Motion.Arrive(visual, TimeSpan.FromMilliseconds(28) + Motion.Stagger * step);
        }
    }

    void Slide(int direction) => SlideIn(new Vector3(Motion.SlidePx * direction, 0, 0));
    void Swap() => SlideIn(new Vector3(0, 4, 0));

    void SlideIn(Vector3 from)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Pane);
        Motion.MoveIn(visual, from);
        Motion.FadeIn(visual, duration: Motion.Enter * 0.8);
    }
```

Row and tile hover transitions (`MakeRow`'s `BackgroundTransition`, `MainWindow.Tiles.cs`'s `BrushTransition`/`ScaleTransition`/`TranslationTransition`): `Duration = Motion.Or0(Motion.Quick)` / `Motion.Or0(Motion.Glide)`.

**The exit animation.** One path hides the window: `HideMenu(instant)`. `Dismiss` hands focus back first (so typing goes there at once), then fades. A key during the exit hides at once and shows afresh; nothing waits on an animation.

```csharp
    Motion.Leaving? leaving; // set while the exit animation runs

    // Hides it: fading out (Motion.Exit) unless `instant` or animations are off. Picks that capture
    // the screen (screenshots, the colour picker, OCR) pass instant: true so the fading menu isn't in
    // the picture.
    void HideMenu(bool instant = false)
    {
        if (!appWindow.IsVisible) return;
        if (leaving != null) { if (instant) HideNow(); return; }
        if (instant || !Motion.Enabled) { HideNow(); return; }
        Root.IsHitTestVisible = false; // clicks during the fade go nowhere
        leaving = Motion.Leave(ElementCompositionPreview.GetElementVisual(Root), 4f, HideNow);
    }

    void HideNow()
    {
        leaving?.Cancel(); // puts opacity and translation back for the next showing
        leaving = null;
        Root.IsHitTestVisible = true;
        appWindow.Hide();
    }

    void Dismiss()
    {
        var target = previous != IntPtr.Zero && IsWindowVisible(previous) && ClassOf(previous) != "Shell_TrayWnd"
            ? previous : FindWindow("Progman", null);
        if (target != IntPtr.Zero) SetForegroundWindow(target); // first: your next key goes there
        HideMenu();
    }

    // In Show, first lines: a key during the exit ends it and shows afresh.
    void Show(string at, bool asLauncher = false, bool asFinder = false)
    {
        if (leaving != null) HideNow();
        bool showing = appWindow.IsVisible;
        // ... unchanged
    }
```

The `Activated` handler's `HideMenu()` (deactivated) stays: with focus already elsewhere it just fades. `Toggle` / `ToggleLauncher` / `ToggleFinder` check `GetForegroundWindow() == hwnd`, which is false during the exit, so a second press re-shows: as wanted. Screen-capturing actions: find their `HideMenu()` call sites (`Run`, `Launch`) and pass `instant: true` when the action is `omawin capture …` / `--snip`.

**`Bar/Bar.cs`**, `Arrive` on Composition (off the UI thread once started):

```csharp
    static void Arrive()
    {
        if (arrived || windows.Count == 0) return;
        arrived = true;
        foreach (var bar in windows)
        {
            int i = 0;
            foreach (var w in bar.Widgets)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(w.Element, true);
                var visual = ElementCompositionPreview.GetElementVisual(w.Element);
                var delay = Motion.Stagger * i++;
                Motion.FadeIn(visual, delay);
                Motion.MoveIn(visual, new Vector3(0, -4, 0), delay); // drops down into place
            }
        }
    }
```

`Widgets.cs` `TrayWidget.Open`: `ElementCompositionPreview.GetElementVisual(drawer)` + `Motion.FadeIn(visual)`. The listening pulse (`:232`): start it only `if (Motion.Enabled)`; re-evaluate on `Motion.Changed`.

**`bin/toggle-animations.ps1`** (5.1-compatible: install's look step runs it)

```powershell
<#
.SYNOPSIS
    omawin toggle animations: every animation on or off, the menu app's and GlazeWM's windows'.

.DESCRIPTION
    Writes %LOCALAPPDATA%\launcher\animations (on / off): the menu app watches it (Motion.cs), and
    themes/render.ps1 fills it into GlazeWM's animations.enabled, then GlazeWM reloads.

        .\toggle-animations.ps1            toggle
        .\toggle-animations.ps1 on|off     set
        .\toggle-animations.ps1 status     say which
        .\toggle-animations.ps1 default    only if never set: off in a virtual machine (as Omarchy's
                                           vm-no-animations.sh), else on. Install runs this.
#>
param([ValidateSet('toggle', 'on', 'off', 'status', 'default')][string]$To = 'toggle')
$ErrorActionPreference = 'Stop'
$State = Join-Path $env:LOCALAPPDATA 'launcher'
$file = Join-Path $State 'animations'
$now = if ((Test-Path $file) -and (Get-Content $file -Raw).Trim() -eq 'off') { 'off' } else { 'on' }
if ($To -eq 'status') { "animations $now"; return }
if ($To -eq 'default') {
    if (Test-Path $file) { return }
    $model = "$((Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).Model)"
    $To = if ($model -match 'Virtual|VMware|VirtualBox|KVM|QEMU|HVM domU|Parallels') { 'off' } else { 'on' }
}
$new = if ($To -eq 'toggle') { if ($now -eq 'on') { 'off' } else { 'on' } } else { $To }
New-Item -ItemType Directory -Force $State | Out-Null
Set-Content $file $new -NoNewline -Encoding ascii
& (Join-Path (Split-Path $PSScriptRoot) 'themes\render.ps1') -Reload
"animations $new"
```

**`bin/commands.txt`**:

```
toggle animations | Every animation on or off: the menu, the bar and GlazeWM's windows (off by default in a virtual machine); or: on, off, status | pwsh bin/toggle-animations.ps1
```

**`themes/render.ps1`**, beside `$c['name'] = $Name`:

```powershell
# Not a colour: GlazeWM's animations.enabled, from `omawin toggle animations` (bin/toggle-animations.ps1).
$animFile = Join-Path $State 'animations'
$c['animations'] = if ((Test-Path $animFile) -and (Get-Content $animFile -Raw).Trim() -eq 'off') { 'false' } else { 'true' }
```

**`install/look.ps1`**: `& "$Repo\bin\toggle-animations.ps1" default` before the first theme is applied (that render reads it).

**Done when:** `grep -n "TimeSpan.FromMilliseconds\|CreateCubicBezier" menu-app` finds animation numbers only in `Motion.cs` (timeouts and debounces aside); Esc fades the menu out in 130 ms and Win+Space during that fade shows it again at once; `omawin toggle animations` makes the menu, the bar and GlazeWM's windows jump with no fade; the screenshot taken from the menu doesn't show the menu.

---

## 1.5 Ship window animations (fork)

**Files**

| File | Change |
|---|---|
| `.github/workflows/animation-check.yaml` | restored (`git revert 445d61e0`), plus a close step and an `enabled: false` run |
| `scripts/animation-check.ps1` | close with `glazewm command close`; check the close overlay is gone; `-Disabled` |
| `packages/wm-common/src/parsed_config.rs` | `AnimationsConfig.enabled`, `window_close`, accessors, manual `Default`, tests |
| `packages/wm/src/animation_manager.rs` | read config through the accessors; `CloseState`, `start_close`, closes in `tick_update` / `update_tick_rate`; `ensure_context`; tests |
| `packages/wm/src/wm.rs` | `Close` arm starts the close animation; `should_settle` uses the accessor |
| `packages/wm/src/animation_manager.rs` `trigger_threshold` | pixel-only, validated at config load |
| `glazewm/config.yaml` (omawin) | `enabled: {{ animations }}`, `window_close` |
| `scripts/omawin-release.ps1` | release, with `omawin-features.txt` (1.2) |

### 1. The frame check back in CI

```
git -C <fork> switch omawin
git revert 445d61e0     # brings back .github/workflows/animation-check.yaml
```

Then add to the workflow, after "Record animations (real speed)":

```yaml
      # Animations off (animations.enabled: false): nothing may animate, no overlay may appear.
      - name: Record with animations off
        if: always()
        shell: pwsh
        run: ./scripts/animation-check.ps1 -Out out-off -DurationMs 200 -Disabled
```

and `out-off/` to the upload's `path`. In `scripts/animation-check.ps1`:

```powershell
param(
    [string]$Bin = 'target\release',
    [string]$Out = 'out',
    [int]$DurationMs = 600,
    [int]$OpenMs = 0,
    [int]$CloseMs = 0,
    # animations.enabled: false; the checks then also fail on any overlay seen at any frame.
    [switch]$Disabled
)
if ($CloseMs -le 0) { $CloseMs = [Math]::Max(1, [int]($DurationMs * 0.8)) }
# ... the config gains:
#   animations:
#     enabled: $(if ($Disabled) { 'false' } else { 'true' })
#     window_close: { effect_type: 'popin', duration_ms: $CloseMs, easing: 'ease_out_quint' }
# ... and the close step closes through GlazeWM (the close animation's only hook), then waits
#     $CloseMs + 300 ms and checks that no window of the overlay class is left
#     (check: the overlay's window class name in platform_impl/windows/animation_window.rs).
```

### 2. A week of daily use (🖥)

Known rough edges to fix as found, each with a frame-check case where CI can show it:
- flicker during sync (`platform_sync.rs` ~:207, the 20 ms overlay hold in `cancel_animation`);
- the fullscreen check (`platform_sync.rs` ~:452);
- `trigger_threshold` taking non-pixel units (`animation_plan_for_window`'s `TODO`). Validate at load instead:

```rust
// In wm/src/user_config.rs, where the parsed config is checked after reading (check: the
// function that validates the config there; add one if none).
/// Fails when `trigger_threshold` isn't in pixels (a percentage has no
/// meaning for a move's distance).
fn validate_animations(config: &ParsedConfig) -> anyhow::Result<()> {
  if let Some(move_config) = &config.animations.window_move {
    if move_config.trigger_threshold.unit != LengthUnit::Pixel {
      bail!("`animations.window_move.trigger_threshold` must be in pixels (e.g. '10px').");
    }
  }
  Ok(())
}
```

*(check)* `LengthValue`'s unit field and enum names in `wm-platform/src/models/length_value.rs`.
- the 100 ms settle for new windows (`SETTLE_DELAY` in `wm.rs`): keep unless the week says otherwise.

### 3. The close animation

Only for windows closed by GlazeWM's `close` command (the key): there, the window still exists. A window that closes itself just vanishes, as today.

**`parsed_config.rs`**

```rust
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(default, rename_all(serialize = "camelCase"))]
pub struct AnimationsConfig {
  /// Whether any window animation runs. Off keeps the settings below for
  /// when it's turned on again.
  pub enabled: bool,

  pub window_move: Option<WindowMoveAnimationConfig>,
  pub window_open: Option<AnimationEffectConfig>,

  /// How a window closed by the `close` command leaves. `fade` fades its
  /// snapshot out; `popin` also shrinks it to `CLOSE_END_SCALE`.
  pub window_close: Option<AnimationEffectConfig>,
}

impl Default for AnimationsConfig {
  fn default() -> Self {
    AnimationsConfig {
      enabled: true,
      window_move: None,
      window_open: None,
      window_close: None,
    }
  }
}

impl AnimationsConfig {
  /// The open animation, when animations are enabled and it's configured.
  #[must_use]
  pub fn open_effect(&self) -> Option<&AnimationEffectConfig> {
    self.window_open.as_ref().filter(|_| self.enabled)
  }

  /// The move animation, when animations are enabled and it's configured.
  #[must_use]
  pub fn move_effect(&self) -> Option<&WindowMoveAnimationConfig> {
    self.window_move.as_ref().filter(|_| self.enabled)
  }

  /// The close animation, when animations are enabled and it's configured.
  #[must_use]
  pub fn close_effect(&self) -> Option<&AnimationEffectConfig> {
    self.window_close.as_ref().filter(|_| self.enabled)
  }
}

#[cfg(test)]
mod tests {
  use super::{AnimationEffectConfig, AnimationsConfig};

  /// An `AnimationsConfig` with every animation configured.
  fn all_on() -> AnimationsConfig {
    AnimationsConfig {
      window_open: Some(AnimationEffectConfig::default()),
      window_close: Some(AnimationEffectConfig::default()),
      ..AnimationsConfig::default()
    }
  }

  #[test]
  fn enabled_by_default() {
    assert!(AnimationsConfig::default().enabled);
    assert!(all_on().open_effect().is_some());
  }

  #[test]
  fn disabled_hides_every_effect_but_keeps_them() {
    let config = AnimationsConfig { enabled: false, ..all_on() };
    assert!(config.open_effect().is_none());
    assert!(config.close_effect().is_none());
    assert!(config.window_open.is_some());
  }
}
```

(Struct-level `#[serde(default)]` takes missing fields from this `Default`, so a config without `enabled:` keeps animations on.)

**Read the config only through the accessors.** `animation_manager.rs` `hides_new_windows`:

```rust
pub fn hides_new_windows(config: &UserConfig) -> bool {
  config.value.general.hide_method == wm_common::HideMethod::Cloak
    && config.value.animations.open_effect().is_some_and(|open| {
      matches!(open.effect_type, AnimationEffect::Fade | AnimationEffect::Popin)
    })
}
```

`animation_plan_for_window`, the `let effect = match (trigger, &config.value.animations)` block becomes:

```rust
    let effect = match trigger {
      AnimationTrigger::WindowOpened => {
        let open_config = config.value.animations.open_effect()?;
        if self.is_animating(&window.id()) {
          return None;
        }
        open_config
      }
      AnimationTrigger::WindowMoved => {
        let move_config = config.value.animations.move_effect()?;
        // ... the existing distance / threshold code, unchanged ...
        &move_config.effect
      }
    };
```

`wm.rs` `should_settle`: `config.value.animations.move_effect().is_some_and(|m| m.effect.effect_type == wm_common::AnimationEffect::Slide)`. Turning animations off mid-animation: `reload_config` already re-applies `set_hide_windows_on_show`; also cancel running ones there so nothing is left half-faded:

```rust
  // In reload_config, after config.reload():
  if !config.value.animations.enabled {
    for window in state.windows() {
      state.animation_manager.cancel_animation(&window.id());
      state.pending_sync.queue_container_to_redraw(window);
    }
  }
```

**`animation_manager.rs`**: the close state, beside `FadeState`:

```rust
/// Size a close pop-out ends at, relative to the window's (Omarchy's
/// `windowsOut`, popin 95%).
pub const CLOSE_END_SCALE: f32 = 0.95;

/// State of a close animation: a snapshot of a closed window fading out
/// (and, for `popin`, shrinking) where the window was.
#[derive(Clone, Debug)]
struct CloseState {
  clock: AnimationClock,

  /// Where the window was when it was closed.
  rect: Rect,

  /// Scale the snapshot ends at (1.0: fade only).
  end_scale: f32,
}

impl CloseState {
  /// Returns the snapshot's rect and opacity at the current progress.
  fn current(&self) -> (Rect, OpacityValue) {
    let progress = self.clock.eased_progress();
    let scale = 1.0 + (self.end_scale - 1.0) * progress;
    (
      scaled_about_center(&self.rect, scale),
      OpacityValue(1.0 - progress),
    )
  }
}

/// Scales a rect about its center, rounding to whole pixels.
// LINT: Window sizes fit easily in an `f32` and back.
#[allow(clippy::cast_possible_truncation, clippy::cast_precision_loss)]
fn scaled_about_center(rect: &Rect, scale: f32) -> Rect {
  let width = (rect.width() as f32 * scale).round() as i32;
  let height = (rect.height() as f32 * scale).round() as i32;
  Rect::from_xy(
    rect.x() + (rect.width() - width) / 2,
    rect.y() + (rect.height() - height) / 2,
    width,
    height,
  )
}
```

New fields on `AnimationManager` (and in `new`): `closes: HashMap<Uuid, CloseState>` and `close_windows: HashMap<Uuid, AnimationWindow>`, keyed by an id of their own (the window's id is unmanaged and forgotten while its snapshot still fades; keeping them apart means `cancel_animation(window_id)` never touches a close).

Extract the context creation from `start_slides` so both use it:

```rust
  /// Creates the shared GPU context on first use.
  ///
  /// Returns `false` when it couldn't be made (animations are then
  /// skipped, never retried in a loop: the next animation tries again).
  fn ensure_context(&mut self, dispatcher: &Dispatcher) -> bool {
    if self.context.is_none() {
      match AnimationContext::new(dispatcher) {
        Ok(context) => self.context = Some(context),
        Err(err) => {
          tracing::warn!("Failed to create animation context: {err}");
          return false;
        }
      }
    }
    true
  }
```

```rust
  /// Starts a close animation for a window that the WM is about to close.
  ///
  /// Takes a snapshot of the window as it is now; the snapshot fades out
  /// over the closing window, which disappears under it. If the app keeps
  /// the window open (e.g. it asks to save), the window is simply still
  /// there once the snapshot has faded.
  #[cfg(target_os = "windows")]
  pub fn start_close(
    &mut self,
    window: &WindowContainer,
    config: &UserConfig,
    frame_rate: u32,
    dispatcher: &Dispatcher,
  ) {
    let Some(effect) = config.value.animations.close_effect() else {
      return;
    };

    let properties = window.native_properties();

    // Nothing to see, or nothing that should move: minimized, mid-fade
    // (still transparent) or fullscreen-like windows just close.
    if properties.is_minimized
      || self.hidden.contains(&window.id())
      || properties.is_maximized
    {
      return;
    }

    if !self.ensure_context(dispatcher) {
      return;
    }

    // A slide in progress ends here: its overlay goes, and the snapshot is
    // taken from the real window.
    self.cancel_animation(&window.id());

    let rect = properties.frame.clone();
    let native = window.native().clone();
    let request = AnimationWindowRequest {
      window: &native,
      inner_rect: rect.clone(),
      outer_rect: rect.clone(),
      opacity: None,
    };

    let overlay = match &self.context {
      Some(context) => {
        AnimationWindow::new_many(context, &[request], dispatcher).pop()
      }
      None => None,
    };

    match overlay {
      Some(Ok(overlay)) => {
        let id = Uuid::new_v4();
        self.close_windows.insert(id, overlay);
        self.closes.insert(
          id,
          CloseState {
            clock: AnimationClock::new(effect, frame_rate),
            rect,
            end_scale: if effect.effect_type == AnimationEffect::Popin {
              CLOSE_END_SCALE
            } else {
              1.0
            },
          },
        );
        self.update_tick_rate();
      }
      Some(Err(err)) => {
        tracing::warn!("Failed to create close animation window: {err}");
      }
      None => {}
    }
  }
```

*(check)* `AnimationWindowRequest`'s field types (`window: &NativeWindow`?) and that a new overlay is placed above its source window in z-order (slides hide the real window, so this never mattered); if it isn't, raise it (`SetWindowPos(HWND_TOPMOST)` in `platform_impl/windows/animation_window.rs`).

`tick_update`: the early return becomes `if self.slides.is_empty() && self.closes.is_empty() { return Ok(()); }`; inside the transaction closure, after the slides:

```rust
          for (id, close) in &self.closes {
            if let Some(anim_window) = self.close_windows.get(id) {
              let (rect, opacity) = close.current();
              anim_window.update(&rect, Some(opacity))?;
            }
          }
```

and after the transaction, finished closes go (on the same 20 ms-later thread as `cancel_animation`):

```rust
    let done = self
      .closes
      .iter()
      .filter(|(_, close)| close.clock.is_complete())
      .map(|(id, _)| *id)
      .collect::<Vec<_>>();

    for id in done {
      self.closes.remove(&id);
      if let Some(anim_window) = self.close_windows.remove(&id) {
        std::thread::spawn(move || {
          if let Err(err) = anim_window.destroy() {
            tracing::warn!("Failed to destroy close animation window: {err}");
          }
        });
      }
    }
    self.update_tick_rate();
```

*(check)* `AnimationWindow: Send` (the existing `cancel_animation` already moves one into a thread, so yes). `update_tick_rate`: chain `self.closes.values().map(|c| c.clock.frame_rate)`. On exit (`WindowManager::cleanup`): destroy every close overlay (add `AnimationManager::destroy_all(&mut self)` that drains `close_windows` and `windows`, if cleanup doesn't already).

Tests, in the existing `mod tests`:

```rust
  #[test]
  fn scaling_keeps_the_center() {
    let rect = Rect::from_xy(100, 100, 200, 100);
    let scaled = super::scaled_about_center(&rect, 0.95);
    assert_eq!(scaled.width(), 190);
    assert_eq!(scaled.height(), 95);
    assert_eq!(scaled.x() + scaled.width() / 2, rect.x() + rect.width() / 2);
  }

  #[test]
  fn close_ends_invisible_and_small() {
    let close = super::CloseState {
      clock: clock(500, 160),
      rect: Rect::from_xy(0, 0, 1000, 800),
      end_scale: super::CLOSE_END_SCALE,
    };
    let (rect, opacity) = close.current();
    assert_eq!(rect.width(), 950);
    assert!(opacity.0.abs() < f32::EPSILON);
  }
```

**`wm.rs`**, the `Close` arm:

```rust
      InvokeCommand::Close => {
        match subject_container.as_window_container() {
          Ok(window) => {
            // Snapshot first: once closed there's nothing to capture.
            #[cfg(target_os = "windows")]
            {
              let frame_rate = window
                .monitor()
                .and_then(|monitor| monitor.native_properties().refresh_rate)
                .unwrap_or(60);
              let WmState { animation_manager, dispatcher, .. } = state;
              animation_manager.start_close(&window, config, frame_rate, dispatcher);
            }

            // Window handle might no longer be valid here.
            if let Err(err) = window.native().close() {
              warn!("Failed to close window: {:?}", err);
            }

            Ok(())
          }
          _ => Ok(()),
        }
      }
```

*(check)* `window.monitor()`'s name and return type (`CommonGetters`), and `WmState` destructuring with private fields (use `&mut state.animation_manager` / `&state.dispatcher` as two expressions if that's simpler: disjoint field borrows are allowed).

### 4. omawin's config

`glazewm/config.yaml`:

```yaml
animations:
  # `omawin toggle animations` (bin/toggle-animations.ps1); render.ps1 fills it in.
  enabled: {{ animations }}
  window_move:
    effect_type: 'slide'
    duration_ms: 200
    easing: 'ease_out_quint'
    trigger_threshold: '10px'
  window_open:
    effect_type: 'fade'
    duration_ms: 200
    easing: 'ease_out_cubic'
  # A window closed with the key fades out, shrinking a little (Omarchy's windowsOut).
  window_close:
    effect_type: 'popin'
    duration_ms: 160
    easing: 'ease_out_quint'
```

The official GlazeWM ignores unknown keys under `animations` *(check: its `AnimationsConfig` isn't `deny_unknown_fields`; it has no `animations` section at all, so the whole section is ignored)*.

### 5. Release and 6. upstream

- `scripts/omawin-release.ps1 -Version <n>`: the ZIP gains `omawin-features.txt` (1.2). `install/glazewm-fork.ps1` already takes the newest `v<base>-omawin.N` (`Get-Release`); add `omawin-features.txt` to `$Files`. Nothing else to point.
- Upstream to glzr-io/glazewm as separate PRs: easing functions (`easing_function.rs`), the snapshot slide (`animation_window.rs` + `start_slides`), `emit`. Each PR its own branch off upstream `main`, cherry-picked.

**Done when:** the restored workflow passes on `omawin` at 600 ms and 200 ms and with `-Disabled`; a week of daily use without a stuck overlay or a window left transparent; closing with the key fades the window out; `omawin toggle animations` turns window animations off and on without restarting GlazeWM.

---

## 1.6 Finish the native bar; Zebar out

Prerequisite: the native bar covers everything Zebar's does. Two pieces are left (docs/native-bar.md): **media** (below) and the **music and agents drop-downs** (Zebar's `music.js` / agents panel; `Win+M` = `omawin music open` opens Zebar's music panel today). Those move to the shell as native panels first (later the first plugins, roadmap 4.6). The migration ships in the same release as they do, not before.

**Files**

| File | Change |
|---|---|
| `menu-app/Bar/Media.cs` | new: the media widget |
| `menu-app/Bar/Widgets.cs` | `"media" => new MediaWidget()` in `BarWidget.Create` |
| `menu-app/Bar/Bar.cs` | `Native` always true; `Set`, `StopZebar`, the `bar` file go; `LayoutFile` stays `zebar\tokyo-islands\bar.json` (roadmap: until 4.5) |
| `migrations/<yyyy-mm-dd>-zebar-out.ps1` | new |
| `packages.ps1` | drop `glzr-io.zebar` (both lists) |
| `install/zebar-fork.ps1` | delete |
| `install/admin.ps1`, `install/admin-batch.ps1`, `install/demo-ui.ps1` | forks: GlazeWM only |
| `install/desktop.ps1` | no `~/.glzr/zebar` junction, no `build-client.ps1`; `$forkNames` GlazeWM only |
| `install/steps.txt` | `desktop  | GlazeWM, dictation` |
| `bin/ports.ps1`, `bin/omawin.cs` (`SetPorts`, ~:680, :779), `check.ps1` (:187-199) | ports ready when **the GlazeWM fork** is in (Zebar's no longer counts); `ZEBAR_ASSET_PORT` dropped |
| `bin/omawin.cs` | `start-zebar` builtin and `StartZebar()` go |
| `bin/commands.txt` | rows `start zebar`, `restart zebar`, `bar` go |
| `glazewm/config.yaml` | `startup_commands` without `start zebar`; `shutdown_commands: []`; the `window_process: zebar` rule goes; comments that name Zebar |
| `zebar/` | everything goes except `tokyo-islands/bar.json`; `voice-meter.cs` (the native bar uses it, `Bar.cs:270`) moves to `bin/voice-meter.cs` |
| `build/helpers.ps1`, `bin/install-binaries.ps1` (`$helpers`), `check.ps1` (:215-217, :273, :306, :328+) | helpers: `omawin`, `voice-meter` (new path), `omawin-listen` |
| `bin/profile-ram.ps1` | Zebar's budget row goes |
| `docs/native-bar.md`, `docs/architecture.md`, `docs/install.md`, `docs/themes.md`, `manual/` | Zebar removed; native-bar.md step 3 done |
| `themes/render.ps1` | the `theme.css` block goes |

**`menu-app/Bar/Media.cs`** (Windows' media controls: `Windows.Media.Control`, a WinRT API projected by CsWinRT, so AOT-safe)

```csharp
using Windows.Media.Control;

namespace SystemMenu;

// What's playing anywhere on the PC (a browser tab, Spotify, the music player), from Windows' own
// media controls, as Zebar's media widget showed: the title and artist; click plays / pauses,
// right click skips, the wheel goes back and forward. Hidden while nothing plays.
sealed class MediaWidget : BarWidget
{
    readonly Cell cell = new("Media");
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    string text = "";
    bool playing;
    public override FrameworkElement Element => cell;

    public MediaWidget()
    {
        cell.Visibility = Visibility.Collapsed;
        cell.Click = () => _ = session?.TryTogglePlayPauseAsync();
        cell.RightClick = () => _ = session?.TrySkipNextAsync();
        cell.Wheel = up => _ = up > 0 ? session?.TrySkipNextAsync() : session?.TrySkipPreviousAsync();
        _ = StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => Watch(manager.GetCurrentSession());
            Watch(manager.GetCurrentSession());
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void Watch(GlobalSystemMediaTransportControlsSession? next)
    {
        session = next;
        if (next != null)
        {
            next.MediaPropertiesChanged += (s, _) => { if (s == session) _ = ReadAsync(); };
            next.PlaybackInfoChanged += (s, _) => { if (s == session) _ = ReadAsync(); };
        }
        _ = ReadAsync();
    }

    // Raised on worker threads: read there, show on the UI thread (Bar.Refresh -> Update).
    async Task ReadAsync()
    {
        var s = session;
        if (s == null) { text = ""; playing = false; SystemMenu.Bar.Refresh(); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var info = s.GetPlaybackInfo();
            playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            text = string.IsNullOrEmpty(props.Artist) ? props.Title : $"{props.Title} · {props.Artist}";
        }
        catch (Exception ex) { App.Log(ex); text = ""; }
        SystemMenu.Bar.Refresh();
    }

    public override void Update(BarData data)
    {
        cell.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        cell.SetIcon(Glyph(playing ? 0xF03E4 : 0xF040A)); // pause / play (Nerd Font md)
        cell.SetText(text.Length > 40 ? text[..39] + "…" : text);
        cell.Tip = text;
    }

    public override void Paint(Theme theme) { base.Paint(theme); cell.Paint(theme); }
}
```

*(check)* event handler subscriptions on a session replaced later aren't removed (sessions are few; add `-=` with stored delegates if `profile ram` shows growth); `Bar.Refresh` is safe from any thread (it is used from `Tray.Changed`).

**`migrations/<yyyy-mm-dd>-zebar-out.ps1`** (5.1; safe on a new PC; safe twice)

```powershell
# The bar is the menu app's own now (menu-app/Bar, docs/native-bar.md): Zebar is no longer used.
# For this account: stops its Zebar, removes what omawin set up for it (the ~/.glzr/zebar junction
# into the repo, the link only; the bar pick file; ZEBAR_ASSET_PORT; the Zebar-only helpers omawin
# built into ~/.local/bin) and lets winget update Zebar again (omawin had pinned it for its fork).
# Then Zebar itself is uninstalled (the maintainer's call, 2026-10-30: uninstalling what omawin
# no longer needs is fine). A per-user install goes at once; a machine-wide one (older installs put
# it there, with our fork's zebar.exe) needs admin, which an update never asks for (AGENTS.md), so it
# is added to the pending admin list for the user to approve when they choose.
$session = (Get-Process -Id $PID).SessionId
Get-Process zebar -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session |
    Stop-Process -Force -ErrorAction SilentlyContinue

$link = "$HOME\.glzr\zebar"
$item = Get-Item $link -Force -ErrorAction SilentlyContinue
if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    $target = @($item.Target) | Select-Object -First 1
    if ($target -and $target.TrimEnd('\') -match '\\(omawin|windows-dotfiles)\\zebar$') {
        [IO.Directory]::Delete($item.FullName) # the junction only, never what it points to
        Write-Host "  removed $link"
    }
}

Remove-Item (Join-Path $env:LOCALAPPDATA 'launcher\bar') -Force -ErrorAction SilentlyContinue

if ([Environment]::GetEnvironmentVariable('ZEBAR_ASSET_PORT', 'User')) {
    [Environment]::SetEnvironmentVariable('ZEBAR_ASSET_PORT', $null, 'User')
    Write-Host '  removed ZEBAR_ASSET_PORT'
}

# Only the helpers omawin built for Zebar's widgets (bin/install-binaries.ps1 put them there).
$bin = Join-Path $HOME '.local\bin'
foreach ($name in 'cpu-cores', 'win-keys', 'app-icons', 'theme-watch') {
    foreach ($file in "$name.exe", "$name.exe.old") {
        Remove-Item (Join-Path $bin $file) -Force -ErrorAction SilentlyContinue
    }
}

if (Get-Command winget -ErrorAction SilentlyContinue) {
    $pins = winget pin list --id glzr-io.zebar --exact 2>$null | Out-String
    if ($pins -match 'glzr-io\.zebar') { winget pin remove --id glzr-io.zebar --exact 2>$null | Out-Null }
    $machineWide = Test-Path "$env:ProgramFiles\glzr.io\Zebar"
    if (-not $machineWide) {
        # Per user: no admin needed.
        winget uninstall --id glzr-io.zebar --exact --silent --accept-source-agreements 2>$null | Out-Null
        Write-Host '  uninstalled Zebar (the bar is the menu app now)'
    } else {
        # Machine-wide: queued, never prompted here. The admin batch's verbs gain `zebar-remove`
        # (winget uninstall --scope machine, then glazewm-fork's Zebar files and certificate entries).
        $pending = Join-Path $env:LOCALAPPDATA 'launcher\admin-pending.txt'
        if (-not (Select-String -Path $pending -Pattern '^zebar-remove' -Quiet -ErrorAction SilentlyContinue)) {
            Add-Content $pending 'zebar-remove | Uninstall Zebar (no longer used; installed for every account)'
        }
        Write-Host '  Zebar: uninstall waiting for your OK (admin, once) on the welcome page'
    }
}
```

**Zebar's uninstall** (decided 2026-10-30): uninstalled by this migration when it's per user; when machine-wide, queued as `zebar-remove` in the pending admin list (the no-admin rule's one prompt, at the user's moment).

**Ports** (`bin/ports.ps1`, and the same logic in `bin/omawin.cs` `SetPorts` and `check.ps1`):

```powershell
# Only with our GlazeWM fork in place (the official one keeps 6123 whatever this says). Zebar no
# longer counts: the bar is the menu app's, and it reads GLAZEWM_IPC_PORT like everything else.
$ready = $sig -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like 'CN=omawin code signing*' -and
    (Test-Path "$glzr\GlazeWM\cli\glazewm.official.exe")
$ports = [ordered]@{ GLAZEWM_IPC_PORT = "$glazewm" }
```

**Done when:** `omawin check` passes with 0 warnings on a PC that had Zebar; `omawin profile ram` shows no `zebar` / WebView2 processes; sign-in paints the bar (`omawin profile login`) without the 2 s Zebar wait; `grep -ri zebar` outside `docs/`, `migrations/` and `zebar/tokyo-islands/bar.json` finds nothing.

---

## 1.7 Typing never waits: a virtualized launcher list

The launcher's search draws up to 150 rows per key (`Render`, `foreach (var match in list.Take(150))`). Rows are already pooled (`RowView`, `NextRow`), so the cost is binding 150 of them (text runs, icon lookups, tiles) each keystroke. Bind only the rows on screen plus a margin. The list's scrolling is custom (Composition `Translation` on `ListLayer`, `ScrollTo`), so an `ItemsRepeater` would mean replacing it with a `ScrollViewer`; **instead the existing pool is windowed**: same visuals, same motion. Only the launcher's search list (uniform rows, no section titles) is virtualized; menus and pages (short, with titles) keep drawing all rows.

**Files:** `menu-app/MainWindow.xaml.cs`, `menu-app/MainWindow.xaml` (spacers).

**`MainWindow.xaml`**, inside `ListLayer`:

```xml
<StackPanel x:Name="Rows" Spacing="2">
    <Border x:Name="TopSpacer" Visibility="Collapsed" />
    <!-- pooled rows are inserted here, before BottomSpacer -->
    <Border x:Name="BottomSpacer" Visibility="Collapsed" />
</StackPanel>
```

**`MainWindow.xaml.cs`**

```csharp
    // The launcher's search, virtualized: every match is a row in `rows` (its Run), but only the
    // rows on screen and Overscan either side are bound to pooled views; spacers stand for the
    // rest. Scrolling past the bound ones binds the next (ScrollTo -> EnsureBound).
    List<Launcher.Match>? virtualMatches;
    string virtualQuery = "";
    int boundFirst; // the list index of pool[0] while virtualizing
    const int Overscan = 6;

    // MakeRow: the slot goes before BottomSpacer (was Rows.Children.Add(v.Slot)).
    //   Rows.Children.Insert(Rows.Children.Count - 1, v.Slot);

    // `rows` keeps one entry per match; its Border is the bound view's or null.
    // readonly List<(Border? Row, Action Run)> rows = [];

    // The view drawing list row `index`, if it's bound.
    RowView? ViewAt(int index)
    {
        int p = virtualMatches != null ? index - boundFirst : index;
        return p >= 0 && p < used ? pool[p] : null;
    }

    // In Render's launcher branch, in place of the foreach over list.Take(150):
    //   virtualMatches = list; virtualQuery = query;
    //   foreach (var match in list) { var item = match.Item; rows.Add((null, () => Launch(item, query))); }
    //   BindRows(first: 0);
    // and at Render's start: virtualMatches = null; TopSpacer/BottomSpacer collapsed.

    void BindRows(int first)
    {
        var list = virtualMatches!;
        int visible = (int)Math.Ceiling(Math.Max(bodyDip, ViewHeight) / Pitch);
        first = Math.Clamp(first, 0, Math.Max(0, list.Count - 1));
        int last = Math.Min(list.Count, first + visible + 2 * Overscan);
        boundFirst = first;
        used = 0;
        liftedRow = null;
        for (int i = first; i < last; i++)
        {
            var item = list[i].Item;
            var v = NextRow(RowDip - 2, 28);
            v.Index = i; // the list index, not the pool index
            SetTile(v, item.Icon, KindColor(item), 15, item.IconKey);
            SetLabel(v, item.Name, list[i].Positions, dim: false);
            v.Sub.Visibility = Visibility.Collapsed;
            v.Below.Visibility = Visibility.Collapsed;
            SetRight(v, Tag.Text, item.Hint);
            rows[i] = (v.Row, rows[i].Run);
        }
        for (int i = used; i < pool.Count; i++) pool[i].Slot.Visibility = Visibility.Collapsed;
        // StackPanel spacing applies between visible children only (check), so a spacer is its rows'
        // pitch less one gap.
        SetSpacer(TopSpacer, first);
        SetSpacer(BottomSpacer, list.Count - last);
    }

    static void SetSpacer(Border spacer, int rowCount)
    {
        spacer.Visibility = rowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        spacer.Height = Math.Max(0, rowCount * RowDip - 2);
    }

    // After every scroll: rebind when the visible rows run out of bound ones.
    void EnsureBound()
    {
        if (virtualMatches == null) return;
        int top = (int)(scrollY / Pitch), bottom = (int)((scrollY + ViewHeight) / Pitch);
        if (top < boundFirst + (boundFirst > 0 ? 2 : 0) || bottom > boundFirst + used - 2)
        {
            BindRows(top - Overscan);
            if (ViewAt(selected) is { } v) { liftedRow = v; LiftRow(v, true); }
        }
    }
```

Changes at the four places that index the pool by list index:
- `ScrollTo`: call `EnsureBound()` after `scrollY = y;`.
- `ScrollToSelected`: in virtual mode the row's place is `selected * Pitch` (no `ActualOffset` needed): `if (virtualMatches != null) { if (selected == 0) ScrollTo(0, animate); else ScrollIntoView(selected * Pitch, RowDip - 2, Pitch, animate); return; }`.
- `Highlight`: scroll first, then lift, so the selected row is bound: in virtual mode `ScrollToSelected(!jump)` runs before `liftedRow = ViewAt(selected)`.
- `Cascade`: `pool[i].Index - first` (already in 1.4's version).
- `MakeRow`'s `PointerEntered` / `Tapped` use `v.Index` (the list index), unchanged.
- `Render`'s "rows this drawing didn't need" loop is skipped in virtual mode (`BindRows` did it).

Icons stay off the UI thread (`AppIcons` decodes on its STA worker; only `WriteableBitmap` creation is on the UI thread, which it must be).

**Done when:** `omawin profile menu -Typing` shows key → list-painted under 16 ms (median) for every prefix of "visual studio code", at 150% scale; scrolling the full unfiltered list with a held ↓ never shows an empty row.

---

## 1.8 Crisp icons everywhere

**Files**

| File | Change |
|---|---|
| `menu-app/AppIcons.cs` | size per request; `SIIGBF_*` flags; area-average downscale; never up; cache by size; packaged apps' logos; stats for the self test |
| `menu-app/IconSizes.cs` | new: standard sizes, `PixelsFor`, `Downscale` (pure, testable) |
| `menu-app/MainWindow.xaml.cs` (`SetTile`), `MainWindow.Tiles.cs` (`MakeTile`), finder preview (`ShowPreview`) | ask for the drawn size; show 1:1 |
| `menu-app/Bar/Tray.cs`, `Bar/Widgets.cs` (`TrayWidget`) | draw at the bar's pixel size from the best source |
| `menu-app/App.xaml.cs` | `--icon-selftest` |

**`menu-app/IconSizes.cs`**

```csharp
namespace SystemMenu;

// Icon pixel sizes: what to ask Windows for, given how big an icon is drawn and the monitor's
// scale, and a good downscale for when what came back is bigger (never up).
public static class IconSizes
{
    // The sizes apps ship icons at.
    static readonly int[] Standard = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    // The smallest standard size at or above the pixels drawn (256 at most).
    public static int PixelsFor(double units, double scale)
    {
        int needed = (int)Math.Ceiling(units * scale - 0.01);
        foreach (var s in Standard) if (s >= needed) return s;
        return 256;
    }

    // Area-average downscale of premultiplied BGRA (each output pixel is the mean of the input
    // area it covers, partial pixels weighted): as sharp as Fant for downscaling, no API needed.
    public static byte[] Downscale(byte[] src, int w, int h, int dw, int dh)
    {
        if (dw >= w && dh >= h) return src;
        var dst = new byte[dw * dh * 4];
        double sx = (double)w / dw, sy = (double)h / dh;
        for (int y = 0; y < dh; y++)
        {
            double y0 = y * sy, y1 = y0 + sy;
            for (int x = 0; x < dw; x++)
            {
                double x0 = x * sx, x1 = x0 + sx;
                double b = 0, g = 0, r = 0, a = 0, area = 0;
                for (int iy = (int)y0; iy < Math.Min(h, (int)Math.Ceiling(y1)); iy++)
                {
                    double wy = Math.Min(iy + 1, y1) - Math.Max(iy, y0);
                    for (int ix = (int)x0; ix < Math.Min(w, (int)Math.Ceiling(x1)); ix++)
                    {
                        double wgt = wy * (Math.Min(ix + 1, x1) - Math.Max(ix, x0));
                        int k = (iy * w + ix) * 4;
                        b += src[k] * wgt; g += src[k + 1] * wgt; r += src[k + 2] * wgt; a += src[k + 3] * wgt;
                        area += wgt;
                    }
                }
                int o = (y * dw + x) * 4;
                dst[o] = (byte)Math.Round(b / area); dst[o + 1] = (byte)Math.Round(g / area);
                dst[o + 2] = (byte)Math.Round(r / area); dst[o + 3] = (byte)Math.Round(a / area);
            }
        }
        return dst;
    }
}
```

**`menu-app/AppIcons.cs`**: the request carries the pixel size; the callback gets the image and its pixel size.

```csharp
    // SIIGBF flags (shobjidl_core.h): BIGGERSIZEOK lets Windows hand back a larger image than asked
    // (we scale it down ourselves, well); ICONONLY wants the icon, not a thumbnail (apps only:
    // pictures want their thumbnails).
    const int BiggerSizeOk = 0x1, IconOnly = 0x4;
    public sealed record Icon(ImageSource Image, int Pixels);
    public sealed record Stat(string Key, int Requested, int Received, int Drawn);
    public static readonly System.Collections.Concurrent.ConcurrentQueue<Stat> Stats = new(); // for --icon-selftest

    // Calls back on the UI thread with the icon at `pixels` (or smaller, when that's all there is;
    // never stretched up). Cache key: key@pixels.
    public static void Get(string key, int pixels, Action<Icon> done) { /* as Get today, with
        id = key + "@" + pixels; the worker parses it back */ }

    // In Work: (pixels, width, height) = Load(id) where
    static (byte[], int, int) Load(string key, int pixels)
    {
        if (key.StartsWith("big:")) return Fit(Pixels(key[4..], pixels, BiggerSizeOk), pixels, key);
        if (key.StartsWith("path:")) return Fit(Pixels(key[5..], pixels, BiggerSizeOk), pixels, key);
        // A packaged (Store) app: its own logo for this size first (below), else the shell's icon.
        return Fit(PackagedLogo(key, pixels) ?? Pixels(@"shell:AppsFolder\" + key, pixels, IconOnly | BiggerSizeOk), pixels, key);
    }

    // Down to `pixels` if bigger (keeping its shape); left as is if smaller.
    static (byte[], int, int) Fit((byte[] Data, int W, int H) image, int pixels, string key)
    {
        int largest = Math.Max(image.W, image.H);
        if (largest <= pixels) { Stats.Enqueue(new(key, pixels, largest, largest)); return image; }
        int w = Math.Max(1, image.W * pixels / largest), h = Math.Max(1, image.H * pixels / largest);
        Stats.Enqueue(new(key, pixels, largest, pixels));
        return (IconSizes.Downscale(image.Data, image.W, image.H, w, h), w, h);
    }

    // Pixels gains the flags argument: getImage(factory, new SIZE { cx = size, cy = size }, flags, &hbitmap).
```

Packaged apps' logos (`PackagedLogo`): their AppsFolder id is an AUMID, `<PackageFamilyName>!<AppId>`.

```csharp
    // A Store app's logo at the size asked, from its package (targetsize-N assets, unplated when
    // the package has them; check: whether GetLogo prefers altform-unplated, or the shell's
    // ICONONLY image already does and this adds nothing; measure before keeping it).
    static (byte[], int, int)? PackagedLogo(string aumid, int pixels)
    {
        int bang = aumid.IndexOf('!');
        if (bang <= 0) return null;
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser("", aumid[..bang])) // this user's, no admin (check)
                foreach (var entry in package.GetAppListEntries())
                {
                    if (entry.AppUserModelId != aumid) continue;
                    var stream = entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(pixels, pixels))
                        .OpenReadAsync().AsTask().GetAwaiter().GetResult();
                    return Decode(stream); // BitmapDecoder -> premultiplied BGRA (ThemeThumbs' decode, without scaling)
                }
        }
        catch (Exception) { }
        return null;
    }
```

**Showing 1:1** (`MainWindow.xaml.cs` `SetTile`; `MainWindow.Tiles.cs` `MakeTile`; the finder's preview):

```csharp
    double Scale => Content.XamlRoot?.RasterizationScale ?? GetDpiForWindow(hwnd) / 96.0;

    // In SetTile, for a tile of `v.Tile.Width` units, the icon 2 units smaller:
    double units = v.Tile.Width - 2;
    AppIcons.Get(iconKey, IconSizes.PixelsFor(units, Scale), icon =>
    {
        if (v.IconKey != iconKey) return;
        v.Tile.Background = new SolidColorBrush(Colors.Transparent);
        v.Icon.Source = icon.Image;
        // One image pixel on one screen pixel: its size in units is pixels / scale. Smaller than the
        // slot when that's all the app has: centred, not blurred up.
        v.Icon.Width = v.Icon.Height = icon.Pixels / Scale;
        v.Icon.Stretch = Stretch.Uniform;
        v.Icon.HorizontalAlignment = HorizontalAlignment.Center;
        v.Icon.VerticalAlignment = VerticalAlignment.Center;
        v.Icon.Visibility = Visibility.Visible;
        v.Glyph.Visibility = Visibility.Collapsed;
    });
```

(`icon.Pixels` here is the image's largest side as drawn, from `Fit`; make `Icon.Pixels` that value.) Monitor change: the window moves to a monitor of another scale → `Content.XamlRoot.Changed` → when `RasterizationScale` differs from the last, clear `AppIcons`' in-memory map entries for the old size? Not needed: keys carry the size, so a re-render simply asks for the new one; call `Render()` if visible.

The finder's `big:` preview: ask for the preview panel's real pixel size (`PreviewArt.ActualWidth * Scale`, capped at 256 by the API's standard sizes; *(check)* whether `GetImage` returns more than 256 for pictures with `BIGGERSIZEOK`: thumbnails can be larger, so cap at 512 for `big:`).

**Tray** (`Bar/Tray.cs`):

```csharp
    public sealed class Icon
    {
        // ... as now, plus:
        public IntPtr Hicon;   // the app's icon handle (a USER object, valid while the app keeps it)
        public int PixelSize;  // the size Pixels was drawn at
    }

    // The bar's icon size in pixels: TrayWidget sets it from its XamlRoot's scale (15 units today).
    static int target = 24;
    public static void SetPixelSize(int pixels)
    {
        if (pixels == target) return;
        target = pixels;
        lock (icons) foreach (var i in icons) if (i.Hicon != IntPtr.Zero) { i.Pixels = Picture(i.Hicon, pixels) ?? i.Pixels; i.PixelSize = pixels; i.Stamp++; }
        Changed?.Invoke();
    }

    // In Read, NIF_ICON: icon.Hicon = handle; icon.Pixels = Picture(handle, target); icon.PixelSize = target.

    // The icon at `size` pixels. From the icon's resource when it came from one (CopyImage with
    // LR_COPYFROMRESOURCE picks the closest frame the app ships; check: it fails for icons made in
    // memory, and for another process's icon); else drawn at its own size, centred, when that's
    // smaller than `size`, so it's never stretched up.
    static byte[]? Picture(IntPtr hicon, int size)
    {
        if (hicon == IntPtr.Zero) return null;
        var best = CopyImage(hicon, 1 /* IMAGE_ICON */, size, size, 0x4000 /* LR_COPYFROMRESOURCE */);
        var source = best != IntPtr.Zero ? best : hicon;
        int native = best != IntPtr.Zero ? size : NativeSize(hicon);
        int drawn = Math.Min(native, size), at = (size - drawn) / 2;
        try { return Draw(source, size, at, drawn); } // DrawIconEx(dc, at, at, source, drawn, drawn, ...) into a size x size DIB, as Picture today
        finally { if (best != IntPtr.Zero) DestroyIcon(best); }
    }

    // The icon's own size, from its colour bitmap (GetIconInfo + GetObject).
    static int NativeSize(IntPtr hicon)
    {
        if (!GetIconInfo(hicon, out var info)) return Size;
        try
        {
            var bm = new BITMAP();
            GetObject(info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask, Marshal.SizeOf<BITMAP>(), ref bm);
            return info.hbmColor != IntPtr.Zero ? bm.bmWidth : bm.bmWidth; // a mask-only icon is twice as tall, as wide
        }
        finally { if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor); if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask); }
    }

    [DllImport("user32.dll")] static extern IntPtr CopyImage(IntPtr image, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    // BITMAP and GetObject as in AppIcons.cs
```

`TrayWidget.Update`: `Tray.SetPixelSize(IconSizes.PixelsFor(15, Element.XamlRoot?.RasterizationScale ?? 1))`; the bitmap is `new WriteableBitmap(icon.PixelSize, icon.PixelSize)`; the `Image` is `icon.PixelSize / scale` units square (1:1). Apps that draw state into their icon keep working: each `NIM_MODIFY` brings a new handle, drawn again.

**`--icon-selftest`** (`App.xaml.cs`, before the single-copy check like `--qs-selftest`): in its own process, asks `AppIcons` for the first 30 apps of `AppsFolder` at 26 and 48 units for scales 1.0, 1.5 and 2.0, waits, and prints `Stats`: key, requested, received, drawn, and `UPSCALED` for any `Drawn > Received` (must be none). Needs a UI thread for `WriteableBitmap`: run it inside `Application.Start` with a hidden window, as `--render-about` does.

**Done when:** `--icon-selftest` prints no `UPSCALED`; screenshots of the launcher list, tiles, finder and tray at 100%, 150% and 200% beside Start and the taskbar look as sharp (🖥).

---

## 1.9 Theme pictures ready before you look

**Files**

| File | Change |
|---|---|
| `build/theme-thumbs.py` | new: thumbnails from each theme's backgrounds (Pillow, pinned) |
| `.github/workflows/release.yml` | job `thumbs`; `release` packs `omawin-theme-thumbs-<tag>.zip` |
| `build/release.ps1` | packs `<InDir>\thumbs` |
| `bin/install-binaries.ps1` | unpacks it into `%LOCALAPPDATA%\launcher\thumbnails\shipped` |
| `menu-app/ThemeThumbs.cs` | shipped first; size by scale; `Preload` |
| `menu-app/MainWindow.Tiles.cs` | the palette gradient as the placeholder |
| `menu-app/ThemeBackgrounds.cs` | new: full backgrounds fetched at low priority |
| `themes/import-omarchy.ps1` | prints the themes that are new (CI makes their thumbnails from the JSON anyway) |

Thumbnails are JPEG: every Windows decodes it (WebP needs the WebP extension, not on every Windows 10), and a 320-pixel photo is ~15-30 KB.

**`build/theme-thumbs.py`** (CI only; `python -m pip install pillow==11.0.0`)

```python
"""Theme thumbnails for the release: each theme's backgrounds (themes/*.json, "backgrounds"),
downloaded once, scaled to 320 and 640 pixels wide (100-250% scale), saved as
<out>/<theme>--<picture>-<width>.jpg. Named like menu-app/ThemeThumbs.cs's keys, so it finds them.

    python build/theme-thumbs.py --themes themes --out out/thumbs [--all-backgrounds]
"""
import argparse, json, pathlib, io, urllib.request
from PIL import Image  # pillow, pinned in the workflow

WIDTHS = (320, 640)

def stem(url: str) -> str:
    return pathlib.PurePosixPath(url.split("?")[0]).stem

def fetch(url: str, repo: pathlib.Path) -> bytes:
    if url.startswith(("http://", "https://")):
        with urllib.request.urlopen(url, timeout=60) as r:
            return r.read()
    return (repo / url).read_bytes()  # a path in the repo

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--themes", type=pathlib.Path, required=True)
    p.add_argument("--out", type=pathlib.Path, required=True)
    p.add_argument("--all-backgrounds", action="store_true", help="every background (the background grid), not only the first")
    a = p.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)
    repo = a.themes.parent
    for theme_file in sorted(a.themes.glob("*.json")):
        theme = json.loads(theme_file.read_text(encoding="utf-8"))
        backgrounds = theme.get("backgrounds") or []
        for url in backgrounds if a.all_backgrounds else backgrounds[:1]:
            image = Image.open(io.BytesIO(fetch(url, repo))).convert("RGB")
            for width in WIDTHS:
                height = max(1, round(image.height * width / image.width))
                small = image.resize((width, height), Image.LANCZOS)
                target = a.out / f"{theme_file.stem}--{stem(url)}-{width}.jpg"
                small.save(target, "JPEG", quality=85, optimize=True, progressive=True)
                print(f"{target.name}  {target.stat().st_size} bytes")

if __name__ == "__main__":
    main()
```

(`themes/*.json` includes `starship.json` etc.; files that aren't themes have no `backgrounds` and are skipped. JS would be Bun per AGENTS.md; Python here only because Pillow decodes WebP and runs anywhere; it runs only in CI.)

**`release.yml`**: a job beside `helpers`:

```yaml
  # The theme picker's pictures, made once here instead of on every PC (roadmap 1.9).
  thumbs:
    needs: meta
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - uses: actions/setup-python@<sha> # v5 (pin by SHA as the others)
        with:
          python-version: '3.12'
      - run: python -m pip install --quiet pillow==11.0.0
      - run: python build/theme-thumbs.py --themes themes --out out/thumbs --all-backgrounds
      - uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: thumbs
          path: out/thumbs
          if-no-files-found: error
```

and `release: needs: [meta, helpers, menu, music, thumbs]`. **`build/release.ps1`**, after the helpers: `if (Test-Path (Join-Path $InDir 'thumbs')) { Add-Zip (Join-Path $InDir 'thumbs') "omawin-theme-thumbs-$Tag.zip" }`. When the bundle (install-speed.md, stage 1) lands, this ZIP moves into it.

**`bin/install-binaries.ps1`**, a piece after the menu app (same pattern: `Get-PieceRelease`, `Save-Installed`):

```powershell
    # --- the theme picker's pictures (%LOCALAPPDATA%\launcher\thumbnails\shipped) ----------------
    if ($WantMenu) {
        $key = Get-SourceKey @('themes')
        if (-not (Test-Installed 'thumbs' $key { Test-Path (Join-Path $env:LOCALAPPDATA 'launcher\thumbnails\shipped') })) {
            $zip = Get-PieceRelease 'omawin-theme-thumbs-*.zip' @('themes') $false # never built here: the menu app makes its own
            if ($zip) {
                $dest = Join-Path $env:LOCALAPPDATA 'launcher\thumbnails\shipped'
                New-Item -ItemType Directory -Force $dest | Out-Null
                Expand-Archive -Path $zip -DestinationPath $dest -Force
                Write-Note 'theme pictures from the release'
                Save-Installed 'thumbs' (Get-ReleaseKey $key)
            }
        }
    }
```

*(check)* `Get-PieceRelease`'s third argument (can build) set to `$false` makes it return `$null` without a release rather than fail.

**`menu-app/ThemeThumbs.cs`**

```csharp
    static readonly string Shipped = Path.Combine(Dir, "shipped");

    // The shipped picture for this scale (320 up to 125%, else 640), if there is one.
    static string? ShippedFile(string key, double scale)
    {
        var file = Path.Combine(Shipped, $"{key}-{(scale <= 1.25 ? 320 : 640)}.jpg");
        return File.Exists(file) ? file : null;
    }

    // Get(file, theme, scale, done): shipped first, then the made one, then make it (today's path).
    // The cache key gains the width; DecodePixelWidth = the tile's pixel width (1.8's rule), not 320.

    // Every theme's picture decoded into memory at the picker's size, a few at a time, at low
    // priority, once the menu app is idle after start-up: Style > Theme then shows every tile in the
    // first frame. ~24 small images, a few MB.
    public static void Preload(double scale, int tilePixels)
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        var themes = MenuModel.Themes();
        int next = 0;
        void One()
        {
            if (next >= themes.Count) return;
            var (file, theme) = themes[next++];
            Get(file, theme, scale, _ => { });
            ui.TryEnqueue(DispatcherQueuePriority.Low, One); // one per idle slot
        }
        ui.TryEnqueue(DispatcherQueuePriority.Low, One);
    }
```

A `BitmapImage` created from a URI decodes lazily, when first drawn *(check)*: for a real preload use `await image.SetSourceAsync(stream)` on a `FileStream.AsRandomAccessStream()` with `DecodePixelWidth` set first, which decodes then. `App.OnLaunched`: after `Program.Trace("ready")`, `Task.Delay(3000).ContinueWith(_ => queue.TryEnqueue(DispatcherQueuePriority.Low, () => ThemeThumbs.Preload(scale, tilePixels)))` with the primary monitor's scale.

**Placeholder** (`MainWindow.Tiles.cs`, the `"swatch"` case): the frame's background before the picture arrives is the palette as a gradient instead of a flat colour:

```csharp
                var g = t.Gradient.Length >= 2 ? t.Gradient : [t.Background, t.Accent];
                var palette = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
                for (int i = 0; i < g.Length; i++)
                    palette.GradientStops.Add(new GradientStop { Color = Hex(g[i]), Offset = (double)i / (g.Length - 1) });
                palette.Opacity = 0.55; // under the "Aa" in the theme's text colour
                var frame = new Border { Background = palette, /* ... as now */ };
```

**`menu-app/ThemeBackgrounds.cs`** (new): full backgrounds at low priority, so a first switch never downloads.

```csharp
using Windows.Networking.Connectivity;

namespace SystemMenu;

// Every theme's backgrounds, fetched quietly in the background into the folder
// themes/set-theme.ps1 reads them from (%LOCALAPPDATA%\launcher\backgrounds\<theme>), one at a time,
// so switching to any theme never waits on a download. On a metered connection only the current
// theme's. Started an hour after the menu app (not competing with sign-in), and each day after.
public static class ThemeBackgrounds
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static void Start() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromHours(1));
        while (true)
        {
            try { await FetchAll(); } catch (Exception ex) { App.Log(ex); }
            await Task.Delay(TimeSpan.FromDays(1));
        }
    });

    static bool Metered()
    {
        var cost = NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
        return cost == null || cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming;
    }

    static async Task FetchAll()
    {
        var current = MenuModel.CurrentThemeName();
        foreach (var (file, theme) in MenuModel.Themes().OrderBy(t => t.File != current))
        {
            if (file != current && Metered()) return;
            foreach (var url in theme.Backgrounds.Where(b => b.StartsWith("https://")))
            {
                var target = Path.Combine(MenuModel.StateDir, "backgrounds", file, Path.GetFileName(new Uri(url).LocalPath));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(target + ".part", bytes);
                File.Move(target + ".part", target, overwrite: true); // same .part rename as set-theme.ps1
                await Task.Delay(2000); // gentle
            }
        }
    }
}
```

`App.OnLaunched`: `ThemeBackgrounds.Start();` with the others. *(check)* disk use: 24 themes × ~3 backgrounds × 1-5 MB ≈ 100-300 MB; if `profile ram`/disk review says too much, fetch only each theme's first background and the rest on demand.

**The background grid** (mission-todo track 5): the release already ships every background's thumbnail (`--all-backgrounds`), so that grid reads `shipped\<theme>--<picture>-<w>.jpg` the same way.

**Done when:** on a fresh install (Windows Sandbox), Style › Theme shows every theme's picture in the first frame (screenshot at open), with no network traffic from the menu app at that moment; switching to any theme an hour after install doesn't wait on a download.

---

## 1.10 Community themes in the picker

**Files**

| File | Change |
|---|---|
| `.github/workflows/community-themes.yml` | new: weekly index build, published to the rolling release `community-themes` |
| `build/community-themes.ps1` | new: the list, each theme's data at a pinned commit, palettes converted |
| `build/theme-thumbs.py` | `--community <index.json>` mode: thumbnails from `preview.png` or the first background |
| `themes/community-extra.txt` | new: hand-kept repos (one `owner/repo` per line) |
| `menu-app/CommunityThemes.cs` | new: fetch, verify, load the index |
| `menu-app/Json.cs` | `CommunityIndex`, `CommunityTheme` in the context |
| `menu-app/MenuModel.cs` | `Theme.Preview` (an installed theme's `preview.png`) |
| `menu-app/MainWindow.Tiles.cs`, `MainWindow.xaml.cs` | the picker's Built-in / Community chips (Tab), search by name / author / light / dark, live preview, Enter installs |
| `menu-app/ThemeThumbs.cs` | `community:<name>` keys; `Theme.Preview` first for installed themes |
| `themes/theme.ps1` | `add`, `search`, `preview`, `unpreview`; install at a commit; credit in `list` |
| `themes/convert-theme.ps1` | write `preview` (path to the clone's `preview.png`) into the theme JSON |
| `themes/set-theme.ps1`, `themes/render.ps1`, `Bar.cs`, `MainWindow.xaml.cs` | `theme-preview` (a temporary palette) honoured while it exists |
| `bin/commands.txt` | `theme add`, `theme search`, `theme preview`; `theme install` stays as an alias |

**Explore first** (roadmap): fetch `https://omarchy.org/themes/` from a session or CI run that can reach it and record its shape (static HTML with GitHub links, or a JSON it loads). The script below reads GitHub repo links out of the HTML, which works for either a static page or one whose data is inlined; if the page loads a JSON, read that URL instead and update `Get-OmarchyOrgRepos`.

**Index format** (`index.json` inside `community-themes.zip`):

```json
{
  "generated": "2026-11-02T03:17:00Z",
  "themes": [
    {
      "name": "everforest-dark",
      "title": "Everforest Dark",
      "repo": "someone/omarchy-everforest-dark-theme",
      "commit": "4f2c…40 hex",
      "author": "someone",
      "license": "MIT",
      "stars": 41,
      "updated": "2026-10-20T10:00:00Z",
      "mode": "dark",
      "theme": { "name": "Everforest Dark", "mode": "dark", "background": "#2d353b", "accent": "#a7c080", "...": "omawin's theme JSON (themes/<name>.json format), backgrounds as raw URLs at the commit" },
      "colors": "accent = \"#a7c080\"\n…  (colors.toml as text, for render.ps1 during a preview)",
      "thumb": "thumbs/everforest-dark--preview"
    }
  ]
}
```

**`build/community-themes.ps1`** (pwsh 7 on `windows-latest`, where `convert-theme.ps1` runs as on a PC)

```powershell
<#
.SYNOPSIS
    The community theme index (roadmap 1.10): every Omarchy community theme's palette and picture,
    read at a pinned commit without cloning, for the menu app's Community chip.

.DESCRIPTION
    Sources, deduplicated by owner/repo: omarchy.org/themes (GitHub links on the page), GitHub repos
    with the omarchy-theme topic or named omarchy-*-theme, and themes/community-extra.txt. For each:
    the repo's facts (GitHub API), its default branch's commit, and at that commit colors.toml (or
    alacritty.toml), light.mode and preview.png (or the first file in backgrounds/). The palette is
    converted with themes/convert-theme.ps1 -Untrusted, as `omawin theme install` does. Only data is
    read: no code from a theme ever runs here or on a PC.

        ./build/community-themes.ps1 -Out out/community    (GITHUB_TOKEN for the API's rate limit)
#>
param([Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$headers = @{ 'User-Agent' = 'omawin-community-themes'; Accept = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
$slug = '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$'

function Get-OmarchyOrgRepos {
    try {
        $html = (Invoke-WebRequest 'https://omarchy.org/themes/' -UseBasicParsing -TimeoutSec 60).Content
        [regex]::Matches($html, 'github\.com/([A-Za-z0-9-]+/[A-Za-z0-9._-]+?)(?:\.git)?(?=["''/?#<\s])') | ForEach-Object { $_.Groups[1].Value }
    } catch { Write-Warning "omarchy.org/themes: $_" }
}
function Get-TopicRepos {
    foreach ($q in 'topic:omarchy-theme', 'omarchy- in:name theme in:name') {
        for ($page = 1; $page -le 10; $page++) {
            $r = Invoke-RestMethod "https://api.github.com/search/repositories?q=$([uri]::EscapeDataString($q))&per_page=100&page=$page" -Headers $headers
            $r.items | Where-Object { $_.name -match '^omarchy-.+-theme$' -or $_.topics -contains 'omarchy-theme' } | ForEach-Object full_name
            if ($r.items.Count -lt 100) { break }
        }
    }
}
function Get-Raw([string]$Repo, [string]$Commit, [string]$Path) {
    try { (Invoke-WebRequest "https://raw.githubusercontent.com/$Repo/$Commit/$Path" -UseBasicParsing -TimeoutSec 60) } catch { $null }
}

$extra = Get-Content (Join-Path $Repo 'themes\community-extra.txt') -ErrorAction SilentlyContinue | Where-Object { $_ -and -not $_.StartsWith('#') }
$repos = @(@(Get-OmarchyOrgRepos) + @(Get-TopicRepos) + @($extra) | ForEach-Object { $_.Trim().TrimEnd('/') } |
    Where-Object { $_ -match $slug -and $_ -notmatch '^basecamp/omarchy$' } | Sort-Object -Unique)
Write-Host "$($repos.Count) candidate repos"

$work = Join-Path $Out 'work'; New-Item -ItemType Directory -Force $work, (Join-Path $Out 'previews') | Out-Null
$themes = foreach ($full in $repos) {
    try {
        $info = Invoke-RestMethod "https://api.github.com/repos/$full" -Headers $headers
        if ($info.archived -or $info.private) { continue }
        $commit = (Invoke-RestMethod "https://api.github.com/repos/$full/commits/$($info.default_branch)" -Headers $headers).sha
        $name = ($info.name -replace '^omarchy-', '' -replace '-theme$', '').ToLowerInvariant()
        if ($name -cnotmatch '^[a-z0-9_][a-z0-9._+-]*$') { continue } # theme.ps1's Get-ThemeName rule
        $src = Join-Path $work $name; New-Item -ItemType Directory -Force $src | Out-Null
        $colors = Get-Raw $full $commit 'colors.toml'
        if ($colors) { [IO.File]::WriteAllText("$src\colors.toml", $colors.Content) }
        else {
            $alacritty = Get-Raw $full $commit 'alacritty.toml'
            if (-not $alacritty) { Write-Host "  skip $full (no palette)"; continue }
            [IO.File]::WriteAllText("$src\alacritty.toml", $alacritty.Content)
        }
        if (Get-Raw $full $commit 'light.mode') { Set-Content "$src\light.mode" '' }
        # Backgrounds: names only, from the tree at that commit (no download here).
        $tree = Invoke-RestMethod "https://api.github.com/repos/$full/git/trees/${commit}?recursive=0" -Headers $headers
        $bgTree = $tree.tree | Where-Object { $_.path -eq 'backgrounds' -and $_.type -eq 'tree' }
        $backgrounds = @()
        if ($bgTree) {
            $backgrounds = @((Invoke-RestMethod $bgTree.url -Headers $headers).tree | Where-Object { $_.path -match '\.(jpe?g|png|webp)$' } |
                Sort-Object path | ForEach-Object { "https://raw.githubusercontent.com/$full/$commit/backgrounds/$($_.path)" })
        }
        $preview = if ($tree.tree | Where-Object path -eq 'preview.png') { "https://raw.githubusercontent.com/$full/$commit/preview.png" } elseif ($backgrounds) { $backgrounds[0] }
        & (Join-Path $Repo 'themes\convert-theme.ps1') -Source $src -Name $name -Out $src -Untrusted | Out-Null
        $theme = Get-Content "$src\$name.json" -Raw | ConvertFrom-Json
        $theme.backgrounds = $backgrounds
        [pscustomobject][ordered]@{
            name = $name; title = $theme.name; repo = $full; commit = $commit
            author = $info.owner.login; license = $info.license.spdx_id; stars = $info.stargazers_count; updated = $info.pushed_at
            mode = $theme.mode; theme = $theme; colors = (Get-Content "$src\colors\$name.toml" -Raw)
            preview = $preview; thumb = "thumbs/$name--preview"
        }
        Write-Host "  ok   $full ($name, $commit)"
    } catch { Write-Warning "$full`: $_" }
}
# Two repos giving the same theme name: the one with more stars.
$themes = @($themes | Group-Object name | ForEach-Object { $_.Group | Sort-Object stars -Descending | Select-Object -First 1 })
@{ generated = (Get-Date).ToUniversalTime().ToString('o'); themes = $themes } | ConvertTo-Json -Depth 8 |
    Set-Content (Join-Path $Out 'index.json') -Encoding utf8
Remove-Item $work -Recurse -Force
```

*(check)* `convert-theme.ps1 -Out $src` writes `$src\<name>.json` and `$src\colors\<name>.toml` (its layout under `-Out` is themes/'s); and that it reads nothing but `colors.toml` / `alacritty.toml` / `light.mode` / `backgrounds/` (it must not need the whole clone).

**`build/theme-thumbs.py`**, community mode: `--community out/community/index.json --out out/community/thumbs` makes `<name>--preview-320.jpg` and `-640.jpg` from each entry's `preview` URL (the same resize and save as above).

**`.github/workflows/community-themes.yml`**

```yaml
# The community theme index for the menu app's Community chip (roadmap 1.10), rebuilt weekly and
# published as the assets of the rolling release "community-themes": community-themes.zip
# (index.json + thumbs/) and community-themes.zip.sha256. The menu app fetches those.
name: Community themes

on:
  schedule:
    - cron: '17 3 * * 1'   # Mondays 03:17 UTC
  workflow_dispatch:

concurrency:
  group: community-themes
  cancel-in-progress: false

permissions:
  contents: read

jobs:
  index:
    runs-on: windows-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - uses: actions/setup-python@<sha> # v5, pinned by SHA
        with:
          python-version: '3.12'
      - name: The index
        shell: pwsh
        env:
          GITHUB_TOKEN: ${{ github.token }}
        run: ./build/community-themes.ps1 -Out out/community
      - name: Thumbnails
        shell: pwsh
        run: |
          python -m pip install --quiet pillow==11.0.0
          python build/theme-thumbs.py --community out/community/index.json --out out/community/thumbs
      - name: Pack
        shell: pwsh
        run: |
          New-Item -ItemType Directory -Force dist | Out-Null
          Compress-Archive -Path out/community/index.json, out/community/thumbs -DestinationPath dist/community-themes.zip -CompressionLevel Optimal
          $hash = (Get-FileHash dist/community-themes.zip -Algorithm SHA256).Hash.ToLowerInvariant()
          Set-Content dist/community-themes.zip.sha256 "$hash  community-themes.zip" -NoNewline
          Write-Host "community-themes.zip $((Get-Item dist/community-themes.zip).Length) bytes, $hash"
      - name: Publish (the rolling release)
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $tag = 'community-themes'
          gh release view $tag --repo '${{ github.repository }}' *> $null
          if ($LASTEXITCODE -ne 0) {
            gh release create $tag --repo '${{ github.repository }}' --title 'Community themes (rebuilt weekly)' --notes 'The index the theme picker reads. Not an omawin release.' --latest=false --target main
          }
          gh release upload $tag dist/community-themes.zip dist/community-themes.zip.sha256 --repo '${{ github.repository }}' --clobber
```

`--latest=false` keeps `releases/latest` (what `bin/release.ps1` and `omawin update` read) on omawin's real releases *(check: `bin/release.ps1` picks releases by `v*` tag, not "latest", or filters this one out)*.

**`menu-app/CommunityThemes.cs`** (new)

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SystemMenu;

public sealed class CommunityIndex { public string Generated { get; set; } = ""; public List<CommunityTheme> Themes { get; set; } = []; }
public sealed class CommunityTheme
{
    public string Name { get; set; } = "", Title { get; set; } = "", Repo { get; set; } = "", Commit { get; set; } = "";
    public string Author { get; set; } = "", License { get; set; } = "", Mode { get; set; } = "dark", Updated { get; set; } = "";
    public int Stars { get; set; }
    public Theme Theme { get; set; } = new();
    public string Colors { get; set; } = "";
    public string Thumb { get; set; } = "";
}

// The community theme index (built weekly by .github/workflows/community-themes.yml): fetched in the
// background at most once a day, checked against its SHA-256, unpacked into
// %LOCALAPPDATA%\launcher\community (index.json, thumbs\). The picker's Community chip reads it.
public static class CommunityThemes
{
    const string Base = "https://github.com/mjveilleux/omawin/releases/download/community-themes/";
    static string Dir => Path.Combine(MenuModel.StateDir, "community");
    static string IndexFile => Path.Combine(Dir, "index.json");
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
    static CommunityIndex? loaded;
    static DateTime loadedStamp;

    // What's there now (empty until the first fetch); read again when the file changed.
    public static List<CommunityTheme> All()
    {
        var stamp = File.GetLastWriteTimeUtc(IndexFile);
        if (loaded == null || stamp != loadedStamp)
        {
            try { loaded = JsonSerializer.Deserialize(File.ReadAllText(IndexFile), Json.Default.CommunityIndex); }
            catch { loaded = new CommunityIndex(); }
            loadedStamp = stamp;
        }
        return loaded?.Themes ?? [];
    }

    public static string ThumbFile(CommunityTheme t, double scale) =>
        Path.Combine(Dir, t.Thumb.Replace('/', '\\') + (scale <= 1.25 ? "-320.jpg" : "-640.jpg"));

    // From App.OnLaunched: a minute after start, then daily.
    public static void Start() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try { await Refresh(); } catch (Exception ex) { App.Log(ex); }
            await Task.Delay(TimeSpan.FromDays(1));
        }
    });

    static async Task Refresh()
    {
        var sumFile = Path.Combine(Dir, "community-themes.zip.sha256");
        var sum = (await http.GetStringAsync(Base + "community-themes.zip.sha256")).Split(' ')[0].Trim();
        if (File.Exists(sumFile) && File.ReadAllText(sumFile).Trim() == sum && File.Exists(IndexFile)) return; // unchanged
        var zip = await http.GetByteArrayAsync(Base + "community-themes.zip");
        if (Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() != sum) throw new InvalidDataException("community-themes.zip: SHA-256 mismatch");
        // Unpacked aside, then swapped in: the picker never reads half an index.
        var incoming = Dir + ".new";
        if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
        using (var stream = new MemoryStream(zip)) ZipFile.ExtractToDirectory(stream, incoming); // refuses entries outside the folder
        File.WriteAllText(Path.Combine(incoming, "community-themes.zip.sha256"), sum);
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        Directory.Move(incoming, Dir);
    }
}
```

`Json.cs`: `[JsonSerializable(typeof(CommunityIndex))]`. The SHA-256 is from the same release, so it guards against a broken download, not a forged one; the index is data only (colours, pictures, names), and installing re-reads the theme from its repo at the index's commit.

**`Theme.Preview`** (`MenuModel.cs`): `public string? Preview { get; set; }`. `convert-theme.ps1` writes `"preview": "<Source>\preview.png"` when that file exists (installed themes: the clone's). `ThemeThumbs.Get`: source = `theme.Preview ?? theme.Backgrounds[0]`, and the key uses `preview` as the picture name then; for a community tile (`SwatchFile = "community:<name>"`) it reads `CommunityThemes.ThumbFile` directly (nothing to make).

**The picker** (`MainWindow.Tiles.cs` `ThemePicker`, `MainWindow.xaml.cs`):

```csharp
    bool communityThemes;              // the picker's Community chip (Tab)
    string? previewing;                // a community theme shown live, not installed
    Microsoft.UI.Dispatching.DispatcherQueueTimer? previewTimer;

    TileLayout ThemePicker(string query)
    {
        var layout = new TileLayout();
        // The chips, as the finder's (Scopes): Built in | Community. Tab moves between them.
        SetThemeChips();
        if (!communityThemes)
        {
            layout.Flow(MenuModel.Themes().Where(t => Matches(query, t.Theme.Name, null, t.Theme.Mode))
                .Select(t => new TileSpec
                {
                    Key = "theme:" + t.File, Style = "swatch", Label = t.Theme.Name, Swatch = t.Theme, SwatchFile = t.File, Current = t.File == themeName,
                    Color = Hex(t.Theme.Accent), Run = () => SetTheme(t.File),
                }), rowHeight: SwatchRowDip);
            return layout;
        }
        var installed = MenuModel.Themes().Select(t => t.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        layout.Flow(CommunityThemes.All()
            .Where(c => Matches(query, c.Title, c.Author, c.Mode))
            .OrderByDescending(c => c.Stars)
            .Select(c => new TileSpec
            {
                Key = "community:" + c.Name, Style = "swatch", Label = c.Title, Sub = "by " + c.Author, Swatch = c.Theme,
                SwatchFile = "community:" + c.Name, Current = installed.Contains(c.Name),
                Color = Hex(c.Theme.Accent), Run = () => InstallCommunity(c),
            }), rowHeight: SwatchRowDip);
        return layout;
    }

    // Name, author, or "light" / "dark".
    static bool Matches(string query, string name, string? author, string mode) =>
        query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || mode.Equals(query, StringComparison.OrdinalIgnoreCase);

    // Selecting a community tile previews it across the desktop after a short pause (so arrowing
    // past tiles doesn't re-theme everything on each); leaving the picker or Esc puts yours back.
    void OnThemeSelected(TileSpec spec)  // called from Highlight when pickingTheme
    {
        previewTimer ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        previewTimer.Stop();
        if (!spec.Key.StartsWith("community:")) { EndPreview(); return; }
        var name = spec.Key["community:".Length..];
        previewTimer.Interval = TimeSpan.FromMilliseconds(350);
        previewTimer.IsRepeating = false;
        previewTimer.Tick += Fire;
        previewTimer.Start();
        void Fire(object? s, object e)
        {
            previewTimer.Tick -= Fire;
            if (previewing == name) return;
            previewing = name;
            MenuModel.Omawin("theme", "preview", name);
        }
    }

    void EndPreview()
    {
        if (previewing == null) return;
        previewing = null;
        MenuModel.Omawin("theme", "unpreview");
    }

    void InstallCommunity(CommunityTheme c)
    {
        previewing = null; // `theme add` applies it: no unpreview
        MenuModel.Omawin("theme", "add", c.Name);
        HideMenu();
    }
```

Tab in the picker: in `OnKey` / `PressKey`, `case "tab": if (pickingTheme) { communityThemes = !communityThemes; selected = 0; Render(); }`. `Back` / `HideMenu` while `previewing != null` call `EndPreview()`. The footer credits the selected community theme: `"<title> by <author> · <license> · <repo>"`.

**Preview without installing** (`themes/theme.ps1`):

```powershell
    'preview' {
        # A community theme across the desktop for a moment, from the index's palette, nothing
        # installed: written as a temporary theme "preview" in %LOCALAPPDATA%\launcher\themes\preview\,
        # named in launcher\theme-preview, which render.ps1, the menu app and the bar prefer while it
        # exists. The wallpaper is the theme's thumbnail (already on disk).
        $entry = Get-CommunityTheme $Target
        $dir = Join-Path $Root 'preview'
        New-Item -ItemType Directory -Force (Join-Path $dir 'colors') | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir 'colors\preview.toml'), $entry.colors)
        $entry.theme | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $dir 'preview.json') -Encoding utf8
        Set-Content (Join-Path $State 'theme-preview') $Target -NoNewline
        & "$PSScriptRoot\set-theme.ps1" -Preview -Name $Target
    }
    'unpreview' {
        Remove-Item (Join-Path $State 'theme-preview') -ErrorAction SilentlyContinue
        & "$PSScriptRoot\set-theme.ps1" (Get-CurrentTheme) -Preview # the colours back; the wallpaper too
    }
    'add' {
        # By name from the community index (omawin theme search), or any git link (as install).
        if (Test-GitUrl $Target) { $Action = 'install' } # falls through to install below
        else {
            $entry = Get-CommunityTheme $Target
            Install-Theme "https://github.com/$($entry.repo)" -Commit $entry.commit
            return
        }
    }
    'search' {
        $words = @($Target -split '\s+' | Where-Object { $_ })
        foreach ($t in (Get-CommunityIndex).themes | Sort-Object stars -Descending) {
            $text = "$($t.name) $($t.title) $($t.author) $($t.mode)"
            if (@($words | Where-Object { $text -notmatch [regex]::Escape($_) }).Count -eq 0) {
                Write-Host ("{0,-24} {1,-6} by {2,-18} {3,5}*  github.com/{4}" -f $t.name, $t.mode, $t.author, $t.stars, $t.repo)
            }
        }
    }
```

with helpers:

```powershell
function Get-CommunityIndex {
    $file = Join-Path $State 'community\index.json'
    if (-not (Test-Path $file)) { throw 'No community index yet (the menu app fetches it a minute after it starts; or the network is off).' }
    Get-Content $file -Raw | ConvertFrom-Json
}
function Get-CommunityTheme([string]$Name) {
    $t = (Get-CommunityIndex).themes | Where-Object name -eq $Name | Select-Object -First 1
    if (-not $t) { throw "No community theme '$Name' (omawin theme search <words>)." }
    $t
}
```

`Install-Theme` is today's `'install'` block made a function, with `-Commit`: when given, the clone is pinned to it (GitHub serves a commit by SHA):

```powershell
        if ($Commit) {
            git init --quiet $incoming
            git -C $incoming fetch --depth 1 --quiet -- $Url $Commit
            git -C $incoming checkout --quiet FETCH_HEAD
        } else { git clone --depth 1 --quiet -- $Url $incoming }
```

`ValidateSet` gains `'add', 'search', 'preview', 'unpreview'`. `'update'` shows what changed before pulling (`git -C $clone fetch` then `git -C $clone log --oneline HEAD..FETCH_HEAD` and `git diff --stat`), then asks (`Read-Host 'Update? [y/N]'`) when in a terminal. `'list'` prints the repo (`git -C $clone remote get-url origin`) beside installed themes.

**`themes/set-theme.ps1`**: `-Preview`: only what's quick and colour-only (render.ps1: GlazeWM, noctty, Windows Terminal; the menu app and bar see `theme-preview`), the wallpaper from the thumbnail; not the lock screen, editors, VS Code, RStudio, btop, nor `launcher\theme`. `render.ps1`: `if (Test-Path "$State\theme-preview") { $Name = 'preview' }` (its palette is `themes\preview\colors\preview.toml` via `Find-ThemeFile`, with the folder layout above *(check `Find-ThemeFile`'s path: it looks in `launcher\themes\<relative>`, so write the preview's files as `launcher\themes\colors\preview.toml` and `launcher\themes\preview.json` instead, and delete them on `unpreview`)*). `MenuModel.CurrentThemeName()` and `Bar`'s theme watcher: `theme-preview` present → `"preview"`.

**`bin/commands.txt`**:

```
theme add | Add a community theme by name (omawin theme search), or any theme by its git link; applies it | pwsh themes/theme.ps1 add
theme search | Community themes matching words (name, author, light, dark), with their repos | pwsh themes/theme.ps1 search
theme preview | Show a community theme across the desktop without installing it; omawin theme preview off puts yours back (the picker does this as you move) | pwsh themes/theme.ps1 preview
```

(`theme install` stays, as the alias the roadmap keeps. `preview off` maps to `unpreview` inside theme.ps1.)

**Done when:** Style › Theme › Tab shows every indexed community theme's picture and colours in the first frame (pictures from `community\thumbs`, already on disk); resting on one re-themes the desktop within ~1 s and Esc puts yours back; Enter installs it at the index's commit with no link to copy; `omawin theme search nord` lists matches; the weekly workflow publishes `community-themes.zip` under ~5 MB.

---

## After Phase 1

- Tick each item in [mission-todo.md](../mission-todo.md) with its commit; write measured numbers (1.1's baseline, 1.7's typing, 1.2's key → frame) into the roadmap.
- Open questions this file creates: `open-questions/emit-or-exec.md` (1.2).
- Manual pages to update as items land: the keys page (nothing rebinds, but Win+Space now works while GlazeWM is paused), Style › Theme (Community chip), `toggle animations`.
