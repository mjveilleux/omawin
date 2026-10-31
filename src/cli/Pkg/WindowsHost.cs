using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Omawin.Cli.Pkg;

// The real PC: the managers found where they install themselves, run in this terminal.
public sealed class WindowsHost(string repo, string state) : Host
{
    public override string Repo => repo;
    public override string State => state;
    public override TextWriter Out => Console.Out;
    public override bool NoAdmin => Environment.GetEnvironmentVariable("OMAWIN_NO_ADMIN") is { Length: > 0 };

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
    static readonly string WindowsPowerShell = Path.Combine(System32, "WindowsPowerShell", "v1.0", "powershell.exe");

    // The PATH as it is now in the registry as well as this process's: a manager installed since
    // this terminal opened is found too.
    static IEnumerable<string> PathDirs()
    {
        var parts = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
        if (OperatingSystem.IsWindows())
        {
            parts.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            parts.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        }
        return parts.Where(p => p != null).SelectMany(p => p!.Split(Path.PathSeparator))
            .Select(d => Environment.ExpandEnvironmentVariables(d.Trim())).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static string? OnPathFirst(string file, params string[] alsoIn)
    {
        foreach (var dir in PathDirs().Concat(alsoIn))
        {
            try { var f = Path.Combine(dir, file); if (File.Exists(f)) return f; }
            catch (ArgumentException) { }
        }
        return null;
    }

    // Chocolatey's own copy, in the folder only admins can change: it's the one run elevated.
    static string? Choco()
    {
        var root = Environment.GetEnvironmentVariable("ChocolateyInstall") is { Length: > 0 } r ? r
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey");
        var exe = Path.Combine(root, "bin", "choco.exe");
        return File.Exists(exe) ? exe : null;
    }

    // Scoop is a PowerShell script (shims\scoop.ps1); its scoop.cmd runs it with Windows'
    // PowerShell, and so does this, without cmd.exe's quoting in between.
    static string? Scoop()
    {
        var root = Environment.GetEnvironmentVariable("SCOOP") is { Length: > 0 } s ? s : Path.Combine(Home, "scoop");
        var script = Path.Combine(root, "shims", "scoop.ps1");
        if (File.Exists(script)) return script;
        return OnPathFirst("scoop.ps1");
    }

    // What to start for a program name: the file and the arguments that go before the caller's.
    (string File, string[] Prefix)? Resolve(string program) => program switch
    {
        Managers.Winget => OnPathFirst("winget.exe", Path.Combine(Local, "Microsoft", "WindowsApps")) is { } w ? (w, []) : null,
        Managers.Scoop => Scoop() is { } s ? (WindowsPowerShell, ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", s]) : null,
        Managers.Choco => (Choco() ?? OnPathFirst("choco.exe")) is { } c ? (c, []) : null,
        Managers.Mise => OnPathFirst("mise.exe", Path.Combine(Local, "mise", "bin")) is { } m ? (m, []) : null,
        "wsl" => File.Exists(Path.Combine(System32, "wsl.exe")) ? (Path.Combine(System32, "wsl.exe"), []) : null,
        "pwsh" => OnPathFirst("pwsh.exe", Path.Combine(Local, "omawin", "tools", "pwsh")) is { } p ? (p, []) : null,
        "npm" => OnPathFirst("npm.cmd") is { } n ? (Path.Combine(System32, "cmd.exe"), ["/d", "/c", n]) : null,
        _ => null,
    };

    public override bool Has(string program) => Resolve(program) != null;

    ProcessStartInfo Info(string program, IReadOnlyList<string> args)
    {
        var (file, prefix) = Resolve(program) ?? throw new InvalidOperationException($"{program} isn't on this PC");
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in prefix.Concat(args)) info.ArgumentList.Add(a);
        return info;
    }

    public override (int Code, string Output) Capture(string program, IReadOnlyList<string> args)
    {
        try
        {
            var info = Info(program, args);
            info.RedirectStandardOutput = info.RedirectStandardError = true;
            info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
            info.CreateNoWindow = true;
            using var p = Process.Start(info)!;
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output + err.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return (-1, ex.Message); }
    }

    public override int Live(string program, IReadOnlyList<string> args)
    {
        try
        {
            using var p = Process.Start(Info(program, args))!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"  {program}: {ex.Message}");
            return -1;
        }
    }

    public override int? Elevated(string program, IReadOnlyList<string> args)
    {
        ProcessStartInfo info;
        if (program == "admin-batch")
        {
            // Windows' own PowerShell, never a per-user pwsh.exe: anything that can write to your
            // folder could swap that one (AGENTS.md). Hidden: it reports to its status file.
            string[] all = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(repo, "install", "admin-batch.ps1"), .. args];
            info = new ProcessStartInfo(WindowsPowerShell, string.Join(' ', all.Select(Actions.Quote))) { WindowStyle = ProcessWindowStyle.Hidden };
        }
        else if (program == Managers.Choco && Choco() is { } choco)
            info = new ProcessStartInfo(choco, string.Join(' ', args.Select(Actions.Quote)));
        else
        {
            Console.Error.WriteLine($"  {program}: not something omawin runs as administrator");
            return null;
        }
        info.UseShellExecute = true;
        info.Verb = "runas";
        try
        {
            using var p = Process.Start(info);
            if (p == null) return null;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception) { return null; }   // declined (ERROR_CANCELLED), or no admin to give it
    }

    public override IReadOnlyList<string> OnPath(string command)
    {
        var extensions = Path.HasExtension(command) ? [""]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<string>();
        foreach (var dir in PathDirs())
            foreach (var ext in extensions)
            {
                try { var f = Path.Combine(dir, command + ext); if (File.Exists(f) && !found.Contains(f, StringComparer.OrdinalIgnoreCase)) found.Add(f); }
                catch (ArgumentException) { }
            }
        return found;
    }

    public override (string Db, string Version)? FindWingetCatalog() => WingetCatalog.Find();
    public override List<string> ReadWingetCatalog(string db) => WingetCatalog.Read(db);

    public override string? Ask(string prompt)
    {
        if (Console.IsInputRedirected) return null;
        Console.Write(prompt);
        return Console.ReadLine()?.Trim();
    }
}
