using Clawsharp.Config.Agent;
using Clawsharp.Config.Memory;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
namespace Clawsharp.Core.Utilities;

/// <summary>Project-wide shared constants replacing magic strings and numbers.</summary>
public static class ClawsharpConstants
{
    /// <summary>Default base directory for clawsharp data files.</summary>
    public const string DefaultBaseDir = "~/.clawsharp";

    /// <summary>Default config file name.</summary>
    public const string DefaultConfigFileName = "config.json";

    /// <summary>HTTP provider request timeout in seconds.</summary>
    public const int ProviderTimeoutSeconds = 120;

    /// <summary>Maximum image attachment size in bytes (5 MB).</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Maximum Telegram message length in characters.</summary>
    public const int TelegramMaxMessageLength = 4096;

    /// <summary>Maximum Discord message length in characters.</summary>
    public const int DiscordMaxMessageLength = 2000;

    /// <summary>Maximum voice/audio file size in bytes for transcription (25 MB — Whisper API limit).</summary>
    public const long MaxVoiceFileBytes = 25 * 1024 * 1024;

    /// <summary>Default Ollama base URL.</summary>
    public const string OllamaDefaultBaseUrl = "http://localhost:11434";

    /// <summary>Default LM Studio base URL.</summary>
    public const string LmStudioDefaultBaseUrl = "http://localhost:1234";

    /// <summary>Default OpenAI base URL.</summary>
    public const string OpenAiDefaultBaseUrl = "https://api.openai.com/v1";

    /// <summary>Default Anthropic base URL.</summary>
    public const string AnthropicDefaultBaseUrl = "https://api.anthropic.com";

    /// <summary>Default Gemini base URL.</summary>
    public const string GeminiDefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Anthropic API version header value.</summary>
    public const string AnthropicVersion = "2023-06-01";

    /// <summary>Discord CDN host for image attachment whitelisting.</summary>
    public const string DiscordCdnHost = "https://cdn.discordapp.com/";

    /// <summary>Discord media host for image attachment whitelisting.</summary>
    public const string DiscordMediaHost = "https://media.discordapp.net/";

    /// <summary>Telegram Bot API base URL template (append bot token + method).</summary>
    public const string TelegramApiBaseUrl = "https://api.telegram.org/bot";

    /// <summary>Telegram file download URL template.</summary>
    public const string TelegramFileBaseUrl = "https://api.telegram.org/file/bot";

    // Channel names → ChannelName (Intellenum in Core/ChannelName.cs)
    // Finish reasons → FinishReason (Intellenum in Core/FinishReason.cs)
    // Cron schedule kinds → CronScheduleKind (Intellenum in Cron/CronScheduleKind.cs)
    // Cron sources → CronSource (Intellenum in Cron/CronSource.cs)
    // Memory backends → MemoryBackend (Intellenum in Config/MemoryBackend.cs)
    // LLM provider types → LlmProviderType (Intellenum in Config/LlmProviderType.cs)

    /// <summary>HTTP header name constants.</summary>
    public static class HttpHeaders
    {
        /// <summary>Web pairing code header.</summary>
        public const string PairingCode = "X-Pairing-Code";

        /// <summary>Standard authorization header.</summary>
        public const string Authorization = "Authorization";

        /// <summary>Anthropic API key header.</summary>
        public const string AnthropicApiKey = "x-api-key";

        /// <summary>Anthropic API version header.</summary>
        public const string AnthropicApiVersion = "anthropic-version";

        /// <summary>Google Gemini API key header.</summary>
        public const string GeminiApiKey = "x-goog-api-key";

        /// <summary>Brave Search subscription token header.</summary>
        public const string BraveSubscriptionToken = "X-Subscription-Token";
    }
}