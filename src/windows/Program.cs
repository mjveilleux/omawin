using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

namespace Omawin.Windows;

// omawin-windows: Phase 8's fixes that answer in a terminal. You type the omawin names
// (bin/commands.txt routes them here); the menu's rows run them in a small terminal you can read.
//   who-has [path…]        which programs hold a file or folder open; close or end them (8.1)
//   health [days]          why the PC restarted, crashed or woke up (8.15)
//   startup [off|on <name>]  everything that starts with Windows, and switches (8.16)
//   top [name] | top end <pid>  processes by app, by CPU and memory (8.17)
//   hosts [add|off|on …]   the hosts file (8.19)
//   connections [name]     which apps are talking to the internet (8.20)
//   wifi-qr [network]      the Wi-Fi as a QR code a phone joins from (8.21)
//   clip [transform]       the clipboard's text converted in place (8.14)
//   classic-menu [on|off|status]  the full right-click menu back (8.24)
//   --version
public static partial class Program
{
    public static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "(dev)";

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var rest = args.Skip(1).ToArray();
        try
        {
            return verb switch
            {
                "--version" or "-v" => Say("omawin-windows " + Version),
                "who-has" => WhoHasCommand.Run(rest),
                "health" => HealthCommand.Run(rest),
                "startup" => StartupCommand.Run(rest),
                "top" => TopCommand.Run(rest),
                "hosts" => HostsCommand.Run(rest),
                "connections" => ConnectionsCommand.Run(rest),
                "wifi-qr" => WifiCommand.Run(rest),
                "clip" => ClipCommand.Run(rest),
                "classic-menu" => ClassicMenuCommand.Run(rest),
                _ => Help(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return Fail(ex.Message);
        }
    }

    static int Help()
    {
        Console.WriteLine("""
            omawin-windows: run through omawin (omawin help lists everything):
              omawin who-has [path]       who holds a file or folder open (no path: the one on the clipboard)
              omawin health [days]        why the PC restarted, crashed or woke up
              omawin startup              what starts with Windows; startup off <name>, startup on <name>
              omawin top [name]           processes by app; top end <pid>
              omawin hosts                the hosts file; hosts add <address> <name>, hosts off <name>, hosts on <name>
              omawin connections [name]   which apps are talking to the internet
              omawin wifi share [network] the Wi-Fi as a QR code
              omawin clip [transform]     the clipboard's text converted (clip lists them)
              omawin classic-menu         the full right-click menu, or Windows 11's short one (on, off, status)
            """);
        return 0;
    }

    // From a key or a menu row run hidden (omawin.exe starts it with no window), nothing can be
    // printed or answered: what it says goes to the note at the bottom of the screen instead.
    static readonly bool InTerminal = GetConsoleWindow() != 0 || Console.IsOutputRedirected;

    [LibraryImport("kernel32.dll")] private static partial nint GetConsoleWindow();

    public static int Say(string text)
    {
        if (InTerminal) Console.WriteLine(text);
        else Note(text);
        return 0;
    }

    public static int Fail(string message)
    {
        if (InTerminal) Console.Error.WriteLine("omawin: " + message);
        else Note(message);
        return 1;
    }

    // The menu app's note (its --say request, handed over as omawin.exe hands requests: the request
    // file, then its event). Nothing when the menu app isn't running.
    static void Note(string text)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out var signal)) return;
            using (signal)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "system-menu-request"), "--say " + text.ReplaceLineEndings(" "));
                signal.Set();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Whether a question can be answered here: a terminal (a menu row's or yours) with its input.
    public static bool CanAsk => InTerminal && !Console.IsInputRedirected;

    // Asked only where someone can answer. With no terminal, or input redirected (a script), the
    // answer is no.
    public static bool Ask(string question)
    {
        if (!CanAsk) return false;
        Console.Write(question + " [y/N] ");
        var answer = Console.ReadLine();
        return answer != null && answer.Trim().StartsWith('y');
    }

    // A line for a choice among several, or null when none is typed (Enter, or no terminal).
    public static string? Prompt(string question)
    {
        if (!InTerminal || Console.IsInputRedirected) return null;
        Console.Write(question + " ");
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? null : answer;
    }
}
