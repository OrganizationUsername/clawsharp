using System.Text.Json.Serialization;

namespace Clawsharp.Goals;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<GoalStatus>)])]
[JsonSerializable(typeof(List<Goal>))]
[JsonSerializable(typeof(Goal))]
[JsonSerializable(typeof(GoalStep))]
internal sealed partial class GoalJsonContext : JsonSerializerContext;