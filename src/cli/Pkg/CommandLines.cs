namespace Omawin.Cli.Pkg;

// Each manager's command lines and what its exit codes mean. Pure: the program's name and its
// arguments, so what omawin pkg runs is tested without running it.
public static class CommandLines
{
    // winget's own flags for running unattended: no prompts, no agreements to click.
    static readonly string[] Quiet = ["--silent", "--accept-source-agreements", "--disable-interactivity"];

    public static string[] Install(string manager, string id, string? scope) => manager switch
    {
        // Per user unless asked: winget would pick a machine-wide installer for some packages,
        // and that asks for admin (AGENTS.md: no admin by default).
        Managers.Winget => ["install", "--id", id, "--exact", "--scope", scope ?? "user", "--accept-package-agreements", .. Quiet],
        Managers.Scoop => ["install", id],
        Managers.Choco => ["install", id, "-y", "--no-progress"],
        // Installs it and pins it in mise's global config (~/.config/mise/config.toml), so it's on
        // the PATH in every shell.
        Managers.Mise => ["use", "--global", id + "@latest"],
        _ => throw new ArgumentException("not a manager to install with: " + manager),
    };

    public static string[] Remove(string manager, string id) => manager switch
    {
        Managers.Winget => ["uninstall", "--id", id, "--exact", .. Quiet],
        Managers.Scoop => ["uninstall", id],
        Managers.Choco => ["uninstall", id, "-y"],
        Managers.Mise => ["unuse", "--global", id],
        _ => throw new ArgumentException("not a manager to remove with: " + manager),
    };

    // One package, or (id null) everything that manager has.
    public static string[] Update(string manager, string? id) => manager switch
    {
        Managers.Winget => id == null
            ? ["upgrade", "--all", "--accept-package-agreements", .. Quiet]
            : ["upgrade", "--id", id, "--exact", "--accept-package-agreements", .. Quiet],
        Managers.Scoop => ["update", id ?? "*"],
        Managers.Choco => ["upgrade", id ?? "all", "-y", "--no-progress"],
        Managers.Mise => id == null ? ["upgrade"] : ["upgrade", id],
        "npm" => ["update", "--global", id ?? ""],
        _ => throw new ArgumentException("not a manager to update with: " + manager),
    };

    // What it has: winget writes its export to a file (its tables cut long ids short).
    public static string[] List(string manager, string exportFile) => manager switch
    {
        Managers.Winget => ["export", "--output", exportFile, "--include-versions", "--accept-source-agreements", "--disable-interactivity"],
        Managers.Scoop => ["export"],
        Managers.Choco => ["list", "--limit-output"],
        Managers.Mise => ["ls", "--json", "--installed"],
        _ => throw new ArgumentException("not a manager to list: " + manager),
    };

    // What has an update waiting.
    public static string[] Outdated(string manager) => manager switch
    {
        Managers.Winget => ["upgrade", "--accept-source-agreements", "--disable-interactivity"],
        Managers.Scoop => ["status"],
        Managers.Choco => ["outdated", "--limit-output"],
        Managers.Mise => ["outdated", "--json"],
        _ => throw new ArgumentException("not a manager to ask: " + manager),
    };

    public static string[] Search(string manager, string query) => manager switch
    {
        Managers.Winget => ["search", query, "--source", "winget", "--accept-source-agreements", "--disable-interactivity"],
        Managers.Scoop => ["search", query],
        Managers.Choco => ["search", query, "--limit-output"],
        Managers.Mise => ["registry"],
        _ => throw new ArgumentException("not a manager to search: " + manager),
    };

    public static List<Installed> ParseSearch(string manager, string output, string query) => manager switch
    {
        Managers.Winget => Parse.WingetTable(output),
        Managers.Scoop => Parse.ScoopTable(output),
        Managers.Choco => Parse.ChocoLimited(output),
        Managers.Mise => Parse.MiseRegistry(output, query),
        _ => [],
    };

    public static List<Installed> ParseOutdated(string manager, string output) => manager switch
    {
        Managers.Winget => Parse.WingetTable(output, available: true),
        Managers.Scoop => Parse.ScoopTable(output, status: true),
        Managers.Choco => Parse.ChocoLimited(output, outdated: true),
        Managers.Mise => Parse.MiseOutdated(output),
        _ => [],
    };

