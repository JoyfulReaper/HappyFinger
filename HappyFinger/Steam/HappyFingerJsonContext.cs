using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyFinger.Steam;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RandomGameDetails))]
internal partial class HappyFingerJsonContext
    : JsonSerializerContext
{
}