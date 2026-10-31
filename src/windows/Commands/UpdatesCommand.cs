namespace Omawin.Windows;

// omawin updates (8.18): Windows Update without surprises. Nothing here hides an update or turns
// Windows Update off; it says what's coming and moves the restart to when you choose.
//   omawin updates                       what's waiting, whether a restart is owed, the pause, active hours
//   omawin updates restart [tonight|<time>|now|cancel]   the restart, at 03:00 unless said (no admin)
//   omawin updates pause [days]          pause for a week, or 1-35 days (admin: one prompt)
//   omawin updates resume                the undo
//   omawin updates hours <start>-<end>   active hours, when Windows never restarts itself; auto: Windows picks
//   omawin updates uninstall [KB]        take off the update that broke something (8.44; admin: one prompt)
static class UpdatesCommand
{
    public static int Run(string[] args)
    {
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        var now = DateTimeOffset.Now;
        switch (verb)
        {
            case "status":
            {
                // From a key or the bar (hidden), only the one line that matters.
                var state = UpdateAgent.Read(withAgent: Program.CanAsk || Console.IsOutputRedirected);
                if (!Program.CanAsk && !Console.IsOutputRedirected) return Program.Say(UpdatePlan.Report(state, now, TimeZoneInfo.Local).First());
                foreach (var line in UpdatePlan.Report(state, now, TimeZoneInfo.Local)) Console.WriteLine(line);
                return 0;
            }
            case "restart": return Restart(args.Length > 1 ? args[1].ToLowerInvariant() : "tonight");
            case "pause":
            {
                int days = 7;
                if (args.Length > 1 && (!int.TryParse(args[1], out days) || days is < 1 or > UpdatePlan.MaxPauseDays))
                    return Program.Fail($"a pause is 1 to {UpdatePlan.MaxPauseDays} days: omawin updates pause [days]");
                var values = UpdatePlan.PauseValues(now, days);
                Console.WriteLine($"Windows Update pauses until {now.AddDays(days):ddd d MMM HH:mm} (security updates too; Settings › Windows Update shows it, and its Resume works).");
                if (!Program.Ask("That's a setting for the whole PC: Windows asks once. Go ahead?")) return 0;
                var failed = Elevated.Run(status => AdminScripts.UpdatePause(values, status));
                return failed == null ? Program.Say($"Updates are paused for {days} day{(days == 1 ? "" : "s")} (undo: omawin updates resume).") : Program.Fail(failed);
            }
            case "resume":
            {
                Console.WriteLine("Windows Update resumes now (it checks again soon after).");
                if (!Program.Ask("That's a setting for the whole PC: Windows asks once. Go ahead?")) return 0;
                var failed = Elevated.Run(status => AdminScripts.UpdatePause([], status));
                return failed == null ? Program.Say("Updates are no longer paused.") : Program.Fail(failed);
            }
            case "hours":
            {
                if (args.Length < 2) return Program.Fail("omawin updates hours <start>-<end> (like 9-18), or auto");
                (int, int)? hours = null;
                if (args[1] != "auto")
                {
                    hours = UpdatePlan.ParseHours(string.Join("", args[1..]), out var why);
                    if (hours == null) return Program.Fail(why!);
                }
                var before = UpdateAgent.Read(withAgent: false);
                var was = before.ActiveHoursAutomatic ? "chosen by Windows" : before.ActiveStart is { } s && before.ActiveEnd is { } e ? $"{s:00}:00-{e:00}:00" : "not set";
                Console.WriteLine(hours is { } h
                    ? $"Active hours: {h.Item1:00}:00 to {h.Item2:00}:00, set by hand (now {was}). Windows doesn't restart itself in them."
                    : $"Active hours chosen by Windows from when you use the PC (now {was}).");
                if (!Program.Ask("That's a setting for the whole PC: Windows asks once. Go ahead?")) return 0;
                var failed = Elevated.Run(status => AdminScripts.UpdateActiveHours(hours, status));
                return failed == null ? Program.Say("Done.") : Program.Fail(failed);
            }
            case "uninstall": return Uninstall(args.Length > 1 ? args[1] : null, now);
            default:
                return Program.Fail("omawin updates [restart [tonight|<time>|now|cancel] | pause [days] | resume | hours <start>-<end>|auto | uninstall <KB>]");
        }
    }

