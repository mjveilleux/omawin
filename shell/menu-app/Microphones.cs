namespace SystemMenu;

// `omawin mic [toggle|mute|unmute|status]` (roadmap 8.11): every active microphone muted or
// unmuted at once through Core Audio's endpoint volume (IAudioEndpointVolume::SetMute, documented),
// the same mute Sound settings shows. The bar's indicators show a red crossed-out microphone while
// they're all muted (Bar/Widgets.cs). No key yet: one is bound only once you choose it.
public static partial class Audio
{
    // The microphones' mute, as the bar last read it (null: not read yet, or none plugged in).
    public static bool? MicsMuted { get; private set; }

    // Each active microphone's mute now, in Windows' order.
    static List<(IAudioEndpointVolume Volume, bool Muted)> Mics()
    {
        var mics = new List<(IAudioEndpointVolume, bool)>();
        var devices = Enumerator();
        devices.EnumAudioEndpoints(DataFlow.Capture, ActiveOnly, out var all);
        all.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            try
            {
                all.Item(i, out var device);
                var volume = Volume(device);
                volume.GetMute(out bool muted);
                mics.Add((volume, muted));
            }
            catch (Exception ex) { App.Log(ex); } // one that went away mid-way: the rest still count
        }
        return mics;
    }

    // For the bar: all muted (true), any live (false), none plugged in (null). Cheap: no meters.
    public static bool? ReadMicsMuted()
    {
        try
        {
            var mics = Mics();
            MicsMuted = mics.Count == 0 ? null : mics.All(m => m.Muted);
        }
        catch { MicsMuted = null; }
        return MicsMuted;
    }

    // Does the request and says how it ended, for the note at the bottom of the screen.
    public static string SetMics(string action)
    {
        try
        {
            var mics = Mics();
            if (action == "status") return MicMute.Say(mics.Select(m => m.Muted).ToList());
            var target = MicMute.Target(mics.Select(m => m.Muted).ToList(), action);
            if (target == null) return mics.Count == 0 ? "No microphone" : $"omawin mic: toggle, mute, unmute or status, not \"{action}\"";
            var none = Guid.Empty;
            var after = new List<bool>();
            foreach (var (volume, muted) in mics)
            {
                try { volume.SetMute(target.Value, ref none); after.Add(target.Value); }
                catch (Exception ex) { App.Log(ex); after.Add(muted); }
            }
            MicsMuted = after.All(m => m);
            return MicMute.Say(after);
        }
        catch (Exception ex) { App.Log(ex); return "The microphones couldn't be reached"; }
    }
}
