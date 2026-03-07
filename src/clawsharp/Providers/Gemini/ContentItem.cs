using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A content item in a Gemini conversation (role + parts).</summary>
internal sealed class ContentItem
{
    /// <summary>The role ("user" or "model").</summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    /// <summary>Content parts (text, function calls, function responses).</summary>
    [JsonPropertyName("parts")]
    public List<ContentPart> Parts { get; init; } = [];
}