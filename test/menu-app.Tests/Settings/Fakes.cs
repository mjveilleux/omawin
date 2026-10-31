namespace SystemMenu.Tests.Settings;

// A Windows that keeps values in a dictionary: what the store's logic (history, undo, the admin
// rule, commands) is tested against. Ignored ids take writes and keep their value, like a setting
// a policy holds; slow ids are read off the calling thread, like a radio.
sealed class FakeWindows : ISettingHandler
{
    public readonly Dictionary<string, string?> Values = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Slow = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Writes = [];

    public string? Read(SettingEntry e) => Values.GetValueOrDefault(e.Id);
    public string? Write(SettingEntry e, string value)
    {
        Writes.Add($"{e.Id}={value}");
        if (!Ignored.Contains(e.Id)) Values[e.Id] = value;
        return null;
    }
    public bool Fast(SettingEntry e) => !Slow.Contains(e.Id);
}

sealed class TempDir : IDisposable
{
    public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omawin-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

static class Fixture
{
    public static readonly string[] Lines =
    [
        @"look.dark-apps | - | Dark mode for apps | dark theme | switch | reg HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme dword on=0 off=1 broadcast=ImmersiveColorSet |",
        @"explorer.open-to | - | File Explorer opens to | home | choice | reg HKCU\A\LaunchTo dword values=""1:This PC,2:Home,3:Downloads"" |",
        @"input.pointer-speed | - | Pointer speed | mouse | slider | spi pointer-speed range=1..20 |",
        @"developer.long-paths | - | Long paths | maxpath | switch | reg HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled dword on=1 off=0 | admin",
        @"developer.vm-platform | - | Virtual Machine Platform | wsl | switch | builtin windows-feature VirtualMachinePlatform | admin restart",
        @"display.night-light | - | Night light | blue | link | link ms-settings:nightlight |",
    ];

    public static List<SettingEntry> Entries() => Catalog.Parse([("settings.txt", Lines)]).Entries;

    public static (SettingStore Store, FakeWindows Windows) Store(TempDir dir)
    {
        var windows = new FakeWindows();
        var entries = Entries();
        var store = new SettingStore(() => entries, _ => windows,
            new SettingHistory(dir.File("settings-history.tsv")), new SettingAdminQueue(dir.File("settings-admin-queue.tsv")));
        windows.Values["look.dark-apps"] = "off";
        windows.Values["explorer.open-to"] = "2";
        windows.Values["input.pointer-speed"] = "10";
        windows.Values["developer.long-paths"] = "off";
        windows.Values["developer.vm-platform"] = "off";
        return (store, windows);
    }
}