    // Takes an update off (8.44): the KB given, or, with none, one picked from what was installed
    // lately (the ones `omawin health` suspects marked). Says what happens, then one prompt runs it
    // all: a restore point, the uninstall, a week's pause; the restart that finishes it is asked.
    static int Uninstall(string? given, DateTimeOffset now)
    {
        string? kb;
        if (given != null)
        {
            kb = UpdateSuspects.ParseKb(given);
            if (kb == null) return Program.Fail("omawin updates uninstall <KB number, like KB5031234>");
        }
        else
        {
            if (!Program.CanAsk) return Program.Fail("omawin updates uninstall <KB number>: run it in a terminal");
            IReadOnlyList<InstalledUpdate> recent;
            try { recent = UpdateAgent.History(50); }
            catch (InvalidOperationException ex) { return Program.Fail($"Windows Update's history couldn't be read ({ex.Message})"); }
            var withKb = recent.Where(u => UpdateSuspects.Kb(u.Title) != null && !UpdateSuspects.Ignored(u.Title)).Take(15).ToList();
            if (withKb.Count == 0) return Program.Say("Windows Update has installed nothing lately that can be taken off.");
            var suspected = Suspected(recent, now);
            Console.WriteLine("Installed lately (! = crashes or restarts started after it: omawin health says which):");
            foreach (var u in withKb)
            {
                var when = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(u.WhenUtc, DateTimeKind.Utc)), TimeZoneInfo.Local);
                var id = UpdateSuspects.Kb(u.Title)!;
                Console.WriteLine($"  {(suspected.Contains(id) ? "!" : " ")} KB{id,-8} {when:yyyy-MM-dd}  {u.Title}");
            }
            kb = Program.Prompt("Which KB? (Enter for none)") is { } typed ? UpdateSuspects.ParseKb(typed) : null;
            if (kb == null) return 0;
        }

        Console.WriteLine($"""
            KB{kb} comes off this PC:
              - a restore point first (when System Protection is on), so System Restore can go back to now;
              - Windows' own uninstaller for it, which shows its own window and asks too (DISM for a
                cumulative update that came with its servicing stack, which that uninstaller can't take off);
              - Windows Update paused for a week, so it isn't put straight back (omawin updates resume ends that).
            A restart finishes it.
            """);
        if (!Program.Ask("That changes the whole PC: Windows asks for an administrator once. Go ahead?")) return 0;
        var failed = Elevated.Run(status => AdminScripts.UpdateUninstall(kb, UpdatePlan.PauseValues(now, 7), status));
        if (failed != null)
        {
            Program.Fail(failed);
            // Windows makes a restore point before most updates: System Restore can go back to before it.
            if (Program.Ask("System Restore can take the PC back to before the update (your files stay). Open it?"))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rstrui.exe")) { UseShellExecute = true })?.Dispose(); }
                catch (System.ComponentModel.Win32Exception) { }
            return 1;
        }
        Console.WriteLine($"KB{kb} is uninstalled once the PC restarts; updates are paused for a week.");
        if (!Program.Ask("Restart now? Save your work first.")) return Program.Say("omawin updates restart tonight restarts it at 03:00.");
        var error = UpdateAgent.ScheduleRestart(DateTime.Now, 0);
        return error == null ? 0 : Program.Fail(error);
    }

    // The KBs `omawin health` would name for the last weeks' crashes: the same events, read the same way.
    static HashSet<string> Suspected(IReadOnlyList<InstalledUpdate> recent, DateTimeOffset now)
    {
        var (events, since) = HealthCommand.ReadEvents(UpdateSuspects.DaysNeeded, now, []);
        return UpdateSuspects.Find(recent, events, since, now)
            .SelectMany(s => s.Updates).Select(u => UpdateSuspects.Kb(u.Title)).OfType<string>().ToHashSet();
    }

    static int Restart(string when)
    {
        if (when == "cancel")
        {
            var failed = UpdateAgent.CancelRestart();
            return failed == null ? Program.Say("The restart is called off.") : Program.Fail(failed);
        }
        if (when == "now")
        {
            if (!Program.Ask("Restart now? Save your work first.")) return 0;
            var failed = UpdateAgent.ScheduleRestart(DateTime.Now, 0);
            return failed == null ? 0 : Program.Fail(failed);
        }
        var at = when == "tonight" ? UpdatePlan.Tonight : UpdatePlan.ParseTime(when);
        if (at == null) return Program.Fail("omawin updates restart [tonight|<time like 22:30>|now|cancel]");
        var (whenLocal, seconds) = UpdatePlan.Next(at.Value, DateTime.Now);
        var error = UpdateAgent.ScheduleRestart(whenLocal, seconds);
        return error == null
            ? Program.Say($"The PC restarts at {whenLocal:HH:mm}{(whenLocal.Date == DateTime.Today ? "" : " tomorrow")} (Windows warns first; omawin updates restart cancel calls it off).")
            : Program.Fail(error);
    }
}
