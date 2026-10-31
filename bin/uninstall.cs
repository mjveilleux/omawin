// omawin-uninstall: `omawin uninstall` (roadmap 2.10), all of omawin out of this Windows account,
// what docs/install.md's "Undoing it" lists by hand:
//
//   omawin uninstall              shows everything it will do, lets you untick what to keep, asks once
//   omawin uninstall --dry-run    shows it and stops
//   omawin uninstall --yes        no questions: everything ticked by default
//
// It stops the desktop, removes the sign-in tasks, takes back each config it placed (a link, a stub,
// a copy) and puts back what you had there before (the <path>.backup-<timestamp> copies install
// made), removes its PATH entries and environment variables, gives back the taskbar and the
// desktop's icons, and removes omawin's own programs. The programs it put in for you (the tools
// bundle, the font) are listed and ticked; programs winget has (omawin may have put them in, or
// you may have) are listed unticked; anything else (your account folder, the shared folder, your
// Linux in WSL, Neovim's plugins, settings apps keep themselves) stays, and the summary says so.
//
// No admin (roadmap 2.9): what only admin can undo (omawin's GlazeWM in Program Files, a program
// installed for the whole PC) is said, with the command, and left. A file in use (the terminal
// you ran this in, this program itself) is removed once it's free, or at your next sign-in.
//
// A small native exe like omawin.exe (build/helpers.ps1 compiles it with Windows' own csc.exe:
// C# 5, no newer syntax). The rules with no Windows in them (UninstallRules) are unit-tested on
// Linux (test/cli.Tests/UninstallRulesTests.cs, which builds this file with OMAWIN_TESTS).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
#if !OMAWIN_TESTS
using Microsoft.Win32;
#endif

// What it decides without touching Windows: which backup to put back, which PATH entries are
// omawin's, which files are its stubs, what packages.tsv says, and how a typed choice reads.
public static class UninstallRules
{
    // The newest of install's copies of $path (<path>.backup-<yyyyMMdd-HHmmss>, Move-Aside in
    // install/lib.ps1) among $siblings, or null. The stamp sorts as text.
    public static string LatestBackup(string path, IEnumerable<string> siblings)
    {
        var prefix = Leaf(path) + ".backup-";
        return siblings
            .Where(s => Leaf(s).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => Leaf(s), StringComparer.OrdinalIgnoreCase)
            .LastOrDefault();
    }

    // The last part of a Windows path (either slash), whatever system this runs on.
    public static string Leaf(string path)
    {
        var p = path.TrimEnd('\\', '/');
        return p.Substring(Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/')) + 1);
    }

    // Whether $path is $folder or inside it (Windows paths: either slash, any case).
    public static bool IsUnder(string path, string folder)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
        var p = Normalize(path);
        var f = Normalize(folder);
        return p == f || p.StartsWith(f + "\\", StringComparison.Ordinal);
    }
    static string Normalize(string path)
    {
        return path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }

    // The user PATH without the entries $ours says are omawin's, the rest as they were (raw:
    // %VARIABLES% unexpanded). $expand turns an entry into the path it means.
    public static string WithoutEntries(string raw, Func<string, string> expand, Func<string, bool> ours, out int removed)
    {
        var kept = new List<string>();
        removed = 0;
        foreach (var entry in (raw ?? "").Split(';'))
        {
            if (entry.Trim().Length == 0) continue;
            if (ours(expand(entry))) { removed++; continue; }
            kept.Add(entry);
        }
        return string.Join(";", kept);
    }

    // A small file install wrote (Set-FileContent in install/lib.ps1: the PowerShell profile stubs,
    // ~/.bashrc, noctty's config): its first line says it's omawin's.
    public static bool IsOurStub(string content)
    {
        if (content == null) return false;
        var text = content.TrimStart('\uFEFF');
        return text.StartsWith("# Managed by omawin", StringComparison.Ordinal) || text.StartsWith("# omawin:", StringComparison.Ordinal);
    }

