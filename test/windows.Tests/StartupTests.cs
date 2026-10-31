namespace Omawin.Windows.Tests;

public class StartupTests
{
    [Theory]
    [InlineData(null, true)]                 // no value: Task Manager never touched it
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 2, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 7, 0, 0, 0 }, false)]
    public void TheLowBitMeansOff(byte[]? value, bool on) => Assert.Equal(on, StartupItems.IsEnabled(value));

    [Fact]
    public void SwitchValuesAreTaskManagersTwelveBytes()
    {
        var when = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc);
        var off = StartupItems.Disabled(when);
        Assert.Equal(12, off.Length);
        Assert.Equal(3, off[0]);
        Assert.Equal(when.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.False(StartupItems.IsEnabled(off));
        Assert.Equal([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], StartupItems.Enabled());
        Assert.True(StartupItems.IsEnabled(StartupItems.Enabled()));
    }

    static readonly StartupItem[] Items =
    [
        new(StartupSource.UserRun, "Discord", @"""C:\Users\u\AppData\Local\Discord\Update.exe"" --processStart Discord.exe", true),
        new(StartupSource.UserRun, "OneDrive", @"""C:\Program Files\Microsoft OneDrive\OneDrive.exe"" /background", true),
        new(StartupSource.UserFolder, "Notes.lnk", @"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Notes.lnk", true),
        new(StartupSource.MachineRun, "OneDriveSetup", @"C:\Windows\SysWOW64\OneDriveSetup.exe /thfirstsetup", false),
    ];

    [Theory]
    [InlineData("discord", "Discord")]
    [InlineData("OneDrive", "OneDrive")]      // exact beats the longer one that starts with it
    [InlineData("Notes", "Notes.lnk")]        // a shortcut by its name
    [InlineData("setup", "OneDriveSetup")]    // contained
    public void MatchFindsOne(string typed, string name)
    {
        var (item, ambiguous) = StartupItems.Match(Items, typed);
        Assert.Equal(name, item?.Name);
        Assert.Empty(ambiguous);
    }

    [Fact]
    public void MatchSaysWhenSeveralFit()
    {
        var (item, ambiguous) = StartupItems.Match(Items, "o");
        Assert.Null(item);
        Assert.True(ambiguous.Count > 1);
        Assert.Empty(StartupItems.Match(Items, "zzz").Ambiguous);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\App\app.exe"" --flag", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe --flag", @"C:\Program Files\App\app.exe")]
    [InlineData(@"rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    [InlineData("cmd /c thing", "cmd")]
    public void ProgramIsTheExeOfACommandLine(string command, string program) => Assert.Equal(program, StartupItems.Program(command));

    [Fact]
    public void MachineEntriesNeedAdmin()
    {
        Assert.False(Items[0].ForEveryAccount);
        Assert.True(Items[3].ForEveryAccount);
        Assert.Equal("Run32", StartupItems.ApprovedKey(StartupSource.MachineRun32));
        Assert.Null(StartupItems.ApprovedKey(StartupSource.Service));
    }
}
