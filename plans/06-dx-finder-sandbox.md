# 06: Omarchy's developer experience, the finder and sandboxes (roadmap phases 5, 6, 7)

> **Uncompiled sketch.** Nothing here has been built or run. It names real files and functions as of 2026-10-30 and follows [AGENTS.md](../AGENTS.md). **(check)** marks an API, flag or behaviour that needs confirming on a real PC before it's trusted. Build it, test it, then correct this file.

Ground rules that apply to every section:

- Menu app (`shell/menu-app/`): C# 12, .NET 9, WinUI 3, **Native AOT**: no `[ComImport]` (use `[GeneratedComInterface]` like `Audio.cs`, or vtable calls like `AppIcons.cs`), no reflection-based JSON (use `JsonDocument` / `Utf8JsonReader`).
- `bin/omawin.cs`: **C# 5** (no `$""`, no `?.`, no expression-bodied members, no `out var`).
- Scripts that run at install or inside a sandbox: **Windows PowerShell 5.1**.
- Every feature is an omawin command (a row in `bin/commands.txt`); keys are **proposed, never bound** until the maintainer confirms.
- User data in `%LOCALAPPDATA%\launcher\` (`MenuModel.StateDir`); personal definitions in the account folder (`MenuModel.Work`) or the shared folder (`MenuModel.Shared`), read through `MenuModel.LayeredLines`.

A shared helper several sections use:

### `shell/menu-app/GlazeIpc.cs` (new): one-shot GlazeWM IPC

`Tiling.cs` keeps a long-lived subscription; this is the request / response form (a fresh socket per call costs ~1 ms on localhost).

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SystemMenu;

// One request to GlazeWM's IPC server, the same messages as `glazewm query windows` /
// `glazewm command ...`; the reply's "data" (null when GlazeWM isn't up or said no).
public static class GlazeIpc
{
    static string Port => Environment.GetEnvironmentVariable("GLAZEWM_IPC_PORT") is { Length: > 0 } p ? p.Trim() : "6123";

    public static async Task<JsonDocument?> Ask(string message, int timeoutMs = 1500)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://localhost:{Port}"), cts.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cts.Token);
            var buffer = new byte[256 * 1024];
            using var ms = new MemoryStream();
            while (true)
            {
                var part = await socket.ReceiveAsync(buffer, cts.Token);
                ms.Write(buffer, 0, part.Count);
                if (part.EndOfMessage) break;
            }
            var doc = JsonDocument.Parse(ms.ToArray());
            // {"messageType":"client_response","clientMessage":"...","data":{...},"error":null,"success":true}
            if (doc.RootElement.TryGetProperty("success", out var ok) && ok.GetBoolean()) return doc;
            doc.Dispose();
            return null;
        }
        catch { return null; }
    }

    public static Task Command(string command) => Ask("command " + command);

    // Every managed window: (container id, hwnd, process name, title).
    public static async Task<List<(string Id, IntPtr Hwnd, string Process, string Title)>> Windows()
    {
        var list = new List<(string, IntPtr, string, string)>();
        using var doc = await Ask("query windows");
        if (doc == null) return list;
        foreach (var w in doc.RootElement.GetProperty("data").GetProperty("windows").EnumerateArray())
            list.Add((w.GetProperty("id").GetString()!, (IntPtr)w.GetProperty("handle").GetInt64(),
                w.GetProperty("processName").GetString() ?? "", w.GetProperty("title").GetString() ?? ""));
        return list;
    }

    public static Task Focus(string containerId) => Command($"focus --container-id {containerId}");
}
```

---

# Phase 5: Omarchy's developer experience

## 5.3 Web apps and TUIs

**Shape.**

- Definition (portable, in git with the account folder): `~/omawin-account/launcher/webapps.txt` (layered: repo starter set, shared folder, account), one per line: `<slug> | <name> | <url>`.
- Machine state (rebuildable): `%LOCALAPPDATA%\launcher\webapps\<slug>\profile\` (Edge `--user-data-dir`), `...\<slug>\icon.png`.
- Launcher item, appended to the **account's** `launcher/items.txt`: `%LOCALAPPDATA%\launcher\webapps\<slug>\icon.png | <name> | omawin.exe webapp open <slug>`. The icon column becomes "a glyph, or a path to a .png/.ico" (change below).
- Starter set: `default/launcher/webapps.txt` in the repo (Omarchy's list; **check against Omarchy's current `install/` web apps** before copying): HEY, Basecamp, WhatsApp, Google Photos, Google Contacts, Google Messages, ChatGPT, YouTube, GitHub, X, Figma, Discord, Zoom. Not installed by default; `omawin webapp install` with no args offers them in fzf.

**`bin/commands.txt` rows**

```
webapp install | A web app: its own window, profile and icon, in the launcher: omawin webapp install <name> <url> [icon-url] | pwsh bin/webapp.ps1 install
webapp remove | Remove a web app (its launcher item, profile and icon): omawin webapp remove <name> | pwsh bin/webapp.ps1 remove
webapp list | The web apps installed here | pwsh bin/webapp.ps1 list
webapp open | Open a web app, or bring its window to the front if it's open: omawin webapp open <slug> | app --webapp
tui install | A terminal program as a launcher item, launch-or-focus: omawin tui install <name> <command> | pwsh bin/tui.ps1 install
tui remove | Remove a terminal program's launcher item | pwsh bin/tui.ps1 remove
```

(The roadmap writes `omawin install webapp`; `install` is already `install.ps1`, and omawin.cs resolves the longest group+name first, so both spellings could coexist: add `install webapp | ... | pwsh bin/webapp.ps1 install` as an alias row if wanted.)

### `bin/webapp.ps1` (new, PS 5.1)

```powershell
# Web apps, like Omarchy's omarchy-webapp-install: a site in its own Edge window (--app), with its
# own profile folder (signed in separately from your browser), its icon from the site's manifest,
# and a launcher item that opens it or brings it to the front (the menu app: WebApps.cs).
param([Parameter(Position = 0)][string]$Verb = 'list', [Parameter(Position = 1)][string]$Name,
      [Parameter(Position = 2)][string]$Url, [Parameter(Position = 3)][string]$Icon)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

$account = & "$PSScriptRoot\user-layer.ps1"
$defs = Join-Path $account 'launcher\webapps.txt'
$items = Join-Path $account 'launcher\items.txt'
$state = Join-Path $env:LOCALAPPDATA 'launcher\webapps'

