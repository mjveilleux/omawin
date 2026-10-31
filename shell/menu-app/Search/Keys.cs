namespace SystemMenu.Search;

// What Tab, Esc and Backspace do in One Search's bar, in one place and without the window
// (plans/one-search.md, guardrail 9: typing is never lost, and Esc steps back one thing at a time:
// the actions, then the text, then a view inside the filter, then the filter, then the window).
// Pure; the window does what the answer says.

// Where the bar is: what's typed, the filter token, whether Ctrl+K's actions are open, and
// whether a view inside the filter is showing (a menu section under Commands, a settings area,
// a page opened from a result).
public readonly record struct BarState(string Text, Tag? Filter, bool ActionsOpen = false, bool InView = false);

public enum BarMove { None, SetFilter, CloseActions, ClearText, Back, Hide }

// Filter and Text are the bar's new token and text, for SetFilter.
public readonly record struct BarAnswer(BarMove Move, Tag? Filter = null, string Text = "");

public static class SearchKeys
{
    // Tab / Shift+Tab. A filter's name typed in All ("set") locks that filter, the name gone; a
    // prefix ("/road", ">theme") becomes its filter's token, the words kept; otherwise the next or
    // previous filter, the words kept.
    public static BarAnswer Tab(BarState s, bool back)
    {
        if (s.Text.Length > 0 && s.Text[0] is '/' or '>')
            return new(BarMove.SetFilter, s.Text[0] == '/' ? Tag.File : Tag.Command, s.Text[1..].TrimStart());
        if (s.Filter is null && QueryParser.TagFromTyped(s.Text) is Tag typed)
            return new(BarMove.SetFilter, typed, "");
        return new(BarMove.SetFilter, QueryParser.NextFilter(s.Filter, back), s.Text);
    }

    public static BarAnswer Escape(BarState s)
    {
        if (s.ActionsOpen) return new(BarMove.CloseActions);
        if (s.Text.Length > 0) return new(BarMove.ClearText, s.Filter);
        if (s.InView) return new(BarMove.Back);
        if (s.Filter is not null) return new(BarMove.SetFilter, null, "");
        return new(BarMove.Hide);
    }

    // Backspace on an empty bar: out of a view, else the filter goes. With text it's a letter.
    public static BarAnswer Backspace(BarState s)
    {
        if (s.Text.Length > 0 || s.ActionsOpen) return new(BarMove.None);
        if (s.InView) return new(BarMove.Back);
        if (s.Filter is not null) return new(BarMove.SetFilter, null, "");
        return new(BarMove.None);
    }
}
