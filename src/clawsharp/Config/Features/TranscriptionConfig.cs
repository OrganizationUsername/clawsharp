namespace Clawsharp.Config.Features;

/// <summary>
/// Top-level voice transcription configuration, shared across all channels.
/// Telegram, WhatsApp, Discord, and Signal all use the same API key.
/// Supports enc2: encryption, op:// (1Password), and bws: (Bitwarden SM) references.
/// </summary>
public sealed class TranscriptionConfig
{
    /// <summary>Whether voice transcription is active (default: true when ApiKey is set).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Transcription provider: "groq" (default), "openai", "azure", "gcp", or "awstranscribe".
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// API key for the transcription provider.
    /// Set via: clawsharp config set transcription.apiKey=...  (auto-encrypts with enc2:)
    /// Or use a password manager reference: op://Personal/Groq/api_key  or  bws:&lt;uuid&gt;
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Cloud service region (required for Azure: "eastus", "westus2", etc.).
    /// Used to build the Azure endpoint URL. Not used for groq/openai/gcp.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// BCP-47 language code hint (default: "en-US").
    /// Used as locale hint for Azure and languageCode for GCP.
    /// Groq/OpenAI Whisper auto-detect language; this value is ignored for them.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Maximum number of speakers for diarization (0 = disabled, default).
    /// Supported by Azure (2–35) and GCP.
    /// Ignored by groq/openai (no native diarization support).
    /// </summary>
    public int MaxSpeakers { get; init; }

    /// <summary>
    /// Whisper model override (groq/openai only).
    /// Groq default: whisper-large-v3-turbo  |  OpenAI default: whisper-1
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// AWS secret access key (awstranscribe only).
    /// Set via: clawsharp config set transcription.awsSecretKey=...
    /// </summary>
    public string? AwsSecretKey { get; set; }

    /// <summary>Resolved model name, applying Provider-specific defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveModel
    {
        get
        {
            if (Model is not null)
            {
                return Model;
            }

            if (string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return "whisper-1";
            }

            return "whisper-large-v3-turbo";
        }
    }

    /// <summary>Resolved language code, defaulting to "en-US".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveLanguage =>
        string.IsNullOrEmpty(Language) ? "en-US" : Language;

    /// <summary>Whether diarization is requested (MaxSpeakers > 0).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DiarizationEnabled => MaxSpeakers > 0;
}