    // The one-line ~/.Rprofile install writes (install/software.ps1): source('<repo>/config/r/rprofile.R').
    public static bool IsOurRprofile(string content)
    {
        if (content == null) return false;
        var text = content.Trim();
        return text.StartsWith("source(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal)
            && text.IndexOf("config/r/rprofile.R", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf('\n') < 0;
    }

    public class Package
    {
        public string Name, Source, Id, Version, Where;
    }

    // %LOCALAPPDATA%\launcher\packages.tsv (install/lib.ps1, Set-PackageSource): name, source, id,
    // version, folder; comments and blank lines skipped, a short row's missing fields empty.
    public static List<Package> ParsePackages(IEnumerable<string> lines)
    {
        var rows = new List<Package>();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
            var f = line.Split('\t');
            if (f.Length < 2 || f[0].Trim().Length == 0) continue;
            rows.Add(new Package
            {
                Name = f[0].Trim(), Source = f[1].Trim(),
                Id = f.Length > 2 ? f[2].Trim() : "", Version = f.Length > 3 ? f[3].Trim() : "", Where = f.Length > 4 ? f[4].Trim() : ""
            });
        }
        return rows;
    }

    // An answer to "numbers flip a choice": "3 7", "3,7", "2-5", "all", "none". Returns the ticks
    // after it, or null with $error said (a number out of range, a word it doesn't know).
    public static bool[] ApplyChoice(bool[] ticks, string answer, out string error)
    {
        error = null;
        var next = (bool[])ticks.Clone();
        var text = (answer ?? "").Trim().ToLowerInvariant();
        if (text == "all") { for (int i = 0; i < next.Length; i++) next[i] = true; return next; }
        if (text == "none") { for (int i = 0; i < next.Length; i++) next[i] = false; return next; }
        foreach (var word in text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int from, to;
            var dash = word.IndexOf('-');
            bool ok = dash > 0
                ? int.TryParse(word.Substring(0, dash), out from) & int.TryParse(word.Substring(dash + 1), out to)
                : int.TryParse(word, out from) & int.TryParse(word, out to);
            if (!ok || from < 1 || to > next.Length || from > to)
            {
                error = "'" + word + "' isn't a number from 1 to " + next.Length;
                return null;
            }
            for (int i = from; i <= to; i++) next[i - 1] = !next[i - 1];
        }
        return next;
    }

    // A git value_regex is avoided (paths hold regex characters): git config --fixed-value.
    // Whether an include.path or core.excludesFile value points into the omawin checkout.
    public static bool PointsInto(string gitValue, string repo)
    {
        return IsUnder((gitValue ?? "").Replace('/', '\\'), repo);
    }
}

#if !OMAWIN_TESTS
static class Uninstall
{
    // One thing it will do. Choice: shown with a number and a tick you can flip.
    class Item
    {
        public string Group, What, Note;
        public bool Choice, Ticked;
        public Action Do;
    }

    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string Docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    static readonly string Repo = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTFILES"))
        ? Path.Combine(Home, "omawin") : Environment.GetEnvironmentVariable("DOTFILES");
    static readonly string StateDir = Path.Combine(Local, "launcher");
    static readonly string Tools = Path.Combine(Local, @"omawin\tools");
    static readonly string Bin = Path.Combine(Home, @".local\bin");
    static readonly string User = Environment.UserName;
    static readonly int Session = Process.GetCurrentProcess().SessionId;

    static readonly List<Item> Items = new List<Item>();
    static readonly List<string> Left = new List<string>();        // what stays, and why
    static readonly List<string> Later = new List<string>();       // paths in use: removed once free
    static readonly List<string> Failed = new List<string>();
    static bool DryRun;

    static int Main(string[] args)
    {
        bool yes = false;
        foreach (var a in args)
        {
            if (a == "--yes" || a == "-y") yes = true;
            else if (a == "--dry-run" || a == "-n") DryRun = true;
            else if (a == "--help" || a == "-h" || a == "/?")
            {
                Console.WriteLine("omawin uninstall [--dry-run] [--yes]: all of omawin out of this account (it shows what first).");
                return 0;
            }
            else { Console.Error.WriteLine("omawin uninstall: unknown option " + a + " (--dry-run, --yes)"); return 2; }
        }

        Plan();
        Show();
        if (DryRun) { Console.WriteLine(); Console.WriteLine("(--dry-run: nothing was changed)"); return 0; }
        if (!yes)
        {
            if (Console.IsInputRedirected) { Console.WriteLine(); Console.WriteLine("Nobody to ask: run omawin uninstall --yes to go ahead."); return 1; }
            if (!Ask()) { Console.WriteLine("Nothing was changed."); return 1; }
        }

        Console.WriteLine();
        foreach (var item in Items)
        {
            if (item.Choice && !item.Ticked) continue;
            try { item.Do(); Say("  done  ", item.What); }
            catch (Exception ex) { Failed.Add(item.What + ": " + ex.Message); Say("  !     ", item.What + ": " + ex.Message); }
        }
        ScheduleLater();
        Summary();
        return Failed.Count == 0 ? 0 : 1;
    }

    static void Say(string mark, string text) { Console.WriteLine(mark + text); }

