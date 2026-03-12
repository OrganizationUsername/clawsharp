using System.Text.Json.Serialization;

namespace Clawsharp.Analytics;

[JsonSerializable(typeof(InteractionRecord))]
[JsonSerializable(typeof(ToolCallSummary))]
[JsonSerializable(typeof(IReadOnlyList<ToolCallSummary>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AnalyticsJsonContext : JsonSerializerContext;
