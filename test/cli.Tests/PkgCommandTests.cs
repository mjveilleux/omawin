using Omawin.Cli.Pkg;

namespace Omawin.Cli.Tests;

// omawin pkg's commands end to end against a PC that only says what it was asked to run: what
// gets run, what's recorded, and when admin is asked for.
public sealed class PkgCommandTests : IDisposable
{
    sealed class FakeHost(string repo, string state) : Host
    {
        public override string Repo => repo;
        public override string State => state;
        public override TextWriter Out { get; } = new StringWriter();
        public override bool NoAdmin => noAdmin;
        public bool noAdmin;
        public HashSet<string> Present = ["winget", "scoop", "choco", "mise"];
        public List<string> Ran = [];
        public List<string> Elevations = [];
        public Dictionary<string, int> Codes = [];                  // "winget install" -> exit code
        public Dictionary<string, string> Outputs = [];             // "choco list" -> output
        public int? ElevatedCode = 0;

        public override bool Has(string program) => Present.Contains(program);
        public override (int Code, string Output) Capture(string program, IReadOnlyList<string> args)
        {
            Ran.Add(program + " " + string.Join(' ', args));
            return (0, Outputs.GetValueOrDefault(program + " " + args[0], ""));
        }
        public override int Live(string program, IReadOnlyList<string> args)
        {
            Ran.Add(program + " " + string.Join(' ', args));
            return Codes.GetValueOrDefault(program + " " + args[0], 0);
        }
        public override int? Elevated(string program, IReadOnlyList<string> args)
        {
            Elevations.Add(program + " " + string.Join(' ', args));
            return ElevatedCode;
        }
        public override IReadOnlyList<string> OnPath(string command) => [];
        public override string? Ask(string prompt) => null;
        public override string TempFile(string name) => Path.Combine(state, name);
    }

    readonly string root = Path.Combine(Path.GetTempPath(), "omawin-pkg-test-" + Guid.NewGuid().ToString("N"));
    readonly FakeHost host;
    string Record => Path.Combine(root, "state", "packages.tsv");