    // --- the plan -------------------------------------------------------------------------------
    static void Add(string group, string what, Action action) { Items.Add(new Item { Group = group, What = what, Do = action }); }
    static void AddChoice(string group, string what, bool ticked, string note, Action action)
    {
        Items.Add(new Item { Group = group, What = what, Choice = true, Ticked = ticked, Note = note, Do = action });
    }

    static void Plan()
    {
        // The desktop first: GlazeWM shows every window it hid on another workspace as it exits.
        Add("The desktop", "stop GlazeWM, the bar and the menu app, the music player", StopDesktop);
        foreach (var task in new[] { "omawin GlazeWM (" + User + ")", "omawin lock screen (" + User + ")" })
        {
            var name = task;
            if (Run("schtasks.exe", "/Query /TN " + Quote(name), 10000) == 0)
                Add("The desktop", "remove the scheduled task '" + name + "'", delegate { Must(Run("schtasks.exe", "/Delete /F /TN " + Quote(name), 15000) == 0, "schtasks refused"); });
        }
        var fork = Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", @"omawin\glazewm-fork.txt");
        if (File.Exists(fork))
            Left.Add("omawin's GlazeWM for the whole PC (Program Files): it takes admin; run omawin glazewm admin-off before this, or it stays");

        PlanSettings();
        PlanLook();
        PlanPrograms();
        PlanData(File.Exists(fork));

        Left.Add("your account folder (" + Path.Combine(Home, "omawin-account") + ") and the PC's shared folder: yours");
        Left.Add("Neovim's plugins and your lockfile (" + Path.Combine(Local, "nvim-data") + "), Windows Terminal's and Claude Code's settings, Handy's: kept by those apps");
        Left.Add("your Linux in WSL, if you have one: wsl --unregister <name> removes it and everything in it");
        Left.Add("Windows' ads and suggestions stay off, the wallpaper and the lock screen picture stay: each is Windows' own setting");
    }

