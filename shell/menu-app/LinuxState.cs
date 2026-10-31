namespace SystemMenu;

// Your Linux in WSL as the bar shows it (roadmap 3.10): running or stopped, and the VM's memory.
// Asking WSL costs a wsl.exe each time, so the bar asks only while the VM is up (its process,
// vmmemWSL, is there): with no VM, nothing in Linux runs and nothing is asked. While it's up, WSL
// is asked when the VM comes or goes and then every AskEvery, since a distro can stop while the
// VM stays (vmIdleTimeout). Pure (no WinUI), so it's tested anywhere; Bar.cs does the asking.
public static class LinuxState
{
    public sealed record Status(string Distro, string Name, bool Running, long? VmBytes);

    public static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(30);

    // Whether to ask wsl.exe now: the VM is up, and it just came up, or it's been AskEvery.
    public static bool ShouldAsk(bool vmUp, bool vmWasUp, DateTime lastAsked, DateTime now) =>
        vmUp && (!vmWasUp || now - lastAsked >= AskEvery);

    // `wsl.exe --list --running --quiet`'s output (UTF-8 with WSL_UTF8, else UTF-16 read as either:
    // NULs, CRs and blanks are dropped): whether the distro is in it.
    public static bool Running(string output, string distro) =>
        output.Replace("\0", "").Split('\n').Any(l => l.Trim('\r', ' ', '\t').Equals(distro, StringComparison.OrdinalIgnoreCase));

    // The bar's tooltip: the name, its state, the VM's memory when Windows lets it be read, and
    // what the clicks do.
    public static string Tip(Status s) => s.Running
        ? $"{Title(s.Name)}: running{(s.VmBytes is { } b ? " · " + Memory(b) : "")} · click: a terminal · right click: stop it"
        : $"{Title(s.Name)}: stopped · click: start it, in a terminal";

    public static string Memory(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes >> 20} MB";

    static string Title(string name) => name.Length == 0 ? "Linux" : char.ToUpperInvariant(name[0]) + name[1..];
}
