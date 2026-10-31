using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Omawin.Cli.Profile;

// `omawin profile linux` (roadmap 3.5): what a cold start of your Linux in WSL costs against a warm
// one, and what keeping it warm costs in memory: the numbers plans/04-linux.md's table waits for,
// printed ready to paste. No window opens (wsl.exe is timed straight), so it measures WSL itself,
// not the terminal; `omawin profile terminal -Wsl` times the whole Win+Enter.
//
// Each run, in order:
//   cold VM       after `wsl --shutdown`: the VM, the distro and its systemd
//   cold distro   after `wsl --terminate <distro>`, the VM still up (vmIdleTimeout keeps it)
//   warm          the distro running: wsl.exe's own round trip
//   warm shell    bash -i with your rc (Omarchy's and omawin's): what a terminal waits for
// then, the distro left running and settled, its memory (inside: `free -m`; outside: the VM's
// process, which Windows may not let a user read) and what systemd spent booting.
public abstract class LinuxMachine
{
    // wsl.exe with these arguments, its output (WSL_UTF8 set, so UTF-8), its exit code; -1 when it
    // didn't finish in time.
    public abstract (int Code, string Output) Wsl(IReadOnlyList<string> args, int timeoutMs = 120_000);
    // The WSL VM's working set (vmmemWSL, or vmmem on older WSL), in bytes; null when unreadable.
    public abstract long? VmWorkingSet();
    public abstract void Sleep(int ms);
    // Whether a person can answer a question here, and their answer.
    public abstract string? Ask(string prompt);
}

public sealed record LinuxProfileResult(
    string Distro,
    List<double> ColdVm, List<double> ColdDistro, List<double> Warm, List<double> WarmShell,
    int? UsedMb, int? CacheMb, int? VmMb, string? Boot, List<string> Blame);

