namespace SystemMenu;

// A real microphone mute (roadmap 8.11): every microphone at once, not just the default one, since
// an app may listen on any. What a request means, worked out here (pure: menu-app.Tests runs it);
// Microphones.cs does it through Core Audio.
public static class MicMute
{
    // The mute every microphone should get for "mute", "unmute" or "toggle", given each one's mute
    // now; null when there's no microphone or the action isn't one of those. Toggle mutes them all
    // unless they're all muted already: one live microphone among muted ones is still listening.
    public static bool? Target(IReadOnlyList<bool> muted, string action) => muted.Count == 0 ? null : action switch
    {
        // Not "on" / "off": "mic on" reads as the microphone on, "mute on" as muted.
        "mute" => true,
        "unmute" => false,
        "toggle" or "" => !muted.All(m => m),
        _ => null,
    };

    // The note at the bottom of the screen, and `omawin mic status`'s answer.
    public static string Say(IReadOnlyList<bool> muted) => muted.Count switch
    {
        0 => "No microphone",
        _ when muted.All(m => m) => muted.Count == 1 ? "Microphone muted" : $"All {muted.Count} microphones muted",
        _ when muted.Any(m => m) => $"{muted.Count(m => !m)} of {muted.Count} microphones live",
        _ => muted.Count == 1 ? "Microphone on" : $"{muted.Count} microphones on",
    };
}
