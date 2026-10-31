using System.Text;

namespace Omawin.Cli.Pkg;

// omawin pkg: one package command over winget, Scoop, Chocolatey, mise and the Linux in WSL
// (roadmap 2B, plans/03-install-update-packages.md section 6). A thin layer, not a package
// manager: it picks a manager, runs it, shows the manager's own words as they come, and records
// what it did in packages.tsv so remove and update go back to the same manager.
//
//   omawin pkg install <name...> [--from winget|scoop|choco|mise] [--machine] [--linux]
//   omawin pkg remove <name...> [--linux]
//   omawin pkg update [name...] [--all] [--from <manager>] [--linux]
//   omawin pkg list [--refresh] [--updates] [--linux]
//   omawin pkg info <name>
//   omawin pkg move <name> --to <manager>
//   omawin pkg prefer [manager...]
public sealed class PkgCommand(Host host)
{
    readonly TextWriter o = host.Out;
    string RecordFile => Path.Combine(host.State, "packages.tsv");
    string PreferFile => Path.Combine(host.State, "pkg-prefer.txt");
    string PinsFile => Path.Combine(host.State, "pkg-pins.tsv");
    string InstalledFile => Path.Combine(host.State, "installed.txt");

    NameTable? names;
    NameTable Names => names ??= NameTable.Parse(
        ReadLines(Path.Combine(host.Repo, "default", "packages", "names.txt")),
        ReadLines(Path.Combine(host.Repo, "install", "tools.txt")));

