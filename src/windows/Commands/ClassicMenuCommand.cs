namespace Omawin.Windows;

// omawin classic-menu [on|off|status] (8.24): Windows 10's full right-click menu in Explorer, or
// Windows 11's short one again. No argument: the other one. Explorer restarts to show it (asked
// first in a terminal; from the menu, it's done, since picking the row asked for it).
static class ClassicMenuCommand
{
    public static int Run(string[] args)
    {
        var what = args.Length > 0 ? args[0] : "toggle";
        bool now = ClassicMenu.IsOn();
        if (what == "status") return Program.Say(now ? "The full right-click menu is on." : "Windows 11's short right-click menu is on.");
        if (what is not ("on" or "off" or "toggle")) return Program.Fail("omawin classic-menu [on|off|status]");
        bool on = what == "toggle" ? !now : what == "on";
        if (on == now) return Program.Say(on ? "The full right-click menu is already on." : "The short right-click menu is already on.");
        ClassicMenu.Set(on);
        var done = on ? "The full right-click menu is on" : "Windows 11's short right-click menu is back";
        if (!Program.CanAsk || Program.Ask($"{done}. Explorer restarts to show it (the taskbar blinks, open folders close). Now?"))
        {
            ClassicMenu.RestartExplorer();
            return Program.Say(done + ".");
        }
        return Program.Say($"{done} from Explorer's next start (sign out, or System menu › Update › Restart › Explorer).");
    }
}