function Get-Slug([string]$n) { ($n.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') }

function Get-Url([string]$u) {
    if ($u -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*:') { $u = "https://$u" }
    # --app= runs javascript:, file: and data: as documents: only http(s), no whitespace.
    if ($u -match '\s' -or $u -notmatch '^https?://') { throw "A web app's address must be http(s): $u" }
    $u
}

# The best icon the site offers, square, 192 px or more (512 preferred): the web app manifest's
# icons, then apple-touch-icon, then /favicon.ico. Saved as icon.png (or .ico).
function Save-Icon([string]$site, [string]$dir, [string]$given) {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($given) { $candidates.Add($given) }
    try {
        $page = Invoke-WebRequest $site -UseBasicParsing -TimeoutSec 10
        $base = [Uri]$page.BaseResponse.ResponseUri  # after redirects (check: PS 5.1 property name)
        $html = $page.Content
        $m = [regex]::Match($html, '<link[^>]+rel=["'']?manifest["'']?[^>]*>', 'IgnoreCase')
        if ($m.Success) {
            $href = [regex]::Match($m.Value, 'href=["'']([^"'']+)').Groups[1].Value
            $manifestUrl = [Uri]::new($base, [Net.WebUtility]::HtmlDecode($href))
            $manifest = Invoke-RestMethod $manifestUrl -TimeoutSec 10
            $icons = @($manifest.icons | Where-Object { $_.src } | ForEach-Object {
                $size = 0
                foreach ($s in "$($_.sizes)".Split(' ')) { $w = ($s -split 'x')[0]; if ($w -match '^\d+$') { $size = [Math]::Max($size, [int]$w) } }
                # maskable icons have padding for a circle crop: a last resort
                $rank = if ("$($_.purpose)" -match 'maskable' -and "$($_.purpose)" -notmatch 'any') { 1 } else { 0 }
                [pscustomobject]@{ Src = [Uri]::new($manifestUrl, $_.src).AbsoluteUri; Size = $size; Rank = $rank; Svg = "$($_.type)" -match 'svg' -or $_.src -match '\.svg' }
            } | Where-Object { -not $_.Svg })
            $best = $icons | Sort-Object Rank, @{ e = { if ($_.Size -ge 512) { 0 } elseif ($_.Size -ge 192) { 1 } else { 2 } } }, @{ e = { -$_.Size } } | Select-Object -First 1
            if ($best) { $candidates.Add($best.Src) }
        }
        foreach ($rel in 'apple-touch-icon', 'icon') {
            $l = [regex]::Match($html, "<link[^>]+rel=[""']?$rel[""']?[^>]*>", 'IgnoreCase')
            if ($l.Success) { $candidates.Add([Uri]::new($base, [regex]::Match($l.Value, 'href=["'']([^"'']+)').Groups[1].Value).AbsoluteUri) }
        }
        $candidates.Add([Uri]::new($base, '/favicon.ico').AbsoluteUri)
    } catch { Write-Host "  (couldn't read $site for its icon: $($_.Exception.Message))" }

    foreach ($c in $candidates) {
        $ext = if ($c -match '\.ico(\?|$)') { '.ico' } else { '.png' }
        $out = Join-Path $dir "icon$ext"
        try {
            Invoke-WebRequest $c -OutFile "$out.part" -UseBasicParsing -TimeoutSec 10
            $bytes = [IO.File]::ReadAllBytes("$out.part")
            $png = $bytes.Length -gt 8 -and $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50
            $ico = $bytes.Length -gt 4 -and $bytes[0] -eq 0 -and $bytes[2] -eq 1
            # (A JPEG or WebP named .png: re-encode with System.Drawing, which reads JPEG; WebP: check.)
            if ($png -or $ico) { Move-Item "$out.part" $out -Force; return $out }
            Remove-Item "$out.part" -ErrorAction SilentlyContinue
        } catch { Remove-Item "$out.part" -ErrorAction SilentlyContinue }
    }
    $null
}

function Add-Line([string]$file, [string]$line, [string]$key) {
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    $lines = @(); if (Test-Path $file) { $lines = @(Get-Content $file -Encoding UTF8) }
    $lines = @($lines | Where-Object { $_ -notmatch [regex]::Escape($key) })
    # UTF-8 without BOM, as the menu app reads it (items.txt holds Nerd Font glyphs).
    [IO.File]::WriteAllLines($file, [string[]]($lines + $line), (New-Object Text.UTF8Encoding $false))
}

switch ($Verb) {
    'install' {
        if (-not $Name -or -not $Url) { throw 'omawin webapp install <name> <url> [icon-url]' }
        if ($Name -match '[|/\\]') { throw "A web app's name can't hold | / or \: $Name" }
        $slug = Get-Slug $Name; $Url = Get-Url $Url
        $dir = Join-Path $state $slug
        New-Item -ItemType Directory -Force (Join-Path $dir 'profile') | Out-Null
        $iconFile = Save-Icon $Url $dir $Icon
        Add-Line $defs "$slug | $Name | $Url" "^$([regex]::Escape($slug)) \|"
        $glyph = if ($iconFile) { $iconFile } else { [char]::ConvertFromUtf32(0xF059F) }  # web
        Add-Line $items "$glyph | $Name | omawin.exe webapp open $slug" "webapp open $slug$"
        Write-Host "  $Name -> the launcher (Win+Space)"
    }
    'remove' {
        $slug = Get-Slug $Name
        foreach ($f in $defs, $items) {
            if (Test-Path $f) {
                $keep = @(Get-Content $f -Encoding UTF8 | Where-Object { $_ -notmatch "^$slug \|" -and $_ -notmatch "webapp open $slug$" })
                [IO.File]::WriteAllLines($f, [string[]]$keep, (New-Object Text.UTF8Encoding $false))
            }
        }
        # Its profile is the web app's sign-in and data: moved aside, never deleted (AGENTS.md).
        $dir = Join-Path $state $slug
        if (Test-Path $dir) { Move-Item $dir "$dir.backup-$(Get-Date -Format yyyyMMddHHmmss)" }
    }
    default { if (Test-Path $defs) { Get-Content $defs | Where-Object { $_ -and $_ -notmatch '^#' } } }
}
```

`bin/tui.ps1` is the same shape with no icon fetch: the item line is `<glyph> | <name> | omawin.exe launch terminal --title <name> -- <command>` and launch-or-focus matches the window title (below).

### Launcher icon from a file (`shell/menu-app/Launcher.cs`)

```csharp
// LaunchItem.IconKey: a .png / .ico path in the icon column is the item's picture (AppIcons
// "path:" goes through IShellItemImageFactory, which gives a PNG's own thumbnail).
public string? IconKey => AppId
    ?? (Icon.Length > 4 && (Icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || Icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        ? "path:" + Environment.ExpandEnvironmentVariables(Icon) : null)
    ?? (BuiltIn == "vpn" && Vpn.IconFile(...) is { } ico ? "path:" + ico : null);
// and the row drawing must not print the path as a glyph: where SetTile gets item.Icon, pass ""
// when IconKey came from the icon column.
```

### `shell/menu-app/WebApps.cs` (new): open or focus

Launch-or-focus by process identity: a web app's top-level windows belong to the Edge browser process started with **its** `--user-data-dir`, so the window's PID's command line names the profile. Reading another same-user process's command line: `NtQueryInformationProcess(ProcessCommandLineInformation = 60)` (Windows 8.1+), microseconds, no WMI.

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SystemMenu;

public sealed record WebApp(string Slug, string Name, string Url);

public static class WebApps
{
    public static string Dir(string slug) => Path.Combine(MenuModel.StateDir, "webapps", slug);

    public static List<WebApp> Load() => [.. MenuModel.LayeredLines("webapps.txt")
        .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray()).Where(p => p.Length == 3)
        .Select(p => new WebApp(p[0], p[1], p[2]))
        .GroupBy(w => w.Slug).Select(g => g.Last())]; // the account's line wins

    static string? Edge()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
        {
            var exe = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    // `omawin webapp open <slug>` (App.HandleRequest: --webapp <slug>).
    public static async Task Open(string slug)
    {
        var app = Load().FirstOrDefault(w => w.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (app == null) { Snip.Say($"No web app \"{slug}\" (omawin webapp list)"); return; }
        string profile = Path.Combine(Dir(app.Slug), "profile");

        foreach (var w in await GlazeIpc.Windows())
        {
            if (!w.Process.Equals("msedge", StringComparison.OrdinalIgnoreCase)) continue;
            GetWindowThreadProcessId(w.Hwnd, out uint pid);
            var cmd = CommandLine(pid);
            if (cmd != null && cmd.Contains(profile, StringComparison.OrdinalIgnoreCase))
            {
                await GlazeIpc.Focus(w.Id); // moves to its workspace too
                return;
            }
        }
        var edge = Edge();
        if (edge == null) { Snip.Say("Web apps need Microsoft Edge"); return; }
        Directory.CreateDirectory(profile);
        var info = new ProcessStartInfo(edge) { UseShellExecute = false };
        info.ArgumentList.Add("--app=" + app.Url);
        info.ArgumentList.Add("--user-data-dir=" + profile);
        info.ArgumentList.Add("--no-first-run");
        info.ArgumentList.Add("--no-default-browser-check");
        // Its own taskbar group and icon (check: Edge derives an AUMID per user-data-dir).
        Process.Start(info);
    }

    // --- a process's command line ---------------------------------------------------------
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returned);

    public static string? CommandLine(uint pid)
    {
        IntPtr process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out int size); // STATUS_INFO_LENGTH_MISMATCH, size set
            if (size <= 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (NtQueryInformationProcess(process, 60, buffer, size, out _) != 0) return null;
                // UNICODE_STRING { ushort Length; ushort MaximumLength; (pad); IntPtr Buffer }
                int length = Marshal.ReadInt16(buffer);
                IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return Marshal.PtrToStringUni(text, length / 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(process); }
    }
}
```

`App.HandleRequest`: `else if (args.Length > 1 && args[0] == "--webapp") _ = WebApps.Open(args[1]);`
TUIs: `else if (args.Length > 1 && args[0] == "--tui") ...` matches `GlazeIpc.Windows()` by `Title == name` (the terminal sets it from `--title`), else runs the item's command.

**Done when:** `omawin webapp install GitHub github.com` puts GitHub with its icon in Win+Space; Enter opens it; Enter again focuses the same window.
**Risks:** separate profiles mean separate sign-ins (Omarchy shares the browser's profile; offer `--shared-profile` that uses `--profile-directory=Default` and title matching instead); sites without a manifest icon get a blurry favicon.

---

## 5.4 The key cheat sheet from `config/glazewm/config.yaml`

**Format** (added to `config/glazewm/config.yaml`; existing prose comments stay above):

```yaml
keybindings:
  # section: Windows
  # Focus a window in a direction. Win+arrows like Omarchy ...
  # desc: Focus the window to the left
  - commands: ['focus --direction left']
    bindings: ['lwin+left', 'alt+h', 'alt+left']
  ...
  # section: Desktop
  # desc: The launcher
  - commands: ['shell-exec --hide-window omawin.exe launcher']
    bindings: ['lwin+space']
```

Rules: `# section: <name>` sets the section for following bindings; `# desc: <text>` is the **last comment line** right before `- commands:`. Bindings inside `binding_modes:` get section `<mode name> mode`. A binding without `desc` is shown with its command as the description (and `omawin check` warns, so they get one). `default/launcher/keys.txt` stays only for keys GlazeWM doesn't own (herdr, the finder's, in-app keys); its header says so.

### `shell/menu-app/Pages.cs`: `Shortcuts`

```csharp
// The cheat sheet: GlazeWM's bindings (config/glazewm/config.yaml, read from the repo's template: the
// placeholders don't touch bindings) with their "# desc:" comments, then default/launcher/keys.txt's
// keys GlazeWM doesn't own (herdr, the finder). Enter on a GlazeWM row runs its commands.
public sealed record Shortcut(string Section, string Keys, string What, string[]? Commands = null);

public static class Shortcuts
{
    public static List<Shortcut> Load()
    {
        var list = new List<Shortcut>();
        try { list.AddRange(FromGlazeConfig(File.ReadAllLines(Path.Combine(MenuModel.Repo, "config", "glazewm", "config.yaml")))); }
        catch (Exception ex) { App.Log(ex); }
        try
        {
            list.AddRange(MenuModel.LayeredLines("keys.txt")
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray()).Where(p => p.Length == 3)
                .Select(p => new Shortcut(p[0], p[1], p[2])));
        }
        catch { }
        return list;
    }

    // A line scanner, not a YAML parser (none under AOT without reflection): enough for
    // config.yaml's shape, where each binding is "- commands: [...]" then "bindings: [...]",
    // each a one-line flow list of single-quoted strings.
    public static IEnumerable<Shortcut> FromGlazeConfig(string[] lines)
    {
        string? block = null, mode = null, section = null, desc = null;
        string[]? commands = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                block = line.TrimEnd(':'); mode = null; section = null; desc = null; commands = null;
                continue;
            }
            if (block is not ("keybindings" or "binding_modes")) continue;
            var t = line.Trim();
            if (block == "binding_modes" && t.StartsWith("- name:")) { mode = Unquote(t["- name:".Length..]); continue; }
            if (t.StartsWith("# section:")) { section = t["# section:".Length..].Trim(); continue; }
            if (t.StartsWith("# desc:")) { desc = t["# desc:".Length..].Trim(); continue; }
            if (t.StartsWith('#') || t.Length == 0) continue;
            if (t.StartsWith("- commands:")) { commands = FlowList(t["- commands:".Length..]); continue; }
            if (t.StartsWith("bindings:") && commands != null)
            {
                var keys = FlowList(t["bindings:".Length..]).Select(KeyLabel);
                yield return new Shortcut(mode != null ? $"{mode} mode" : section ?? "Desktop",
                    string.Join("  /  ", keys), desc ?? string.Join(", ", commands), mode == null ? commands : null);
                desc = null; commands = null;
            }
        }
    }

    // ['a', 'b c'] -> [a, b c]
    static string[] FlowList(string s)
    {
        s = s.Trim();
        if (!s.StartsWith('[')) return [Unquote(s)];
        var items = new List<string>();
        var m = System.Text.RegularExpressions.Regex.Matches(s, @"'((?:[^']|'')*)'|""((?:[^""\\]|\\.)*)""");
        foreach (System.Text.RegularExpressions.Match x in m)
            items.Add(x.Groups[1].Success ? x.Groups[1].Value.Replace("''", "'") : x.Groups[2].Value);
        return [.. items];
    }

    static string Unquote(string s) => s.Trim().Trim('\'', '"');

    // lwin+shift+s -> Win+Shift+S
    public static string KeyLabel(string binding) => string.Join("+", binding.Split('+').Select(k => k.Trim().ToLowerInvariant() switch
    {
        "lwin" or "rwin" => "Win", "alt" or "lmenu" => "Alt", "ctrl" or "lctrl" or "control" => "Ctrl", "shift" or "lshift" => "Shift",
        "space" => "Space", "enter" or "return" => "Enter", "escape" => "Esc",
        "left" => "←", "right" => "→", "up" => "↑", "down" => "↓",
        var k1 when k1.Length == 1 => k1.ToUpperInvariant(),
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    }));
}
```

`MainWindow.Pages.cs`, the `"keys"` case: the row's action runs the binding.

```csharp
AddRow(Glyph(0xF030C), k.Keys, color, k.Commands is { } cmds ? () =>
{
    HideMenu(); // focus goes back to the window you were in, so "focus --direction" etc. act on it
    _ = Task.Run(async () => { await Task.Delay(60); foreach (var c in cmds) await GlazeIpc.Command(c); });
} : () => { }, Tag.Text, k.Section, sub: k.What);
SetKeys(("↑↓", "move"), ("↵", "run"), ("esc", "back"));
```

`bin/check.ps1`: warn for each `- commands:` under `keybindings:` whose previous non-blank line isn't `# desc:` (same scan in PowerShell). Unit test (plain C#, runs in the cloud): `FromGlazeConfig` over the real `config/glazewm/config.yaml` yields a `Win+Space` row with a description, and a `resize mode` row.

---

## 5.5 Notifications and the OSD

### Toasts: `shell/menu-app/Notify.cs` (new)

Windows App SDK 1.7 (`SystemMenu.csproj`), unpackaged and self-contained. `AppNotificationManager` works unpackaged since 1.4 *(check under Native AOT and `WindowsAppSDKSelfContained`)*; the handler must be attached **before** `Register()`.

```csharp
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace SystemMenu;

// omawin's own notifications (install finished, update available, reminders, low battery) and
// `omawin notify <text>`, as Windows toasts in Action Center; the bottom-of-screen note when
// Windows won't take them (Focus on, or registration failed).
public static class Notify
{
    static bool registered;

    // App start (App.OnLaunched, after the window exists).
    public static void Start()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, e) => Invoked(e.Arguments);
            // Display name and icon for an unpackaged app (check: Register(string, Uri) overload, 1.5+).
            AppNotificationManager.Default.Register("omawin", new Uri(Path.Combine(AppContext.BaseDirectory, "omawin.png")));
            registered = true;
        }
        catch (Exception ex) { App.Log(ex); }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { AppNotificationManager.Default.Unregister(); } catch { } };
    }

    public static void Show(string title, string? body = null, string? action = null, string? actionLabel = null)
    {
        if (!registered) { Snip.Say(body == null ? title : $"{title} · {body}"); return; }
        try
        {
            var b = new AppNotificationBuilder().AddText(title);
            if (body != null) b.AddText(body);
            if (action != null) b.AddArgument("do", action)
                .AddButton(new AppNotificationButton(actionLabel ?? "Open").AddArgument("do", action));
            AppNotificationManager.Default.Show(b.BuildNotification());
        }
        catch (Exception ex) { App.Log(ex); Snip.Say(title); }
    }

    // A click on the toast or its button: "do" is an omawin command line ("update", "launcher packages").
    static void Invoked(IDictionary<string, string> args)
    {
        if (args.TryGetValue("do", out var command))
            MenuModel.Omawin(command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // `omawin remind 25m Stand up`: a toast later (lost if the menu app restarts; fine for minutes).
    public static void Remind(TimeSpan after, string text) =>
        _ = Task.Delay(after).ContinueWith(_ => Show("Reminder", text));
}
```

- `App.HandleRequest`: `--notify <text…>` → `Notify.Show(string.Join(' ', args[1..]))`; `--remind <25m|1h> <text…>`.
- Toast activation when the app **isn't** running starts the exe with an activation argument *(check: `AppInstance.GetCurrent().GetActivatedEventArgs()` with `ExtendedActivationKind.AppNotification`)*; `Program.Main` must then hand it to the running copy like any request.
- `commands.txt`:
  ```
  notify | A notification from omawin: omawin notify <text> | app --notify
  remind | A reminder later: omawin remind 25m <text> | app --remind
  ```
- Callers: `install.ps1` at the end (`omawin.exe notify "omawin installed" ` when it ran > 60 s); `bin/update-available.ps1` (an "Update" button: `do=update`); low battery in `QuickSettings.cs` via `Windows.System.Power.PowerManager.RemainingChargePercentChanged` at 15 % and 5 % (once per discharge).

### OSD: `shell/menu-app/Osd.cs` (new), drawn like `Note` (`Snip.cs:549`)

```csharp
// The volume / brightness OSD: a small bar at the bottom centre of the monitor with the mouse,
// 1.2 s, never takes focus. Same plain Win32 window kind as the note (Win.Window), drawn by GDI+.
sealed class Osd : Win.Window
{
    static Osd? shown;
    string glyph = ""; int percent; bool muted; readonly float scale;

    Osd() : base("omawin osd", 0x80 | 0x8 | 0x80000 | 0x08000000 | 0x20, 0x80000000) // tool, topmost, layered, noactivate, transparent; popup
        => scale = Win.DpiAt(Win.Cursor()) / 96f;

    // From any thread: Snip.Post marshals to the overlay thread, as Note does.
    public static void Show(string glyph, int percent, bool muted = false) => Snip.Post(() =>
    {
        var osd = shown ??= new Osd();
        (osd.glyph, osd.percent, osd.muted) = (glyph, Math.Clamp(percent, 0, 100), muted);
        var area = Win.MonitorAt(Win.Cursor(), work: true);
        int w = (int)(260 * osd.scale), h = (int)(44 * osd.scale);
        Win.SetLayeredWindowAttributes(osd.Hwnd, 0, 240, 2);
        Win.SetWindowPos(osd.Hwnd, (IntPtr)(-1), area.X + (area.Width - w) / 2, area.Bottom - h - (int)(90 * osd.scale), w, h, 0x0040 | 0x0010);
        Win.InvalidateRect(osd.Hwnd, IntPtr.Zero, false); // (check: add to Win if missing)
        Win.SetTimer(osd.Hwnd, 1, 1200, IntPtr.Zero);       // restarts the timer on each change
    });

    protected override IntPtr Message(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case 0x0113: Win.ShowWindow(Hwnd, 0); return IntPtr.Zero; // WM_TIMER: hide (kept for reuse)
            case 0x0014: return (IntPtr)1;
            case 0x000F:
                // Paint: the theme's surface (MenuModel.LoadTheme(current)), the glyph in the icon font
                // (JetBrainsMono Nerd Font), a track in Selection and the level in Accent (Red when
                // muted), the number on the right. As Note's paint, with Graphics.FromHdc.
                break;
        }
        return base.Message(message, wParam, lParam);
    }
}
```

When it shows: every volume change through omawin (Win+A page, `MainWindow.Audio.cs` `Adjust`), every `omawin brightness up/down` (`QuickSettings.Command`), and changes from anywhere for the default speaker via the endpoint volume callback (`Audio.cs` already declares `RegisterControlChangeNotify`; implement `IAudioEndpointVolumeCallback` as `[GeneratedComClass]` *(check)*). Windows' own flyout still shows for hardware volume keys (there's no supported switch to hide it); to avoid two at once, the callback path shows the OSD only for changes that **didn't** come from a media key (track the last `WM_APPCOMMAND`-free change: if the change arrives within 50 ms of a VK_VOLUME_* keydown seen by a low-level hook… **not worth it**: default to showing ours only for omawin-initiated changes and brightness, where Windows shows nothing on external monitors).

---

## 5.6 Idle lock and screensaver

`shell/menu-app/IdleLock.cs` (new), started in `App.OnLaunched` next to `StayAwake.Reset()`. On by default (Omarchy's defaults: screensaver 150 s, lock 300 s); `%LOCALAPPDATA%\launcher\idle-lock-off` turns it off.

```csharp
using System.Runtime.InteropServices;

namespace SystemMenu;

// Omarchy's hypridle: after 150 s idle the screensaver, after 300 s Windows' own lock
// (LockWorkStation). Not while stay awake is on, media plays, or something is full screen
// (a game, a presentation, a video call shared full screen). A 5 s poll of GetLastInputInfo:
// Windows has no "user went idle" event for a normal app (check: none better than polling;
// RegisterPowerSettingNotification GUID_SESSION_USER_PRESENCE fires only at the display-off timeout).
public static class IdleLock
{
    public static int ScreensaverSeconds = 150, LockSeconds = 300; // later: from omawin's settings catalog
    static string OffFile => Path.Combine(MenuModel.StateDir, "idle-lock-off");
    public static bool On => !File.Exists(OffFile);

    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public static void Start() => new Thread(Run) { IsBackground = true, Name = "idle" }.Start();

    static void Run()
    {
        bool savered = false, locked = false;
        while (true)
        {
            Thread.Sleep(5000);
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) continue;
                uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime); // wraps every 49.7 days; uint math handles it
                if (idleMs < 5000) { savered = locked = false; continue; } // back: the next idle period counts again
                if (!On || StayAwake.On || Busy() || MediaPlaying()) continue;
                if (!savered && idleMs >= ScreensaverSeconds * 1000) { savered = true; Screensaver(); }
                if (!locked && idleMs >= LockSeconds * 1000) { locked = true; LockWorkStation(); }
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    // QUNS_BUSY (2, a full-screen app), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4).
    static bool Busy() => SHQueryUserNotificationState(out int s) == 0 && s is 2 or 3 or 4;

    // Anything playing through Windows' media controls (browsers, Spotify, omawin's music player
    // when it registers SMTC). WinRT, works unpackaged (check under AOT).
    static bool MediaPlaying()
    {
        var manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
        return manager.GetSessions().Any(s => s.GetPlaybackInfo()?.PlaybackStatus ==
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    // Omarchy's screensaver is a full-screen terminal running a text effect (tte) on its logo;
    // ours the same, in the default terminal, closed by any key (the script exits on input).
    static void Screensaver() =>
        MenuModel.Omawin("launch", "terminal", "--title", "omawin-screensaver", "--", "pwsh", "-NoProfile", "-File",
            Path.Combine(MenuModel.Repo, "bin", "screensaver.ps1"));

    public static void Toggle()
    {
        try { if (On) File.WriteAllText(OffFile, ""); else File.Delete(OffFile); } catch (Exception ex) { App.Log(ex); }
        Snip.Say(On ? "Idle lock: on · locks after 5 min" : "Idle lock: off");
    }
}
```

- `config/glazewm/config.yaml` window rule: `- commands: ['set-fullscreen --shown-on-top']` `match: [{ window_title: { equals: 'omawin-screensaver' } }]` (with `on: ['manage', 'title_change']`).
- `bin/screensaver.ps1`: hides the cursor, runs `tte` (if installed through mise; else a simple colour-cycling ASCII logo) until `[Console]::KeyAvailable`. Optional: skip the screensaver entirely if `tte` is missing.
- `commands.txt`: `toggle idle-lock | Lock the PC after 5 minutes idle (Omarchy's default), or stop | app --toggle idle-lock`; `App.HandleRequest`: `--toggle idle-lock` → `IdleLock.Toggle()`. The system menu's stay-awake row gets a sibling with the same `TagFor` treatment.
- Interacts with Windows' own "screen off after" / "sleep after": untouched; stay awake overrides both, as today.

---

## 5.7 Scratchpad (GlazeWM fork, branch `omawin`)

Model: a workspace marked `special: true` that's **never displayed**; `move --workspace scratchpad` hides a window there (it's cloaked like any off-screen workspace's); `toggle-scratchpad` brings its windows to the focused workspace **floating, centred, on top**, and sends them back on the next toggle (i3's model, which needs no new rendering path).

`config/glazewm/config.yaml` (omawin):

```yaml
workspaces:
  - name: '1'
  # ...
  - name: 'scratchpad'
    special: true
# proposed keys (confirm first): Omarchy's Super+S toggles, Super+Alt+S sends the window there
#  - commands: ['toggle-scratchpad']
#    bindings: ['lwin+s']
#  - commands: ['move --workspace scratchpad']
#    bindings: ['lwin+alt+s']
```

### `packages/wm-common/src/parsed_config.rs`

```rust
#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all(serialize = "camelCase"))]
pub struct WorkspaceConfig {
  pub name: String,
  #[serde(default)]
  pub display_name: Option<String>,
  #[serde(default)]
  pub bind_to_monitor: Option<u32>,
  #[serde(default = "default_bool::<false>")]
  pub keep_alive: bool,
  /// A hidden holding workspace (the scratchpad): never displayed, never
  /// picked for a monitor, skipped by next/prev workspace.
  #[serde(default = "default_bool::<false>")]
  pub special: bool,
}
```

(`InvokeUpdateWorkspaceConfig` and `update_workspace_config.rs`: carry `special` through unchanged; it isn't settable at runtime.)

### `packages/wm-common/src/app_command.rs`

```rust
  /// Shows the scratchpad's windows floating and centred on the focused
  /// workspace, or sends them back if they're shown.
  ToggleScratchpad {
    #[clap(long, default_value = "scratchpad")]
    name: String,
  },
```

### `packages/wm/src/models/monitor.rs`

```rust
  /// The workspace this monitor shows: the first in focus order that isn't
  /// special (a special workspace is never displayed).
  pub fn displayed_workspace(&self) -> Option<Workspace> {
    self
      .child_focus_order()
      .filter_map(|child| child.as_workspace().cloned())
      .find(|workspace| !workspace.config().special)
  }
```

(`Workspace::is_displayed()` builds on this, so special workspaces now report `false` everywhere, which also keeps `move_window_to_workspace` from moving focus into one.)

### `packages/wm/src/user_config.rs`

```rust
  /// Configs not yet active, never special ones: those are only activated
  /// by name (`move --workspace scratchpad`), never handed to a monitor.
  pub fn inactive_workspace_configs(&self, active_workspaces: &[Workspace]) -> Vec<&WorkspaceConfig> {
    self
      .value
      .workspaces
      .iter()
      .filter(|config| !config.special)
      .filter(|config| {
        !active_workspaces
          .iter()
          .any(|workspace| workspace.config().name == config.name)
      })
      .collect()
  }
```

(Check every other caller of `inactive_workspace_configs` still wants special ones excluded: `activate_workspace::workspace_config` resolves by **name** first, so `move --workspace scratchpad` still activates it.)

### `packages/wm/src/wm_state.rs`

```rust
  /// Active workspaces in config order, without special ones (next / prev
  /// active workspace never lands on the scratchpad).
  pub fn sorted_workspaces(&self, config: &UserConfig) -> Vec<Workspace> {
    let mut workspaces: Vec<_> = self
      .workspaces()
      .into_iter()
      .filter(|workspace| !workspace.config().special)
      .collect();
    config.sort_workspaces(&mut workspaces);
    workspaces
  }

  // New field on `WmState`:
  /// Windows the scratchpad toggle brought out, and which scratchpad they
  /// came from, so the next toggle sends them back.
  pub scratchpad_shown: Vec<(Uuid, String)>,
```

`focus_workspace.rs`: refuse a special target (`focus --workspace scratchpad` would otherwise cloak the current workspace and show nothing):

```rust
  if config.value.workspaces.iter().any(|w| w.special && Some(&w.name) == target_workspace_name.as_ref()) {
    tracing::warn!("Can't focus special workspace; use `toggle-scratchpad`.");
    return Ok(());
  }
```

### `packages/wm/src/commands/workspace/toggle_scratchpad.rs` (new)

```rust
use anyhow::Context;
use wm_common::{FloatingStateConfig, WindowState};

use crate::{
  commands::window::{
    move_window_to_workspace, set_window_position, update_window_state,
    WindowPositionTarget,
  },
  models::{WindowContainer, WorkspaceTarget},
  traits::{CommonGetters, WindowGetters},
  user_config::UserConfig,
  wm_state::WmState,
};

/// Shows the windows of the special workspace `name` floating and centred
/// on the focused workspace, or sends back the ones shown before.
///
/// Returns without changes when the scratchpad has no windows.
pub fn toggle_scratchpad(
  name: &str,
  state: &mut WmState,
  config: &UserConfig,
) -> anyhow::Result<()> {
  // Shown: send back the ones still open (a closed one just drops out).
  let shown = std::mem::take(&mut state.scratchpad_shown);
  let still_open: Vec<(WindowContainer, String)> = shown
    .into_iter()
    .filter_map(|(id, from)| {
      state.windows().into_iter().find(|w| w.id() == id).map(|w| (w, from))
    })
    .collect();

  if !still_open.is_empty() {
    for (window, from) in still_open {
      move_window_to_workspace(window, WorkspaceTarget::Name(from), state, config)?;
    }
    return Ok(());
  }

  let Some(scratchpad) = state.workspace_by_name(name) else {
    tracing::info!("Scratchpad '{}' is empty.", name);
    return Ok(());
  };

  let focused_workspace = state
    .focused_container()
    .and_then(|c| c.workspace())
    .context("No focused workspace.")?;
  let target = focused_workspace.config().name;

  let windows: Vec<WindowContainer> = scratchpad
    .descendants()
    .filter_map(|c| c.as_window_container().ok())
    .collect();

  let floating = &config.value.window_behavior.state_defaults.floating;
  for window in windows {
    let id = window.id();
    move_window_to_workspace(window.clone(), WorkspaceTarget::Name(target.clone()), state, config)?;
    let window = update_window_state(
      window,
      WindowState::Floating(FloatingStateConfig { centered: true, shown_on_top: true }),
      state,
      config,
    )?;
    if !window.has_custom_floating_placement() || floating.centered {
      set_window_position(window.clone(), &WindowPositionTarget::Centered, state)?;
    }
    state.scratchpad_shown.push((id, name.to_string()));
    // Focus the last one brought out (check: helper name for "focus this window and queue
    // the focus sync"; focus_container_by_id does it by id).
    crate::commands::container::focus_container_by_id(&id, state)?;
  }

  Ok(())
}
```

`wm.rs`, in the `InvokeCommand` match: `InvokeCommand::ToggleScratchpad { name } => toggle_scratchpad(name, state, config),`; export it from `commands/workspace/mod.rs`.

Also:
- `WorkspaceDto`: add `pub is_special: bool` so the bar (native bar and Zebar) leaves the scratchpad out of its workspace list.
- A window closed while shown: filtered out above. A window moved elsewhere by hand while shown: it's sent back on the next toggle (acceptable; or drop it from `scratchpad_shown` in `move_window_to_workspace` when the move isn't from this command).
- Tests (`#[cfg(test)]` in `user_config.rs`): `next_inactive_workspace_config` never returns a special config; parsing `special: true` from YAML. `displayed_workspace` skipping special needs a container fixture *(check: is there one in the crate's tests?)*.

Other 5.7 items (layout cycling, pop-out, corner style and transparency toggles) are menu rows calling existing GlazeWM commands (`toggle-floating --centered`, `set-transparency`), no fork change.

---

## 5.8 Detect before offering: menu conditions

**Format**: an optional last field starting with `?` on any `default/launcher/menu.txt` row (submenus too, with an empty action field). `?!x` negates.

```
install.linux | 󰌽 | Linux (WSL) | | ?!wsl
tools.sandbox | 󰆧 | Sandbox | omawin sandbox | ?sandbox
tools.lazydocker |  | lazydocker | task lazydocker | ?docker
update.scoop | 󰏔 | Scoop apps | task scoop update * | ?cmd scoop
remove.thing | ... | ... | ... | ?installed winget:Some.Id
```

Conditions: `wsl` (a distro registered), `docker`, `cmd <name>` (on PATH), `installed <kind:id>` (`installed.txt`), `sandbox` (Pro+ and the feature on), `sandbox-capable` (Pro+), `pro` (not Home), `battery`, `file <path>`.

### `shell/menu-app/MenuModel.cs`

```csharp
public sealed record MenuEntry(string Id, string Icon, string Label, string? Action, bool Confirm, string? Package, string? Condition = null) { ... }

// in Load(): split the whole line, take a trailing ?condition off before the 4-way split
var all = line.Split('|').Select(p => p.Trim()).ToList();
string? condition = null;
if (all.Count >= 4 && all[^1].StartsWith('?')) { condition = all[^1][1..].Trim(); all.RemoveAt(all.Count - 1); }
var parts = new[] { all[0], all[1], all[2], all.Count > 3 ? string.Join(" | ", all.Skip(3)) : "" }; // an action may contain |
...
entries[parts[0]] = new MenuEntry(parts[0], parts[1], parts[2], action, confirm, package, condition);

// Children() and Search() leave out rows whose condition is known false (unknown: shown):
public static List<MenuEntry> Children(Dictionary<string, MenuEntry> entries, string parent) =>
    entries.Values.Where(e => e.Parent == parent && Conditions.Allows(e.Condition)).ToList();
```

### `shell/menu-app/Conditions.cs` (new)

```csharp
using Microsoft.Win32;

namespace SystemMenu;

// The answers to menu.txt's ?conditions, worked out in the background and cached
// (%LOCALAPPDATA%\launcher\conditions.tsv), so the menu opens at once on the last answers, as
// Omarchy's menu guards do. Refreshed at start and when the menu opens if older than a minute;
// an answer that changes while the menu is open redraws it.
public static class Conditions
{
    static readonly Dictionary<string, bool> answers = [];
    static readonly object gate = new();
    static DateTime refreshedAt = DateTime.MinValue;
    static string CacheFile => Path.Combine(MenuModel.StateDir, "conditions.tsv");
    public static event Action? Changed;

    public static void Start()
    {
        try
        {
            foreach (var l in File.ReadAllLines(CacheFile))
                if (l.Split('\t') is [var k, var v]) answers[k] = v == "1";
        }
        catch { }
        Refresh();
    }

    // Shown unless known false: a row is never missing because the answer isn't in yet.
    public static bool Allows(string? condition)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        bool not = condition.StartsWith('!');
        var key = not ? condition[1..] : condition;
        lock (gate) if (!answers.TryGetValue(key, out var yes)) { Want(key); return true; } else return yes != not;
    }

    static readonly HashSet<string> wanted = [];
    static void Want(string key) { lock (gate) wanted.Add(key); }

    public static void Refresh(bool ifOld = false)
    {
        if (ifOld && DateTime.Now - refreshedAt < TimeSpan.FromMinutes(1)) return;
        refreshedAt = DateTime.Now;
        Task.Run(() =>
        {
            string[] keys;
            lock (gate) keys = [.. answers.Keys.Concat(wanted).Distinct()];
            bool changed = false;
            foreach (var key in keys)
            {
                bool value;
                try { value = Evaluate(key); } catch { continue; }
                lock (gate) { if (!answers.TryGetValue(key, out var old) || old != value) changed = true; answers[key] = value; }
            }
            if (!changed) return;
            try { lock (gate) File.WriteAllLines(CacheFile, answers.Select(a => $"{a.Key}\t{(a.Value ? 1 : 0)}")); } catch { }
            Changed?.Invoke(); // MainWindow: DispatcherQueue.TryEnqueue(() => { if (Visible && !finding) Render(); })
        });
    }

    static bool Evaluate(string key)
    {
        var (name, arg) = key.Split(' ', 2) is [var n, var a] ? (n, a.Trim()) : (key, "");
        return name switch
        {
            // Registered distros are subkeys of Lxss (no wsl.exe start, which is slow and can wake the VM).
            "wsl" => Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss")?.GetSubKeyNames().Length > 0,
            "docker" => OnPath("docker") || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe")),
            "cmd" => OnPath(arg),
            "installed" => MenuModel.Installed().Contains(arg.ToLowerInvariant()), // (check: the installed-set accessor's name)
            "pro" => !IsHome(),
            "sandbox-capable" => !IsHome(),
            "sandbox" => !IsHome() && File.Exists(Path.Combine(Environment.SystemDirectory, "WindowsSandbox.exe")),
            "battery" => System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery, // or GetSystemPowerStatus
            "file" => File.Exists(Environment.ExpandEnvironmentVariables(arg)) || Directory.Exists(Environment.ExpandEnvironmentVariables(arg)),
            _ => true,
        };
    }

    static bool IsHome() =>
        (Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")?.GetValue("EditionID") as string ?? "")
            .StartsWith("Core", StringComparison.OrdinalIgnoreCase); // Core, CoreN, CoreSingleLanguage = Home

    static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(d => new[] { ".exe", ".cmd", ".bat", "" }.Any(ext => File.Exists(Path.Combine(d.Trim(), exe + ext))));
}
```

Hook-up: `App.OnLaunched` → `Conditions.Start()`; `MainWindow.Show` → `Conditions.Refresh(ifOld: true)`. Unit tests (cloud-runnable): the line parser (trailing `?x`, actions with `|`, submenus with empty action).

---

## 5.9 Update › All apps across winget, Scoop and Chocolatey

`default/launcher/menu.txt`:

```
update.everything | 󰏔 | All apps (winget, Scoop, Chocolatey) | task omawin update apps
```

`commands.txt`: `update apps | Every app's updates: winget, then Scoop and Chocolatey when they're here (Chocolatey's asks for admin first) | pwsh bin/update-apps.ps1`

### `bin/update-apps.ps1` (new, PS 5.1)

```powershell
# Update › All apps: each package manager here updates what it installed, through its own command.
# winget and Scoop never need admin. Chocolatey's upgrades do: said first, asked once, skipped on No.
param([switch]$Yes)
$ErrorActionPreference = 'Continue'
function Step($name) { Write-Host "`n== $name ==" -ForegroundColor Cyan }
$failed = @()

Step 'winget'
winget upgrade --all --accept-source-agreements --accept-package-agreements --disable-interactivity
if ($LASTEXITCODE -ne 0) { $failed += 'winget' }

if (Get-Command scoop -ErrorAction SilentlyContinue) {
    Step 'Scoop'
    scoop update          # Scoop itself and its buckets
    scoop update *        # every app (held ones stay held)
    if ($LASTEXITCODE -ne 0) { $failed += 'scoop' }
}

if (Get-Command choco -ErrorAction SilentlyContinue) {
    Step 'Chocolatey'
    $outdated = @(choco outdated --limit-output | Where-Object { $_ -match '\|' })
    if ($outdated.Count -eq 0) { Write-Host '  nothing to update' }
    else {
        Write-Host "  $($outdated.Count) to update: $(($outdated | ForEach-Object { ($_ -split '\|')[0] }) -join ', ')"
        Write-Host '  Chocolatey installs for the whole PC, so this needs admin: one prompt.'
        $go = $Yes -or ((Read-Host '  Update them now? [y/N]') -match '^y')
        if ($go) {
            $p = Start-Process choco -Verb RunAs -Wait -PassThru -ArgumentList 'upgrade', 'all', '-y', '--no-progress'
            if ($p.ExitCode -ne 0) { $failed += 'chocolatey' }
        } else { Write-Host '  skipped' }
    }
}

if ($failed) { Write-Host "`nSome didn't finish: $($failed -join ', ')" -ForegroundColor Yellow; exit 1 }
Write-Host "`nAll up to date." -ForegroundColor Green
```

(AGENTS.md's "an update never prompts" is about `omawin update`; this is a row the user picks, and the prompt is said first and skippable.)

---

# Phase 6: the finder (Win+F)

State added to `MainWindow` (finder fields sit next to `findScope`, `findHits`, `previewToken`):

```csharp
string? browseDir;                       // 6.2: listing this folder instead of searching
bool actionsOpen;                        // 6.1: the action list is showing
List<FileSearch.Hit> actingOn = [];      // what the actions apply to
FileSearch.Hit? renaming;                // 6.1: the search box is the new name
readonly HashSet<string> marked = new(StringComparer.OrdinalIgnoreCase); // 6.6
const string ContentsOnly = "*contents*"; // 6.3: a chip, like FoldersOnly
List<FileSearch.Line> grepHits = [];     // 6.3
```

## 6.1 Actions on a result (Ctrl+K, or → on a file)

### `shell/menu-app/FileActions.cs` (new)

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SystemMenu;

public sealed record FileAction(string Id, int Glyph, string Label, string? Key, bool Many, Func<FileSearch.Hit, bool> Applies, Action<IReadOnlyList<FileSearch.Hit>> Run);

public static class FileActions
{
    static bool InLinux(string path) => path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
    static string Dir(FileSearch.Hit h) => h.IsDir ? h.Path : Path.GetDirectoryName(h.Path)!;

    // Keys proposed in the roadmap (Ctrl+E, Ctrl+T, Ctrl+Shift+C): shown in the list, bound only once confirmed.
    public static readonly FileAction[] All =
    [
        new("editor", 0xF0A1E, "Open in the editor", "ctrl e", false, _ => true,
            h => MenuModel.Omawin("launch", "editor", h[0].Path)),
        new("terminal", 0xF018D, "Terminal here", "ctrl t", false, _ => true,
            h => MenuModel.Omawin(InLinux(h[0].Path) ? ["launch", "terminal", "--dir", Dir(h[0]), "--wsl"] : ["launch", "terminal", "--dir", Dir(h[0])])),
        new("project", 0xF0645, "Open as a project", null, false, h => RepoRoot(h.Path) != null,
            h => MenuModel.Omawin("launch", "project", RepoRoot(h[0].Path)!)),
        new("copy-path", 0xF018F, "Copy path", "ctrl shift c", true, _ => true,
            h => SetText(string.Join("\r\n", h.Select(x => x.Path)))),
        new("copy-relative", 0xF018F, "Copy path in its repo", null, true, h => RepoRoot(h.Path) != null,
            h => SetText(string.Join("\n", h.Select(x => Path.GetRelativePath(RepoRoot(x.Path)!, x.Path).Replace('\\', '/'))))),
        new("copy-file", 0xF0C4E, "Copy the file (paste it anywhere)", null, true, _ => true,
            h => _ = CopyFiles(h)),
        new("rename", 0xF04F2, "Rename", "f2", false, _ => true, _ => { /* MainWindow.StartRename */ }),
        new("recycle", 0xF0A7A, "Send to the Recycle Bin", "delete", true, _ => true,
            h => Recycle(h.Select(x => x.Path))), // MainWindow asks "Are you sure?" first (the confirming pattern)
        new("open-with", 0xF0544, "Open with…", null, false, h => !h.IsDir,
            h => Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {h[0].Path}") { UseShellExecute = false })),
        new("reveal", 0xF0256, "Show in folder", "ctrl enter", false, _ => true,
            h => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{h[0].Path}\"") { UseShellExecute = true })),
        new("properties", 0xF02FD, "Properties", "alt enter", false, _ => true,
            h => SHObjectProperties(IntPtr.Zero, 2 /* SHOP_FILEPATH */, h[0].Path, null)),
    ];

    public static string? RepoRoot(string path)
    {
        for (var d = Directory.Exists(path) ? path : Path.GetDirectoryName(path); d != null; d = Path.GetDirectoryName(d))
            if (Directory.Exists(Path.Combine(d, ".git")) || File.Exists(Path.Combine(d, ".git"))) return d; // a worktree's .git is a file
        return null;
    }

    static void SetText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        Clipboard.Flush(); // stays after the menu app stops
        Snip.Say("Copied");
    }

    // The files themselves, as Explorer's Ctrl+C: pastes into Explorer, Teams, Outlook, a chat.
    static async Task CopyFiles(IReadOnlyList<FileSearch.Hit> hits)
    {
        var items = new List<IStorageItem>();
        foreach (var h in hits)
            items.Add(h.IsDir ? await StorageFolder.GetFolderFromPathAsync(h.Path) : await StorageFile.GetFileFromPathAsync(h.Path));
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetStorageItems(items);
        Clipboard.SetContent(data);
        Clipboard.Flush(); // (check: Flush with storage items; if it throws, skip Flush, the menu app stays running anyway)
        Snip.Say(hits.Count == 1 ? $"Copied {hits[0].Name}" : $"Copied {hits.Count} files");
    }

    // --- the Recycle Bin ---------------------------------------------------------------------
    // SHFileOperation with FOF_ALLOWUNDO sends to the Recycle Bin; FOF_WANTNUKEWARNING warns
    // instead of deleting for good when something can't go there (too big, a network drive).
    // (IFileOperation with FOFX_RECYCLEONDELETE (0x00080000) is the modern form: same result,
    // needs [GeneratedComInterface] for IFileOperation and IShellItem; worth it only for progress UI.)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc;
        public string pFrom; public string? pTo;
        public ushort fFlags; public int fAnyOperationsAborted;
        public IntPtr hNameMappings; public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHObjectProperties(IntPtr hwnd, uint type, string name, string? page);

    public static bool Recycle(IEnumerable<string> paths)
    {
        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0", // double-NUL terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_WANTNUKEWARNING, // we asked already
        };
        int result = SHFileOperation(ref op);
        if (result != 0) App.Log(new Exception($"SHFileOperation delete: 0x{result:X}"));
        return result == 0 && op.fAnyOperationsAborted == 0;
    }

    // Rename in place; false with why when it can't.
    public static string? Rename(string path, string newName)
    {
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "That name has characters Windows doesn't allow";
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (string.Equals(target, path, StringComparison.Ordinal)) return null;
        bool caseOnly = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target))) return "Something with that name is already there";
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, caseOnly ? path + ".~" : target); else File.Move(path, caseOnly ? path + ".~" : target);
            if (caseOnly) { if (Directory.Exists(path + ".~")) Directory.Move(path + ".~", target); else File.Move(path + ".~", target); }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
```

### `MainWindow.xaml.cs`: drawing and keys

```csharp
// In RenderFinder, first thing:
if (actionsOpen) { RenderActions(query); return; }
if (renaming != null) { RenderRename(); return; }

void RenderActions(string query)
{
    SetCrumbs("", actingOn.Count == 1 ? actingOn[0].Name : $"{actingOn.Count} items");
    Placeholder.Text = "Search actions…";
    foreach (var a in FileActions.All.Where(a => (a.Many || actingOn.Count == 1) && actingOn.All(a.Applies)))
    {
        var hits = new List<int>();
        if (query.Length > 0 && Fuzzy.Match(a.Label, query, hits) == Fuzzy.None) continue;
        AddRow(char.ConvertFromUtf32(a.Glyph), a.Label, Hex(a.Id == "recycle" ? theme.Red : theme.Accent),
            () => RunAction(a), Tag.Text, a.Key ?? "", highlight: hits);
    }
    SetKeys(("↵", "do"), ("esc", "back"));
}

void RunAction(FileAction a)
{
    var on = actingOn.ToList();
    actionsOpen = false;
    if (a.Id == "rename") { StartRename(on[0]); return; }
    if (a.Id == "recycle")
    {
        // The menu's own "Are you sure?" rows, then the Recycle Bin, then the list again.
        ConfirmThen($"Recycle {(on.Count == 1 ? on[0].Name : on.Count + " items")}", () => { FileActions.Recycle(on.Select(h => h.Path)); marked.Clear(); Render(); });
        return;
    }
    if (a.Id is not ("copy-path" or "copy-relative" or "copy-file")) HideMenu();
    a.Run(on);
    if (a.Id.StartsWith("copy")) { marked.Clear(); Input.Text = ""; Render(); }
}

void StartRename(FileSearch.Hit hit)
{
    renaming = hit;
    Input.Text = hit.Name;
    // Select the name without its extension, as Explorer's F2 does.
    int dot = hit.IsDir ? -1 : hit.Name.LastIndexOf('.');
    Input.Select(0, dot > 0 ? dot : hit.Name.Length);
    Render();
}

// OnKey, before the finder's Tab case:
case VirtualKey.K when ctrl && finding && !actionsOpen && renaming == null:
case VirtualKey.Right when finding && !ctrl && Input.SelectionStart == Input.Text.Length && SelectedIsFile():
    OpenActions(); break;
case VirtualKey.Enter when finding && renaming != null:
    if (FileActions.Rename(renaming.Path, Input.Text.Trim()) is { } why) Snip.Say(why);
    else { renaming = null; Input.Text = ""; Render(); }
    break;
case VirtualKey.Escape when finding && (actionsOpen || renaming != null):
    actionsOpen = false; renaming = null; Input.Text = ""; Render(); break;
case VirtualKey.F2 when finding: if (selected < findHits.Count) StartRename(findHits[selected]); break;
case VirtualKey.Delete when finding && Input.SelectionLength == 0 && Input.SelectionStart == Input.Text.Length:
    OpenActions(); RunAction(FileActions.All.First(a => a.Id == "recycle")); break;

void OpenActions()
{
    actingOn = marked.Count > 0 ? [.. findHits.Where(h => marked.Contains(h.Path))] : selected < findHits.Count ? [findHits[selected]] : [];
    if (actingOn.Count == 0) return;
    actionsOpen = true; Input.Text = ""; selected = 0; Render();
}
```

(`ConfirmThen` generalises the existing `confirming` row pair to a callback; `Input.TextChanged` resets `selected` and renders, which is fine in all three modes.)

## 6.2 Browse, not just search (→ / ←)

```csharp
// FileSearch.cs: a folder's contents as hits, folders first, filtered by the query (fuzzy, own name).
public static List<Hit> List(string dir, string query, int limit = 400)
{
    var hits = new List<Hit>();
    try
    {
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System }))
        {
            if (Skipped(e.FullName)) continue;
            bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
            hits.Add(new Hit(e.FullName, e.Name, Path.GetFileName(dir), "", isDir, isDir ? 0 : ((FileInfo)e).Length,
                e.LastWriteTime, null, false, 0));
            if (hits.Count > 5000) break; // a huge folder: the first 5000 are plenty to filter
        }
    }
    catch (Exception ex) { App.Log(ex); }
    var scored = hits.Select(h => (h, s: query.Length == 0 ? 1 : Fuzzy.Match(h.Name, query))).Where(x => x.s != Fuzzy.None);
    return [.. scored.OrderByDescending(x => x.h.IsDir).ThenByDescending(x => x.s).ThenBy(x => x.h.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.h).Take(limit)];
}
```

```csharp
// RenderFinder: browsing replaces the search.
if (browseDir != null)
{
    SetCrumbs("", null);
    BrowseCrumbs(browseDir); // the path as chips, last one accent (as SetCrumbs draws)
    Placeholder.Text = $"Filter {Path.GetFileName(browseDir)}…";
    findHits = FileSearch.List(browseDir, query);
    foreach (var hit in findHits) AddFileRow(hit, query);
    FooterLeft.Text = $"{findHits.Count} items";
    SetKeys(("→", "open folder · actions"), ("←", "up"), ("↵", "open"), ("esc", "back to search"));
    return;
}

// OnKey (finder), ahead of the generic Right / Left cases:
case VirtualKey.Right when finding && Input.Text.Length == 0 && selected < findHits.Count && findHits[selected].IsDir:
    browseDir = findHits[selected].Path; selected = 0; Render(); Slide(1); break;
case VirtualKey.Left when finding && browseDir != null && Input.Text.Length == 0:
    var from = browseDir;
    browseDir = Path.GetDirectoryName(browseDir); // a drive root's parent is null: back to search
    selected = 0; Render();
    if (browseDir != null) { selected = Math.Max(0, findHits.FindIndex(h => h.Path == from)); Highlight(); }
    Slide(-1); break;
case VirtualKey.Escape when finding && browseDir != null && Input.Text.Length == 0:
    browseDir = null; selected = 0; Render(); break;
```

A `FileSystemWatcher` on `browseDir` (NotifyFilter: FileName | DirectoryName) re-renders on change, disposed when leaving it.

## 6.3 Contents: search inside files

**fff v0.11.0 has a grep API** (checked in its `crates/fff-c/include/fff.h` at the tag): `fff_live_grep` / `fff_live_grep_ex` returning an `FffResult` whose handle is an `FffGrepResult`, read with `fff_grep_result_get_count`, `fff_grep_result_get_match` and per-match accessors, freed with `fff_free_grep_result`. `mode`: 0 plain (SIMD), 1 regex, 2 fuzzy; the query takes fff's constraints (`*.rs pattern`). `enable_content_indexing` only makes it faster ("build content index after the initial scan for faster grep"), so grep works on every root and indexing is turned on only for the roots picked.

### `FileSearch.cs` additions

```csharp
public sealed record Line(string Path, string Name, string RootLabel, string RelativeDir, long LineNumber, int Column,
    string Text, (int Start, int End)[] Ranges, string? Git);

// Lines matching `query` across the roots (or one), best files first (fff orders by frecency),
// within a time budget per root so typing never waits. Off the UI thread.
public static List<Line> Grep(string query, string? scope, int limit = 200, int budgetMs = 120, bool regex = false, CancellationToken cancel = default)
{
    var lines = new List<Line>();
    Index[] all;
    lock (gate) all = [.. indexes.Where(i => scope == null || i.Root == scope)];
    foreach (var index in all)
    {
        if (cancel.IsCancellationRequested || lines.Count >= limit) break;
        IntPtr envelope = IntPtr.Zero, result = IntPtr.Zero;
        try
        {
            envelope = fff_live_grep_ex(index.Handle, query, (byte)(regex ? 1 : 0), 0 /* 10 MB */, 5 /* per file */, true /* smart case */,
                0 /* file offset */, (uint)Math.Min(50, limit - lines.Count), (ulong)budgetMs, true /* enforce budget */, 0, 0, false);
            if (envelope == IntPtr.Zero || !fff_result_get_success(envelope)) continue;
            result = fff_result_get_handle(envelope);
            if (result == IntPtr.Zero) continue;
            uint count = fff_grep_result_get_count(result);
            for (uint i = 0; i < count; i++)
            {
                IntPtr m = fff_grep_result_get_match(result, i);
                if (m == IntPtr.Zero || fff_grep_match_get_is_binary(m)) continue; // (check: accessor name; else read the struct's is_binary)
                string rel = (Marshal.PtrToStringUTF8(fff_grep_match_get_relative_path(m)) ?? "").Replace('/', '\\');
                string text = Marshal.PtrToStringUTF8(fff_grep_match_get_line_content(m)) ?? "";
                // Ranges are byte offsets in the UTF-8 line: convert to char offsets for highlighting.
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                uint rc = fff_grep_match_get_match_ranges_count(m);
                var ranges = new (int, int)[rc];
                for (uint r = 0; r < rc; r++)
                {
                    IntPtr range = fff_grep_match_get_match_range(m, r);
                    int s = Marshal.ReadInt32(range), e = Marshal.ReadInt32(range, 4);
                    ranges[r] = (System.Text.Encoding.UTF8.GetCharCount(bytes, 0, Math.Min(s, bytes.Length)),
                                 System.Text.Encoding.UTF8.GetCharCount(bytes, 0, Math.Min(e, bytes.Length)));
                }
                string full = Path.Combine(index.Root, rel);
                if (Skipped(full)) continue;
                string? git = Marshal.PtrToStringUTF8(fff_grep_match_get_git_status(m));
                lines.Add(new Line(full, Path.GetFileName(full), Label(index.Root), Path.GetDirectoryName(rel) ?? "",
                    (long)fff_grep_match_get_line_number(m), (int)fff_grep_match_get_col(m), text.TrimEnd('\r'), ranges,
                    string.IsNullOrEmpty(git) || git == "clean" ? null : git));
            }
        }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            if (result != IntPtr.Zero) fff_free_grep_result(result);
            if (envelope != IntPtr.Zero) fff_free_result(envelope);
        }
    }
    return lines;
}

[DllImport(Lib)] static extern IntPtr fff_live_grep_ex(IntPtr fff, [MarshalAs(UnmanagedType.LPUTF8Str)] string query, byte mode,
    ulong maxFileSize, uint maxMatchesPerFile, [MarshalAs(UnmanagedType.U1)] bool smartCase, uint fileOffset, uint pageLimit,
    ulong timeBudgetMs, [MarshalAs(UnmanagedType.U1)] bool enforceTimeBudget, uint beforeContext, uint afterContext,
    [MarshalAs(UnmanagedType.U1)] bool classifyDefinitions);
[DllImport(Lib)] static extern void fff_free_grep_result(IntPtr result);
[DllImport(Lib)] static extern uint fff_grep_result_get_count(IntPtr result);
[DllImport(Lib)] static extern IntPtr fff_grep_result_get_match(IntPtr result, uint index);
[DllImport(Lib)] static extern IntPtr fff_grep_match_get_relative_path(IntPtr m);
[DllImport(Lib)] static extern IntPtr fff_grep_match_get_line_content(IntPtr m);
[DllImport(Lib)] static extern IntPtr fff_grep_match_get_git_status(IntPtr m);
[DllImport(Lib)] static extern ulong fff_grep_match_get_line_number(IntPtr m);
[DllImport(Lib)] static extern uint fff_grep_match_get_col(IntPtr m);
[DllImport(Lib)] static extern uint fff_grep_match_get_match_ranges_count(IntPtr m);
[DllImport(Lib)] static extern IntPtr fff_grep_match_get_match_range(IntPtr m, uint index);
[DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] static extern bool fff_grep_match_get_is_binary(IntPtr m); // (check: exists?)
```

Content indexing per root (`Create`): `EnableContentIndexing = ContentRoots.Contains(root)`, where content roots are `default/launcher/finder-content.txt` (layered, paths or `repos` = every git repo root), default: roots that are git repos or named `repos`. Not Downloads.

**Fallback: `rg --json`** (ripgrep is in the core) only if a future fff drops the grep call; kept as a sketch:

```csharp
// rg's JSON lines: {"type":"match","data":{"path":{"text":...},"lines":{"text":...},"line_number":N,
// "submatches":[{"start":s,"end":e},...]}}. Streamed into the list in batches every 50 ms.
static async Task RipGrep(string query, string root, Action<List<Line>> batch, CancellationToken cancel)
{
    var info = new ProcessStartInfo("rg") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
    foreach (var a in new[] { "--json", "--smart-case", "--max-count", "5", "--max-columns", "300", "--max-filesize", "10M", "--", query, root }) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!;
    using var _ = cancel.Register(() => { try { p.Kill(); } catch { } });
    var pending = new List<Line>(); var last = Environment.TickCount64;
    while (await p.StandardOutput.ReadLineAsync(cancel) is { } json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("type").GetString() != "match") continue;
        var d = doc.RootElement.GetProperty("data");
        if (!d.GetProperty("path").TryGetProperty("text", out var pathText)) continue; // non-UTF-8 path: "bytes"
        string text = d.GetProperty("lines").TryGetProperty("text", out var t) ? t.GetString()!.TrimEnd('\n', '\r') : "";
        var ranges = d.GetProperty("submatches").EnumerateArray().Select(s => (s.GetProperty("start").GetInt32(), s.GetProperty("end").GetInt32())).ToArray(); // bytes: convert as above
        string full = pathText.GetString()!;
        pending.Add(new Line(full, Path.GetFileName(full), Path.GetFileName(root), Path.GetDirectoryName(Path.GetRelativePath(root, full)) ?? "",
            d.GetProperty("line_number").GetInt64(), 0, text, ranges, null));
        if (Environment.TickCount64 - last > 50) { batch(pending); pending = []; last = Environment.TickCount64; }
    }
    if (pending.Count > 0) batch(pending);
}
```

**In the window:** a **Contents** chip after Images (`Chip("Contents", ContentsOnly, char.ConvertFromUtf32(0xF0B76))`, Tab reaches it), or a query starting with `#` (the `#` is dropped). In that mode `RenderFinder` doesn't search synchronously: it starts `Grep` on a worker with a `CancellationTokenSource` replaced on every keystroke (120 ms debounce; queries under 2 chars show "Type to search inside files"), and renders rows when results come back (only if the query is still the same). Row: file name + `:line` on top; the matching line beneath with ranges in Accent (a `TextBlock` with `Run`s). Preview: the file's lines around the match (±10), the match line on a Selection background, ranges highlighted. Enter: `omawin launch editor --line <n> <path>`; Ctrl+K: the file's actions.

`bin/omawin.cs` `LaunchEditor` gains `--line` (C# 5):

```csharp
static int LaunchEditor(string[] args)
{
    string line = null;
    var rest = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--line" && i + 1 < args.Length) { line = args[++i]; continue; }
        rest.Add(args[i]);
    }
    if (rest.Count == 0) return Fail("which file or folder? omawin launch editor [--line N] <path>");
    var path = rest[0];
    var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
    var editor = Current("editor");
    var exe = Find(editor.Program);
    if (editor.Name == "nvim" || exe == null)
        return line == null
            ? LaunchTerminal(new[] { "--dir", dir, "--", "nvim", path })
            : LaunchTerminal(new[] { "--dir", dir, "--", "nvim", "+" + line, path });
    if (editor.Name == "code")
    {
        var codeExe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(exe)), "Code.exe");
        var codeArgs = line == null ? new List<string> { path } : new List<string> { "--goto", path + ":" + line };
        if (File.Exists(codeExe)) return Detach(codeExe, codeArgs);
        // ... code.cmd as today, with the same arguments
    }
    return Detach(exe, new List<string> { path });
}
```

## 6.4 Better previews

`ShowPreview` dispatches by extension after the shared header (name, path, facts). All work off the UI thread, results dropped when `token != previewToken`.

```csharp
string ext = Path.GetExtension(hit.Name).ToLowerInvariant();
switch (ext)
{
    case ".pdf": _ = PreviewPdf(hit.Path, token); return;
    case ".csv" or ".tsv": PreviewTable(hit.Path, ext == ".tsv" ? '\t' : ',', token); return;
    case ".md" or ".markdown": PreviewMarkdown(hit.Path, token); return;
}
// Office files, videos, fonts, 3D models: Windows' own thumbnail. Already there: the non-picture
// branch calls AppIcons.Get("big:" + path), which is IShellItemImageFactory::GetImage at 256 px
// (flags 0: the thumbnail when a handler has one, else the icon). Make the art taller (300) when
// the result is wider than an icon (the handler gave a real thumbnail).
// Code: the existing first-22-lines path, coloured (below).

async Task PreviewPdf(string path, int token)
{
    try
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file); // throws for password-protected: show the icon
        using var page = pdf.GetPage(0);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 380 * 2 }); // 2x for crispness
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (token != previewToken) return;
            var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            PreviewArt.Height = 300;
            PreviewArt.Child = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
            PreviewFacts.Children.Add(Pill($"{pdf.PageCount} pages", theme.Muted));
        });
    }
    catch (Exception ex) { App.Log(ex); }
}

// CSV: the first 20 rows, 6 columns, as a grid; RFC 4180 quotes handled.
void PreviewTable(string path, char sep, int token) => Task.Run(() =>
{
    var rows = new List<string[]>();
    try { foreach (var r in Csv(File.ReadLines(path), sep).Take(20)) rows.Add(r); } catch { }
    DispatcherQueue.TryEnqueue(() =>
    {
        if (token != previewToken || rows.Count == 0) return;
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 2 };
        int cols = Math.Min(6, rows.Max(r => r.Length));
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols && c < rows[r].Length; c++)
            {
                var cell = new TextBlock { Text = rows[r][c].Length > 24 ? rows[r][c][..24] + "…" : rows[r][c], FontFamily = monoFont, FontSize = 11.5,
                    Foreground = Brush(r == 0 ? theme.Accent : theme.Muted), FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
            }
        }
        PreviewTextBox.Child = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; // (check: PreviewTextBox is a Border holding PreviewText; restore it on the next preview)
        PreviewTextBox.Visibility = Visibility.Visible;
    });
});

static IEnumerable<string[]> Csv(IEnumerable<string> lines, char sep)
{
    var field = new System.Text.StringBuilder(); var row = new List<string>(); bool quoted = false;
    foreach (var line in lines)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; } else field.Append(ch); }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == sep) { row.Add(field.ToString()); field.Clear(); }
            else field.Append(ch);
        }
        if (quoted) { field.Append('\n'); continue; } // a quoted field spanning lines
        row.Add(field.ToString()); field.Clear();
        yield return [.. row]; row.Clear();
    }
}
```

**Code in the theme's colours.** First version: a small regex tokenizer per family, no tree-sitter (no native dependency, ~0.1 ms for 22 lines). Tree-sitter (`tree-sitter-highlight` through a C ABI) is a later option if the tokenizer isn't enough.

```csharp
// Colours from the theme: keywords Purple, strings Green, comments Comment, numbers Yellow,
// types / capitalised words Cyan, the rest Muted. Lines go into PreviewText as Runs.
static readonly Dictionary<string, (Regex Comment, string[] Keywords)> Languages = new()
{
    ["c"] = (new(@"//.*|/\*.*?\*/"), ["if","else","for","while","return","class","struct","public","private","static","void","new","var","const","let","fn","impl","use","pub","match","async","await","func","package","import","namespace","using","interface","enum","true","false","null","this","self"]),
    ["hash"] = (new(@"#.*"), ["if","then","else","elif","fi","for","in","do","done","def","class","return","import","from","function","param","foreach","while","true","false","None","local","export"]),
    ["sql"] = (new(@"--.*"), ["select","from","where","join","on","group","by","order","insert","update","delete","into","values","create","table","as","and","or","not","null","left","inner"]),
};
static string? FamilyOf(string ext) => ext switch
{
    ".cs" or ".rs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".go" or ".java" or ".c" or ".cpp" or ".h" or ".kt" or ".swift" or ".dart" => "c",
    ".py" or ".sh" or ".ps1" or ".psm1" or ".rb" or ".yaml" or ".yml" or ".toml" or ".r" => "hash",
    ".sql" => "sql",
    _ => null,
};
static readonly Regex Token = new(@"(?<s>""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`)|(?<n>\b\d[\d_.xXa-fA-F]*\b)|(?<w>[A-Za-z_][A-Za-z0-9_]*)|(?<o>.)", RegexOptions.Compiled);

void ColorLines(IEnumerable<string> lines, string family)
{
    var (comment, keywords) = Languages[family];
    var kw = new HashSet<string>(keywords, StringComparer.Ordinal);
    PreviewText.Inlines.Clear();
    foreach (var line in lines)
    {
        var c = comment.Match(line);
        string code = c.Success ? line[..c.Index] : line;
        foreach (Match m in Token.Matches(code))
        {
            string color = m.Groups["s"].Success ? theme.Green : m.Groups["n"].Success ? theme.Yellow
                : m.Groups["w"].Success && kw.Contains(m.Value) ? theme.Purple
                : m.Groups["w"].Success && char.IsUpper(m.Value[0]) ? theme.Cyan : theme.Muted;
            PreviewText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = m.Value, Foreground = Brush(color) });
        }
        if (c.Success) PreviewText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line[c.Index..], Foreground = Brush(theme.Comment), FontStyle = Windows.UI.Text.FontStyle.Italic });
        PreviewText.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
    }
}
```

(The tokenizer is good enough for 22 lines of preview and wrong in edge cases such as a `//` inside a string: acceptable here. Markdown: `#` lines SemiBold in Accent, `- ` bullets, `` ` `` spans in the mono font, the rest as text: the same Runs approach.)

