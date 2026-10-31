using System.ComponentModel;
using System.Diagnostics;

namespace Omawin.Windows;

// Runs one of AdminScripts' scripts after one Windows prompt, only when you asked for the change
// (a command you typed): never on its own, never during an update. What will change is said first;
// declining leaves everything as it was.
public static class Elevated
{
    // Null when it was done; else why not ("you said no", the script's own error).
    public static string? Run(Func<string, string> script)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
        Directory.CreateDirectory(dir);
        var status = Path.Combine(dir, $"admin-result-{Environment.ProcessId}.txt");
        File.Delete(status);
        // Windows' own PowerShell, by its full path: an elevated program must not be one a user's
        // folder could swap (AGENTS.md).
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var info = new ProcessStartInfo(powershell,
            "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + AdminScripts.Encode(script(status)))
        {
            UseShellExecute = true, // runas needs the shell
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(info);
            if (p == null) return "Windows didn't start it";
            p.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "you said no at Windows' prompt; nothing changed"; } // ERROR_CANCELLED
        try
        {
            var said = File.Exists(status) ? File.ReadAllText(status).Trim() : "";
            File.Delete(status);
            return said == "ok" ? null : said.StartsWith("fail: ", StringComparison.Ordinal) ? said[6..] : "it didn't say how it went";
        }
        catch (IOException ex) { return ex.Message; }
    }
}
