using System.Runtime.InteropServices;

namespace Omawin.Windows;

// omawin pip [title] (8.6): the window in front (from a key or the menu), or the one whose title
// holds the words, shown live in a small always-on-top window. It runs until that's closed.
static unsafe partial class PipCommand
{
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EnumWindows(nint callback, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int GetWindowTextW(nint hwnd, char* text, int max);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial int GetClassNameW(nint hwnd, char* name, int max);
    [LibraryImport("kernel32.dll")] private static partial nint GetConsoleWindow();

    static readonly List<(nint Hwnd, string Title)> found = [];

    public static int Run(string[] args)
    {
        nint target;
        if (args.Length == 0)
        {
            target = GetForegroundWindow();
            // Typed in a terminal, the window in front is that terminal: say how to pick another.
            if (target == 0 || target == GetConsoleWindow() || Title(target).Length == 0 && !Program.CanAsk)
                return Program.Fail("which window? omawin pip <part of its title> (or from a key, the window in front)");
        }
        else
        {
            var words = string.Join(' ', args);
            found.Clear();
            EnumWindows((nint)(delegate* unmanaged<nint, nint, int>)&Each, 0);
            var matches = found.Where(w => w.Title.Contains(words, StringComparison.OrdinalIgnoreCase) && w.Hwnd != GetConsoleWindow()).ToList();
            if (matches.Count == 0) return Program.Fail($"no window's title holds \"{words}\"");
            if (matches.Count > 1 && matches.Select(m => m.Title).Distinct().Count() > 1)
            {
                Console.Error.WriteLine("omawin: more than one window matches; say which:");
                foreach (var m in matches) Console.Error.WriteLine("  " + m.Title);
                return 1;
            }
            target = matches[0].Hwnd;
        }
        var failed = Pip.Run(target);
        return failed == null ? 0 : Program.Fail(failed);
    }

    // Top-level windows you can see, with a title, that aren't omawin's own picture.
    [UnmanagedCallersOnly]
    static int Each(nint hwnd, nint param)
    {
        if (IsWindowVisible(hwnd) && GetAncestor(hwnd, 3 /* GA_ROOTOWNER */) == hwnd)
        {
            var title = Title(hwnd);
            var cls = stackalloc char[64];
            int n = GetClassNameW(hwnd, cls, 64);
            if (title.Length > 0 && new string(cls, 0, n) != Pip.ClassName) found.Add((hwnd, title));
        }
        return 1;
    }

    static string Title(nint hwnd)
    {
        var buffer = stackalloc char[512];
        int n = GetWindowTextW(hwnd, buffer, 512);
        return n > 0 ? new string(buffer, 0, n) : "";
    }
}
