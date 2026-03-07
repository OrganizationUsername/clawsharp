using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Slack;

/// <summary>Response from the Slack chat.postMessage API.</summary>
internal sealed class SlackPostMessageResponse
{
    /// <summary>Whether the request was successful.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The timestamp of the posted message (used as a message identifier for edits).</summary>
    [JsonPropertyName("ts")]
    public string? Ts { get; init; }
}