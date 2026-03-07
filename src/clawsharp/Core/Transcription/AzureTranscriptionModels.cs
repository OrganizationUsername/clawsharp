using System.Text.Json.Serialization;

namespace Clawsharp.Core.Transcription;

/// <summary>Response from Azure Fast Transcription API.</summary>
internal sealed class AzureFastTranscriptionResponse
{
    [JsonPropertyName("combinedPhrases")]
    public List<AzureCombinedPhrase>? CombinedPhrases { get; init; }

    [JsonPropertyName("phrases")]
    public List<AzurePhrase>? Phrases { get; init; }
}

internal sealed class AzureCombinedPhrase
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("channel")]
    public int? Channel { get; init; }
}

internal sealed class AzurePhrase
{
    [JsonPropertyName("speaker")]
    public int? Speaker { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("offsetMilliseconds")]
    public long OffsetMilliseconds { get; init; }

    [JsonPropertyName("channel")]
    public int? Channel { get; init; }
}

/// <summary>Request body for the "definition" field in Azure Fast Transcription multipart upload.</summary>
internal sealed class AzureTranscriptionDefinition
{
    [JsonPropertyName("locales")]
    public List<string>? Locales { get; init; }

    [JsonPropertyName("diarization")]
    public AzureDiarizationOptions? Diarization { get; init; }
}

internal sealed class AzureDiarizationOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("maxSpeakers")]
    public int MaxSpeakers { get; init; }
}