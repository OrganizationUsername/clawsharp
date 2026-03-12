using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Slack;

/// <summary>
/// Request to update an existing Slack message via the chat.update API.
/// </summary>
internal sealed class SlackUpdateMessageRequest : IRequest<SlackUpdateMessageRequest, SlackPostMessageResponse>
{
    /// <summary>The Slack channel ID containing the message.</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "";

    /// <summary>The timestamp of the message to update.</summary>
    [JsonPropertyName("ts")]
    public string Ts { get; init; } = "";

    /// <summary>The new message text content.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "chat.update";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackUpdateMessageRequest> RequestTypeInfo => SlackJsonContext.Default.SlackUpdateMessageRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackPostMessageResponse> ResponseTypeInfo => SlackJsonContext.Default.SlackPostMessageResponse;
}