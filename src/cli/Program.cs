using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Omawin.Cli;

// omawin: every command of this setup, by name (bin/commands.txt), like Omarchy's `omarchy`.
// CommandTable decides what the words mean and Actions what the row asks for; this file only
// does it: hands the menu app a request, starts a process, or runs a builtin.
static partial class Program
{
    // DOTFILES overrides where the repo is (as for the menu app); normally ~/omawin.
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Repo = Environment.GetEnvironmentVariable("DOTFILES") is { Length: > 0 } d ? d : Path.Combine(Home, "omawin");
    static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
    // The release this exe is from: CI passes -p:Version=<tag>; a local build says (dev).
    static readonly string Version = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        is [System.Reflection.AssemblyInformationalVersionAttribute { InformationalVersion: var v }] && !v.StartsWith("1.0.0") ? v.Split('+')[0] : "(dev)";

    // From a key (GlazeWM's --hide-window) there's no console: nothing to print to, and scripts
    // run hidden. Output to a pipe or file counts as a terminal.
    static readonly bool InTerminal = !OperatingSystem.IsWindows() || GetConsoleWindow() != 0 || Console.IsOutputRedirected;

    [LibraryImport("kernel32.dll")] private static partial nint GetConsoleWindow();
    [LibraryImport("user32.dll")] private static partial int AllowSetForegroundWindow(int processId);

    static int Main(string[] args)
    {
        if (InTerminal) try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        CommandTable table;
        try { table = CommandTable.Parse(File.ReadAllLines(Path.Combine(Repo, "bin", "commands.txt"), Encoding.UTF8)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (args is ["--version" or "-v"]) return Say("omawin " + Version);
            return Fail($"can't read bin/commands.txt in {Repo}: {ex.Message}");
        }

        switch (table.Resolve(args))
        {
            case ShowVersion: return Say("omawin " + Version);
            case ShowHelp h: return table.Help(h.Group) is { } text ? Say(text, newline: false) : Fail($"no group \"{h.Group}\". omawin help lists them.");
            case NotInGroup n:
                Fail($"no command \"{n.Typed}\"; {n.Group} has:");
                Say(table.Help(n.Group)!, newline: false);
                return 1;
            case Unknown u: return Fail($"no command \"{u.Typed}\". omawin help lists them.");
            case RunCommand r: return Run(Actions.Plan(r.Command, r.Extra, Repo, AccountFolder()));
            default: return Fail("unreachable");
        }
    }

    static int Run(Plan plan) => plan switch
    {
        SendToApp s => Send(s.Request),
        AskApp a => Ask(a.Request),
        StartProcess p => Start(p.File, p.Arguments),
        // Builtins move here one at a time (roadmap 2.11): bin/omawin.cs hands this exe (shipped
        // beside it as omawin-cli.exe) the ones it doesn't have, and still runs the rest itself.
        RunBuiltin { Name: "pkg" } b => new Omawin.Cli.Pkg.PkgCommand(new Omawin.Cli.Pkg.WindowsHost(Repo, StateDir)).Run(b.Args),
        RunBuiltin { Name: "profile-linux" } b => ProfileLinux(b.Args),
        RunBuiltin b => Fail($"builtin {b.Name} isn't in this omawin yet; bin/omawin.cs runs it"),
        BadAction bad => Fail(bad.Message),
        _ => Fail("unreachable"),
    };

    // omawin profile linux (roadmap 3.5). Its runs stop WSL, which ends the keep-warm too, so the
    // keep-warm is started again after (omawin start terminal: it only starts what isn't running).
    static int ProfileLinux(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return Fail("profile linux measures WSL, on Windows");
        var code = Omawin.Cli.Profile.LinuxProfile.Command(new Omawin.Cli.Profile.WslMachine(), args, Repo, StateDir, Console.Out);
        if (code == 0)
        {
            var omawin = Path.Combine(Home, ".local", "bin", "omawin.exe");
            try { if (File.Exists(omawin)) Process.Start(new ProcessStartInfo(omawin, "start terminal") { UseShellExecute = false, CreateNoWindow = true })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return code;
    }

    // This account's folder (docs/work.md): OMAWIN_USER_LAYER, else ~/omawin-account, else the
    // older ~/omawin-work where it hasn't moved yet.
    static string AccountFolder()
    {
        if (Environment.GetEnvironmentVariable("OMAWIN_USER_LAYER") is { Length: > 0 } set) return set;
        var now = Path.Combine(Home, "omawin-account");
        var before = Path.Combine(Home, "omawin-work");
        return !Directory.Exists(now) && Directory.Exists(before) ? before : now;
    }

    // In a terminal: here, waited for, its exit code ours. From a key: hidden, not waited for.
    static int Start(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = !InTerminal });
            if (process == null || !InTerminal) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return Fail($"{file}: {ex.Message}"); }
    }