## 6.5 Filters and places

### `shell/menu-app/FinderFilter.cs` (new, plain C#, unit-tested in the cloud)

```csharp
namespace SystemMenu;

// Filter words in the finder's query: .pdf (or .pdf,.docx), today, yesterday, week, month, year,
// >10mb, <500kb, folder / file. What's left is the name query. Applied after fff's search (fff
// gets the extensions as its own *.ext constraints, so it doesn't spend its results on others).
public sealed record FinderFilter(string Rest, string[] Extensions, DateTime? Since, long? MinBytes, long? MaxBytes, bool? Folders)
{
    public static FinderFilter Parse(string query, DateTime now)
    {
        var rest = new List<string>(); var exts = new List<string>();
        DateTime? since = null; long? min = null, max = null; bool? folders = null;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i]; var lw = w.ToLowerInvariant();
            if (lw.Length > 1 && lw[0] == '.' && lw[1..].Split(',').All(e => e.TrimStart('.').All(char.IsLetterOrDigit)))
                exts.AddRange(lw.Split(',').Select(e => e.TrimStart('.')).Where(e => e.Length > 0));
            else if (lw == "today") since = now.Date;
            else if (lw == "yesterday") since = now.Date.AddDays(-1);
            else if (lw is "week" or "thisweek") since = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)); // Monday
            else if (lw == "this" && i + 1 < words.Length && words[i + 1].ToLowerInvariant() is "week" or "month" or "year") { words[i + 1] = "this" + words[i + 1]; }
            else if (lw is "month" or "thismonth") since = new DateTime(now.Year, now.Month, 1);
            else if (lw is "year" or "thisyear") since = new DateTime(now.Year, 1, 1);
            else if ((lw[0] is '>' or '<') && Bytes(lw[1..]) is long b) { if (lw[0] == '>') min = b; else max = b; }
            else if (lw is "folder" or "folders") folders = true;
            else if (lw is "file" or "files") folders = false;
            else rest.Add(w);
        }
        return new(string.Join(' ', rest), [.. exts.Distinct()], since, min, max, folders);
    }

    static long? Bytes(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+(?:\.\d+)?)(b|kb|k|mb|m|gb|g)?$");
        if (!m.Success) return null;
        double n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (long)(n * (m.Groups[2].Value switch { "kb" or "k" => 1024, "mb" or "m" => 1 << 20, "gb" or "g" => 1L << 30, _ => 1 }));
    }

    public bool Any => Extensions.Length > 0 || Since != null || MinBytes != null || MaxBytes != null || Folders != null;

    // fff's query: the name words plus *.ext constraints.
    public string FffQuery => string.Join(' ', new[] { Rest }.Concat(Extensions.Select(e => "*." + e)).Where(s => s.Length > 0));

    public bool Keep(FileSearch.Hit h) =>
        (Extensions.Length == 0 || (!h.IsDir && Extensions.Contains(Path.GetExtension(h.Name).TrimStart('.').ToLowerInvariant())))
        && (Since == null || h.Modified >= Since)
        && (MinBytes == null || (!h.IsDir && h.Size >= MinBytes))
        && (MaxBytes == null || (!h.IsDir && h.Size <= MaxBytes))
        && (Folders == null || h.IsDir == Folders);
}
```

