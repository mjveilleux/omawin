// What the shell's one keyboard hook (KeyHook.cs) does with a key, decided without Win32 so it can
// be tested: Caps Lock remapped (roadmap 8.12), text snippets (8.13), the Copilot key (8.23) and
// Space for Quick Look in Explorer (8.4).
// Each answers "swallow this key?" and lists the keys to send instead; KeyHook sends them, tagged
// as omawin's so the hook lets them through.

namespace SystemMenu;

public readonly record struct SentKey(ushort Vk, bool Down);

// Caps Lock as Escape, as Control, or Escape when tapped and Control when held with another key
// (input.caps-lock in the settings catalog). Live, with no restart and no admin; for the sign-in
// screen and admin windows too, `omawin keyboard signin` writes Windows' own Scancode Map instead.
public sealed class CapsLockRemap
{
    public const ushort VkCaps = 0x14, VkEscape = 0x1B, VkLControl = 0xA2;
    // Longer than this, a lone Caps press is a held Control that wasn't used, not an Escape.
    public const uint TapMs = 300;
    public static readonly string[] Modes = ["caps", "escape", "control", "escape-control"];

    string mode = "caps";
    bool held, used;
    uint downAt;

    public string Mode
    {
        get => mode;
        set { mode = Modes.Contains(value) ? value : "caps"; held = used = false; }
    }

    public bool Active => mode != "caps";

    public bool OnKey(uint vk, bool down, uint time, List<SentKey> send)
    {
        if (mode == "caps") return false;
        if (vk != VkCaps)
        {
            // Another key while Caps is held: in escape-control, Caps was Control after all. Control
            // goes down first, then this key again, so the window sees Ctrl+key in that order (a key
            // sent from here arrives after the one being handled).
            if (held && down && mode == "escape-control" && !used)
            {
                used = true;
                send.Add(new(VkLControl, true));
                send.Add(new((ushort)vk, true));
                return true;
            }
            return false;
        }
        if (down && held) return true; // auto-repeat: nothing more to send
        held = down;
        switch (mode)
        {
            case "escape": send.Add(new(VkEscape, down)); break;
            case "control": send.Add(new(VkLControl, down)); break;
            case "escape-control":
                if (down) { downAt = time; used = false; }
                else if (used) send.Add(new(VkLControl, false));
                else if (unchecked(time - downAt) < TapMs) { send.Add(new(VkEscape, true)); send.Add(new(VkEscape, false)); }
                break;
        }
        return true;
    }
}

// The Copilot key sends Win+Shift+F23 (Windows 11 keyboards since 2024). Taken, it opens the
// launcher instead of Copilot (input.copilot-key); its down and up are both swallowed, and
// Start is kept from opening when Win comes up alone.
public sealed class CopilotKey
{
    public const ushort VkF23 = 0x86;
    // An unassigned key between Win's down and up stops Start opening (as AutoHotkey does).
    public const ushort VkMask = 0xE8;

    public bool On { get; set; }

    public bool OnKey(uint vk, bool down, bool win, bool shift, List<SentKey> send, out bool fire)
    {
        fire = false;
        if (!On || vk != VkF23) return false;
        if (down)
        {
            if (!win || !shift) return false; // a real F23 from a macro keyboard stays F23
            fire = true;
            send.Add(new(VkMask, true));
            send.Add(new(VkMask, false));
        }
        return true;
    }
}

public sealed record Snippet(string Trigger, string Text);

// Text snippets: "<trigger> | <text>" lines in ~/omawin-account/snippets.txt and the shared
// folder's (this account's win on the same trigger). Type the trigger anywhere and it's replaced.
public static class Snippets
{
    public static List<Snippet> Parse(IEnumerable<IEnumerable<string>> layers)
    {
        var byTrigger = new Dictionary<string, Snippet>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var lines in layers)
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var bar = line.IndexOf(" | ", StringComparison.Ordinal);
                if (bar <= 0) continue;
                var trigger = line[..bar].Trim();
                // At least two characters, none of them spaces: one character would fire all the time.
                if (trigger.Length < 2 || trigger.Any(char.IsWhiteSpace)) continue;
                if (!byTrigger.ContainsKey(trigger)) order.Add(trigger);
                byTrigger[trigger] = new Snippet(trigger, line[(bar + 3)..]);
            }
        return order.Select(t => byTrigger[t]).ToList();
    }

    // {date} 2026-10-31, {time} 14:05, {clipboard} the newest copied text; \n a new line, \t a tab.
    public static string Expand(string text, DateTime now, Func<string> clipboard)
    {
        var b = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is 'n' or 't' or '\\')
            {
                b.Append(text[i + 1] switch { 'n' => '\n', 't' => '\t', _ => '\\' });
                i++;
            }
            else b.Append(text[i]);
        }
        var s = b.ToString();
        if (s.Contains("{date}")) s = s.Replace("{date}", now.ToString("yyyy-MM-dd"));
        if (s.Contains("{time}")) s = s.Replace("{time}", now.ToString("HH:mm"));
        if (s.Contains("{clipboard}")) s = s.Replace("{clipboard}", clipboard());
        return s;
    }

    // Triggers that can never fire because a shorter one ends the same way and fires first
    // (";d" before ";date"): said by `omawin keyboard`.
    public static IEnumerable<(string Never, string Because)> Shadowed(IReadOnlyList<Snippet> list) =>
        from a in list
        from b in list
        where a.Trigger != b.Trigger && b.Trigger.Length < a.Trigger.Length && Contains(a.Trigger[..^1], b.Trigger)
        select (a.Trigger, b.Trigger);

    static bool Contains(string typedSoFar, string trigger) => typedSoFar.Contains(trigger, StringComparison.Ordinal);
}

