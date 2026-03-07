using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Breakdown of prompt token counts returned by the OpenAI API under <c>usage.prompt_tokens_details</c>.</summary>
internal sealed class PromptTokensDetails
{
    /// <summary>Tokens served from the automatic prefix cache (billed at 0.50× input price).</summary>
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; init; }
}