`RenderFinder`: `var f = FinderFilter.Parse(bare, DateTime.Now); findHits = FileSearch.Search(f.FffQuery, …, limit: f.Any ? 400 : 60).Where(f.Keep).Take(60)`; each recognised filter word shows as a small pill after the crumbs (so you see it was understood). An empty name with a date filter (`today`) lists by `Modified` descending (fff's empty query gives frecency; re-sort).

Tests: `.pdf today` → exts [pdf], since today; `>10mb` → 10485760; `this week report` → since Monday, rest "report"; `.net` alone stays a filter (document it; `\.net` searches the name).

### `default/launcher/finder-places.txt` (new, layered)

```
# Places in the finder (Win+F): pinned at the top of the empty list, and as chips after the folders.
# Enter browses into the place (6.2). Layered: omawin's, the shared folder's, this account's.
#   <icon> | <name> | <path>      (env vars expand; ~ is your home folder)
󰉍 | Downloads | ~\Downloads
󰉋 | Home | ~
󰋊 | Temp | %TEMP%
```

```csharp
public sealed record Place(string Icon, string Name, string Path);
public static List<Place> Places() => [.. MenuModel.LayeredLines("finder-places.txt")
    .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
    .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray()).Where(p => p.Length == 3)
    .Select(p => new Place(p[0], p[1], Expand(p[2]))).Where(p => Directory.Exists(p.Path))];
static string Expand(string p) => Environment.ExpandEnvironmentVariables(p.StartsWith('~') ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..] : p);
```

Empty query (no chip): places first (rows with a "place" tag; Enter or → browses), then fff's frecency list. Linux places (`\\wsl.localhost\<distro>\home\<user>`) come from 3.7.

## 6.6 Many at once, and drag out

**Marking.** Space is part of the query (fff constraints use spaces), so marking uses **Ctrl+Space**, as the Packages page does (`TogglePackagePick`). (The roadmap says Space; confirm with the maintainer: open question.)

```csharp
case VirtualKey.Space when ctrl && finding && selected < findHits.Count:
    var p = findHits[selected].Path;
    if (!marked.Remove(p)) marked.Add(p);
    Move(1); Render(); // AddFileRow draws a check on marked rows; the footer says "3 marked"
    break;
```

Marks survive query changes (a set of paths) and clear on Esc at the top or after an action. `OpenActions` already uses `marked` when it isn't empty; actions with `Many == false` are hidden then.

**Drag out** (`AddFileRow`): each row is a real file drag source.

```csharp
v.Row.CanDrag = true;
v.Row.DragStarting -= v.DragHandler; // pooled rows: one handler, looking up the row's current hit
v.DragHandler = async (s, e) =>
{
    var deferral = e.GetDeferral();
    try
    {
        var hits = marked.Contains(hit.Path) ? findHits.Where(h => marked.Contains(h.Path)).ToList() : [hit];
        var items = new List<Windows.Storage.IStorageItem>();
        foreach (var h in hits)
            items.Add(h.IsDir ? await Windows.Storage.StorageFolder.GetFolderFromPathAsync(h.Path) : await Windows.Storage.StorageFile.GetFileFromPathAsync(h.Path));
        e.Data.SetStorageItems(items);
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Link;
        if (hit.IsDir || !IsImage(hit.Name)) e.DragUI.SetContentFromDataPackage(); // shell's drag image
    }
    finally { deferral.Complete(); }
};
v.Row.DragStarting += v.DragHandler;
```

Risks *(check on the PC)*: the window hides on deactivation (`Activated` handler): a drag must not hide it until the drop ends (`DropCompleted` → then hide); UIPI: the menu app inherits GlazeWM's uiAccess, and a drag from a higher-integrity process into a normal one is blocked; if so, start drags only when the menu app runs at medium IL without uiAccess, or hand the drag to a helper.

## 6.7 Into file dialogs (Listary's trick)

When the finder opens, `previous` (the window that had focus) is already recorded. If it's a common file dialog, a folder result gets a top action **"Open in the dialog"** and Enter on a folder does that instead of opening Explorer.

Plain Win32 rather than UI Automation (UIA is a large COM surface under AOT; the classic dialog's controls have stable IDs): the dialog is class `#32770`, and its File name box is the ComboBoxEx with control ID **1148** (`cmb13`) holding an Edit. Typing a folder path there and pressing Open navigates the dialog to it (it doesn't pick a file) *(check: true for both Open and Save, Windows 10/11, IFileDialog-based dialogs; Office's own dialogs are different and won't match)*.

```csharp
// shell/menu-app/FileDialogs.cs (new)
public static class FileDialogs
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dlg, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, System.Text.StringBuilder l);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_COMMAND = 0x0111;
    const int IDOK = 1, FileNameCombo = 1148;

    // The file name box of a common Open / Save dialog, or zero.
    public static IntPtr NameBox(IntPtr dialog)
    {
        var cls = new System.Text.StringBuilder(64);
        if (dialog == IntPtr.Zero || GetClassName(dialog, cls, cls.Capacity) == 0 || cls.ToString() != "#32770") return IntPtr.Zero;
        var combo = GetDlgItem(dialog, FileNameCombo);
        if (combo == IntPtr.Zero) return IntPtr.Zero;
        // ComboBoxEx32 > ComboBox > Edit, or ComboBox > Edit (check per Windows build)
        var edit = FindWindowEx(combo, IntPtr.Zero, "Edit", null);
        if (edit == IntPtr.Zero) { var inner = FindWindowEx(combo, IntPtr.Zero, "ComboBox", null); edit = FindWindowEx(inner, IntPtr.Zero, "Edit", null); }
        return edit;
    }

    // Send the dialog to `folder`, keeping the file name you'd typed (Save dialogs).
    public static bool Go(IntPtr dialog, string folder)
    {
        var edit = NameBox(dialog);
        if (edit == IntPtr.Zero) return false;
        var typed = new System.Text.StringBuilder(1024);
        SendMessage(edit, WM_GETTEXT, (IntPtr)typed.Capacity, typed);
        SetForegroundWindow(dialog);
        SendMessage(edit, WM_SETTEXT, IntPtr.Zero, folder);
        PostMessage(dialog, WM_COMMAND, (IntPtr)IDOK, IntPtr.Zero); // "Open"/"Save" with a folder path navigates
        // Put the name back once it has navigated (check: 150 ms enough; watch for the edit's text to clear instead).
        Task.Delay(150).ContinueWith(_ => SendMessage(edit, WM_SETTEXT, IntPtr.Zero, typed.ToString()));
        return true;
    }
}
```

`MainWindow`: on `Show(asFinder: true)`, `dialogInFront = FileDialogs.NameBox(previous) != IntPtr.Zero`; the footer then says "↵ open in the dialog"; Enter on a folder (or a file: its folder) calls `FileDialogs.Go(previous, dir)` and hides. UI Automation is the fallback for dialogs without ID 1148 *(check which apps)*.

## 6.8 Explorer's developer defaults

Entries in the settings catalog (`default/launcher/settings.txt`, format from [02-settings-and-one-search.md](02-settings-and-one-search.md): `id | icon | name | keywords | kind | how | notes`), each checked on the PC before shipping:

```
explorer.extensions | 󰈔 | File extensions | ext extensions explorer | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt on=0 off=1 broadcast | minBuild=19041
explorer.hidden | 󰘓 | Hidden files | hidden dotfiles explorer | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden on=1 off=2 broadcast |
explorer.fullpath | 󰉋 | Full path in the title bar | path title explorer | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState\FullPath on=1 off=0 broadcast |
explorer.compact | 󰘞 | Compact view | compact density explorer | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\UseCompactMode on=1 off=0 restart-explorer |
explorer.openTo | 󰋜 | Open File Explorer to | home this pc launch | choice | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\LaunchTo home=2 thispc=1 downloads=3 | (check: a value for the user's own folder; none known, so "This PC" is the default)
explorer.gallery | 󰉏 | Gallery in the navigation pane | gallery explorer | switch | reg HKCU\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}\System.IsPinnedToNameSpaceTree on=1 off=0 restart-explorer | minBuild=22621 (check)
explorer.home | 󰋜 | Home in the navigation pane | home quick access | switch | reg HKCU\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}\System.IsPinnedToNameSpaceTree on=1 off=0 restart-explorer | minBuild=22621 (check)
explorer.recent | 󰋚 | Recent files in Home / Quick access | recent recommendations | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ShowRecent on=1 off=0 broadcast |
explorer.frequent | 󰋚 | Frequent folders in Quick access | frequent | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ShowFrequent on=1 off=0 broadcast |
explorer.recommended | 󰋚 | Recommended section in Home | recommended | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Start_TrackDocs on=1 off=0 broadcast | (check: Home's "Recommended" on 23H2+ may use a different value)
explorer.checkboxes | 󰄵 | Item check boxes | checkbox select | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\AutoCheckSelect on=1 off=0 broadcast |
```

Dark / light by the theme is already the look step's `AppsUseLightTheme`. Applying the developer defaults: `default/launcher/settings-defaults.txt` (ids and values: `explorer.extensions on`, `explorer.hidden on`, `explorer.fullpath on`, `explorer.compact on`, `explorer.openTo thispc`, `explorer.gallery off`, `explorer.recent off`, `explorer.frequent off`), applied by the `look` step through `omawin setting set`, each recorded in `settings-history.tsv` so Undo puts any of them back. Until the catalog lands (Phase 1B), the look step can set the same registry values directly with `.backup` of the old values in `%LOCALAPPDATA%\launcher\explorer-before.reg` (`reg export`).

---

# Phase 7: sandboxes

## 7.1 `omawin sandbox test`

`commands.txt`:

```
sandbox test | The install on a clean Windows: Windows Sandbox (Pro and up), omawin installed from this checkout (-Branch <name>: from GitHub), then omawin check and a screenshot | pwsh bin/sandbox-test.ps1
sandbox | A throwaway Windows (Windows Sandbox) with Downloads shared read-only | pwsh bin/sandbox.ps1
```

### `bin/sandbox-test.ps1` (new, PS 5.1)

```powershell
# omawin's install on a clean Windows, in Windows Sandbox (Windows 11 Pro / Enterprise / Education):
# a .wsb maps this checkout read-only, an "in" folder with the script that runs at sign-in, and a
# writable "out" folder for the results; the script bootstraps winget (the sandbox has none), the
# way winget-pkgs' Tools/SandboxTest.ps1 does, installs omawin, runs omawin check, takes a
# screenshot, and writes done.txt. This waits for done.txt and prints the summary.
param(
    [string]$Branch,              # install what's pushed on this branch (GitHub's zip) instead of this checkout
    [int]$MemoryMB = 8192,
    [int]$TimeoutMinutes = 45,
    [switch]$KeepOpen             # leave the sandbox running afterwards (to look around)
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot

# --- can this PC do it? -------------------------------------------------------------------
$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
if ($edition -like 'Core*') {
    Write-Host "Windows Sandbox isn't available on Windows Home ($edition). CI tests the install on every push (.github/workflows)."
    exit 2
}
$sandboxExe = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path $sandboxExe)) {
    Write-Host 'Windows Sandbox is off. Turning it on needs admin and a restart:'
    Write-Host '  omawin install -Step admin   (pick "Windows Sandbox"), or'
    Write-Host '  Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All   (as admin)'
    exit 2
}

# --- the folders ----------------------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $env:LOCALAPPDATA "launcher\sandbox\$stamp"
$in = Join-Path $work 'in'; $out = Join-Path $work 'out'
New-Item -ItemType Directory -Force $in, $out | Out-Null

$source = 'mapped'
if ($Branch) {
    # The public repo's address from this checkout's origin (no owner written in the repo).
    $origin = (git -C $repo remote get-url origin).Trim() -replace '\.git$', '' -replace '^git@github\.com:', 'https://github.com/'
    $source = "$origin/archive/refs/heads/$Branch.zip"
}

# --- what runs inside, at sign-in (Windows PowerShell 5.1, as WDAGUtilityAccount, an admin) --
$inside = @'
$ErrorActionPreference = 'Continue'
$out = 'C:\sandbox-out'
Start-Transcript "$out\sandbox.log" | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
function Mark($what) { Add-Content "$out\times.txt" ("{0,6:N1}s  {1}" -f $watch.Elapsed.TotalSeconds, $what) }
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

# 1. winget. Windows Sandbox has no Store and no App Installer. SandboxTest.ps1's current way
#    (check: follow it if it changed): the Microsoft.WinGet.Client module's Repair-WinGetPackageManager,
#    which installs App Installer and its dependencies. The older way, still in SandboxTest.ps1's
#    history: download VCLibs, UI.Xaml and the msixbundle + license on the host, map them in, and
#    Add-AppxProvisionedPackage them here.
try {
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    Install-Module Microsoft.WinGet.Client -Force -Scope AllUsers -AllowClobber
    Import-Module Microsoft.WinGet.Client
    Repair-WinGetPackageManager -AllUsers -Latest -Force   # (check: parameter set on WinPS 5.1)
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\WindowsApps;$env:PATH"
    winget --version | Out-File "$out\winget-version.txt"
} catch { "winget bootstrap failed: $_" | Out-File "$out\winget-error.txt" }
Mark 'winget ready'

# 2. omawin's files at ~/omawin (install links configs from there).
$home2 = $env:USERPROFILE
if ('__SOURCE__' -eq 'mapped') {
    # From the read-only mapped checkout; build output and big caches left out.
    robocopy 'C:\omawin-src' "$home2\omawin" /MIR /XD bin obj lib .vs node_modules /NFL /NDL /NJH /NJS /NP | Out-Null
} else {
    Invoke-WebRequest '__SOURCE__' -OutFile "$env:TEMP\omawin.zip" -UseBasicParsing
    Expand-Archive "$env:TEMP\omawin.zip" "$env:TEMP\omawin-zip" -Force
    Move-Item (Get-ChildItem "$env:TEMP\omawin-zip" -Directory | Select-Object -First 1).FullName "$home2\omawin"
}
Mark 'files in place'

# 3. The install, plain output, no WSL (no nested virtualization in Windows Sandbox).
$env:OMAWIN_PLAIN = '1'
$env:OMAWIN_SANDBOX = '1'   # install: skip the wsl preset and anything that waits on a person (check: what install reads)
& powershell -NoProfile -ExecutionPolicy Bypass -File "$home2\omawin\install.ps1" *> "$out\install.log"
"$LASTEXITCODE" | Out-File "$out\install-exit.txt"
Mark 'install finished'
Copy-Item "$env:LOCALAPPDATA\launcher\install-times.txt" $out -ErrorAction SilentlyContinue

# 4. omawin check, from a fresh PowerShell (PATH as a new terminal would have it).
& powershell -NoProfile -ExecutionPolicy Bypass -File "$home2\omawin\check.ps1" *> "$out\check.txt"
"$LASTEXITCODE" | Out-File "$out\check-exit.txt"
Mark 'check finished'

# 5. The desktop, after giving GlazeWM and the bar time to start.
Start-Sleep 20
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$b = [Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
[Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
$bmp.Save("$out\desktop.png", [Drawing.Imaging.ImageFormat]::Png)
Mark 'screenshot'

Stop-Transcript | Out-Null
'done' | Out-File "$out\done.txt"
'@
$inside = $inside.Replace('__SOURCE__', $source)
[IO.File]::WriteAllText((Join-Path $in 'run.ps1'), $inside, (New-Object Text.UTF8Encoding $true))  # BOM: WinPS 5.1 reads UTF-8 right

# --- the .wsb -----------------------------------------------------------------------------
function Esc([string]$s) { [Security.SecurityElement]::Escape($s) }
$wsb = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Enable</Networking>
  <MemoryInMB>$MemoryMB</MemoryInMB>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$(Esc $repo)</HostFolder><SandboxFolder>C:\omawin-src</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(Esc $in)</HostFolder><SandboxFolder>C:\sandbox-in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(Esc $out)</HostFolder><SandboxFolder>C:\sandbox-out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File C:\sandbox-in\run.ps1</Command>
  </LogonCommand>
</Configuration>
"@
$wsbFile = Join-Path $work 'omawin-test.wsb'
[IO.File]::WriteAllText($wsbFile, $wsb)

# --- start it -----------------------------------------------------------------------------
# 24H2+: the wsb command line (start / list / exec / stop). Its --config takes the XML itself,
# not a path (check). Before 24H2: open the .wsb with WindowsSandbox.exe; only one sandbox at a time.
$id = $null
if (Get-Command wsb -ErrorAction SilentlyContinue) {
    $started = wsb start --config $wsb 2>&1 | Out-String
    $m = [regex]::Match($started, '[0-9a-fA-F-]{36}')        # it prints the new sandbox's id (check)
    if ($m.Success) { $id = $m.Value }
    if ($id) { wsb connect --id $id | Out-Null }             # a window, to watch it (check: needed, or does start show one?)
} else {
    Start-Process $sandboxExe -ArgumentList "`"$wsbFile`""
}
Write-Host "Windows Sandbox started; results in $out"

