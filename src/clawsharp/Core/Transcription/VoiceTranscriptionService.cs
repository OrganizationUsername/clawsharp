using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Core.Transcription;

/// <summary>
/// Shared voice transcription service supporting multiple cloud providers.
/// Channels that support voice notes: Telegram, WhatsApp, Discord, Signal.
///
/// Providers:
///   - groq          -- Groq Whisper (default, fast, free tier; no diarization)
///   - openai        -- OpenAI Whisper (no diarization)
///   - azure         -- Azure Fast Transcription API (diarization supported, up to 35 speakers)
///   - gcp           -- GCP Cloud Speech-to-Text v1 (diarization supported)
///   - awstranscribe -- Deferred (requires AWSSDK.TranscribeStreaming package)
///
/// Configuration (config.json):
///   "transcription": {
///     "provider":    "azure",
///     "apiKey":      "enc2:...",
///     "region":      "eastus",      -- required for azure
///     "language":    "en-US",       -- optional, default en-US
///     "maxSpeakers": 2              -- 0 = disabled; >0 enables diarization (azure/gcp only)
///   }
///
/// Diarization output format (when maxSpeakers > 0 and provider supports it):
///   "Speaker 1: Hello there.\nSpeaker 2: Hi, how can I help?"
/// </summary>
public sealed partial class VoiceTranscriptionService
{
    private const int MaxErrorBodyLength = 500;

    private readonly HttpClient? _http;

    private readonly string? _apiKey;

    private readonly string? _baseUrl; // groq/openai

    private readonly string? _model; // groq/openai

    private readonly string? _azureUrl; // azure

    private readonly string? _gcpUrl; // gcp

    private readonly string _provider;

    private readonly string _language;

    private readonly int _maxSpeakers;

    private readonly ILogger<VoiceTranscriptionService> _logger;

    private int _diarizationWarnLogged; // 0 = not logged, 1 = logged; use Interlocked

    /// <summary>True when a provider and API key are configured and enabled.</summary>
    public bool IsEnabled { get; }

    public VoiceTranscriptionService(
        IOptions<AppConfig> options,
        IHttpClientFactory httpFactory,
        ILogger<VoiceTranscriptionService> logger)
    {
        _logger = logger;
        var cfg = options.Value.Transcription;
        if (cfg is not { Enabled: true } || string.IsNullOrEmpty(cfg.ApiKey))
        {
            IsEnabled = false;
            _provider = TranscriptionProvider.Groq;
            _language = "en-US";
            return;
        }

        IsEnabled = true;
        _apiKey = cfg.ApiKey;
        _http = httpFactory.CreateClient("transcription");
        _provider = (cfg.Provider ?? TranscriptionProvider.Groq).ToLowerInvariant();
        _language = cfg.EffectiveLanguage;
        _maxSpeakers = cfg.MaxSpeakers;

        if (string.Equals(_provider, TranscriptionProvider.Azure, StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(cfg.Region))
            {
                throw new InvalidOperationException(
                    "transcription.region is required when provider is 'azure' (e.g. \"eastus\").");
            }

            _azureUrl = $"https://{cfg.Region}.api.cognitive.microsoft.com" +
                        "/speechtotext/transcriptions:transcribe?api-version=2025-10-15";
        }
        else if (string.Equals(_provider, TranscriptionProvider.Gcp, StringComparison.Ordinal))
        {
            _gcpUrl = "https://speech.googleapis.com/v1/speech:recognize";
        }
        else if (string.Equals(_provider, TranscriptionProvider.OpenAi, StringComparison.Ordinal))
        {
            _baseUrl = ClawsharpConstants.OpenAiDefaultBaseUrl + "/audio/transcriptions";
            _model = cfg.EffectiveModel;
        }
        else // groq + unknown -> Groq endpoint
        {
            _baseUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
            _model = cfg.EffectiveModel;
        }
    }

    /// <summary>
    /// Transcribes raw audio bytes. Returns the transcript text, or speaker-labelled
    /// diarization when enabled and the provider supports it. Returns null on error.
    /// </summary>
    public Task<string?> TranscribeAsync(byte[] audioBytes, string mimeType, CancellationToken ct)
    {
        if (_http is null || _apiKey is null)
        {
            return Task.FromResult<string?>(null);
        }

        return _provider switch
        {
            var p when p == TranscriptionProvider.Azure => TranscribeAzureAsync(audioBytes, mimeType, ct),
            var p when p == TranscriptionProvider.Gcp => TranscribeGcpAsync(audioBytes, mimeType, ct),
            var p when p == TranscriptionProvider.AwsTranscribe => TranscribeAwsAsync(),
            _ => TranscribeOpenAiCompatAsync(audioBytes, mimeType, ct),
        };
    }

