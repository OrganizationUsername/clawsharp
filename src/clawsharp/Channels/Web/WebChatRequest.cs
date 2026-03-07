using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Channels.Web;

/// <summary>
/// Incoming chat request from a web client via POST /chat.
/// This is an inbound request (deserialized from the web client), not an outbound HTTP call.
/// The IRequest interface members are implemented for interface consistency but are not used for dispatch.
/// </summary>
internal sealed class WebChatRequest : IRequest<WebChatRequest, WebChatResponse>
{
    /// <summary>The user's message text.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>Optional session identifier for conversation continuity.</summary>
    [JsonPropertyName("session_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<WebChatRequest> RequestTypeInfo => WebJsonContext.Default.WebChatRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<WebChatResponse> ResponseTypeInfo => WebJsonContext.Default.WebChatResponse;
}