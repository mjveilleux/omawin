using System.Diagnostics;
using Microsoft.Win32;

namespace Omawin.Windows;

// The full right-click menu back (roadmap 8.24): Windows 11's short menu hides most entries behind
// "Show more options". An empty per-user registration of the new menu's COM class makes Explorer
// fall back to the full one; it's per account, needs no admin, and Explorer reads it when it starts.
// Documented in practice rather than by Microsoft, so `check` until it's seen on each new build.
public static class ClassicMenu
{
    const string Clsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    const string Server = Clsid + @"\InprocServer32";

    public static bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Server);
        return key != null && key.GetValue("") is string s && s.Length == 0;
    }

    public static void Set(bool on)
    {
        if (on)
        {
            using var key = Registry.CurrentUser.CreateSubKey(Server);
            key.SetValue("", "", RegistryValueKind.String);
            return;
        }
        // Only what `on` made: InprocServer32, then the class key if nothing else is left in it.
        Registry.CurrentUser.DeleteSubKey(Server, throwOnMissingSubKey: false);
        using (var clsid = Registry.CurrentUser.OpenSubKey(Clsid))
            if (clsid == null || clsid.SubKeyCount > 0 || clsid.ValueCount > 0) return;
        Registry.CurrentUser.DeleteSubKey(Clsid, throwOnMissingSubKey: false);
    }

    // Explorer reads the menu when it starts. Ended, Windows starts it again by itself (the
    // taskbar blinks); the open folder windows close.
    public static void RestartExplorer()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("explorer"))
            using (p)
                try { if (p.SessionId == session) p.Kill(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