# --- wait for done.txt --------------------------------------------------------------------
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$seen = 0
while (-not (Test-Path (Join-Path $out 'done.txt')) -and (Get-Date) -lt $deadline) {
    Start-Sleep 5
    $times = Join-Path $out 'times.txt'
    if (Test-Path $times) { $lines = @(Get-Content $times); for (; $seen -lt $lines.Count; $seen++) { Write-Host "  $($lines[$seen])" } }
}
if (-not (Test-Path (Join-Path $out 'done.txt'))) { Write-Host "Timed out after $TimeoutMinutes min; see $out\sandbox.log"; exit 1 }

# --- the summary ----------------------------------------------------------------------------
$installExit = (Get-Content (Join-Path $out 'install-exit.txt') -ErrorAction SilentlyContinue) -join ''
$checkExit = (Get-Content (Join-Path $out 'check-exit.txt') -ErrorAction SilentlyContinue) -join ''
Write-Host ''
Write-Host "install exit $installExit, check exit $checkExit"
Get-Content (Join-Path $out 'check.txt') -Tail 15 -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $out 'install-times.txt')) { Write-Host "`nsteps:"; Get-Content (Join-Path $out 'install-times.txt') }
Write-Host "`nscreenshot: $out\desktop.png"
if ($id -and -not $KeepOpen) { wsb stop --id $id | Out-Null }
if ($installExit -ne '0' -or $checkExit -ne '0') { exit 1 }
```

Also:
- `install/admin.ps1`'s plan gains an opt-in `Windows Sandbox` line (`Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All -NoRestart`), offered only when `EditionID` isn't `Core*`, and said up front: "needs a restart".
- `install.ps1` reads `OMAWIN_SANDBOX=1` (or a `-Unattended` switch): no wsl preset, no questions, no sign-out.
- The sandbox user **is an admin** (WDAGUtilityAccount): this run doesn't prove "no admin" (2.9). A second mode later: create a standard local user inside and run the install as that user via `Start-Process -Credential` *(check: whether the logon session works for GUI parts)*.
- Risks (roadmap): the winget bootstrap changes; 24H2's missing inbox Store apps (Terminal, Notepad: the install must not assume them); GlazeWM / the bar in the sandbox's RDP-like session (the screenshot shows whether they draw).

## 7.2 A throwaway Windows from the menu

`default/launcher/menu.txt`: `tools.sandbox | 󰆧 | Sandbox (throwaway Windows) | omawin sandbox | ?sandbox-capable` (5.8 hides it on Home; on Pro without the feature, `bin/sandbox.ps1` explains how to turn it on, as above).

### `bin/sandbox.ps1` (new, PS 5.1)

```powershell
# A throwaway Windows: Windows Sandbox with your Downloads shared read-only (for an installer or a
# file you don't trust), networking on, clipboard on. Everything inside is gone when you close it.
$ErrorActionPreference = 'Stop'
if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID -like 'Core*') { Write-Host "Windows Sandbox isn't on Windows Home."; exit 2 }
$exe = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path $exe)) { Write-Host 'Windows Sandbox is off: omawin install -Step admin (pick Windows Sandbox), then restart.'; exit 2 }
$downloads = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path
$wsb = @"
<Configuration>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$([Security.SecurityElement]::Escape($downloads))</HostFolder><SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\Downloads</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  </MappedFolders>
</Configuration>
"@
$file = Join-Path $env:LOCALAPPDATA 'launcher\sandbox\throwaway.wsb'
New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
[IO.File]::WriteAllText($file, $wsb)
Start-Process $exe -ArgumentList "`"$file`""
```

