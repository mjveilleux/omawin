using SystemMenu;

namespace Omawin.Windows;

// omawin open-with (roadmap 8.25): pictures, Markdown, JSON and logs opened in omawin's viewer.
// Windows forbids a program from making itself the default (the UserChoice keys are hashed and
// guarded by UCPD since 2024), so omawin only does what any installer may: registers
// omawin-view.exe for those types in this account's classes (HKCU, no admin), so it's offered in
// "Open with" and on its own page of Settings › Default apps, and opens that page; you pick it
// there, type by type or all at once. UserChoice is read (to say what each type opens with now),
// never written. This file is the plan (which values, where), pure so it's tested on Linux;
// OpenWithRegistry.cs writes it.
public static class OpenWith
{
    // The name Settings knows omawin by: ms-settings:defaultapps?registeredAppUser=omawin opens
    // its page (the RegisteredApplications value's name).
    public const string AppName = "omawin";
    public const string ProgIdPrefix = "omawin.view.";
    public const string ExeName = "omawin-view.exe";
    public const string Capabilities = @"Software\omawin\Capabilities";
    public const string SettingsPage = "ms-settings:defaultapps?registeredAppUser=" + AppName;

    // What the viewer shows (ViewerFile, the menu app's own list); what `on` offers with no types
    // named is the roadmap's set: pictures, Markdown, JSON and logs (a .txt or .csv stays your
    // editor's or Excel's unless you name it).
    public static IReadOnlyList<string> Supported => [.. ViewerFile.Pictures, .. ViewerFile.Texts];
    public static IReadOnlyList<string> Default => [.. ViewerFile.Pictures, "md", "markdown", "json", "jsonc", "log"];

    // The types named: extensions with or without the dot, or a group (pictures, text, all).
    // Unknown words come back in `unknown` (and nothing is done).
    public static List<string> Types(IEnumerable<string> words, out List<string> unknown)
    {
        var types = new List<string>();
        unknown = [];
        foreach (var raw in words)
        {
            var w = raw.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
            IEnumerable<string> add = w switch
            {
                "pictures" or "images" => ViewerFile.Pictures,
                "text" => ViewerFile.Texts,
                "all" => Supported,
                _ when Supported.Contains(w) => [w],
                _ => [],
            };
            if (!add.Any()) { unknown.Add(raw); continue; }
            foreach (var t in add) if (!types.Contains(t)) types.Add(t);
        }
        return types;
    }

    // One registry value under HKCU: its key, its name ("" is the key's default) and its text.
    public sealed record Value(string Key, string Name, string Data);

    public static string ProgId(string type) => ProgIdPrefix + type;
    public static string Command(string exe) => $"\"{exe}\" \"%1\"";

    // Everything that makes Windows offer omawin-view.exe for these types:
    //   - a ProgID per type (omawin.view.png), what opening it runs, and its icon
    //   - the type's OpenWithProgids naming it: "Open with" lists it
    //   - Applications\omawin-view.exe: its name in those lists and the types it takes
    //   - omawin's Capabilities and its RegisteredApplications entry: its page in Default apps
    public static List<Value> Plan(string exe, IEnumerable<string> types)
    {
        var list = new List<Value>();
        string app = @"Software\Classes\Applications\" + ExeName;
        list.Add(new(app, "FriendlyAppName", "omawin viewer"));
        list.Add(new(app + @"\shell\open\command", "", Command(exe)));
        list.Add(new(Capabilities, "ApplicationName", "omawin viewer"));
        list.Add(new(Capabilities, "ApplicationDescription", "Pictures and text (Markdown, JSON, logs) in omawin's own viewer, in your theme."));
        list.Add(new(@"Software\RegisteredApplications", AppName, Capabilities));
        foreach (var t in types)
        {
            string progId = @"Software\Classes\" + ProgId(t);
            bool picture = ViewerFile.Pictures.Contains(t);
            list.Add(new(progId, "", $"{t.ToUpperInvariant()} {(picture ? "picture" : "file")} (omawin viewer)"));
            list.Add(new(progId + @"\DefaultIcon", "", $"\"{exe}\",0"));
            list.Add(new(progId + @"\shell\open\command", "", Command(exe)));
            list.Add(new($@"Software\Classes\.{t}\OpenWithProgids", ProgId(t), ""));
            list.Add(new(app + @"\SupportedTypes", "." + t, ""));
            list.Add(new(Capabilities + @"\FileAssociations", "." + t, ProgId(t)));
        }
        return list;
    }

    // What `off` removes: only what Plan writes, for every type the viewer could have had.
    public static (List<string> Keys, List<Value> Values) Removal()
    {
        var keys = new List<string> { @"Software\Classes\Applications\" + ExeName, Capabilities };
        var values = new List<Value> { new(@"Software\RegisteredApplications", AppName, "") };
        foreach (var t in Supported)
        {
            keys.Add(@"Software\Classes\" + ProgId(t));
            values.Add(new($@"Software\Classes\.{t}\OpenWithProgids", ProgId(t), ""));
        }
        return (keys, values);
    }

    // A line of `omawin open-with`: the type, whether omawin's offered for it, and what Windows
    // opens it with now (the ProgID you picked, by its name where it has one).
    public static string Line(string type, bool offered, string? choice, string? choiceName) =>
        $"{"." + type,-9} {(offered ? "offered" : "-"),-8} " + (choice == null ? "Windows' default"
            : choice.Equals(ProgId(type), StringComparison.OrdinalIgnoreCase) ? "omawin viewer"
            : choiceName is { Length: > 0 } && !choiceName.StartsWith('@') ? choiceName : choice);
}
