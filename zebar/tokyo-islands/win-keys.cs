// win-keys: presses a Windows-key shortcut, for the Zebar dock's Start, Search, quick
// settings and show-desktop buttons (Windows has no command for those; the keys are the only way).
//
//   win-keys win        Start
//   win-keys win+s      Search
//   win-keys win+a      quick settings
//   win-keys win+d      show the desktop
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows.
using System;
using System.Runtime.InteropServices;

static class WinKeys
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    const ushort LWin = 0x5B;
    const uint KeyUp = 2;

    static INPUT Key(ushort vk, bool up)
    {
        return new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KeyUp : 0 } };
    }

    static int Main(string[] args)
    {
        string combo = args.Length > 0 ? args[0].ToLowerInvariant() : "win";
        if (!combo.StartsWith("win")) return 1;
        string rest = combo.Length > 4 ? combo.Substring(4) : "";
        if (rest.Length > 1 || (rest.Length == 1 && !char.IsLetterOrDigit(rest[0]))) return 1;
        var inputs = rest.Length == 0
            ? new[] { Key(LWin, false), Key(LWin, true) }
            : new[] { Key(LWin, false), Key((ushort)char.ToUpperInvariant(rest[0]), false), Key((ushort)char.ToUpperInvariant(rest[0]), true), Key(LWin, true) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        return 0;
    }
}
