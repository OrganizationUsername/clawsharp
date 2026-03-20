using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Request body for the OpenRouter Chat Completions API (POST /api/v1/chat/completions).
/// Superset of the OpenAI format with additional routing, provider preference, and plugin fields.
/// </summary>
internal sealed class OpenRouterRequest : IRequest<OpenRouterRequest, OpenRouterResponse>
{
    /// <summary>Model identifier (e.g. "anthropic/claude-sonnet-4.6").</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    /// <summary>Ordered list of conversation messages.</summary>
    [JsonPropertyName("messages")]
    public List<CompletionMessage> Messages { get; init; } = [];

    /// <summary>Sampling temperature (0.0 - 2.0).</summary>
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }

    /// <summary>Available tool definitions for function calling.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FunctionTool>? Tools { get; init; }

    /// <summary>Tool choice strategy ("auto", "none", or a specific tool).</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; init; }

    /// <summary>
    /// Reasoning effort for models that support it (e.g. o1, o3).
    /// Valid values: "low", "medium", "high". Null means provider default.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; init; }

    /// <summary>Whether to stream the response via SSE.</summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; init; }

    /// <summary>Options controlling stream behavior (e.g. include usage in final chunk).</summary>
    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamOptions? StreamOptions { get; init; }

    // ── OpenRouter-specific fields ──────────────────────────────────────────

    /// <summary>
    /// Fallback model list. OpenRouter will try each model in order until one succeeds.
    /// Mutually exclusive with <see cref="Model"/> in practice — set Model to empty and use this instead.
    /// </summary>
    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Models { get; init; }

    /// <summary>
    /// Routing strategy. Set to "fallback" to enable sequential failover through <see cref="Models"/>.
    /// </summary>
    [JsonPropertyName("route")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Route { get; init; }

    /// <summary>
    /// Provider routing preferences — control which upstream providers serve the request
    /// (e.g. prefer Anthropic over Google for a given model).
    /// </summary>
    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterProviderPreferences? Provider { get; init; }

    /// <summary>
    /// OpenRouter plugins (e.g. web search, response healing).
    /// </summary>
    [JsonPropertyName("plugins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenRouterPlugin>? Plugins { get; init; }

    /// <summary>
    /// Stable end-user identifier for abuse detection and rate limiting on OpenRouter's side.
    /// </summary>
    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? User { get; init; }

    /// <summary>
    /// Output modalities. Set to ["image", "text"] for models that generate both,
    /// or ["image"] for image-only models. Null omits the field (text-only default).
    /// </summary>
    [JsonPropertyName("modalities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Modalities { get; init; }

    /// <summary>
    /// Audio output configuration. Required when modalities includes "audio".
    /// Controls voice selection and output format.
    /// </summary>
    [JsonPropertyName("audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterAudioConfig? AudioConfig { get; init; }

    // ── IRequest implementation ─────────────────────────────────────────────

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<OpenRouterRequest> RequestTypeInfo => OpenRouterJsonContext.Default.OpenRouterRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<OpenRouterResponse> ResponseTypeInfo => OpenRouterJsonContext.Default.OpenRouterResponse;
}