    static void PlanSettings()
    {
        const string S = "Your settings";
        // Folder links into the checkout (Set-Junction): the link goes, your folder from before comes back.
        foreach (var link in new[] { Path.Combine(Local, "nvim"), Path.Combine(Roaming, "neovide"), Path.Combine(Home, @".config\fastfetch"), Path.Combine(Docs, "PowerShell") })
        {
            var path = link;
            if (!IsLink(path)) continue;
            Add(S, "remove the link " + path + Restores(path), delegate { Directory.Delete(path, false); PutBack(path); });
        }
        // Small files of omawin's (Set-FileContent), its copies, and what it renders.
        var stubs = new[]
        {
            Path.Combine(Docs, @"PowerShell\profile.ps1"), Path.Combine(Docs, @"PowerShell\Microsoft.PowerShell_profile.ps1"),
            Path.Combine(Local, @"noctty\config.ghostty"), Path.Combine(Home, ".bashrc")
        };
        foreach (var stub in stubs)
        {
            var path = stub;
            if (IsLink(Path.GetDirectoryName(path))) continue;   // an old install's link into the checkout: the link goes, above
            if (File.Exists(path) && UninstallRules.IsOurStub(SafeRead(path))) Add(S, "remove " + path + Restores(path), delegate { RemoveFile(path); });
        }
        var rprofile = Path.Combine(Docs, ".Rprofile");
        if (File.Exists(rprofile) && UninstallRules.IsOurRprofile(SafeRead(rprofile))) Add(S, "remove " + rprofile + Restores(rprofile), delegate { RemoveFile(rprofile); });
        // Copies: removed only while they're still omawin's file unchanged (a copy you edited is yours).
        var copies = new[]
        {
            new[] { Path.Combine(Home, ".bash_profile"), Path.Combine(Repo, @"config\bash\bash_profile") },
            new[] { Path.Combine(Home, ".wslconfig"), Path.Combine(Repo, @"wsl\.wslconfig") },
            new[] { Path.Combine(Home, ".wslconfig"), Path.Combine(Home, @"omawin-account\wsl\.wslconfig") },
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in copies)
        {
            var path = pair[0];
            if (seen.Contains(path) || !File.Exists(path) || !File.Exists(pair[1]) || SafeRead(path) != SafeRead(pair[1])) continue;
            seen.Add(path);
            Add(S, "remove " + path + " (omawin's copy)" + Restores(path), delegate { RemoveFile(path); });
        }
        var glaze = Path.Combine(Home, @".glzr\glazewm\config.yaml");
        if (File.Exists(glaze)) Add(S, "remove GlazeWM's config, rendered from omawin's (" + glaze + ")" + Restores(glaze), delegate { RemoveFile(glaze); });
        var fragment = Path.Combine(Local, @"Microsoft\Windows Terminal\Fragments\omawin");
        if (Directory.Exists(fragment)) Add(S, "remove Windows Terminal's omawin color scheme (" + fragment + ")", delegate { RemoveTree(fragment); });

        // The file types omawin's viewer was offered for (omawin open-with, roadmap 8.25): only
        // omawin's own keys and values (the types' keys stay; Windows' choice is Windows').
        using (var app = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\omawin-view.exe"))
            if (app != null) Add(S, "stop offering omawin's viewer for pictures and text (omawin open-with)", RemoveOpenWith);

        // Environment variables that point into the checkout, and this account's GlazeWM port.
        foreach (var name in new[] { "STARSHIP_CONFIG", "HERDR_CONFIG_PATH", "GLAZEWM_IPC_PORT" })
        {
            var variable = name;
            var value = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
            if (string.IsNullOrEmpty(value)) continue;
            if (variable != "GLAZEWM_IPC_PORT" && !UninstallRules.IsUnder(value, Repo)) continue;
            Add(S, "remove the variable " + variable, delegate { Environment.SetEnvironmentVariable(variable, null, EnvironmentVariableTarget.User); });
        }

        // git: the include of omawin's gitconfig and its ignore file (your own include of your
        // account folder's gitconfig stays, with your name and email in it).
        var git = Which("git.exe");
        if (git != null)
        {
            foreach (var include in Lines(Capture(git, "config --global --get-all include.path")))
            {
                var value = include;
                if (UninstallRules.PointsInto(value, Repo))
                    Add(S, "~/.gitconfig: stop including " + value, delegate { Must(Run(git, "config --global --fixed-value --unset include.path " + Quote(value), 15000) == 0, "git config refused"); });
            }
            var excludes = Capture(git, "config --global --get core.excludesFile").Trim();
            if (UninstallRules.PointsInto(excludes, Repo))
                Add(S, "~/.gitconfig: core.excludesFile back to git's own", delegate { Must(Run(git, "config --global --unset core.excludesFile", 15000) == 0, "git config refused"); });
        }

        // The PATH: omawin's folders off it (~/.local/bin, the tools bundle's folders).
        int count;
        UninstallRules.WithoutEntries(ReadUserPath(), Environment.ExpandEnvironmentVariables, IsOursOnPath, out count);
        if (count > 0) Add(S, "take " + count + " of omawin's folders off your PATH", delegate
        {
            int removed;
            WriteUserPath(UninstallRules.WithoutEntries(ReadUserPath(), Environment.ExpandEnvironmentVariables, IsOursOnPath, out removed));
        });
    }

    // ~/.local/bin only while nothing but omawin's is in it: other tools put theirs there too (uv's).
    static readonly string[] OurExes = { "omawin.exe", "omawin-uninstall.exe", "voice-meter.exe", "omawin-listen.exe", "omawin-music.exe", "omawin-view.exe" };
    static bool IsOursOnPath(string path)
    {
        if (UninstallRules.IsUnder(path, Path.Combine(Local, "omawin"))) return true;
        if (!UninstallRules.IsUnder(path, Bin) || !Directory.Exists(Bin)) return false;
        return Directory.GetFileSystemEntries(Bin).All(f => OurExes.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(f).EndsWith(".old", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).IndexOf(".exe.old-", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // What omawin open-with wrote (src/windows/Explorer/OpenWith.cs Plan), undone.
    static void RemoveOpenWith()
    {
        const string Prefix = "omawin.view.";
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
        {
            if (classes == null) return;
            foreach (var name in classes.GetSubKeyNames().Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                classes.DeleteSubKeyTree(name, false);
                using (var offered = classes.OpenSubKey("." + name.Substring(Prefix.Length) + @"\OpenWithProgids", true))
                    if (offered != null) offered.DeleteValue(name, false);
            }
            classes.DeleteSubKeyTree(@"Applications\omawin-view.exe", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\omawin\Capabilities", false);
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            if (registered != null) registered.DeleteValue("omawin", false);
        using (var omawin = Registry.CurrentUser.OpenSubKey(@"Software\omawin"))
            if (omawin == null || omawin.SubKeyCount > 0 || omawin.ValueCount > 0) return;
        Registry.CurrentUser.DeleteSubKey(@"Software\omawin", false);
    }

    static void PlanLook()
    {
        const string L = "Windows' look";
        if (TaskbarHides()) Add(L, "the taskbar stays up again (no auto-hide)", delegate { SetTaskbarHides(false); });
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel"))
        {
            var value = key == null ? null : key.GetValue(RecycleBin);
            if (value is int && (int)value == 1) Add(L, "the Recycle Bin back on the desktop", ShowRecycleBin);
        }
        if (DesktopIcons.Hidden()) Add(L, "the desktop's icons shown again", delegate { DesktopIcons.Show(); });
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"))
        {
            if (key != null && key.GetValue("StartupDelayInMSec") != null)
                Add(L, "Windows' own start-up delay back for the programs in its Run list", delegate
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", true))
                    {
                        if (k == null) return;
                        k.DeleteValue("StartupDelayInMSec", false);
                        k.DeleteValue("WaitForIdleState", false);
                    }
                });
        }
    }

    static void PlanPrograms()
    {
        const string O = "omawin itself";
        // Each exe, and an older copy an update moved aside while it ran (<name>.old, <name>.old-<time>).
        var mine = !Directory.Exists(Bin) ? new List<string>() : Directory.GetFiles(Bin)
            .Where(f => OurExes.Any(n => Path.GetFileName(f).Equals(n, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith(n + ".old", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (mine.Count > 0) Add(O, "remove omawin.exe and its helpers from " + Bin, delegate
        {
            foreach (var f in mine) RemoveFile(f, false);
            TryRemoveEmpty(Bin);
        });
        var menu = Path.Combine(Local, "system-menu");
        if (Directory.Exists(menu)) Add(O, "remove the menu app (" + menu + ")", delegate { RemoveTree(menu); });
        var noctty = Path.Combine(Local, @"Programs\noctty");
        if (Directory.Exists(noctty)) Add(O, "remove noctty, the terminal (" + noctty + ")", delegate { RemoveTree(noctty); });

        // What install put in for you, from where it says it came from.
        const string P = "Programs omawin put in";
        var rows = UninstallRules.ParsePackages(SafeLines(Path.Combine(StateDir, "packages.tsv")));
        foreach (var row in rows.Where(r => r.Source == "bundle"))
        {
            var r = row;
            var dir = string.IsNullOrEmpty(r.Where) ? Path.Combine(Tools, r.Name) : r.Where;
            if (!Directory.Exists(dir)) continue;
            // Only its own folder: one unpacked into a folder of yours (Neovim's parsers, merged into
            // nvim-data) stays with that folder.
            if (!UninstallRules.IsUnder(dir, Path.Combine(Local, "omawin"))) continue;
            if (UninstallRules.IsUnder(dir, Path.Combine(Local, @"omawin\fonts")))
            {
                AddChoice(P, "the " + r.Name + " Nerd Font, for this account", true, "the tools bundle", delegate { RemoveFonts(dir); });
                continue;
            }
            AddChoice(P, r.Name + (r.Version.Length > 0 ? " " + r.Version : ""), true, "the tools bundle, " + dir, delegate { RemoveTree(dir); });
        }
        // winget's: omawin may have put them in, or you had them: you tick what goes.
        var winget = Which("winget.exe");
        foreach (var row in rows.Where(r => r.Source == "winget"))
        {
            var r = row;
            if (winget == null) { Left.Add(r.Id + " (winget): no winget here to remove it with"); continue; }
            AddChoice(P, r.Id, false, "winget: yours or omawin's", delegate
            {
                // --scope user: a whole-PC install would raise an admin prompt, and this asks for none.
                var code = Run(winget, "uninstall --id " + Quote(r.Id) + " --exact --silent --scope user --disable-interactivity --accept-source-agreements", 600000);
                Must(code == 0, "winget couldn't (exit " + code + "; installed for the whole PC? winget uninstall --id " + r.Id + " from an admin terminal)");
            });
        }
        foreach (var row in rows.Where(r => r.Source != "bundle" && r.Source != "winget" && r.Source != "found"))
            Left.Add(row.Name + " (" + row.Source + "): " + HowToRemove(row));
    }

    static string HowToRemove(UninstallRules.Package row)
    {
        switch (row.Source)
        {
            case "scoop": return "scoop uninstall " + row.Id;
            case "choco": return "choco uninstall " + row.Id + " (admin)";
            case "mise": return "mise uninstall " + row.Id;
            case "npm": return "npm uninstall -g " + row.Id;
            default: return "its own installer put it in; remove it its own way";
        }
    }

    static void PlanData(bool forkIn)
    {
        const string D = "omawin's data";
        if (Directory.Exists(StateDir))
            AddChoice(D, "omawin's data: clipboard and launch history, themes, settings (" + StateDir + ")", true, null, delegate { RemoveTree(StateDir); });
        var bundle = Path.Combine(Local, "omawin");
        AddFinal(bundle);
        if (Directory.Exists(Path.Combine(Repo, ".git")))
        {
            string why = null;
            var git = Which("git.exe");
            if (forkIn) why = "kept: omawin glazewm admin-off needs it";
            else if (git != null && Capture(git, "-C " + Quote(Repo) + " status --porcelain").Trim().Length > 0) why = "unticked: it has changes of yours";
            else if (git != null && Capture(git, "-C " + Quote(Repo) + " log --oneline @{upstream}..HEAD").Trim().Length > 0) why = "unticked: it has commits not pushed";
            AddChoice(D, "the omawin checkout (" + Repo + ")", why == null, why, delegate { RemoveTree(Repo); });
        }
    }

    // %LOCALAPPDATA%\omawin, once every tool in it went (a tool you kept keeps it).
    static void AddFinal(string bundle)
    {
        if (!Directory.Exists(bundle)) return;
        Add("omawin's data", "remove " + bundle + " when nothing you kept is in it", delegate
        {
            foreach (var d in new[] { Tools, Path.Combine(bundle, "fonts") }) TryRemoveEmpty(d);
            if (Directory.Exists(bundle) && Directory.GetDirectories(bundle).Length == 0) RemoveTree(bundle);
        });
    }

    // --- showing it and asking --------------------------------------------------------------------
    static void Show()
    {
        Console.WriteLine("omawin uninstall: everything omawin set up in this account (" + User + ")");
        string group = null;
        int n = 0;
        foreach (var item in Items)
        {
            if (item.Group != group) { group = item.Group; Console.WriteLine(); Console.WriteLine(group); }
            if (item.Choice)
            {
                n++;
                Console.WriteLine("  [" + (item.Ticked ? "x" : " ") + "] " + n.ToString().PadLeft(2) + "  " + item.What + (item.Note != null ? "  (" + item.Note + ")" : ""));
            }
            else Console.WriteLine("        " + item.What);
        }
        if (Left.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Left as it is");
            foreach (var l in Left) Console.WriteLine("        " + l);
        }
    }

    static bool Ask()
    {
        var choices = Items.Where(i => i.Choice).ToList();
        while (true)
        {
            Console.WriteLine();
            Console.Write(choices.Count > 0
                ? "Numbers flip a tick (3 7, 2-5, all, none); Enter uninstalls; q stops: "
                : "Enter uninstalls; q stops: ");
            var answer = Console.ReadLine();
            if (answer == null) return false;
            answer = answer.Trim();
            if (answer.Length == 0) return true;
            if (answer.Equals("q", StringComparison.OrdinalIgnoreCase) || answer.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
            string error;
            var ticks = UninstallRules.ApplyChoice(choices.Select(c => c.Ticked).ToArray(), answer, out error);
            if (ticks == null) { Console.WriteLine("  " + error); continue; }
            for (int i = 0; i < ticks.Length; i++) choices[i].Ticked = ticks[i];
            Show();
        }
    }

    static void Summary()
    {
        Console.WriteLine();
        if (Later.Count > 0)
        {
            Console.WriteLine("In use, so removed once free (or at your next sign-in):");
            foreach (var l in Later) Console.WriteLine("        " + l);
        }
        if (Failed.Count > 0)
        {
            Console.WriteLine("Didn't go:");
            foreach (var f in Failed) Console.WriteLine("  !     " + f);
        }
        Console.WriteLine(Failed.Count == 0 ? "omawin is uninstalled from this account. Sign out and back in for a clean start." : "The rest of omawin is uninstalled; the lines above say what's left.");
    }

    // --- doing it ---------------------------------------------------------------------------------
    static void Must(bool ok, string why) { if (!ok) throw new Exception(why); }

    static void StopDesktop()
    {
        // GlazeWM's own exit first: it shows the windows it hid on other workspaces (cloaked), which a
        // plain kill would leave hidden.
        if (InSession("glazewm").Length > 0)
        {
            foreach (var cli in new[] { Path.Combine(Tools, @"glazewm\cli\glazewm.exe"), Path.Combine(ProgramFiles(), @"glzr.io\GlazeWM\cli\glazewm.exe") })
            {
                if (!File.Exists(cli)) continue;
                Run(cli, "command wm-exit", 5000);
                break;
            }
            var waited = Stopwatch.StartNew();
            while (InSession("glazewm").Length > 0 && waited.ElapsedMilliseconds < 5000) Thread.Sleep(100);
        }
        // Not your terminals (noctty): what runs in them is yours. Its folder goes once they're closed.
        var mine = Ancestors();
        foreach (var name in new[] { "glazewm", "glazewm-watcher", "SystemMenu", "omawin-music", "voice-meter", "omawin-listen" })
            foreach (var p in InSession(name))
            {
                if (mine.Contains(p.Id)) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
            }
    }

    static Process[] InSession(string name)
    {
        return Process.GetProcessesByName(name).Where(p => { try { return p.SessionId == Session; } catch (Exception) { return false; } }).ToArray();
    }

    // Puts back the newest backup install made of $path, if there is one and $path is free.
    static void PutBack(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return;
        var parent = Path.GetDirectoryName(path);
        if (parent == null || !Directory.Exists(parent)) return;
        var backup = UninstallRules.LatestBackup(path, Directory.GetFileSystemEntries(parent));
        if (backup == null) return;
        if (Directory.Exists(backup)) Directory.Move(backup, path); else File.Move(backup, path);
    }
    static string Restores(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent == null || !Directory.Exists(parent)) return "";
        var backup = UninstallRules.LatestBackup(path, Directory.GetFileSystemEntries(parent));
        return backup == null ? "" : "; yours from before back (" + Path.GetFileName(backup) + ")";
    }

    static void RemoveFile(string path) { RemoveFile(path, true); }
    static void RemoveFile(string path, bool putBack)
    {
        try { File.Delete(path); }
        catch (IOException) { Later.Add(path); return; }
        catch (UnauthorizedAccessException) { Later.Add(path); return; }
        if (putBack) PutBack(path);
    }

    // A folder and everything in it, never following a link out of it (Directory.Delete would go
    // into a junction's target). A file in use leaves the folder for later.
    static void RemoveTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (!DeleteTree(dir)) Later.Add(dir);
    }
    static bool DeleteTree(string dir)
    {
        bool all = true;
        if (IsLink(dir)) { try { Directory.Delete(dir, false); return true; } catch (Exception) { return false; } }
        foreach (var sub in Directory.GetDirectories(dir)) all &= DeleteTree(sub);
        foreach (var file in Directory.GetFiles(dir))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            catch (Exception) { all = false; }
        }
        if (!all) return false;
        try { Directory.Delete(dir, false); return true; } catch (Exception) { return false; }
    }
    static void TryRemoveEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir, false); } catch (Exception) { }
    }

    static bool IsLink(string path)
    {
        try { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception) { return false; }
    }

    // The bundle's font: its registry names for this account, then its files (Windows keeps a font
    // open while a program uses it: those go later).
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool RemoveFontResource(string file);
    static void RemoveFonts(string bundleDir)
    {
        var faces = Directory.GetFiles(bundleDir, "*.ttf").Select(f => Path.GetFileName(f)).ToList();
        var fontDir = Path.Combine(Local, @"Microsoft\Windows\Fonts");
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts", true))
        {
            if (key != null)
                foreach (var name in key.GetValueNames())
                {
                    var file = key.GetValue(name) as string;
                    if (file == null || !faces.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)) continue;
                    if (!UninstallRules.IsUnder(file, fontDir)) continue;
                    key.DeleteValue(name, false);
                    RemoveFontResource(file);
                    RemoveFile(file, false);
                }
        }
        RemoveTree(bundleDir);
    }

    // --- the PATH -------------------------------------------------------------------------------
    static string ReadUserPath()
    {
        using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
            return key == null ? "" : (key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "");
    }
    // Raw (REG_EXPAND_SZ), as install/lib.ps1's Add-UserPath writes it, then running programs told.
    static void WriteUserPath(string value)
    {
        using (var key = Registry.CurrentUser.OpenSubKey("Environment", true))
            key.SetValue("Path", value, RegistryValueKind.ExpandString);
        IntPtr result;
        SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out result);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    // --- the taskbar, the Recycle Bin, the desktop's icons ---------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public int left, top, right, bottom; public IntPtr lParam; }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    static bool TaskbarHides()
    {
        var bar = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
        return (SHAppBarMessage(4, ref bar).ToUInt64() & 1) != 0;   // ABM_GETSTATE, ABS_AUTOHIDE
    }
    static void SetTaskbarHides(bool hide)
    {
        var bar = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), lParam = new IntPtr(hide ? 1 : 0) };
        SHAppBarMessage(10, ref bar);                                   // ABM_SETSTATE
    }
    const string RecycleBin = "{645FF040-5081-101B-9F08-00AA002F954E}";
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    static void ShowRecycleBin()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", true))
            if (key != null) key.DeleteValue(RecycleBin, false);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);       // SHCNE_ASSOCCHANGED: the desktop redraws
    }

