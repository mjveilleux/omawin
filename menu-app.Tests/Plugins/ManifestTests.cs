using SystemMenu.Plugins;

namespace SystemMenu.Tests.Plugins;

public sealed class ManifestTests : IDisposable
{
    // An Omarchy plugin's manifest as Omarchy writes it (no omawin block), with a comment and a
    // trailing comma (people hand-edit these) and a field omawin doesn't know.
    const string OmarchyOnly = """
        {
          // a Pomodoro timer, as in Omarchy's community plugins
          "schemaVersion": 1,
          "id": "b.timer",
          "name": "Timer",
          "version": "0.0.5",
          "kinds": ["service", "bar-widget"],
          "entryPoints": { "service": "Service.qml", "barWidget": "BarWidget.qml" },
          "barWidget": {
            "displayName": "Timer",
            "defaultSection": "right",
            "defaults": { "workMinutes": 25 },
            "schema": [ { "key": "workMinutes", "type": "integer", "min": 1, "max": 120, "defaultValue": 25 } ]
          },
          "futureField": { "kept": true },
        }
        """;

    // The same plugin with omawin's block: a Bun service and a XAML panel.
    const string WithOmawin = """
        {
          "schemaVersion": 1, "id": "b.timer", "name": "Timer", "version": "0.0.5",
          "kinds": ["service", "bar-widget"],
          "entryPoints": { "service": "Service.qml" },
          "omawin": {
            "command": ["bun", "omawin/service.ts"],
            "panel": "omawin/Panel.xaml",
            "widget": { "style": "ring" },
            "keys": [ { "binding": "lwin+shift+w", "action": "panel" } ]
          }
        }
        """;

    static readonly CheckContext Ctx = new(new Version(1, 0), FirstParty: false, AdminVerbs: new HashSet<string> { "dns-set" });
    readonly string dir;

    public ManifestTests()
    {
        // A plugin folder named after its id, with the files the manifest points at.
        dir = Path.Combine(Path.GetTempPath(), "omawin-tests-" + Guid.NewGuid().ToString("N"), "b.timer");
        Directory.CreateDirectory(Path.Combine(dir, "omawin"));
        File.WriteAllText(Path.Combine(dir, "omawin", "service.ts"), "");
        File.WriteAllText(Path.Combine(dir, "omawin", "Panel.xaml"), "<StackPanel/>");
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);

    static Manifest Read(string json) => Manifest.Parse(json, out var error) ?? throw new Xunit.Sdk.XunitException(error);

    static Manifest Edit(string json, Action<Manifest> change)
    {
        var m = Read(json);
        change(m);
        return m;
    }

    [Fact]
    public void ReadsOmarchysManifestWithCommentsAndTrailingCommas()
    {
        var m = Read(OmarchyOnly);
        Assert.Equal("b.timer", m.Id);
        Assert.True(m.Has("bar-widget"));
        Assert.Equal("Service.qml", m.EntryPoints!["service"]);
        Assert.Equal("integer", m.BarWidget!.Schema[0].Type);
        Assert.True(m.StartsAtStartup); // a service
    }

    [Fact]
    public void KeepsFieldsItDoesNotKnow()
    {
        var again = Read(Read(OmarchyOnly).ToJson());
        Assert.NotNull(again.Rest);
        Assert.True(again.Rest!.ContainsKey("futureField"));
        Assert.Equal("Service.qml", again.EntryPoints!["service"]);
    }

    [Fact]
    public void BadJsonIsAnErrorNotACrash()
    {
        Assert.Null(Manifest.Parse("{ not json", out var error));
        Assert.Contains("not valid JSON", error);
    }

    [Fact]
    public void AnOmarchyOnlyPluginIsFineButWarned()
    {
        var (errors, warnings) = ManifestCheck.Check(Read(OmarchyOnly), dir, Ctx);
        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("no omawin block"));
    }

    [Fact]
    public void AGoodOmawinPluginHasNoErrors()
    {
        var (errors, warnings) = ManifestCheck.Check(Read(WithOmawin), dir, Ctx);
        Assert.Empty(errors);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("../escape.ts", "isn't a file in the plugin")]
    [InlineData("/usr/bin/evil", "absolute path")]
    [InlineData("curl", "a file in the plugin, or bun")]
    public void CommandsMustBeTheirOwnFilesOrARunner(string first, string because)
    {
        var m = Edit(WithOmawin, m => m.Omawin!.Command = [first]);
        Assert.Contains(ManifestCheck.Check(m, dir, Ctx).Errors, e => e.Contains(because));
    }

    [Fact]
    public void BashNeedsTheLinuxSide()
    {
        var onWindows = Edit(WithOmawin, m => m.Omawin!.Command = ["bash", "omawin/service.ts"]);
        Assert.Contains(ManifestCheck.Check(onWindows, dir, Ctx).Errors, e => e.Contains("side"));
        var onLinux = Edit(WithOmawin, m => { m.Omawin!.Command = ["bash", "omawin/service.ts"]; m.Omawin.Side = "linux"; });
        Assert.Empty(ManifestCheck.Check(onLinux, dir, Ctx).Errors);
    }

    [Fact]
    public void AMissingPanelFileIsAnError()
    {
        var m = Edit(WithOmawin, m => m.Omawin!.Panel = "omawin/Missing.xaml");
        Assert.Contains(ManifestCheck.Check(m, dir, Ctx).Errors, e => e.Contains("Missing.xaml"));
    }

    [Fact]
    public void OnlyFirstPartyPluginsMayAskForAdminVerbs()
    {
        var m = Edit(WithOmawin, m => m.Omawin!.AdminVerbs = ["dns-set"]);
        Assert.Contains(ManifestCheck.Check(m, dir, Ctx).Errors, e => e.Contains("first-party"));
        Assert.Empty(ManifestCheck.Check(m, dir, Ctx with { FirstParty = true }).Errors);
        var unknown = Edit(WithOmawin, m => m.Omawin!.AdminVerbs = ["format-disk"]);
        Assert.Contains(ManifestCheck.Check(unknown, dir, Ctx with { FirstParty = true }).Errors, e => e.Contains("format-disk"));
    }

    [Fact]
    public void ANewerOmawinIsRequiredWhenAsked()
    {
        var m = Edit(WithOmawin, m => m.Omawin!.MinVersion = "2.0");
        Assert.Contains(ManifestCheck.Check(m, dir, Ctx).Errors, e => e.Contains("needs omawin 2.0"));
    }

    [Theory]
    [InlineData("lwin+shift+w", true)]
    [InlineData("alt+f4", true)]
    [InlineData("lwin+shift+", false)]
    [InlineData("hyper+w", false)]
    public void ProposedKeysUseGlazeWMsBindingShape(string binding, bool ok)
    {
        var m = Edit(WithOmawin, m => m.Omawin!.Keys = [new KeyProposal { Binding = binding, Action = "panel" }]);
        Assert.Equal(ok, !ManifestCheck.Check(m, dir, Ctx).Errors.Any(e => e.StartsWith("key ")));
    }

    [Theory]
    [InlineData("omawin/service.ts", true)]
    [InlineData(@"omawin\service.ts", true)]
    [InlineData("../other/x.ts", false)]
    [InlineData(@"..\..\x.ts", false)]
    [InlineData(@"C:\Windows\x.exe", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("", false)]
    public void InsideKeepsPathsInThePluginsFolder(string relative, bool inside) =>
        Assert.Equal(inside, ManifestCheck.Inside(dir, relative));
}
