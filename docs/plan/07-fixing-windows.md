# Phase 8: fixing Windows, how to build it

> **Uncompiled sketch.** Nothing below has been built or run. It's the code and data [roadmap.md](roadmap.md) Phase 8 (8.0-8.51, 8K) needs, written so the work could start from this file alone. Signatures, struct layouts and registry values are what the SDK headers and Microsoft's docs say as far as the author knows; anything marked **(check)** is uncertain and must be confirmed on a PC (or in the header) before it ships. Nothing here invents an API: where no documented one exists it says so.

Read with: [AGENTS.md](../../AGENTS.md) (no admin by default, one prompt for opt-in admin; keys never change by guesswork), [roadmap.md](roadmap.md) 1B (the settings catalog) and 2.9 (no admin), [plugins.md](plugins.md) (the plugin protocol).

## 0. Ground rules for this code

- **C# 12, .NET 9, Native AOT, no reflection.** P/Invoke with `[LibraryImport]` (source-generated, `partial`), blittable structs only (`fixed char` buffers, `int` for `BOOL`, two `uint`s for a `FILETIME` inside a struct so it stays 4-aligned). COM either by vtable slot (`delegate* unmanaged[Stdcall]`, as `menu-app/AppIcons.cs` and `Wmi.cs`'s `Vtable` do) or `[GeneratedComInterface]` (as `menu-app/Audio.cs` does). Never `[ComImport]`, never `dynamic`, never `Marshal.GetObjectForIUnknown`. JSON through `JsonDocument` / `Utf8JsonWriter` or a source-generated `JsonSerializerContext`.
- The existing menu-app code still uses `[DllImport]` (works under AOT for blittable signatures); new code uses `[LibraryImport]`. Moving old code over is not part of Phase 8.
- **Scripts:** anything in `install/` or run by the admin batch is **Windows PowerShell 5.1** (no `??`, no `?.`, no ternary). `bin/profile-ram.ps1` and other `omawin` commands run in pwsh 7 (they already use `??`).
- **Homes** (roadmap 8.0): *core* = `menu-app/`; *shared library* = `shared/Omawin.Windows/` (referenced by the menu app and first-party plugins); *first-party plugin* = `plugins/<id>/` (`manifest.json`, `src/<Id>.csproj`, AOT exe, `--selftest`); *catalog* = a line in `launcher/settings.txt` (1B.2).
- **Admin** (2.9): code never elevates itself. It queues a verb (`admin-pending.txt`, plan 03's mechanism); `omawin admin` shows the list; one UAC prompt runs `install/admin-batch.ps1`; the elevated process does the fixed list and exits. Section 0.3.
- **User data** (history, indexes, profiles, snapshots) goes in `%LOCALAPPDATA%\launcher\`, never the repo.

### 0.1 What already exists (don't rebuild)

| Item | Already there | Left for Phase 8 |
|---|---|---|
| 8.10 per-app volume and output | `Audio.cs`: `Apps()`, `ChangeApp`, `ToggleAppMute` (`IAudioSessionManager2` / `ISimpleAudioVolume`), `SendTo` / `OutputOf` (undocumented `IAudioPolicyConfig`, EarTrumpet's) | live refresh from session events instead of re-reading; see 8.10 |
| 8.14 private copies skipped | `Background.cs` `ClipboardHistory.ReadText` checks `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory` | `CanUploadToCloudClipboard`, event-driven listener (it polls every 500 ms), images, pins, transforms |
| 8.49 Bluetooth services | `QuickSettings.cs` `Bt.SetAudioServices` already calls `BluetoothSetServiceState` with the Handsfree UUID | a call that turns off **only** Handsfree; see 8.49 |
| 8.21 WLAN session | `QuickSettings.cs` `Wlan.Session`, `WlanSetProfile` | `WlanGetProfile` with the plain-key flag; QR |
| 8.5 GlazeWM IPC | `Bar/GlazeWm.cs` (subscribes, queries, matches replies by `clientMessage`) | `query windows` + focus history |
| 8.17 process tree, private working set | `bin/profile-ram.ps1` (CIM), `About.cs` (`NtQuerySystemInformation`) | in-process snapshot for the launcher; see 8.17 |
| `StartupApproved` format | `no-ads.ps1` writes `03` + 11 zero bytes to disable | the reader and the full list; see 8.16 |

### 0.2 Shared helpers (`shared/Omawin.Windows/Native.cs`)

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Omawin.Windows;

// COM by vtable slot (slot numbers as the SDK header lists the methods, IUnknown's three first).
public static unsafe partial class Com
{
    public const uint CLSCTX_INPROC_SERVER = 0x1, CLSCTX_LOCAL_SERVER = 0x4, CLSCTX_ALL = 0x17;

    public static IntPtr Slot(IntPtr com, int index) => (*(IntPtr**)com)[index];
    public static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    public static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr obj);
    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coinit); // 2 = COINIT_APARTMENTTHREADED

    public static IntPtr Create(Guid clsid, Guid iid, uint context = CLSCTX_ALL)
    {
        Check(CoCreateInstance(clsid, IntPtr.Zero, context, iid, out var p));
        return p;
    }

    public static IntPtr Query(IntPtr unknown, Guid iid)
    {
        var qi = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(unknown, 0);
        IntPtr result;
        Check(qi(unknown, &iid, &result));
        return result;
    }

    // For [GeneratedComInterface] wrappers, as Audio.cs does.
    public static readonly StrategyBasedComWrappers Wrappers = new();
    public static T Wrap<T>(IntPtr unknown) => (T)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
}

public static partial class Win32
{
    public const int ERROR_MORE_DATA = 234, ERROR_INSUFFICIENT_BUFFER = 122, ERROR_NO_MORE_ITEMS = 259, ERROR_HANDLE_EOF = 38;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hwnd, uint msg, nuint w, string? l, uint flags, uint timeoutMs, out nuint result);

    // WM_SETTINGCHANGE to every top-level window: "Environment" after an env var change, null or
    // the policy area otherwise. SMTO_ABORTIFHUNG so one hung window can't stall us.
    public static void BroadcastSettingChange(string? area)
    {
        const nint HWND_BROADCAST = 0xFFFF;
        const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 5000, out _);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr h);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* name, ref uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(IntPtr process, uint exitCode);

    public const uint PROCESS_TERMINATE = 0x0001, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static unsafe string? ExePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally { CloseHandle(h); }
    }

    // A pid plus its start time names one process for good (pids are reused).
    public static long StartTime(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try { return GetProcessTimes(h, out var c, out _, out _, out _) ? c : 0; }
        finally { CloseHandle(h); }
    }
}
```

### 0.3 Admin verbs (one prompt, then exit)

The mechanism is plan 03's ([03-install-update-packages.md](03-install-update-packages.md), "No admin"): pending verbs in `%LOCALAPPDATA%\launcher\admin-pending.txt` (one `<verb> <arg>` per line), `bin/admin.ps1` previews them and raises the one UAC prompt when you say so (never during an update), `install/admin-batch.ps1 -Plan` checks each line against `install/admin-verbs.ps1`'s table and runs it, and the status file reports back. First-party plugins reach it through plan 05's `AdminQueue` ([05-plugins.md](05-plugins.md) §9.6). Catalog lines marked `admin` go through plan 03's `setting <id>` verb. **Naming (check):** plan 05 writes multi-word verbs (`hosts write`) while plan 03's batch splits a line on its first space; use plan 03's hyphenated form (`hosts-write`) in both.

Phase 8 adds these to `$AdminVerbs` (PS 5.1; `Args` is matched against the whole argument string; nothing else from the non-admin side is trusted):

```powershell
# install/admin-verbs.ps1 additions
$PathRx = '^"[A-Za-z]:\\[^"<>|?*]+"'
$AdminVerbs += @{
    'hosts-write'      = @{ Args = '^[0-9A-F]{64}$'; Restart = $false; Say = { param($a) 'Replace the hosts file (the old one is kept beside it)' } }   # arg: SHA-256 of pending.hosts
    'dns-set'          = @{ Args = '^\d{1,4} ([0-9a-fA-F:.]{2,39})(,[0-9a-fA-F:.]{2,39}){0,3}$'; Restart = $false; Say = { param($a) "Set a network adapter's DNS servers to $(($a -split ' ')[1])" } }
    'dns-reset'        = @{ Args = '^\d{1,4}$'; Restart = $false; Say = { param($a) "Give a network adapter's DNS back to your router (DHCP)" } }
    'usn-index-install'= @{ Args = $null; Restart = $false; Say = { param($a) 'Install the whole-disk search service (it reads file names, never contents)' } }
    'usn-index-remove' = @{ Args = $null; Restart = $false; Say = { param($a) 'Remove the whole-disk search service' } }
    'service-start'    = @{ Args = '^[A-Za-z0-9_.-]{1,80} (auto|delayed|manual|disabled)$'; Restart = $false; Say = { param($a) $n, $m = $a -split ' '; "Set the service $n to start: $m" } }
    'task-enable'      = @{ Args = '^"\\[^"]*" "[^"]+" (on|off)$'; Restart = $false; Say = { param($a) "Turn a scheduled task $(($a -split ' ')[-1])" } }
    'startup-machine'  = @{ Args = '^(Run|Run32|StartupFolder) "[^"]+" (on|off)$'; Restart = $false; Say = { param($a) "Turn a startup program for every account $(($a -split ' ')[-1])" } }
    'dynamic-ports'    = @{ Args = '^reset$'; Restart = $false; Say = { param($a) "Move Windows' reserved ports out of the developer range (3000, 8080, …)" } }
    'reserve-port'     = @{ Args = '^\d{1,5}$'; Restart = $false; Say = { param($a) "Keep port $a free for your own servers" } }
    'devdrive-create'  = @{ Args = $PathRx + ' \d{2,5} [D-Z]$'; Restart = $false; Say = { param($a) $p = $a -split ' '; "Create a Dev Drive ($($p[-2]) GB, drive $($p[-1]):)" } }
    'devdrive-trust'   = @{ Args = '^[D-Z]$'; Restart = $false; Say = { param($a) "Trust Dev Drive $($a): (Defender scans it in performance mode)" } }
    'scancode-map'     = @{ Args = '^(remove|([0-9a-f]{2}){16,})$'; Restart = $true; Say = { param($a) 'Remap keys for every account, the sign-in screen included' } }
    'firewall-allow'   = @{ Args = $PathRx + ' "[^"]{1,80}"$'; Restart = $false; Say = { param($a) "Let $(($a -split '"')[3]) accept connections on private networks" } }
    'ssh-agent'        = @{ Args = '^(auto|manual)$'; Restart = $false; Say = { param($a) "Start Windows' SSH agent with Windows" } }
    'update-uninstall' = @{ Args = '^\d{6,8}$'; Restart = $true; Say = { param($a) "Uninstall update KB$a" } }
    'end-process'      = @{ Args = '^\d{1,10} \d{1,20}$'; Restart = $false; Say = { param($a) "End process $(($a -split ' ')[0]) (it runs as admin or another account)" } }
    'env-machine'      = @{ Args = '^[A-Za-z_][A-Za-z0-9_()]{0,63} .{0,32000}$'; Restart = $false; Say = { param($a) "Change the environment variable $(($a -split ' ', 2)[0]) for every account" } }
    'wsl-compact'      = @{ Args = $PathRx + '$'; Restart = $false; Say = { param($a) 'Compact a Linux disk (WSL is shut down first)' } }
    'hibernate'        = @{ Args = '^(on|off)$'; Restart = $false; Say = { param($a) "Turn hibernation $a" } }
}
```

In `install/admin-batch.ps1`'s `switch ($s.Verb)`, each case splits `$s.Arg` itself, quotes respected:

```powershell
# Splits '"C:\a b\c.vhdx" 100 D' into C:\a b\c.vhdx, 100, D.
function Split-Arg([string]$Arg) { @([regex]::Matches($Arg, '"([^"]*)"|(\S+)') | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } }) }
```

The small verbs' cases are here; the bigger ones are in their sections below (`$a = Split-Arg $s.Arg`).

```powershell
'service-start' {
    $a = Split-Arg $s.Arg
    $svc = Get-Service -Name $a[0] -ErrorAction Stop
    Say $s.Line 'before' "$($a[0])`t$($svc.StartType)"
    switch ($a[1]) {
        'delayed'  { sc.exe config $a[0] start= delayed-auto | Out-Null }
        'auto'     { Set-Service $a[0] -StartupType Automatic }
        'manual'   { Set-Service $a[0] -StartupType Manual }
        'disabled' { Set-Service $a[0] -StartupType Disabled; Stop-Service $a[0] -Force -ErrorAction SilentlyContinue }
    }
}
'task-enable' {
    $a = Split-Arg $s.Arg
    if ($a[2] -eq 'on') { Enable-ScheduledTask -TaskPath $a[0] -TaskName $a[1] | Out-Null } else { Disable-ScheduledTask -TaskPath $a[0] -TaskName $a[1] | Out-Null }
}
'startup-machine' {
    $a = Split-Arg $s.Arg
    $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\$($a[0])"
    if (-not (Test-Path $key)) { New-Item $key -Force | Out-Null }
    $bytes = New-Object byte[] 12
    if ($a[2] -eq 'off') { $bytes[0] = 3; [BitConverter]::GetBytes([DateTime]::UtcNow.ToFileTimeUtc()).CopyTo($bytes, 4) } else { $bytes[0] = 2 }
    Set-ItemProperty $key -Name $a[1] -Value $bytes -Type Binary
}
'firewall-allow' {
    $a = Split-Arg $s.Arg
    if (-not (Test-Path -LiteralPath $a[0])) { throw "not found: $($a[0])" }
    $name = "omawin: $($a[1]) (private networks)"
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $name -Direction Inbound -Program $a[0] -Action Allow -Profile Private | Out-Null
    }
}
'ssh-agent' {
    Set-Service ssh-agent -StartupType $(if ($s.Arg -eq 'auto') { 'Automatic' } else { 'Manual' })
    if ($s.Arg -eq 'auto') { Start-Service ssh-agent }
    # The user's side then sets, without admin: git config --global core.sshCommand C:/Windows/System32/OpenSSH/ssh.exe
}
'hibernate' { powercfg /hibernate $s.Arg | Out-Null }
'end-process' {
    $a = Split-Arg $s.Arg
    $p = Get-Process -Id ([int]$a[0]) -ErrorAction Stop
    if ($p.StartTime.ToFileTimeUtc() -ne [long]$a[1]) { throw 'that process already ended (its id now belongs to another)' }
    Stop-Process -Id $p.Id -Force
}
'wsl-compact' {
    $vhd = (Split-Arg $s.Arg)[0]
    wsl.exe --shutdown
    $tmp = Join-Path $env:TEMP "omawin-compact-$PID.txt"
    "select vdisk file=`"$vhd`"", 'attach vdisk readonly', 'compact vdisk', 'detach vdisk' | Set-Content $tmp -Encoding ascii
    diskpart /s $tmp | Out-Null
    Remove-Item $tmp
}
``` Undo: before changing a value the case writes the old one to the status file (`"<line>\tbefore\t<what>\t<old>"`), which `bin/admin.ps1` copies to `settings-history.tsv` (plan 02's undo), so undo is another verb in a later batch.

## 8A. Files and disks

### 8.1 Who holds a file (`shared/Omawin.Windows/RestartManager.cs`, `omawin who-has <path>`)

Restart Manager is documented (`restartmanager.h`, `rstrtmgr.dll`), needs no admin, and reports processes of every session (names for other users' processes; ending them needs admin). It sees open file handles and loaded modules, **not** a process's current directory (a shell `cd`'d into a folder): for a folder, the files inside it are registered, and a "can't delete the folder" with no holder reported points at a terminal sitting in it (say so in the row).

```csharp
using System.Runtime.InteropServices;
namespace Omawin.Windows;

public sealed record Holder(uint Pid, long Started, string Name, string? Service, HolderKind Kind, string? Exe);
public enum HolderKind { Unknown = 0, MainWindow = 1, OtherWindow = 2, Service = 3, Explorer = 4, Console = 5, Critical = 1000 }

public static unsafe partial class RestartManager
{
    const int CCH_RM_SESSION_KEY = 32, CCH_RM_MAX_APP_NAME = 255, CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS { public uint dwProcessId; public uint StartLow, StartHigh; } // FILETIME kept 4-aligned: 12 bytes

    [StructLayout(LayoutKind.Sequential)]
    struct RM_PROCESS_INFO // 668 bytes
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[CCH_RM_MAX_APP_NAME + 1];
        public fixed char strServiceShortName[CCH_RM_MAX_SVC_NAME + 1];
        public int ApplicationType;   // RM_APP_TYPE
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;      // BOOL
    }

    [LibraryImport("rstrtmgr.dll")] private static partial int RmStartSession(out uint session, uint flags, char* sessionKey);
    [LibraryImport("rstrtmgr.dll")] private static partial int RmEndSession(uint session);
    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmRegisterResources(uint session, uint nFiles, IntPtr* files, uint nApplications, RM_UNIQUE_PROCESS* applications, uint nServices, IntPtr* services);
    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmGetList(uint session, out uint needed, ref uint count, RM_PROCESS_INFO* info, out uint rebootReasons);

    // Every process holding any of these files (or files under these folders, up to maxFiles).
    public static List<Holder> Who(IEnumerable<string> paths, int maxFiles = 5000)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(maxFiles - files.Count));
            else files.Add(p);
            if (files.Count >= maxFiles) break;
        }
        if (files.Count == 0) return [];

        char* key = stackalloc char[CCH_RM_SESSION_KEY + 1];
        int err = RmStartSession(out uint session, 0, key);
        if (err != 0) throw new System.ComponentModel.Win32Exception(err);
        var strings = new IntPtr[files.Count];
        try
        {
            for (int i = 0; i < files.Count; i++) strings[i] = Marshal.StringToHGlobalUni(files[i]);
            // Registered in chunks: one call with thousands of names is fine, but chunks keep each call short.
            for (int at = 0; at < strings.Length; at += 500)
                fixed (IntPtr* p = &strings[at])
                {
                    err = RmRegisterResources(session, (uint)Math.Min(500, strings.Length - at), p, 0, null, 0, null);
                    if (err != 0) throw new System.ComponentModel.Win32Exception(err);
                }

            uint count = 0;
            RM_PROCESS_INFO[] info = [];
            while (true)
            {
                fixed (RM_PROCESS_INFO* pi = info)
                    err = RmGetList(session, out uint needed, ref count, pi, out _);
                if (err == 0) break;
                if (err != Win32.ERROR_MORE_DATA) throw new System.ComponentModel.Win32Exception(err);
                info = new RM_PROCESS_INFO[needed + 4]; // more may open between the two calls
                count = (uint)info.Length;
            }

            var result = new List<Holder>((int)count);
            for (int i = 0; i < count; i++)
            {
                ref var r = ref info[i];
                fixed (char* name = r.strAppName) fixed (char* svc = r.strServiceShortName)
                {
                    long started = ((long)r.Process.StartHigh << 32) | r.Process.StartLow;
                    var service = new string(svc);
                    result.Add(new Holder(r.Process.dwProcessId, started, new string(name),
                        service.Length > 0 ? service : null, (HolderKind)r.ApplicationType, Win32.ExePath(r.Process.dwProcessId)));
                }
            }
            return result;
        }
        finally
        {
            foreach (var s in strings) if (s != IntPtr.Zero) Marshal.FreeHGlobal(s);
            RmEndSession(session);
        }
    }
}
```

Acting on a holder (`shared/Omawin.Windows/Processes.cs`):

```csharp
public static unsafe partial class Processes
{
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, nint, int> callback, nint state);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(IntPtr hwnd);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hwnd, uint msg, nuint w, nint l);

    [ThreadStatic] static List<IntPtr>? found;
    [ThreadStatic] static uint wanted;

    [UnmanagedCallersOnly]
    static int Collect(IntPtr hwnd, nint _)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == wanted && IsWindowVisible(hwnd)) found!.Add(hwnd);
        return 1;
    }

    // "Close": WM_CLOSE to its visible top-level windows, as clicking X would (it may ask to save).
    // False when it has none (a service, a console app): then only "End" is offered.
    public static bool Close(Holder h)
    {
        if (Win32.StartTime(h.Pid) != h.Started) return false; // gone, pid reused
        found = []; wanted = h.Pid;
        EnumWindows(&Collect, 0);
        foreach (var w in found) PostMessage(w, 0x0010 /* WM_CLOSE */, 0, 0);
        return found.Count > 0;
    }

    // "End" (after the user confirmed). Access denied for an elevated or another user's process:
    // the shell then offers the admin verb `end-process <pid> <started>`, which checks the start
    // time the same way before Stop-Process.
    public static string? End(Holder h)
    {
        var p = Win32.OpenProcess(Win32.PROCESS_TERMINATE | Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, h.Pid);
        if (p == IntPtr.Zero) return "needs admin";
        try
        {
            if (!Win32.GetProcessTimes(p, out var created, out _, out _, out _) || created != h.Started) return "it already ended";
            return Win32.TerminateProcess(p, 1) ? null : $"error {Marshal.GetLastPInvokeError()}";
        }
        finally { Win32.CloseHandle(p); }
    }
}
```

`bin/commands.txt`: `who-has | Which programs hold a file or folder open; close or end them | app --who-has`. From a terminal the menu app answers on its pipe with lines `pid\tname\tkind\tservice\texe`; with no terminal (from a key) it opens the launcher's *Who has* page on the path in the clipboard (or Explorer's selection, 8.4). `Holder.Kind == Critical` (csrss, smss…) shows no End. `--selftest` in the shared library: open a temp file with `FileShare.None`, `Who` must return our own pid.

### 8.2 Whole-disk search (`plugins/disk-index/`: a service + a finder source)

Reading the USN journal needs a volume handle (`\\.\C:`), so admin: the indexer is a Windows service (the one standing elevated thing, only when the user turns this on). Its binary goes in `%ProgramFiles%\omawin\disk-index\` (admin-writable only: a service exe in a user-writable folder would let any process run code as SYSTEM). The finder talks to it over a named pipe.

```csharp
// plugins/disk-index/src/Usn.cs
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

static unsafe partial class Usn
{
    // CTL_CODE(FILE_DEVICE_FILE_SYSTEM, n, method, FILE_ANY_ACCESS)
    const uint FSCTL_ENUM_USN_DATA = 0x000900B3;     // 44, METHOD_NEITHER
    const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;  // 46, METHOD_NEITHER
    const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4; // 61, METHOD_BUFFERED
    const uint FSCTL_CREATE_USN_JOURNAL = 0x000900E7;// 57, METHOD_NEITHER
    const uint USN_REASON_FILE_CREATE = 0x100, USN_REASON_FILE_DELETE = 0x200,
               USN_REASON_RENAME_OLD_NAME = 0x1000, USN_REASON_RENAME_NEW_NAME = 0x2000;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0 { public ulong UsnJournalID; public long FirstUsn, NextUsn, LowestValidUsn, MaxUsn; public ulong MaximumSize, AllocationDelta; } // 56

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_ENUM_DATA_V0 { public ulong StartFileReferenceNumber; public long LowUsn, HighUsn; } // 24: V2 records

    [StructLayout(LayoutKind.Sequential)]
    struct READ_USN_JOURNAL_DATA_V0 { public long StartUsn; public uint ReasonMask, ReturnOnlyOnClose; public ulong Timeout, BytesToWaitFor, UsnJournalID; } // 40

    [StructLayout(LayoutKind.Sequential)]
    struct CREATE_USN_JOURNAL_DATA { public ulong MaximumSize, AllocationDelta; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, IntPtr overlapped);

    public static SafeFileHandle OpenVolume(char letter) =>
        CreateFile($@"\\.\{letter}:", 0x80000000 /* GENERIC_READ */, 0x3 /* SHARE_READ|WRITE */, 0, 3 /* OPEN_EXISTING */, 0, 0);

    public static USN_JOURNAL_DATA_V0 Query(SafeFileHandle volume)
    {
        USN_JOURNAL_DATA_V0 data;
        if (!DeviceIoControl(volume, FSCTL_QUERY_USN_JOURNAL, null, 0, &data, sizeof(USN_JOURNAL_DATA_V0), out _, 0))
        {
            // ERROR_JOURNAL_NOT_ACTIVE (1179): create one (what Windows Search does too; 32 MB, 8 MB delta).
            if (Marshal.GetLastPInvokeError() != 1179) throw new System.ComponentModel.Win32Exception();
            var create = new CREATE_USN_JOURNAL_DATA { MaximumSize = 32 << 20, AllocationDelta = 8 << 20 };
            if (!DeviceIoControl(volume, FSCTL_CREATE_USN_JOURNAL, &create, sizeof(CREATE_USN_JOURNAL_DATA), null, 0, out _, 0))
                throw new System.ComponentModel.Win32Exception();
            return Query(volume);
        }
        return data;
    }

    public delegate void OnRecord(ulong frn, ulong parent, ReadOnlySpan<char> name, uint attributes);

    // Every file and folder on the volume, in MFT order: about a second per million files.
    public static void Enumerate(SafeFileHandle volume, long highUsn, OnRecord add)
    {
        var med = new MFT_ENUM_DATA_V0 { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = highUsn };
        var buffer = GC.AllocateUninitializedArray<byte>(1 << 20);
        fixed (byte* p = buffer)
            while (true)
            {
                if (!DeviceIoControl(volume, FSCTL_ENUM_USN_DATA, &med, sizeof(MFT_ENUM_DATA_V0), p, buffer.Length, out int got, 0))
                {
                    if (Marshal.GetLastPInvokeError() == Win32.ERROR_HANDLE_EOF) return;
                    throw new System.ComponentModel.Win32Exception();
                }
                med.StartFileReferenceNumber = *(ulong*)p; // first 8 bytes: where the next call starts
                for (int at = 8; at < got;)
                {
                    byte* r = p + at;
                    uint length = *(uint*)r;
                    ushort major = *(ushort*)(r + 4);
                    if (major == 2) ParseV2(r, add);
                    else if (major == 3) ParseV3(r, add);
                    at += (int)length;
                }
            }
    }

    // USN_RECORD_V2: RecordLength 0, Major 4, Minor 6, FileReferenceNumber 8, ParentFileReferenceNumber 16,
    // Usn 24, TimeStamp 32, Reason 40, SourceInfo 44, SecurityId 48, FileAttributes 52,
    // FileNameLength 56 (bytes), FileNameOffset 58, FileName 60.
    static void ParseV2(byte* r, OnRecord add) =>
        add(*(ulong*)(r + 8), *(ulong*)(r + 16), new ReadOnlySpan<char>(r + *(ushort*)(r + 58), *(ushort*)(r + 56) / 2), *(uint*)(r + 52));

    // USN_RECORD_V3 (ReFS, so a Dev Drive): 128-bit ids. FileReferenceNumber 8 (16 bytes), Parent 24,
    // Usn 40, TimeStamp 48, Reason 56, SourceInfo 60, SecurityId 64, FileAttributes 68,
    // FileNameLength 72, FileNameOffset 74. (check) whether ReFS supports FSCTL_ENUM_USN_DATA at all;
    // if not, a ReFS volume is indexed by a directory walk once and then kept up from the journal.
    // The low 64 bits are used as the key (check: unique on ReFS in practice).
    static void ParseV3(byte* r, OnRecord add) =>
        add(*(ulong*)(r + 8), *(ulong*)(r + 24), new ReadOnlySpan<char>(r + *(ushort*)(r + 74), *(ushort*)(r + 72) / 2), *(uint*)(r + 68));

    public enum Change { Created, Deleted, RenamedFrom, RenamedTo }

    // Blocks until there's news (BytesToWaitFor = 1), then reports it. Returns the next USN to read
    // from; persist it with the journal id. If start < journal.FirstUsn the journal wrapped while we
    // were off: rescan from scratch.
    public static long Follow(SafeFileHandle volume, ulong journalId, long start, Action<Change, ulong, ulong, string, uint> on)
    {
        var read = new READ_USN_JOURNAL_DATA_V0
        {
            StartUsn = start, UsnJournalID = journalId, BytesToWaitFor = 1, Timeout = 0, ReturnOnlyOnClose = 0,
            ReasonMask = USN_REASON_FILE_CREATE | USN_REASON_FILE_DELETE | USN_REASON_RENAME_OLD_NAME | USN_REASON_RENAME_NEW_NAME,
        };
        var buffer = new byte[64 * 1024];
        fixed (byte* p = buffer)
        {
            if (!DeviceIoControl(volume, FSCTL_READ_USN_JOURNAL, &read, sizeof(READ_USN_JOURNAL_DATA_V0), p, buffer.Length, out int got, 0))
                throw new System.ComponentModel.Win32Exception();
            for (int at = 8; at < got;)
            {
                byte* r = p + at;
                if (*(ushort*)(r + 4) == 2)
                {
                    uint reason = *(uint*)(r + 40);
                    var name = new string((char*)(r + *(ushort*)(r + 58)), 0, *(ushort*)(r + 56) / 2);
                    ulong frn = *(ulong*)(r + 8), parent = *(ulong*)(r + 16);
                    uint attrs = *(uint*)(r + 52);
                    if ((reason & USN_REASON_FILE_DELETE) != 0) on(Change.Deleted, frn, parent, name, attrs);
                    else if ((reason & USN_REASON_RENAME_OLD_NAME) != 0) on(Change.RenamedFrom, frn, parent, name, attrs);
                    else if ((reason & USN_REASON_RENAME_NEW_NAME) != 0) on(Change.RenamedTo, frn, parent, name, attrs);
                    else if ((reason & USN_REASON_FILE_CREATE) != 0) on(Change.Created, frn, parent, name, attrs);
                }
                at += (int)*(uint*)r;
            }
            return *(long*)p;
        }
    }
}
```

The index (memory budget: ~50 MB per million names):

```csharp
// plugins/disk-index/src/NameIndex.cs. Names stored once as UTF-16 in one big char[] (offset, length);
// frn → slot through a Dictionary; parents as slots, so a path is a walk up to the root.
sealed class NameIndex
{
    readonly Dictionary<ulong, int> slotOf = new(1 << 20);
    readonly List<ulong> frns = new(1 << 20);
    readonly List<int> parentSlot = new(1 << 20);   // -1 = unknown yet (parents can come after children)
    readonly List<ulong> parentFrn = new(1 << 20);
    readonly List<(int Offset, int Length)> names = new(1 << 20);
    readonly List<uint> attributes = new(1 << 20);
    char[] pool = new char[16 << 20];
    int poolUsed;
    public ulong RootFrn;   // from GetFileInformationByHandle on "C:\" (FILE_FLAG_BACKUP_SEMANTICS):
                            // ((ulong)nFileIndexHigh << 32) | nFileIndexLow

    public void Add(ulong frn, ulong parent, ReadOnlySpan<char> name, uint attrs) { /* append; grow pool ×2 */ }
    public void Remove(ulong frn) { /* tombstone: Length = 0; compact on save */ }
    public void Link() { /* after the scan: parentSlot[i] = slotOf[parentFrn[i]] */ }
    public string PathOf(int slot, char drive) { /* walk parentSlot to RootFrn, join with '\' */ return ""; }

    // Substring match, case-insensitive (ordinal), over the pool in parallel chunks; the finder's
    // fuzzy scorer ranks the first few thousand hits. Excluded: other accounts' profile folders
    // (C:\Users\<other>), so one account can't list another's file names through the service.
    public List<int> Search(string query, int max) => [];

    public void Save(string file, ulong journalId, long nextUsn) { /* binary: header, arrays, pool */ }
    public static NameIndex? Load(string file, out ulong journalId, out long nextUsn) { journalId = 0; nextUsn = 0; return null; }
}
```

Service host without `ServiceBase` (whose AOT status is **(check)**): the raw SCM entry points, AOT-safe.

```csharp
// plugins/disk-index/src/Service.cs
static unsafe partial class Service
{
    [StructLayout(LayoutKind.Sequential)] struct SERVICE_TABLE_ENTRYW { public char* Name; public delegate* unmanaged<uint, char**, void> Main; }
    [StructLayout(LayoutKind.Sequential)] struct SERVICE_STATUS { public uint Type, CurrentState, ControlsAccepted, Win32ExitCode, SpecificExitCode, CheckPoint, WaitHint; }

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static partial bool StartServiceCtrlDispatcherW(SERVICE_TABLE_ENTRYW* table);
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    static partial IntPtr RegisterServiceCtrlHandlerExW(string name, delegate* unmanaged<uint, uint, IntPtr, IntPtr, uint> handler, IntPtr context);
    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static partial bool SetServiceStatus(IntPtr handle, SERVICE_STATUS* status);

    const uint SERVICE_WIN32_OWN_PROCESS = 0x10, SERVICE_RUNNING = 4, SERVICE_STOPPED = 1, SERVICE_STOP_PENDING = 3;
    const uint SERVICE_ACCEPT_STOP = 1, SERVICE_ACCEPT_SHUTDOWN = 4, SERVICE_CONTROL_STOP = 1, SERVICE_CONTROL_SHUTDOWN = 5;
    static IntPtr handle;
    static readonly ManualResetEventSlim stop = new();

    public static void Run()
    {
        fixed (char* name = "omawin-disk-index")
        {
            var table = stackalloc SERVICE_TABLE_ENTRYW[2];
            table[0] = new SERVICE_TABLE_ENTRYW { Name = name, Main = &Main };
            table[1] = default;
            StartServiceCtrlDispatcherW(table); // returns when the service stops
        }
    }

    [UnmanagedCallersOnly]
    static void Main(uint argc, char** argv)
    {
        handle = RegisterServiceCtrlHandlerExW("omawin-disk-index", &Control, 0);
        Report(SERVICE_RUNNING);
        new Thread(Indexer.Run) { IsBackground = true }.Start();   // per fixed NTFS volume: load or scan, Follow, Save hourly
        new Thread(Pipe.Serve) { IsBackground = true }.Start();
        stop.Wait();
        Indexer.SaveAll();
        Report(SERVICE_STOPPED);
    }

    [UnmanagedCallersOnly]
    static uint Control(uint control, uint eventType, IntPtr data, IntPtr context)
    {
        if (control is SERVICE_CONTROL_STOP or SERVICE_CONTROL_SHUTDOWN) { Report(SERVICE_STOP_PENDING); stop.Set(); }
        return 0;
    }

    static void Report(uint state)
    {
        var s = new SERVICE_STATUS { Type = SERVICE_WIN32_OWN_PROCESS, CurrentState = state, ControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN : 0 };
        SetServiceStatus(handle, &s);
    }
}
```

Pipe: `\\.\pipe\omawin-disk-index`, JSON lines `{"q":"report","max":200,"drives":"C"}` → `{"paths":[…]}`. Created with `NamedPipeServerStreamAcl.Create(…, PipeSecurity)` allowing `Authenticated Users` read/write, `SYSTEM` full; no write commands exist on the pipe (it only answers searches). Responses never include other accounts' profile folders.

Admin verb `usn-index-install` (PS 5.1 in the batch): copy the plugin's exe (checked against `SHA256SUMS.txt`) to `%ProgramFiles%\omawin\disk-index\`, then

```powershell
sc.exe create omawin-disk-index binPath= "`"$env:ProgramFiles\omawin\disk-index\omawin-disk-index.exe`" --service" start= delayed-auto obj= LocalSystem DisplayName= "omawin whole-disk search"
sc.exe description omawin-disk-index "Keeps a list of file names for omawin's finder (the Everywhere chip). Names only, never file contents."
sc.exe failure omawin-disk-index reset= 86400 actions= restart/60000
sc.exe start omawin-disk-index
```

`usn-index-remove`: `sc.exe stop`, `sc.exe delete`, remove the folder and `%ProgramData%\omawin\disk-index\` (the saved index). After it's installed, 8.31's catalog entry (indexer off) is offered.

### 8.3 Disk cleanup (`plugins/disk/`, a page)

Sizes with .NET's `FileSystemEnumerable` (one `NtQueryDirectoryFile` stream per folder, no extra stat per file; AOT-safe; reparse points skipped so a junction isn't counted twice):

```csharp
using System.IO.Enumeration;

static class Sizes
{
    static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false, BufferSize = 64 * 1024,
    };

    public static long Of(string folder) =>
        !Directory.Exists(folder) ? 0
        : new FileSystemEnumerable<long>(folder, (ref FileSystemEntry e) => e.Length, Walk) { ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory }.Sum();
}

// Machine-wide caches: (name, folder, the tool's own clean command or null = delete the folder's contents).
static class Caches
{
    static string L(string p) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), p);
    static string H(string p) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p);
    static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static readonly (string Name, Func<string> Folder, string? Clean)[] All =
    [
        ("npm",      () => Env("npm_config_cache") ?? L("npm-cache"),                 "npm cache clean --force"),
        ("pnpm",     () => L(@"pnpm\store"),                                           "pnpm store prune"),      // (check) older pnpm: ~/.pnpm-store
        ("Yarn",     () => Env("YARN_CACHE_FOLDER") ?? L(@"Yarn\Cache"),               "yarn cache clean"),
        ("Bun",      () => Env("BUN_INSTALL_CACHE_DIR") ?? H(@".bun\install\cache"),   "bun pm cache rm"),
        ("NuGet",    () => Env("NUGET_PACKAGES") ?? H(@".nuget\packages"),             "dotnet nuget locals all --clear"),
        ("NuGet HTTP", () => L(@"NuGet\v3-cache"),                                     "dotnet nuget locals http-cache --clear"),
        ("pip",      () => Env("PIP_CACHE_DIR") ?? L(@"pip\Cache"),                    "pip cache purge"),
        ("uv",       () => Env("UV_CACHE_DIR") ?? L(@"uv\cache"),                      "uv cache clean"),
        ("cargo registry", () => Path.Combine(Env("CARGO_HOME") ?? H(".cargo"), "registry"), null),        // re-downloaded on next build
        ("cargo git",      () => Path.Combine(Env("CARGO_HOME") ?? H(".cargo"), "git"), null),
        ("Go build", () => Env("GOCACHE") ?? L("go-build"),                            "go clean -cache"),
        ("Go modules", () => Env("GOMODCACHE") ?? H(@"go\pkg\mod"),                    "go clean -modcache"),
        ("Gradle",   () => Path.Combine(Env("GRADLE_USER_HOME") ?? H(".gradle"), "caches"), null),
        ("Maven",    () => H(@".m2\repository"),                                       null),
        ("mise downloads", () => L(@"mise\cache"),                                     "mise cache clear"),     // (check) path
        ("Temp",     () => Path.GetTempPath(),                                         null),                   // files older than 7 days only
    ];
}