public static class LinuxProfile
{
    // -Runs n (3), -Settle seconds (60), -Yes (don't ask before stopping WSL).
    public static int Command(LinuxMachine machine, string[] args, string repo, string state, TextWriter output)
    {
        int runs = 3, settle = 60;
        bool yes = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].TrimStart('-').ToLowerInvariant();
            if (a == "yes" || a == "y") yes = true;
            else if ((a == "runs" || a == "settle") && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n >= 0)
            {
                if (a == "runs") runs = Math.Max(1, n); else settle = n;
                i++;
            }
            else { output.WriteLine("omawin profile linux [-Runs 3] [-Settle 60] [-Yes]"); return 2; }
        }
        var distro = Distro(repo, state);
        var (listed, names) = machine.Wsl(["--list", "--quiet"], 30_000);
        if (listed != 0 || !Lines(names).Contains(distro, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine($"omawin: {distro} isn't installed for this account (omawin preset add wsl)");
            return 1;
        }
        if (!yes)
        {
            var answer = machine.Ask($"This stops every Linux in WSL (wsl --shutdown) {runs} time{(runs == 1 ? "" : "s")}: save your work there first. Go on? [y/N] ");
            if (answer == null) { output.WriteLine("omawin: this stops WSL, so it asks first; -Yes to run it without a question"); return 1; }
            if (!answer.Trim().StartsWith('y')) { output.WriteLine("Nothing done."); return 1; }
        }
        var result = Measure(machine, distro, runs, settle, output);
        output.Write(Report(result));
        return 0;
    }

    // The Linux omawin opens (omawin default linux): its WSL name, from bin/defaults.txt.
    public static string Distro(string repo, string state)
    {
        static IEnumerable<string> Read(string file) { try { return File.ReadAllLines(file); } catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; } }
        var rows = Read(Path.Combine(repo, "bin", "defaults.txt")).Select(l => l.Split('|').Select(x => x.Trim()).ToArray())
            .Where(f => f.Length >= 5 && f[0] == "linux").ToList();
        var picked = Read(Path.Combine(state, "defaults", "linux")).FirstOrDefault()?.Trim();
        return (rows.FirstOrDefault(f => f[1] == picked) ?? rows.FirstOrDefault())?[4] ?? "archlinux";
    }

    public static LinuxProfileResult Measure(LinuxMachine machine, string distro, int runs, int settleSeconds, TextWriter progress)
    {
        List<double> coldVm = [], coldDistro = [], warm = [], warmShell = [];
        string[] tru = ["-d", distro, "--exec", "/bin/true"];
        double Time(IReadOnlyList<string> args)
        {
            var clock = Stopwatch.StartNew();
            var (code, _) = machine.Wsl(args);
            return code == 0 ? clock.Elapsed.TotalMilliseconds : double.NaN;
        }
        for (int run = 1; run <= runs; run++)
        {
            progress.WriteLine($"run {run} of {runs}...");
            machine.Wsl(["--shutdown"]);
            machine.Sleep(3000); // the VM's process takes a moment to go
            coldVm.Add(Time(tru));
            machine.Wsl(["--terminate", distro]);
            machine.Sleep(1000);
            coldDistro.Add(Time(tru));
            warm.Add(Time(tru));
            warmShell.Add(Time(["-d", distro, "--cd", "~", "--exec", "bash", "-ic", "exit"]));
        }
        if (settleSeconds > 0) progress.WriteLine($"letting it settle for {settleSeconds} s, then its memory...");
        machine.Sleep(settleSeconds * 1000);
        var (_, free) = machine.Wsl(["-d", distro, "--exec", "free", "-m"]);
        var (used, cache) = ParseFree(free);
        var vm = machine.VmWorkingSet();
        var (_, boot) = machine.Wsl(["-d", distro, "--exec", "systemd-analyze", "time"], 30_000);
        var (_, blame) = machine.Wsl(["-d", distro, "--exec", "systemd-analyze", "blame", "--no-pager"], 30_000);
        return new LinuxProfileResult(distro, coldVm, coldDistro, warm, warmShell, used, cache,
            vm is { } bytes ? (int)(bytes / (1024 * 1024)) : null, BootLine(boot), [.. Lines(blame).Take(5)]);
    }

    // `free -m`'s Mem: line: used and buff/cache, in MB (procps' columns: total used free shared
    // buff/cache available).
    public static (int? Used, int? Cache) ParseFree(string text)
    {
        foreach (var line in Lines(text))
        {
            if (!line.StartsWith("Mem:")) continue;
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int? At(int i) => f.Length > i && int.TryParse(f[i], out var v) ? v : null;
            return (At(2), At(5));
        }
        return (null, null);
    }

    // systemd-analyze time's "Startup finished in ... = 2.345s" line, from "in" on; null without one
    // (no systemd, or still booting).
    public static string? BootLine(string text)
    {
        var line = Lines(text).FirstOrDefault(l => l.StartsWith("Startup finished in "));
        return line?["Startup finished in ".Length..];
    }

    public static double? Median(IEnumerable<double> values)
    {
        var v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        return v.Count == 0 ? null : v[(v.Count - 1) / 2];
    }

    // The table, then the lines for plans/04-linux.md and the budget for omawin profile ram.
    public static string Report(LinuxProfileResult r)
    {
        var s = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        int runs = r.ColdVm.Count;
        string Cell(double? v) => v is { } x && !double.IsNaN(x) ? x.ToString("0", c).PadLeft(7) : "-".PadLeft(7);
        void Row(string name, List<double> values)
        {
            s.Append(name.PadRight(46));
            foreach (var v in values) s.Append(Cell(v));
            s.Append(' ').Append(Cell(Median(values))).AppendLine();
        }
        s.AppendLine().AppendLine($"Linux start, {r.Distro} (ms)").AppendLine();
        s.Append(new string(' ', 46));
        for (int i = 1; i <= runs; i++) s.Append($"run {i}".PadLeft(7));
        s.AppendLine("  median");
        Row("cold: the VM and the distro (wsl --shutdown)", r.ColdVm);
        Row("cold distro, the VM up (wsl --terminate)", r.ColdDistro);
        Row("warm: wsl.exe's round trip", r.Warm);
        Row("warm: bash with your rc (a terminal's wait)", r.WarmShell);

        s.AppendLine().AppendLine("Memory, the distro running and settled").AppendLine();
        s.AppendLine($"  in Linux (free -m)   {(r.UsedMb is { } u ? $"{u} MB used" : "-")}{(r.CacheMb is { } k ? $", {k} MB cache" : "")}");
        s.AppendLine($"  the VM, from Windows {(r.VmMb is { } m ? $"{m} MB" : "- (Windows didn't let this user read it)")}");
        if (r.Boot != null || r.Blame.Count > 0)
        {
            s.AppendLine().AppendLine($"systemd's boot: {r.Boot ?? "-"}");
            foreach (var line in r.Blame) s.AppendLine("  " + line);
        }

        // What the warm side costs: the VM as Windows sees it when it's readable, else Linux's own
        // count (used plus cache: WSL hands cache back to Windows lazily).
        int? idle = r.VmMb ?? (r.UsedMb is { } used ? used + (r.CacheMb ?? 0) : null);
        s.AppendLine().AppendLine("For plans/04-linux.md, 3.5's table:").AppendLine();
        s.AppendLine($"| keeper off, after `wsl --shutdown` | 0 MB | {Cell(Median(r.ColdVm)).Trim()} ms |");
        s.AppendLine($"| keeper on, systemd + Docker socket idle | {(idle is { } i2 ? i2 + " MB" : "-")} | {Cell(Median(r.WarmShell)).Trim()} ms (bash ready) |");
        if (idle is { } i3)
            s.AppendLine().AppendLine($"omawin profile ram's WSL budget (idle + 20%): {(int)Math.Ceiling(i3 * 1.2)} MB");
        return s.ToString();
    }

    // wsl.exe's lines: CRs, NULs (UTF-16 read as bytes) and blanks out.
    public static List<string> Lines(string text) =>
        [.. text.Replace("\0", "").Split('\n').Select(l => l.Trim('\r', ' ', '\t')).Where(l => l.Length > 0)];
}