    // -------------------------------------------------------------------------
    // Groq / OpenAI Whisper (OpenAI-compatible multipart upload)
    // -------------------------------------------------------------------------

    private async Task<string?> TranscribeOpenAiCompatAsync(byte[] audioBytes, string mimeType, CancellationToken ct)
    {
        if (_maxSpeakers > 0 && Interlocked.CompareExchange(ref _diarizationWarnLogged, 1, 0) == 0)
        {
            LogDiarizationUnsupported(_provider);
        }

        var cleanMime = mimeType.Split(';')[0].Trim();
        var ext = MimeToExt(cleanMime);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(cleanMime);
        form.Add(fileContent, "file", $"audio.{ext}");
        form.Add(new StringContent(_model!), "model");

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Content = form;

        using var resp = await _http!.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await ReadErrorBodyAsync(resp, ct).ConfigureAwait(false);
            LogTranscriptionHttpError(_provider, (int)resp.StatusCode, errorBody);
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(
            stream, VoiceTranscriptJsonContext.Default.VoiceTranscriptResult, ct).ConfigureAwait(false);
        return result?.Text;
    }

    // -------------------------------------------------------------------------
    // Azure Fast Transcription API (with optional diarization)
    // -------------------------------------------------------------------------

    private async Task<string?> TranscribeAzureAsync(byte[] audioBytes, string mimeType, CancellationToken ct)
    {
        var cleanMime = mimeType.Split(';')[0].Trim();
        var ext = MimeToExt(cleanMime);

        AzureDiarizationOptions? diarization = null;
        if (_maxSpeakers > 0)
        {
            diarization = new AzureDiarizationOptions { Enabled = true, MaxSpeakers = _maxSpeakers };
        }

        var defObj = new AzureTranscriptionDefinition
        {
            Locales = [_language],
            Diarization = diarization,
        };
        var defJson = JsonSerializer.Serialize(
            defObj, VoiceTranscriptJsonContext.Default.AzureTranscriptionDefinition);

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(cleanMime);
        form.Add(audioContent, "audio", $"audio.{ext}");
        form.Add(new StringContent(defJson), "definition");

        using var req = new HttpRequestMessage(HttpMethod.Post, _azureUrl);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        req.Content = form;

        using var resp = await _http!.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await ReadErrorBodyAsync(resp, ct).ConfigureAwait(false);
            LogTranscriptionHttpError("azure", (int)resp.StatusCode, errorBody);
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(
            stream, VoiceTranscriptJsonContext.Default.AzureFastTranscriptionResponse, ct).ConfigureAwait(false);
        return FormatAzureTranscript(result);
    }

    private static string? FormatAzureTranscript(AzureFastTranscriptionResponse? result)
    {
        if (result is null)
        {
            return null;
        }

        // Diarization path: build "Speaker N: text" segments grouped by speaker
        if (result.Phrases is { Count: > 0 } && result.Phrases.Any(p => p.Speaker.HasValue))
        {
            var sb = new StringBuilder();
            int? lastSpeaker = null;
            foreach (var phrase in result.Phrases)
            {
                if (string.IsNullOrEmpty(phrase.Text))
                {
                    continue;
                }

                var speakerId = (phrase.Speaker ?? 0) + 1; // display as 1-based
                if (lastSpeaker != speakerId)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"Speaker {speakerId}: ");
                    lastSpeaker = speakerId;
                }
                else
                {
                    sb.Append(' ');
                }

                sb.Append(phrase.Text);
            }

