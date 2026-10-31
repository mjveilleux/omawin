namespace Omawin.Windows;

// omawin who-has [path…] (8.1): who holds a file or folder open, then close or end them. With no
// path, the one on the clipboard (Explorer's "Copy as path", after a delete that failed).
//   --close   close each holder's windows without asking (they may still ask to save)
//   --end     end each holder without asking
static class WhoHasCommand
{
    public static int Run(string[] args)
    {
        bool close = args.Contains("--close"), end = args.Contains("--end");
        var paths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(p => Path.GetFullPath(p.Trim('"'))).ToList();
        if (paths.Count == 0)
        {
            var fromClipboard = Holders.PathFromText(Clipboard.GetText());
            if (fromClipboard == null) return Program.Fail("which file? omawin who-has <path> (or copy its path first: Shift+right-click › Copy as path)");
            paths.Add(fromClipboard);
            Console.WriteLine($"From the clipboard: {fromClipboard}");
        }
        foreach (var p in paths)
            if (!File.Exists(p) && !Directory.Exists(p)) return Program.Fail($"no such file or folder: {p}");

        var holders = RestartManager.Who(paths);
        bool folder = paths.Any(Directory.Exists);
        if (holders.Count == 0) return Program.Say(Holders.NoneFound(folder));

        Console.WriteLine(holders.Count == 1 ? "Held open by:" : $"Held open by {holders.Count} programs:");
        for (int i = 0; i < holders.Count; i++) Console.WriteLine($"  {i + 1}. {Holders.Describe(holders[i])}");

        if (!close && !end)
        {
            var answer = Program.Prompt("\nClose them (c), end them (e), or one by number (c2, e2)? Enter leaves them.");
            if (answer == null) return 0;
            var picked = holders;
            var digits = answer[1..].Trim();
            if (digits.Length > 0)
            {
                if (!int.TryParse(digits, out var n) || n < 1 || n > holders.Count) return Program.Fail($"no number {digits} in the list");
                picked = [holders[n - 1]];
            }
            if (answer.StartsWith('c')) return Close(picked);
            if (answer.StartsWith('e')) return Program.Ask($"End {(picked.Count == 1 ? picked[0].Name : $"{picked.Count} programs")}? Unsaved work in them is lost.") ? End(picked) : 0;
            return 0;
        }
        return close ? Close(holders) : End(holders);
    }

    static int Close(List<Holder> holders)
    {
        int failed = 0;
        foreach (var h in holders)
        {
            if (!Holders.CanClose(h)) { Console.WriteLine($"  {h.Name}: has no window to close; end it instead (e)"); failed++; continue; }
            if (Processes.Close(h.Pid, h.Started)) Console.WriteLine($"  {h.Name}: asked to close (it may ask to save first)");
            else { Console.WriteLine($"  {h.Name}: no window to close"); failed++; }
        }
        return failed == 0 ? 0 : 1;
    }

    static int End(List<Holder> holders)
    {
        int failed = 0;
        foreach (var h in holders)
        {
            if (!Holders.CanEnd(h)) { Console.WriteLine($"  {h.Name}: part of Windows, left running"); failed++; continue; }
            var why = Processes.End(h.Pid, h.Started);
            Console.WriteLine(why == null ? $"  {h.Name}: ended" : $"  {h.Name}: not ended, {why}");
            if (why != null) failed++;
        }
        return failed == 0 ? 0 : 1;
    }
}
