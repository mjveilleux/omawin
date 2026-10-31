namespace SystemMenu;

// Motion's numbers and rules, apart from WinUI (Motion.cs does the animating), so they're
// tested on any machine (test/menu-app.Tests). Omarchy's rules (its agents/skills/motion.md, its
// Hyprland look-and-feel): things arrive within 250 ms and leave in about two thirds of that,
// on ease-out-quint, never linear or ease-in; nothing waits on an animation.
public static partial class Motion
{
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(200);   // a window, a row, a level arriving
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(130);    // the menu leaving: ~2/3 of Enter
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);  // between rows (or widgets) arriving
    public static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(28);     // before the first row, so the window's own fade leads
    public static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(200);   // scrolling, a lifted tile
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);   // a colour change under the mouse or the keys
    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(1200);  // the bar's listening light, one breath (in and out)
    public static readonly TimeSpan Drift = TimeSpan.FromSeconds(7);          // the light along the menu's hairline, one pass
    public const float RisePx = 8f;   // how far an arriving row rises
    public const float SlidePx = 44f; // a submenu sliding in from the side
    public const float DropPx = 4f;   // the menu sinking as it leaves; a bar widget dropping into place
    public const int CascadeRows = 10, CascadeTiles = 18; // past these, rows and tiles are there at once

    // ease-out-quint, cubic-bezier(0.23, 1, 0.32, 1): fast away, a long gentle landing.
    public const float EaseX1 = 0.23f, EaseY1 = 1f, EaseX2 = 0.32f, EaseY2 = 1f;

    // %LOCALAPPDATA%\launcher\animations says "on" or "off" (`omawin toggle animations`); a file
    // with anything else, or none at all, doesn't decide.
    public static bool? ParseSetting(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };

    // `omawin toggle animations [on|off|toggle]`: what it's to be, from what it is; null for a word
    // it doesn't know.
    public static bool? Asked(bool now, string? word) => (word ?? "toggle").Trim().ToLowerInvariant() switch
    {
        "" or "toggle" => !now,
        "on" => true,
        "off" => false,
        _ => null,
    };

    // A virtual machine, by what Windows says the computer is (Win32_ComputerSystem's
    // Manufacturer and Model): animations start off there, as Omarchy's vm-no-animations does,
    // because a VM's display usually has no GPU behind it and every frame is drawn in software.
    public static bool IsVirtualMachine(string? manufacturer, string? model)
    {
        var said = $"{manufacturer} {model}".ToLowerInvariant();
        string[] signs = ["virtual machine", "virtual platform", "vmware", "virtualbox", "innotek", "qemu", "kvm", "xen", "hvm domu",
            "parallels", "bochs", "amazon ec2", "google compute engine", "openstack"];
        return signs.Any(said.Contains);
    }

    // How long row (or tile) `step` of the first screenful waits before it arrives: Lead, then
    // Stagger apart; null for one above the view or past the cascade, which is simply there.
    public static TimeSpan? CascadeDelay(int step, int lastStep) =>
        step < 0 || step > lastStep ? null : Lead + Stagger * step;
}
