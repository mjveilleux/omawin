using System.Diagnostics;

namespace Omawin.Windows;

// omawin disk (8.3): where a developer's disk space went, and getting it back.
//   omawin disk                     the package managers' caches and Linux's disks, biggest first
//   omawin disk clean <cache>       one cache emptied (its tool's own command when it has one)
//   omawin disk projects [folder] [days]  build output in projects untouched for 30 days (or days)
//   omawin disk projects clean [folder] [days]  …deleted, after the list and a yes
//   omawin disk sparse <distro>     a WSL disk that gives freed space back to Windows from now on
// Nothing outside a cache or a project's build output is touched, and nothing without asking.
static class DiskCommand
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    static IReadOnlyList<DevCache> Caches() =>
        DevCaches.All(Environment.GetEnvironmentVariable, LocalAppData, Home, Path.GetTempPath());

    public static int Run(string[] args)
    {
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        return verb switch
        {
            "list" => List(),
            "clean" => Clean(string.Join(' ', args[1..])),
            "projects" => args.Length > 1 && args[1] == "clean" ? Projects(args[2..], clean: true) : Projects(args[1..], clean: false),
            "sparse" => Sparse(string.Join(' ', args[1..])),
            _ => Program.Fail("omawin disk [clean <cache> | projects [clean] [folder] [days] | sparse <distro>]"),
        };
    }

    static int List()
    {
        Console.WriteLine("Measuring…");
        var week = DateTime.Now.AddDays(-7);
        var sized = Caches().AsParallel().Select(c => (Cache: c, Size: DiskScan.SizeOf(c.Folder, c.OldFilesOnly ? week : null)))
            .Where(c => c.Size > 0).OrderByDescending(c => c.Size).ToList();
        if (sized.Count > 0)
        {
            Console.WriteLine($"Caches ({DevCaches.Size(sized.Sum(c => c.Size))}; everything in them comes back when needed):");
            foreach (var (c, size) in sized) Console.WriteLine($"  {DevCaches.Size(size),9}  {c.Name,-26} {c.Folder}");
            Console.WriteLine("  omawin disk clean <name> empties one.");
        }
        var disks = DiskScan.LinuxDisks(LocalAppData);
        if (disks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Linux disks (they grow and never shrink by themselves):");
            foreach (var d in disks)
                Console.WriteLine($"  {DevCaches.Size(DiskScan.OnDisk(d.Vhd)),9}  {d.Name,-26} {(d.Sparse ? "gives space back" : "never shrinks")}  {d.Vhd}");
            if (disks.Any(d => !d.Sparse && d.Name != "Docker Desktop"))
                Console.WriteLine("  omawin disk sparse <distro> makes one give freed space back from now on.");
            if (disks.Any(d => d.Name == "Docker Desktop"))
                Console.WriteLine("  Docker's: docker system df says what's in it; docker builder prune and docker system prune free it.");
        }
        Console.WriteLine();
        Console.WriteLine("omawin disk projects lists build output (node_modules, target, bin and obj, .venv…) in projects you haven't touched for a month.");
        return 0;
    }

    static int Clean(string name)
    {
        var caches = Caches();
        if (name.Length == 0) return Program.Fail("which one? omawin disk clean <cache> (omawin disk lists them)");
        var cache = DevCaches.Find(caches, name);
        if (cache == null) return Program.Fail($"no cache called \"{name}\": {string.Join(", ", caches.Select(c => c.Name))}");
        DateTime? older = cache.OldFilesOnly ? DateTime.Now.AddDays(-7) : null;
        long before = DiskScan.SizeOf(cache.Folder, older);
        if (before == 0) return Program.Say($"{cache.Name} is empty.");

        var tool = cache.Clean == null ? null : Which(cache.Clean.Split(' ')[0]);
        bool useTool = tool != null;
        if (!useTool && !DevCaches.SafeToEmpty(cache.Folder, Home, LocalAppData))
            return Program.Fail($"{cache.Folder} isn't a folder omawin empties by hand (set by an environment variable?): use {cache.Clean ?? "the tool's own command"}");
        Console.WriteLine(useTool
            ? $"{cache.Name}: {DevCaches.Size(before)}. Runs: {cache.Clean}"
            : $"{cache.Name}: {DevCaches.Size(before)} in {cache.Folder}{(cache.OldFilesOnly ? " (files older than a week)" : "")}; its contents are deleted (files in use are left).");
        if (Program.CanAsk && !Program.Ask("Go ahead?")) return 0;

        if (useTool)
        {
            var arguments = cache.Clean!.Split(' ', 2) is [_, var a] ? a : "";
            // npm, pnpm and yarn are .cmd scripts, which only cmd.exe runs.
            var info = tool!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(tool, arguments)
                : new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), $"/d /c \"\"{tool}\" {arguments}\"");
            info.UseShellExecute = false;
            using var p = Process.Start(info);
            p?.WaitForExit();
            if (p == null || p.ExitCode != 0) return Program.Fail($"{cache.Clean} failed");
        }
        else DiskScan.Empty(cache.Folder, older);
        long after = DiskScan.SizeOf(cache.Folder, older);
        return Program.Say($"{cache.Name}: {DevCaches.Size(Math.Max(0, before - after))} freed.");
    }

    static readonly string[] ProjectRoots =
    [
        Path.Combine(Home, "src"), Path.Combine(Home, "code"), Path.Combine(Home, "projects"), Path.Combine(Home, "dev"),
        Path.Combine(Home, "source", "repos"), Path.Combine(Home, "repos"), Path.Combine(Home, "Documents"),
        Path.Combine(Home, "OneDrive", "Documents"),
    ];

    static int Projects(string[] args, bool clean)
    {
        int days = 30;
        var rest = args.ToList();
        if (rest.Count > 0 && int.TryParse(rest[^1], out var d) && d >= 0) { days = d; rest.RemoveAt(rest.Count - 1); }
        var roots = rest.Count > 0 ? [Path.GetFullPath(string.Join(' ', rest))] : ProjectRoots;
        Console.WriteLine($"Looking in {string.Join(", ", roots.Where(Directory.Exists))}…");
        var now = DateTime.Now;
        var all = DiskScan.Projects(roots);
        var old = all.Where(f => (now - f.Touched).TotalDays >= days && f.Size > 0).OrderByDescending(f => f.Size).ToList();
        if (old.Count == 0)
            return Program.Say(all.Count == 0 ? "No build output found." : $"No build output in projects untouched for {days} days ({all.Count} in projects in use).");
        foreach (var f in old)
            Console.WriteLine($"  {DevCaches.Size(f.Size),9}  {Path.GetRelativePath(Home, f.Folder),-60} last changed {DevCaches.Ago(f.Touched, now)}");
        long total = old.Sum(f => f.Size);
        Console.WriteLine($"{DevCaches.Size(total)} in {old.Count} folders of projects untouched for {days} days; each is rebuilt by its tool (npm install, cargo build…) when you need it.");
        if (!clean)
        {
            Console.WriteLine($"omawin disk projects clean{(rest.Count > 0 ? " " + string.Join(' ', rest) : "")}{(days != 30 ? " " + days : "")} deletes them, after asking.");
            return 0;
        }
        if (!Program.Ask($"Delete these {old.Count} folders ({DevCaches.Size(total)})?")) return 0;
        long freed = old.Sum(f => DiskScan.Delete(f.Folder));
        return Program.Say($"{DevCaches.Size(freed)} freed.");
    }

    // WSL's own switch (no admin): the distro is stopped first, which ends what runs in it, so asked.
    static int Sparse(string distro)
    {
        var disks = DiskScan.LinuxDisks(LocalAppData).Where(d => d.Name != "Docker Desktop").ToList();
        var match = disks.FirstOrDefault(d => d.Name.Equals(distro, StringComparison.OrdinalIgnoreCase));
        if (match == null) return Program.Fail($"which distro? {string.Join(", ", disks.Select(d => d.Name))}");
        if (match.Sparse) return Program.Say($"{match.Name}'s disk already gives space back.");
        if (!Program.Ask($"{match.Name} stops (what runs in it ends), then its disk gives freed space back to Windows from now on. Go ahead?")) return 0;
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        foreach (var a in new[] { $"--terminate \"{match.Name}\"", $"--manage \"{match.Name}\" --set-sparse true" })
        {
            using var p = Process.Start(new ProcessStartInfo(wsl, a) { UseShellExecute = false });
            p?.WaitForExit();
            if (p == null || p.ExitCode != 0) return Program.Fail($"wsl {a} failed (an older WSL? wsl --update)");
        }
        return Program.Say($"{match.Name}'s disk gives freed space back from now on (space already freed comes back as Linux trims it: sudo fstrim -av inside).");
    }

    // The program as PATH finds it (an exe, or a .cmd script), or null when it isn't installed.
    static string? Which(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => new[] { ".exe", ".cmd", ".bat" }.Select(ext => Path.Combine(dir.Trim('"'), program + ext)))
            .FirstOrDefault(File.Exists);
}