    public PkgCommandTests()
    {
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "default", "packages"));
        Directory.CreateDirectory(Path.Combine(repo, "install"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        File.WriteAllLines(Path.Combine(repo, "default", "packages", "names.txt"),
        [
            "python | Python.Python.3.13 | python | python | python | - | - | python | mise",
            "fd | sharkdp.fd | fd | fd | fd | fd | fd-find | fd | -",
            "jq | jqlang.jq | jq | jq | jq | jq | jq | jq | -",
        ]);
        File.WriteAllLines(Path.Combine(repo, "install", "tools.txt"), ["fd | github:sharkdp/fd | x | . | sharkdp.fd"]);
        host = new FakeHost(repo, Path.Combine(root, "state"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    int Run(params string[] args) => new PkgCommand(host).Run(args);
    List<PackageRow> Rows() => PackageRecord.Parse(File.Exists(Record) ? File.ReadAllLines(Record) : []);

    [Fact]
    public void InstallRunsTheChosenManagerAndRecordsIt()
    {
        Assert.Equal(0, Run("install", "python", "jq"));
        Assert.Contains(host.Ran, r => r.StartsWith("mise use --global python@latest"));
        Assert.Contains(host.Ran, r => r.StartsWith("winget install --id jqlang.jq --exact --scope user"));
        Assert.Equal(["jq winget jqlang.jq user", "python mise python "], Rows().Select(r => $"{r.Name} {r.Source} {r.Id} {r.Scope}"));
        Assert.Equal(["winget:jqlang.jq"], File.ReadAllLines(Path.Combine(root, "state", "installed.txt")));
    }

    [Fact]
    public void AFailedInstallIsntRecordedAndFailsTheCommand()
    {
        host.Codes["winget install"] = unchecked((int)0x8A150101);
        Assert.Equal(1, Run("install", "jq"));
        Assert.Empty(Rows());
        Assert.Contains("close it", host.Out.ToString());
    }

    [Fact]
    public void WhatsAlreadyHereIsntInstalledAgain()
    {
        File.WriteAllLines(Record, ["fd\tbundle\tfd\t10.2.0\tC:\\tools\\fd"]);
        Assert.Equal(0, Run("install", "fd"));
        Assert.DoesNotContain(host.Ran, r => r.Contains("install"));
        Assert.Contains("already here", host.Out.ToString());
    }

    [Fact]
    public void ChocolateyPackagesShareOnePrompt()
    {
        host.Outputs["choco list"] = "jq|1.7.1\nfd|10.2.0\n";
        Assert.Equal(0, Run("install", "jq", "fd", "--from", "choco"));
        Assert.Equal(["choco install jq fd -y --no-progress"], host.Elevations);
        Assert.Equal(["fd choco 10.2.0 machine", "jq choco 1.7.1 machine"], Rows().Select(r => $"{r.Name} {r.Source} {r.Version} {r.Scope}"));
    }

    [Fact]
    public void NoAdminMeansNoPrompt()
    {
        host.noAdmin = true;
        Assert.Equal(1, Run("install", "jq", "--machine"));
        Assert.Empty(host.Elevations);
        Assert.Empty(Rows());
    }

    [Fact]
    public void MachineWideWingetGoesThroughTheAdminBatch()
    {
        // The batch reports through its status file; this one ran and said nothing about jq.
        Assert.Equal(1, Run("install", "jq", "--machine"));
        Assert.Single(host.Elevations);
        Assert.StartsWith("admin-batch -Status ", host.Elevations[0]);
        Assert.EndsWith("-Packages jqlang.jq", host.Elevations[0]);
        Assert.Empty(Rows());
        Assert.Contains("didn't install", host.Out.ToString());
    }

    [Fact]
    public void RemoveGoesBackToTheManagerThatInstalledIt()
    {
        File.WriteAllLines(Record, ["Python.Python.3.13\twinget\tPython.Python.3.13\t3.13.0", "fd\tbundle\tfd\t10.2.0"]);
        Assert.Equal(0, Run("remove", "python"));
        Assert.Contains(host.Ran, r => r.StartsWith("winget uninstall --id Python.Python.3.13"));
        Assert.Equal(["fd"], Rows().Select(r => r.Name));
        // The bundle's copy is omawin's core: not removed, said how to replace it.
        Assert.Equal(1, Run("remove", "fd"));
        Assert.Contains("pkg move fd", host.Out.ToString());
    }

    [Fact]
    public void AnUninstallOfSomethingAlreadyGoneStillClearsTheRecord()
    {
        File.WriteAllLines(Record, ["jq\twinget\tjqlang.jq"]);
        host.Codes["winget uninstall"] = unchecked((int)0x8A150014);
        Assert.Equal(0, Run("remove", "jq"));
        Assert.Empty(Rows());
    }

    [Fact]
    public void UpdateLeavesYoursAloneAndChocolateyForWhenYouAsk()
    {
        File.WriteAllLines(Record,
        [
            "jq\twinget\tjqlang.jq",
            "Zoom.Zoom\twinget\tZoom.Zoom\t6.0\t\t\tfound",
            "go\tmise\tgo",
            "7zip\tchoco\t7zip",
        ]);
        Assert.Equal(0, Run("update"));
        Assert.Contains(host.Ran, r => r.StartsWith("winget upgrade --id jqlang.jq"));
        Assert.Contains(host.Ran, r => r == "mise upgrade go");
        Assert.DoesNotContain(host.Ran, r => r.Contains("Zoom"));
        Assert.Empty(host.Elevations);
        Assert.Contains("pkg update --from choco", host.Out.ToString());

        host.Outputs["choco list"] = "7zip|24.09\n";
        Assert.Equal(0, Run("update", "--from", "choco"));
        Assert.Equal(["choco upgrade 7zip -y --no-progress"], host.Elevations);
    }

    [Fact]
    public void MoveInstallsThereRemovesTheOldAndPins()
    {
        File.WriteAllLines(Record, ["jq\twinget\tjqlang.jq\t1.7.1"]);
        Assert.Equal(0, Run("move", "jq", "--to", "scoop"));
        Assert.Contains(host.Ran, r => r == "scoop install jq");
        Assert.Contains(host.Ran, r => r.StartsWith("winget uninstall --id jqlang.jq"));
        Assert.Equal(["jq scoop"], Rows().Select(r => $"{r.Name} {r.Source}"));
        Assert.Equal(["jq\tscoop"], File.ReadAllLines(Path.Combine(root, "state", "pkg-pins.tsv")));
        // The pin decides the next install of it.
        File.Delete(Record);
        host.Ran.Clear();
        Run("install", "jq");
        Assert.Contains(host.Ran, r => r == "scoop install jq");
    }

    [Fact]
    public void AFailedMoveChangesNothing()
    {
        File.WriteAllLines(Record, ["jq\twinget\tjqlang.jq\t1.7.1"]);
        host.Codes["scoop install"] = 1;
        Assert.Equal(1, Run("move", "jq", "--to", "scoop"));
        Assert.DoesNotContain(host.Ran, r => r.Contains("uninstall"));
        Assert.Equal(["jq winget"], Rows().Select(r => $"{r.Name} {r.Source}"));
        Assert.False(File.Exists(Path.Combine(root, "state", "pkg-pins.tsv")));
    }

    [Fact]
    public void PreferIsChecked()
    {
        Assert.Equal(1, Run("prefer", "bundle"));
        Assert.Equal(0, Run("prefer", "scoop", "winget"));
        Assert.Equal(["scoop", "winget"], File.ReadAllLines(Path.Combine(root, "state", "pkg-prefer.txt")));
        Run("install", "jq");
        Assert.Contains(host.Ran, r => r == "scoop install jq");
    }

    [Fact]
    public void ListRefreshAsksEveryManagerAndWritesTheRecord()
    {
        host.Outputs["scoop export"] = """{"apps":[{"Name":"fd","Version":"10.2.0"}]}""";
        host.Outputs["mise ls"] = """{"go":[{"version":"1.23.2","installed":true}]}""";
        host.Present.Remove("choco");
        Assert.Equal(0, Run("list", "--refresh"));
        Assert.Contains(host.Ran, r => r.StartsWith("winget export --output"));
        Assert.Equal(["fd scoop found", "go mise found"], Rows().Select(r => $"{r.Name} {r.Source} {r.By}"));
        Assert.Equal(["scoop:fd"], File.ReadAllLines(Path.Combine(root, "state", "installed.txt")));
    }

    [Fact]
    public void ACommandThatIsntOneSaysWhichAre()
    {
        Assert.Equal(1, Run("instal", "x"));
        Assert.Contains("install, remove", host.Out.ToString());
    }
}
