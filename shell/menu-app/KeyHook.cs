// The shell's one low-level keyboard hook (roadmap 8.0: on a key, so core; one hook, never two
// fighting): Caps Lock remapped (8.12), text snippets (8.13) and the Copilot key (8.23). What each
// does with a key is KeyRules.cs (pure, tested); this file is the Win32 around it.
//
// Installed only while one of them is on (input.caps-lock, input.copilot-key in the settings
// catalog; a snippets.txt with lines), on a thread of its own with a message loop, as low-level
// hooks need. Windows drops a hook that's too slow (LowLevelHooksTimeout), and keys wait on it, so
// the callback only looks at the key: files are read when they change, never per key.
//
// Keys omawin sends carry a tag in dwExtraInfo; the hook passes those through untouched. They are
// injected keys, which GlazeWM ignores, so a remap never triggers a GlazeWM binding (why there's
// no Hyper key yet: open-questions/hyper-key.md).

using System.Runtime.InteropServices;

namespace SystemMenu;

public static unsafe partial class KeyHook
{
    const int WH_KEYBOARD_LL = 13, HC_ACTION = 0;
    const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80, WM_QUIT = 0x0012;
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    public const nuint OurTag = 0x6F6D6177; // "omaw"

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int x, y; }

    // INPUT on 64-bit: the type, padding, then the 32-byte union (KEYBDINPUT is 24 of it).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetWindowsHookExW(int id, nint proc, nint module, uint thread);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint thread, uint msg, nuint w, nint l);
    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int vk);
    [LibraryImport("user32.dll")] private static partial short GetKeyState(int vk);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial uint SendInput(uint count, INPUT* inputs, int size);
    [LibraryImport("user32.dll")] private static partial int ToUnicodeEx(uint vk, uint scan, byte* state, char* buffer, int size, uint flags, nint layout);
    [LibraryImport("user32.dll")] private static partial nint GetKeyboardLayout(uint thread);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, nint pid);
    [LibraryImport("user32.dll")] private static partial nint SetWinEventHook(uint min, uint max, nint module, nint proc, uint pid, uint thread, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWinEvent(nint hook);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);

    static readonly CapsLockRemap caps = new();
    static readonly CopilotKey copilot = new();
    static readonly SnippetTyping typing = new();
    static volatile Snippet[] snippets = [];
    static readonly List<SentKey> pending = new(8);
    static nint hook;
    static uint thread;
    static Action? openLauncher;
    static readonly object gate = new();
    static readonly List<FileSystemWatcher> watchers = [];

    public static string Dir => Path.Combine(MenuModel.StateDir, "keys");
    public static string ValueFile(string name) => Path.Combine(Dir, name);
    static string[] SnippetFiles => [Path.Combine(MenuModel.Shared, "snippets.txt"), Path.Combine(MenuModel.Work, "snippets.txt")];

    // Called once at start: reads the settings and the snippets, watches them, and puts the hook
    // in only if something needs it.
    public static void Start(Action launcher)
    {
        openLauncher = launcher;
        Reload();
        Directory.CreateDirectory(Dir);
        Watch(Dir, "*");
        foreach (var file in SnippetFiles)
            if (Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir)) Watch(dir, "snippets.txt");
    }

    static void Watch(string dir, string filter)
    {
        try
        {
            var w = new FileSystemWatcher(dir, filter) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, _) => Reload();
            w.Changed += changed;
            w.Created += changed;
            w.Deleted += changed;
            w.Renamed += (_, _) => Reload();
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { App.Log(ex); }
    }

    // A setting the hook reads ("caps-lock", "copilot-key"): one word in launcher\keys\<name>.
    public static string Read(string name, string otherwise)
    {
        try { return File.Exists(ValueFile(name)) ? File.ReadAllText(ValueFile(name)).Trim() : otherwise; }
        catch (IOException) { return otherwise; }
    }

    public static void Write(string name, string value)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ValueFile(name), value);
        Reload();
    }

    static void Reload()
    {
        lock (gate)
        {
            var mode = Read("caps-lock", "caps");
            if (mode != caps.Mode) caps.Mode = mode;
            copilot.On = Read("copilot-key", "windows") == "launcher";
            try { snippets = [.. Snippets.Parse(SnippetFiles.Where(File.Exists).Select(File.ReadAllLines))]; }
            catch (IOException) { } // being saved: the next change event reads it
            typing.Reset();
            bool needed = caps.Active || copilot.On || snippets.Length > 0;
            if (needed && thread == 0) Install();
            else if (!needed && thread != 0) Remove();
        }
    }

    static void Install()
    {
        var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            thread = GetCurrentThreadId();
            hook = SetWindowsHookExW(WH_KEYBOARD_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&Proc, GetModuleHandleW(null), 0);
            // The window in front changing: what was typed before belongs to another window.
            var foreground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0,
                (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&ForegroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (hook == 0) App.Log(new Exception($"keyboard hook: SetWindowsHookEx failed ({Marshal.GetLastPInvokeError()})"));
            ready.Set();
            MSG msg;
            while (GetMessageW(&msg, 0, 0, 0) > 0) { }
            if (hook != 0) UnhookWindowsHookEx(hook);
            if (foreground != 0) UnhookWinEvent(foreground);
            hook = 0;
        }) { IsBackground = true, Name = "keys", Priority = ThreadPriority.Highest };
        t.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    static void Remove()
    {
        PostThreadMessageW(thread, WM_QUIT, 0, 0);
        thread = 0;
    }

    [UnmanagedCallersOnly]
    static void ForegroundChanged(nint winEvent, uint ev, nint hwnd, int obj, int child, uint eventThread, uint time) => typing.Reset();

    [UnmanagedCallersOnly]
    static nint Proc(int code, nint w, nint l)
    {
        if (code == HC_ACTION)
        {
            var k = (KBDLLHOOKSTRUCT*)l;
            if (k->dwExtraInfo != OurTag)
                try { if (OnKey(k)) return 1; }
                catch (Exception ex) { App.Log(ex); } // never let a mistake here stop the keyboard
        }
        return CallNextHookEx(hook, code, w, l);
    }

    static bool OnKey(KBDLLHOOKSTRUCT* k)
    {
        bool down = (k->flags & LLKHF_UP) == 0;
        pending.Clear();
        bool swallow = caps.OnKey(k->vkCode, down, k->time, pending);
        if (!swallow)
        {
            bool win = GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
            swallow = copilot.OnKey(k->vkCode, down, win, GetAsyncKeyState(0x10) < 0, pending, out bool fire);
            if (fire && openLauncher is { } open) ThreadPool.QueueUserWorkItem(_ => open());
        }
        if (pending.Count > 0) Send(pending);
        if (!swallow && down && (k->flags & LLKHF_INJECTED) == 0 && snippets.Length > 0) swallow = Snippet(k);
        return swallow;
    }

    // The character this key types in the window in front, fed to the snippet buffer; on a
    // trigger, its last key is swallowed and the rest of it erased, then the text typed.
    static bool Snippet(KBDLLHOOKSTRUCT* k)
    {
        uint vk = k->vkCode;
        if (vk == 0x08) { typing.Backspace(); return false; }
        bool ctrl = GetAsyncKeyState(0x11) < 0, alt = GetAsyncKeyState(0x12) < 0;
        bool win = GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
        if (SnippetTyping.Breaks(vk) || win || ctrl && !alt) { typing.Reset(); return false; }
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x14) return false; // modifiers alone

        var state = stackalloc byte[256];
        if (GetAsyncKeyState(0x10) < 0) state[0x10] = 0x80;
        if (ctrl && alt) state[0x11] = state[0x12] = 0x80; // AltGr
        if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 0x01;
        var buffer = stackalloc char[4];
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), 0));
        // Flag 4: leave the keyboard's state alone, so a dead key (^, ¨) still works for you.
        if (ToUnicodeEx(vk, k->scanCode, state, buffer, 4, 0x4, layout) != 1) return false;
        var hit = typing.Feed(buffer[0], snippets);
        if (hit == null) return false;
        var text = Snippets.Expand(hit.Text, DateTime.Now, () => Newest());
        ThreadPool.QueueUserWorkItem(_ => Type(hit.Trigger.Length - 1, text));
        return true;
    }

    static string Newest()
    {
        try { return ClipboardHistory.Entries() is [var f, ..] ? File.ReadAllText(f) : ""; }
        catch (IOException) { return ""; }
    }

    // The trigger's earlier characters erased, then the text: new lines as Enter, tabs as Tab,
    // the rest as Unicode characters (any layout). A window running as admin drops it (Windows
    // keeps a normal program from typing into one).
    static void Type(int erase, string text)
    {
        var keys = new List<INPUT>();
        void Vk(ushort vk) { keys.Add(Key(vk, true)); keys.Add(Key(vk, false)); }
        for (int i = 0; i < erase; i++) Vk(0x08);
        foreach (var c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { Vk(0x0D); continue; }
            if (c == '\t') { Vk(0x09); continue; }
            keys.Add(new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = OurTag } });
            keys.Add(new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, dwExtraInfo = OurTag } });
        }
        var array = keys.ToArray();
        fixed (INPUT* p = array)
            if (SendInput((uint)array.Length, p, sizeof(INPUT)) != array.Length)
                Snip.Say("Snippets can't type into a window running as admin.");
    }

    static void Send(List<SentKey> list)
    {
        var array = new INPUT[list.Count];
        for (int i = 0; i < list.Count; i++) array[i] = Key(list[i].Vk, list[i].Down);
        fixed (INPUT* p = array) SendInput((uint)array.Length, p, sizeof(INPUT));
    }

    static INPUT Key(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (Extended(vk) ? KEYEVENTF_EXTENDEDKEY : 0), dwExtraInfo = OurTag },
    };

    // Keys that share a scan code with the number pad's and are told apart by the extended flag.
    static bool Extended(ushort vk) => vk is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or 0xA3 or 0xA5 or 0x5B or 0x5C or 0x5D or 0x6F or 0x90;
}
