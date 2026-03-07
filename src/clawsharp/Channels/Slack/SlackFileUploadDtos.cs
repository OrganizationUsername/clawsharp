using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Channels.Slack;

/// <summary>Response from files.getUploadURLExternal (step 1 of 3-step upload).</summary>
internal sealed class SlackUploadUrlResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("upload_url")]
    public string? UploadUrl { get; init; }

    [JsonPropertyName("file_id")]
    public string? FileId { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>Request body for files.completeUploadExternal (step 3 of 3-step upload).</summary>
internal sealed class SlackCompleteUploadRequest : IRequest<SlackCompleteUploadRequest, SlackCompleteUploadResponse>
{
    [JsonPropertyName("files")]
    public List<SlackFileReference> Files { get; init; } = [];

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    [JsonPropertyName("initial_comment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InitialComment { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "files.completeUploadExternal";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackCompleteUploadRequest> RequestTypeInfo =>
        SlackJsonContext.Default.SlackCompleteUploadRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<SlackCompleteUploadResponse> ResponseTypeInfo =>
        SlackJsonContext.Default.SlackCompleteUploadResponse;
}

/// <summary>Reference to a file uploaded via getUploadURLExternal.</summary>
internal sealed class SlackFileReference
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }
}

/// <summary>Response from files.completeUploadExternal (step 3 of 3-step upload).</summary>
internal sealed class SlackCompleteUploadResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}