## 7.3 Coding agents fenced in: `omawin agent sandbox`

`commands.txt`: `agent sandbox | A coding agent in your Linux (Arch), in the current project, inside its sandbox: omawin agent sandbox [claude|codex|opencode] [folder] | pwsh bin/agent-sandbox.ps1`

Claude Code's sandbox works in **WSL2** (bubblewrap + socat; not WSL1; on native Windows it runs unsandboxed). Settings keys (from code.claude.com/docs/en/sandboxing, read 2026-10-30): `sandbox.enabled`, `sandbox.failIfUnavailable` (exit at start instead of running open), `sandbox.allowUnsandboxedCommands` (false: no `dangerouslyDisableSandbox` retry), `sandbox.autoAllowBashIfSandboxed`, `sandbox.excludedCommands`, `sandbox.filesystem.allowWrite / denyWrite / denyRead / allowRead`, `sandbox.network.allowedDomains / deniedDomains / strictAllowlist / allowLocalBinding / allowUnixSockets`.

The project's `.claude/settings.local.json` is the user's (never overwritten); omawin passes its settings with `--settings <file>` *(check: the flag merges a settings file for this session)*, from `config/claude/sandbox-settings.json` in the repo:

```json
{
  "sandbox": {
    "enabled": true,
    "failIfUnavailable": true,
    "allowUnsandboxedCommands": false,
    "autoAllowBashIfSandboxed": true,
    "excludedCommands": [],
    "filesystem": {
      "denyRead": ["~/.ssh", "~/.aws", "~/.config/gh", "/mnt/c/Users"]
    },
    "network": {
      "allowedDomains": ["github.com", "*.github.com", "*.githubusercontent.com", "registry.npmjs.org", "*.npmjs.org", "pypi.org", "files.pythonhosted.org", "crates.io", "static.crates.io", "index.crates.io", "proxy.golang.org", "rubygems.org", "nuget.org", "*.nuget.org"]
    }
  }
}
```

