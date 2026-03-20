using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single content part within a multimodal message (text or image).</summary>
internal sealed class ContentPart
{
    /// <summary>Part type: "text" or "image_url".</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Text content (when type is "text").</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Image URL data (when type is "image_url").</summary>
    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageUrl? ImageUrl { get; init; }

    /// <summary>File data (PDF, text, etc.) — used with type "file".</summary>
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileContentData? File { get; init; }

    /// <summary>Audio input data — used with type "input_audio".</summary>
    [JsonPropertyName("input_audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioContentData? InputAudio { get; init; }

    /// <summary>Video URL data (when type is "video_url").</summary>
    [JsonPropertyName("video_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoUrl? VideoUrl { get; init; }
}