    // The Linux in WSL (--linux): one sh script that uses pacman in Arch (Omarchy's repositories),
    // apt-get elsewhere, with the package names as its arguments ("$@"), so nothing typed is ever
    // part of the script. sudo asks for the Linux password in this terminal when it needs one.
    public static string[] Linux(string verb, IReadOnlyList<string> packages)
    {
        var script = verb switch
        {
            "install" => "if command -v pacman >/dev/null; then sudo pacman -S --needed --noconfirm \"$@\"; else sudo apt-get install -y \"$@\"; fi",
            "remove" => "if command -v pacman >/dev/null; then sudo pacman -Rns --noconfirm \"$@\"; else sudo apt-get remove -y \"$@\"; fi",
            "update" => "if command -v pacman >/dev/null; then if [ $# -gt 0 ]; then sudo pacman -S --needed --noconfirm \"$@\"; else sudo pacman -Syu --noconfirm; fi; else sudo apt-get update && if [ $# -gt 0 ]; then sudo apt-get install --only-upgrade -y \"$@\"; else sudo apt-get upgrade -y; fi; fi",
            "list" => "if command -v pacman >/dev/null; then pacman -Qe; else dpkg-query -W -f='${binary:Package} ${Version}\\n' $(apt-mark showmanual); fi",
            _ => throw new ArgumentException("no Linux verb " + verb),
        };
        return ["--", "sh", "-c", script, "omawin-pkg", .. packages];
    }

    // winget's exit codes that matter (HRESULTs as Int32), from winget-cli's returnCodes.md.
    public static (bool Ok, string Text) Winget(int code) => unchecked((uint)code) switch
    {
        0 => (true, "done"),
        0x8A15002B => (true, "already up to date"),                              // UPDATE_NOT_APPLICABLE
        0x8A150061 => (true, "already installed"),                               // PACKAGE_ALREADY_INSTALLED
        0x8A15010D => (true, "already installed"),                               // INSTALL_ALREADY_INSTALLED
        0x8A150109 => (true, "done; a restart finishes it"),                     // INSTALL_REBOOT_REQUIRED_TO_FINISH
        0x8A15010A => (false, "Windows must restart before this can install"),  // INSTALL_REBOOT_REQUIRED_FOR_INSTALL
        0x8A150014 => (false, "no package with that id"),                        // NO_APPLICATIONS_FOUND
        0x8A150010 => (false, "no installer for this scope or architecture"),    // NO_APPLICABLE_INSTALLER
        0x8A15002C => (false, "some updates failed (above)"),                     // UPDATE_ALL_HAS_FAILURE
        0x8A150101 => (false, "the program is running: close it and try again"), // INSTALL_PACKAGE_IN_USE
        0x8A150102 => (false, "another install is running: try again when it's done"), // INSTALL_INSTALL_IN_PROGRESS
        0x8A150103 => (false, "a file is in use: close the program and try again"), // INSTALL_FILE_IN_USE
        0x8A150104 => (false, "a dependency is missing"),                        // INSTALL_MISSING_DEPENDENCY
        0x8A150105 => (false, "the disk is full"),                               // INSTALL_DISK_FULL
        0x8A150106 => (false, "not enough memory"),                              // INSTALL_INSUFFICIENT_MEMORY
        0x8A150107 => (false, "no network"),                                     // INSTALL_NO_NETWORK
        0x8A15010B => (true, "done; Windows is restarting to finish it"),       // INSTALL_REBOOT_INITIATED
        0x8A15010C => (false, "cancelled"),                                      // INSTALL_CANCELLED_BY_USER
        0x8A15010E => (false, "a newer version is already installed"),          // INSTALL_DOWNGRADE
        0x8A15010F => (false, "blocked by a policy on this PC"),                // INSTALL_BLOCKED_BY_POLICY
        0x8A150110 => (false, "its dependencies didn't install"),               // INSTALL_DEPENDENCIES
        _ => (false, $"winget exit 0x{unchecked((uint)code):X8}"),
    };

    // Whether an uninstall's failure means it was already gone (the record can drop it).
    public static bool Gone(string manager, int code) =>
        manager == Managers.Winget && unchecked((uint)code) == 0x8A150014;

    // winget's "no installer for this scope": per user was asked and it has only a whole-PC one.
    public static bool NeedsMachineScope(int code) => unchecked((uint)code) == 0x8A150010;

    // Whether running this needs admin: Chocolatey's installs are machine-wide by nature, and
    // winget's are when asked for (--machine).
    public static bool NeedsAdmin(string manager, string? scope) =>
        manager == Managers.Choco || (manager == Managers.Winget && scope == "machine");
}