(Writes are limited to the working directory by default; the denyRead of `/mnt/c/Users` keeps Windows' home out *(check: denyRead with the project itself under /mnt/c: allowRead re-allows it, or keep projects in Linux's home, which 3.x recommends)*.)

### `bin/agent-sandbox.ps1` (new)

```powershell
# A coding agent fenced in: in Linux (the default distro, omawin default linux), in the project,
# with its sandbox on and refusing to start without it. Claude Code: its own sandbox (bubblewrap
# in WSL2). Codex: its own sandbox (--sandbox workspace-write) in Linux too. OpenCode: no sandbox
# of its own; refused here, said so.
param([Parameter(Position = 0)][string]$Agent = 'claude', [Parameter(Position = 1)][string]$Folder = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$distro = (& omawin.exe default linux --get 2>$null) ; if (-not $distro) { $distro = 'archlinux' }  # (check: how omawin.cs reports the default distro)

# The folder as Linux sees it: a Windows path through /mnt/<drive>, a \\wsl.localhost path as is.
function To-Linux([string]$p) {
    if ($p -match '^\\\\wsl(\$|\.localhost)\\[^\\]+\\(.*)$') { return '/' + ($Matches[2] -replace '\\', '/') }
    (& wsl.exe -d $distro -- wslpath -a ($p -replace '\\', '/')).Trim()
}
$dir = To-Linux $Folder

# What the sandbox needs in Linux (Arch: pacman; omawin's Arch has these from 3.2's package list).
& wsl.exe -d $distro -- sh -c 'command -v bwrap >/dev/null && command -v socat >/dev/null'
if ($LASTEXITCODE -ne 0) {
    Write-Host 'The sandbox needs bubblewrap and socat in Linux: sudo pacman -S --needed bubblewrap socat'
    exit 1
}

switch ($Agent) {
    'claude' {
        $settings = To-Linux (Join-Path $repo 'config\claude\sandbox-settings.json')
        $cmd = "cd '$dir' && exec claude --settings '$settings'"
    }
    'codex' { $cmd = "cd '$dir' && exec codex --sandbox workspace-write --ask-for-approval on-request" }  # (check: current flag names)
    default { Write-Host "$Agent has no sandbox of its own; use claude or codex here."; exit 1 }
}
& omawin.exe launch terminal --wsl --title "$Agent (sandboxed)" --dir $Folder -- bash -lc $cmd
```