// Project-local build output, found by walking the project roots (Projects.Find's roots) with a
// marker beside it, so a folder that merely has the name is never touched.
static class ProjectJunk
{
    public static readonly (string Folder, string Marker)[] Kinds =
    [
        ("node_modules", "package.json"),
        ("target", "Cargo.toml"),
        (".venv", @".venv\pyvenv.cfg"),   // marker inside
        ("bin", "*.csproj"), ("obj", "*.csproj"),
        (".next", "next.config.*"), (".turbo", "turbo.json"),
        (".gradle", "settings.gradle*"), ("build", "build.gradle*"),
        ("zig-cache", "build.zig"), (".zig-cache", "build.zig"),
    ];
    // Walk: don't descend into a match (node_modules inside node_modules), skip .git and reparse
    // points, stop at depth 6. Show per project: kind, size, last write of the project's newest
    // source file ("untouched for 4 months"): the default selection is projects untouched > 30 days.
}
```

Never deleted without the row being picked; deletion goes through `FileSystem.DeleteDirectory` to the Recycle Bin only for small folders (< 1 GB), else a plain delete after a confirm that states the size.

**WSL and Docker disks.** The distros' disks are listed in the registry (per user, no admin):

```csharp
// HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss\{guid}: DistributionName, BasePath ("\\?\C:\…"
// on newer WSL), VhdFileName (default ext4.vhdx) (check: VhdFileName present on all versions).
static IEnumerable<(string Name, string Vhd, long OnDisk, bool Sparse)> WslDisks()
{
    using var lxss = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
    if (lxss == null) yield break;
    foreach (var id in lxss.GetSubKeyNames())
    {
        using var d = lxss.OpenSubKey(id);
        if (d?.GetValue("DistributionName") is not string name || d.GetValue("BasePath") is not string basePath) continue;
        var vhd = Path.Combine(basePath.Replace(@"\\?\", ""), d.GetValue("VhdFileName") as string ?? "ext4.vhdx");
        if (!File.Exists(vhd)) continue;
        var info = new FileInfo(vhd);
        yield return (name, vhd, info.Length, info.Attributes.HasFlag(FileAttributes.SparseFile));
    }
}
// Docker Desktop's: %LOCALAPPDATA%\Docker\wsl\disk\docker_data.vhdx (newer) or …\wsl\data\ext4.vhdx (older) (check).
```

Actions (shown with the sizes before and after):

```powershell
# Sparse: the VHD gives freed space back by itself from then on. No admin. The distro must be stopped.
wsl --terminate <distro>
wsl --manage <distro> --set-sparse true          # (check) newer WSL may require --allow-unsafe here
# For new distros: %USERPROFILE%\.wslconfig
#   [experimental]
#   sparseVhd=true

# One-off compaction (admin verb `wsl-compact <vhdx>`), after trimming inside Linux:
wsl -d <distro> -u root fstrim -av
wsl --shutdown
# in the admin batch (diskpart needs admin; Optimize-VHD needs Hyper-V's module, absent on Home):
@"
select vdisk file="$vhd"
attach vdisk readonly
compact vdisk
detach vdisk
"@ | Set-Content $tmp -Encoding ascii; diskpart /s $tmp

# Docker: its own commands, never the file
docker system df
docker builder prune --all --force
docker system prune --force          # (asked: removes stopped containers and dangling images)
```

### 8.4 Quick Look in Explorer (core: the keyboard hook + an STA worker)

Explorer's selection through its documented automation: `ShellWindows` → each window's `IServiceProvider` → `SID_STopLevelBrowser` → `IShellBrowser` → `QueryActiveShellView` → `IFolderView2::GetSelection` → `IShellItemArray` → paths. `IShellWindows` is a dual (IDispatch) interface; called by vtable. All calls go to Explorer's process (a few ms each), so they run on the worker, never in the hook.

```csharp
// menu-app/QuickLook.cs
using System.Runtime.InteropServices;
using Omawin.Windows;

static unsafe partial class ExplorerSelection
{
    static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static readonly Guid IID_IShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    static readonly Guid IID_IServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    static readonly Guid IID_IFolderView2 = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    const uint SIGDN_FILESYSPATH = 0x80058000;

    // VARIANT: 24 bytes on 64-bit. Passed by value; the ABI (by hidden reference on x64) is the runtime's job.
    [StructLayout(LayoutKind.Sequential)] struct VARIANT { public ushort vt; ushort r1, r2, r3; public nint a, b; }

    [LibraryImport("user32.dll")] private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags); // GA_ROOT = 2
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(IntPtr hwnd);
    [LibraryImport("ole32.dll")] private static partial void CoTaskMemFree(IntPtr p);

    // The selected files of the Explorer window (and, on Windows 11, its visible tab) whose
    // top-level window is `foreground`. Empty when nothing is selected. Call on an STA thread.
    public static List<string> Of(IntPtr foreground)
    {
        var paths = new List<string>();
        IntPtr windows = Com.Create(CLSID_ShellWindows, IID_IShellWindows, Com.CLSCTX_ALL);
        try
        {
            int count;
            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Com.Slot(windows, 7))(windows, &count)); // get_Count
            for (int i = 0; i < count; i++)
            {
                IntPtr item = 0, sp = 0, browser = 0, view = 0, folderView = 0, array = 0;
                try
                {
                    var index = new VARIANT { vt = 3 /* VT_I4 */, a = i };
                    if (((delegate* unmanaged[Stdcall]<IntPtr, VARIANT, IntPtr*, int>)Com.Slot(windows, 8))(windows, index, &item) != 0 || item == 0) continue; // Item
                    sp = Com.Query(item, IID_IServiceProvider);
                    var sid = SID_STopLevelBrowser; var iid = IID_IShellBrowser;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Com.Slot(sp, 3))(sp, &sid, &iid, &browser) != 0) continue; // QueryService

                    // IShellBrowser : IOleWindow. GetWindow (slot 3) is the tab's own window on
                    // Windows 11 (ShellTabWindowClass) (check); the visible one under the foreground
                    // top-level window is the tab you see.
                    IntPtr tab;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Com.Slot(browser, 3))(browser, &tab) != 0) continue;
                    if (GetAncestor(tab, 2) != foreground || !IsWindowVisible(tab)) continue;

                    if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Com.Slot(browser, 15))(browser, &view) != 0 || view == 0) continue; // QueryActiveShellView
                    folderView = Com.Query(view, IID_IFolderView2);
                    // IFolderView2::GetSelection(BOOL fNoneImpliesFolder, IShellItemArray**): slot 32 (check against shobjidl_core.h).
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Com.Slot(folderView, 32))(folderView, 0, &array) != 0 || array == 0) break;
                    uint n;
                    Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Com.Slot(array, 7))(array, &n)); // IShellItemArray::GetCount
                    for (uint k = 0; k < n; k++)
                    {
                        IntPtr shellItem;
                        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Slot(array, 8))(array, k, &shellItem) != 0) continue; // GetItemAt
                        try
                        {
                            IntPtr name;
                            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Slot(shellItem, 5))(shellItem, SIGDN_FILESYSPATH, &name) == 0) // IShellItem::GetDisplayName
                            {
                                paths.Add(Marshal.PtrToStringUni(name)!);
                                CoTaskMemFree(name);
                            }
                        }
                        finally { Com.Release(shellItem); }
                    }
                    break; // found the window
                }
                finally { Com.Release(array); Com.Release(folderView); Com.Release(view); Com.Release(browser); Com.Release(sp); Com.Release(item); }
            }
        }
        finally { Com.Release(windows); }
        return paths;
    }
}
```

The key, in the shared hook (8.12): Space is taken only when it's plainly meant for Quick Look.

```csharp
// A KeyHook handler (section 8.12). Runs inside the hook: no COM, no I/O, just window classes.
sealed class QuickLookKey : IKeyHandler
{
    long lastTypeAhead; // Explorer's type-to-find uses Space inside names ("my file"): leave it alone then

    public bool OnKey(in KeyEvent k)
    {
        if (!k.Down || k.Injected) return false;
        if (k.Vk is >= 0x30 and <= 0x5A) { lastTypeAhead = k.Time; return false; } // a letter or digit
        if (k.Vk != 0x20 /* VK_SPACE */ || k.Modifiers != 0) return false;
        if (k.Time - lastTypeAhead < 1000) return false;
        var fg = Win.GetForegroundWindow();
        if (Win.ClassName(fg) != "CabinetWClass") return false;
        // The focused control of Explorer's UI thread: the item view is a DirectUIHWND; renaming,
        // the address bar and the search box are edits.
        var gui = new Win.GUITHREADINFO { cbSize = (uint)sizeof(Win.GUITHREADINFO) };
        if (!Win.GetGUIThreadInfo(Win.GetWindowThreadProcessId(fg, out _), ref gui) || Win.ClassName(gui.hwndFocus) != "DirectUIHWND") return false;
        QuickLook.Post(fg);  // the STA worker reads the selection; nothing selected → it re-sends Space (tagged, 8.12)
        return true;         // swallowed
    }
}
// GUITHREADINFO: cbSize, flags (uint), hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize,
// hwndCaret (IntPtr), rcCaret (RECT): 72 bytes on 64-bit.
```

Preview: the finder's own preview (6.4) for text, images, code, PDFs it can draw; Office and anything else through the file type's registered **preview handler** in its own child window:

```csharp
static unsafe partial class PreviewHandlers
{
    const string IID_IPreviewHandler = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    static readonly Guid IID_IPreviewHandlerGuid = new("8895B1C6-B41F-4C1C-A562-0D564250836F");
    static readonly Guid IID_IInitializeWithFile = new("B7D14566-0509-4CCE-A71F-0A554233BD9B");
    static readonly Guid IID_IInitializeWithStream = new("B824B49D-22AC-4161-AC8A-9916E8FA3F7F");

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryString(uint flags, uint str, string assoc, string? extra, char* output, ref uint size);
    const uint ASSOCSTR_SHELLEXTENSION = 16;

