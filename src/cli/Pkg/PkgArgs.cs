namespace Omawin.Cli.Pkg;

// `omawin pkg <verb> [names] [flags]`, GNU-style as omawin's commands take them:
//   omawin pkg install go --from mise      omawin pkg move fd --to scoop
public sealed record PkgArgs(string Verb, List<string> Names, string? From = null, string? To = null, bool Linux = false, string? Scope = null, bool Refresh = false, bool Updates = false, bool All = false)
{
    public static PkgArgs Parse(IReadOnlyList<string> args, out string? error)
    {
        string? problem = null, from = null, to = null, scope = null;
        bool linux = false, refresh = false, updates = false, all = false;
        var names = new List<string>();
        var verb = args.Count > 0 ? args[0] : "";
        for (int i = 1; i < args.Count; i++)
        {
            var a = args[i];
            string? Value()
            {
                // --from mise and --from=mise both.
                var eq = a.IndexOf('=');
                if (eq > 0) return a[(eq + 1)..];
                if (i + 1 < args.Count && !args[i + 1].StartsWith('-')) return args[++i];
                problem ??= $"{a} needs a value";
                return null;
            }
            switch (a.Split('=', 2)[0])
            {
                case "--from": from = Value()?.ToLowerInvariant(); break;
                case "--to": to = Value()?.ToLowerInvariant(); break;
                case "--linux": linux = true; break;
                case "--machine": scope = "machine"; break;
                case "--user": scope = "user"; break;
                case "--refresh": refresh = true; break;
                case "--updates": updates = true; break;
                case "--all": all = true; break;
                // pkg update -List, as bin/pkg-update.ps1 took it.
                case "-List" or "--list" when verb == "update": verb = "list"; break;
                default:
                    if (a.StartsWith('-')) problem ??= $"no option {a}";
                    else names.Add(a);
                    break;
            }
        }
        error = problem;
        return new PkgArgs(verb, names, from, to, linux, scope, refresh, updates, all);
    }
}
