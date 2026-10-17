using Microsoft.Win32;

namespace OmawinMusic;

// Is dictation listening right now? The same test as the bar's voice-meter
// (zebar/tokyo-islands/voice-meter.cs): Handy (Win+H) or Windows voice typing is using the mic,
// per Windows' microphone privacy log (LastUsedTimeStop is 0 while in use).
static class Dictation
{
    const string MicLog = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    const string VoiceTyping = "MicrosoftWindows.Client.CBS_";
    const string Handy = "#handy.exe";

    public static bool Listening()
    {
        try
        {
            using var log = Registry.CurrentUser.OpenSubKey(MicLog);
            if (log == null) return false;
            foreach (var name in log.GetSubKeyNames())
                if (name.StartsWith(VoiceTyping, StringComparison.OrdinalIgnoreCase) && InUse(log, name)) return true;
            using var programs = log.OpenSubKey("NonPackaged");
            if (programs == null) return false;
            foreach (var name in programs.GetSubKeyNames())
                if (name.EndsWith(Handy, StringComparison.OrdinalIgnoreCase) && InUse(programs, name)) return true;
        }
        catch { }
        return false;
    }

    static bool InUse(RegistryKey parent, string name)
    {
        using var app = parent.OpenSubKey(name);
        object? start = app?.GetValue("LastUsedTimeStart"), stop = app?.GetValue("LastUsedTimeStop");
        return start != null && stop != null && Convert.ToInt64(stop) == 0;
    }
}
