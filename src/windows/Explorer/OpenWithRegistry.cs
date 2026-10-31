using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Omawin.Windows;

// OpenWith's plan written to and read from this account's registry (HKCU only, no admin).
public static partial class OpenWithRegistry
{
    public static void Apply(IEnumerable<OpenWith.Value> values)
    {
        foreach (var v in values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(v.Key);
            key.SetValue(v.Name, v.Data, RegistryValueKind.String);
        }
        Changed();
    }

    public static void Remove()
    {
        var (keys, values) = OpenWith.Removal();
        foreach (var v in values)
        {
            using var key = Registry.CurrentUser.OpenSubKey(v.Key, writable: true);
            key?.DeleteValue(v.Name, throwOnMissingValue: false);
        }
        foreach (var k in keys) Registry.CurrentUser.DeleteSubKeyTree(k, throwOnMissingSubKey: false);
        // Software\omawin is only Capabilities' parent: gone too when nothing else is in it.
        using (var omawin = Registry.CurrentUser.OpenSubKey(@"Software\omawin"))
            if (omawin == null || omawin.SubKeyCount > 0 || omawin.ValueCount > 0) { Changed(); return; }
        Registry.CurrentUser.DeleteSubKey(@"Software\omawin", throwOnMissingSubKey: false);
        Changed();
    }

    // Whether omawin is offered for a type (its ProgID is there).
    public static bool Offered(string type)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + OpenWith.ProgId(type));
        return key != null;
    }

    // The ProgID Windows opens a type with for this account, and its name; null when you never
    // picked one. Read only: these keys are Windows' to write.
    public static (string? ProgId, string? Name) Choice(string type)
    {
        string? progId = null;
        foreach (var sub in new[] { "UserChoiceLatest", "UserChoice" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{type}\{sub}");
            if (key?.GetValue("ProgId") is string p && p.Length > 0) { progId = p; break; }
        }
        if (progId == null) return (null, null);
        using var cls = Registry.ClassesRoot.OpenSubKey(progId);
        using var application = Registry.ClassesRoot.OpenSubKey(progId + @"\Application");
        var name = application?.GetValue("ApplicationName") as string ?? cls?.GetValue("") as string;
        return (progId, name);
    }

    public static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo(OpenWith.SettingsPage) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception)
        {
            // An older Windows without the app's own page: the Default apps list.
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
    }

    // Explorer reads the types again (its "Open with" lists and icons).
    static void Changed() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0 /* SHCNF_IDLIST */, 0, 0);

    [LibraryImport("shell32.dll")] private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
