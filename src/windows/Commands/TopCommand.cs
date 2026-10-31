namespace Omawin.Windows;

// omawin top [name] (8.17): processes by app, the busiest first, faster than opening Task Manager.
//   omawin top            every app: CPU over one second, memory (private, as Task Manager counts it)
//   omawin top <name>     only the apps matching, each process listed with its pid and ports
//   omawin top end <pid>  end one, after asking
static class TopCommand
{
    public static int Run(string[] args)
    {
        if (args.Length >= 1 && args[0] == "end") return End(args.Length > 1 ? args[1] : null);
        var typed = args.Length > 0 ? string.Join(' ', args) : null;

        var first = Snapshot.Take();
        long started = DateTime.UtcNow.Ticks;
        Thread.Sleep(1000);
        var second = Snapshot.Take();
        var groups = ProcessGroups.Filter(ProcessGroups.Group(second, first, DateTime.UtcNow.Ticks - started, Environment.ProcessorCount), typed);
        if (groups.Count == 0) return Program.Say($"Nothing running matches \"{typed}\".");

        var listening = typed != null ? Ports() : [];
        Console.WriteLine($"  {"CPU",5}  {"Memory",8}  App");
        foreach (var g in typed == null ? groups.Take(30) : groups)
        {
            var count = g.Processes > 1 ? $" ({g.Processes})" : "";
            Console.WriteLine($"  {g.Cpu,4:0.0}%  {ProcessGroups.Size(g.PrivateBytes),8}  {g.Name}{count}");
            if (typed == null) continue;
            // Asked about by name: each process, with what it listens on.
            foreach (var p in g.Members.OrderByDescending(p => p.PrivateBytes))
            {
                var ports = listening.TryGetValue(p.Pid, out var ps) ? "  listens on " + string.Join(", ", ps) : "";
                Console.WriteLine($"           pid {p.Pid,-7} {ProcessGroups.Size(p.PrivateBytes),8}  {p.Name}{ports}");
            }
        }
        if (typed == null && groups.Count > 30) Console.WriteLine($"  … and {groups.Count - 30} more (omawin top <name> narrows it)");
        else if (typed != null) Console.WriteLine("\nomawin top end <pid> ends one.");
        return 0;
    }

    // Listening ports by pid (Localhost's view): "3000", "5173/udp".
    static Dictionary<uint, List<string>> Ports()
    {
        var byPid = new Dictionary<uint, List<string>>();
        try
        {
            foreach (var s in Sockets.All().Where(s => s.Listening && s.LocalPort > 0).DistinctBy(s => (s.Pid, s.LocalPort, s.Protocol)))
            {
                if (!byPid.TryGetValue(s.Pid, out var list)) byPid[s.Pid] = list = [];
                list.Add(s.Protocol == "UDP" ? $"{s.LocalPort}/udp" : s.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (System.ComponentModel.Win32Exception) { }
        return byPid;
    }

    static int End(string? pidText)
    {
        if (!uint.TryParse(pidText, out var pid)) return Program.Fail("which? omawin top end <pid> (omawin top <name> shows pids)");
        var p = Snapshot.Take().FirstOrDefault(x => x.Pid == pid);
        if (p.Name == null) return Program.Fail($"no process {pid}");
        if (ProcessGroups.AppOf(p, new Dictionary<uint, Proc>()) == ProcessGroups.WindowsGroup)
            return Program.Fail($"{p.Name} is part of Windows; ending it can crash the PC, so omawin won't");
        if (!Program.Ask($"End {p.Name} (pid {pid})? Unsaved work in it is lost.")) return 0;
        // The start time from the snapshot: had the process ended and its pid been reused since,
        // the new one's start time differs and nothing is ended.
        var why = Processes.End(pid, p.Created);
        return why == null ? Program.Say($"{p.Name} ended.") : Program.Fail($"{p.Name} not ended: {why}");
    }
}
