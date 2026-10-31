using Omawin.Cli.Profile;

namespace Omawin.Cli.Tests;

// `omawin profile linux`: the order that makes each start cold or warm, what it reads from Linux,
// and the report's numbers. The machine is a stand-in that records what wsl.exe was asked.
public class LinuxProfileTests
{
    sealed class FakeMachine(string? answer = "y") : LinuxMachine
    {
        public readonly List<string> Calls = [];
        public string Free = "               total        used        free      shared  buff/cache   available\nMem:           15890         612       14801           3         476       15278\nSwap:           4096           0        4096\n";
        public override (int Code, string Output) Wsl(IReadOnlyList<string> args, int timeoutMs = 120_000)
        {
            var line = string.Join(' ', args);
            Calls.Add(line);
            return line switch
            {
                "--list --quiet" => (0, "Ubuntu\r\narchlinux\r\n"),
                _ when line.EndsWith("free -m") => (0, Free),
                _ when line.EndsWith("systemd-analyze time") => (0, "Startup finished in 1.204s (userspace)\ngraphical.target reached after 1.180s in userspace.\n"),
                _ when line.EndsWith("blame --no-pager") => (0, "  402ms systemd-resolved.service\n  130ms docker.socket\n"),
                _ => (0, ""),
            };
        }
        public override long? VmWorkingSet() => null;
        public override void Sleep(int ms) { }
        public override string? Ask(string prompt) => answer;
    }

    static string Repo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omawin-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        File.WriteAllText(Path.Combine(dir, "bin", "defaults.txt"), "linux | arch | Arch Linux | wsl/install.ps1 | archlinux\nlinux | ubuntu | Ubuntu | wsl/install.ps1 | Ubuntu\n");
        return dir;
    }

    [Fact]
    public void EachRunStopsTheVmThenTheDistroBeforeTimingThem()
    {
        var m = new FakeMachine();
        LinuxProfile.Measure(m, "archlinux", 2, 0, TextWriter.Null);
        var run = m.Calls.Take(6).ToList();
        Assert.Equal(["--shutdown", "-d archlinux --exec /bin/true", "--terminate archlinux", "-d archlinux --exec /bin/true",
            "-d archlinux --exec /bin/true", "-d archlinux --cd ~ --exec bash -ic exit"], run);
        Assert.Equal(2, m.Calls.Count(c => c == "--shutdown"));
    }

    [Fact]
    public void ReadsLinuxsMemoryAndBoot()
    {
        var r = LinuxProfile.Measure(new FakeMachine(), "archlinux", 1, 0, TextWriter.Null);
        Assert.Equal(612, r.UsedMb);
        Assert.Equal(476, r.CacheMb);
        Assert.Equal("1.204s (userspace)", r.Boot);
        Assert.Equal(["402ms systemd-resolved.service", "130ms docker.socket"], r.Blame);
        Assert.Equal((null, null), LinuxProfile.ParseFree("no such thing"));
    }

    [Fact]
    public void TheReportGivesTheTablesRowsAndABudget()
    {
        var r = new LinuxProfileResult("archlinux", [8100, 7900, 8300], [2100, 2000, 2050], [40, 45, 41], [120, double.NaN, 110], 600, 400, null, null, []);
        var text = LinuxProfile.Report(r);
        Assert.Contains("| keeper off, after `wsl --shutdown` | 0 MB | 8100 ms |", text);
        // A failed run counts for nothing in the median; without the VM's own figure, Linux's
        // used + cache is what the warm side costs.
        Assert.Contains("| keeper on, systemd + Docker socket idle | 1000 MB | 110 ms (bash ready) |", text);
        Assert.Contains("(idle + 20%): 1200 MB", text);
        Assert.Contains("Windows didn't let this user read it", text);
    }

    [Fact]
    public void AsksBeforeStoppingWslAndRefusesAnUnknownLinux()
    {
        var repo = Repo();
        var state = Path.Combine(repo, "state");
        var no = new FakeMachine("n");
        Assert.Equal(1, LinuxProfile.Command(no, [], repo, state, TextWriter.Null));
        Assert.DoesNotContain("--shutdown", no.Calls);
        var nobody = new FakeMachine(null);
        Assert.Equal(1, LinuxProfile.Command(nobody, [], repo, state, TextWriter.Null));
        Assert.DoesNotContain("--shutdown", nobody.Calls);
        Assert.Equal(0, LinuxProfile.Command(new FakeMachine(null), ["-Yes", "-Runs", "1", "-Settle", "0"], repo, state, TextWriter.Null));

        Directory.CreateDirectory(Path.Combine(state, "defaults"));
        File.WriteAllText(Path.Combine(state, "defaults", "linux"), "ubuntu");
        Assert.Equal("Ubuntu", LinuxProfile.Distro(repo, state));
        File.WriteAllText(Path.Combine(repo, "bin", "defaults.txt"), "linux | debian | Debian | x | Debian\n");
        Assert.Equal(1, LinuxProfile.Command(new FakeMachine(), ["-Yes"], repo, state, TextWriter.Null));
        Directory.Delete(repo, true);
    }
}