    public static Guid? HandlerFor(string extension)
    {
        var buffer = stackalloc char[64]; uint size = 64;
        return AssocQueryString(0, ASSOCSTR_SHELLEXTENSION, extension, IID_IPreviewHandler, buffer, ref size) == 0
            && Guid.TryParse(new string(buffer), out var clsid) ? clsid : null;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    // Handlers run out of process (prevhost.exe) when created with CLSCTX_LOCAL_SERVER, so a
    // crashing handler can't take the shell down. IPreviewHandler: SetWindow 3, SetRect 4,
    // DoPreview 5, Unload 6. IInitializeWithFile::Initialize 3 (path, STGM_READ = 0);
    // IInitializeWithStream::Initialize 3 (IStream from SHCreateStreamOnFileEx).
    public static IntPtr Show(Guid clsid, string path, IntPtr host, int width, int height)
    {
        var handler = Com.Create(clsid, IID_IPreviewHandlerGuid, Com.CLSCTX_LOCAL_SERVER);
        IntPtr init;
        try { init = Com.Query(handler, IID_IInitializeWithFile); }
        catch { init = 0; }
        if (init != 0)
            fixed (char* p = path) Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, uint, int>)Com.Slot(init, 3))(init, p, 0));
        else { /* IInitializeWithStream with SHCreateStreamOnFileEx(path, STGM_READ|STGM_SHARE_DENY_NONE …) */ }
        Com.Release(init);
        var rect = new RECT { Right = width, Bottom = height };
        Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, RECT*, int>)Com.Slot(handler, 3))(handler, host, &rect));
        Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.Slot(handler, 5))(handler));
        return handler; // Unload (slot 6) and Release when the preview closes or moves on
    }
}
```

The Quick Look window: a borderless WinUI window centred on Explorer's monitor; Space or Esc closes, ←/→ step through the selection, Enter opens with the default app. `host` is a plain Win32 child window inside it (preview handlers draw into an HWND).

## 8B. Windows and the desktop

### 8.5 Window switcher (core; `omawin switch`, no key until one is confirmed)

Data from GlazeWM (it knows workspaces; `EnumWindows` doesn't), order from a focus history kept from its events, live pictures from DWM.

```csharp
// menu-app/Bar/GlazeWm.cs additions: a query that awaits its own reply. Replies carry
// "clientMessage" (the text sent), which Handle() already reads.
readonly Dictionary<string, TaskCompletionSource<JsonElement>> pending = [];
public async Task<JsonElement?> QueryAsync(string query, int timeoutMs = 500)
{
    var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (pending) pending[query] = done;
    await SendAsync(query);
    var first = await Task.WhenAny(done.Task, Task.Delay(timeoutMs));
    lock (pending) pending.Remove(query);
    return first == done.Task ? done.Task.Result : null;
}
// In Handle(): if (pending.Remove(asked, out var waiter)) waiter.TrySetResult(root.GetProperty("data").Clone());
// Subscribe to focus_changed too (it is) and keep the history:
readonly LinkedList<string> focusHistory = new(); // container ids, most recent first, max 200
// on focus_changed: id = data.focusedContainer.id (check: field names in GlazeWM 3's event payload)
```

```csharp
// menu-app/Switcher.cs
public sealed record WindowRow(string Id, IntPtr Hwnd, string Title, string Process, string Workspace, bool Focused);

static class Switcher
{
    // `query workspaces` gives the tree: workspace { name, children: [ split | window … ] }.
    // A window: { type: "window", id, handle, title, processName, hasFocus, state: { type } } (check: names).
    public static async Task<List<WindowRow>> Windows(GlazeWm wm)
    {
        var rows = new List<WindowRow>();
        if (await wm.QueryAsync("query workspaces") is not { } data) return rows;
        foreach (var ws in data.GetProperty("workspaces").EnumerateArray())
            Collect(ws, ws.GetProperty("name").GetString() ?? "", rows);
        // Most recently focused first; never-focused after, in tree order.
        var rank = wm.FocusHistory.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return [.. rows.OrderBy(r => rank.TryGetValue(r.Id, out var i) ? i : int.MaxValue)];
    }

    static void Collect(JsonElement node, string workspace, List<WindowRow> rows)
    {
        if (node.TryGetProperty("type", out var t) && t.GetString() == "window")
            rows.Add(new WindowRow(node.GetProperty("id").GetString()!, (IntPtr)node.GetProperty("handle").GetInt64(),
                node.GetProperty("title").GetString() ?? "", node.GetProperty("processName").GetString() ?? "",
                workspace, node.TryGetProperty("hasFocus", out var f) && f.GetBoolean()));
        if (node.TryGetProperty("children", out var children))
            foreach (var c in children.EnumerateArray()) Collect(c, workspace, rows);
    }

    // Typed text filters by the menu's own fuzzy scorer over "title · process · workspace".
    // Enter: GlazeWM focuses it (switching workspace if needed); fallback SetForegroundWindow
    // (the menu app is foreground while open, so it may hand focus on).
    public static void Focus(GlazeWm wm, WindowRow row) => wm.Command($"focus --container-id {row.Id}"); // (check) flag name
}
```

Live thumbnails over the WinUI window (DWM draws them above the XAML content; positions in physical pixels):

```csharp
// shared/Omawin.Windows/Thumbnails.cs
public sealed partial class Thumbnail : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct DWM_THUMBNAIL_PROPERTIES { public uint dwFlags; public RECT rcDestination, rcSource; public byte opacity; public int fVisible, fSourceClientAreaOnly; } // 48

    const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [LibraryImport("dwmapi.dll")] private static partial int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUnregisterThumbnail(IntPtr thumbnail);
    [LibraryImport("dwmapi.dll")] private static partial int DwmUpdateThumbnailProperties(IntPtr thumbnail, in DWM_THUMBNAIL_PROPERTIES properties);
    [LibraryImport("dwmapi.dll")] private static partial int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);

    IntPtr handle;

    // destination: a top-level window of this process (the switcher, the PiP window).
    public Thumbnail(IntPtr destination, IntPtr source) => Marshal.ThrowExceptionForHR(DwmRegisterThumbnail(destination, source, out handle));

    public (int Width, int Height) SourceSize => DwmQueryThumbnailSourceSize(handle, out var s) == 0 ? (s.cx, s.cy) : (0, 0);

    // Fits the source (or a region of it, in the source window's pixels) into the box, keeping its shape.
    public void Show(RECT box, RECT? region = null, byte opacity = 255, bool clientOnly = false)
    {
        var (sw, sh) = region is { } r ? (r.Right - r.Left, r.Bottom - r.Top) : SourceSize;
        if (sw <= 0 || sh <= 0) return;
        double scale = Math.Min((double)(box.Right - box.Left) / sw, (double)(box.Bottom - box.Top) / sh);
        int w = (int)(sw * scale), h = (int)(sh * scale);
        int x = box.Left + (box.Right - box.Left - w) / 2, y = box.Top + (box.Bottom - box.Top - h) / 2;
        var p = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | (region != null ? DWM_TNP_RECTSOURCE : 0),
            rcDestination = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h },
            rcSource = region ?? default, opacity = opacity, fVisible = 1, fSourceClientAreaOnly = clientOnly ? 1 : 0,
        };
        DwmUpdateThumbnailProperties(handle, p);
    }

    public void Dispose() { if (handle != 0) { DwmUnregisterThumbnail(handle); handle = 0; } }
}
```

In the switcher: one `Thumbnail` per visible row (max ~12), boxes from `element.TransformToVisual(root).TransformBounds(...)` × `XamlRoot.RasterizationScale`, re-`Show`n on scroll, all disposed on close. A window on another workspace: GlazeWM hides it; DWM keeps drawing **cloaked** windows (Alt+Tab relies on that) but not `SW_HIDE`-hidden ones (check GlazeWM's hide method; if hidden, show the app icon instead).

### 8.6 Picture-in-picture (`plugins/pip/`, its own small Win32 window)

```csharp
// plugins/pip/src/Pip.cs. `omawin pip` (focused window) or `omawin pip --region` (pick a region with
// the screenshot overlay first; region in the source window's client pixels).
static unsafe partial class Pip
{
    static IntPtr source, window;
    static Thumbnail? thumb;
    static Thumbnail.RECT? region;
    static byte opacity = 255;

    public static void Run(IntPtr target, Thumbnail.RECT? area)
    {
        source = target; region = area;
        fixed (char* cls = "omawin-pip")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = GetModuleHandleW(null), lpszClassName = cls, hCursor = LoadCursorW(0, 32512) };
            RegisterClassExW(&wc);
            // Title "omawin pip": GlazeWM's rule floats it (below). Topmost, no taskbar button.
            window = CreateWindowExW(0x00000008 /* WS_EX_TOPMOST */ | 0x00000080 /* WS_EX_TOOLWINDOW */, cls, "omawin pip",
                0x80000000 /* WS_POPUP */ | 0x00040000 /* WS_THICKFRAME */ | 0x10000000 /* WS_VISIBLE */,
                Corner().X, Corner().Y, 480, 270, 0, 0, wc.hInstance, 0);
        }
        thumb = new Thumbnail(window, source);
        Fit();
        // The source closing ends PiP: an out-of-context WinEvent, no polling.
        GetWindowThreadProcessId(source, out var pid);
        SetWinEventHook(0x8001 /* EVENT_OBJECT_DESTROY */, 0x8001, 0, &OnDestroy, pid, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    }

    static void Fit() { GetClientRect(window, out var rc); thumb?.Show(rc, region, opacity, clientOnly: region != null); }

    [UnmanagedCallersOnly]
    static nint WndProc(IntPtr hwnd, uint msg, nuint w, nint l)
    {
        switch (msg)
        {
            case 0x0005: Fit(); return 0;                                    // WM_SIZE
            case 0x0084:                                                     // WM_NCHITTEST: drag anywhere, resize at the edges
                var hit = DefWindowProcW(hwnd, msg, w, l);
                return hit == 1 /* HTCLIENT */ ? 2 /* HTCAPTION */ : hit;
            case 0x00A3: SetForegroundWindow(source); DestroyWindow(hwnd); return 0;  // WM_NCLBUTTONDBLCLK: go to it, close PiP
            case 0x020A:                                                     // WM_MOUSEWHEEL: opacity
                opacity = (byte)Math.Clamp(opacity + ((short)(w >> 16) > 0 ? 25 : -25), 60, 255); Fit(); return 0;
            case 0x0002: thumb?.Dispose(); PostQuitMessage(0); return 0;     // WM_DESTROY
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }

    [UnmanagedCallersOnly]
    static void OnDestroy(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    { if (hwnd == source && idObject == 0) PostMessageW(window, 0x0010 /* WM_CLOSE */, 0, 0); }

    // P/Invokes (all user32 unless noted): RegisterClassExW, CreateWindowExW (StringMarshalling.Utf16 or char*),
    // DefWindowProcW, GetMessageW, TranslateMessage, DispatchMessageW, PostQuitMessage, DestroyWindow,
    // GetClientRect, SetForegroundWindow, PostMessageW, LoadCursorW, SetWinEventHook,
    // GetWindowThreadProcessId; kernel32 GetModuleHandleW. WNDCLASSEXW and MSG as blittable structs
    // with char* / function-pointer fields. Corner(): the work area's bottom-right of the source's monitor.
}
```

`glazewm/config.yaml` (the template), so GlazeWM floats it instead of tiling it (check: v3 rule syntax and the `--shown-on-top` flag):

```yaml
window_rules:
  - commands: ['set-floating --shown-on-top true']
    match:
      - window_process: { equals: 'omawin-pip' }
```

### 8.7 Monitor profiles (core: `shared/Omawin.Windows/DisplayLayouts.cs`, applied from the bar's monitor watch)

`QueryDisplayConfig` / `SetDisplayConfig` / `DisplayConfigGetDeviceInfo` are documented (`wingdi.h`, user32). A profile is the raw path and mode arrays plus each target's EDID identity; adapter LUIDs change between boots, so they're patched on restore.

```csharp
public static unsafe partial class DisplayLayouts
{
    const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    const uint SDC_APPLY = 0x80, SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20, SDC_SAVE_TO_DATABASE = 0x200, SDC_ALLOW_CHANGES = 0x400, SDC_VALIDATE = 0x40;
    const int PathSize = 72, ModeSize = 64;
    const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }

    // DISPLAYCONFIG_PATH_INFO (72): source { adapterId 0, id 8, modeInfoIdx 12, statusFlags 16 },
    // target { adapterId 20, id 28, modeInfoIdx 32, outputTechnology 36, rotation 40, scaling 44,
    // refreshRate 48 (num, den), scanLineOrdering 56, targetAvailable 60, statusFlags 64 }, flags 68.
    // DISPLAYCONFIG_MODE_INFO (64): infoType 0 (1 source, 2 target, 3 desktop image), id 4, adapterId 8, union 16.
    // Source mode (infoType 1) union: width 16, height 20, pixelFormat 24, position.x 28, position.y 32.

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_TARGET_DEVICE_NAME // 420
    {
        public int type; public uint size; public LUID adapterId; public uint id;   // header (20)
        public uint flags, outputTechnology;
        public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        public fixed char monitorFriendlyDeviceName[64];
        public fixed char monitorDevicePath[128];
    }

    [LibraryImport("user32.dll")] private static partial int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [LibraryImport("user32.dll")] private static partial int QueryDisplayConfig(uint flags, ref uint numPaths, byte* paths, ref uint numModes, byte* modes, IntPtr topology);
    [LibraryImport("user32.dll")] private static partial int SetDisplayConfig(uint numPaths, byte* paths, uint numModes, byte* modes, uint flags);
    [LibraryImport("user32.dll")] private static partial int DisplayConfigGetDeviceInfo(void* packet);

    public sealed record Target(uint AdapterLow, int AdapterHigh, uint Id, ushort Maker, ushort Product, uint Connector, string Name);
    public sealed record Layout(string Key, byte[] Paths, byte[] Modes, Target[] Targets);

    public static Layout Current()
    {
        int err;
        byte[] paths, modes;
        do
        {
            GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm);
            paths = new byte[np * PathSize]; modes = new byte[nm * ModeSize];
            fixed (byte* p = paths) fixed (byte* m = modes) err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, p, ref nm, m, 0);
            if (err == 0) { Array.Resize(ref paths, (int)np * PathSize); Array.Resize(ref modes, (int)nm * ModeSize); }
        } while (err == Win32.ERROR_INSUFFICIENT_BUFFER); // a monitor came or went between the two calls
        if (err != 0) throw new System.ComponentModel.Win32Exception(err);

        var targets = new List<Target>();
        fixed (byte* p = paths)
            for (int i = 0; i < paths.Length / PathSize; i++)
            {
                byte* path = p + i * PathSize;
                var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME { type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), adapterId = *(LUID*)(path + 20), id = *(uint*)(path + 28) };
                if (DisplayConfigGetDeviceInfo(&name) != 0) continue;
                targets.Add(new Target(name.adapterId.Low, name.adapterId.High, name.id, name.edidManufactureId, name.edidProductCodeId, name.connectorInstance, new string(name.monitorFriendlyDeviceName)));
            }
        return new Layout(KeyOf(targets), paths, modes, [.. targets]);
    }

    // The set of monitors, whatever port they're on: maker + product (+ an index for two identical ones).
    static string KeyOf(IEnumerable<Target> targets) =>
        string.Join("+", targets.Select(t => $"{t.Maker:x4}{t.Product:x4}").Order().GroupBy(x => x).SelectMany(g => g.Select((x, i) => $"{x}.{i}")));

    // Puts a saved layout back on the monitors plugged in now. False when they don't match or
    // Windows refuses it (then nothing changed: validated first).
    public static bool Apply(Layout saved)
    {
        var now = Current();
        if (now.Key != saved.Key) return false;
        var paths = (byte[])saved.Paths.Clone();
        var modes = (byte[])saved.Modes.Clone();
        // Old adapter LUID → today's, matched through the monitors (maker, product, nth of its kind).
        var map = new Dictionary<(uint, int), LUID>();
        foreach (var t in saved.Targets)
        {
            var match = now.Targets.FirstOrDefault(n => n.Maker == t.Maker && n.Product == t.Product && n.Connector == t.Connector)
                     ?? now.Targets.FirstOrDefault(n => n.Maker == t.Maker && n.Product == t.Product);
            if (match == null) return false;
            map[(t.AdapterLow, t.AdapterHigh)] = new LUID { Low = match.AdapterLow, High = match.AdapterHigh };
        }
        fixed (byte* p = paths) fixed (byte* m = modes)
        {
            for (int i = 0; i < paths.Length / PathSize; i++)
                foreach (int at in new[] { 0, 20 })                              // source and target adapter ids
                {
                    var luid = (LUID*)(p + i * PathSize + at);
                    if (map.TryGetValue((luid->Low, luid->High), out var to)) *luid = to;
                }
            for (int i = 0; i < modes.Length / ModeSize; i++)
            {
                var luid = (LUID*)(m + i * ModeSize + 8);
                if (map.TryGetValue((luid->Low, luid->High), out var to)) *luid = to;
            }
            uint np = (uint)(paths.Length / PathSize), nm = (uint)(modes.Length / ModeSize);
            const uint flags = SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
            if (SetDisplayConfig(np, p, nm, m, flags | SDC_VALIDATE) != 0) return false;
            return SetDisplayConfig(np, p, nm, m, flags | SDC_APPLY | SDC_SAVE_TO_DATABASE) == 0;
        }
    }
}
```

Not covered by `SetDisplayConfig`: **scaling (DPI)** per monitor, which only has undocumented `DisplayConfigSetDeviceInfo` packet types (-3 get, -4 set, used by several tools) **(check; undocumented: behind a `minBuild` and skipped when it fails)**. Input source and contrast over **DDC/CI**: `SetVCPFeature(physicalMonitor, 0x60 /* input select */, value)` and `0x12` (contrast), with the same physical-monitor handles `QuickSettings.cs`'s `Brightness` already opens.

Flow (core, the bar's monitor watch, `Bar/Bar.cs`): on `WM_DISPLAYCHANGE` wait 2 s for the dock to settle, take `Current().Key`; if a profile exists for that key and differs from now, apply it and say so in a note ("Desk layout"); if none exists, save the current one 30 s after the last change the user makes (so the layout they arrange becomes the profile). Profiles in `%LOCALAPPDATA%\launcher\display-profiles\<key>.json` (base64 arrays + targets + name). `omawin display save|apply|list`.

### 8.8 Wallpaper per monitor (core, themes)

```csharp
// menu-app/Wallpaper.cs: IDesktopWallpaper (shobjidl_core.h), documented since Windows 8.
static partial class Wallpaper
{
    static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    static readonly Guid IID_IDesktopWallpaper = new("B92B56A9-8B55-4E14-9A89-0199BBB6F93B");

    internal enum Position { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    internal partial interface IDesktopWallpaper
    {
        void SetWallpaper(string? monitorId, string wallpaper);
        void GetWallpaper(string? monitorId, out string wallpaper);
        void GetMonitorDevicePathAt(uint index, out string monitorId);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT(string monitorId, out Thumbnail.RECT rect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(Position position);
        void GetPosition(out Position position);
        void SetSlideshow(IntPtr items);           // IShellItemArray
        void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(uint options, uint tickMs);
        void GetSlideshowOptions(out uint options, out uint tickMs);
        void AdvanceSlideshow(string? monitorId, int direction);
        void GetStatus(out uint state);
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    // The theme's backgrounds spread over the monitors, left to right: monitor i gets picture
    // (start + i) % count; Win+Ctrl+Space (theme next-background) moves `start` on by the number
    // of monitors. Called on the STA thread that owns theme switching.
    public static void Spread(IReadOnlyList<string> pictures, int start)
    {
        var wp = Omawin.Windows.Com.Wrap<IDesktopWallpaper>(Omawin.Windows.Com.Create(CLSID_DesktopWallpaper, IID_IDesktopWallpaper));
        wp.GetMonitorDevicePathCount(out uint n);
        var monitors = new List<(string Id, int Left)>();
        for (uint i = 0; i < n; i++)
        {
            wp.GetMonitorDevicePathAt(i, out var id);
            try { wp.GetMonitorRECT(id, out var r); monitors.Add((id, r.Left)); } catch { } // a detached monitor's path: skip
        }
        wp.SetPosition(Position.Fill);
        int k = 0;
        foreach (var m in monitors.OrderBy(m => m.Left))
            wp.SetWallpaper(m.Id, pictures[(start + k++) % pictures.Count]);
    }
}
```

Today's theme switch sets one wallpaper for all monitors (`set-theme.ps1`); it calls `omawin wallpaper spread` (an `app` request) instead, falling back to its current method when the menu app isn't running. Theme backgrounds live in `%LOCALAPPDATA%\launcher\backgrounds` ([themes.md](../themes.md)).

### 8.9 Keyboard lights (`plugins/lights/`, a service; off until turned on)

```csharp
// plugins/lights/src/Lights.cs. Windows.Devices.Lights.LampArray (Dynamic Lighting), through CsWinRT's
// AOT-safe projection (the menu app already uses Windows.Devices.Radios the same way).
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.UI;

sealed class Lights
{
    readonly Dictionary<string, LampArray> arrays = [];
    Color accent;

    public void Start(Color themeAccent)
    {
        accent = themeAccent;
        var watcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
        watcher.Added += async (_, info) =>
        {
            var lamps = await LampArray.FromIdAsync(info.Id);
            if (lamps == null) return;
            lock (arrays) arrays[info.Id] = lamps;
            // IsAvailable / AvailabilityChanged: false while another app (or Windows' own effects)
            // controls the device (check: added in Windows 11 22H2, build 22621).
            lamps.AvailabilityChanged += (a, _) => Paint(a);
            Paint(lamps);
        };
        watcher.Removed += (_, update) => { lock (arrays) arrays.Remove(update.Id); };
        watcher.Start();
    }

    // On a theme switch (the SDK's "settings"/theme event): every lamp in the accent.
    public void Theme(Color newAccent) { accent = newAccent; lock (arrays) foreach (var a in arrays.Values) Paint(a); }

