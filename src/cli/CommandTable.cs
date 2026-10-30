using System.Text;

namespace Omawin.Cli;

// One row of bin/commands.txt: `<name> | <summary> | <action>`. Section counts the blank lines
// before it, which split `omawin help` into groups of rows.
public sealed record Command(string Name, string Summary, string Action, int Section);

// What the words typed after `omawin` mean.
public abstract record Resolution;
public sealed record ShowVersion : Resolution;
public sealed record ShowHelp(string? Group) : Resolution;
public sealed record RunCommand(Command Command, string[] Extra) : Resolution;
// The group exists but has no command by that name: an error, then the group's commands.
public sealed record NotInGroup(string Group, string Typed) : Resolution;
public sealed record Unknown(string Typed) : Resolution;

// bin/commands.txt, loaded and matched. Pure (no files, no processes) so it's tested anywhere;
// the routing is bin/omawin.cs's, rule for rule, so both read the file the same way.
public sealed class CommandTable(IReadOnlyList<Command> commands)
{
    public IReadOnlyList<Command> Commands { get; } = commands;

    public static CommandTable Parse(IEnumerable<string> lines)
    {
        var list = new List<Command>();
        int section = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) section++;
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', 3);
            if (parts.Length < 3) continue;
            list.Add(new Command(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), section));
        }
        return new CommandTable(list);
    }

    public Command? Find(string name) => Commands.FirstOrDefault(c => c.Name == name);

    public bool IsGroup(string word) => Commands.Any(c => c.Name.StartsWith(word + " ", StringComparison.Ordinal));

    public Resolution Resolve(string[] args)
    {
        if (args is ["--version" or "-v"]) return new ShowVersion();
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h") return new ShowHelp(args.Length > 1 ? args[1] : null);
        // The longest command the words spell: "theme set gruvbox" is "theme set" + gruvbox.
        for (int n = Math.Min(args.Length, 3); n > 0; n--)
            if (Find(string.Join(' ', args[..n])) is { } command) return new RunCommand(command, args[n..]);
        var typed = string.Join(' ', args);
        if (IsGroup(args[0])) return args.Length == 1 ? new ShowHelp(args[0]) : new NotInGroup(args[0], typed);
        return new Unknown(typed);
    }

    // `omawin help [group]`: names padded to one column, a blank line between sections.
    // Null when the group has no commands.
    public string? Help(string? group)
    {
        var shown = group == null ? Commands : Commands.Where(c => c.Name == group || c.Name.StartsWith(group + " ", StringComparison.Ordinal)).ToList();
        if (shown.Count == 0) return null;
        var text = new StringBuilder();
        if (group == null) text.Append("omawin <command> [arguments]: this setup's commands (bin/commands.txt)\n\n");
        int width = shown.Max(c => c.Name.Length) + 2;
        int last = -1;
        foreach (var c in shown)
        {
            if (last >= 0 && c.Section != last) text.Append('\n');
            last = c.Section;
            text.Append("  ").Append(c.Name.PadRight(width)).Append(c.Summary).Append('\n');
        }
        return text.ToString();
    }
}
