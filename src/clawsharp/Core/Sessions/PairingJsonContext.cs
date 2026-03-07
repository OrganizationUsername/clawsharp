using System.Text.Json.Serialization;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Sessions;

/// <summary>Source-generated JSON serialization context for pairing store models.</summary>
[JsonSerializable(typeof(PendingPair))]
[JsonSerializable(typeof(List<PendingPair>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class PairingJsonContext : JsonSerializerContext;