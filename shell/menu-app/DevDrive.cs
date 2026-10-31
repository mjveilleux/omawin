// `omawin dev-drive` (roadmap 8.32): Microsoft's Dev Drive, a ReFS volume Defender scans in its
// performance mode, which makes builds and package installs noticeably faster on Windows. omawin
// makes one as a growing virtual disk (a VHDX), in the one admin prompt after saying exactly what
// it will do, and points the package managers' caches at it (your environment variables, each
// undone with omawin env undo). Your code is never moved: new projects can live on it.
//
// This file is the command and its rules; the disk side is the elevated script's devdrive verb
// (bin/settings-admin.ps1) and IDevDriveHost (DevDriveWin.cs). Pure C#: tested in menu-app.Tests.

namespace SystemMenu;

public interface IDevDriveHost
{
    int Build { get; }
    // Drive letters in use, and the ReFS volumes among them (on Windows 11 client editions,
    // ReFS is only made for Dev Drives).
    IReadOnlyList<(char Letter, bool ReFS)> Volumes();
    Task<(bool Ran, string? Error)> CreateAsync(string vhdx, int gb, char letter);
    void MakeFolder(string path);
}

public static class DevDrive
{
    public const string Usage = "omawin dev-drive | create [<GB>] [<letter>] | caches <letter>";
    public const int MinGB = 50, DefaultGB = 100, MinBuild = 22621;
    public const string DefaultVhdx = @"C:\DevDrive\dev.vhdx";

    // The caches that move by an environment variable, each to <drive>:\packages\<sub>. Not here:
    // CARGO_HOME (it moves cargo's own programs and PATH with it) and MAVEN_OPTS (a value of
    // yours, not a folder): asked about on their own, never set here.
    public static readonly (string Var, string Sub)[] CacheVars =
    [
        ("npm_config_cache", @"packages\npm"),
        ("YARN_CACHE_FOLDER", @"packages\yarn"),
        ("BUN_INSTALL_CACHE_DIR", @"packages\bun"),
        ("NUGET_PACKAGES", @"packages\nuget"),
        ("PIP_CACHE_DIR", @"packages\pip"),
        ("UV_CACHE_DIR", @"packages\uv"),
        ("GOMODCACHE", @"packages\go\mod"),
        ("GOCACHE", @"packages\go\build"),
        ("GRADLE_USER_HOME", @"packages\gradle"),
        ("VCPKG_DEFAULT_BINARY_CACHE", @"packages\vcpkg"),
        ("MISE_CACHE_DIR", @"packages\mise"),
    ];

    // The first letter from D that isn't in use.
    public static char? FreeLetter(IEnumerable<char> used)
    {
        var taken = used.Select(char.ToUpperInvariant).ToHashSet();
        for (char c = 'D'; c <= 'Z'; c++) if (!taken.Contains(c)) return c;
        return null;
    }

    // Each cache variable for this drive: set it, or leave one you've set to somewhere else.
    public static List<(string Var, string Value, string? Kept)> Caches(char letter, Func<string, string?> current) =>
        CacheVars.Select(c =>
        {
            var value = $@"{char.ToUpperInvariant(letter)}:\{c.Sub}";
            var now = current(c.Var);
            return (c.Var, value, now == null || now.Equals(value, StringComparison.OrdinalIgnoreCase) ? null : now);
        }).ToList();

    public static async Task<SettingCommands.Answer> Answer(IDevDriveHost host, IEnvStore env, SettingHistory history, string[] a)
    {
        bool yes = a.Contains("--yes");
        var words = a.Where(x => x != "--yes").ToList();
        var volumes = host.Volumes();
        switch (words.FirstOrDefault())
        {
            case null or "status":
            {
                var refs = volumes.Where(v => v.ReFS).Select(v => $"{v.Letter}:").ToList();
                var set = CacheVars.Count(c => env.Get(EnvScope.User, c.Var) != null);
                return new(0, (refs.Count > 0 ? $"Dev Drive: {string.Join(", ", refs)} (ReFS)" : "No Dev Drive on this PC: omawin dev-drive create makes one (one admin prompt)")
                    + $"\nPackage caches pointed somewhere: {set} of {CacheVars.Length} (omawin dev-drive caches <letter> points them at a Dev Drive)");
            }
            case "create":
            {
                if (host.Build < MinBuild) return new(1, $"Dev Drive needs Windows 11 (build {MinBuild} or later, updated); this is {host.Build}");
                int gb = DefaultGB;
                char? letter = null;
                foreach (var w in words.Skip(1))
                {
                    if (int.TryParse(w, out var n)) gb = n;
                    else if (w.TrimEnd(':') is [var c] && char.IsAsciiLetter(c)) letter = char.ToUpperInvariant(c);
                    else return new(1, Usage);
                }
                if (gb < MinGB) return new(1, $"a Dev Drive is at least {MinGB} GB");
                letter ??= FreeLetter(volumes.Select(v => v.Letter));
                if (letter is not { } l || l < 'D') return new(1, "no free drive letter from D to Z");
                if (volumes.Any(v => v.Letter == l)) return new(1, $"{l}: is in use");
                if (!yes)
                    return new(3, $"Make a Dev Drive, in one admin prompt:\n  {l}: a ReFS drive of up to {gb} GB, in the file {DefaultVhdx} (it grows as it's used)\n"
                        + "  Defender scans it in its performance mode (trusted), and it's attached again at every start-up (a task)\n"
                        + "Nothing of yours moves; omawin dev-drive caches " + l + " then points the package caches at it.");
                var (ran, error) = await host.CreateAsync(DefaultVhdx, gb, l);
                if (!ran) return new(1, "Not now: nothing changed");
                return error == null ? new(0, $"Dev Drive {l}: made. Next: omawin dev-drive caches {l}") : new(1, $"Dev Drive: {error}");
            }
            case "caches":
            {
                if (words.Count < 2 || words[1].TrimEnd(':') is not [var c] || !char.IsAsciiLetter(c)) return new(1, "omawin dev-drive caches <letter>");
                var l = char.ToUpperInvariant(c);
                if (!volumes.Any(v => v.Letter == l)) return new(1, $"no drive {l}:");
                var plan = Caches(l, v => env.Get(EnvScope.User, v)?.Value);
                var todo = plan.Where(p => p.Kept == null && !Equals(env.Get(EnvScope.User, p.Var)?.Value, p.Value)).ToList();
                var kept = plan.Where(p => p.Kept != null).ToList();
                if (todo.Count == 0) return new(0, "The package caches are already as they'd be" + (kept.Count > 0 ? $" ({kept.Count} of yours point elsewhere, kept)" : ""));
                if (!yes)
                    return new(3, $"Point {todo.Count} package caches at {l}: (your environment variables; the old caches stay where they are):\n  "
                        + string.Join("\n  ", todo.Select(p => $"{p.Var} = {p.Value}"))
                        + (kept.Count > 0 ? "\nKept, set by you: " + string.Join(", ", kept.Select(p => p.Var)) : ""));
                var said = new List<string>();
                foreach (var p in todo)
                {
                    host.MakeFolder(p.Value);
                    var r = await EnvVars.Answer(env, history, ["set", p.Var, p.Value]);
                    if (r.Code != 0) said.Add(r.Text);
                }
                return new(said.Count == 0 ? 0 : 1, said.Count == 0
                    ? $"{todo.Count} package caches now on {l}: (new terminals use them; omawin env undo puts back one at a time)"
                    : string.Join('\n', said));
            }
            default:
                return new(2, Usage);
        }
    }
}
