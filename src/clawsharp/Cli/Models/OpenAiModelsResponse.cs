using System.Text.Json.Serialization;

namespace Clawsharp.Cli.Models;

/// <summary>Response from the OpenAI-compatible /models endpoint.</summary>
internal sealed class OpenAiModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiModelItem>? Data { get; set; }
}

/// <summary>A single model entry from an OpenAI-compatible API.</summary>
internal sealed class OpenAiModelItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}