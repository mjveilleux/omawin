namespace Omawin.Windows;

// Key remaps Windows does itself, before any program sees a key (roadmap 8.12): the "Scancode Map"
// value Microsoft's keyboard scan-code mapper reads at start. Unlike the shell's live remap
// (input.caps-lock), it works at the sign-in screen and in windows running as admin, and GlazeWM
// sees the new key as a real one; it needs admin and a restart, and it's for every account. Pure:
// the bytes and their meaning; Commands/KeysCommand.cs writes it through one prompt.
public static class ScancodeMap
{
    public const string Key = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    public const string Value = "Scancode Map";

    public const ushort Esc = 0x0001, Caps = 0x003A, LCtrl = 0x001D, RCtrl = 0xE01D, LAlt = 0x0038, RAlt = 0xE038,
        LWin = 0xE05B, RWin = 0xE05C, Apps = 0xE05D, Off = 0x0000;

    // The few remaps offered by name: (from, to) pairs.
    public static readonly (string Name, string Says, (ushort From, ushort To)[] Maps)[] Presets =
    [
        ("caps-escape", "Caps Lock is Escape", [(Caps, Esc)]),
        ("caps-control", "Caps Lock is Control", [(Caps, LCtrl)]),
        ("swap-caps-control", "Caps Lock and left Control swapped", [(Caps, LCtrl), (LCtrl, Caps)]),
        ("caps-off", "Caps Lock does nothing", [(Caps, Off)]),
    ];

    // 8 zero bytes (version, flags), the count (mappings + 1, the terminator), each mapping as a
    // DWORD (low word the new scancode, high word the key's own), then 4 zero bytes.
    public static byte[] Build(IReadOnlyList<(ushort From, ushort To)> maps)
    {
        var b = new byte[16 + 4 * maps.Count];
        BitConverter.TryWriteBytes(b.AsSpan(8), (uint)(maps.Count + 1));
        for (int i = 0; i < maps.Count; i++)
        {
            BitConverter.TryWriteBytes(b.AsSpan(12 + 4 * i), maps[i].To);
            BitConverter.TryWriteBytes(b.AsSpan(14 + 4 * i), maps[i].From);
        }
        return b;
    }

    // The mappings in a value, or null when it isn't a well-formed map.
    public static List<(ushort From, ushort To)>? Read(byte[]? b)
    {
        if (b == null || b.Length < 16 || BitConverter.ToUInt64(b, 0) != 0) return null;
        uint count = BitConverter.ToUInt32(b, 8);
        if (count == 0 || b.Length != 12 + 4 * count || BitConverter.ToUInt32(b, b.Length - 4) != 0) return null;
        var list = new List<(ushort, ushort)>();
        for (int i = 0; i < count - 1; i++)
            list.Add((BitConverter.ToUInt16(b, 14 + 4 * i), BitConverter.ToUInt16(b, 12 + 4 * i)));
        return list;
    }

    public static string Describe(IReadOnlyList<(ushort From, ushort To)> maps)
    {
        var preset = Presets.FirstOrDefault(p => p.Maps.Length == maps.Count && p.Maps.All(maps.Contains));
        return preset.Name != null ? preset.Says : string.Join(", ", maps.Select(m => $"{Name(m.From)} is {Name(m.To)}"));
    }

    static string Name(ushort code) => code switch
    {
        Esc => "Escape", Caps => "Caps Lock", LCtrl => "left Control", RCtrl => "right Control", LAlt => "left Alt",
        RAlt => "right Alt", LWin => "left Windows", RWin => "right Windows", Apps => "Menu", Off => "nothing",
        _ => $"key 0x{code:X4}",
    };
}