    public int Run(IReadOnlyList<string> args)
    {
        var a = PkgArgs.Parse(args, out var error);
        if (error != null) return Fail(error);
        // No name: the fuzzy pickers over winget's catalogue and what's here (fzf, PowerShell).
        if (a.Verb is "install" or "remove" && a.Names.Count == 0 && !a.Linux)
            return host.Live("pwsh", ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(host.Repo, "bin", $"pkg-{a.Verb}.ps1")]);
        return a.Verb switch
        {
            "install" => Install(a),
            "remove" => Remove(a),
            "update" => Update(a),
            "list" => List(a),
            "info" => a.Names.Count == 1 ? Info(a.Names[0]) : Fail("omawin pkg info <name>"),
            "move" => a.Names.Count == 1 && a.To != null ? Move(a.Names[0], a.To) : Fail("omawin pkg move <name> --to winget|scoop|choco|mise"),
            "prefer" => Prefer(a.Names),
            "index" => Index(a.Force),
            _ => Fail($"no pkg command \"{a.Verb}\" (install, remove, update, list, info, move, prefer, index)"),
        };
    }

    // --- index: winget's catalogue as a list (the Packages page, the fzf pickers) ----------------

    // %LOCALAPPDATA%\launcher\winget-index.tsv, read again only when winget's catalogue changed
    // (its package version, kept in winget-index.version); prints the list's path.
    int Index(bool force)
    {
        var list = Path.Combine(host.State, "winget-index.tsv");
        var stamp = Path.Combine(host.State, "winget-index.version");
        if (host.FindWingetCatalog() is not { } catalog)
            return Fail("winget's catalog isn't on this PC yet: run winget once (winget search x), then try again");
        var (db, version) = catalog;
        if (!force && File.Exists(list) && ReadLines(stamp).FirstOrDefault()?.Trim() == version)
        {
            o.WriteLine(list);
            return 0;
        }
        WriteLines(list, host.ReadWingetCatalog(db));
        WriteLines(stamp, [version]);
        o.WriteLine(list);
        return 0;
    }

    // --- install ----------------------------------------------------------------------------

    int Install(PkgArgs a)
    {
        if (a.Linux) return Linux("install", a.Names);
        var prefer = Choose.Prefer(ReadLines(PreferFile));
        var pins = ReadLines(PinsFile).ToList();
        var rows = ReadRecord();
        var failed = 0;
        var machine = new List<(string Name, string Id)>();   // winget --machine: one prompt, install/admin-batch.ps1
        var choco = new List<(string Name, string Id)>();     // Chocolatey: one prompt, one choco.exe
        foreach (var typed in a.Names)
        {
            var have = PackageRecord.Find(rows, typed, Names);
            if (have.Count > 0 && a.From == null)
            {
                o.WriteLine($"  {typed}: already here ({string.Join(", ", have.Select(Where))}); omawin pkg move {typed} --to <manager> to change that");
                continue;
            }
            var choice = Choose.Manager(typed, a.From, Choose.Pin(pins, typed), prefer, Names, host.Has);
            if (choice is SearchIn search) choice = Search(search);
            if (choice is CantChoose cant) { failed++; o.WriteLine($"  {typed}: {cant.Message}"); continue; }
            var (manager, id) = (UseManager)choice;
            if (!Choose.SafeId(id)) { failed++; o.WriteLine($"  {typed}: \"{id}\" isn't a package id"); continue; }
            var name = Names[typed]?.Name ?? Names.ByManagerId(manager, id)?.Name ?? id;
            if (a.Scope == "machine" && manager is Managers.Scoop or Managers.Mise) { failed++; o.WriteLine($"  {name}: {Managers.Label(manager)} installs for you alone; --machine is winget's"); continue; }
            if (manager == Managers.Choco) { choco.Add((name, id)); continue; }
            if (manager == Managers.Winget && a.Scope == "machine") { machine.Add((name, id)); continue; }

            o.WriteLine($"\n  installing {name} with {Managers.Label(manager)} ({id})");
            var code = host.Live(manager, CommandLines.Install(manager, id, a.Scope));
            var (ok, text) = manager == Managers.Winget ? CommandLines.Winget(code) : (code == 0, $"exit {code}");
            if (!ok && manager == Managers.Winget && CommandLines.NeedsMachineScope(code) && a.Scope == null)
                text = $"it has only a whole-PC installer: omawin pkg install {name} --machine (one admin prompt)";
            if (ok) { Recorded(ref rows, new PackageRow(name, manager, id, Scope: manager == Managers.Winget ? "user" : "")); o.WriteLine($"  ✓ {name}: {text}"); }
            else { failed++; o.WriteLine($"  ✗ {name}: {text}"); }
        }
        failed += InstallMachineWide(machine, ref rows);
        failed += Chocolatey("install", choco, ref rows);
        return failed == 0 ? 0 : 1;
    }

    // Not a name omawin knows: each manager's catalogue, in order; the first with an exact hit
    // (its id or name is what you typed) is used, else you pick from what they found.
    Choice Search(SearchIn s)
    {
        if (!Choose.SafeId(s.Query)) return new CantChoose($"\"{s.Query}\" isn't a package name");
        var found = new List<(string Manager, Installed Hit)>();
        foreach (var m in s.Managers)
        {
            o.WriteLine($"  looking for {s.Query} in {Managers.Label(m)} ...");
            var (_, output) = host.Capture(m, CommandLines.Search(m, s.Query));
            var hits = CommandLines.ParseSearch(m, output, s.Query);
            if (Choose.Exact(hits, s.Query) is { } exact) return new UseManager(m, exact.Id);
            found.AddRange(hits.Take(10).Select(h => (m, h)));
        }
        if (found.Count == 0) return new CantChoose($"no manager has {s.Query} ({string.Join(", ", s.Managers.Select(Managers.Label))})");
        for (int i = 0; i < found.Count; i++)
            o.WriteLine($"  {i + 1,3}  {found[i].Hit.Id,-40} {found[i].Hit.Version,-14} {Managers.Label(found[i].Manager)}  {found[i].Hit.Name}");
        var answer = host.Ask($"  which one? (1-{found.Count}, Enter for none) ");
        if (int.TryParse(answer, out var n) && n >= 1 && n <= found.Count) return new UseManager(found[n - 1].Manager, found[n - 1].Hit.Id);
        return new CantChoose($"several match: omawin pkg install <id> --from <manager>");
    }

    // winget's whole-PC installs, all after one prompt: install/admin-batch.ps1, the install's own
    // admin step, under Windows' PowerShell, which takes only package ids and reports each.
    int InstallMachineWide(List<(string Name, string Id)> packages, ref List<PackageRow> rows)
    {
        if (packages.Count == 0) return 0;
        if (!MayAskForAdmin($"{packages.Count} program{(packages.Count == 1 ? "" : "s")} for the whole PC with winget: {string.Join(", ", packages.Select(p => p.Name))}"))
            return packages.Count;
        var status = host.TempFile("admin-status.txt");
        File.WriteAllText(status, "");
        try
        {
            if (host.Elevated("admin-batch", ["-Status", status, "-Packages", string.Join(',', packages.Select(p => p.Id))]) == null)
            {
                o.WriteLine("  no admin: nothing changed");
                return packages.Count;
            }
            var said = File.ReadAllLines(status).Select(l => l.Split('\t')).Where(f => f.Length >= 2)
                .GroupBy(f => f[0]).ToDictionary(g => g.Key, g => g.Last());
            var failed = 0;
            foreach (var (name, id) in packages)
            {
                if (said.TryGetValue(id, out var f) && f[1] == "done") { Recorded(ref rows, new PackageRow(name, Managers.Winget, id, Scope: "machine")); o.WriteLine($"  ✓ {name}: done"); }
                else { failed++; o.WriteLine($"  ✗ {name}: {(f is { Length: > 2 } ? f[2] : "didn't install")}"); }
            }
            return failed;
        }
        finally { File.Delete(status); }
    }

    // Chocolatey installs, removes and upgrades for the whole PC: all of a command's packages in
    // one elevated choco.exe (Chocolatey's own copy in ProgramData, which only admins can change),
    // then its own list says which made it.
    int Chocolatey(string verb, List<(string Name, string Id)> packages, ref List<PackageRow> rows)
    {
        if (packages.Count == 0) return 0;
        var what = verb switch { "install" => "install", "uninstall" => "remove", _ => "update" };
        if (!MayAskForAdmin($"Chocolatey will {what} {string.Join(", ", packages.Select(p => p.Name))} for the whole PC"))
            return packages.Count;
        string[] args = [verb, .. packages.Select(p => p.Id), "-y", "--no-progress"];
        if (host.Elevated(Managers.Choco, args) == null) { o.WriteLine("  no admin: nothing changed"); return packages.Count; }
        var (_, output) = host.Capture(Managers.Choco, CommandLines.List(Managers.Choco, ""));
        var now = Parse.ChocoLimited(output).ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);
        var failed = 0;
        foreach (var (name, id) in packages)
        {
            var there = now.TryGetValue(id, out var version);
            if (verb == "uninstall" ? !there : there)
            {
                if (verb == "uninstall") Forget(ref rows, Managers.Choco, id);
                else Recorded(ref rows, new PackageRow(name, Managers.Choco, id, version ?? "", Scope: "machine"));
                o.WriteLine($"  ✓ {name}: done{(verb == "upgrade" ? $" ({version})" : "")}");
            }
            else { failed++; o.WriteLine($"  ✗ {name}: Chocolatey didn't {what} it (its window said why)"); }
        }
        return failed;
    }

    // Admin only when you asked for something that needs it, said first, never under
    // OMAWIN_NO_ADMIN (AGENTS.md, roadmap 2.9).
    bool MayAskForAdmin(string what)
    {
        if (host.NoAdmin)
        {
            o.WriteLine($"  needs admin, and OMAWIN_NO_ADMIN is set: not done ({what})");
            return false;
        }
        o.WriteLine($"\n  one admin prompt: {what}");
        return true;
    }

    // --- remove -----------------------------------------------------------------------------

    int Remove(PkgArgs a)
    {
        if (a.Linux) return Linux("remove", a.Names);
        var rows = ReadRecord();
        var failed = 0;
        var choco = new List<(string, string)>();
        foreach (var typed in a.Names)
        {
            var have = PackageRecord.Find(rows, typed, Names);
            if (a.From != null) have = have.Where(r => r.Source == a.From).ToList();
            if (have.Count == 0) { failed++; o.WriteLine($"  {typed}: not installed as far as omawin knows (omawin pkg list --refresh)"); continue; }
            foreach (var row in have)
            {
                switch (row.Source)
                {
                    case Managers.Winget or Managers.Scoop or Managers.Mise:
                    {
                        o.WriteLine($"\n  removing {row.Name} with {Managers.Label(row.Source)} ({row.Id})");
                        var code = host.Live(row.Source, CommandLines.Remove(row.Source, row.Id));
                        var (ok, text) = row.Source == Managers.Winget ? CommandLines.Winget(code) : (code == 0, $"exit {code}");
                        if (ok || CommandLines.Gone(row.Source, code)) { Forget(ref rows, row.Source, row.Id); o.WriteLine($"  ✓ {row.Name} removed"); }
                        else { failed++; o.WriteLine($"  ✗ {row.Name}: {text}"); }
                        break;
                    }
                    case Managers.Choco: choco.Add((row.Name, row.Id)); break;
                    case Managers.Bundle:
                        failed++;
                        o.WriteLine($"  {row.Name} is part of omawin's core (the tools bundle): omawin pkg move {row.Name} --to winget puts your own copy first");
                        break;
                    default:
                        failed++;
                        o.WriteLine($"  {row.Name} came from {row.Source}, which omawin pkg can't remove: its own uninstaller (Windows' Installed apps)");
                        break;
                }
            }
        }
        failed += Chocolatey("uninstall", choco, ref rows);
        return failed == 0 ? 0 : 1;
    }

    // --- update -----------------------------------------------------------------------------

    // Each package through the manager it came from. Without names: everything omawin put in
    // (what's yours from elsewhere is left alone; --all updates that too). Chocolatey's need
    // admin, so they're only done when you name them or say --from choco: an update never prompts
    // on its own (AGENTS.md).
    int Update(PkgArgs a)
    {
        if (a.Linux) return Linux("update", a.Names);
        var rows = ReadRecord();
        List<PackageRow> picked;
        if (a.Names.Count > 0)
        {
            picked = [];
            foreach (var typed in a.Names)
            {
                var have = PackageRecord.Find(rows, typed, Names);
                if (have.Count == 0) return Fail($"omawin doesn't know where {typed} came from (omawin pkg list --refresh)");
                picked.AddRange(have);
            }
        }
        else if (a.All)
        {
            // Everything every manager has, by the manager's own update-all.
            var failedAll = 0;
            foreach (var m in Managers.Installable.Where(m => a.From == null || m == a.From))
            {
                if (!host.Has(m)) continue;
                if (m == Managers.Choco)
                {
                    if (a.From != Managers.Choco) { o.WriteLine("\n  Chocolatey: needs admin, so not without asking: omawin pkg update --all --from choco"); continue; }
                    if (!MayAskForAdmin("Chocolatey will update everything it has")) { failedAll++; continue; }
                    if (host.Elevated(Managers.Choco, CommandLines.Update(m, null)) is not 0) failedAll++;
                    continue;
                }
                o.WriteLine($"\n  {Managers.Label(m)}: everything");
                var code = host.Live(m, CommandLines.Update(m, null));
                if (!(m == Managers.Winget ? CommandLines.Winget(code).Ok : code == 0)) failedAll++;
            }
            failedAll += UpdateRows(rows.Where(r => r.Source is Managers.Bundle or "script").ToList(), ref rows, a.From);
            return failedAll == 0 ? 0 : 1;
        }
        else picked = rows.Where(r => !r.Yours).ToList();
        if (a.From != null) picked = picked.Where(r => r.Source == a.From).ToList();
        if (picked.Count == 0) { o.WriteLine("  nothing to update"); return 0; }
        var failed = UpdateRows(picked, ref rows, a.Names.Count > 0 ? Managers.Choco : a.From);
        return failed == 0 ? 0 : 1;
    }

    // allowAdmin: the one manager whose admin updates may prompt this time (choco, when asked).
    int UpdateRows(List<PackageRow> picked, ref List<PackageRow> rows, string? allowAdmin)
    {
        var failed = 0;
        var choco = new List<(string, string)>();
        foreach (var group in picked.GroupBy(r => r.Source))
        {
            switch (group.Key)
            {
                case Managers.Winget or Managers.Scoop or Managers.Mise or "npm":
                    foreach (var row in group)
                    {
                        if (!Choose.SafeId(row.Id)) { failed++; o.WriteLine($"  ✗ {row.Name}: \"{row.Id}\" isn't a package id"); continue; }
                        if (!host.Has(row.Source)) { failed++; o.WriteLine($"  ✗ {row.Name}: {Managers.Label(row.Source)} isn't on this PC any more"); continue; }
                        o.WriteLine($"\n  {Managers.Label(row.Source)}: {row.Name}");
                        var code = host.Live(row.Source, CommandLines.Update(row.Source, row.Id));
                        var (ok, text) = row.Source == Managers.Winget ? CommandLines.Winget(code) : (code == 0, $"exit {code}");
                        if (!ok) { failed++; o.WriteLine($"  ✗ {row.Name}: {text}"); }
                    }
                    break;
                case Managers.Choco:
                    if (allowAdmin == Managers.Choco) choco.AddRange(group.Select(r => (r.Name, r.Id)));
                    else o.WriteLine($"\n  Chocolatey: {string.Join(", ", group.Select(r => r.Name))} need admin, so not without asking: omawin pkg update --from choco");
                    break;
                case Managers.Bundle:
                    // The bundle's tools are omawin's install's business (install/software.ps1),
                    // still PowerShell: run there, as bin/pkg-update.ps1 did.
                    failed += Software("tools bundle (the newest release)", $"Install-ToolBundle (Get-WingetInstalled) -Only @({Quoted(group.Select(r => r.Id))})", group);
                    break;
                case "script":
                    foreach (var row in group)
                        failed += Software($"{row.Id} (its installer)", $"if ($ExtraInstalls.Contains('{row.Id}')) {{ & $ExtraInstalls['{row.Id}'] }} else {{ throw 'no install script for {row.Id} in install/packages.ps1' }}", [row]);
                    break;
                case "found":
                    foreach (var row in group) o.WriteLine($"  {row.Name}: yours, from elsewhere; left alone");
                    break;
                default:
                    foreach (var row in group) o.WriteLine($"  {row.Name}: unknown source \"{group.Key}\"");
                    break;
            }
        }
        failed += Chocolatey("upgrade", choco, ref rows);
        return failed;
    }

    // A call into the install's PowerShell (install/software.ps1's functions), for the sources
    // only it knows how to put in: the tools bundle and the tools with their own installers.
    int Software(string what, string call, IEnumerable<PackageRow> rows)
    {
        if (rows.Any(r => !Choose.SafeId(r.Id))) { o.WriteLine($"  ✗ {what}: not a package id"); return 1; }
        o.WriteLine($"\n  {what}");
        var repo = host.Repo.Replace("'", "''");
        var script = "$ErrorActionPreference = 'Stop'; $Repo = '" + repo + "'; $Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'; " +
                     ". \"$Repo\\install\\packages.ps1\"; . \"$Repo\\install\\lib.ps1\"; $SoftwareFunctionsOnly = $true; . \"$Repo\\install\\software.ps1\"; " +
                     call + "; Update-SessionPath";
        var code = host.Live("pwsh", ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script]);
        if (code != 0) { o.WriteLine($"  ✗ {what}: exit {code}"); return 1; }
        return 0;
    }

    static string Quoted(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => "'" + i + "'"));

    // --- list -------------------------------------------------------------------------------

    int List(PkgArgs a)
    {
        if (a.Linux) return Linux("list", []);
        var rows = ReadRecord();
        if (a.Refresh || !File.Exists(RecordFile))
        {
            var listed = new Dictionary<string, IReadOnlyList<Installed>>();
            foreach (var m in Managers.Installable.Where(host.Has))
                if (Ask(m) is { } now) listed[m] = now;
            rows = PackageRecord.Merge(rows, listed, Names);
            WriteRecord(rows);
            WriteInstalled(rows);
        }
        var waiting = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (a.Updates)
            foreach (var m in Managers.Installable.Where(host.Has))
            {
                var (_, output) = host.Capture(m, CommandLines.Outdated(m));
                foreach (var p in CommandLines.ParseOutdated(m, output)) waiting[m + ":" + p.Id] = p.Version;
            }
        if (a.From != null) rows = rows.Where(r => r.Source == a.From).ToList();
        if (rows.Count == 0) { o.WriteLine("  nothing recorded yet: omawin pkg list --refresh asks each manager"); return 0; }
        var table = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(r => new[]
        {
            r.Name, Managers.Label(r.Source) + (r.Yours ? " (yours)" : ""), r.Version, r.Id == r.Name ? "" : r.Id,
            waiting.TryGetValue(r.Key, out var v) && v.Length > 0 ? "→ " + v : "",
        }).ToList();
        Print(["name", "from", "version", "id", a.Updates ? "update" : ""], table);
        o.WriteLine($"\n  {rows.Count} packages{(a.Updates ? $", {table.Count(t => t[4].Length > 0)} with an update (omawin pkg update)" : "")}. {RecordFile}");
        return 0;
    }

    // What a manager says it has, or null when it couldn't say (its rows are then left as they are).
    List<Installed>? Ask(string manager)
    {
        o.WriteLine($"  asking {Managers.Label(manager)} ...");
        if (manager == Managers.Winget)
        {
            var file = host.TempFile("winget-export.json");
            try
            {
                host.Capture(manager, CommandLines.List(manager, file));
                return File.Exists(file) ? Parse.WingetExport(File.ReadAllText(file)) : null;
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }
        var (code, output) = host.Capture(manager, CommandLines.List(manager, ""));
        if (code != 0) return null;
        return manager switch
        {
            Managers.Scoop => Parse.ScoopExport(output),
            Managers.Choco => Parse.ChocoLimited(output),
            _ => Parse.MiseList(output),
        };
    }

    // --- info -------------------------------------------------------------------------------

    // Which managers have it, at what versions, which copy is installed, and which runs (PATH order).
    int Info(string typed)
    {
        var known = Names[typed];
        var rows = PackageRecord.Find(ReadRecord(), typed, Names);
        o.WriteLine($"  {known?.Name ?? typed}{(known?.BundleName != null ? "  (in omawin's tools bundle)" : "")}");
        var table = new List<string[]>();
        foreach (var m in Managers.Installable)
        {
            var id = known?.IdIn(m) ?? (known == null ? typed : null);
            var here = rows.FirstOrDefault(r => r.Source == m);
            if (id == null && here == null) continue;
            var latest = "";
            if (id != null && host.Has(m) && Choose.SafeId(id))
            {
                var (_, output) = host.Capture(m, CommandLines.Search(m, id));
                latest = Choose.Exact(CommandLines.ParseSearch(m, output, id), id) is { } hit ? (hit.Version.Length > 0 ? hit.Version : "has it") : "-";
            }
            table.Add([Managers.Label(m), id ?? "-", host.Has(m) ? latest : "(not on this PC)", here != null ? $"installed {here.Version}".Trim() : ""]);
        }
        foreach (var r in rows.Where(r => !Managers.IsInstallable(r.Source)))
            table.Add([Managers.Label(r.Source), r.Id, "", $"installed {r.Version}".Trim()]);
        Print(["manager", "id", "has", "here"], table);
        var command = known?.Command ?? typed;
        var copies = host.OnPath(command);
        if (copies.Count > 0)
        {
            o.WriteLine($"\n  runs: {copies[0]}");
            foreach (var c in copies.Skip(1)) o.WriteLine($"  shadowed: {c}");
        }
        else if (known?.Command != null) o.WriteLine($"\n  {command} isn't on the PATH");
        return 0;
    }

    // --- move, prefer -----------------------------------------------------------------------

    // Installs it with another manager, then takes the old copies away (the tools bundle's stays:
    // it's omawin's, and your manager's copy comes first on the PATH), and pins the choice.
    int Move(string typed, string to)
    {
        if (!Managers.IsInstallable(to)) return Fail($"no package manager \"{to}\" ({string.Join(", ", Managers.Installable)})");
        var before = PackageRecord.Find(ReadRecord(), typed, Names);
        var name = before.FirstOrDefault()?.Name ?? Names[typed]?.Name ?? typed;
        if (before.Any(r => r.Source == to)) o.WriteLine($"  {name} is already from {Managers.Label(to)}");
        else
        {
            var install = new PkgArgs("install", [Names[typed]?.Name ?? typed], From: to);
            if (Install(install) != 0) return Fail($"{name} didn't install with {Managers.Label(to)}; nothing else changed");
        }
        var rows = ReadRecord();
        var failed = 0;
        var choco = new List<(string, string)>();
        foreach (var old in before.Where(r => r.Source != to))
        {
            if (old.Source == Managers.Bundle) { Forget(ref rows, old.Source, old.Id); o.WriteLine($"  the tools bundle's {old.Name} stays in omawin's folder; {Managers.Label(to)}'s comes first on the PATH"); continue; }
            if (old.Source == Managers.Choco) { choco.Add((old.Name, old.Id)); continue; }
            if (!Managers.IsInstallable(old.Source)) { o.WriteLine($"  the {old.Source} copy stays (omawin pkg can't remove it)"); continue; }
            var code = host.Live(old.Source, CommandLines.Remove(old.Source, old.Id));
            var ok = old.Source == Managers.Winget ? CommandLines.Winget(code).Ok || CommandLines.Gone(old.Source, code) : code == 0;
            if (ok) { Forget(ref rows, old.Source, old.Id); o.WriteLine($"  ✓ the {Managers.Label(old.Source)} copy removed"); }
            else { failed++; o.WriteLine($"  the {Managers.Label(old.Source)} copy stays: exit {code}"); }
        }
        failed += Chocolatey("uninstall", choco, ref rows);
        // The new copy was recorded as "manager:id" while the old one held the name; with the
        // old one gone, it takes the name.
        if (!rows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) &&
            PackageRecord.Find(rows, name, Names).FirstOrDefault(r => r.Source == to) is { } moved)
        {
            rows = rows.Select(r => r == moved ? r with { Name = name } : r).ToList();
            WriteRecord(rows);
        }
        WriteLines(PinsFile, Choose.SetPin(ReadLines(PinsFile), name, to));
        o.WriteLine($"  {name} now comes from {Managers.Label(to)} (pinned: omawin pkg install and update use it)");
        return failed == 0 ? 0 : 1;
    }

    int Prefer(List<string> order)
    {
        if (order.Count == 0)
        {
            var now = Choose.Prefer(ReadLines(PreferFile));
            o.WriteLine("  " + string.Join(" ", now.Count > 0 ? now : Managers.Installable.ToList()) + (now.Count == 0 ? "  (omawin's default)" : ""));
            return 0;
        }
        var bad = order.Where(m => !Managers.IsInstallable(m.ToLowerInvariant())).ToList();
        if (bad.Count > 0) return Fail($"not a manager to prefer: {string.Join(", ", bad)} ({string.Join(", ", Managers.Installable)})");
        var list = Choose.Prefer(order);
        WriteLines(PreferFile, list);
        o.WriteLine($"  omawin pkg installs with {string.Join(", then ", list.Select(Managers.Label))} (a name's own pick or your pin comes first)");
        if (list.Contains(Managers.Choco)) o.WriteLine("  Chocolatey installs for the whole PC: each command that uses it asks for admin once");
        return 0;
    }

    // --- the Linux in WSL ---------------------------------------------------------------------

    int Linux(string verb, List<string> typed)
    {
        var distro = LinuxDistro();
        if (distro == null) return Fail("no Linux picked (omawin default linux arch)");
        if (verb is "install" or "remove" && typed.Count == 0) return Fail($"omawin pkg {verb} <name...> --linux");
        // names.txt's Arch name where it has one (fd is fd-find in Ubuntu: apt's column).
        var apt = !distro.Contains("arch", StringComparison.OrdinalIgnoreCase);
        var packages = typed.Select(t => Names[t]?.Ids.GetValueOrDefault(apt ? "apt" : "arch") ?? t).ToList();
        if (packages.FirstOrDefault(p => !Choose.SafeId(p)) is { } badName) return Fail($"\"{badName}\" isn't a package name");
        return host.Live("wsl", ["-d", distro, .. CommandLines.Linux(verb, packages)]) == 0 ? 0 : 1;
    }

    // The Linux omawin opens (omawin default linux): its WSL name, from bin/defaults.txt.
    string? LinuxDistro()
    {
        var rows = ReadLines(Path.Combine(host.Repo, "bin", "defaults.txt")).Select(l => l.Split('|').Select(x => x.Trim()).ToArray())
            .Where(f => f.Length >= 5 && f[0] == "linux").ToList();
        var picked = ReadLines(Path.Combine(host.State, "defaults", "linux")).FirstOrDefault()?.Trim();
        return (rows.FirstOrDefault(f => f[1] == picked) ?? rows.FirstOrDefault())?[4];
    }

    // --- the record -------------------------------------------------------------------------

    List<PackageRow> ReadRecord() => PackageRecord.Parse(ReadLines(RecordFile));

    void WriteRecord(IEnumerable<PackageRow> rows) => WriteLines(RecordFile, PackageRecord.Format(rows));

    // A row for this source and id, replacing any it had; written at once, so a failure later in
    // the command loses nothing; installed.txt's mark too.
    void Recorded(ref List<PackageRow> rows, PackageRow row)
    {
        rows = rows.Where(r => !(r.Source == row.Source && string.Equals(r.Id, row.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        // A second copy of one thing (python from winget and from mise) gets its own name: names
        // are the record's key (install/lib.ps1 keeps one row per name). The name still finds both.
        if (rows.Any(r => string.Equals(r.Name, row.Name, StringComparison.OrdinalIgnoreCase))) row = row with { Name = row.Key };
        rows.Add(row);
        WriteRecord(rows);
        if (PackageRecord.InstalledKey(row.Source, row.Id) is { } key && !ReadLines(InstalledFile).Any(l => l.Trim() == key))
            WriteLines(InstalledFile, ReadLines(InstalledFile).Append(key));
    }

    void Forget(ref List<PackageRow> rows, string source, string id)
    {
        rows = rows.Where(r => !(r.Source == source && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        WriteRecord(rows);
        if (PackageRecord.InstalledKey(source, id) is { } key) WriteLines(InstalledFile, ReadLines(InstalledFile).Where(l => l.Trim() != key));
    }

    // installed.txt from the record, after a refresh: the Packages page's marks match what pkg list says.
    void WriteInstalled(IEnumerable<PackageRow> rows) =>
        WriteLines(InstalledFile, rows.Select(r => PackageRecord.InstalledKey(r.Source, r.Id)).OfType<string>().Distinct());

    static IEnumerable<string> ReadLines(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : []; }
        catch (IOException) { return []; }
    }

    // Written whole to a file beside it, then swapped in: a reader never sees half a file.
    static void WriteLines(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines.ToList(), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    // --- output -----------------------------------------------------------------------------

    static string Where(PackageRow r) => Managers.Label(r.Source) + (r.Version.Length > 0 ? " " + r.Version : "");

    void Print(string[] header, List<string[]> rows)
    {
        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        string Line(string[] cells) => "  " + string.Join("  ", cells.Select((c, i) => i == cells.Length - 1 ? c : c.PadRight(widths[i]))).TrimEnd();
        o.WriteLine(Line(header));
        foreach (var r in rows) o.WriteLine(Line(r));
    }

    int Fail(string message)
    {
        o.WriteLine("omawin pkg: " + message);
        return 1;
    }
}
