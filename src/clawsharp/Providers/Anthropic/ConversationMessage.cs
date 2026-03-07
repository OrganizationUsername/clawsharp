using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>A single message in an Anthropic conversation (role + content blocks).</summary>
internal sealed class ConversationMessage
{
    /// <summary>The role ("user" or "assistant").</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    /// <summary>Content as a JSON element (string for simple text, array for content blocks).</summary>
    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }
}