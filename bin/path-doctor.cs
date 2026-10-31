// omawin path (roadmap 8.35): both PATHs, what's wrong with them, and which program wins for
// each command and why.
//
//   omawin path              your PATH and Windows' (the system PATH): empty, quoted, relative,
//                            missing and duplicate entries, the length, and which node / python /
//                            git ... runs; more commands to look up can follow: omawin path rg fd
//   omawin path fix          your PATH without those entries (the system PATH needs admin, so it's
//                            only reported); what it was is kept, and new terminals see it at once
//   omawin path undo         your PATH back as it was before the last fix
//
// Windows builds a program's PATH as the system PATH, then yours: a program in both is found in
// the system's first, which is why a version manager's folder in your PATH (mise's shims) can lose
// to an installer's in the system PATH. Entries are read raw (%VARS% unexpanded) and a fix writes
// them back raw, as REG_EXPAND_SZ: [Environment]::SetEnvironmentVariable would store REG_SZ and
// break entries like %USERPROFILE%\.cargo\bin.
//
// A small native exe compiled with the C# compiler that ships with Windows (C# 5: no newer
// syntax), by build/helpers.ps1 like omawin.exe. PathDoctor below is pure (no registry, no
// files), so test/cli.Tests runs it anywhere.
#if OMAWIN_TESTS
// The tests build with nullable references on; this file is C# 5, from before they existed.
#pragma warning disable CS8600, CS8618
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public sealed class PathEntry
{
    public string Scope;    // "system" or "user"
    public string Raw;      // as stored, %VARS% and all
    public string Folder;   // expanded, unquoted, no trailing \
    public string Key;      // Folder, lowercased: what duplicates are told by
    public string Problem;  // null when it's fine
}

public sealed class Winner
{
    public string Command;
    public string File;     // the one that runs
    public string Scope;
    public List<string> Others = new List<string>();   // the ones it hides, in order
    public string Note;     // why it's worth a look, or null
}

public static class PathDoctor
{
    // Past this, some programs see PATH cut short (cmd's own SET; setx stops at 1024).
    public const int LengthLimit = 2047;

    public static readonly string[] DefaultCommands =
        { "node", "bun", "python", "python3", "git", "pwsh", "nvim", "go", "cargo", "java", "dotnet", "code", "mise", "ssh" };

