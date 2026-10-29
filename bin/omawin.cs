// omawin: every command of this setup, by name (bin/commands.txt), like Omarchy's `omarchy`.
// The keys (GlazeWM), the system menu, the dock and you in a terminal all go through it:
//
//   omawin                      what there is (the same as omawin help)
//   omawin capture text         a command: its group, then its name
//   omawin theme set gruvbox    words after the command are passed on
//   omawin theme                that group's commands
//   omawin --version            the release this exe is from ((dev) built off a checkout)
//
// What a command does is its row in commands.txt: hand the always-running menu app a request
// (menu-app/, instant), run a PowerShell script of this repo, or start a program. In a
// terminal, scripts run right there and their output shows; from a key (GlazeWM runs it with
// --hide-window) they run hidden.
//
// A tiny native exe because starting PowerShell would bring a delay. install.ps1 compiles it
// into ~/.local/bin with the C# compiler that ships with Windows (C# 5: no newer syntax).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Omawin
{
    // DOTFILES overrides where the repo is (the menu app reads it too); normally ~/omawin.
    static readonly string Repo = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTFILES"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin")
        : Environment.GetEnvironmentVariable("DOTFILES");
    // This account's folder (docs/work.md), {user} in commands.txt. OMAWIN_USER_LAYER overrides where
    // it is, as for the menu app (MenuModel.Work) and PowerShell (bin/user-layer.ps1); normally
    // ~/omawin-account, or ~/omawin-work where it hasn't moved yet.
    static readonly string UserLayer = AccountFolder();
    static string AccountFolder()
    {
        var set = Environment.GetEnvironmentVariable("OMAWIN_USER_LAYER");
        if (!string.IsNullOrEmpty(set)) return set;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var now = Path.Combine(home, "omawin-account");
        var before = Path.Combine(home, "omawin-work");
        return !Directory.Exists(now) && Directory.Exists(before) ? before : now;
    }
    static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");

    // The release this exe is from (omawin --version): CI's build (build/helpers.ps1 -Release
    // <tag>, .github/workflows/release.yml) compiles it in under /define:OMAWIN_VERSION, with a
    // small generated BuildInfo class. Built off a checkout (install.ps1, CI without a tag) it
    // says (dev), which is how a download is told from a build.
#if OMAWIN_VERSION
    static readonly string Version = BuildInfo.Version;
#else
    static readonly string Version = "(dev)";
