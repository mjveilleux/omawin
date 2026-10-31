// A setting's value as the catalog speaks it, and the handler interface that reads and writes it
// (plans/02-settings-and-one-search.md §4). Values are words everywhere above the handlers: a
// switch is "on" / "off", a choice its value key ("1"), a slider its number ("20"). This file
// is pure C# (no registry, no P/Invoke), so menu-app.Tests checks it anywhere.

namespace SystemMenu;

// One per verb of the catalog's "how" (reg, spi, powercfg, builtin, link, omawin). Read returns
// the value in words, or null when it can't tell (a key never written, a radio that isn't there).
// Write returns null on success, else a short reason for the note. Both are safe off the UI thread.
public interface ISettingHandler
{
    string? Read(SettingEntry e);
    string? Write(SettingEntry e, string value);
    // A choice whose values come from the PC (refresh rates, audio outputs). Default: the catalog's.
    IReadOnlyList<Choice> Choices(SettingEntry e) => e.Choices;
    // Cheap enough to read on the UI thread for rows on screen? (reg, spi, powercfg: microseconds;
    // radios, WMI and WinRT are not.)
    bool Fast(SettingEntry e) => true;
}

public static class SettingValues
{
    // A raw value (as the registry or an API gave it, null when absent) in the catalog's words:
    // on= / off= for a switch (absent is whichever of them is "-", "delete the value"), the raw
    // value itself for a choice or slider. A switch's value that's neither is unknown: null, "?".
    public static string? ToWords(SettingEntry e, string? raw)
    {
        raw ??= e.Default;
        if (e.Kind != SettingKind.Switch) return raw;
        if (raw == null) return e.How.On == "-" ? "on" : e.How.Off == "-" ? "off" : null;
        return raw == e.How.On ? "on" : raw == e.How.Off ? "off" : null;
    }

    // The other way: what to write. "-" means delete the value.
    public static string FromWords(SettingEntry e, string value) =>
        e.Kind == SettingKind.Switch ? (value == "on" ? e.How.On : e.How.Off) ?? value : value;

    // A registry value as text, the way the catalog writes it. A DWORD comes back from .NET as an
    // int, so 0xFFFFFFFF is -1: compared as unsigned (TapsEnabled's on is 4294967295).
    public static string? RawText(object? value) => value switch
    {
        null => null,
        int i => unchecked((uint)i).ToString(),
        long l => unchecked((ulong)l).ToString(),
        string[] lines => string.Join('\n', lines),
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString(),
    };

    // A catalog number as the bits a DWORD / QWORD holds ("4294967295" and "-1" are the same).
    public static int Dword(string raw) =>
        uint.TryParse(raw, out var u) ? unchecked((int)u) : int.TryParse(raw, out var i) ? i
        : raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? unchecked((int)Convert.ToUInt32(raw[2..], 16))
        : throw new FormatException($"'{raw}' isn't a DWORD");

    public static long Qword(string raw) =>
        ulong.TryParse(raw, out var u) ? unchecked((long)u) : long.TryParse(raw, out var i) ? i
        : throw new FormatException($"'{raw}' isn't a QWORD");

    // Typed values are forgiving: true / 1 / yes for on, a choice's label for its key.
    public static string Normalize(SettingEntry e, string value, IReadOnlyList<Choice> choices)
    {
        value = value.Trim();
        return e.Kind switch
        {
            SettingKind.Switch => value.ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "on" or "enable" or "enabled" => "on",
                "0" or "false" or "no" or "off" or "disable" or "disabled" => "off",
                var x => x,
            },
            SettingKind.Choice => choices.FirstOrDefault(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Value
                ?? choices.FirstOrDefault(c => c.Label.Equals(value, StringComparison.OrdinalIgnoreCase))?.Value ?? value,
            _ => value,
        };
    }

    // Null when the value fits the setting, else what would.
    public static string? Validate(SettingEntry e, string value, IReadOnlyList<Choice> choices) => e.Kind switch
    {
        SettingKind.Switch when value is not ("on" or "off") => $"{e.Id}: on or off",
        SettingKind.Choice when choices.Count == 0 => $"{e.Id}: no values to choose from on this PC",
        SettingKind.Choice when choices.All(c => c.Value != value) =>
            $"{e.Id}: one of {string.Join(", ", choices.Select(c => c.Value == c.Label ? c.Value : $"{c.Value} ({c.Label})"))}",
        SettingKind.Slider when e.Range is var (lo, hi) && (!int.TryParse(value, out var n) || n < lo || n > hi) =>
            $"{e.Id}: a number from {lo} to {hi}",
        _ => null,
    };

    // How a value reads in a row or a note: On / Off, a choice's label, a slider's number.
    public static string Label(SettingEntry e, string? value, IReadOnlyList<Choice> choices) => value is null ? "?" : e.Kind switch
    {
        SettingKind.Switch => value == "on" ? "On" : value == "off" ? "Off" : value,
        SettingKind.Choice => choices.FirstOrDefault(c => c.Value == value)?.Label ?? value,
        _ => value,
    };

    // The value one step from the current one: a switch flips (unknown turns on), a choice moves
    // to the next value and wraps, a slider moves ~5% of its range and stops at its ends. Null:
    // nothing to step (an action or a link, or a choice with no values).
    public static string? Next(SettingEntry e, string? current, int by, IReadOnlyList<Choice> choices)
    {
        switch (e.Kind)
        {
            case SettingKind.Switch:
                return current == "on" ? "off" : "on";
            case SettingKind.Choice:
            {
                if (choices.Count == 0) return null;
                int i = -1;
                for (int k = 0; k < choices.Count; k++) if (choices[k].Value == current) { i = k; break; }
                // Unknown: the first one going forward, the last going back.
                if (i < 0) return (by >= 0 ? choices[0] : choices[^1]).Value;
                return choices[((i + by) % choices.Count + choices.Count) % choices.Count].Value;
            }
            case SettingKind.Slider when e.Range is var (lo, hi):
            {
                int step = Math.Max(1, (hi - lo) / 20);
                int now = int.TryParse(current, out var n) ? n : lo;
                return Math.Clamp(now + by * step, lo, hi).ToString();
            }
            default:
                return null;
        }
    }

    // When the change shows, for the note after it.
    public static string WhenItApplies(SettingEntry e) =>
        e.Takes.HasFlag(Takes.Restart) ? " (after a restart)"
        : e.Takes.HasFlag(Takes.SignOut) ? " (after you sign out)"
        : e.How.Flags.Contains("restart-explorer") ? " (Restart Explorer to apply)" : "";
}