    // A request for the always-running menu app: written to a file, then its event wakes it.
    // Started if it isn't running.
    static int Send(string request)
    {
        if (!OperatingSystem.IsWindows()) return Fail("the menu app runs on Windows only");
        if (EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out var signal))
        {
            using (signal)
            {
                Directory.CreateDirectory(StateDir);
                File.WriteAllText(Path.Combine(StateDir, "system-menu-request"), request);
                AllowSetForegroundWindow(-1); // ASFW_ANY: let it take focus
                signal.Set();
            }
            return 0;
        }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "system-menu", "SystemMenu.exe");
        if (!File.Exists(exe)) return Fail($"the menu app isn't built: {exe} (omawin restart menu builds it)");
        Process.Start(new ProcessStartInfo(exe, request) { UseShellExecute = false })?.Dispose();
        return 0;
    }

    // A request whose answer is printed (omawin setting get ...): the menu app writes it to a file
    // named for this process and sets an event named the same way (shell/menu-app/Reply.cs). The
    // wait covers a cold start of the menu app. Code 3 is a question: the text is shown, and on
    // yes the request goes again with --yes (omawin settings admin: the list, then the prompt).
    static int Ask(string request)
    {
        if (!OperatingSystem.IsWindows()) return Fail("the menu app runs on Windows only");
        var id = Environment.ProcessId.ToString();
        var reply = Path.Combine(StateDir, "replies", id + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reply)!);
        File.Delete(reply);
        using (var done = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\omawin-reply-" + id))
        {
            if (Send($"--reply {id} {request}") is var sent and not 0) return sent;
            if (!done.WaitOne(TimeSpan.FromSeconds(20))) return Fail("the menu app didn't answer (omawin restart menu)");
        }
        string[] lines;
        try { lines = File.ReadAllLines(reply, Encoding.UTF8); File.Delete(reply); }
        catch (IOException ex) { return Fail("no answer: " + ex.Message); }
        int code = lines.Length > 0 && int.TryParse(lines[0], out var c) ? c : 1;
        var text = string.Join(Environment.NewLine, lines.Skip(1));
        if (code == 3)
        {
            if (!InTerminal) return 1; // from a key there's no one to ask
            Console.WriteLine(text);
            Console.Write("Apply these? [y/N] ");
            if (Console.ReadLine()?.Trim().StartsWith('y') != true) { Console.WriteLine("Nothing changed."); return 1; }
            return Ask(request + " --yes");
        }
        if (InTerminal && text.Length > 0) (code == 0 ? Console.Out : Console.Error).WriteLine(text);
        return code;
    }

    static int Say(string text, bool newline = true)
    {
        if (InTerminal) { if (newline) Console.WriteLine(text); else Console.Write(text); }
        return 0;
    }

    static int Fail(string message)
    {
        if (InTerminal) Console.Error.WriteLine("omawin: " + message);
        return 1;
    }
}
