

namespace SystemMenu.Tests.Keys;

// The keyboard hook's decisions (KeyRules.cs): Caps Lock's remaps, the Copilot key, snippets.
public class KeyRulesTests
{
    const uint Caps = 0x14, A = 0x41;

    static (bool Swallow, List<SentKey> Sent) Press(CapsLockRemap r, uint vk, bool down, uint time)
    {
        var sent = new List<SentKey>();
        return (r.OnKey(vk, down, time, sent), sent);
    }

    [Fact]
    public void CapsLockLeftAloneUntilChosen()
    {
        var r = new CapsLockRemap();
        Assert.False(r.Active);
        Assert.False(Press(r, Caps, true, 0).Swallow);
        r.Mode = "nonsense";
        Assert.Equal("caps", r.Mode);
    }

    [Fact]
    public void CapsAsEscapeAndControlMirrorTheKey()
    {
        var r = new CapsLockRemap { Mode = "escape" };
        Assert.Equal([new SentKey(0x1B, true)], Press(r, Caps, true, 0).Sent);
        Assert.Empty(Press(r, Caps, true, 30).Sent); // auto-repeat, swallowed
        Assert.Equal([new SentKey(0x1B, false)], Press(r, Caps, false, 60).Sent);
        r.Mode = "control";
        Assert.Equal([new SentKey(0xA2, true)], Press(r, Caps, true, 0).Sent);
        Assert.False(Press(r, A, true, 10).Swallow); // the A goes through with Control held
        Assert.Equal([new SentKey(0xA2, false)], Press(r, Caps, false, 60).Sent);
    }

    [Fact]
    public void EscapeWhenTapped()
    {
        var r = new CapsLockRemap { Mode = "escape-control" };
        Assert.Empty(Press(r, Caps, true, 1000).Sent);
        Assert.Equal([new SentKey(0x1B, true), new SentKey(0x1B, false)], Press(r, Caps, false, 1100).Sent);
        // Held too long alone: neither.
        Press(r, Caps, true, 2000);
        Assert.Empty(Press(r, Caps, false, 2600).Sent);
    }

    [Fact]
    public void ControlWhenHeldWithAnotherKeyInTheRightOrder()
    {
        var r = new CapsLockRemap { Mode = "escape-control" };
        Press(r, Caps, true, 0);
        var (swallow, sent) = Press(r, A, true, 50);
        Assert.True(swallow); // the A is sent again after Control, so the window sees Ctrl+A
        Assert.Equal([new SentKey(0xA2, true), new SentKey((ushort)A, true)], sent);
        Assert.False(Press(r, A, false, 80).Swallow);
        Assert.False(Press(r, A, true, 90).Swallow); // Control already down: keys go straight through
        Assert.Equal([new SentKey(0xA2, false)], Press(r, Caps, false, 100).Sent); // no Escape
    }

    [Fact]
    public void TapTimeSurvivesTheTickCounterWrapping()
    {
        var r = new CapsLockRemap { Mode = "escape-control" };
        Press(r, Caps, true, uint.MaxValue - 50);
        Assert.Equal(2, Press(r, Caps, false, 49).Sent.Count);
    }

    [Fact]
    public void CopilotKeyIsWinShiftF23Only()
    {
        var k = new CopilotKey();
        var sent = new List<SentKey>();
        Assert.False(k.OnKey(0x86, true, true, true, sent, out var fire));
        k.On = true;
        Assert.True(k.OnKey(0x86, true, true, true, sent, out fire));
        Assert.True(fire);
        Assert.Equal([new SentKey(0xE8, true), new SentKey(0xE8, false)], sent); // keeps Start shut
        Assert.True(k.OnKey(0x86, false, true, true, sent, out fire));
        Assert.False(fire);
        Assert.False(k.OnKey(0x86, true, false, false, sent, out _)); // a plain F23 stays F23
        Assert.False(k.OnKey(0x41, true, true, true, sent, out _));
    }

    [Fact]
    public void SnippetsParseLayeredLines()
    {
        var list = Snippets.Parse([
            ["# mine", ";sig | Best,\\nMe", ";date | {date}", "x | too short", "two words | no", "bad line"],
            [";sig | Cheers,\\nMe"],
        ]);
        Assert.Equal([";sig", ";date"], list.Select(s => s.Trigger));
        Assert.Equal("Cheers,\\nMe", list[0].Text); // the account's wins
    }

    [Fact]
    public void ExpandFillsPlaceholders()
    {
        var now = new DateTime(2026, 10, 31, 14, 5, 0);
        Assert.Equal("Best,\nMe\tX \\n", Snippets.Expand("Best,\\nMe\\tX \\\\n", now, () => ""));
        Assert.Equal("2026-10-31 14:05 copied", Snippets.Expand("{date} {time} {clipboard}", now, () => "copied"));
    }

    [Fact]
    public void TypingSpotsTheLongestTrigger()
    {
        List<Snippet> list = [new(";2", "two"), new(";sig2", "sig two"), new(";d", "d")];
        var t = new SnippetTyping();
        Snippet? hit = null;
        foreach (var c in "hello ;sig2") hit = t.Feed(c, list) ?? hit;
        Assert.Equal(";sig2", hit!.Trigger);
        Assert.Null(t.Feed('2', list)); // started again after a hit
    }

    [Fact]
    public void BackspaceAndResetForgetTyping()
    {
        List<Snippet> list = [new(";x", "x")];
        var t = new SnippetTyping();
        t.Feed(';', list);
        t.Backspace();
        Assert.Null(t.Feed('x', list));
        t.Feed(';', list);
        t.Reset();
        Assert.Null(t.Feed('x', list));
        Assert.True(SnippetTyping.Breaks(0x0D));
        Assert.True(SnippetTyping.Breaks(0x25));
        Assert.False(SnippetTyping.Breaks(0x41));
    }

    [Fact]
    public void ShadowedTriggersAreFound()
    {
        List<Snippet> list = [new(";d", "d"), new(";date", "{date}"), new(";sig", "s")];
        Assert.Equal([(";date", ";d")], Snippets.Shadowed(list));
    }
}
