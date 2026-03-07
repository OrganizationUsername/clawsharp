using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Slack;

/// <summary>Source-generated JSON serialization context for Slack channel models.</summary>
[JsonSerializable(typeof(SlackSocketEnvelope))]
[JsonSerializable(typeof(SlackAcknowledgeResponse))]
[JsonSerializable(typeof(SlackPostMessageRequest))]
[JsonSerializable(typeof(SlackPostMessageResponse))]
[JsonSerializable(typeof(SlackUpdateMessageRequest))]
[JsonSerializable(typeof(SlackUploadUrlResponse))]
[JsonSerializable(typeof(SlackCompleteUploadRequest))]
[JsonSerializable(typeof(SlackCompleteUploadResponse))]
[JsonSerializable(typeof(SlackFileReference))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class SlackJsonContext : JsonSerializerContext;