    void Paint(LampArray a)
    {
        if (!a.IsAvailable) return;
        a.BrightnessLevel = 1.0;
        a.SetColor(accent);
        // Keyboards: dim everything, then the keys GlazeWM binds in the theme's foreground?
        // a.SetColorsForKeys(colors, Windows.System.VirtualKey[] keys) (later; not on by default).
    }
}
```

Windows only lets an app drive the lights while it's in the foreground, unless the user picks it as the **background controller** (Settings › Personalization › Dynamic Lighting). Whether an unpackaged exe can appear in that list, or needs package identity (8.22's sparse package), is **(check)**. The accent comes from the theme's palette (`themes/colors/<name>.toml`, `accent = "#rrggbb"`).

## 8C. Sound, input and the clipboard

### 8.10 Per-app volume and output (exists; what's left)

`Audio.cs` already lists sessions (`IAudioSessionManager2::GetSessionEnumerator` → `IAudioSessionControl2` / `ISimpleAudioVolume`), sets each app's level and mute, and routes an app to a speaker through the undocumented `IAudioPolicyConfig` with a fallback. Left: the Audio page re-reads `Apps()` on open and on keys; a new app starting to play while the page is open isn't seen until then. Add `IAudioSessionManager2::RegisterSessionNotification` (slot 6, after `GetSessionEnumerator`) with an `IAudioSessionNotification` (`OnSessionCreated`) implemented as a `[GeneratedComClass]`:

```csharp
// Audio.cs: extend the interface (slots: GetAudioSessionControl 3, GetSimpleAudioVolume 4,
// GetSessionEnumerator 5, RegisterSessionNotification 6, UnregisterSessionNotification 7,
// RegisterDuckNotification 8, UnregisterDuckNotification 9; IAudioSessionManager's two first).
internal partial interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr group, uint flags, out IntPtr control);
    void GetSimpleAudioVolume(IntPtr group, uint flags, out IntPtr volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

[GeneratedComInterface, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
internal partial interface IAudioSessionNotification { void OnSessionCreated(IntPtr newSession); }

[GeneratedComClass]
internal sealed partial class SessionWatcher(Action changed) : IAudioSessionNotification
{
    // Called on a COM worker thread: only post. Don't call back into the session manager here.
    public void OnSessionCreated(IntPtr newSession) => changed();
}
// Docs: call GetSessionEnumerator once before registering, or new sessions aren't reported; keep a
// reference to the manager and the watcher for as long as it's registered; unregister on page close.
```

### 8.11 Microphone mute (core: `omawin mic toggle`, a bar indicator; key confirmed before binding)

```csharp
// Audio.cs additions.
public static bool AllMicsMuted()
{
    bool any = false;
    foreach (var d in List().Where(d => d.Input)) { any = true; if (!d.Muted) return false; }
    return any;
}

// Mutes (or unmutes) every active capture device, not just the default: an app may use any.
public static bool ToggleAllMics()
{
    bool mute = !AllMicsMuted();
    var none = Guid.Empty;
    var devices = Enumerator();
    devices.EnumAudioEndpoints(DataFlow.Capture, ActiveOnly, out var all);
    all.GetCount(out uint n);
    for (uint i = 0; i < n; i++)
        try { all.Item(i, out var d); Volume(d).SetMute(mute, ref none); } catch (Exception ex) { App.Log(ex); }
    micsMuted = mute; // remembered: a microphone plugged in while muted is muted too (OnDeviceAdded below)
    return mute;
}
static bool micsMuted;
```

A microphone plugged in later: `IMMDeviceEnumerator::RegisterEndpointNotificationCallback` (slot 6 of `IMMDeviceEnumerator`; the interface in `Audio.cs` declares only the first three, add `GetDevice`'s neighbours: `RegisterEndpointNotificationCallback` 6, `UnregisterEndpointNotificationCallback` 7) with an `IMMNotificationClient` (`{7991EEC9-7E89-4D85-8390-6C703CEC60C0}`: `OnDeviceStateChanged`, `OnDeviceAdded`, `OnDeviceRemoved`, `OnDefaultDeviceChanged`, `OnPropertyValueChanged`) as a `[GeneratedComClass]`; on a capture device turning active while `micsMuted`, mute it.

"An app is listening": Windows' privacy store records it per app, readable without admin **(undocumented but stable since Windows 10 1903; check)**:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged\<exe path with \ as #>
    LastUsedTimeStart (QWORD FILETIME), LastUsedTimeStop (QWORD; 0 while in use)
HKCU\…\ConsentStore\microphone\<PackageFamilyName>      (Store apps, same values)
```

Watched with `RegNotifyChangeKeyValue(key, true /* subtree */, REG_NOTIFY_CHANGE_LAST_SET, event, true)`: when an app's `LastUsedTimeStop` becomes 0 while `micsMuted`, a toast "Zoom tried to use the microphone (muted)". The bar shows a red mic glyph while `AllMicsMuted()`.

### 8.12 Key remaps (core: the one low-level keyboard hook; catalog for sign-in-screen remaps)

The shell's single `WH_KEYBOARD_LL` hook (8.4, 8.12, 8.13, 8.23 share it), on a thread of its own with a message loop. Handlers run in order; the first that returns true swallows the key. The hook must return within Windows' `LowLevelHooksTimeout` (on Windows 7+ a hook that's repeatedly too slow is removed silently), so handlers only look at the key and post work.

```csharp
// menu-app/KeyHook.cs
public readonly record struct KeyEvent(uint Vk, uint Scan, bool Down, bool Injected, bool Ours, uint Time, KeyMods Modifiers);
[Flags] public enum KeyMods { None = 0, Shift = 1, Ctrl = 2, Alt = 4, Win = 8 }
public interface IKeyHandler { bool OnKey(in KeyEvent k); }

public static unsafe partial class KeyHook
{
    const int WH_KEYBOARD_LL = 13, HC_ACTION = 0;
    const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
    public const nuint OurTag = 0x6F6D6177; // "omaw" in dwExtraInfo: keys omawin sends, which handlers ignore

    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nuint dwExtraInfo; }

    [LibraryImport("user32.dll", SetLastError = true)] private static partial IntPtr SetWindowsHookExW(int id, IntPtr proc, IntPtr module, uint thread);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(IntPtr hook, int code, nint w, nint l);
    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int vk);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr GetModuleHandleW(string? name);

    static IntPtr hook;
    static IKeyHandler[] handlers = [];

    public static void Start(params IKeyHandler[] list)
    {
        handlers = list;
        var t = new Thread(() =>
        {
            hook = SetWindowsHookExW(WH_KEYBOARD_LL, (IntPtr)(delegate* unmanaged<int, nint, nint, nint>)&Proc, GetModuleHandleW(null), 0);
            Win.Loop(() => { }); // GetMessage loop: LL hooks are called on the installing thread's queue
        }) { IsBackground = true, Name = "keys", Priority = ThreadPriority.Highest };
        t.Start();
    }

    [UnmanagedCallersOnly]
    static nint Proc(int code, nint w, nint l)
    {
        if (code == HC_ACTION)
        {
            var k = (KBDLLHOOKSTRUCT*)l;
            var e = new KeyEvent(k->vkCode, k->scanCode, (k->flags & LLKHF_UP) == 0, (k->flags & LLKHF_INJECTED) != 0,
                k->dwExtraInfo == OurTag, k->time, Mods());
            if (!e.Ours)
                foreach (var h in handlers)
                    try { if (h.OnKey(e)) return 1; } catch { /* never let a handler break typing */ }
        }
        return CallNextHookEx(hook, code, w, l);
    }

    static KeyMods Mods() =>
        (GetAsyncKeyState(0x10) < 0 ? KeyMods.Shift : 0) | (GetAsyncKeyState(0x11) < 0 ? KeyMods.Ctrl : 0) |
        (GetAsyncKeyState(0x12) < 0 ? KeyMods.Alt : 0) | (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0 ? KeyMods.Win : 0);
}

// SendInput, tagged so our own hook (and the remapper) let it through.
public static unsafe partial class Keys
{
    [StructLayout(LayoutKind.Explicit, Size = 40)] // INPUT on 64-bit: type, pad, then the 32-byte union
    struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; } // 24
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    [LibraryImport("user32.dll", SetLastError = true)] private static partial uint SendInput(uint count, INPUT* inputs, int size);

    public static bool Vk(ushort vk, bool down, bool extended = false)
    {
        var i = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (extended ? KEYEVENTF_EXTENDEDKEY : 0), dwExtraInfo = KeyHook.OurTag } };
        return SendInput(1, &i, sizeof(INPUT)) == 1;
    }

    public static bool Text(string text)
    {
        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = text[i], dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = KeyHook.OurTag } };
            inputs[2 * i + 1] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = text[i], dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, dwExtraInfo = KeyHook.OurTag } };
        }
        fixed (INPUT* p = inputs) return SendInput((uint)inputs.Length, p, sizeof(INPUT)) == inputs.Length; // fewer: UIPI (an elevated window)
    }
}
```

Live remaps (catalog choices, `builtin remap <key> <to>`; state in the account's `settings.txt`):

```csharp
// Caps Lock as Escape, Control, "Escape when tapped, Control when held", or Hyper.
sealed class CapsRemap(Func<string> mode) : IKeyHandler
{
    const uint VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_LCONTROL = 0xA2, VK_LSHIFT = 0xA0, VK_LMENU = 0xA4, VK_LWIN = 0x5B;
    bool held, usedAsModifier; uint downAt;

    public bool OnKey(in KeyEvent k)
    {
        var m = mode();
        if (m == "caps") return false;
        if (k.Vk != VK_CAPITAL)
        {
            // Another key while Caps is held: in "escape-control", Caps is Control after all.
            if (held && k.Down && m == "escape-control" && !usedAsModifier) { usedAsModifier = true; Keys.Vk((ushort)VK_LCONTROL, true); }
            return false;
        }
        if (k.Down && held) return true;   // auto-repeat
        held = k.Down;
        switch (m)
        {
            case "escape": Keys.Vk((ushort)VK_ESCAPE, k.Down); break;
            case "control": Keys.Vk((ushort)VK_LCONTROL, k.Down); break;
            case "escape-control":
                if (k.Down) { downAt = k.Time; usedAsModifier = false; }
                else if (usedAsModifier) Keys.Vk((ushort)VK_LCONTROL, false);
                else if (k.Time - downAt < 300) { Keys.Vk((ushort)VK_ESCAPE, true); Keys.Vk((ushort)VK_ESCAPE, false); }
                break;
            case "hyper":
                foreach (var vk in new[] { VK_LCONTROL, VK_LSHIFT, VK_LMENU, VK_LWIN }) Keys.Vk((ushort)vk, k.Down);
                if (!k.Down) MaskStart(); // Win released alone would open Start
                break;
        }
        return true;
    }

    // AutoHotkey's trick: an unassigned key (vk 0xE8) between Win down and up stops Start opening.
    public static void MaskStart() { Keys.Vk(0xE8, true); Keys.Vk(0xE8, false); }
}
```

**A limit to settle before shipping Hyper:** keys omawin sends are *injected*, and GlazeWM ignores injected keys (AGENTS.md: "GlazeWM ignores simulated keypresses"), so a Hyper made this way can't trigger GlazeWM's bindings. Either the GlazeWM fork accepts injected keys carrying omawin's tag (`dwExtraInfo == 0x6F6D6177`), or Hyper bindings move into the shell. Write it in `open-questions/` before building Hyper.

Remaps that work at the sign-in screen and in elevated windows: the **`Scancode Map`** value (documented in Microsoft's keyboard scan-code mapper notes), admin and a restart. Builder (used by the shell to make the hex for the `scancode-map` admin verb):

```csharp
// HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout, "Scancode Map" (REG_BINARY):
//   8 zero bytes (version, flags) | count (DWORD, mappings + 1) | mappings (DWORD each: low word =
//   the new scancode, high word = the key's own) | 4 zero bytes. Extended keys: 0xE0 in the high byte.
public static class ScancodeMap
{
    public const ushort Esc = 0x0001, Caps = 0x003A, LCtrl = 0x001D, RCtrl = 0xE01D, LAlt = 0x0038, RAlt = 0xE038,
                        LWin = 0xE05B, RWin = 0xE05C, Apps = 0xE05D, Off = 0x0000;

    public static byte[] Build(IReadOnlyList<(ushort From, ushort To)> maps)
    {
        var b = new byte[16 + 4 * maps.Count];
        BitConverter.TryWriteBytes(b.AsSpan(8), (uint)(maps.Count + 1));
        for (int i = 0; i < maps.Count; i++)
        {
            BitConverter.TryWriteBytes(b.AsSpan(12 + 4 * i), maps[i].To);
            BitConverter.TryWriteBytes(b.AsSpan(14 + 4 * i), maps[i].From);
        }
        return b; // the last 4 bytes stay zero
    }
    // Caps → Esc: 00000000 00000000 02000000 01003A00 00000000
    public static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}
```

```powershell
# admin batch, verb 'scancode-map <hex|remove>' (PS 5.1). Takes effect after a restart.
'scancode-map' {
    $a = Split-Arg $s.Arg
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout'
    if ($a[0] -eq 'remove') { Remove-ItemProperty $key -Name 'Scancode Map' -ErrorAction SilentlyContinue }
    else {
        $bytes = [byte[]]($a[0] -split '(..)' -ne '' | ForEach-Object { [Convert]::ToByte($_, 16) })
        Set-ItemProperty $key -Name 'Scancode Map' -Value $bytes -Type Binary
    }
}
```

### 8.13 Text snippets (core, the same hook)

```csharp
// menu-app/Snippets.cs. Snippets: ~/omawin-account/snippets.txt and the PC's shared folder's, lines
// "<trigger> | <text>" (\n for a new line; {date} {time} {clipboard} expand). Triggers start with ';'.
sealed partial class SnippetKeys(Func<IReadOnlyDictionary<string, string>> snippets) : IKeyHandler
{
    readonly char[] typed = new char[32];
    int length;

    [LibraryImport("user32.dll")] private static partial int ToUnicodeEx(uint vk, uint scan, byte* state, char* buffer, int size, uint flags, IntPtr layout);
    [LibraryImport("user32.dll")] private static partial IntPtr GetKeyboardLayout(uint thread);

    public unsafe bool OnKey(in KeyEvent k)
    {
        if (!k.Down || k.Injected) return false;
        if (k.Vk is 0x08) { if (length > 0) length--; return false; }                          // Backspace
        if (k.Vk is 0x0D or 0x09 or 0x1B or >= 0x21 and <= 0x28 || (k.Modifiers & (KeyMods.Ctrl | KeyMods.Win)) != 0) { length = 0; return false; }
        // The character this key makes in the foreground window's layout. Flag 0x4 (Windows 10 1607+):
        // don't change the keyboard state, so dead keys (^, ¨) still work for the user.
        var state = stackalloc byte[256];
        if ((k.Modifiers & KeyMods.Shift) != 0) state[0x10] = 0x80;
        if ((k.Modifiers & KeyMods.Alt) != 0 && (k.Modifiers & KeyMods.Ctrl) != 0) { state[0x11] = state[0x12] = 0x80; } // AltGr
        if ((Win.GetKeyState(0x14) & 1) != 0) state[0x14] = 0x01;                              // Caps Lock on
        var buffer = stackalloc char[4];
        var thread = Win.GetWindowThreadProcessId(Win.GetForegroundWindow(), out _);
        if (ToUnicodeEx(k.Vk, k.Scan, state, buffer, 4, 0x4, GetKeyboardLayout(thread)) != 1) { return false; }
        if (length == typed.Length) { Array.Copy(typed, 1, typed, 0, length - 1); length--; }
        typed[length++] = buffer[0];

        var tail = new string(typed, 0, length);
        foreach (var (trigger, text) in snippets())
            if (tail.EndsWith(trigger, StringComparison.Ordinal))
            {
                length = 0;
                // The trigger's last key is swallowed, so trigger.Length - 1 characters are on screen.
                Snippets.Post(() =>
                {
                    for (int i = 0; i < trigger.Length - 1; i++) { Keys.Vk(0x08, true); Keys.Vk(0x08, false); }
                    Keys.Text(Snippets.Expand(text));
                });
                return true;
            }
        return false;
    }
}
// Reset `length` on a foreground change (SetWinEventHook EVENT_SYSTEM_FOREGROUND = 0x0003) and on a mouse
// click (no mouse hook: the foreground event plus Enter/arrows covers most cases). Expand(): {date} →
// yyyy-MM-dd, {time} → HH:mm, {clipboard} → ClipboardHistory's newest, "\n" → "\r" sent as Enter.
// An elevated foreground window drops injected input (UIPI): Keys.Text returns false, and the note says
// "snippets don't reach admin windows".
```

### 8.14 Clipboard (core, `Background.cs`)

Already done: the two privacy formats. Changes:

```csharp
// 1. A third format Windows defines: don't send to the cloud clipboard. Honoured by not syncing (omawin
//    doesn't sync), recorded so a future sync would skip it.
uint cloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

// 2. Event-driven instead of a 500 ms poll (AGENTS: react to events): a message-only window on the
//    clipboard thread, AddClipboardFormatListener → WM_CLIPBOARDUPDATE (0x031D).
[LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
private static partial bool AddClipboardFormatListener(IntPtr hwnd);
// CreateWindowExW(0, "STATIC", null, 0, 0, 0, 0, 0, HWND_MESSAGE = (IntPtr)(-3), 0, 0, 0), subclass its
// WndProc (or a registered class) → on 0x031D: the same ReadText()/Save() as today.

// 3. Images: "PNG" (registered format; browsers and most apps put it) as bytes, else CF_DIBV5 (17) /
//    CF_DIB (8) → PNG with System.Drawing (Snip.cs already uses it). Saved as <stamp>.png beside the
//    .txt entries, with a 256 px thumbnail <stamp>.thumb.png that the page shows (8K item 7: the full
//    image is read from disk only on paste).
// 4. Pins: clipboard\pinned\ (not trimmed by Keep), Ctrl+P on a row.
```

Transforms on paste (Ctrl+K on a clipboard row → choose; the result is pasted, the original kept):

```csharp
static class ClipTransforms
{
    public static readonly (string Name, Func<string, string?> Apply)[] All =
    [
        ("Plain text", s => s),                                    // pasting CF_UNICODETEXT alone already drops formatting
        ("JSON, pretty", Pretty),
        ("JSON, one line", s => Json(s, indented: false)),
        ("Base64 encode", s => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))),
        ("Base64 decode", s => { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim())); } catch { return null; } }),
        ("URL encode", Uri.EscapeDataString),
        ("URL decode", Uri.UnescapeDataString),
        ("Timestamp → date", FromTimestamp),
        ("Trim lines", s => string.Join('\n', s.Split('\n').Select(l => l.TrimEnd()))),
        ("lower case", s => s.ToLowerInvariant()), ("UPPER CASE", s => s.ToUpperInvariant()),
    ];

    static string? Pretty(string s) => Json(s, indented: true);

    // JsonDocument + Utf8JsonWriter: no serializer, nothing for the trimmer to lose.
    static string? Json(string s, bool indented)
    {
        try
        {
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                doc.WriteTo(w);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return null; }
    }

    // 10 digits: seconds; 13: milliseconds. Shown in local time with the offset.
    static string? FromTimestamp(string s)
    {
        if (!long.TryParse(s.Trim(), out var n)) return null;
        var t = s.Trim().Length >= 13 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
        return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    }
}
```

## 8D. What's going on with my PC

### 8.15 Why it restarted, crashed or woke (`plugins/health/`, a page; shared event-log reader)

`System.Diagnostics.Eventing.Reader.EventLogReader` lives in the `System.Diagnostics.EventLog` package; its AOT/trim status is **(check)**. The Windows Event Log API (`wevtapi.dll`, documented) is a handful of calls, so the shared library uses it directly: query by XPath, render each event as XML, read `EventData`.

```csharp
// shared/Omawin.Windows/EventLog.cs
using System.Xml.Linq;

public sealed record LogEvent(string Provider, int Id, DateTime Time, IReadOnlyDictionary<string, string> Data, IReadOnlyList<string> Values);

public static unsafe partial class EventLog
{
    const uint EvtQueryChannelPath = 0x1, EvtQueryReverseDirection = 0x200, EvtRenderEventXml = 1;
    static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    [LibraryImport("wevtapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr EvtQuery(IntPtr session, string path, string query, uint flags);
    [LibraryImport("wevtapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNext(IntPtr resultSet, uint size, IntPtr* events, uint timeoutMs, uint flags, out uint returned);
    [LibraryImport("wevtapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtRender(IntPtr context, IntPtr fragment, uint flags, uint bufferBytes, char* buffer, out uint usedBytes, out uint propertyCount);
    [LibraryImport("wevtapi.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(IntPtr handle);

    // Newest first, at most `max`. A log that needs admin to read (some Operational logs) throws
    // UnauthorizedAccessException: the page then says "needs admin to read" for that section.
    public static List<LogEvent> Query(string channel, string xpath, int max = 200)
    {
        var results = new List<LogEvent>();
        var q = EvtQuery(0, channel, xpath, EvtQueryChannelPath | EvtQueryReverseDirection);
        if (q == 0)
        {
            int e = Marshal.GetLastPInvokeError();
            if (e == 5) throw new UnauthorizedAccessException(channel);
            if (e == 15007 /* ERROR_EVT_CHANNEL_NOT_FOUND */) return results;
            throw new System.ComponentModel.Win32Exception(e);
        }
        try
        {
            var batch = stackalloc IntPtr[32];
            var buffer = new char[16 * 1024];
            while (results.Count < max && EvtNext(q, 32, batch, 1000, 0, out uint got))
                for (uint i = 0; i < got; i++)
                {
                    try
                    {
                        string xml;
                        fixed (char* b = buffer)
                        {
                            if (!EvtRender(0, batch[i], EvtRenderEventXml, (uint)buffer.Length * 2, b, out uint used, out _))
                            {
                                if (Marshal.GetLastPInvokeError() != Win32.ERROR_INSUFFICIENT_BUFFER) continue;
                                buffer = new char[used / 2 + 1];
                                fixed (char* b2 = buffer) EvtRender(0, batch[i], EvtRenderEventXml, (uint)buffer.Length * 2, b2, out used, out _);
                            }
                            xml = new string(buffer, 0, (int)(used / 2) - 1);
                        }
                        results.Add(Parse(xml));
                    }
                    finally { EvtClose(batch[i]); }
                }
        }
        finally { EvtClose(q); }
        return results;
    }

    static LogEvent Parse(string xml)
    {
        var e = XElement.Parse(xml);
        var sys = e.Element(Ns + "System")!;
        var data = new Dictionary<string, string>();
        var values = new List<string>();
        // EventData's <Data Name="…">; classic events (Application Error on older builds) have
        // unnamed <Data> in order, kept in Values. UserData (some providers) flattened the same way.
        foreach (var d in e.Element(Ns + "EventData")?.Elements(Ns + "Data") ?? e.Element(Ns + "UserData")?.Descendants() ?? [])
        {
            values.Add(d.Value);
            if (d.Attribute("Name")?.Value is { } name) data[name] = d.Value;
            else if (d.Name.Namespace != Ns) data[d.Name.LocalName] = d.Value;
        }
        return new LogEvent(sys.Element(Ns + "Provider")!.Attribute("Name")!.Value, int.Parse(sys.Element(Ns + "EventID")!.Value),
            DateTime.Parse(sys.Element(Ns + "TimeCreated")!.Attribute("SystemTime")!.Value).ToLocalTime(), data, values);
    }

    // Last `days` days, as an XPath time filter (milliseconds).
    public static string Since(int days) => $"TimeCreated[timediff(@SystemTime) <= {days * 86_400_000L}]";
}
```

The queries (System log unless said; all readable by a standard user except where marked):

```csharp
static class HealthQueries
{
    static string Q(string provider, string ids, int days = 30) =>
        $"*[System[Provider[@Name='{provider}'] and ({ids}) and {EventLog.Since(days)}]]";

    // Unexpected restart: the PC came back without a clean shutdown.
    // Data: BugcheckCode (≠0: it was a blue screen), PowerButtonTimestamp (≠0: the power button was held),
    // SleepInProgress (it died going to or in sleep).
    public static readonly (string Log, string XPath) KernelPower41 = ("System", Q("Microsoft-Windows-Kernel-Power", "EventID=41"));
    // "The previous system shutdown was unexpected" (date and time in Values[0], Values[1]).
    public static readonly (string, string) Unexpected6008 = ("System", Q("EventLog", "EventID=6008"));
    // Blue screen: the bugcheck as text in param1 ("0x0000009f (…)"), the dump file in param2.
    // The source shown as "BugCheck" is this provider.
    public static readonly (string, string) BugCheck1001 = ("System", Q("Microsoft-Windows-WER-SystemErrorReporting", "EventID=1001"));
    // Who restarted or shut down, and why: param1 process (e.g. C:\Windows\servicing\TrustedInstaller.exe,
    // or MoUsoCoreWorker.exe for Windows Update), param3 reason, param5 "restart"/"power off", param7 user.
    public static readonly (string, string) User1074 = ("System", Q("User32", "EventID=1074"));
    // Clean start and stop (to bracket sessions): Kernel-General 12 = started, 13 = shut down.
    public static readonly (string, string) Boots = ("System", Q("Microsoft-Windows-Kernel-General", "EventID=12 or EventID=13"));
    // What woke it: WakeSourceText, SleepTime, WakeTime (check field names).
    public static readonly (string, string) Wake1 = ("System", Q("Microsoft-Windows-Power-Troubleshooter", "EventID=1"));
    // App crashes (Application log). Named fields on newer builds (AppName, AppVersion, ModuleName,
    // ExceptionCode) (check); else Values[0] app, [1] version, [3] module, [6] exception code.
    public static readonly (string, string) AppCrash1000 = ("Application", Q("Application Error", "EventID=1000"));
    public static readonly (string, string) AppHang1002 = ("Application", Q("Application Hang", "EventID=1002"));
    // Slow boots: 100 = boot (BootTime, MainPathBootTime, BootPostBootTime, in ms) (check names);
    // 101-110 = what slowed it (101 app, 102 driver, 103 service, …; Name/FriendlyName, TotalTime, DegradationTime).
    // (check) this log may need admin or the Event Log Readers group.
    public static readonly (string, string) Boot100 = ("Microsoft-Windows-Diagnostics-Performance/Operational",
        $"*[System[(EventID>=100 and EventID<=110) and {EventLog.Since(90)}]]");
}
```

Plain sentences, newest first:

```csharp
static string Explain(LogEvent e) => (e.Provider, e.Id) switch
{
    ("Microsoft-Windows-Kernel-Power", 41) when Num(e, "BugcheckCode") != 0 => $"Crashed (blue screen 0x{Num(e, "BugcheckCode"):x}) and restarted",
    ("Microsoft-Windows-Kernel-Power", 41) when Num(e, "PowerButtonTimestamp") != 0 => "Was switched off by holding the power button",
    ("Microsoft-Windows-Kernel-Power", 41) when Num(e, "SleepInProgress") != 0 => "Lost power or froze while asleep",
    ("Microsoft-Windows-Kernel-Power", 41) => "Restarted without shutting down (power lost, or it froze)",
    ("Microsoft-Windows-WER-SystemErrorReporting", 1001) => $"Blue screen: {Get(e, "param1")}",
    ("User32", 1074) => $"{Path.GetFileNameWithoutExtension(Get(e, "param1"))} {Get(e, "param5")}ed it: {Get(e, "param3")}",
    ("Application Error", 1000) => $"{Get(e, "AppName", 0)} crashed (in {Get(e, "ModuleName", 3)})",
    ("Microsoft-Windows-Power-Troubleshooter", 1) => $"Woke up: {Get(e, "WakeSourceText")}",
    _ => $"{e.Provider} {e.Id}",
};
static string Get(LogEvent e, string name, int position = -1) =>
    e.Data.TryGetValue(name, out var v) ? v : position >= 0 && position < e.Values.Count ? e.Values[position] : "";
static ulong Num(LogEvent e, string name) => ulong.TryParse(Get(e, name), out var n) ? n : 0;
```

Also on the page, each run when opened (no admin needed unless said): `powercfg /lastwake`, `powercfg /waketimers`, and a link to `powercfg /sleepstudy /output %TEMP%\sleep.html` **(check: may need admin)**.

### 8.16 Startup list (`plugins/startup/`, a page)

Sources, each with its switch:

| Source | Where | Switch (Task Manager's own, never a delete) | Admin |
|---|---|---|---|
| Your Run key | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `HKCU\…\Explorer\StartupApproved\Run`, value named like the Run value | no |
| PC Run key (64-bit) | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | `HKLM\…\Explorer\StartupApproved\Run` | yes |
| PC Run key (32-bit) | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run` | `HKLM\…\Explorer\StartupApproved\Run32` | yes |
| Your Startup folder | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` | `HKCU\…\StartupApproved\StartupFolder`, value = the file name (`Foo.lnk`) | no |
| PC Startup folder | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp` | `HKLM\…\StartupApproved\StartupFolder` | yes |
| Store apps' startup tasks | `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<PackageFamilyName>\<TaskId>`, `State` (DWORD) **(check)** | `State`: 0 disabled, 1 disabled by user, 2 enabled (the `StartupTaskState` enum's values) **(check)** | no |
| Scheduled tasks at logon / boot | Task Scheduler (outside `\Microsoft\`) | `Disable-ScheduledTask` | yours: no; others: yes |
| Services set to start | `HKLM\SYSTEM\CurrentControlSet\Services\<name>`: `Start` (2 auto, 3 manual, 4 disabled), `DelayedAutostart` | → manual (3), never deleted | yes |
| Read-only, flagged if odd | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` `Shell`, `Userinit`; `RunOnce` keys | shown, not switched | — |

`StartupApproved` values are 12 bytes: byte 0 is the state, bytes 4-11 a FILETIME when it was disabled. Seen in the wild: `02`/`06` enabled, `03`/`07` disabled, so the low bit is "disabled" **(check on the PC)**; no value means enabled.

```csharp
static class StartupApproved
{
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Disabled() { var b = new byte[12]; b[0] = 3; BitConverter.TryWriteBytes(b.AsSpan(4), DateTime.UtcNow.ToFileTimeUtc()); return b; }
    public static byte[] Enabled() { var b = new byte[12]; b[0] = 2; return b; }

    // HKCU ones directly; HKLM ones as the admin verb startup-machine <Run|Run32|StartupFolder> "<name>" <on|off>.
    public static void Set(string approvedKey, string name, bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(approvedKey);
        k.SetValue(name, on ? Enabled() : Disabled(), Microsoft.Win32.RegistryValueKind.Binary);
    }
}

// Services: third-party ones (ImagePath outside %SystemRoot%, or svchost-hosted with a ServiceDll outside it).
static IEnumerable<(string Name, string Display, int Start, bool Delayed, string Image)> AutoServices()
{
    using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services")!;
    var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    foreach (var name in root.GetSubKeyNames())
    {
        using var s = root.OpenSubKey(name);
        if (s?.GetValue("Type") is not int type || (type & 0x30) == 0) continue;           // 0x10/0x20: Win32 services, not drivers
        if (s.GetValue("Start") is not int start || start != 2) continue;                    // automatic only
        var image = Environment.ExpandEnvironmentVariables(s.GetValue("ImagePath") as string ?? "").Trim('"');
        using var p = s.OpenSubKey("Parameters");
        var dll = p?.GetValue("ServiceDll") as string;
        var file = dll != null ? Environment.ExpandEnvironmentVariables(dll) : image;
        if (file.StartsWith(windir, StringComparison.OrdinalIgnoreCase)) continue;
        yield return (name, s.GetValue("DisplayName") as string ?? name, start, s.GetValue("DelayedAutostart") is 1, file);
    }
}
```

Scheduled tasks: the page runs this once when opened (a 0.3 s PowerShell start doesn't show on an occasional page), JSON back:

```powershell
Get-ScheduledTask | Where-Object {
    $_.TaskPath -notlike '\Microsoft\*' -and
    ($_.Triggers | Where-Object { $_.CimClass.CimClassName -in 'MSFT_TaskLogonTrigger', 'MSFT_TaskBootTrigger' })
} | ForEach-Object {
    [pscustomobject]@{
        Path = $_.TaskPath; Name = $_.TaskName; State = [string]$_.State; Author = $_.Author
        RunAs = $_.Principal.UserId; Run = ($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)".Trim() }) -join '; '
    }
} | ConvertTo-Json -Compress
```

Boot cost per item: Diagnostics-Performance 101 (app) and 103 (service) events from 8.15 matched by name. Every switch writes the old value to `settings-history.tsv` (undo).

### 8.17 Processes in the launcher (`plugins/top/`, a launcher source: `top` or a process name)

One `NtQuerySystemInformation(SystemProcessInformation = 5)` call gives every process with its private working set and CPU times, no handle per process (protected processes included). The struct is in `winternl.h` mostly as `Reserved` fields; the offsets below are the widely used ones from the NT headers **(undocumented: check on x64 and arm64; fallback: `Process.GetProcesses()` + `GetProcessTimes`)**.

```csharp
static unsafe partial class Snapshot
{
    [LibraryImport("ntdll.dll")] private static partial int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returned);

    public readonly record struct Proc(uint Pid, uint Parent, string Name, long PrivateWs, long Commit, long CpuTicks, uint Session);

    // SYSTEM_PROCESS_INFORMATION (64-bit): NextEntryOffset 0, NumberOfThreads 4, WorkingSetPrivateSize 8,
    // CreateTime 32, UserTime 40, KernelTime 48, ImageName (UNICODE_STRING: Length 56, Buffer 64),
    // UniqueProcessId 80, InheritedFromUniqueProcessId 88, HandleCount 96, SessionId 100,
    // WorkingSetSize 144, PagefileUsage 184, PrivatePageCount 200.
    public static List<Proc> Take()
    {
        int size = 1 << 20;
        while (true)
        {
            var buffer = new byte[size];
            fixed (byte* b = buffer)
            {
                int status = NtQuerySystemInformation(5, b, size, out int needed);
                if (status == unchecked((int)0xC0000004)) { size = needed + (1 << 16); continue; } // STATUS_INFO_LENGTH_MISMATCH
                if (status != 0) throw new System.ComponentModel.Win32Exception(status);
                var list = new List<Proc>(512);
                for (byte* p = b; ; )
                {
                    ushort nameBytes = *(ushort*)(p + 56);
                    char* name = *(char**)(p + 64);
                    list.Add(new Proc((uint)*(nint*)(p + 80), (uint)*(nint*)(p + 88),
                        name == null ? "System Idle" : new string(name, 0, nameBytes / 2),
                        *(long*)(p + 8), *(long*)(p + 200), *(long*)(p + 40) + *(long*)(p + 48), *(uint*)(p + 100)));
                    uint next = *(uint*)p;
                    if (next == 0) break;
                    p += next;
                }
                return list;
            }
        }
    }
}
// CPU %: two snapshots 1 s apart, Δticks / (Δwall × 10^7 × cores). Grouped by app the way profile-ram.ps1
// does (msedgewebview2 / crashpad_handler / conhost count toward their parent). Ports per pid from 8.20's
// tables (LISTEN rows); files per pid on demand with 8.1 (per process: RmRegisterResources can't ask
// "what does pid X hold", so this row says "open files: use Who has on a path" — no handle enumeration,
// which would need NtQuerySystemInformation(SystemExtendedHandleInformation) and admin to name them).
// End: 8.1's Processes.End, after a confirm; refreshed every 2 s while the page is open.
```

### 8.18 Windows Update without surprises (catalog entries + `plugins/updates/`, a service with a bar line)

Restart pending, cheaply, from the registry (readable without admin; watched with `RegNotifyChangeKeyValue`, no polling):

```csharp
static bool RestartPending()
{
    using var hklm = Microsoft.Win32.Registry.LocalMachine;
    return hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null
        || hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null;
}
```

What's pending and the history, from the Windows Update Agent (`Microsoft.Update.Session`, documented COM). It's IDispatch-based with many members; rather than hand-count vtable slots **(the C# vtable route is possible: `IUpdateSession` `{816858A4-260D-4260-933A-2585F1ABC76B}`, CLSID `{4CB43D7F-7EEE-4906-8698-60DA1C38F2FE}`; slots to check in `wuapi.h`)**, the plugin runs this script hourly and after resume (a background service: PowerShell's start-up cost doesn't show):

```powershell
# plugins/updates/status.ps1 (PS 5.1 is fine). Offline: the agent's last scan, no network, about a second.
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $false
$pending = $searcher.Search("IsInstalled=0 and IsHidden=0").Updates | ForEach-Object {
    [pscustomobject]@{
        Title = $_.Title; Kb = ($_.KBArticleIDs | ForEach-Object { "KB$_" }) -join ','
        Downloaded = $_.IsDownloaded
        Restart = $_.InstallationBehavior.RebootBehavior -ne 0     # 0 never, 1 always, 2 can request
    }
}
$history = $searcher.QueryHistory(0, [Math]::Min(100, $searcher.GetTotalHistoryCount())) |
    Where-Object { $_.Operation -eq 1 -and $_.ResultCode -eq 2 } |     # installation, succeeded
    ForEach-Object { [pscustomobject]@{ Date = $_.Date.ToString('o'); Title = $_.Title; Kb = if ($_.Title -match 'KB(\d{6,8})') { $Matches[1] } } }  # Date: UTC (check)
[pscustomobject]@{
    RestartRequired = (New-Object -ComObject Microsoft.Update.SystemInfo).RebootRequired
    Pending = @($pending); History = @($history)
} | ConvertTo-Json -Depth 3 -Compress
```

Bar line when a restart is pending: "Restart needed for updates" with *Restart tonight* (`shutdown /r /t <seconds until 03:00> /c "omawin: updates"`, no admin), *Not today*.

Catalog entries (active hours, pause a week, no automatic restart while signed in, drivers excluded: 8.43): `system.update-*` in the catalog section below.

## 8E. Network

### 8.19 DNS, hosts and proxy (catalog + `plugins/hosts/`)

Reading the current DNS (no admin) with .NET's `NetworkInterface.GetAllNetworkInterfaces()` → `GetIPProperties().DnsAddresses` (its `Id` is the adapter GUID). Setting needs admin, so it's a verb. The batch runs PowerShell, where the DnsClient module's cmdlets do it:

```powershell
# admin batch, verbs 'dns-set <ifIndex> <servers>' and 'dns-reset <ifIndex>' (PS 5.1, DnsClient module, Windows 8+).
# ifIndex: NetworkInterface.GetIPProperties().GetIPv4Properties().Index on the shell's side.
'dns-set' {
    $a = Split-Arg $s.Arg
    Set-DnsClientServerAddress -InterfaceIndex ([int]$a[0]) -ServerAddresses ($a[1] -split ',')
    Clear-DnsClientCache
}
'dns-reset' { Set-DnsClientServerAddress -InterfaceIndex ([int]$s.Arg) -ResetServerAddresses; Clear-DnsClientCache }
# DNS over HTTPS (Windows 11): Cloudflare, Google and Quad9 are in Windows' built-in list
# (Get-DnsClientDohServerAddress), so setting their IPs is enough to make DoH possible; making it
# required per adapter (check: syntax and whether it's per interface in 23H2+):
#   netsh dns add encryption server=1.1.1.1 dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=no
```

The same through the documented API, for a future native admin helper (`SetInterfaceDnsSettings`, Windows 10 2004+, `netioapi.h`):

```csharp
static unsafe partial class Dns
{
    const uint DNS_INTERFACE_SETTINGS_VERSION1 = 1;
    const ulong DNS_SETTING_IPV6 = 0x0001, DNS_SETTING_NAMESERVER = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct DNS_INTERFACE_SETTINGS // 64 bytes on 64-bit
    {
        public uint Version; public ulong Flags;
        public char* Domain, NameServer, SearchList;
        public uint RegistrationEnabled, RegisterAdapterName, EnableLLMNR, QueryAdapterName;
        public char* ProfileNameServer;
    }

    [LibraryImport("iphlpapi.dll")] private static partial uint SetInterfaceDnsSettings(Guid iface, DNS_INTERFACE_SETTINGS* settings);

    // servers: "1.1.1.1,1.0.0.1" (comma-separated (check)), "" = back to DHCP. ERROR_ACCESS_DENIED without admin.
    public static uint Set(Guid iface, string servers, bool ipv6 = false)
    {
        fixed (char* ns = servers)
        {
            var s = new DNS_INTERFACE_SETTINGS { Version = DNS_INTERFACE_SETTINGS_VERSION1, Flags = DNS_SETTING_NAMESERVER | (ipv6 ? DNS_SETTING_IPV6 : 0), NameServer = ns };
            return SetInterfaceDnsSettings(iface, &s);
        }
    }
}
```

Presets: *Your router* (dhcp), *Cloudflare* (`1.1.1.1,1.0.0.1` / `2606:4700:4700::1111,2606:4700:4700::1001`), *Quad9* (`9.9.9.9,149.112.112.112` / `2620:fe::fe,2620:fe::9`), *Google* (`8.8.8.8,8.8.4.4`).

Hosts editor: reads `%SystemRoot%\System32\drivers\etc\hosts` (readable by users), edits as rows (address, names, comment, on/off = commented with `# omawin-off `), writes the whole new file to `%LOCALAPPDATA%\launcher\pending.hosts`, then queues `hosts-write <its SHA-256>`. The elevated batch may run as another account, so it finds the file beside the status file it was given (in the queuing user's `launcher` folder) **(check: plan 03's status path)**, and refuses it if the hash differs from what you approved:

```powershell
'hosts-write' {
    $file = Join-Path (Split-Path $Status) 'pending.hosts'
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $s.Arg) { throw 'pending.hosts changed after you approved it' }
    $new = Get-Content -LiteralPath $file -Encoding UTF8
    # Every line: blank, a comment, or "<IPv4|IPv6> <name> [names…] [# comment]". Anything else refuses the batch item.
    foreach ($l in $new) {
        if ($l -notmatch '^\s*(#.*)?$' -and $l -notmatch '^\s*[0-9a-fA-F:.]+\s+[A-Za-z0-9._-]+(\s+[A-Za-z0-9._-]+)*\s*(#.*)?$') { throw "bad line: $l" }
    }
    $hosts = "$env:SystemRoot\System32\drivers\etc\hosts"
    Copy-Item $hosts "$hosts.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
    [IO.File]::WriteAllLines($hosts, $new, (New-Object Text.UTF8Encoding $false))
    ipconfig /flushdns | Out-Null
}
```

Proxy (no admin, `builtin proxy`): `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings` `ProxyEnable` (DWORD 0/1), `ProxyServer` (`host:port`), `ProxyOverride` (`<local>;…`), `AutoConfigURL`; then tell WinINet: `InternetSetOptionW(0, 39 /* INTERNET_OPTION_SETTINGS_CHANGED */, 0, 0)` and `InternetSetOptionW(0, 37 /* INTERNET_OPTION_REFRESH */, 0, 0)` (`wininet.dll`).

### 8.20 Connections per app (`plugins/net/`, a launcher page beside Localhost)

```csharp
// shared/Omawin.Windows/Sockets.cs (8.17 and Localhost use it too).
public sealed record Socket(string Protocol, string Local, int LocalPort, string Remote, int RemotePort, string State, uint Pid);

public static unsafe partial class Sockets
{
    const int AF_INET = 2, AF_INET6 = 23;
    const int TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(void* table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sorted, int af, int tableClass, uint reserved);
    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedUdpTable(void* table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sorted, int af, int tableClass, uint reserved);

    static readonly string[] States = ["", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"];

    // Ports are in network byte order in the low 16 bits.
    static int Port(uint p) => (int)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF));
    static string V4(uint a) => new System.Net.IPAddress(a).ToString();
    static string V6(byte* a) => new System.Net.IPAddress(new ReadOnlySpan<byte>(a, 16)).ToString();

    static byte[] Table(bool tcp, int af)
    {
        int size = 0;
        while (true)
        {
            var buffer = new byte[Math.Max(size, 4)];
            fixed (byte* b = buffer)
            {
                uint r = tcp ? GetExtendedTcpTable(b, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0)
                             : GetExtendedUdpTable(b, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
                if (r == 0) return buffer;
                if (r != 122) throw new System.ComponentModel.Win32Exception((int)r); // ERROR_INSUFFICIENT_BUFFER: size updated
            }
        }
    }

    public static List<Socket> All()
    {
        var list = new List<Socket>();
        fixed (byte* t = Table(true, AF_INET))
        {
            // MIB_TCPTABLE_OWNER_PID: dwNumEntries, then MIB_TCPROW_OWNER_PID (24): state, localAddr, localPort, remoteAddr, remotePort, pid
            uint n = *(uint*)t; var row = (uint*)(t + 4);
            for (uint i = 0; i < n; i++, row += 6)
                list.Add(new Socket("TCP", V4(row[1]), Port(row[2]), V4(row[3]), Port(row[4]), States[Math.Min(row[0], 12u)], row[5]));
        }
        fixed (byte* t = Table(true, AF_INET6))
        {
            // MIB_TCP6ROW_OWNER_PID (56): localAddr[16] 0, localScopeId 16, localPort 20, remoteAddr[16] 24, remoteScopeId 40, remotePort 44, state 48, pid 52
            uint n = *(uint*)t; byte* row = t + 4;
            for (uint i = 0; i < n; i++, row += 56)
                list.Add(new Socket("TCP", V6(row), Port(*(uint*)(row + 20)), V6(row + 24), Port(*(uint*)(row + 44)), States[Math.Min(*(uint*)(row + 48), 12u)], *(uint*)(row + 52)));
        }
        fixed (byte* t = Table(false, AF_INET))
        {
            // MIB_UDPROW_OWNER_PID (12): localAddr, localPort, pid
            uint n = *(uint*)t; var row = (uint*)(t + 4);
            for (uint i = 0; i < n; i++, row += 3) list.Add(new Socket("UDP", V4(row[0]), Port(row[1]), "", 0, "", row[2]));
        }
        fixed (byte* t = Table(false, AF_INET6))
        {
            // MIB_UDP6ROW_OWNER_PID (28): localAddr[16], localScopeId 16, localPort 20, pid 24
            uint n = *(uint*)t; byte* row = t + 4;
            for (uint i = 0; i < n; i++, row += 28) list.Add(new Socket("UDP", V6(row), Port(*(uint*)(row + 20)), "", 0, "", *(uint*)(row + 24)));
        }
        return list;
    }
}
```

The page: established TCP grouped by app (8.17's grouping), remote names from `System.Net.Dns.GetHostEntryAsync` with a cache and a 1 s timeout (shown as the IP until it answers), refreshed every 2 s while open (no change signal exists for the tables). Bytes per connection (`GetPerTcpConnectionEStats`) need admin: not shown.

### 8.21 Share Wi-Fi with a QR code (core, Quick settings)

```csharp
// QuickSettings.cs, class Wlan: the saved network's key. Returned in plain text only to callers allowed
// "wlan_secure_get_plaintext_key": (check) whether a standard user gets it for their own (per-user)
// profiles and all-user ones; if not, the row says "needs admin" and offers nothing else.
[LibraryImport("wlanapi.dll", StringMarshalling = StringMarshalling.Utf16)]
private static partial uint WlanGetProfile(IntPtr client, in Guid iface, string profileName, IntPtr reserved, out IntPtr xml, ref uint flags, out uint grantedAccess);
const uint WLAN_PROFILE_GET_PLAINTEXT_KEY = 0x4;

public static (string Ssid, string? Key, string Encryption)? Profile(string profileName)
{
    using var s = new Session();
    foreach (var iface in s.Interfaces)
    {
        uint flags = WLAN_PROFILE_GET_PLAINTEXT_KEY;
        if (WlanGetProfile(s.Handle, iface.InterfaceGuid, profileName, 0, out var xmlPtr, ref flags, out _) != 0) continue;
        try
        {
            var x = System.Xml.Linq.XElement.Parse(Marshal.PtrToStringUni(xmlPtr)!);
            System.Xml.Linq.XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
            var ssid = x.Element(ns + "SSIDConfig")?.Element(ns + "SSID")?.Element(ns + "name")?.Value ?? profileName;
            var security = x.Element(ns + "MSM")?.Element(ns + "security");
            // authEncryption: authentication (open, shared, WPA2PSK, WPA3SAE, …) and encryption (none, WEP, AES, …).
            var encryption = security?.Element(ns + "authEncryption")?.Element(ns + "encryption")?.Value ?? "none";
            var key = security?.Element(ns + "sharedKey");
            bool plain = key?.Element(ns + "protected")?.Value == "false";
            return (ssid, plain ? key!.Element(ns + "keyMaterial")?.Value : null, encryption);
        }
        finally { WlanFreeMemory(xmlPtr); }
    }
    return null;
}

// The QR text phones understand: WIFI:T:<WPA|WEP|nopass>;S:<ssid>;P:<password>;H:<true|false>;;
// with \ ; , : " escaped by a backslash.
static string WifiQrText(string ssid, string? key, string encryption)
{
    static string E(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"([\\;,:""])", @"\$1");
    var type = key == null ? "nopass" : encryption.Equals("WEP", StringComparison.OrdinalIgnoreCase) ? "WEP" : "WPA"; // WPA covers WPA2/WPA3 personal
    return $"WIFI:T:{type};S:{E(ssid)};{(key != null ? $"P:{E(key)};" : "")}H:false;;";
}

// ZXing.Net is already in the menu app (Snip's QR reading): encode to a BitMatrix, draw dark modules
// on a light square (keep it dark-on-light whatever the theme: not every phone reads inverted codes).
static ZXing.Common.BitMatrix QrMatrix(string text) =>
    new ZXing.QrCode.QRCodeWriter().encode(text, ZXing.BarcodeFormat.QR_CODE, 0, 0,
        new Dictionary<ZXing.EncodeHintType, object> { [ZXing.EncodeHintType.MARGIN] = 2, [ZXing.EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M });
```

## 8F. Windows' own surfaces

### 8.22 Notification history (core; needs package identity)

`UserNotificationListener` needs package identity and the `userNotificationListener` capability, and the user's consent (Windows asks once). The shell gets identity without becoming an MSIX app through a **sparse package** (shell-apps.md decision 7): a signed package that has only a manifest and points at the existing install folder.

```xml
<!-- packaging/sparse/AppxManifest.xml -->
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
         xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap uap3 uap10 rescap">
  <!-- Publisher must equal the signing certificate's subject exactly (release-signing.md). -->
  <Identity Name="omawin.Shell" Publisher="CN=omawin" Version="1.0.0.0" ProcessorArchitecture="neutral" />
  <Properties>
    <DisplayName>omawin</DisplayName>
    <PublisherDisplayName>omawin</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  </Properties>
  <Resources><Resource Language="en-us" /></Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <rescap:Capability Name="unvirtualizedResources" />
    <uap3:Capability Name="userNotificationListener" />
  </Capabilities>
  <Applications>
    <Application Id="Shell" Executable="SystemMenu.exe" uap10:TrustLevel="mediumIL" uap10:RuntimeBehavior="win32App">
      <uap:VisualElements DisplayName="omawin" Description="omawin's shell" BackgroundColor="transparent"
                          Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" AppListEntry="none" />
    </Application>
  </Applications>
</Package>
```

```xml
<!-- menu-app/app.manifest, inside <assembly>: ties the exe to the package when it starts. -->
<msix xmlns="urn:schemas-microsoft-com:msix.v1" publisher="CN=omawin" packageName="omawin.Shell" applicationId="Shell" />
```

```powershell
# CI: pack and sign (no content files: /nv skips validation of the missing executable).
makeappx pack /d packaging\sparse /p omawin-shell.msix /nv
signtool sign /fd SHA256 /a /f $cert /p $pass omawin-shell.msix
# On the PC, per user, no admin (the certificate must chain to a root the PC trusts: CI signing):
Add-AppxPackage -Path omawin-shell.msix -ExternalLocation "$env:LOCALAPPDATA\system-menu"
# Undo: Get-AppxPackage omawin.Shell | Remove-AppxPackage
```

(check) side effects of identity on the menu app: AppData redirection shouldn't apply to `mediumIL` + `unvirtualizedResources`, but verify `%LOCALAPPDATA%\launcher` still resolves to the real folder; toasts then show as "omawin" with its icon.

```csharp
// menu-app/Notifications.cs (CsWinRT projection; AOT-safe)
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

static class NotificationHistory
{
    public static async Task Start()
    {
        var listener = UserNotificationListener.Current;
        // Must be called from the UI thread; Windows asks the user once.
        if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed) return;
        foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast)) Add(n);
        // (check) NotificationChanged works for desktop apps with identity; if it throws
        // (0x80070490 has been reported), fall back to re-reading GetNotificationsAsync every 10 s.
        listener.NotificationChanged += (s, e) =>
        {
            if (e.ChangeKind == UserNotificationChangedKind.Added && s.GetNotification(e.UserNotificationId) is { } n) Add(n);
        };
    }

    static void Add(UserNotification n)
    {
        var app = n.AppInfo.DisplayInfo.DisplayName;
        var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
        var lines = binding?.GetTextElements().Select(t => t.Text).ToList() ?? [];
        // %LOCALAPPDATA%\launcher\notifications\<yyyy-MM>.tsv: time, app, title, body (tabs/newlines escaped).
        Save(n.CreationTime, app, lines);
        if (Code(string.Join(' ', lines)) is { } code) Offer($"Copy code {code}", () => ClipboardHistory.SetText(code));
    }

    // Verification codes, as Omarchy spots them: 4-8 digits (or a 3-3 split) near a word that says so.
    static readonly System.Text.RegularExpressions.Regex CodeWord = new(@"(code|verification|verify|otp|one-time|passcode|pin|security)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex Digits = new(@"\b(\d{3}[- ]?\d{3}|\d{4,8})\b");
    static string? Code(string text) => CodeWord.IsMatch(text) && Digits.Match(text) is { Success: true } m ? m.Value.Replace("-", "").Replace(" ", "") : null;
    static void Save(DateTimeOffset when, string app, List<string> lines) { }
    static void Offer(string label, Action act) { }
}
```

### 8.23 The Copilot key (core, the hook)

The Copilot key sends Left Win + Left Shift + F23 (shell-apps.md). Windows' own remap only offers packaged apps registered as Copilot providers **(check: whether 8.22's identity plus an `appExtension` registration would make omawin selectable there, which would beat a hook)**.

```csharp
sealed class CopilotKey(Action open) : IKeyHandler
{
    const uint VK_F23 = 0x86;
    bool swallowing;

    public bool OnKey(in KeyEvent k)
    {
        if (k.Vk == VK_F23 && (k.Modifiers & (KeyMods.Win | KeyMods.Shift)) == (KeyMods.Win | KeyMods.Shift))
        {
            if (k.Down && !swallowing) { swallowing = true; CapsRemap.MaskStart(); Launcher.Post(open); } // mask: Win's release mustn't open Start
            if (!k.Down) swallowing = false;
            return true;
        }
        return false;
    }
}
// What it opens is a choice (launcher by default; a coding agent; nothing), set once it's confirmed
// (AGENTS: keys never change by guesswork).
```

### 8.24 / 8.25

8.24 is one catalog line (section C). 8.25 is "later" in the roadmap: no code here.

## Settings catalog lines (8.24, 8.26-8.31, 8.37-8.40, 8.43, 8.45, 8.46, 8.48, 8.51)

Format and grammar: [02-settings-and-one-search.md](02-settings-and-one-search.md) §1 (`id | icon | name | keywords | kind | how | notes`; ids `<area>.<name>`; `reg <path\value> <dword|qword|sz|expand_sz> on= off= [values="…"] [broadcast=<area>] [refresh-explorer] [restart-explorer]`, `off=-` deletes; `spi <name>` and `powercfg <name> [ac|dc|both]` from named tables in `Switches.cs`; `check` in notes hides a line until it's verified on a PC). Lines plan 02 already has are not repeated: `developer.dev-mode`, `developer.long-paths`, `developer.sudo`, `system.fast-startup`, `system.game-bar`, `system.hibernate`, `explorer.open-to`, `input.sticky-shortcut`, `privacy.ad-id`, `privacy.tailored`, `privacy.online-speech`.

Three additions to plan 02's grammar that Phase 8 needs (`Catalog.cs` / `Switches.cs`):
- **`+`** joins targets set together (all to on, or all to off): `reg A dword on=1 off=0 + reg B dword on=1 off=0`. The row reads its state from the first.
- **`recommended=<value>`** in notes: what omawin's *recommended* set uses (8G); `omawin tune-up` lists every line with one, applied or not.
- **`HKCU\Software\Policies` isn't writable by a standard user** (Users can only read it) **(check on the PC)**. Lines there are `admin`, and since the one prompt may run as another account, the `setting <id>` verb (plan 03's admin verbs) writes to the queuing user's hive, `HKEY_USERS\<their SID>\Software\Policies\…`, the SID passed with the batch.

Named `spi` and `powercfg` entries these lines add to `Switches.cs`'s tables:

| Name | Get / Set | Shape |
|---|---|---|
| `spi win-arranging` | `SPI_GETWINARRANGING` 0x0082 / `SPI_SETWINARRANGING` 0x0083 | BOOL in pvParam / uiParam |
| `spi menu-delay` | `SPI_GETMENUSHOWDELAY` 0x006A / `SPI_SETMENUSHOWDELAY` 0x006B | DWORD ms in pvParam / uiParam |
| `spi min-animate` | `SPI_GETANIMATION` 0x0048 / `SPI_SETANIMATION` 0x0049 | `ANIMATIONINFO { cbSize, iMinAnimate }` |
| `spi filter-hotkey` | `SPI_GETFILTERKEYS` 0x0032 / `SPI_SETFILTERKEYS` 0x0033 | `FILTERKEYS` (24 bytes), flag `FKF_HOTKEYACTIVE` 0x4 |
| `spi toggle-hotkey` | `SPI_GETTOGGLEKEYS` 0x0034 / `SPI_SETTOGGLEKEYS` 0x0035 | `TOGGLEKEYS { cbSize, dwFlags }`, flag `TKF_HOTKEYACTIVE` 0x4 |
| `powercfg hibernate-after` | `SUB_SLEEP` / `HIBERNATEIDLE` (seconds; the catalog's minutes × 60) | needs hibernation on (`system.hibernate`) |
| `powercfg wake-timers` | `SUB_SLEEP` / `RTCWAKE` (0 off, 1 on, 2 important only) | |
| `powercfg standby-network` | `SUB_NONE` / `CONNECTIVITYINSTANDBY` (check: alias, values) | Modern Standby PCs only |

Every `set` goes through `SystemParametersInfoW(action, ui, pv, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE /* 0x3 */)`; powercfg through `powercfg /setacvalueindex|/setdcvalueindex SCHEME_CURRENT <sub> <setting> <n>` then `/setactive SCHEME_CURRENT` (no admin for the active scheme's values).

```text
# --- 8.24 the full right-click menu -------------------------------------------------------------
explorer.classic-menu | - | Full right-click menu (no "Show more options") | context menu right click classic windows 10 | switch | builtin classic-menu | restart-explorer recommended=on
# builtin classic-menu: on = HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 exists with an
# empty (Default) value (REG_SZ ""); off = delete the {86ca…} key. A builtin because the state is "the key exists".

# --- 8.26 Windows stops fighting the tiling manager (recommended, on by default) ----------------
system.snap-flyout | - | Snap layouts on the maximize button | snap layouts flyout maximize hover | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\EnableSnapAssistFlyout dword on=1 off=0 | recommended=off
system.snap-drag | - | Snap windows by dragging them to an edge | snap drag edge arrange aero | switch | spi win-arranging + reg "HKCU\Control Panel\Desktop\WindowArrangementActive" sz on=1 off=0 | recommended=off
system.snap-assist | - | Suggest other windows after snapping one | snap assist suggest | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\SnapAssist dword on=1 off=0 | recommended=off
system.snap-bar | - | Snap bar at the top of the screen while dragging | snap bar top drag | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\EnableSnapBar dword on=1 off=0 | recommended=off minBuild=22621 check
system.shake | - | Shake a window to minimize the others | aero shake title bar | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\DisallowShaking dword on=0 off=1 | recommended=off
input.win-hotkeys | - | Windows' own Win+key shortcuts that clash with omawin's | disabled hotkeys win key conflict | switch | builtin disabled-hotkeys | restart-explorer recommended=off check
# builtin disabled-hotkeys: HKCU\…\Explorer\Advanced\DisabledHotkeys (REG_SZ, one character per Win+<key>, e.g. "AEFHMQSV"),
# the letters taken from glazewm/config.yaml's Win bindings; off restores what was there. (check: which keys Win11 honours.)

# --- 8.27 snappier menus and switching -----------------------------------------------------------
look.menu-delay | - | How fast classic menus open | menu show delay submenu speed | choice | spi menu-delay values="400:Windows default,100:Fast,0:Instant" | recommended=100
system.alt-tab-tabs | - | Alt+Tab shows Edge's tabs | alt tab edge tabs browser | choice | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\MultiTaskingAltTabFilter dword values="3:Windows only,2:3 recent tabs,1:5 recent tabs,0:20 recent tabs" | recommended=3 check
look.min-max-animation | - | Minimize and maximize animations | animation minimize maximize window | switch | spi min-animate | recommended=off

# --- 8.28 less running in the background ---------------------------------------------------------
system.edge-startup-boost | - | Edge preloads at sign-in (startup boost) | edge startup boost preload | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Edge\StartupBoostEnabled dword on=- off=0 | admin recommended=off
system.edge-background | - | Edge keeps running after it's closed | edge background continue running | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Edge\BackgroundModeEnabled dword on=- off=0 | admin recommended=off
system.widgets | - | Widgets (the news board) | widgets news interests board | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests dword on=- off=0 | admin recommended=off
system.game-bar | (plan 02's line, extended) | … | … | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled dword on=1 off=0 + reg HKCU\System\GameConfigStore\GameDVR_Enabled dword on=1 off=0 | recommended=off
privacy.background-app | - | Let <app> run in the background | background apps permission store | switch | builtin background-app <PackageFamilyName> | check
# One row per installed Store app, Phone Link first: HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\<PFN>
# Disabled=1 + DisabledByUser=1 (DWORD) to stop it (check). Each row shows the memory that app was using (8K's groups).
system.delivery-optimization | - | Upload updates to other PCs (Delivery Optimization) | delivery optimization peer upload | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode dword on=- off=0 | admin recommended=off
system.maintenance-wake | - | Wake the PC at night for maintenance | automatic maintenance wake night | switch | reg "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance\WakeUp" dword on=1 off=0 | admin recommended=off check
system.startup-onedrive | - | OneDrive starts at sign-in | onedrive startup login | switch | builtin startup-item OneDrive | (asked: it may hold your files)
system.startup-teams | - | Teams starts at sign-in | teams startup login | switch | builtin startup-item MSTeams | (asked)
# builtin startup-item: 8.16's StartupApproved switch (Run key) or the Store app's startup task, whichever the app uses.

# --- 8.29 accidents that don't happen -------------------------------------------------------------
input.layout-hotkey | - | Alt+Shift switches the keyboard layout | language hotkey alt shift layout input | switch | reg "HKCU\Keyboard Layout\Toggle\Hotkey" sz on=1 off=3 + reg "HKCU\Keyboard Layout\Toggle\Language Hotkey" sz on=1 off=3 + reg "HKCU\Keyboard Layout\Toggle\Layout Hotkey" sz on=2 off=3 | signout recommended=off check
input.narrator-shortcut | - | Win+Ctrl+Enter starts Narrator | narrator shortcut accessibility | switch | reg HKCU\Software\Microsoft\Narrator\NoRoam\WinEnterLaunchEnabled dword on=1 off=0 | recommended=off check
input.filter-shortcut | - | Holding Right Shift asks about Filter Keys | filter keys shortcut prompt | switch | spi filter-hotkey | recommended=off
input.toggle-shortcut | - | Holding Num Lock asks about Toggle Keys | toggle keys shortcut prompt | switch | spi toggle-hotkey | recommended=off
privacy.finish-setup | - | "Let's finish setting up your device" after updates | scoobe finish setting up | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement\ScoobeSystemSettingEnabled dword on=1 off=0 | recommended=off
look.spotlight-icon | - | "Learn about this picture" icon on the desktop | spotlight desktop icon learn picture | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel\{2cc5ca98-6485-489a-920e-b3e88a6ccce3} dword on=0 off=1 | refresh-explorer recommended=off check
# (input.sticky-shortcut is plan 02's.)

# --- 8.30 sleep that doesn't drain the battery -----------------------------------------------------
system.hibernate-after | - | Hibernate after this long asleep (on battery) | hibernate standby sleep battery drain | choice | powercfg hibernate-after dc values="0:Never,60:1 hour,180:3 hours,360:6 hours" |
system.wake-timers | - | Wake timers on battery | wake timers sleep battery | switch | powercfg wake-timers dc | recommended=off
system.standby-network | - | Network stays connected in standby (battery) | modern standby network connectivity | choice | powercfg standby-network dc values="0:Off,1:On,2:Managed by Windows" | check
# (system.fast-startup is plan 02's; recommended=off.)

# --- 8.31 Windows' file indexer ---------------------------------------------------------------------
system.search-indexer | - | Windows Search indexer | indexer search service wsearch indexing | switch | builtin service WSearch on=delayed off=disabled | admin (asked; offered once the finder or 8.2 replaces it)

# --- 8.37 UTF-8 everywhere ----------------------------------------------------------------------------
developer.utf8 | - | Use UTF-8 for programs that aren't Unicode (beta) | utf8 code page 65001 encoding acp | switch | reg HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage\ACP sz on=65001 off=<before> + reg HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage\OEMCP sz on=65001 off=<before> + reg HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage\MACCP sz on=65001 off=<before> | admin restart (asked: a few old programs break)
# off=<before>: the values recorded in settings-history.tsv when it was turned on (often 1252 / 437 / 10000).

# --- 8.38-8.40 developer ------------------------------------------------------------------------------
developer.firewall-dev | - | Dev servers may listen on private networks | firewall node bun python dotnet go prompt allow | action | builtin firewall-dev | admin (asked: which runtimes)
developer.ssh-agent | - | SSH agent starts with Windows | ssh agent openssh keys | switch | builtin ssh-agent | admin
developer.case-sensitive | - | Make a folder case-sensitive (for Linux tools) | case sensitive folder fsutil wsl | action | builtin case-sensitive | check
# builtin case-sensitive: asks for the folder, runs `fsutil file setCaseSensitiveInfo "<folder>" enable`
# (check: no admin needed for a folder you own; needs the WSL feature). Long paths, developer mode, sudo: plan 02's lines.

# --- 8.18 / 8.43 Windows Update ------------------------------------------------------------------------
system.update-active-hours | - | Windows Update: active hours | update restart hours working | choice | builtin update-active-hours values="8-17,9-18,7-23" | admin check
# builtin: HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings ActiveHoursStart / ActiveHoursEnd (DWORD hours) (check).
system.update-pause | - | Pause Windows updates for a week | update pause defer | action | builtin update-pause 7 | admin check
# builtin: HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings PauseUpdatesExpiryTime, PauseQualityUpdatesStartTime/EndTime,
# PauseFeatureUpdatesStartTime/EndTime (REG_SZ, ISO 8601 UTC) as Settings writes them (check).
system.update-no-auto-restart | - | No automatic restart while you're signed in | update restart reboot | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers dword on=1 off=- | admin recommended=on
system.update-no-drivers | - | Drivers not replaced by Windows Update | update driver exclude | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ExcludeWUDriversInQualityUpdate dword on=1 off=- | admin (asked, with the trade-off said)

# --- 8.45 every AI feature in one place (Look › AI) -------------------------------------------------
look.ai-copilot-button | - | Copilot button on the taskbar | ai copilot taskbar button | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowCopilotButton dword on=1 off=0 | refresh-explorer recommended=off
look.ai-copilot | - | Windows Copilot (policy) | ai copilot turn off | switch | reg HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot dword on=- off=1 | admin recommended=off check
look.ai-recall | - | Recall saves snapshots of the screen | ai recall snapshots copilot+ | switch | reg HKCU\Software\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis dword on=- off=1 | admin recommended=off
look.ai-recall-remove | - | Remove Recall from Windows | ai recall remove feature | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\AllowRecallEnablement dword on=- off=0 | admin restart check
look.ai-click-to-do | - | Click to Do (act on what's on screen) | ai click to do screen | switch | reg HKCU\Software\Policies\Microsoft\Windows\WindowsAI\DisableClickToDo dword on=- off=1 | admin recommended=off check
look.ai-paint | - | AI in Paint (Image Creator, Cocreator, generative fill) | ai paint image creator cocreator | switch | reg HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint\DisableImageCreator dword on=- off=1 + reg HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint\DisableCocreator dword on=- off=1 + reg HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint\DisableGenerativeFill dword on=- off=1 | admin recommended=off check
look.ai-notepad | - | AI in Notepad (rewrite, summarize) | ai notepad copilot rewrite | link | link notepad.exe | (no documented setting known: a link, honestly labelled, until one is confirmed)
look.ai-edge | - | Copilot and the sidebar in Edge | ai edge copilot sidebar | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Edge\HubsSidebarEnabled dword on=- off=0 | admin recommended=off

# --- 8.46 Explorer's slow paths ----------------------------------------------------------------------
explorer.folder-type | - | Explorer guesses folder types (slow on big folders) | folder type discovery downloads slow general items | switch | builtin folder-type-general | restart-explorer recommended=off check
# builtin: on (Windows' default) = no AllFolders entry; off = HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell
# FolderType = "NotSpecified" (REG_SZ), after asking: it also clears Bags and BagMRU, which resets every folder's saved view (check).
explorer.home-recent | - | Recent files in Explorer's Home | quick access recent files | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ShowRecent dword on=1 off=0 | refresh-explorer recommended=off
explorer.home-frequent | - | Frequent folders in Explorer's Home | quick access frequent folders | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ShowFrequent dword on=1 off=0 | refresh-explorer
explorer.home-cloud | - | Files from Office.com in Explorer's Home | quick access cloud microsoft 365 | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ShowCloudFilesInQuickAccess dword on=1 off=0 | refresh-explorer recommended=off check
explorer.gallery | - | Gallery in Explorer's side pane | gallery navigation pane | switch | reg HKCU\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}\System.IsPinnedToNameSpaceTree dword on=1 off=0 | restart-explorer recommended=off
explorer.separate-process | - | Folder windows in their own process | separate process explorer hang crash | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\SeparateProcess dword on=1 off=0 | restart-explorer recommended=on
# (explorer.open-to is plan 02's; recommended=1, This PC.)

# --- 8.48 web results out of Windows search -----------------------------------------------------------
system.search-web | - | Web results in Windows search | bing web search start | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled dword on=- off=0 | recommended=off check
system.search-suggestions | - | Web suggestions in the search box (policy) | search box suggestions bing policy | switch | reg HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions dword on=- off=1 | admin restart-explorer recommended=off
system.search-highlights | - | Search highlights | search highlights dynamic bing promo | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings\IsDynamicSearchBoxEnabled dword on=1 off=0 | recommended=off

# --- 8.51 diagnostic data at the minimum ----------------------------------------------------------------
privacy.diagnostic-data | - | Diagnostic data: Required only | telemetry diagnostic data optional | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry dword on=- off=1 | admin recommended=off
# off = 1 (Required), the minimum Home and Pro honour (0 only on Enterprise/Education). Windows Update and Defender are untouched.
privacy.feedback | - | Windows asks for feedback | feedback frequency siuf | switch | reg HKCU\Software\Microsoft\Siuf\Rules\NumberOfSIUFInPeriod dword on=- off=0 | recommended=off check
privacy.activity-history | - | Activity history | activity history timeline | switch | reg HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities dword on=- off=0 + reg HKLM\SOFTWARE\Policies\Microsoft\Windows\System\UploadUserActivities dword on=- off=0 | admin recommended=off
privacy.inking-typing | - | Send inking and typing data | inking typing personalization | switch | reg HKCU\Software\Microsoft\Input\TIPC\Enabled dword on=1 off=0 | recommended=off check
```

Builtins named above (`Builtins.cs`, beside plan 02's): `classic-menu`, `disabled-hotkeys`, `background-app`, `startup-item`, `service`, `firewall-dev`, `ssh-agent`, `case-sensitive`, `update-active-hours`, `update-pause`, `folder-type-general`. Admin ones produce verbs (next section); the rest write HKCU directly with undo.

## 8H. Developer papercuts (code)

### 8.32 Dev Drive (admin verb, asked; 🖥)

The quick path is Windows' own page: `link ms-settings:disksandvolumes` (Settings › System › Storage › Disks & volumes › Create Dev Drive). omawin's verb does the same unattended, inside the one admin batch:

```powershell
# admin batch, verb 'devdrive-create' <vhdx path> <size GB> <letter> (PS 5.1). Dev Drive needs
# Windows 11 with the Dev Drive update (22621.2338+) and is at least 50 GB. A VHDX grows as used.
'devdrive-create' {
    $a = Split-Arg $s.Arg
    $vhd, $gb, $letter = $a[0], [int]$a[1], $a[2]
    if ($gb -lt 50) { throw 'a Dev Drive is at least 50 GB' }
    if (Test-Path $vhd) { throw "$vhd exists" }
    New-Item -ItemType Directory -Force (Split-Path $vhd) | Out-Null
    # diskpart makes the file (New-VHD needs Hyper-V's module, which Home doesn't have).
    $script = Join-Path $env:TEMP 'omawin-devdrive.txt'
    "create vdisk file=`"$vhd`" maximum=$($gb * 1024) type=expandable" | Set-Content $script -Encoding ascii
    diskpart /s $script | Out-Null
    Remove-Item $script
    $disk = Mount-DiskImage -ImagePath $vhd -PassThru | Get-Disk
    Initialize-Disk -Number $disk.Number -PartitionStyle GPT
    New-Partition -DiskNumber $disk.Number -UseMaximumSize -DriveLetter $letter | Out-Null
    Format-Volume -DriveLetter $letter -DevDrive -NewFileSystemLabel 'Dev' -Confirm:$false | Out-Null
    fsutil devdrv trust "$($letter):" | Out-Null           # Defender's performance mode applies to trusted Dev Drives
    # A VHDX isn't attached again after a restart by itself (check: Settings-made ones are); a task at
    # start-up attaches it. It runs once per boot and exits: nothing elevated stays running.
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -Command `"Mount-DiskImage -ImagePath '$vhd'`""
    Register-ScheduledTask -TaskName 'omawin Dev Drive' -TaskPath '\omawin\' -Action $action -Trigger (New-ScheduledTaskTrigger -AtStartup) `
        -Principal (New-ScheduledTaskPrincipal -UserId 'SYSTEM' -RunLevel Highest) -Force | Out-Null
}
'devdrive-trust' { fsutil devdrv trust "$($s.Arg):" | Out-Null }
# Filters some tools need on a Dev Drive (they're blocked by default) (check per tool):
#   fsutil devdrv setfiltersallowed "PrjFlt, bindFlt, wcifs"     # ProjFS (VFS for Git), Docker/containers
```

Then, without admin: folders and the package caches' environment variables (user scope, broadcast; existing caches stay where they are unless the user asks to move them; 8.3 offers to clean the old ones):

```csharp
static readonly (string Var, string Sub)[] CacheVars =
[
    ("npm_config_cache", @"packages\npm"),
    ("YARN_CACHE_FOLDER", @"packages\yarn"),
    ("BUN_INSTALL_CACHE_DIR", @"packages\bun"),
    ("NUGET_PACKAGES", @"packages\nuget"),
    ("PIP_CACHE_DIR", @"packages\pip"),
    ("UV_CACHE_DIR", @"packages\uv"),
    ("CARGO_HOME", @"packages\cargo"),          // moves cargo's bin too: PATH updated (8.35), asked
    ("GOMODCACHE", @"packages\go\mod"),
    ("GOCACHE", @"packages\go\build"),
    ("GRADLE_USER_HOME", @"packages\gradle"),
    ("MAVEN_OPTS", @"-Dmaven.repo.local=D:\packages\maven"), // a value, not a folder
    ("VCPKG_DEFAULT_BINARY_CACHE", @"packages\vcpkg"),
    ("MISE_CACHE_DIR", @"packages\mise"),       // (check) name
];
// pnpm: `pnpm config set store-dir D:\packages\pnpm-store` (it reads its own config, not an env var (check)).
// Code: ~/src → a folder on the Dev Drive only if ~/src is empty or the user picks "move" (robocopy /MOVE
// with a progress note); otherwise new projects go to D:\src and the Projects page lists both.
// Measured: one build of a chosen project before and after (`omawin profile build <dir>`, timed 3×).
```

### 8.33 Git as fast as on Linux (`git/gitconfig`, the `shell` step)

```ini
[core]
	# Windows-specific speed-ups (Git for Windows): cache stat data in memory, read the index in
	# parallel, remember untracked folders, and let the built-in daemon watch the tree so
	# `git status` doesn't walk it (Git 2.37+; ignored on network drives and \\wsl$ paths).
	fscache = true
	preloadindex = true
	untrackedCache = true
	fsmonitor = true
	longpaths = true
[feature]
	manyFiles = true
[index]
	# manyFiles turns on skipHash (Git 2.40+), which older libgit2-based tools read as a corrupt
	# index (check: which GUIs still do); keep the hash.
	skipHash = false
[fetch]
	writeCommitGraph = true
```

Proof: `Measure-Command { git status }` × 5 in a big repo (the repo of a large project the user has, or a clone of `torvalds/linux` in the sandbox), before and after; written to `manual/`.

### 8.34 "Port already in use" that isn't (admin verbs)

```powershell
# Diagnose (no admin; numbers only, so no locale issue): the excluded ranges and the dynamic range.
netsh int ipv4 show excludedportrange protocol=tcp     # rows "start end [*]"; * = an administered (manual) exclusion
netsh int ipv4 show dynamicport tcp                    # Windows' default: start 49152, 16384 ports
```

```csharp
// Localhost (exists) explains a failed bind: is the port inside an excluded range?
static List<(int Start, int End)> ExcludedTcpRanges()
{
    var psi = new ProcessStartInfo("netsh", "int ipv4 show excludedportrange protocol=tcp") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    using var p = Process.Start(psi)!;
    var ranges = new List<(int, int)>();
    foreach (var line in p.StandardOutput.ReadToEnd().Split('\n'))
        if (System.Text.RegularExpressions.Regex.Match(line, @"^\s*(\d+)\s+(\d+)") is { Success: true } m)
            ranges.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
    return ranges;
}
// "Port 3000 is reserved by Windows (Hyper-V/WSL took 2980-3079 at boot). Fix: move Windows' dynamic
// ports out of the dev range (admin, once)."
```

```powershell
# admin batch, verb 'dynamic-ports reset': the dynamic range back to Windows' default, so Hyper-V/WinNAT
# reserve from 49152 up, not among 3000/5173/8080. (Some installs move it down to 1024.)
'dynamic-ports' {
    netsh int ipv4 set dynamicport tcp start=49152 num=16384 | Out-Null
    netsh int ipv6 set dynamicport tcp start=49152 num=16384 | Out-Null
    netsh int ipv4 set dynamicport udp start=49152 num=16384 | Out-Null
    netsh int ipv6 set dynamicport udp start=49152 num=16384 | Out-Null
    # WinNAT re-reserves its ranges when restarted (WSL and Docker reconnect).
    net stop winnat 2>$null | Out-Null; net start winnat 2>$null | Out-Null
}
# verb 'reserve-port <n>': an administered exclusion keeps Hyper-V from taking that port at boot; your own
# server can still bind it (check: that binding inside an administered exclusion works on current builds).
'reserve-port' {
    net stop winnat 2>$null | Out-Null
    netsh int ipv4 add excludedportrange protocol=tcp startport=$($s.Arg) numberofports=1 store=persistent | Out-Null
    net start winnat 2>$null | Out-Null
}
```

### 8.35 PATH doctor (`omawin path`, `bin/path.ps1`, pwsh; fixes with undo)

```powershell
<#
.SYNOPSIS
    omawin path: both PATHs, what's wrong with them, and which program wins for each command.
    -Fix  removes duplicates, missing folders and empty entries from your PATH (with undo);
          the system PATH's fixes are queued as one admin item (env-machine Path <value>).
#>
param([switch]$Fix, [string[]]$Commands = @('node', 'bun', 'python', 'python3', 'git', 'pwsh', 'nvim', 'go', 'cargo', 'java', 'dotnet', 'code', 'mise'))

# Raw values: %VARS% unexpanded, so a fix writes back exactly what it read, as REG_EXPAND_SZ.
function Get-RawPath([Microsoft.Win32.RegistryKey]$Root, [string]$Key) {
    $k = $Root.OpenSubKey($Key)
    $k.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
}
$system = Get-RawPath ([Microsoft.Win32.Registry]::LocalMachine) 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
$user = Get-RawPath ([Microsoft.Win32.Registry]::CurrentUser) 'Environment'

function Normal([string]$p) { [Environment]::ExpandEnvironmentVariables($p.Trim().Trim('"')).TrimEnd('\').ToLowerInvariant() }

$entries = @()
foreach ($scope in 'system', 'user') {
    $raw = if ($scope -eq 'system') { $system } else { $user }
    $i = 0
    foreach ($e in $raw -split ';') {
        $n = Normal $e
        $problem =
            if ($e.Trim() -eq '') { 'empty' }
            elseif ($e -match '^\s|\s$|"') { 'quotes or spaces around it' }
            elseif (-not [IO.Path]::IsPathRooted([Environment]::ExpandEnvironmentVariables($e.Trim('"')))) { 'relative' }
            elseif (-not (Test-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($e.Trim('"'))))) { 'missing' }
            elseif ($entries | Where-Object Normal -eq $n) { "duplicate of $(($entries | Where-Object Normal -eq $n | Select-Object -First 1).Scope)" }
        $entries += [pscustomobject]@{ Scope = $scope; Index = $i++; Raw = $e; Normal = $n; Problem = $problem }
    }
}

# Windows builds a process's PATH as system first, then user: a program in both wins from system.
$exts = $env:PATHEXT -split ';'
$winners = foreach ($c in $Commands) {
    $hits = foreach ($e in $entries | Where-Object { -not $_.Problem }) {
        foreach ($x in $exts) { $f = Join-Path $e.Normal "$c$x"; if (Test-Path -LiteralPath $f) { [pscustomobject]@{ File = $f; Scope = $e.Scope }; break } }
    }
    if (-not $hits) { continue }
    $win = @($hits)[0]
    $note = if ($win.File -like '*\microsoft\windowsapps\*') { "the Store's alias (opens the Store if the app isn't installed)" }
            elseif ($win.Scope -eq 'system' -and @($hits | Where-Object Scope -eq 'user')) { "system PATH beats yours: $(@($hits | Where-Object Scope -eq 'user')[0].File) is ignored" }
    [pscustomobject]@{ Command = $c; Wins = $win.File; Others = (@($hits)[1..9].File -join ', '); Note = $note }
}

# Limits: the whole value; and 2047 characters, past which some programs (cmd's own SET, setx
# truncates at 1024) misbehave.
$length = ($system + ';' + $user).Length

$entries | Where-Object Problem | Format-Table Scope, Raw, Problem -AutoSize
$winners | Format-Table -AutoSize
if ($length -gt 2047) { "PATH is $length characters: over 2047, some programs will see it cut short." }

if ($Fix) {
    $keep = $entries | Where-Object { $_.Scope -eq 'user' -and -not $_.Problem } | ForEach-Object Raw
    $new = ($keep -join ';')
    $history = Join-Path $env:LOCALAPPDATA 'launcher\settings-history.tsv'
    Add-Content $history "$(Get-Date -Format s)`tenv:user:Path`t$user"           # undo: `omawin undo`
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    $k.SetValue('Path', $new, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    omawin.exe env broadcast      # WM_SETTINGCHANGE "Environment" (8.36); new row: env broadcast | … | app --env-broadcast
    $sysKeep = $entries | Where-Object { $_.Scope -eq 'system' -and -not $_.Problem } | ForEach-Object Raw
    if (($sysKeep -join ';') -ne $system) {
        Add-Content (Join-Path $env:LOCALAPPDATA 'launcher\admin-pending.txt') "env-machine Path $($sysKeep -join ';')"
        'The system PATH fix waits for admin: omawin admin'
    }
}
```

Never written through `[Environment]::SetEnvironmentVariable(…, 'User')`: it stores REG_SZ, which breaks entries like `%USERPROFILE%\.cargo\bin`.

### 8.36 Environment variables in the menu (core page)

```csharp
// menu-app/EnvVars.cs
static class EnvVars
{
    const string UserKey = "Environment";
    const string MachineKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public sealed record Var(string Name, string Value, bool Expand, bool Machine);

    public static List<Var> All()
    {
        var list = new List<Var>();
        foreach (var (root, path, machine) in new[] { (Registry.CurrentUser, UserKey, false), (Registry.LocalMachine, MachineKey, true) })
        {
            using var k = root.OpenSubKey(path);
            if (k == null) continue;
            foreach (var n in k.GetValueNames())
                list.Add(new Var(n, k.GetValue(n, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "",
                    k.GetValueKind(n) == RegistryValueKind.ExpandString, machine));
        }
        return list;
    }

    // User variables: written here, then broadcast. Machine ones: queued as one admin item each
    // (env-machine <name> <value>), applied in the next batch. PATH-like values (containing ';')
    // are edited as a list (reorder with Alt+↑/↓) and joined back.
    public static void SetUser(string name, string? value, bool expand)
    {
        using var k = Registry.CurrentUser.CreateSubKey(UserKey);
        var before = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        SettingsHistory.Add($"env:user:{name}", before?.ToString());           // undo
        if (value == null) k.DeleteValue(name, false);
        else k.SetValue(name, value, expand || value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        Omawin.Windows.Win32.BroadcastSettingChange("Environment");
    }
}
```

Explorer rebuilds its environment on that broadcast, so programs started from Explorer see the change. **Programs started by GlazeWM or by omawin.exe inherit *their* environment, which doesn't update**: `omawin launch terminal|project|editor` should build a fresh block for the child (`CreateEnvironmentBlock(out env, userToken, false)` from `userenv.dll`, then `CreateProcess` with `CREATE_UNICODE_ENVIRONMENT`) **(check: whether bin/omawin.cs already does)**, or new terminals from Win+Enter won't see the variable until sign-out.

```powershell
# admin batch, verb 'env-machine' <name> <value>
'env-machine' {
    $name, $value = $s.Arg -split ' ', 2          # the value may hold spaces: not Split-Arg
    $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment', $true)
    Say $s.Line 'before' "$name`t$($k.GetValue($name, '', 'DoNotExpandEnvironmentNames'))"   # for undo
    $kind = if ($value -match '%') { 'ExpandString' } else { 'String' }
    if ($value -eq '') { $k.DeleteValue($name, $false) } else { $k.SetValue($name, $value, $kind) }
    # The broadcast is sent by the user's side after the batch reports done (an elevated process
    # broadcasting works too, but the shell does it once for the whole batch).
}
```

## 8I-8J. Keep it healthy (code)

### 8.41 Disk, battery and device health (on 8.15's page)

```powershell
# plugins/health/health.ps1 (run when the page opens; JSON back)
$disks = Get-PhysicalDisk | ForEach-Object {
    $r = try { $_ | Get-StorageReliabilityCounter -ErrorAction Stop } catch { $null }   # (check) needs admin on most PCs
    [pscustomobject]@{
        Name = $_.FriendlyName; Media = [string]$_.MediaType; Health = [string]$_.HealthStatus   # Healthy / Warning / Unhealthy: no admin
        Status = ($_.OperationalStatus -join ', ')
        Wear = $r.Wear; Temperature = $r.Temperature; TemperatureMax = $r.TemperatureMax; PowerOnHours = $r.PowerOnHours
        ReadErrors = $r.ReadErrorsUncorrected; WriteErrors = $r.WriteErrorsUncorrected
    }
}
# Devices with a problem. 22 (disabled by you) and 45 (not plugged in now) aren't problems.
$codes = @{ 1 = 'not set up'; 3 = 'driver may be corrupted'; 10 = "can't start"; 12 = 'not enough resources'; 18 = 'needs its driver reinstalled'
            28 = 'no driver installed'; 31 = 'not working properly'; 39 = 'driver missing or corrupted'; 43 = 'stopped after reporting a problem'; 52 = 'driver not signed' }
$devices = Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0' |
    Where-Object { $_.ConfigManagerErrorCode -notin 22, 24, 45 } |
    ForEach-Object { [pscustomobject]@{ Name = $_.Name; Code = $_.ConfigManagerErrorCode; Meaning = $codes[[int]$_.ConfigManagerErrorCode]; Id = $_.PNPDeviceID } }
[pscustomobject]@{ Disks = @($disks); Devices = @($devices) } | ConvertTo-Json -Depth 3 -Compress
```

Battery wear in C# (WinRT, no admin, no report file):

```csharp
using Windows.Devices.Power;
static (int DesignMWh, int FullMWh, double Wear)? BatteryWear()
{
    var r = Battery.AggregateBattery.GetReport();
    if (r.DesignCapacityInMilliwattHours is not int design || r.FullChargeCapacityInMilliwattHours is not int full || design <= 0) return null;
    return (design, full, 1.0 - (double)full / design);   // "holds 78% of what it did new"
}
// Cycle count isn't in this report: root\wmi BatteryCycleCount.CycleCount (check: readable without admin),
// or `powercfg /batteryreport /xml /output <file>` (check: no admin).
```

Driver memory (8K item 8): the non-paged pool's size from 8K's counters; above ~1 GB the page names the biggest pool tags via `NtQuerySystemInformation(SystemPoolTagInformation = 22)` **(undocumented; check: works unelevated)** and the drivers containing that tag (the four ASCII bytes searched in `%SystemRoot%\System32\drivers\*.sys`, poolmon's method).

### 8.42 A restore point inside an approved batch

Plan 03 already has the `restore-point` verb, run first in a batch. Two refinements for it:

```powershell
# install/admin-batch.ps1, the restore-point step. Windows makes at most one restore point a day unless
# SystemRestorePointCreationFrequency is 0 (minutes) (check: the value name and unit); set it for the call
# and put it back. With System Protection off for C:, Checkpoint-Computer fails: report "skip", don't turn
# protection on (that's the user's choice, a catalog line of its own if wanted).
foreach ($s in @($steps | Where-Object Verb -eq 'restore-point')) {
    Say $s.Line 'run'
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
    $old = (Get-ItemProperty $key -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
    try {
        Set-ItemProperty $key -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
        Checkpoint-Computer -Description "omawin: $(@($steps | Where-Object Verb -ne 'restore-point').Count) changes" -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
        Say $s.Line 'done'
    } catch { Say $s.Line 'skip' "no restore point: $($_.Exception.Message)" }
    finally {
        if ($null -eq $old) { Remove-ItemProperty $key -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $key -Name SystemRestorePointCreationFrequency -Value $old }
    }
}
```

`omawin undo` lists both kinds: omawin's own changes (`settings-history.tsv`, newest first, each undoable without admin unless it was an admin one) and the restore points (`Get-ComputerRestorePoint` needs admin to list (check); otherwise a link to `rstrui.exe`).

### 8.44 Undo the update that broke it

Correlation (in the health plugin): problem events from 8.15, installs from 8.18's history.

```csharp
public sealed record Install(DateTime When, string Title, string? Kb);
public sealed record Suspect(Install Update, string Problem, int Before, int After, DateTime First);

static IEnumerable<Suspect> Suspects(IReadOnlyList<Install> installs, IReadOnlyList<LogEvent> problems, DateTime now)
{
    // Problems by kind: unexpected restarts (41, 6008), blue screens (1001), each crashing app (1000 by app name).
    var kinds = problems.GroupBy(p => p.Id == 1000 ? $"{Get(p, "AppName", 0)} crashes" : p.Id == 1001 ? "blue screens" : "unexpected restarts");
    foreach (var kind in kinds)
    {
        var times = kind.Select(p => p.Time).Order().ToList();
        // The latest update installed before the first problem of a new run of them.
        foreach (var u in installs.Where(i => i.When > now.AddDays(-30)).OrderByDescending(i => i.When))
        {
            var window = TimeSpan.FromDays(14);
            int before = times.Count(t => t >= u.When - window && t < u.When);
            int after = times.Count(t => t >= u.When && t < u.When + window);
            var first = times.FirstOrDefault(t => t >= u.When);
            // New after the update: at least 3, at least 3× the rate before, and the first within 3 days.
            if (after >= 3 && after >= 3 * Math.Max(before, 1) && first != default && first - u.When < TimeSpan.FromDays(3))
            {
                yield return new Suspect(u, kind.Key, before, after, first);
                break; // one update per kind: the latest matching
            }
        }
    }
}
// "Blue screens started after KB5031234, installed Tuesday: 4 since, none in the two weeks before."
```

```powershell
# admin batch, verb 'update-uninstall <kb number>'. wusa for an update package; DISM for a cumulative update
# that was combined with its servicing stack (wusa refuses those) (check: /quiet is ignored by wusa
# /uninstall on Windows 10+, it shows its own window).
'update-uninstall' {
    $kb = $s.Arg
    $p = Start-Process wusa.exe -ArgumentList "/uninstall /kb:$kb /norestart" -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) {
        $pkg = Get-WindowsPackage -Online | Where-Object { $_.PackageName -like '*RollupFix*' -and $_.PackageState -eq 'Installed' } |
               Sort-Object InstallTime -Descending | Select-Object -First 1      # (check) match by KB via Get-WindowsPackage -PackageName …
        if ($pkg) { Remove-WindowsPackage -Online -PackageName $pkg.PackageName -NoRestart | Out-Null } else { throw "wusa exit $($p.ExitCode)" }
    }
}
# Then in the same batch: `setting system.update-pause` (8.18) so it doesn't come straight back, and a restart offered.
```

### 8.47 The slow right-click extensions

```csharp
// plugins/shellext/src/Extensions.cs: every classic shell extension, with its DLL and company.
public sealed record ShellExt(string Clsid, string Name, string Where, string Kind, string? Dll, string? Company, bool Blocked);

static class Extensions
{
    static readonly string[] Kinds = ["ContextMenuHandlers", "DragDropHandlers", "PropertySheetHandlers", "CopyHookHandlers"];
    const string BlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public static List<ShellExt> All()
    {
        var found = new Dictionary<(string, string, string), ShellExt>();
        using var hkcr = Registry.ClassesRoot;
        var blocked = BlockedSet();
        // Every class with handlers: *, AllFilesystemObjects, Directory, Directory\Background, Folder, Drive,
        // each extension and ProgID, SystemFileAssociations\…. Walking all of HKCR (~10-20k keys) takes ~100 ms.
        foreach (var root in hkcr.GetSubKeyNames().Append(@"Directory\Background").Append(@"SystemFileAssociations"))
            foreach (var kind in Kinds)
            {
                using var k = hkcr.OpenSubKey($@"{root}\shellex\{kind}");
                if (k == null) continue;
                foreach (var name in k.GetSubKeyNames())
                {
                    using var h = k.OpenSubKey(name);
                    // The CLSID is the default value, or the subkey's own name.
                    var clsid = h?.GetValue("") as string is { } v && Guid.TryParse(v, out _) ? v : Guid.TryParse(name, out _) ? name : null;
                    if (clsid == null) continue;
                    var dll = hkcr.OpenSubKey($@"CLSID\{clsid}\InprocServer32")?.GetValue("") as string;
                    dll = dll == null ? null : Environment.ExpandEnvironmentVariables(dll);
                    var company = dll != null && File.Exists(dll) ? FileVersionInfo.GetVersionInfo(dll).CompanyName : null;
                    var display = hkcr.OpenSubKey($@"CLSID\{clsid}")?.GetValue("") as string ?? name;
                    found.TryAdd((clsid.ToUpperInvariant(), root, kind), new ShellExt(clsid, display, root, kind, dll, company, blocked.Contains(clsid.ToUpperInvariant())));
                }
            }
        // Icon overlays (Windows uses only the first 15 by name): HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers.
        return [.. found.Values.OrderBy(e => e.Company?.StartsWith("Microsoft") == true).ThenBy(e => e.Name)];
    }

    static HashSet<string> BlockedSet()
    {
        var set = new HashSet<string>();
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var k = root.OpenSubKey(BlockedKey)) if (k != null) foreach (var n in k.GetValueNames()) set.Add(n.ToUpperInvariant());
        return set;
    }

    // Per user, undoable: a REG_SZ named by the CLSID in HKCU's Blocked key (check: Explorer honours the
    // HKCU list as well as HKLM's; if only HKLM's, this becomes an admin verb shellext-block <clsid> <on|off>). Explorer restarts once.
    public static void Block(string clsid, string name, bool block)
    {
        using var k = Registry.CurrentUser.CreateSubKey(BlockedKey);
        if (block) k.SetValue(clsid, name, RegistryValueKind.String); else k.DeleteValue(clsid, false);
    }
}
```

Timing each extension's load (never inside Explorer, never in the plugin's own process: a third-party DLL can hang or crash): a throwaway probe process per CLSID, killed after 5 s.

```csharp
// plugins/shellext/probe/Probe.cs: `shellext-probe.exe <clsid> <sample file>` → prints "<ms create> <ms init> <ms query>".
static unsafe int Main(string[] args)
{
    Com.CoInitializeEx(0, 2);                         // STA, as in Explorer
    var clsid = Guid.Parse(args[0]);
    var sw = Stopwatch.StartNew();
    var unk = Com.Create(clsid, new Guid("00000000-0000-0000-C000-000000000046"), Com.CLSCTX_INPROC_SERVER); // IUnknown
    var create = sw.Elapsed.TotalMilliseconds;
    // A data object for the sample file: IShellItem → BindToHandler(BHID_DataObject, IID_IDataObject).
    SHCreateItemFromParsingName(args[1], 0, new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), out var item);      // IID_IShellItem
    var bhid = new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4"); var iidData = new Guid("0000010E-0000-0000-C000-000000000046");
    IntPtr data; Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, Guid*, IntPtr*, int>)Com.Slot(item, 3))(item, 0, &bhid, &iidData, &data));
    sw.Restart();
    var init = Com.Query(unk, new Guid("000214E8-0000-0000-C000-000000000046"));          // IShellExtInit
    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, int>)Com.Slot(init, 3))(init, 0, data, 0); // Initialize(pidlFolder, dataObject, hkeyProgID)
    var initialize = sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    var menu = Com.Query(unk, new Guid("000214E4-0000-0000-C000-000000000046"));          // IContextMenu
    var hmenu = CreatePopupMenu();
    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, int>)Com.Slot(menu, 3))(menu, hmenu, 0, 1, 0x7FFF, 0); // QueryContextMenu, CMF_NORMAL
    Console.WriteLine($"{create:F1} {initialize:F1} {sw.Elapsed.TotalMilliseconds:F1}");
    return 0;
}
// [LibraryImport] SHCreateItemFromParsingName (shell32, Utf16, in Guid, out IntPtr) and CreatePopupMenu (user32).
```

The page sorts by total time, marks anything over 50 ms, and offers *Turn off* (Block) and *Turn on*; Windows 11's packaged menu entries (`IExplorerCommand` from MSIX packages, in package manifests as `desktop4:FileExplorerContextMenus`) are listed read-only **(check: whether the Blocked list applies to them)**.

### 8.49 Headphones that stay in stereo (Audio page; 🖥)

The hands-free (HFP) endpoints come from Windows' Bluetooth hands-free enumerator; telling them apart **(check on the PC)**: `PKEY_Device_EnumeratorName` (`{a45c254e-df1c-4efd-8020-67d146a850e0}`, 24) is `BTHHFENUM` for hands-free endpoints and `BTHENUM` for A2DP ones. Shown on the Audio page as "Hands-free (call quality) is in use by Teams" when a capture session is active on that headset.

Turning hands-free off for one headset: the documented Bluetooth call `QuickSettings.cs` already makes, for the Handsfree service only:

```csharp
// QuickSettings.cs, class Bt: like SetAudioServices, one service. Off: the headset stays in A2DP stereo and
// another microphone is used; on again for calls. (check) whether it needs admin on some drivers.
public static string? SetHandsfree(ulong address, bool on)
{
    var parameters = new FindRadioParams { dwSize = (uint)Marshal.SizeOf<FindRadioParams>() };
    var find = BluetoothFindFirstRadio(ref parameters, out var radio);
    if (find == IntPtr.Zero || radio == IntPtr.Zero) return "No Bluetooth radio";
    try
    {
        var device = new DeviceInfo { dwSize = (uint)Marshal.SizeOf<DeviceInfo>(), Address = address, Name = "" };
        var guid = Handsfree;
        uint result = BluetoothSetServiceState(radio, ref device, ref guid, on ? ServiceEnable : ServiceDisable);
        return result is 0 or ErrorInvalidParameter or EInvalidArg ? null : $"BluetoothSetServiceState: error {result}";
    }
    finally { BluetoothFindRadioClose(find); CloseHandle(radio); }
}
```

The alternative Windows' Sound control panel uses ("Disable" on an endpoint) is the undocumented `IPolicyConfig::SetEndpointVisibility` (the slot after `SetDefaultEndpoint` in `Audio.cs`'s declaration) **(undocumented; check)**: kept as a fallback only. Audio enhancements per device (`PKEY_AudioEndpoint_Disable_SysFx`, `{1da5d803-d492-4edd-8c23-e0c0ffee7f0e}`, 5) live under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{endpoint}\FxProperties`, which is admin-only: a link to the device's Sound settings page instead (`ms-settings:sound-properties?endpointId=…` **(check)**). The Bluetooth codec isn't reported by a documented API: not shown.

### 8.50 Remove Windows' extra apps (Install › Remove › Windows apps)

```powershell
# install/windows-apps.txt (in the repo): what's offered, ticked by default or not. Removed for this
# account only (Remove-AppxPackage, no admin); every one is reinstallable from the Store.
#   <package name> | <shown as> | default (yes/ask)
Clipchamp.Clipchamp                         | Clipchamp                    | yes
Microsoft.BingNews                          | News                         | yes
Microsoft.BingWeather                       | Weather                      | ask
Microsoft.BingSearch                        | Bing Search                  | yes
Microsoft.MicrosoftSolitaireCollection      | Solitaire                    | yes
Microsoft.Todos                             | Microsoft To Do              | ask
Microsoft.GamingApp                         | Xbox                         | ask
Microsoft.XboxGamingOverlay                 | Game Bar                     | ask
Microsoft.Xbox.TCUI                         | Xbox Live in-game UI         | ask
Microsoft.XboxSpeechToTextOverlay           | Xbox speech-to-text          | yes
Microsoft.GetHelp                           | Get Help                     | ask
Microsoft.Getstarted                        | Tips                         | yes
Microsoft.MicrosoftOfficeHub                | Microsoft 365 (Office)       | yes
Microsoft.People                            | People                       | yes
Microsoft.PowerAutomateDesktop              | Power Automate               | yes
Microsoft.WindowsFeedbackHub                | Feedback Hub                 | yes
Microsoft.WindowsMaps                       | Maps                         | yes
Microsoft.ZuneVideo                         | Films & TV                   | yes
Microsoft.ZuneMusic                         | Media Player                 | ask
Microsoft.YourPhone                         | Phone Link                   | ask
Microsoft.549981C3F5F10                     | Cortana                      | yes
MicrosoftCorporationII.MicrosoftFamily      | Family                       | yes
MicrosoftCorporationII.QuickAssist          | Quick Assist                 | ask
Microsoft.OutlookForWindows                 | Outlook (new)                | ask
MSTeams                                     | Teams (personal)             | ask
Microsoft.WindowsSoundRecorder              | Sound Recorder               | ask
Microsoft.WindowsAlarms                     | Clock                        | ask
Microsoft.Windows.DevHome                   | Dev Home                     | yes
Microsoft.Copilot                           | Copilot                      | yes   # no-ads already offers it
```

Never listed (the page refuses them even if added to the file): `Microsoft.WindowsStore`, `Microsoft.StorePurchaseApp`, `Microsoft.DesktopAppInstaller` (winget), `Microsoft.WindowsTerminal`, `Microsoft.SecHealthUI`, `Microsoft.WindowsNotepad`, `Microsoft.WindowsCalculator`, `Microsoft.Windows.Photos`, `Microsoft.ScreenSketch`, `Microsoft.Paint`, frameworks (`Microsoft.VCLibs.*`, `Microsoft.UI.Xaml.*`, `Microsoft.NET.*`, `Microsoft.WindowsAppRuntime.*`, `Microsoft.Services.Store.Engagement`), codecs (`Microsoft.*Extension*`, `Microsoft.HEIFImageExtension`, `Microsoft.WebMediaExtensions`, …), `Microsoft.XboxIdentityProvider` (games sign in with it), and anything with `IsFramework` or `SignatureKind -eq 'System'`.

```powershell
# The page's data (sizes: the install folder's size; shared frameworks aren't counted).
Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' } | ForEach-Object {
    $size = (Get-ChildItem -LiteralPath $_.InstallLocation -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    [pscustomobject]@{ Name = $_.Name; Full = $_.PackageFullName; Family = $_.PackageFamilyName; MB = [math]::Round($size / 1MB) }
} | ConvertTo-Json -Compress
# Removing (this account; no admin):
Get-AppxPackage -Name $name | Remove-AppxPackage
# Reinstall: ms-windows-store://pdp/?PFN=<PackageFamilyName>  (opens its Store page)
```

The provisioned copies (what new accounts get) stay: removing those is `Remove-AppxProvisionedPackage -Online` (admin) and isn't offered by default.

## 8K. Idle memory: measure the whole machine (`bin/profile-ram.ps1`)

Today the script groups processes into apps and checks omawin's parts against budgets. It gains three things: **every process in a group you'd recognise**, **memory split into in use / modified / cached / free** (so nobody chases the standby cache), and **budgets for the 600 MB desktop** with on-demand parts counted apart. Same switches (`-Seconds -Top -Json -Save`), plus `-Quick` (no CPU sampling) for `omawin check`.

### The budgets (replaces `$Parts`)

| Part | Processes | Budget MB | CPU % | Counted in the 600 MB desktop |
|---|---|---|---|---|
| window manager (GlazeWM) | glazewm, glazewm-watcher | 80 | 1 | yes |
| shell (menu app, with the native bar from 1.6) | SystemMenu | 200 | 1 | yes |
| terminal (noctty, with its spare shell) | noctty, noctty.com | 150 | 1 | yes |
| agent sessions (herdr, without agents) | herdr | 100 | 1 | yes |
| theme-watch | theme-watch | 30 | 0.5 | yes |
| omawin listen | omawin-listen, omawin | 30 | 0.5 | yes |
| **desktop total** | | **590 (≤ 600)** | | |
| bar (Zebar, until 1.6 ships) | zebar | 350 | 2 | yes, temporarily: the reason the desktop is over today |
| agents (OpenCode, …) | opencode | 800 each | 2 | no: on demand (8K step 2) |
| dictation (Handy) | handy | 300 | 1 | no: on demand (8K step 3) |
| music player | omawin-music, mpv | 250 | 3 | no: while playing |

```powershell
# omawin's parts: process names (no .exe) -> the part, its idle budget (Memory MB, CPU %), and whether
# it's part of the always-on desktop (the mission's 600 MB) or started when you use it.
$Parts = [ordered]@{
    'window manager (GlazeWM)'   = @{ Names = 'glazewm', 'glazewm-watcher'; MB = 80; Cpu = 1; Desktop = $true }
    'shell (menu app)'           = @{ Names = 'SystemMenu'; MB = 200; Cpu = 1; Desktop = $true }
    'terminal (noctty)'          = @{ Names = 'noctty', 'noctty.com'; MB = 150; Cpu = 1; Desktop = $true }
    'agent sessions (herdr)'     = @{ Names = 'herdr'; MB = 100; Cpu = 1; Desktop = $true }
    'theme-watch'                = @{ Names = 'theme-watch'; MB = 30; Cpu = 0.5; Desktop = $true }
    'omawin listen'              = @{ Names = 'omawin-listen', 'omawin'; MB = 30; Cpu = 0.5; Desktop = $true }
    'bar (Zebar, until 1.6)'     = @{ Names = 'zebar'; MB = 350; Cpu = 2; Desktop = $true }
    'agents (on demand)'         = @{ Names = 'opencode', 'claude', 'codex'; MB = 800; Cpu = 2; Desktop = $false; PerProcess = $true }
    'dictation (on demand)'      = @{ Names = 'handy'; MB = 300; Cpu = 1; Desktop = $false }
    'music player (while playing)' = @{ Names = 'omawin-music', 'mpv'; MB = 250; Cpu = 3; Desktop = $false }
}
$DesktopBudgetMB = 600
```

### Everything else, grouped

```powershell
# Whole-machine groups for what isn't omawin's: process name -> group. Anything not listed is grouped by
# its exe's company when it's a hardware maker's (OEM), else shown as itself.
$Groups = [ordered]@{
    'WSL virtual machine'   = 'vmmem', 'vmmemWSL', 'vmcompute', 'wslservice', 'wslhost', 'wslrelay', 'wsl'
    'browsers'              = 'msedge', 'chrome', 'firefox', 'brave', 'vivaldi', 'opera', 'arc'
    'web-view apps'         = 'olk', 'ms-teams', 'Widgets', 'WidgetService', 'WidgetBoard', 'PhoneExperienceHost', 'WebViewHost', 'Copilot', 'M365Copilot', 'Slack', 'Discord', 'Code', 'Spotify', 'Notion', 'Obsidian'
    'Defender (keep)'       = 'MsMpEng', 'MpDefenderCoreService', 'NisSrv', 'SecurityHealthService', 'SecurityHealthSystray', 'smartscreen'
    'Windows Search'        = 'SearchIndexer', 'SearchHost', 'SearchProtocolHost', 'SearchFilterHost'
    'Windows shell'         = 'explorer', 'dwm', 'StartMenuExperienceHost', 'ShellExperienceHost', 'ShellHost', 'TextInputHost', 'ctfmon', 'sihost', 'taskhostw', 'RuntimeBroker', 'ApplicationFrameHost', 'LockApp', 'SystemSettings', 'CrossDeviceResume'
    'OneDrive'              = 'OneDrive', 'OneDrive.Sync.Service', 'FileCoAuth'
    'Windows services'      = 'svchost', 'lsass', 'services', 'wininit', 'winlogon', 'csrss', 'smss', 'fontdrvhost', 'spoolsv', 'audiodg', 'WmiPrvSE', 'dllhost', 'WUDFHost', 'dasHost', 'MoUsoCoreWorker', 'TiWorker', 'TrustedInstaller', 'msiexec'
    'kernel'                = 'System', 'Registry', 'Secure System', 'Memory Compression'
}
$GroupOf = @{}
foreach ($g in $Groups.Keys) { foreach ($n in $Groups[$g]) { $GroupOf[$n] = $g } }
$Oem = 'Lenovo|Dell|HP Inc|Hewlett|ASUS|ASUSTeK|Acer|Micro-Star|MSI|Samsung|Razer|Logitech|Realtek|Intel|NVIDIA|Advanced Micro Devices|Synaptics|Dolby|Waves|Killer|Rivet|McAfee|Norton|Gen Digital'

# Company of an exe, cached by path (Win32_Process gives ExecutablePath; protected processes give none).
$companyOf = @{}
function CompanyOf([string]$path) {
    if (-not $path) { return $null }
    if (-not $companyOf.ContainsKey($path)) { $companyOf[$path] = try { (Get-Item -LiteralPath $path).VersionInfo.CompanyName } catch { $null } }
    $companyOf[$path]
}
```

In the existing `foreach ($proc in $procs.Values)` loop, keep `Path = $p.ExecutablePath` in `$procs`, and after `$app = AppOf $proc`:

```powershell
    $group = if ($PartOf[$app]) { 'omawin' }
             elseif ($GroupOf[$app]) { $GroupOf[$app] }
             elseif ((CompanyOf $proc.Path) -match $Oem) { "OEM: $($Matches[0])" }
             else { 'apps' }
    $e.Group = $group      # added to the per-app object
```

`svchost` by what it hosts (the biggest Windows services, by name):

```powershell
$servicesIn = @{}
foreach ($s in Get-CimInstance Win32_Service -Filter "State='Running'") {
    if ($s.ProcessId) { $servicesIn[[int]$s.ProcessId] += @($s.Name) }
}
# Each svchost process over 30 MB is listed as "svchost: <service names>" under Windows services.
```

### In use vs cached (the number that matters)

```powershell
# Task Manager's memory bar: In use | Modified | Standby (cached) | Free. Standby is file cache Windows
# hands back instantly: free memory, not waste. Property names of the formatted Memory counters (check).
$m = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
$totalB    = $os.TotalVisibleMemorySize * 1KB
$standbyB  = [double]$m.StandbyCacheCoreBytes + $m.StandbyCacheNormalPriorityBytes + $m.StandbyCacheReserveBytes
$freeB     = [double]$m.FreeAndZeroPageListBytes
$modifiedB = [double]$m.ModifiedPageListBytes
$inUseB    = $totalB - $standbyB - $freeB - $modifiedB
$kernel = [pscustomobject]@{
    NonPagedMB      = [math]::Round($m.PoolNonpagedBytes / 1MB)       # drivers; a leak shows here (8.41)
    PagedResidentMB = [math]::Round($m.PoolPagedResidentBytes / 1MB)
    DriversMB       = [math]::Round($m.SystemDriverResidentBytes / 1MB)
    SystemCacheMB   = [math]::Round($m.SystemCacheResidentBytes / 1MB)
}
$memory = [pscustomobject]@{
    TotalMB = [math]::Round($totalB / 1MB); InUseMB = [math]::Round($inUseB / 1MB); ModifiedMB = [math]::Round($modifiedB / 1MB)
    CachedMB = [math]::Round($standbyB / 1MB); FreeMB = [math]::Round($freeB / 1MB)
    # What the groups don't explain: shared pages, page tables, the kernel's own working set.
    UnattributedMB = [math]::Round($inUseB / 1MB - ($apps.Values | Measure-Object MemoryMB -Sum).Sum - $kernel.NonPagedMB - $kernel.PagedResidentMB - $kernel.DriversMB)
}
```

The same numbers without CIM (for `omawin check` from the menu app, or `About.cs`): `GetPerformanceInfo` (`psapi.dll`/`kernel32.dll` `K32GetPerformanceInfo`) gives `PhysicalTotal`, `PhysicalAvailable` (standby + free), `SystemCache`, `KernelPaged`, `KernelNonpaged` in pages (× `PageSize`); it doesn't split standby from free.

```csharp
[StructLayout(LayoutKind.Sequential)]
struct PERFORMANCE_INFORMATION
{
    public uint cb; public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
        KernelTotal, KernelPaged, KernelNonpaged, PageSize; public uint HandleCount, ProcessCount, ThreadCount;
}
[LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo")] [return: MarshalAs(UnmanagedType.Bool)]
private static partial bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);
```

### Output

```powershell
$desktopMB = ($parts | Where-Object { $Parts[$_.Part].Desktop } | Measure-Object MemoryMB -Sum).Sum
$system | Add-Member DesktopMB ([math]::Round($desktopMB)) -PassThru | Add-Member DesktopBudgetMB $DesktopBudgetMB
$groups = $apps.Values | Group-Object Group | ForEach-Object {
    [pscustomobject]@{ Group = $_.Name; Procs = ($_.Group | Measure-Object Procs -Sum).Sum; MemoryMB = [math]::Round(($_.Group | Measure-Object MemoryMB -Sum).Sum)
                       Top = ($_.Group | Sort-Object MemoryMB -Descending | Select-Object -First 3 | ForEach-Object { "$($_.App) $([math]::Round($_.MemoryMB))" }) -join ', ' }
} | Sort-Object MemoryMB -Descending

# -Json gains Memory, Kernel and Groups (the profile-ram skill, .claude/skills/profile-ram/SKILL.md, reads them:
# update it to report in-use vs cached first and to name the biggest group).
[pscustomobject]@{ System = $system; Memory = $memory; Kernel = $kernel; Groups = $groups; Omawin = $parts; Others = $others; Busy = $busy; Changes = $changes } | ConvertTo-Json -Depth 4

# Text: the first lines become
"RAM  {0:N1} GB in use of {1:N1} GB; {2:N1} GB cached (free when needed); {3:N1} GB free" -f ($memory.InUseMB / 1KB), ($memory.TotalMB / 1KB), ($memory.CachedMB / 1KB), ($memory.FreeMB / 1KB)
"omawin desktop  $($system.DesktopMB) of $DesktopBudgetMB MB; on demand: agents, dictation, music counted apart"
"kernel  non-paged pool $($kernel.NonPagedMB) MB, drivers $($kernel.DriversMB) MB; compressed $($system.CompressedMB) MB"
""
"By group"
$groups | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object TrimEnd
```

`-Save` adds rows for each group and for `(in use)` / `(cached)` so before/after for every 8K step reads straight off the history. `omawin check` (`check.ps1`) runs `profile-ram.ps1 -Json -Quick` and warns when `DesktopMB > DesktopBudgetMB` or the non-paged pool is over 1 GB, only after the PC has been up 10 minutes (sign-in noise).

## Before shipping: what to confirm on a PC

Collected **(check)** items, grouped so one session on the PC settles each group:

1. **Explorer automation:** `IFolderView2::GetSelection` slot 32; `IShellBrowser::GetWindow` returning the tab window on Windows 11; `DirectUIHWND` as the item view's focus class.
2. **GlazeWM IPC:** `query workspaces` window fields (`handle`, `title`, `processName`, `hasFocus`), `focus_changed` payload, `focus --container-id`, the PiP window rule syntax, GlazeWM's hide method (cloak vs hide) for thumbnails, and whether the fork can accept omawin-tagged injected keys (Hyper).
3. **Registry values** marked (check) in section C, each round-tripped in CI (1B Testing) before the line ships; `HKCU\Software\Policies` writability for a standard user.
4. **Event log:** Application Error's named fields on current builds; Power-Troubleshooter and Diagnostics-Performance field names; whether Diagnostics-Performance/Operational needs admin.
5. **Package identity:** the sparse package's effect on the menu app's folders, `NotificationChanged` for a desktop app, Dynamic Lighting's background-controller list, the Copilot key's provider list.
6. **Admin-only or not:** `WlanGetProfile` plain key, `Get-StorageReliabilityCounter`, `powercfg /sleepstudy`, `fsutil file setCaseSensitiveInfo`, `BluetoothSetServiceState` on Handsfree.
7. **USN on ReFS** (Dev Drive): `FSCTL_ENUM_USN_DATA` support and V3 records.
8. **Undocumented, with fallbacks:** `SYSTEM_PROCESS_INFORMATION` offsets (x64 and arm64), DPI scaling packets in `DisplayConfigSetDeviceInfo`, the ConsentStore microphone values, pool tags, `IPolicyConfig::SetEndpointVisibility`.

Each first-party plugin's `--selftest` covers what CI can: Restart Manager on a file the test locks itself, an event log query (`Kernel-General` 12 always exists), the TCP table (a listener the test opens), `QueryDisplayConfig` (the runner has one virtual display), `StartupApproved` read/write on a test value, the Scancode Map builder's bytes for Caps→Esc, the Wi-Fi QR escaping, the clipboard transforms, and the update/crash correlation on fixed data.
