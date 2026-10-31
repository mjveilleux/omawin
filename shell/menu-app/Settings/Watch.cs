// A setting changed somewhere else (Windows' own Settings, a script, another account's policy):
// the row shows it at once (plans/02-settings-and-one-search.md §7). Only entries that have been
// shown are watched (the store calls Ensure the first time it reads one), so the handles stay at
// the few dozen keys someone looked at.
//   - registry: one RegNotifyChangeKeyValue per key, thread-agnostic, re-armed after each signal;
//   - SystemParametersInfo: the menu app's own top-level window hears WM_SETTINGCHANGE;
//   - power settings: no event worth wiring; the window re-reads them when it's shown.

using Microsoft.Win32;

namespace SystemMenu;

static class SettingWatch
{
    sealed class KeyWatch(RegistryKey key, bool subtree)
    {
        public readonly RegistryKey Key = key;
        public readonly bool Subtree = subtree;
        public readonly AutoResetEvent Signal = new(false);
        public readonly List<SettingEntry> Entries = [];
    }

    static readonly Dictionary<string, KeyWatch> keys = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> watched = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();
    static SettingStore? store;

    public static void Ensure(SettingStore owner, SettingEntry e)
    {
        lock (gate)
        {
            store = owner;
            if (!watched.Add(e.Id)) return;
            try
            {
                if (e.How.Verb == "reg") foreach (var part in e.Parts) WatchKey(e, part.How.Target);
                else if (e.How.Verb == "builtin" && e.How.Target == "proxy")
                    WatchKey(e, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyEnable");
                else if (e.How.Verb == "builtin" && e.How.Target == "desktop-icons")
                    WatchKey(e, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideIcons");
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    static void WatchKey(SettingEntry e, string target)
    {
        var (hive, path, _) = Catalog.SplitReg(target);
        // A key that doesn't exist yet (never set) can't be watched: its nearest parent that
        // does is, with its subtree, so the key's creation is seen too.
        string at = path;
        bool subtree = false;
        RegistryKey? key;
        while ((key = RegHandler.Root(hive).OpenSubKey(at)) == null)
        {
            int cut = at.LastIndexOf('\\');
            if (cut < 0) return;
            at = at[..cut];
            subtree = true;
        }
        var name = hive + "\\" + at + (subtree ? "\\*" : "");
        if (keys.TryGetValue(name, out var existing)) { existing.Entries.Add(e); key.Dispose(); return; }
        var w = new KeyWatch(key, subtree);
        w.Entries.Add(e);
        keys[name] = w;
        Arm(w);
        ThreadPool.RegisterWaitForSingleObject(w.Signal, (_, _) =>
        {
            Arm(w); // first, so a change made while the entries are read again isn't missed
            List<SettingEntry> list;
            lock (gate) list = [.. w.Entries];
            foreach (var x in list) store?.Invalidate(x);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    static void Arm(KeyWatch w) =>
        SettingsNative.RegNotifyChangeKeyValue(w.Key.Handle.DangerousGetHandle(), w.Subtree,
            SettingsNative.REG_NOTIFY_CHANGE_NAME | SettingsNative.REG_NOTIFY_CHANGE_LAST_SET | SettingsNative.REG_NOTIFY_THREAD_AGNOSTIC,
            w.Signal.SafeWaitHandle.DangerousGetHandle(), asynchronous: true);

    // --- WM_SETTINGCHANGE on the menu app's window ---------------------------------------------
    // Top-level windows get the broadcast (message-only ones don't), and the menu's hidden window
    // is always there. Once per window.
    static bool hooked;

    public static unsafe void HookWindow(nint hwnd)
    {
        if (hooked) return;
        hooked = SettingsNative.SetWindowSubclass(hwnd, &SubclassProc, 0x5E77, 0);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == SettingsNative.WM_SETTINGCHANGE && store is { } s)
        {
            // Rather than map each SPI_SET action to its entry, read every spi entry that's been
            // shown again: a handful, microseconds each. Display changes (refresh rate, HDR)
            // arrive here too, as a broadcast with no area.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<SettingEntry> again;
                lock (gate) again = s.Entries().Where(e => watched.Contains(e.Id) && (e.How.Verb == "spi" || e.How.Target is "refresh-rate" or "hdr")).ToList();
                foreach (var e in again) s.Invalidate(e);
            });
        }
        return SettingsNative.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
