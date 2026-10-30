using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SystemMenu.Plugins;

// A plugin's manifest.json: Omarchy's schema (shell/services/PluginRegistry.qml in Omarchy),
// unchanged, plus an `omawin` block for what runs on Windows (plans/plugins.md,
// plans/05-plugins.md §2.1). Omarchy fields omawin doesn't use (QML entry points) are kept,
// so one repo serves both desktops and a clone round-trips them.
//
// Pure: no WinUI, no Windows calls, so menu-app.Tests runs it anywhere.
public sealed class Manifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string? License { get; set; }
    public string? Description { get; set; }
    public List<string> Kinds { get; set; } = [];
    public string? Activation { get; set; }          // "startup" | "on-demand" (the default)
    public bool KeepLoaded { get; set; }
    public Dictionary<string, string>? EntryPoints { get; set; }
    public BarWidgetInfo? BarWidget { get; set; }
    public OmawinBlock? Omawin { get; set; }
    public JsonObject? Omarchy { get; set; }         // Omarchy's own extras (capabilities, clonedFrom): carried through
    [JsonExtensionData] public Dictionary<string, JsonElement>? Rest { get; set; }

    public bool Has(string kind) => Kinds.Contains(kind);

    // Headless services run from the start; everything else when first shown (plugins.md).
    public bool StartsAtStartup =>
        Activation == "startup" || Has("service") || (Has("launcher-provider") && Omawin?.Provider?.Always == true);

    // Reads a manifest; null with the reason when it isn't one.
    public static Manifest? Parse(string json, out string? error)
    {
        try
        {
            var m = JsonSerializer.Deserialize(json, PluginJson.Default.Manifest);
            error = m is null ? "empty manifest" : null;
            return m;
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, PluginJson.Default.Manifest);
}

public sealed class BarWidgetInfo
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool AllowMultiple { get; set; }
    public string DefaultSection { get; set; } = "right";
    public JsonObject? Defaults { get; set; }
    public List<SettingField> Schema { get; set; } = [];
}

// One setting in barWidget.schema: the settings page is drawn from these.
public sealed class SettingField
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";     // string, integer, number, boolean, enum, color, path, secret (ours)
    public string? Label { get; set; }
    public string? Description { get; set; }
    public JsonNode? DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public List<string>? Options { get; set; }
}

public sealed class OmawinBlock
{
    public string? MinVersion { get; set; }
    public List<string>? Command { get; set; }       // argv, never parsed by a shell
    public string Side { get; set; } = "windows";    // windows | linux (runs inside WSL)
    public int? Interval { get; set; }               // seconds: Waybar's exec + interval; null: long-running
    public string? Panel { get; set; }               // the widget's drop-down (loose XAML)
    public List<PanelInfo> Panels { get; set; } = []; // more: overlays, extra panels
    public List<PageInfo> Pages { get; set; } = [];   // `page` kind: launcher pages
    public string? Menu { get; set; }                // a menu.txt fragment
    public string? Items { get; set; }               // an items.txt fragment
    public string Commands { get; set; } = "commands"; // the script commands folder
    public WidgetInfo Widget { get; set; } = new();
    public ProviderInfo? Provider { get; set; }      // launcher-provider / finder-source
    public List<string> Permissions { get; set; } = [];
    public List<string> Events { get; set; } = [];   // "glazewm.focus_changed", "theme", "power", "audio"
    public List<KeyProposal> Keys { get; set; } = [];
    public List<string> AdminVerbs { get; set; } = []; // first-party plugins only
    public string? Replaces { get; set; }            // a built-in widget it stands in for ("clock")
    public int MemoryMb { get; set; } = 256;         // the Job Object's memory cap
}

public sealed class WidgetInfo
{
    public string Style { get; set; } = "text";      // text | meter | ring | slot (a QML host's window)
    public string? Click { get; set; }               // "panel" | "action <name>" | "run <argv…>"
    public string? RightClick { get; set; }
    public string? MiddleClick { get; set; }
    public Dictionary<string, string> Classes { get; set; } = []; // plugin class -> theme colour ("work": "accent")
}

public sealed class PanelInfo { public string Name { get; set; } = ""; public string File { get; set; } = ""; public string Kind { get; set; } = "panel"; } // panel | overlay
public sealed class PageInfo { public string Name { get; set; } = ""; public string Title { get; set; } = ""; public string? Icon { get; set; } public string File { get; set; } = ""; }
public sealed class ProviderInfo { public string? Prefix { get; set; } public string? Title { get; set; } public string? Icon { get; set; } public bool Always { get; set; } public string? Chip { get; set; } }
public sealed class KeyProposal { public string Binding { get; set; } = ""; public string Action { get; set; } = ""; public string? Description { get; set; } }

// The plugin system's JSON, source-generated (the menu app is Native AOT: no reflection).
// Comments and trailing commas are allowed: people hand-edit manifests. shell.json's types join
// this context when the registry is built (05-plugins.md §2.3).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Manifest))]
internal partial class PluginJson : JsonSerializerContext { }
