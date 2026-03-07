using System.Text.Json.Serialization;

namespace Clawsharp.Core.Transcription;

/// <summary>Request body for GCP Cloud Speech-to-Text v1 recognize endpoint.</summary>
internal sealed class GcpSpeechRequest
{
    [JsonPropertyName("config")]
    public GcpSpeechConfig Config { get; init; } = new();

    [JsonPropertyName("audio")]
    public GcpAudioContent Audio { get; init; } = new();
}

internal sealed class GcpSpeechConfig
{
    [JsonPropertyName("encoding")]
    public string Encoding { get; init; } = "OGG_OPUS";

    [JsonPropertyName("sampleRateHertz")]
    public int? SampleRateHertz { get; init; }

    [JsonPropertyName("languageCode")]
    public string LanguageCode { get; init; } = "en-US";

    [JsonPropertyName("enableSpeakerDiarization")]
    public bool? EnableSpeakerDiarization { get; init; }

    [JsonPropertyName("diarizationSpeakerCount")]
    public int? DiarizationSpeakerCount { get; init; }
}

internal sealed class GcpAudioContent
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed class GcpSpeechResponse
{
    [JsonPropertyName("results")]
    public List<GcpSpeechResult>? Results { get; init; }
}

internal sealed class GcpSpeechResult
{
    [JsonPropertyName("alternatives")]
    public List<GcpSpeechAlternative>? Alternatives { get; init; }
}

internal sealed class GcpSpeechAlternative
{
    [JsonPropertyName("transcript")]
    public string? Transcript { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("words")]
    public List<GcpWord>? Words { get; init; }
}

internal sealed class GcpWord
{
    [JsonPropertyName("word")]
    public string? Word { get; init; }

    [JsonPropertyName("speakerTag")]
    public int SpeakerTag { get; init; }
}