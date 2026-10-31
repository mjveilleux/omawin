namespace Omawin.Windows;

// omawin updates (8.18): Windows Update without surprises. Nothing here hides an update or turns
// Windows Update off; it says what's coming and moves the restart to when you choose.
//   omawin updates                       what's waiting, whether a restart is owed, the pause, active hours
//   omawin updates restart [tonight|<time>|now|cancel]   the restart, at 03:00 unless said (no admin)
//   omawin updates pause [days]          pause for a week, or 1-35 days (admin: one prompt)
//   omawin updates resume                the undo
//   omawin updates hours <start>-<end>   active hours, when Windows never restarts itself; auto: Windows picks
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
            default:
                return Program.Fail("omawin updates [restart [tonight|<time>|now|cancel] | pause [days] | resume | hours <start>-<end>|auto]");
        }
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
