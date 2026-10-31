using System.Text.Json.Serialization;

namespace SystemMenu;

// The JSON the menu app reads and writes, its serializers generated at build time: the native
// build (Native AOT) has no reflection to make them as it runs. Names are matched
// case-insensitively, as themes/*.json and launch-history.json always were.
[JsonSerializable(typeof(History))]
[JsonSerializable(typeof(Theme))]
[JsonSerializable(typeof(CommunityIndex))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class Json : JsonSerializerContext { }