            var diarized = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(diarized))
            {
                return diarized;
            }
        }

        // Fallback: combined transcript
        return result.CombinedPhrases?.FirstOrDefault()?.Text;
    }

    // -------------------------------------------------------------------------
    // GCP Cloud Speech-to-Text v1 (with optional diarization)
    // -------------------------------------------------------------------------

    private async Task<string?> TranscribeGcpAsync(byte[] audioBytes, string mimeType, CancellationToken ct)
    {
        var cleanMime = mimeType.Split(';')[0].Trim();
        var encoding = GcpEncoding(cleanMime);

        int? sampleRate = null;
        if (encoding == "LINEAR16")
        {
            sampleRate = 16000;
        }

        bool? enableDiarization = null;
        int? diarizationCount = null;
        if (_maxSpeakers > 0)
        {
            enableDiarization = true;
            diarizationCount = _maxSpeakers;
        }

        var reqBody = new GcpSpeechRequest
        {
            Config = new GcpSpeechConfig
            {
                Encoding = encoding,
                SampleRateHertz = sampleRate,
                LanguageCode = _language,
                EnableSpeakerDiarization = enableDiarization,
                DiarizationSpeakerCount = diarizationCount,
            },
            Audio = new GcpAudioContent
            {
                Content = Convert.ToBase64String(audioBytes)
            },
        };

        var bodyJson = JsonSerializer.Serialize(
            reqBody, VoiceTranscriptJsonContext.Default.GcpSpeechRequest);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var url = $"{_gcpUrl}?key={Uri.EscapeDataString(_apiKey!)}";
        using var resp = await _http!.PostAsync(url, content, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await ReadErrorBodyAsync(resp, ct).ConfigureAwait(false);
            LogTranscriptionHttpError("gcp", (int)resp.StatusCode, errorBody);
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(
            stream, VoiceTranscriptJsonContext.Default.GcpSpeechResponse, ct).ConfigureAwait(false);
        return FormatGcpTranscript(result);
    }

    private static string? FormatGcpTranscript(GcpSpeechResponse? result)
    {
        if (result?.Results is not { Count: > 0 })
        {
            return null;
        }

        // Diarization: word-level speaker tags are in the last result's first alternative
        var lastResult = result.Results[^1];
        var alt = lastResult.Alternatives?.FirstOrDefault();
        if (alt is null)
        {
            return null;
        }

        if (alt.Words is { Count: > 0 } && alt.Words.Any(w => w.SpeakerTag > 0))
        {
            var sb = new StringBuilder();
            int? lastTag = null;
            foreach (var word in alt.Words)
            {
                if (string.IsNullOrEmpty(word.Word))
                {
                    continue;
                }

                if (word.SpeakerTag != lastTag)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"Speaker {word.SpeakerTag}: ");
                    lastTag = word.SpeakerTag;
                }
                else
                {
                    sb.Append(' ');
                }

                sb.Append(word.Word);
            }

            var diarized = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(diarized))
            {
                return diarized;
            }
        }

        return alt.Transcript;
    }

    // -------------------------------------------------------------------------
    // AWS Transcribe -- deferred (requires AWSSDK.TranscribeStreaming)
    // -------------------------------------------------------------------------

    private static Task<string?> TranscribeAwsAsync()
        => throw new NotSupportedException(
            "AWS Transcribe requires the AWSSDK.TranscribeStreaming NuGet package. " +
            "Add <PackageReference Include=\"AWSSDK.TranscribeStreaming\" /> to clawsharp.csproj and implement TranscribeAwsAsync.");

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string MimeToExt(string cleanMime) => cleanMime switch
    {
        "audio/ogg" => "ogg",
        "audio/mpeg" or "audio/mp3" => "mp3",
        "audio/mp4" or "audio/m4a" => "m4a",
        "audio/wav" => "wav",
        "audio/aac" => "aac",
        "audio/webm" => "webm",
        "audio/flac" => "flac",
        _ => "ogg",
    };

    private static string GcpEncoding(string cleanMime) => cleanMime switch
    {
        "audio/ogg" or "audio/opus" => "OGG_OPUS",
        "audio/mpeg" or "audio/mp3" => "MP3",
        "audio/flac" => "FLAC",
        "audio/wav" or "audio/wave" => "LINEAR16",
        "audio/webm" => "WEBM_OPUS",
        "audio/mp4" or "audio/m4a" => "MP4",
        _ => "OGG_OPUS",
    };

    /// <summary>
    /// Reads up to 500 characters from the HTTP error response body for diagnostic logging.
    /// Returns "(empty)" if the body cannot be read.
    /// </summary>
    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return "(empty)";
            }

            if (body.Length > MaxErrorBodyLength)
            {
                return body[..MaxErrorBodyLength];
            }

            return body;
        }
        catch
        {
            return "(unreadable)";
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Diarization (MaxSpeakers) is configured but not supported by provider '{Provider}'. Plain transcript will be returned.")]
    private partial void LogDiarizationUnsupported(string provider);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Voice transcription failed for provider '{Provider}': HTTP {StatusCode} — {ErrorBody}")]
    private partial void LogTranscriptionHttpError(string provider, int statusCode, string errorBody);
}