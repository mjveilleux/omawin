using System.Diagnostics;
using System.Text;

namespace Omawin.Cli.Profile;

// The real PC for `omawin profile linux`: wsl.exe in System32, the VM's process, this terminal.
public sealed class WslMachine : LinuxMachine
{
    static readonly string WslExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    public override (int Code, string Output) Wsl(IReadOnlyList<string> args, int timeoutMs = 120_000)
    {
        var info = new ProcessStartInfo(File.Exists(WslExe) ? WslExe : "wsl.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["WSL_UTF8"] = "1"; // wsl.exe's own messages in UTF-8, not UTF-16
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info)!;
            p.StandardInput.Close(); // bash -i reads nothing; it exits at once
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch (InvalidOperationException) { } return (-1, ""); }
            return (p.ExitCode, output.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, ""); }
    }

    // vmmemWSL on current WSL; vmmem before it (also any other Hyper-V VM's, so only as a fallback).
    // A process the user may not open: then null, and the report says so.
    public override long? VmWorkingSet()
    {
        foreach (var name in new[] { "vmmemWSL", "vmmem" })
        {
            var found = Process.GetProcessesByName(name);
            try
            {
                if (found.Length == 0) continue;
                return found.Sum(p => p.WorkingSet64);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
            finally { foreach (var p in found) p.Dispose(); }
        }
        return null;
    }

    public override void Sleep(int ms) => Thread.Sleep(ms);

    public override string? Ask(string prompt)
    {
        if (Console.IsInputRedirected) return null;
        Console.Write(prompt);
        return Console.ReadLine();
    }
}