#endif

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

    // Run from a key there's no console and no output to write to: scripts then run hidden
    // instead of in a window of their own. (Output going to a pipe or file counts as a terminal.)
    static readonly bool InTerminal = GetConsoleWindow() != IntPtr.Zero ||
        (GetStdHandle(-11) != IntPtr.Zero && GetStdHandle(-11) != new IntPtr(-1)); // STD_OUTPUT_HANDLE

    class Command { public string Name, Summary, Action; public int Section; }

    // Timing, for profiling (omawin profile terminal): while %LOCALAPPDATA%\launcher\trace-on
    // exists, each step appends "<label> <QueryPerformanceCounter>" to trace.log there. Every
    // process on the PC shares that clock, so the PowerShell profile's marks line up with these.
    static readonly bool Tracing = File.Exists(Path.Combine(StateDir, "trace-on"));
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern void GetSystemTimePreciseAsFileTime(out long now);
    static void Trace(string label) { Trace(label, Stopwatch.GetTimestamp()); }
    static void Trace(string label, long at)
    {
        if (!Tracing) return;
        try { File.AppendAllText(Path.Combine(StateDir, "trace.log"), "omawin " + label + "\t" + at + "\n"); } catch (IOException) { }
    }

    static List<Command> Load()
    {
        var commands = new List<Command>();
        int section = 0; // commands.txt's blank lines make the sections of omawin help
        foreach (var raw in File.ReadAllLines(Path.Combine(Repo, "bin", "commands.txt"), Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0) section++;
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = line.Split(new[] { '|' }, 3);
            if (parts.Length < 3) continue;
            commands.Add(new Command { Name = parts[0].Trim(), Summary = parts[1].Trim(), Action = parts[2].Trim(), Section = section });
        }
        return commands;
    }

    static int Main(string[] args)
    {
        if (Tracing)
        {
            // When Windows created this process, on the same clock: its creation time against
            // the precise system time now (DateTime.Now is too coarse here, up to 15 ms).
            long created, exited, kernel, user, now;
            GetProcessTimes(Process.GetCurrentProcess().Handle, out created, out exited, out kernel, out user);
            GetSystemTimePreciseAsFileTime(out now);
            Trace("process-created", Stopwatch.GetTimestamp() - (long)((now - created) / 1e7 * Stopwatch.Frequency));
            Trace("main " + string.Join(" ", args));
        }
        // Output to a pipe with no console: there's no code page to set, and that's fine.
        if (InTerminal) try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            if (InTerminal) Console.WriteLine("omawin " + Version);
            return 0;
        }
        List<Command> commands;
        try { commands = Load(); }
        catch (Exception ex) { return Fail("can't read bin/commands.txt in " + Repo + ": " + ex.Message); }

        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
            return Help(commands, args.Length > 1 ? args[1] : null);

        // The longest command the words spell: "theme set gruvbox" is "theme set" + gruvbox.
        for (int n = Math.Min(args.Length, 3); n > 0; n--)
        {
            var name = string.Join(" ", args, 0, n);
            var command = commands.FirstOrDefault(c => c.Name == name);
            if (command != null) return Run(command, args.Skip(n).ToArray());
        }
        // A group alone lists its commands; a group with a name it doesn't have is a mistake.
        if (commands.Any(c => c.Name.StartsWith(args[0] + " ")))
        {
            if (args.Length == 1) return Help(commands, args[0]);
            Fail("no command \"" + string.Join(" ", args) + "\"; " + args[0] + " has:");
            Help(commands, args[0]);
            return 1;
        }
        return Fail("no command \"" + string.Join(" ", args) + "\". omawin help lists them.");
    }

    static int Run(Command command, string[] extra)
    {
        var action = command.Action.Replace("{repo}", Repo);
        var space = action.IndexOf(' ');
        var kind = space < 0 ? action : action.Substring(0, space);
        var rest = space < 0 ? "" : action.Substring(space + 1).Trim();
        var words = extra.Select(Quote);
        switch (kind)
        {
            case "app":
                return Send((rest + " " + string.Join(" ", extra)).Trim());
            case "pwsh":
                // A script of this repo, path from the repo root (or from your account folder, {user}/...),
                // then its fixed arguments. {user} is filled in after the split: the path may have spaces.
                var script = rest.Split(new[] { ' ' }, 2);
                var scriptPath = script[0].Replace("{user}", UserLayer).Replace('/', '\\');
                var arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, scriptPath))
                    + (script.Length > 1 ? " " + script[1] : "") + " " + string.Join(" ", words);
                return Start("pwsh", arguments);
            case "run":
                var program = rest.Split(new[] { ' ' }, 2);
                return Start(program[0], ((program.Length > 1 ? program[1] : "") + " " + string.Join(" ", words)).Trim());
            case "builtin":
                var fixedArgs = Split(rest);
                return Builtin(fixedArgs[0], fixedArgs.Skip(1).Concat(extra).ToArray());
            default:
                return Fail("commands.txt: \"" + command.Name + "\" has an unknown action: " + command.Action);
        }
    }

    // In a terminal: here, waiting for it, its exit code ours. From a key: hidden, not waited for.
    static int Start(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = !InTerminal };
        try
        {
            var process = Process.Start(info);
            if (!InTerminal) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) { return Fail(file + ": " + ex.Message); }
    }

    // A request for the menu app (launcher, file finder, system menu, screenshots), which runs
    // hidden all the time: written to a file, then its event wakes it (the same way a second
    // SystemMenu.exe would, without .NET's startup time). Started if it isn't running.
    static int Send(string request)
    {
        EventWaitHandle signal;
        if (EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out signal))
        {
            Directory.CreateDirectory(StateDir);
            File.WriteAllText(Path.Combine(StateDir, "system-menu-request"), request);
            AllowSetForegroundWindow(-1); // let it take focus
            signal.Set();
            signal.Dispose();
            return 0;
        }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "system-menu", "SystemMenu.exe");
        if (!File.Exists(exe)) return Fail("the menu app isn't built: " + exe + " (omawin restart menu builds it)");
        Process.Start(new ProcessStartInfo(exe, request) { UseShellExecute = false });
        return 0;
    }

    static int Builtin(string name, string[] args)
    {
        switch (name)
        {
            case "launch-terminal": return LaunchTerminal(args);
            case "launch-project": return LaunchProject(args);
            case "launch-editor": return LaunchEditor(args);
            case "terminal-here": return TerminalHere();
            case "terminal-warm": return TerminalWarm();
            case "start-glazewm": return StartGlazeWM();
            case "start-zebar": return StartZebar();
            case "update-terminal":
                {
                    var t = Current("terminal");
                    if (t.Winget.EndsWith(".ps1"))
                        return Start("pwsh", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, t.Winget.Replace('/', '\\'))));
                    return Start("winget", "upgrade --id " + t.Winget + " --exact --accept-source-agreements --accept-package-agreements");
                }
            case "default": return Default(args);
            default: return Fail("no builtin \"" + name + "\"");
        }
    }

    // --- defaults: what omawin opens things with (bin/defaults.txt) ---------------------------
    // Like Omarchy's omarchy-default-terminal: the pick is per PC, a file per kind in
    // %LOCALAPPDATA%\launcher\defaults; without one, the kind's first row in defaults.txt.

    class Choice { public string Kind, Name, Label, Winget, Program; }

    static List<Choice> Choices()
    {
        var list = new List<Choice>();
        foreach (var raw in File.ReadAllLines(Path.Combine(Repo, "bin", "defaults.txt"), Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split('|').Select(s => s.Trim()).ToArray();
            if (p.Length >= 5) list.Add(new Choice { Kind = p[0], Name = p[1], Label = p[2], Winget = p[3], Program = p[4] });
        }
        return list;
    }

    static string DefaultFile(string kind) { return Path.Combine(StateDir, "defaults", kind); }

    static Choice Current(string kind)
    {
        var choices = Choices().Where(c => c.Kind == kind).ToList();
        string picked = null;
        try { if (File.Exists(DefaultFile(kind))) picked = File.ReadAllText(DefaultFile(kind)).Trim(); } catch (IOException) { }
        return choices.FirstOrDefault(c => c.Name == picked) ?? choices.FirstOrDefault();
    }

    // A program's full path: the per-user noctty first (bin/noctty.ps1), then
    // PATH as it is now in the registry (a key's GlazeWM may have been started before the
    // program was installed), then where these installers put them.
    static string Find(string program)
    {
        var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) + ";" +
                   Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) + ";" +
                   Environment.GetEnvironmentVariable("Path");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var places = new[] { Path.Combine(local, "Programs", "noctty") }.Concat(path.Split(';')).Concat(new[] {
            Path.Combine(programFiles, "Neovide"), Path.Combine(programFiles, "Neovim", "bin"),
            Path.Combine(local, "Microsoft", "WindowsApps"), Path.Combine(local, "Programs", "Microsoft VS Code", "bin") });
        foreach (var dir in places)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var file = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), program);
                if (File.Exists(file)) return file;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    //   omawin default                     every kind's pick
    //   omawin default terminal            this kind's pick and the choices
    //   omawin default terminal noctty     pick it (installed with winget first if missing)
    static int Default(string[] args)
    {
        var choices = Choices();
        if (args.Length == 0)
        {
            foreach (var kind in choices.Select(c => c.Kind).Distinct())
            {
                var c = Current(kind);
                Say("  " + kind.PadRight(10) + c.Name + "  (" + c.Label + ")");
            }
            return 0;
        }
        var ofKind = choices.Where(c => c.Kind == args[0]).ToList();
        if (ofKind.Count == 0) return Fail("no default \"" + args[0] + "\"; there are: " + string.Join(", ", choices.Select(c => c.Kind).Distinct()));
        if (args.Length == 1)
        {
            var current = Current(args[0]);
            foreach (var c in ofKind) Say((c.Name == current.Name ? "* " : "  ") + c.Name.PadRight(10) + c.Label);
            return 0;
        }
        var pick = ofKind.FirstOrDefault(c => c.Name == args[1]);
        if (pick == null) return Fail("no " + args[0] + " \"" + args[1] + "\"; the choices: " + string.Join(", ", ofKind.Select(c => c.Name)));
        // A Linux is there when WSL has the distro; anything else when its program is found.
        var linux = args[0] == "linux";
        Func<bool> installed = () => linux ? DistroRegistered(pick.Program) : Find(pick.Program) != null;
        if (!installed())
        {
            // Install it where you can watch: here in a terminal, or in a small one from a key.
            if (!InTerminal) return LaunchTerminal(new[] { "--title", "menu-task", "--", "pwsh", "-NoLogo", "-NoExit", "-Command", "omawin default " + args[0] + " " + pick.Name });
            Say("installing " + pick.Label);
            var code = pick.Winget.EndsWith(".ps1")
                ? Start("pwsh", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, pick.Winget.Replace('/', '\\'))) + (linux ? " -Linux " + pick.Name : ""))
                : Start("winget", "install --id " + pick.Winget + " --exact --accept-source-agreements --accept-package-agreements");
            if (code != 0 || !installed()) return Fail(pick.Label + " didn't install; the default stays " + Current(args[0]).Name);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultFile(args[0])));
        File.WriteAllText(DefaultFile(args[0]), pick.Name);
        Say(pick.Label + " is now the default " + args[0]);
        return 0;
    }

    // --- launch: a terminal, a project, an editor, in whichever is the default -------------
    // The Linux in WSL that --wsl opens: the pick of `omawin default linux` (its WSL name).
    static string Distro { get { var c = Current("linux"); return c != null ? c.Program : "archlinux"; } }

    // Whether WSL has a distro of that name for this account (its registry, so WSL isn't started).
    static bool DistroRegistered(string name)
    {
        try
        {
            using (var lxss = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
            {
                if (lxss == null) return false;
                foreach (var id in lxss.GetSubKeyNames())
                    using (var d = lxss.OpenSubKey(id))
                        if (d != null && string.Equals(d.GetValue("DistributionName") as string, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (System.Security.SecurityException) { }
        return false;
    }

    class Launch { public string Dir, Title; public bool Wsl; public string[] Command = new string[0]; }

    //   [--dir <folder>] [--title <title>] [--wsl] [-- <command> <args>]
    static Launch ParseLaunch(string[] args)
    {
        var l = new Launch();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--") { l.Command = args.Skip(i + 1).ToArray(); break; }
            if (args[i] == "--dir" && i + 1 < args.Length) l.Dir = args[++i];
            else if (args[i] == "--title" && i + 1 < args.Length) l.Title = args[++i];
            else if (args[i] == "--wsl") l.Wsl = true;
            else if (l.Dir == null) l.Dir = args[i];
        }
        return l;
    }

    static string Terminal(out Choice choice)
    {
        choice = Current("terminal");
        var exe = Find(choice.Program);
        if (exe == null && choice.Name != "wt") { choice = Choices().First(c => c.Name == "wt"); exe = Find(choice.Program); }
        return exe;
    }

    static int LaunchTerminal(string[] args)
    {
        var l = ParseLaunch(args);
        Choice terminal;
        var exe = Terminal(out terminal);
        if (exe == null) return Fail("no terminal found (omawin default terminal noctty installs one)");
        if (terminal.Name == "noctty") return NocttyWindow(exe, l);
        // Windows Terminal.
        var a = new List<string> { "-w", "new" };
        if (l.Title != null) { a.AddRange(new[] { "--title", l.Title, "--suppressApplicationTitle" }); }
        if (l.Wsl)
        {
            a.AddRange(new[] { "wsl.exe", "-d", Distro });
            if (l.Dir != null) { a.Add("--cd"); a.Add(l.Dir); }
            if (l.Command.Length > 0) { a.Add("--"); a.AddRange(l.Command); }
        }
        else
        {
            if (l.Dir != null) { a.Add("-d"); a.Add(l.Dir); }
            a.AddRange(l.Command);
        }
        return Detach(exe, a);
    }

    // { title, command (null: a shell), color } per tab, from launcher/project-tabs.txt.
    static List<string[]> ProjectTabs()
    {
        var tabs = new List<string[]>();
        foreach (var raw in File.ReadAllLines(Path.Combine(Repo, "launcher", "project-tabs.txt"), Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split('|').Select(s => s.Trim()).ToArray();
            tabs.Add(new[] { p[0], p.Length > 1 && p[1].Length > 0 ? p[1] : null, p.Length > 2 ? p[2] : "" });
        }
        return tabs;
    }

    static string Session(string path)
    {
        var name = path.TrimEnd('\\', '/');
        name = name.Substring(name.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        return System.Text.RegularExpressions.Regex.Replace(name, @"[^\w.-]", "-");
    }

    // A tab's program: its command in a shell that stays open after it (quit nvim and you're at
    // a prompt in the project).
    static string[] TabCommand(string command, bool wsl)
    {
        if (command == null) return new string[0];
        if (wsl) return new[] { "bash", "-lic", command + "; exec bash" };
        return new[] { "pwsh.exe", "-NoLogo", "-NoExit", "-Command", command };
    }

    // --- projects in herdr ---------------------------------------------------------------------
    // With noctty, a project is a herdr session named after the folder (herdr: the multiplexer,
    // and it keeps sessions running with no window, so agents carry on and reopening the project
    // reattaches). A session that isn't running is started headless and, if it comes up empty,
    // given a tab per launcher/project-tabs.txt line before the window opens; then a noctty
    // window attaches to it. Linux projects (WSL) use the herdr there, the same way.

    // A program on the PATH a new sign-in would have (herdr's installer puts a versioned folder
    // there, so the PATH omawin inherited can be out of date).
    static string OnFreshPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) + ";" +
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        foreach (var dir in Environment.ExpandEnvironmentVariables(path).Split(';'))
        {
            if (dir.Trim().Length == 0) continue;
            try { var file = Path.Combine(dir.Trim(), name); if (File.Exists(file)) return file; }
            catch (ArgumentException) { }
        }
        return null;
    }

    static int HerdrProject(string noctty, Launch l)
    {
        var session = Session(l.Dir);
        var herdr = l.Wsl ? "wsl.exe" : OnFreshPath("herdr.exe");
        if (herdr == null) return Fail("herdr isn't installed (install.ps1 -Step software installs it)");
        Func<string[], string> call = args =>
        {
            string output;
            Capture(herdr, HerdrArgs(l, Prepend(new[] { "--session", session }, args), false), out output);
            return output;
        };

        // No reply (herdr reports "server_not_running" on stderr): start the session.
        if (!call(new[] { "tab", "list" }).Contains("\"result\""))
        {
            Detach(herdr, HerdrArgs(l, new[] { "--session", session, "server" }, true));
            Trace("herdr-server-started");
        }
        string tabs = "";
        var waited = Stopwatch.StartNew();
        while (waited.ElapsedMilliseconds < 8000)
        {
            tabs = call(new[] { "tab", "list" });
            if (tabs.Contains("\"result\"")) break;
            Thread.Sleep(50);
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(tabs, "\"tabs\":\\s*\\[\\s*\\]")) SetUpHerdrProject(call, l.Dir, session);
        Trace("herdr-ready");

        var attach = l.Wsl ? new[] { "bash", "-lc", "herdr --session " + ShellQuote(session) } : new[] { herdr, "--session", session };
        return NocttyWindow(noctty, new Launch { Dir = l.Dir, Wsl = l.Wsl, Title = session, Command = attach });
    }

    // herdr's arguments as a command line for `file`: herdr itself on Windows; in Linux, wsl.exe
    // running it through a login shell (~/.local/bin, where herdr lives, is only on a login
    // shell's PATH), in the project folder when `inFolder`.
    static List<string> HerdrArgs(Launch l, string[] args, bool inFolder)
    {
        if (!l.Wsl) return args.ToList();
        var a = new List<string> { "-d", Distro };
        if (inFolder) { a.Add("--cd"); a.Add(l.Dir); }
        a.AddRange(new[] { "-e", "bash", "-lc", "herdr " + string.Join(" ", args.Select(ShellQuote)) });
        return a;
    }

    static string ShellQuote(string word) { return "'" + word.Replace("'", "'\\''") + "'"; }

    static string[] Prepend(string[] first, string[] rest) { return first.Concat(rest).ToArray(); }
    // A new session's workspace: the project folder, a tab per launcher/project-tabs.txt line
    // (the first in front), each tab's command typed into its shell, so quitting it leaves a
    // prompt in the project.
    static void SetUpHerdrProject(Func<string[], string> call, string dir, string session)
    {
        var tabs = ProjectTabs();
        if (tabs.Count == 0) return;
        var created = call(new[] { "workspace", "create", "--cwd", dir, "--label", session });
        var workspace = System.Text.RegularExpressions.Regex.Match(created, "\"workspace_id\":\\s*\"([^\"]+)\"").Groups[1].Value;
        var pane = PaneId(created);
        var tab = System.Text.RegularExpressions.Regex.Match(created, "\"tab_id\":\\s*\"([^\"]+)\"").Groups[1].Value;
        if (workspace.Length == 0 || pane == null) return;
        call(new[] { "tab", "rename", tab, tabs[0][0] });
        if (tabs[0][1] != null) call(new[] { "pane", "run", pane, tabs[0][1].Replace("{session}", session) });
        for (int i = 1; i < tabs.Count; i++)
        {
            var next = call(new[] { "tab", "create", "--workspace", workspace, "--cwd", dir, "--label", tabs[i][0], "--no-focus" });
            var nextPane = PaneId(next);
            if (nextPane != null && tabs[i][1] != null) call(new[] { "pane", "run", nextPane, tabs[i][1].Replace("{session}", session) });
        }
    }

    // The new root pane's id in a herdr creation reply ("root_pane": { ... "pane_id": "w1:p1" }).
    static string PaneId(string json)
    {
        var m = System.Text.RegularExpressions.Regex.Match(json, "\"root_pane\":\\s*\\{.*?\"pane_id\":\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    //   <folder> [--wsl]: a window with a tab per launcher/project-tabs.txt line (noctty: a herdr
    //   session named after the project, reattached if it's already running).
    static int LaunchProject(string[] args)
    {
        var l = ParseLaunch(args);
        if (l.Dir == null) return Fail("which folder? omawin launch project <folder> [--wsl]");
        Choice terminal;
        var exe = Terminal(out terminal);
        if (exe == null) return Fail("no terminal found");

        if (terminal.Name == "noctty") return HerdrProject(exe, l);

        // Windows Terminal.
        var session = Session(l.Dir);
        var tabs = ProjectTabs();
        var a = new List<string> { "-w", "new" };
        for (int i = 0; i < tabs.Count; i++)
        {
            if (i > 0) { a.Add(";"); a.Add("new-tab"); }
            a.AddRange(new[] { "--title", tabs[i][0], "--suppressApplicationTitle" });
            if (tabs[i][2].Length > 0) { a.Add("--tabColor"); a.Add(tabs[i][2]); }
            var command = tabs[i][1] == null ? null : tabs[i][1].Replace("{session}", session);
            if (l.Wsl) { a.AddRange(new[] { "wsl.exe", "-d", Distro, "--cd", l.Dir }); if (command != null) { a.Add("--"); a.AddRange(TabCommand(command, true)); } }
            else { a.Add("-d"); a.Add(l.Dir); a.AddRange(command == null ? new[] { "pwsh.exe", "-NoLogo" } : TabCommand(command, false)); }
        }
        a.AddRange(new[] { ";", "focus-tab", "-t", "0" });
        return Detach(exe, a);
    }

    // Run it hidden and wait (up to 5 s); its output and exit code.
    static int Capture(string file, List<string> args, out string output)
    {
        output = "";
        try
        {
            var info = new ProcessStartInfo(file, string.Join(" ", args.Select(Quote)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            var p = Process.Start(info);
            var read = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch (InvalidOperationException) { } return -1; }
            output = read.Result;
            return p.ExitCode;
        }
        catch (Exception) { return -1; }
    }

    // noctty: a plain shell is a new window of the running instance (+new-window: fast, no
    // start-up); anything that runs a command (a Linux shell, a project's nvim) is a window
    // of its own process, since the running one refuses commands from outside by design.
    static int NocttyWindow(string exe, Launch l)
    {
        var a = new List<string>();
        if (l.Command.Length == 0 && !l.Wsl)
        {
            a.Add("+new-window");
            a.Add("--working-directory=" + (l.Dir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd('\\'));
            if (l.Title != null) a.Add("--title=" + l.Title);
            Trace("noctty-new-window");
            return Detach(Path.Combine(Path.GetDirectoryName(exe), "noctty.com"), a);
        }
        a.Add("--single-instance=false");
        // A one-off process: no spare shell kept ready for windows it will never open.
        a.Add("--windows-spare-shell=false");
        if (l.Title != null) a.Add("--title=" + l.Title);
        if (l.Wsl)
        {
            a.AddRange(new[] { "-e", "wsl.exe", "-d", Distro });
            if (l.Dir != null) { a.Add("--cd"); a.Add(l.Dir); }
            if (l.Command.Length > 0) { a.Add("--"); a.AddRange(l.Command); }
        }
        else
        {
            if (l.Dir != null) a.Add("--working-directory=" + l.Dir.TrimEnd('\\'));
            a.Add("-e");
            a.AddRange(l.Command);
        }
        return Detach(exe, a);
    }

    // --- GlazeWM at sign-in -----------------------------------------------------------------------
    // The sign-in task (install/login-start.ps1) runs this. The official GlazeWM crashes (aborts) when Explorer
    // sets up the taskbar under it: started at the moment of sign-in, 3 s before Explorer, or
    // while Explorer restarts, it dies within seconds (same crash every time). So wait until
    // Explorer is ready: its taskbar exists, its ShellReadyEvent is set (a new session starts with
    // it unset) and it has been up a moment (after an Explorer restart the event is still set
    // from before). Then start GlazeWM through the shell (its manifest asks for uiAccess, which a
    // plain start can't give: 0x800702E4), watch it for 15 s, and start it again if it dies.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenEvent(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static bool ExplorerReady()
    {
        if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero) return false;
        var ready = OpenEvent(0x00100000, false, "ShellReadyEvent"); // SYNCHRONIZE
        if (ready == IntPtr.Zero) return false;
        bool set = WaitForSingleObject(ready, 0) == 0;
        CloseHandle(ready);
        if (!set) return false;
        var explorer = InSession("explorer").OrderBy(p => StartedAt(p)).FirstOrDefault();
        return explorer != null && (DateTime.Now - StartedAt(explorer)).TotalMilliseconds > 1500;
    }

    // A program's processes in this sign-in session only: another account signed in on the same
    // PC (fast user switching) has its own Explorer and GlazeWM.
    static Process[] InSession(string name)
    {
        int session = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName(name).Where(p =>
        {
            try { return p.SessionId == session; }
            catch (Exception) { return false; }
        }).ToArray();
    }

    // When it started; a process that's gone (or can't be asked) counts as long running.
    static DateTime StartedAt(Process p)
    {
        try { return p.StartTime; }
        catch (Exception) { return DateTime.MinValue; }
    }

    // This account's own ports for GlazeWM and Zebar (bin/ports.ps1 says why and how), decided at
    // every sign-in from what Program Files holds now: every account shares those files, and
    // another one may have put the forks in, or the official programs back, since this one last
    // looked. Set for GlazeWM (and so the bar and the menu app it starts) and saved for this
    // account, so what starts from Explorer gets them too. The forks are in when the fork
    // installers' kept official files are there (from omawin.7, which replaced the CLI too) and
    // what runs isn't the official build again: an upgrade that got past the winget pin puts the
    // official files back and leaves the kept ones behind. Never stops the desktop from starting.
    static void SetPorts(bool fork)
    {
        try
        {
            var glzr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "glzr.io");
            var zebarExe = new FileInfo(Path.Combine(glzr, @"Zebar\zebar.exe"));
            var zebarKept = new FileInfo(Path.Combine(glzr, @"Zebar\zebar.official.exe"));
            bool ready = fork
                && File.Exists(Path.Combine(glzr, @"GlazeWM\cli\glazewm.official.exe"))
                && zebarExe.Exists && zebarKept.Exists && zebarExe.Length != zebarKept.Length;
            string glazewm = null, zebar = null;
            if (ready)
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                long number = long.Parse(sid.Substring(sid.LastIndexOf('-') + 1));
                long port = 22000 + 2 * (number % 4000);
                glazewm = port.ToString();
                zebar = (port + 1).ToString();
            }
            SetPort("GLAZEWM_IPC_PORT", glazewm);
            SetPort("ZEBAR_ASSET_PORT", zebar);
        }
        catch (Exception) { }
    }

    // At every sign-in, before Claude Code is up to hold its skill folders open: finish moving
    // your settings folders if a running program kept the move from happening
    // (bin/move-settings.ps1), then Claude Code's rules, skills and settings from the shared
    // folder and this account's (bin/claude-sync.ps1; another account may have added a shared
    // skill). Hidden, in the background; nothing waits for it.
    static void SyncClaude()
    {
        var move = Path.Combine(Repo, @"bin\move-settings.ps1");
        var sync = Path.Combine(Repo, @"bin\claude-sync.ps1");
        if (!File.Exists(sync)) return;
        // A failed move must not cost the sync; a ' in a path is doubled for PowerShell's quotes.
        var command = (File.Exists(move) ? "try { & '" + move.Replace("'", "''") + "' -Quiet } catch { }; " : "")
            + "& '" + sync.Replace("'", "''") + "'";
        try
        {
            Process.Start(new ProcessStartInfo("pwsh",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Repo });
        }
        catch (Exception) { }
    }

    static void SetPort(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            // Saved only when it changed: saving tells every window, which takes a moment.
            if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) != value)
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        }
        catch (Exception) { }
    }

    static int StartGlazeWM()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "glzr.io", "GlazeWM", "glazewm.exe");
        if (!File.Exists(exe)) return Fail("GlazeWM isn't installed (" + exe + ")");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Our fork (install/glazewm-fork.ps1, signed on this PC) starts without the taskbar, so
        // only the official build waits for Explorer.
        bool fork = false;
        try { fork = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exe).Subject.StartsWith("CN=omawin code signing"); }
        catch (Exception) { }
        SetPorts(fork);
        SyncClaude();
        // The menu app (the launcher, and the native bar) from here, alongside GlazeWM rather than
        // after it: it takes seconds to come up at sign-in. GlazeWM's own `omawin start` then
        // finds it running. (After SetPorts: it reads GlazeWM's port.)
        try { Send("--start-hidden"); Trace("menu-started"); } catch (Exception) { }
        for (int attempt = 1; attempt <= 6; attempt++)
        {
            var waited = Stopwatch.StartNew();
            while (!fork && !ExplorerReady() && waited.ElapsedMilliseconds < 90000) Thread.Sleep(25);
            Trace("explorer-ready");
            if (InSession("glazewm").Length == 0)
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = home }); }
                catch (Exception) { }
                Trace("glazewm-started");
            }
            // Up if it's still running 15 s later; otherwise go again.
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 15000 && (InSession("glazewm").Length > 0 || watch.ElapsedMilliseconds < 2000))
                Thread.Sleep(100);
            if (InSession("glazewm").Length > 0) return 0;
            Trace("glazewm-exited");
            Thread.Sleep(1000);
        }
        return Fail("GlazeWM kept exiting at start");
    }
    // --- the bar at sign-in -----------------------------------------------------------------------
    // GlazeWM runs this at login (omawin start zebar). zebar/start.ps1 first looks for an old bar
    // to stop, which costs ~2 s of PowerShell and queries; at sign-in there's none, so the bar
    // starts straight away. If one is running (GlazeWM restarted), or the theme was never
    // rendered, or the bar's server doesn't come up, the script does it all as before.
    static int StartZebar()
    {
        // The native bar picked (omawin bar native): the menu app draws it, so no Zebar; the strip
        // covers the wait for its window (menu-app/Bar/BarWindow.cs, "omawin bar").
        var picked = Path.Combine(StateDir, "bar");
        if (File.Exists(picked) && File.ReadAllText(picked).Trim() == "native")
        {
            var cover = Strip.Show(Path.Combine(Repo, @"zebar\tokyo-islands\theme.css"));
            var waited = Stopwatch.StartNew();
            while (FindWindow(null, "omawin bar") == IntPtr.Zero && waited.ElapsedMilliseconds < 30000) Thread.Sleep(50);
            Trace("bar-native-up");
            Thread.Sleep(300); // its first paint
            cover.Close();
            return 0;
        }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"glzr.io\Zebar\zebar.exe");
        var script = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(Repo, @"zebar\start.ps1"));
        int port;
        if (!int.TryParse(Environment.GetEnvironmentVariable("ZEBAR_ASSET_PORT"), out port)) port = 6124;
        // The port taken already is a leftover too (the bar's music feed can hold it).
        if (!File.Exists(exe) || !File.Exists(Path.Combine(Repo, @"zebar\tokyo-islands\theme.css"))
            || InSession("zebar").Length > 0 || ZebarWebviewsLeft() || Listening(port))
            return Start("pwsh", script);
        var strip = Strip.Show(Path.Combine(Repo, @"zebar\tokyo-islands\theme.css"));
        try { return StartZebar(exe, script, port); }
        finally { strip.Close(); }
    }

    static int StartZebar(string exe, string script, int port)
    {
        Process zebar;
        try
        {
            // As zebar/start.ps1 says: software drawing, ~100 MB less for the same look. Set here
            // and inherited, and started as the script's Start-Process does (shell, hidden).
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-gpu");
            zebar = Process.Start(new ProcessStartInfo(exe, "startup") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (Exception) { return Start("pwsh", script); }
        Trace("zebar-started");
        // Its widgets load from its own server; if that never listens, they'd stay blank. At
        // sign-in, with everything else starting, it has taken from 4 s to over 6 s: 20 s here,
        // as the script it falls back to costs far more (~40 s of PowerShell at sign-in).
        var waited = Stopwatch.StartNew();
        while (waited.ElapsedMilliseconds < 20000)
        {
            if (Listening(port))
            {
                Trace("zebar-serving");
                // The strip stays until Zebar's page has drawn over it: it loads from that
                // server, all from disk, in well under this.
                Thread.Sleep(1500);
                return 0;
            }
            if (zebar.HasExited) { Trace("zebar-exited"); break; }
            Thread.Sleep(100);
        }
        Trace("zebar-fallback");
        try { zebar.Kill(); } catch (Exception) { }
        return Start("pwsh", script);
    }

    // The bar's strip, drawn at once while the bar starts (seconds at sign-in): a plain band in
    // the theme's background, the bar's size, across the top of every monitor. The bar's own
    // window opens above it and draws the same band with its widgets; then this goes. Plain
    // Win32 (a window whose class brush is the colour): WinForms took ~2.4 s to load at sign-in.
    class Strip
    {
        delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS
        {
            public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName;
        }
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc proc, IntPtr data);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int colorRef);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);

        static readonly WndProc Proc = DefWindowProc; // kept, so it isn't collected while windows use it
        readonly List<IntPtr> bands = new List<IntPtr>();
        readonly ManualResetEvent shown = new ManualResetEvent(false);
        uint threadId;

        public static Strip Show(string themeCss)
        {
            var strip = new Strip();
            int colorRef = ColorRef(ThemeBackground(themeCss));
            var thread = new Thread(() =>
            {
                try
                {
                    strip.threadId = GetCurrentThreadId();
                    SetProcessDpiAwarenessContext(new IntPtr(-4)); // per monitor: real pixels
                    var instance = GetModuleHandle(IntPtr.Zero);
                    var cls = new WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc), hInstance = instance, hbrBackground = CreateSolidBrush(colorRef), lpszClassName = "omawin strip" };
                    RegisterClass(ref cls);
                    var screens = new List<RECT>();
                    var dpis = new List<uint>();
                    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT r, IntPtr data) =>
                    {
                        uint dpi = 96, ignored;
                        try { GetDpiForMonitor(monitor, 0, out dpi, out ignored); } catch (Exception) { }
                        screens.Add(r);
                        dpis.Add(dpi);
                        return true;
                    }, IntPtr.Zero);
                    for (int i = 0; i < screens.Count; i++)
                    {
                        var r = screens[i];
                        // A popup tool window that's never activated (GlazeWM leaves it alone), shown at once.
                        var band = CreateWindowEx(0x80 | 0x08000000, "omawin strip", "omawin strip", 0x80000000 | 0x10000000,
                            r.Left, r.Top, r.Right - r.Left, (int)Math.Round(26 * dpis[i] / 96.0), IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                        if (band != IntPtr.Zero) lock (strip.bands) strip.bands.Add(band);
                    }
                    Trace("strip-shown");
                }
                catch (Exception) { }
                strip.shown.Set();
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == 0x8001) break; // Close: done
                    DispatchMessage(ref msg);
                }
                lock (strip.bands) foreach (var b in strip.bands) DestroyWindow(b);
            }) { IsBackground = true };
            thread.Start(); // not waited for: the bar's window takes far longer to open
            return strip;
        }

        // #rrggbb as GDI's COLORREF (0x00bbggrr).
        static int ColorRef(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                int r = Convert.ToInt32(hex.Substring(0, 2), 16), g = Convert.ToInt32(hex.Substring(2, 2), 16), b = Convert.ToInt32(hex.Substring(4, 2), 16);
                return r | (g << 8) | (b << 16);
            }
            catch (Exception) { return 0; }
        }

        // theme.css's --bg-solid, the bar's background (themes/render.ps1 writes it).
        static string ThemeBackground(string themeCss)
        {
            try
            {
                foreach (var line in File.ReadAllLines(themeCss))
                {
                    var t = line.Trim();
                    if (t.StartsWith("--bg-solid:")) return t.Substring(11).Trim().TrimEnd(';').Trim();
                }
            }
            catch (Exception) { }
            return "#000000";
        }

        public void Close()
        {
            shown.WaitOne(2000);
            PostThreadMessage(threadId, 0x8001, IntPtr.Zero, IntPtr.Zero);
        }
    }
    // A bar stopped by force (GlazeWM's shutdown) can leave its webviews running, and a new bar
    // started beside them comes up blank. They hold their profile's lockfile open, so a lockfile
    // that can't be opened means some are left (the script waits for them).
    static bool ZebarWebviewsLeft()
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"zebar\webview-cache");
        if (!Directory.Exists(cache)) return false;
        foreach (var dir in Directory.GetDirectories(cache))
        {
            var lockfile = Path.Combine(dir, @"EBWebView\lockfile");
            if (!File.Exists(lockfile)) continue;
            try { using (File.Open(lockfile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        return false;
    }

    static bool Listening(int port)
    {
        try
        {
            return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(e => e.Port == port);
        }
        catch (Exception) { return false; }
    }

    // --- the terminal, kept running ---------------------------------------------------------------
    // GlazeWM runs this at login (omawin start terminal): every terminal after it is a new window
    // of that warm process, so no start-up, and closing your last terminal doesn't shut it down.
    static int TerminalWarm()
    {
        Choice terminal;
        var exe = Terminal(out terminal);
        if (exe == null || terminal.Name != "noctty") return 0;
        // noctty can run with no window (its config keeps it open after the last one). Only
        // the shared instance answers +list-windows: a one-off window's own process (a
        // project, a Linux terminal) doesn't count as it running.
        string ignored;
        if (Capture(Path.Combine(Path.GetDirectoryName(exe), "noctty.com"), new List<string> { "+list-windows", "--timeout=1000" }, out ignored) == 0) return 0;
        return Detach(exe, new List<string> { "--initial-window=false" });
    }

    // Started without our console handles: a program that keeps running (a terminal, an editor)
    // would otherwise hold whatever ran omawin open, waiting for it.
    static Process StartQuietly(ProcessStartInfo info)
    {
        info.UseShellExecute = false;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        var process = Process.Start(info);
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        return process;
    }

    // --- Win+Enter: a terminal where you are --------------------------------------------------
    // A new terminal (the default one) in the folder of the window in front, else in ~. Native
    // rather than a script: PowerShell alone takes ~0.35 s to start.
    //   noctty           the focused pane's folder (the PowerShell profile and bash tell it
    //                    their folder, OSC 7)
    //   Windows Terminal the tab's folder: the PowerShell profile titles tabs with the folder
    //                    name and records name -> full path in %LOCALAPPDATA%\launcher\dirs; an
    //                    Linux tab's title is its path (bash-init in the Linux dotfiles)
    //   File Explorer    the folder it's showing

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int size);

    static int TerminalHere()
    {
        var hwnd = GetForegroundWindow();
        int pid;
        GetWindowThreadProcessId(hwnd, out pid);
        string process = "";
        try { process = Process.GetProcessById(pid).ProcessName; } catch (ArgumentException) { }

        if (process == "noctty")
        {
            // Its focused pane's folder, from noctty's own state (+list-windows: the pane that
            // is "active" is the one in front; its folder comes from the shell's OSC 7).
            Choice terminal;
            var exe = Terminal(out terminal);
            string json;
            Trace("found-noctty");
            // Our noctty fork takes "inherit" in a request as the folder of its window in
            // front: one request, no folder query first (~70 ms less).
            if (exe != null && terminal.Name == "noctty") return NocttyWindow(exe, new Launch { Dir = "inherit" });
            if (exe != null && Capture(Path.Combine(Path.GetDirectoryName(exe), "noctty.com"), new List<string> { "+list-windows" }, out json) == 0)
            {
                Trace("noctty-listed");
                var pane = System.Text.RegularExpressions.Regex.Match(json, "\"working_directory\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\",\\s*\"focused\":\\s*true,\\s*\"active\":\\s*true");
                if (pane.Success)
                {
                    var folder = pane.Groups[1].Value.Replace("\\\\", "\\");
                    if (folder.StartsWith("/")) return LaunchTerminal(new[] { "--wsl", "--dir", folder });
                    if (Directory.Exists(folder)) return LaunchTerminal(new[] { "--dir", folder });
                }
            }
        }

        if (process == "WindowsTerminal")
        {
            var text = new StringBuilder(512);
            GetWindowText(hwnd, text, text.Capacity);
            var title = text.ToString().Trim();
            // Linux: "~/apps/myapp" or "/etc", maybe after "user@host: " (a distro's own prompt).
            var linux = System.Text.RegularExpressions.Regex.Match(title, @"^(?:[\w.-]+@[\w.-]+:\s*)?((?:~|/)\S*)$");
            if (linux.Success) return LaunchTerminal(new[] { "--wsl", "--dir", linux.Groups[1].Value });
            // PowerShell: a full path (C:\ at a drive root) or the folder name the profile recorded.
            var path = title.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + title.Substring(1) : title;
            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\") && Directory.Exists(path)) return LaunchTerminal(new[] { "--dir", path });
            try
            {
                var record = Path.Combine(StateDir, "dirs", title);
                if (title.Length > 0 && File.Exists(record))
                {
                    path = File.ReadAllText(record).Trim();
                    if (Directory.Exists(path)) return LaunchTerminal(new[] { "--dir", path });
                }
            }
            catch (ArgumentException) { } // a title that isn't a file name
        }

        if (process == "explorer")
        {
            var folder = ExplorerFolder(hwnd);
            if (folder != null && Directory.Exists(folder)) return LaunchTerminal(new[] { "--dir", folder });
        }

        return LaunchTerminal(new[] { "--dir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
    }

    // The folder a File Explorer window shows (Shell.Application, late-bound).
    static string ExplorerFolder(IntPtr hwnd)
    {
        try
        {
            var flags = System.Reflection.BindingFlags.GetProperty;
            var call = System.Reflection.BindingFlags.InvokeMethod;
            var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            var windows = shell.GetType().InvokeMember("Windows", call, null, shell, null);
            int count = (int)windows.GetType().InvokeMember("Count", flags, null, windows, null);
            for (int i = 0; i < count; i++)
            {
                var w = windows.GetType().InvokeMember("Item", call, null, windows, new object[] { i });
                if (w == null || Convert.ToInt64(w.GetType().InvokeMember("HWND", flags, null, w, null)) != hwnd.ToInt64()) continue;
                var document = w.GetType().InvokeMember("Document", flags, null, w, null);
                var folder = document.GetType().InvokeMember("Folder", flags, null, document, null);
                var self = folder.GetType().InvokeMember("Self", flags, null, folder, null);
                return (string)self.GetType().InvokeMember("Path", flags, null, self, null);
            }
        }
        catch (Exception) { }
        return null;
    }

    //   <path>: Neovim in a terminal there, or the editor picked (omawin default editor).
    static int LaunchEditor(string[] args)
    {
        if (args.Length == 0) return Fail("which file or folder? omawin launch editor <path>");
        var path = args[0];
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        var editor = Current("editor");
        var exe = Find(editor.Program);
        if (editor.Name == "nvim" || exe == null)
            return LaunchTerminal(new[] { "--dir", dir, "--", "nvim", path });
        if (editor.Name == "code")
        {
            // code.cmd needs cmd.exe, which re-parses its command line (a path with & or spaces
            // breaks), so start Code.exe, the folder above code.cmd's bin\, with the path as one argument.
            var codeExe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(exe)), "Code.exe");
            if (File.Exists(codeExe)) return Detach(codeExe, new List<string> { path });
            // Otherwise code.cmd after all: /s keeps cmd's quotes as written, and inside them
            // spaces and & are plain characters.
            try
            {
                var info = new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + exe + "\" \"" + path + "\"\"") { UseShellExecute = false, CreateNoWindow = true };
                FreshEnvironment(info);
                StartQuietly(info);
                return 0;
            }
            catch (Exception ex) { return Fail(exe + ": " + ex.Message); }
        }
        return Detach(exe, new List<string> { path });
    }

    // Started and left running, without a console window of its own.
    static int Detach(string file, List<string> args)
    {
        try
        {
            var info = new ProcessStartInfo(file, string.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true };
            FreshEnvironment(info);
            StartQuietly(info);
            return 0;
        }
        catch (Exception ex) { return Fail(file + ": " + ex.Message); }
    }

    // The environment a new sign-in would have, from the registry, rather than whatever the
    // process that ran omawin inherited (GlazeWM keeps the one it started with, so a setting
    // changed since, like STARSHIP_CONFIG, would otherwise never reach new terminals).
    static void FreshEnvironment(ProcessStartInfo info)
    {
        foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables(target))
            {
                var name = (string)e.Key;
                // The machine's USERNAME is SYSTEM; Windows sets the real one per sign-in, so this
                // process's stays.
                if (name.Equals("USERNAME", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    info.EnvironmentVariables[name] = Environment.ExpandEnvironmentVariables((string)e.Value);
            }
        info.EnvironmentVariables["Path"] = Environment.ExpandEnvironmentVariables(
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) + ";" +
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
    }

    // An action's fixed arguments: words, or "quoted words".
    static string[] Split(string text)
    {
        var words = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"]*)\"|(\\S+)"))
            words.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return words.ToArray();
    }

    static void Say(string message)
    {
        if (InTerminal) Console.WriteLine(message);
    }

    static int Help(List<Command> commands, string group)
    {
        var shown = group == null ? commands : commands.Where(c => c.Name == group || c.Name.StartsWith(group + " ")).ToList();
        if (shown.Count == 0) return Fail("no group \"" + group + "\". omawin help lists them.");
        var text = new StringBuilder();
        if (group == null) text.AppendLine("omawin <command> [arguments]: this setup's commands (bin/commands.txt)\n");
        int width = shown.Max(c => c.Name.Length) + 2;
        int last = -1;
        foreach (var c in shown)
        {
            if (last >= 0 && c.Section != last) text.AppendLine();
            last = c.Section;
            text.AppendLine("  " + c.Name.PadRight(width) + c.Summary);
        }
        if (InTerminal) Console.Write(text);
        return 0;
    }

    static int Fail(string message)
    {
        if (InTerminal) Console.Error.WriteLine("omawin: " + message);
        return 1;
    }

    // One argument for a command line, the way programs split it again (CommandLineToArgvW):
    // backslashes are literal except before a quote, so those (and a trailing one) are doubled.
    static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\') { slashes++; continue; }
            quoted.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(ch);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}
