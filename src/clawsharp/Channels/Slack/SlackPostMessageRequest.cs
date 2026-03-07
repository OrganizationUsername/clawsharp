using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Channels.Slack;

/// <summary>
/// Request to post a message to a Slack channel via the chat.postMessage API.
/// </summary>
internal sealed class SlackPostMessageRequest : IRequest<SlackPostMessageRequest, SlackPostMessageResponse>
{
    /// <summary>The Slack channel ID to post to.</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "";

    /// <summary>The message text content.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Optional thread timestamp for threaded replies.</summary>
    [JsonPropertyName("thread_ts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadTs { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "chat.postMessage";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackPostMessageRequest> RequestTypeInfo => SlackJsonContext.Default.SlackPostMessageRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackPostMessageResponse> ResponseTypeInfo => SlackJsonContext.Default.SlackPostMessageResponse;
}