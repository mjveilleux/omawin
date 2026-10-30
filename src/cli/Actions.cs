using System.Text;
using System.Text.RegularExpressions;

namespace Omawin.Cli;

// What a command's action asks for, worked out before anything runs (so it's tested anywhere).
public abstract record Plan;
public sealed record SendToApp(string Request) : Plan;                       // app <request>
public sealed record StartProcess(string File, string Arguments) : Plan;     // pwsh <script>, run <program>
public sealed record RunBuiltin(string Name, string[] Args) : Plan;          // builtin <name>
public sealed record BadAction(string Message) : Plan;

public static partial class Actions
{
    // Done by omawin itself (bin/omawin.cs's Builtin); commands.txt may only name these.
    public static readonly string[] Builtins =
        ["launch-terminal", "launch-project", "launch-editor", "terminal-here", "terminal-warm", "start-glazewm", "start-bar", "update-terminal", "default"];

    // repo: this repo's folder ({repo}); user: this account's folder ({user}, docs/work.md).
    public static Plan Plan(Command command, string[] extra, string repo, string user)
    {
        var action = command.Action.Replace("{repo}", repo);
        var space = action.IndexOf(' ');
        var kind = space < 0 ? action : action[..space];
        var rest = space < 0 ? "" : action[(space + 1)..].Trim();
        var words = string.Join(' ', extra.Select(Quote));
        switch (kind)
        {
            case "app":
                // The menu app splits the request itself, so extra words go as typed.
                return new SendToApp((rest + " " + string.Join(' ', extra)).Trim());
            case "pwsh":
            {
                // The script's path from the repo root (or {user}/...), then its fixed arguments.
                // {user} is filled in after the split: that folder's path may have spaces.
                var script = rest.Split(' ', 2);
                if (script[0].Length == 0) return new BadAction($"commands.txt: \"{command.Name}\": pwsh needs a script");
                var path = WindowsJoin(repo, script[0].Replace("{user}", user).Replace('/', '\\'));
                var arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(path)
                    + (script.Length > 1 ? " " + script[1] : "") + " " + words;
                return new StartProcess("pwsh", arguments);
            }
            case "run":
            {
                var program = rest.Split(' ', 2);
                if (program[0].Length == 0) return new BadAction($"commands.txt: \"{command.Name}\": run needs a program");
                return new StartProcess(program[0], ((program.Length > 1 ? program[1] : "") + " " + words).Trim());
            }
            case "builtin":
            {
                var fixedArgs = Split(rest);
                if (fixedArgs.Length == 0) return new BadAction($"commands.txt: \"{command.Name}\": builtin needs a name");
                if (!Builtins.Contains(fixedArgs[0])) return new BadAction($"commands.txt: \"{command.Name}\": no builtin \"{fixedArgs[0]}\"");
                return new RunBuiltin(fixedArgs[0], [.. fixedArgs.Skip(1), .. extra]);
            }
            default:
                return new BadAction($"commands.txt: \"{command.Name}\" has an unknown action: {command.Action}");
        }
    }

    // Path.Combine with Windows' rules on any OS: a rooted second part (C:\..., \\server, \...)
    // stands alone, as {user} often is.
    public static string WindowsJoin(string root, string relative)
    {
        if (relative.Length >= 2 && relative[1] == ':' || relative.StartsWith('\\') || relative.StartsWith('/')) return relative;
        return root.TrimEnd('\\', '/') + "\\" + relative;
    }

    [GeneratedRegex("\"([^\"]*)\"|(\\S+)")] private static partial Regex Word();

    // The fixed words of a builtin action: spaces split, "double quotes" keep a word whole.
    public static string[] Split(string text) =>
        Word().Matches(text).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();

    // One argument for a command line, the way programs split it again (CommandLineToArgvW):
    // backslashes are literal except before a quote, so those (and a trailing one) are doubled.
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\') { slashes++; continue; }
            quoted.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(ch);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}