    // As bin/desktop-icons.ps1: Explorer's own View > Show desktop icons, which also saves HideIcons.
    static class DesktopIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        static IntPtr View()
        {
            var view = FindWindowEx(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            var worker = IntPtr.Zero;
            while (view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            return view;
        }
        public static bool Hidden()
        {
            var view = View();
            if (view != IntPtr.Zero) return !IsWindowVisible(FindWindowEx(view, IntPtr.Zero, "SysListView32", null));
            using (var key = Registry.CurrentUser.OpenSubKey(Advanced))
                return key != null && Equals(key.GetValue("HideIcons"), 1);
        }
        public static void Show()
        {
            var view = View();
            if (view != IntPtr.Zero && Hidden()) SendMessage(view, 0x0111, new IntPtr(0x7402), IntPtr.Zero);
            using (var key = Registry.CurrentUser.OpenSubKey(Advanced, true))
                if (key != null) key.SetValue("HideIcons", 0, RegistryValueKind.DWord);
            var choice = Path.Combine(StateDir, "desktop-icons");
            if (File.Exists(choice)) File.Delete(choice);
        }
    }

    // --- what's still in use ----------------------------------------------------------------------
    // A small script removes them: once this has exited (this program and omawin.exe free their
    // files then), and again at the next sign-in for anything still held (HKCU RunOnce, no admin).
    static void ScheduleLater()
    {
        if (Later.Count == 0) return;
        var script = Path.Combine(Path.GetTempPath(), "omawin-uninstall-cleanup.cmd");
        var lines = new List<string> { "@echo off", "ping -n 3 127.0.0.1 >nul" };
        var held = false;
        foreach (var path in Later.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (path.IndexOf('"') >= 0 || path.IndexOf('%') >= 0) continue;   // never a path cmd would read as code
            lines.Add(Directory.Exists(path) ? "rmdir /s /q \"" + path + "\" 2>nul" : "del /f /q \"" + path + "\" 2>nul");
            held = true;
        }
        if (!held) return;
        // Itself last, and its sign-in entry once nothing is left to do.
        lines.Add("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce\" /v omawin-uninstall /f >nul 2>&1");
        lines.Add("del /f /q \"%~f0\"");
        File.WriteAllLines(script, lines.ToArray());
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce"))
            key.SetValue("omawin-uninstall", "cmd.exe /c \"" + script + "\"");
        try { Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true }); }
        catch (Exception) { }
    }

    // --- small helpers ------------------------------------------------------------------------------
    static string ProgramFiles() { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); }
    static string SafeRead(string path) { try { return File.ReadAllText(path); } catch (Exception) { return null; } }
    static IEnumerable<string> SafeLines(string path) { try { return File.ReadAllLines(path); } catch (Exception) { return new string[0]; } }
    static IEnumerable<string> Lines(string text) { return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0); }
    static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

    // The first $exe on the PATH (this process's, as the shell that ran it had it).
    static string Which(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (dir.Trim().Length == 0) continue;
            try { var f = Path.Combine(dir.Trim().Trim('"'), exe); if (File.Exists(f)) return f; } catch (Exception) { }
        }
        return null;
    }

    // Runs a program hidden and waits (up to $timeout ms); its exit code, or -1.
    static int Run(string file, string arguments, int timeout)
    {
        try
        {
            var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(info))
            {
                p.OutputDataReceived += delegate { };
                p.ErrorDataReceived += delegate { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch (Exception) { } return -1; }
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception) { return -1; }
    }
    static string Capture(string file, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(info))
            {
                p.ErrorDataReceived += delegate { };
                p.BeginErrorReadLine();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode == 0 ? output : "";
            }
        }
        catch (Exception) { return ""; }
    }

    // This process and every one it runs under (the terminal, its shell, omawin.exe).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static HashSet<int> Ancestors()
    {
        var parent = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(0x2, 0);   // TH32CS_SNAPPROCESS
        if (snapshot == new IntPtr(-1)) return new HashSet<int> { Process.GetCurrentProcess().Id };
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                parent[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
        }
        finally { CloseHandle(snapshot); }
        var chain = new HashSet<int>();
        int id = Process.GetCurrentProcess().Id;
        while (chain.Add(id) && parent.ContainsKey(id)) id = parent[id];
        return chain;
    }
}
#endif