// The last characters typed in the window in front, to spot a snippet's trigger. Cleared when the
// window in front changes and by keys that move the caret (Enter, Tab, Esc, arrows, Home…), since
// then what's on screen before the caret isn't what was typed.
public sealed class SnippetTyping
{
    readonly char[] typed = new char[64];
    int length;

    public void Reset() => length = 0;

    public void Backspace() { if (length > 0) length--; }

    // The snippet whose trigger the text now ends with, or null. When one matches, the buffer
    // starts again (the trigger is about to be replaced).
    public Snippet? Feed(char c, IReadOnlyList<Snippet> snippets)
    {
        if (length == typed.Length) { Array.Copy(typed, 1, typed, 0, length - 1); length--; }
        typed[length++] = c;
        var tail = new ReadOnlySpan<char>(typed, 0, length);
        Snippet? best = null; // the longest: ";sig2" over ";2" when both end here
        foreach (var s in snippets)
            if (tail.EndsWith(s.Trigger.AsSpan(), StringComparison.Ordinal) && (best == null || s.Trigger.Length > best.Trigger.Length))
                best = s;
        if (best != null) length = 0;
        return best;
    }

    // Keys that move the caret or leave the field: what was typed before them no longer counts.
    public static bool Breaks(uint vk) => vk is 0x0D or 0x09 or 0x1B or (>= 0x21 and <= 0x28) or 0x2E;
}

// Where the keyboard's focus is when Space is pressed: the classes of the window in front, of
// the control with the focus inside it, and of that control's parent.
public readonly record struct KeyFocus(string Foreground, string Focus, string FocusParent);

// Quick Look in Explorer (roadmap 8.4): Space on a file in Explorer's file list previews it.
// Space is taken only when it's plainly meant for that: Explorer in front, the focus in its file
// list (DirectUIHWND inside SHELLDLL_DefView; renaming, the address bar, the search box and the
// folder tree are other controls), no modifier (Ctrl+Space selects), and not right after a
// letter or digit (Explorer's type-to-find takes spaces in names: "my file"). Its key-up and
// auto-repeats are swallowed with it, and it fires once. The focus is only asked for on a Space
// that could be one, so other keys cost nothing.
public sealed class QuickLookKey
{
    public const ushort VkSpace = 0x20;
    public const uint TypeAheadMs = 1000;
    public const string Explorer = "CabinetWClass", FileList = "DirectUIHWND", FileView = "SHELLDLL_DefView";

    uint typedAt;
    bool typed, held;

    public bool On { get; set; }

    public static bool InFileList(KeyFocus f) => f.Foreground == Explorer && f.Focus == FileList && f.FocusParent == FileView;

    // What was typed in another window doesn't count (KeyHook calls it when the window in front changes).
    public void Reset() { typed = held = false; }

    public bool OnKey(uint vk, bool down, bool modified, uint time, Func<KeyFocus> focus, out bool fire)
    {
        fire = false;
        if (!On) return false;
        if (vk != VkSpace)
        {
            // Letters and digits start (or go on with) type-to-find.
            if (down && vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x60 and <= 0x69)) { typed = true; typedAt = time; }
            return false;
        }
        if (!down)
        {
            bool mine = held;
            held = false;
            return mine;
        }
        if (held) return true; // auto-repeat of the Space that opened it
        if (modified || typed && unchecked(time - typedAt) < TypeAheadMs) return false;
        if (!InFileList(focus())) return false;
        held = fire = true;
        return true;
    }
}
