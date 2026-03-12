using System.Text.Json.Serialization;

namespace Clawsharp.Core.Sessions;

/// <summary>Source-generated JSON serialization context for pairing store models.</summary>
[JsonSerializable(typeof(PendingPair))]
[JsonSerializable(typeof(List<PendingPair>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class PairingJsonContext : JsonSerializerContext;