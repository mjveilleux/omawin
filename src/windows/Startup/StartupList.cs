using Microsoft.Win32;

namespace Omawin.Windows;

// Reads what starts with Windows from where Windows keeps it (8.16), and switches an entry the way
// Task Manager does: the StartupApproved value beside it, never deleting the entry itself.
public static class StartupList
{
    const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static List<StartupItem> Read()
    {
        var items = new List<StartupItem>();
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        AddRun(items, hkcu, Run, hkcu, StartupSource.UserRun);
        AddRun(items, hklm, Run, hklm, StartupSource.MachineRun);
        AddRun(items, hklm, Run32, hklm, StartupSource.MachineRun32);
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), hkcu, StartupSource.UserFolder);
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), hklm, StartupSource.MachineFolder);
        AddServices(items, hklm);
        return items;
    }

    static void AddRun(List<StartupItem> items, RegistryKey root, string path, RegistryKey approvedRoot, StartupSource source)
    {
        using var key = root.OpenSubKey(path);
        if (key == null) return;
        using var approved = approvedRoot.OpenSubKey(Approved + "\\" + StartupItems.ApprovedKey(source));
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0) continue; // the key's (Default), never a startup entry
            var command = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            items.Add(new StartupItem(source, name, command, StartupItems.IsEnabled(approved?.GetValue(name) as byte[])));
        }
    }

    static void AddFolder(List<StartupItem> items, string folder, RegistryKey approvedRoot, StartupSource source)
    {
        if (folder.Length == 0 || !Directory.Exists(folder)) return;
        using var approved = approvedRoot.OpenSubKey(Approved + "\\" + StartupItems.ApprovedKey(source));
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new StartupItem(source, name, file, StartupItems.IsEnabled(approved?.GetValue(name) as byte[])));
        }
    }

    // Services set to start automatically that aren't Windows' own (their program outside the
    // Windows folder): the ones a program installed. Off = Manual, never deleted or disabled, so
    // anything that needs one still starts it.
    static void AddServices(List<StartupItem> items, RegistryKey hklm)
    {
        using var root = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root == null) return;
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var s = root.OpenSubKey(name);
                if (s?.GetValue("Type") is not int type || (type & 0x30) == 0) continue; // 0x10 / 0x20: programs, not drivers
                if (s.GetValue("Start") is not int start || start is not (2 or 3)) continue;
                var image = Environment.ExpandEnvironmentVariables(s.GetValue("ImagePath") as string ?? "");
                using var p = s.OpenSubKey("Parameters");
                var file = p?.GetValue("ServiceDll") is string dll ? Environment.ExpandEnvironmentVariables(dll) : StartupItems.Program(image);
                if (file.Length == 0 || file.StartsWith(windir, StringComparison.OrdinalIgnoreCase)) continue;
                // Manual (3) ones are listed only once omawin turned them off (they're "off" here).
                if (start == 3 && !Switched(name)) continue;
                items.Add(new StartupItem(StartupSource.Service, name, image, start == 2));
            }
            catch (System.Security.SecurityException) { } // a few services' keys are closed to users
            catch (UnauthorizedAccessException) { }
        }
    }

    // Services omawin switched to Manual, so `omawin startup` still lists them (to turn back on).
    static string SwitchedFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "startup-services.txt");

    static bool Switched(string service) =>
        File.Exists(SwitchedFile) && File.ReadAllLines(SwitchedFile).Any(l => l.Trim().Equals(service, StringComparison.OrdinalIgnoreCase));

    public static void RememberSwitched(string service, bool off)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SwitchedFile)!);
        var lines = File.Exists(SwitchedFile) ? File.ReadAllLines(SwitchedFile).Where(l => !l.Trim().Equals(service, StringComparison.OrdinalIgnoreCase)).ToList() : [];
        if (off) lines.Add(service);
        File.WriteAllLines(SwitchedFile, lines);
    }

    // Your own entries: the StartupApproved value under HKCU, as Task Manager writes it.
    public static void SetMine(StartupItem item, bool on)
    {
        var sub = StartupItems.ApprovedKey(item.Source) ?? throw new InvalidOperationException("not a switchable entry");
        using var key = Registry.CurrentUser.CreateSubKey(Approved + "\\" + sub);
        key.SetValue(item.Name, on ? StartupItems.Enabled() : StartupItems.Disabled(DateTime.UtcNow), RegistryValueKind.Binary);
    }
}