    // Each entry of both PATHs, system first (the order Windows searches them), with what's wrong.
    public static List<PathEntry> Analyze(string system, string user, Func<string, string> expand, Func<string, bool> folderExists)
    {
        var entries = new List<PathEntry>();
        var seen = new Dictionary<string, string>();
        foreach (var scope in new[] { "system", "user" })
        {
            var raw = scope == "system" ? system : user;
            if (string.IsNullOrEmpty(raw)) continue;
            var parts = raw.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                var e = parts[i];
                // A trailing ; leaves one empty part at the end: that's normal, not an entry.
                if (e.Length == 0 && i == parts.Length - 1) continue;
                var folder = expand(e.Trim().Trim('"')).TrimEnd('\\');
                if (folder.Length == 2 && folder[1] == ':') folder += "\\";   // C: alone means the current folder on C:
                var entry = new PathEntry { Scope = scope, Raw = e, Folder = folder, Key = folder.ToLowerInvariant() };
                string first;
                if (e.Trim().Length == 0) entry.Problem = "empty";
                else if (e != e.Trim() || e.IndexOf('"') >= 0) entry.Problem = "quotes or spaces around it";
                else if (folder.IndexOf('%') >= 0) entry.Problem = "uses a variable that isn't set";
                else if (!Rooted(folder)) entry.Problem = "not a full path";
                else if (seen.TryGetValue(entry.Key, out first)) entry.Problem = "duplicate (also in " + (first == scope ? "this PATH" : "the " + first + " PATH") + ")";
                else if (!folderExists(folder)) entry.Problem = "missing";
                if (entry.Problem == null || entry.Problem == "missing") { if (!seen.ContainsKey(entry.Key)) seen[entry.Key] = scope; }
                entries.Add(entry);
            }
        }
        return entries;
    }

    static bool Rooted(string folder)
    {
        return (folder.Length >= 3 && char.IsLetter(folder[0]) && folder[1] == ':' && folder[2] == '\\') || folder.StartsWith("\\\\");
    }

    // Which file runs for each command: the first folder (system, then user) holding command plus
    // one of PATHEXT's extensions, tried in PATHEXT's order, as Windows' own search does.
    public static List<Winner> Winners(List<PathEntry> entries, IEnumerable<string> commands, string[] extensions, Func<string, bool> fileExists)
    {
        var winners = new List<Winner>();
        var usable = entries.Where(e => e.Problem == null).ToList();
        foreach (var command in commands)
        {
            Winner w = null;
            var hitScopes = new List<string>();
            foreach (var e in usable)
            {
                foreach (var ext in extensions)
                {
                    var file = e.Folder + "\\" + command + ext.ToLowerInvariant();
                    if (!fileExists(file)) continue;
                    if (w == null) w = new Winner { Command = command, File = file, Scope = e.Scope };
                    else w.Others.Add(file);
                    hitScopes.Add(e.Scope);
                    break;
                }
            }
            if (w == null) continue;
            if (w.File.IndexOf("\\microsoft\\windowsapps\\", StringComparison.OrdinalIgnoreCase) >= 0)
                w.Note = "an app alias from the Store: if the app isn't installed it opens the Store instead (Settings > Apps > Advanced app settings > App execution aliases)";
            else if (w.Scope == "system" && hitScopes.Contains("user"))
                w.Note = "the system PATH wins over yours, so your " + w.Others[hitScopes.IndexOf("user") - 1] + " is never run";
            winners.Add(w);
        }
        return winners;
    }

    // Your PATH with the problem entries left out, the rest raw and in order.
    public static string FixedUser(List<PathEntry> entries)
    {
        return string.Join(";", entries.Where(e => e.Scope == "user" && e.Problem == null).Select(e => e.Raw).ToArray());
    }

    public static string Report(List<PathEntry> entries, List<Winner> winners, int length)
    {
        var sb = new StringBuilder();
        var bad = entries.Where(e => e.Problem != null).ToList();
        foreach (var scope in new[] { "system", "user" })
        {
            var mine = entries.Where(e => e.Scope == scope).ToList();
            var problems = mine.Where(e => e.Problem != null).ToList();
            sb.AppendLine((scope == "system" ? "Windows' PATH (every account; changing it needs admin)" : "Your PATH") + ": " + mine.Count + " entries, " +
                (problems.Count == 0 ? "all fine" : problems.Count + " to look at"));
            foreach (var e in problems) sb.AppendLine("  " + (e.Raw.Trim().Length == 0 ? "(empty)" : e.Raw) + "  -- " + e.Problem);
        }
        if (length > LengthLimit) sb.AppendLine("PATH is " + length + " characters: past " + LengthLimit + ", some programs see it cut short.");
        if (winners.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Which program runs");
            int width = winners.Max(w => w.Command.Length);
            foreach (var w in winners)
            {
                sb.AppendLine("  " + w.Command.PadRight(width) + "  " + w.File + (w.Others.Count > 0 ? "  (hides " + w.Others.Count + ")" : ""));
                if (w.Note != null) sb.AppendLine("  " + new string(' ', width) + "  ! " + w.Note);
            }
        }
        if (bad.Any(e => e.Scope == "user"))
        {
            sb.AppendLine();
            sb.AppendLine("omawin path fix takes the problem entries out of your PATH (omawin path undo puts them back).");
        }
        return sb.ToString();
    }
}

