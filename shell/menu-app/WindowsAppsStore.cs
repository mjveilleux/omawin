// `omawin windows-apps [remove [<name> …]]` (roadmap 8.50): the Store side of WindowsApps.cs.
// Windows' PackageManager lists the packages installed for this account and removes one for
// this account only, both without admin (Remove-AppxPackage does the same). Answered by the menu
// app, like `omawin setting`, so it costs no PowerShell start.

using Windows.Management.Deployment;

namespace SystemMenu;

static class WindowsAppsStore
{
    public const string Usage = "omawin windows-apps [remove [<name> …]]";

    // Every removed app, for getting it back: "<when>\t<name>\t<family>" in %LOCALAPPDATA%\launcher.
    static string RemovedLog => Path.Combine(MenuModel.StateDir, "windows-apps-removed.tsv");

    public static SettingCommands.Answer Answer(string[] a, Action<string> progress)
    {
        var (list, problems) = WindowsApps.Parse(MenuModel.Layered("windows-apps.txt").Where(File.Exists).Select(f => (f, File.ReadAllLines(f))));
        foreach (var p in problems) App.Log(new FormatException($"{p.Origin}: {p.Message}"));
        var installed = Installed();

        if (a is [] or ["list"]) return new(0, WindowsApps.Format(installed, list));
        if (a[0] != "remove") return new(2, Usage);

        var names = a.Skip(1).Where(x => x != "--yes").ToList();
        var todo = new List<(AppPackage App, string? Why)>();
        if (names.Count == 0)
            todo.AddRange(WindowsApps.Suggested(installed, list).Select(s => (s.App, (string?)s.Why.Why)));
        else
            foreach (var name in names)
            {
                var (app, ambiguous) = WindowsApps.Match(installed, name);
                if (app == null)
                    return new(1, ambiguous.Count > 0
                        ? $"'{name}' could be: {string.Join(", ", ambiguous.Select(x => x.Name))}"
                        : $"no removable app '{name}' for this account (omawin windows-apps lists them)");
                if (todo.All(t => t.App.FullName != app.FullName)) todo.Add((app, null));
            }
        if (todo.Count == 0) return new(0, "None of the apps omawin suggests removing is installed for you (omawin windows-apps lists the rest)");
        // Always asked: a list first (omawin shows it and asks y/N), then the removal on yes.
        if (!a.Contains("--yes")) return new(3, WindowsApps.Confirm(todo));

        var manager = new PackageManager();
        var failed = new List<string>();
        int removed = 0;
        foreach (var (app, _) in todo)
        {
            progress($"removing {app.DisplayName}…");
            try
            {
                var result = manager.RemovePackageAsync(app.FullName).GetAwaiter().GetResult();
                if (result.ExtendedErrorCode != null) { failed.Add($"{app.DisplayName}: {result.ErrorText}"); continue; }
                removed++;
                Directory.CreateDirectory(MenuModel.StateDir);
                File.AppendAllText(RemovedLog, $"{DateTimeOffset.UtcNow:yyyy-MM-dd}\t{app.Name}\t{app.Family}\n");
            }
            catch (Exception ex) { failed.Add($"{app.DisplayName}: {ex.Message}"); }
        }
        var said = $"Removed {removed} app{(removed == 1 ? "" : "s")} for your account; each comes back from the Store (its page: ms-windows-store://pdp/?PFN=<family>, listed in {RemovedLog}).";
        return failed.Count == 0 ? new(0, said) : new(1, said + "\nNot removed:\n  " + string.Join("\n  ", failed));
    }

    // The packages installed for this account, with each removable one's size (its folder, read
    // side by side: a few seconds at most).
    static List<AppPackage> Installed()
    {
        var found = new List<(AppPackage App, string? Path)>();
        foreach (var p in new PackageManager().FindPackagesForUser(""))
        {
            try
            {
                string? path = null;
                try { path = p.InstalledLocation?.Path; } catch (Exception) { } // being updated or removed right now
                var display = "";
                try { display = p.DisplayName; } catch (Exception) { }
                found.Add((new AppPackage(p.Id.Name, p.Id.FamilyName, p.Id.FullName, display.Length > 0 ? display : p.Id.Name,
                    p.IsFramework, p.IsResourcePackage, p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System, -1), path));
            }
            catch (Exception ex) { App.Log(ex); }
        }
        var sized = new AppPackage[found.Count];
        Parallel.For(0, found.Count, i =>
        {
            var (app, path) = found[i];
            sized[i] = WindowsApps.Removable(app) && path != null ? app with { Bytes = FolderSize(path) } : app;
        });
        return [.. sized];
    }

    static long FolderSize(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return -1; }
    }
}
