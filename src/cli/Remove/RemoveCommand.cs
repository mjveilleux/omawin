using System.Diagnostics;
using Omawin.Cli.Pkg;

namespace Omawin.Cli.Remove;

// What `omawin remove` needs from the PC. Tests give it one that records what it was asked
// (test/cli.Tests/RemoveTests.cs); WindowsRemoveHost is the real one.
public abstract class RemoveHost
{
    public abstract TextWriter Out { get; }
    public abstract string? Ask(string prompt);       // null: no one to ask
    public abstract int Omawin(string[] words);       // another omawin command, in this terminal
    public abstract int Pkg(string[] args);           // omawin pkg, in this process
    public abstract void Stop(string process);        // its copies in this session
    public abstract bool Delete(string folder);       // false: it wasn't there
}

// omawin remove <part> [--dry-run] [--yes]: the plan (RemovePlan.cs), shown, asked once, done.
public sealed class RemoveCommand(RemoveHost host, RemoveFacts facts)
{
    readonly TextWriter o = host.Out;

    public int Run(IReadOnlyList<string> args)
    {
        bool dry = args.Contains("--dry-run"), yes = args.Any(a => a is "--yes" or "-y");
        var plan = RemovePlanner.For(args, facts);
        if (plan.Error != null)
        {
            o.WriteLine("  " + plan.Error);
            foreach (var l in plan.Left) o.WriteLine("  stays: " + l);
            return plan.Part.Length > 0 ? 0 : 2; // a part with nothing here isn't a mistake
        }
        o.WriteLine($"\n  omawin remove {plan.Part}:");
        foreach (var s in plan.Steps) o.WriteLine("    - " + s.Says);
        foreach (var l in plan.Left) o.WriteLine("    stays: " + l);
        o.WriteLine("    your own files are never touched");
        if (dry) { o.WriteLine("\n  (--dry-run: nothing done)"); return 0; }
        if (!yes)
        {
            var answer = host.Ask("\n  Remove it? [y/N] ");
            if (answer == null) { o.WriteLine("  no one to ask: omawin remove " + string.Join(' ', args) + " --yes"); return 1; }
            if (!answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y')) { o.WriteLine("  nothing done"); return 0; }
        }

        int failed = 0;
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            switch (plan.Steps[i])
            {
                case RunOmawin r:
                    o.WriteLine($"\n  {r.Says}");
                    if (host.Omawin(r.Words) != 0) { failed++; o.WriteLine($"  ✗ omawin {string.Join(' ', r.Words)} failed"); }
                    break;
                case StopProcess s:
                    host.Stop(s.Name);
                    break;
                case RemovePackage:
                    // The packages together, so Chocolatey's go in one admin prompt (omawin pkg).
                    var keys = new List<string>();
                    while (i < plan.Steps.Count && plan.Steps[i] is RemovePackage p) { keys.Add(p.Key); i++; }
                    i--;
                    if (host.Pkg(["remove", .. keys]) != 0) failed++;
                    break;
                case DeleteFolder d:
                    if (host.Delete(d.Path)) o.WriteLine($"  ✓ deleted {d.What}");
                    break;
            }
        }
        o.WriteLine(failed == 0 ? $"\n  ✓ {plan.Part} removed" : $"\n  {plan.Part}: {failed} step{(failed == 1 ? "" : "s")} failed (above); run it again once that's fixed");
        return failed == 0 ? 0 : 1;
    }

    // The facts from this PC's files: the presets (install/packages.ps1), the picked ones and
    // the package record (%LOCALAPPDATA%\launcher).
    public static RemoveFacts Read(string repo, string state)
    {
        static IEnumerable<string> Lines(string path) => File.Exists(path) ? File.ReadAllLines(path) : [];
        var picked = Lines(Path.Combine(state, "presets.txt")).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        return new RemoveFacts(PresetFile.Parse(Lines(Path.Combine(repo, "install", "packages.ps1"))), picked,
            PackageRecord.Parse(Lines(Path.Combine(state, "packages.tsv"))), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }
}

// The real PC.
public sealed class WindowsRemoveHost(string repo, string state) : RemoveHost
{
    public override TextWriter Out => Console.Out;

    public override string? Ask(string prompt)
    {
        if (Console.IsInputRedirected) return null;
        Console.Write(prompt);
        return Console.ReadLine();
    }

    // omawin.exe beside this one (omawin-cli.exe ships next to it), so the same checkout answers.
    public override int Omawin(string[] words)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "omawin.exe");
        var info = new ProcessStartInfo(File.Exists(exe) ? exe : "omawin.exe") { UseShellExecute = false };
        foreach (var w in words) info.ArgumentList.Add(w);
        try
        {
            using var p = Process.Start(info);
            if (p == null) return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Out.WriteLine("  " + ex.Message); return 1; }
    }

    public override int Pkg(string[] args) => new PkgCommand(new WindowsHost(repo, state)).Run(args);

    public override void Stop(string process)
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName(process))
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session) continue; // another account's: not ours to stop
                    p.Kill();
                    p.WaitForExit(5000);
                    Out.WriteLine($"  stopped {process}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    public override bool Delete(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        try { Directory.Delete(folder, recursive: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Out.WriteLine($"  ✗ {folder}: {ex.Message}"); return false; }
    }
}
