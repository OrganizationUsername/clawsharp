using System.Text.Json.Serialization;

namespace Clawsharp.Cost;

[JsonSerializable(typeof(CostRecord))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CostJsonContext : JsonSerializerContext;