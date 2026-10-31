using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Acting on a process another fix found (8.1's holders, 8.17's list): close it politely, or end it.
// Both check the process's start time first, so a pid reused since it was listed is never touched.
public static unsafe partial class Processes
{
    const uint WM_CLOSE = 0x0010, GW_OWNER = 4;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, nint, int> callback, nint state);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static partial int GetClassName(IntPtr hwnd, char* name, int max);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hwnd, uint msg, nuint w, nint l);

    // The windows the callback collects, for the one pid asked about. EnumWindows calls back on the
    // calling thread, so thread-static state is enough.
    [ThreadStatic] static List<IntPtr>? found;
    [ThreadStatic] static uint wanted;

    [UnmanagedCallersOnly]
    static int Collect(IntPtr hwnd, nint state)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == wanted && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && !IsShell(hwnd)) found!.Add(hwnd);
        return 1;
    }

    // The taskbar and the desktop: WM_CLOSE to the desktop asks to shut the PC down.
    static bool IsShell(IntPtr hwnd)
    {
        var name = stackalloc char[64];
        int n = GetClassName(hwnd, name, 64);
        var cls = new string(name, 0, Math.Max(n, 0));
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW";
    }

    // "Close": WM_CLOSE to its own top-level windows, as clicking X would (it may ask to save).
    // False when it has none (a service, a console program): then only End is offered.
    public static bool Close(uint pid, long started)
    {
        if (Win32.StartTime(pid) != started) return false; // gone, or the pid belongs to another now
        found = [];
        wanted = pid;
        EnumWindows(&Collect, 0);
        foreach (var w in found) PostMessage(w, WM_CLOSE, 0, 0);
        bool any = found.Count > 0;
        found = null;
        return any;
    }

    // "End", after the user said so. Null when it ended; else why not. An elevated process or
    // another account's can't be opened without admin.
    public static string? End(uint pid, long started)
    {
        var p = Win32.OpenProcess(Win32.PROCESS_TERMINATE | Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (p == IntPtr.Zero)
            return Marshal.GetLastPInvokeError() == Win32.ERROR_ACCESS_DENIED ? "needs admin (it runs as admin or as another account)" : "it already ended";
        try
        {
            if (!Win32.GetProcessTimes(p, out var created, out _, out _, out _) || created != started) return "it already ended";
            return Win32.TerminateProcess(p, 1) ? null : $"Windows refused (error {Marshal.GetLastPInvokeError()})";
        }
        finally { Win32.CloseHandle(p); }
    }
}