- Launcher row: `󰒃 | Agent in a sandbox | omawin.exe agent sandbox claude` (it uses the focused project folder when started from a project; from the launcher, the Projects page's Ctrl+K action list gets "Agent in a sandbox").
- herdr tab option in `default/launcher/project-tabs.txt`: a `claude-sandbox` tab running the same command.
- Check with `/sandbox` inside Claude Code that it's active; `failIfUnavailable` makes a broken setup loud.

## 7.4 / 7.5

- 7.4 (later): `sandbox-test.ps1 -Checks` additionally runs `SystemMenu.exe --snip-selftest`, `--qs-selftest` and the bar's preview mode inside, screenshots each into `out\`.
- 7.5: launcher item `󰣇 | Try real Omarchy | https://github.com/omacom/try-omarchy-windows` and a manual page; nothing installed by omawin itself.

---

## Open questions to file (`open-questions/<slug>.md`, defaults in brackets)

- `finder-mark-key`: marking rows with Space (roadmap) vs Ctrl+Space (query has spaces) [Ctrl+Space].
- `webapp-profile`: one profile per web app (separate sign-ins) vs the browser's shared profile [per app, `--shared-profile` option].
- `scratchpad-keys`: Win+S toggle, Win+Alt+S send [unbound until confirmed].
- `finder-action-keys`: Ctrl+E editor, Ctrl+T terminal, Ctrl+Shift+C copy path, F2 rename, Delete recycle [shown in the list, not bound].
- `keys-txt`: keep `default/launcher/keys.txt` for non-GlazeWM keys after the cheat sheet reads `config.yaml` [keep, trimmed].
