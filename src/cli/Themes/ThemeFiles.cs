using System.Text.RegularExpressions;

namespace Omawin.Cli.Themes;

// A theme is data (plans/09-for-everyone.md §1): palettes, pictures, per-app colour files.
// `omawin theme install` copies only what Allowed() accepts and names what it skipped, so a
// theme can never bring a script or a program.
public static partial class ThemeFiles
{
    // What Omarchy's themes hold today: colors.toml, backgrounds (jpg, png, webp, mp4 for moving
    // ones), icons.theme, vscode.json, btop's .theme, neovim.lua, a few .rgb and .conf files.
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".toml", ".json", ".jsonc", ".yaml", ".yml", ".conf", ".css", ".ini", ".theme", ".rgb", ".txt", ".md",
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".mp4", ".webm", ".lua",
    };

    public static bool Allowed(string relative)
    {
        var name = relative.Replace('\\', '/');
        if (name.Length == 0 || name.StartsWith('/') || (name.Length > 1 && name[1] == ':')) return false;
        var parts = name.Split('/');
        if (parts.Any(p => p is ".." or "." || p.StartsWith('.'))) return false; // .git, dotfiles, climbing out
        var ext = Path.GetExtension(name);
        if (!Extensions.Contains(ext)) return false;
        // Lua is code: only Omarchy's Neovim colour spec, at the top of the theme, and checked (Spec).
        return !ext.Equals(".lua", StringComparison.OrdinalIgnoreCase) || name == "neovim.lua";
    }

    // Lua's tokens a plain table literal needs; anything else (a call, function, ..) is code.
    [GeneratedRegex("""\s+|--[^\n]*|"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'|[A-Za-z_][A-Za-z0-9_]*|-?\d+(?:\.\d+)?|[{}=,;]|.""")]
    private static partial Regex Token();
    [GeneratedRegex("""^\s*["']([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)["']""")]
    private static partial Regex Repo();

    // neovim.lua is a lazy.nvim spec: `return { { "owner/plugin" }, { "LazyVim/LazyVim", opts = { colorscheme = "x" } } }`.
    // Neovim downloads and runs the plugins it names, so: it must be a plain table (no function,
    // no call, no require), and the plugins it names are returned to be shown before install.
    // Null when it's code.
    public static IReadOnlyList<string>? Spec(string lua)
    {
        var tokens = Token().Matches(lua).Select(m => m.Value).Where(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith("--")).ToList();
        if (tokens.Count < 3 || tokens[0] != "return" || tokens[1] != "{") return null;
        foreach (var t in tokens.Skip(1))
        {
            bool ok = t is "{" or "}" or "=" or "," or ";" or "true" or "false" or "nil"
                || t[0] is '"' or '\'' || char.IsDigit(t[0]) || t[0] == '-' && t.Length > 1
                || (char.IsLetter(t[0]) || t[0] == '_') && t is not ("function" or "end" or "require" or "return" or "local" or "do" or "load" or "dofile" or "loadstring");
            if (!ok) return null;
        }
        // Identifiers are only allowed as keys: one must be followed by "=".
        for (int i = 1; i < tokens.Count; i++)
            if ((char.IsLetter(tokens[i][0]) || tokens[i][0] == '_') && tokens[i] is not ("true" or "false" or "nil")
                && (i + 1 >= tokens.Count || tokens[i + 1] != "="))
                return null;
        var plugins = new List<string>();
        for (int i = 0; i + 1 < tokens.Count; i++)
            if (tokens[i] == "{" && Repo().Match(tokens[i + 1]) is { Success: true } m) plugins.Add(m.Groups[1].Value);
        return plugins;
    }
}
