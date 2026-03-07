using System.Text.Json.Serialization;

namespace Clawsharp.Cli.Models;

/// <summary>Source-generated JSON context for model list API responses.</summary>
[JsonSerializable(typeof(OpenAiModelsResponse))]
[JsonSerializable(typeof(GeminiModelsResponse))]
internal sealed partial class ModelsJsonContext : JsonSerializerContext;