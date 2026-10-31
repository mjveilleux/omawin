namespace SystemMenu.Search;

// How each tag looks (plans/one-search.md, guardrail 3): a fixed colour from the theme, an icon
// and a word, never the colour alone. Pure: the colour is named by the theme slot it comes from
// (MainWindow.Search.cs paints it), so every theme gives each tag its own.
public enum Paint { Accent, Green, Yellow, Cyan, Purple, Red, Muted, Gradient, Comment, Foreground }

public static class TagLook
{
    public static Paint PaintOf(Tag tag) => tag switch
    {
        Tag.App => Paint.Accent,
        Tag.Setting => Paint.Green,
        Tag.Command => Paint.Yellow,
        Tag.File => Paint.Cyan,
        Tag.Window => Paint.Purple,
        Tag.Project => Paint.Red,
        Tag.Clipboard => Paint.Muted,
        Tag.Theme => Paint.Gradient,
        Tag.Calculator => Paint.Yellow,
        _ => Paint.Comment,
    };

    // Material Design Icons in the Nerd Font (the menu's glyphs).
    public static int Glyph(Tag? tag) => tag switch
    {
        null => 0xF0349,          // magnify: All
        Tag.App => 0xF003B,       // apps
        Tag.Setting => 0xF0493,   // cog
        Tag.Command => 0xF07B7,   // console-line
        Tag.File => 0xF0214,      // file
        Tag.Window => 0xF05AF,    // window-maximize
        Tag.Project => 0xF062C,   // source-branch
        Tag.Clipboard => 0xF014C, // clipboard-text-outline
        Tag.Theme => 0xF03D8,     // palette
        Tag.Calculator => 0xF00EC,// calculator
        Tag.Package => 0xF03D6,   // package-variant
        Tag.Web => 0xF059F,       // web
        _ => 0xF0142,             // chevron-right: More
    };

    // The small uppercase word on a row and on the filter tab: "SETTING", "FILES".
    public static string Word(Tag tag) => tag switch
    {
        Tag.Clipboard => "CLIP",
        Tag.Calculator => "CALC",
        Tag.Package => "PACKAGES",
        Tag.Web => "WEB",
        Tag.More => "",
        _ => Singular(QueryParser.NameOf(tag)).ToUpperInvariant(),
    };

    // The filter tab's own word: "ALL", "APPS", "SETTINGS".
    public static string TabWord(Tag? tag) => tag is Tag t ? QueryParser.NameOf(t).ToUpperInvariant() : "ALL";

    static string Singular(string name) => name.EndsWith('s') ? name[..^1] : name;
}