#if !OMAWIN_TESTS
static class Program
{
    const string UserKey = "Environment";
    const string SystemKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    static readonly string History = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "path-history.tsv");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        try
        {
            if (verb == "fix") return Fix();
            if (verb == "undo") return Undo();
            if (verb == "help" || verb == "-h" || verb == "--help")
            {
                Console.WriteLine("omawin path [command ...] | fix | undo");
                return 0;
            }
            return Show(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("omawin path: " + ex.Message);
            return 1;
        }
    }

    static string Read(Microsoft.Win32.RegistryKey root, string key)
    {
        using (var k = root.OpenSubKey(key))
        {
            if (k == null) return "";
            var v = k.GetValue("Path", "", Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
            return v == null ? "" : v.ToString();
        }
    }

    static List<PathEntry> Entries(out string system, out string user)
    {
        system = Read(Microsoft.Win32.Registry.LocalMachine, SystemKey);
        user = Read(Microsoft.Win32.Registry.CurrentUser, UserKey);
        return PathDoctor.Analyze(system, user, Environment.ExpandEnvironmentVariables, Directory.Exists);
    }

    static int Show(string[] args)
    {
        string system, user;
        var entries = Entries(out system, out user);
        var commands = PathDoctor.DefaultCommands.Concat(args).Distinct(StringComparer.OrdinalIgnoreCase);
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = (string.IsNullOrEmpty(pathext) ? ".COM;.EXE;.BAT;.CMD" : pathext).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var winners = PathDoctor.Winners(entries, commands, extensions, File.Exists);
        var length = Environment.ExpandEnvironmentVariables(system).Length + 1 + Environment.ExpandEnvironmentVariables(user).Length;
        Console.Write(PathDoctor.Report(entries, winners, length));
        return 0;
    }

    static int Fix()
    {
        string system, user;
        var entries = Entries(out system, out user);
        var fixedUser = PathDoctor.FixedUser(entries);
        var removed = entries.Where(e => e.Scope == "user" && e.Problem != null).ToList();
        if (removed.Count == 0) Console.WriteLine("Your PATH has nothing to fix.");
        else
        {
            // What it was, first: undo reads it back. One line per fix, tab-separated, newest last.
            Directory.CreateDirectory(Path.GetDirectoryName(History));
            File.AppendAllText(History, DateTime.Now.ToString("s") + "\t" + user + Environment.NewLine);
            Write(fixedUser);
            Console.WriteLine("Took " + removed.Count + " out of your PATH (omawin path undo puts them back):");
            foreach (var e in removed) Console.WriteLine("  " + (e.Raw.Trim().Length == 0 ? "(empty)" : e.Raw) + "  -- " + e.Problem);
            Console.WriteLine("New terminals see it now; ones already open keep the old one.");
        }
        var systemProblems = entries.Count(e => e.Scope == "system" && e.Problem != null);
        if (systemProblems > 0)
            Console.WriteLine("Windows' PATH has " + systemProblems + " to look at (omawin path lists them); changing it needs admin, so it's left as it is.");
        return 0;
    }

    static int Undo()
    {
        var lines = File.Exists(History) ? File.ReadAllLines(History).Where(l => l.IndexOf('\t') > 0).ToList() : new List<string>();
        if (lines.Count == 0) { Console.WriteLine("No PATH fix to undo."); return 1; }
        var last = lines[lines.Count - 1];
        Write(last.Substring(last.IndexOf('\t') + 1));
        // Undone fixes leave the history, so a second undo goes one further back.
        lines.RemoveAt(lines.Count - 1);
        File.WriteAllLines(History, lines.ToArray());
        Console.WriteLine("Your PATH is back as it was at " + last.Substring(0, last.IndexOf('\t')) + ".");
        return 0;
    }

    // Raw, as REG_EXPAND_SZ, then the "Environment" broadcast so Explorer (and what it starts)
    // picks it up without signing out.
    static void Write(string value)
    {
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UserKey, true))
            k.SetValue("Path", value, Microsoft.Win32.RegistryValueKind.ExpandString);
        IntPtr result;
        SendMessageTimeout((IntPtr)0xffff, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "Environment", 0x0002 /* SMTO_ABORTIFHUNG */, 5000, out result);
    }
}
#endif
