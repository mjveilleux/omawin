namespace Omawin.Cli.Pkg;

// What omawin pkg needs from the PC: finding and running the managers, asking you, and where
// its files are. PkgCommand decides; the host does. Tests give it a host that records what it
// was asked to run (test/cli.Tests/PkgCommandTests.cs); WindowsHost is the real one.
public abstract class Host
{
    // The repo (default/packages/names.txt, install/tools.txt, bin/defaults.txt) and
    // %LOCALAPPDATA%\launcher (packages.tsv, pkg-prefer.txt, pkg-pins.tsv, installed.txt).
    public abstract string Repo { get; }
    public abstract string State { get; }
    public abstract TextWriter Out { get; }

    // A manager's program (winget, scoop, choco, mise, wsl), or null when it isn't on this PC.
    public abstract bool Has(string program);

    // Runs it with its output collected: lists, searches.
    public abstract (int Code, string Output) Capture(string program, IReadOnlyList<string> args);

    // Runs it in this terminal, its output shown as it goes: installs, removals, updates.
    public abstract int Live(string program, IReadOnlyList<string> args);

    // Runs one program as administrator after Windows' one prompt, and waits. Null when the
    // prompt was declined or can't be shown. program is "choco" (Chocolatey's own, machine-wide
    // copy) or "admin-batch" (install/admin-batch.ps1 under Windows' own PowerShell).
    public abstract int? Elevated(string program, IReadOnlyList<string> args);

    // Every copy of a command on the PATH, the one that runs first.
    public abstract IReadOnlyList<string> OnPath(string command);

    // A question in the terminal; null when there's no one to ask (no console, input redirected).
    public abstract string? Ask(string prompt);

    // OMAWIN_NO_ADMIN: nothing may ask for admin (install.ps1 sets it after its one prompt).
    public abstract bool NoAdmin { get; }

    public virtual string TempFile(string name) => Path.Combine(Path.GetTempPath(), $"omawin-pkg-{Environment.ProcessId}-{name}");
}
