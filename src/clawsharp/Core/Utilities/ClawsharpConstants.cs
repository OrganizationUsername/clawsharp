using System.Collections.Frozen;
using Clawsharp.Config.Agent;

namespace Clawsharp.Core.Utilities;

/// <summary>Project-wide shared constants replacing magic strings and numbers.</summary>
public static class ClawsharpConstants
{
    /// <summary>Default base directory for clawsharp data files.</summary>
    public const string DefaultBaseDir = "~/.clawsharp";

    /// <summary>Default config file name.</summary>
    public const string DefaultConfigFileName = "config.json";

    /// <summary>Maximum Telegram message length in characters.</summary>
    public const int TelegramMaxMessageLength = 4096;

    /// <summary>Maximum Discord message length in characters.</summary>
    public const int DiscordMaxMessageLength = 2000;

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

    /// <summary>Telegram Bot API base URL (HTTP client base address).</summary>
    public const string TelegramBaseUrl = "https://api.telegram.org";

    // Channel API base URLs
    public const string SlackBaseUrl = "https://slack.com/api/";
    public const string LineBaseUrl = "https://api.line.me/";
    public const string LarkBaseUrl = "https://open.larksuite.com/";
    public const string FeishuBaseUrl = "https://open.feishu.cn/";

    /// <summary>
    /// Maps each <see cref="LlmProviderType"/> that has a sensible default base URL
    /// to that URL. Providers like Bedrock, Copilot, and VertexAI are excluded
    /// because they require project/account-specific configuration.
    /// </summary>
    public static readonly FrozenDictionary<LlmProviderType, string> DefaultProviderBaseUrls =
        new Dictionary<LlmProviderType, string>
        {
            [LlmProviderType.OpenAi] = OpenAiDefaultBaseUrl,
            [LlmProviderType.Anthropic] = AnthropicDefaultBaseUrl,
            [LlmProviderType.Gemini] = GeminiDefaultBaseUrl,
            [LlmProviderType.Ollama] = OllamaDefaultBaseUrl,
            [LlmProviderType.LmStudio] = LmStudioDefaultBaseUrl,
            [LlmProviderType.OpenRouter] = "https://openrouter.ai/api/v1",
            [LlmProviderType.Groq] = "https://api.groq.com/openai/v1",
            [LlmProviderType.DeepSeek] = "https://api.deepseek.com/v1",
            [LlmProviderType.Mistral] = "https://api.mistral.ai/v1",
            [LlmProviderType.Perplexity] = "https://api.perplexity.ai",
            [LlmProviderType.XAi] = "https://api.x.ai/v1",
            [LlmProviderType.VLlm] = "http://localhost:8000/v1",
            [LlmProviderType.LlamaCpp] = "http://localhost:8080/v1",
            [LlmProviderType.TogetherAi] = "https://api.together.xyz/v1",
            [LlmProviderType.Fireworks] = "https://api.fireworks.ai/inference/v1",
            [LlmProviderType.Cerebras] = "https://api.cerebras.ai/v1",
            [LlmProviderType.Novita] = "https://api.novita.ai/v3/openai",
            [LlmProviderType.HuggingFace] = "https://api-inference.huggingface.co/v1",
            [LlmProviderType.DashScope] = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            [LlmProviderType.Zhipu] = "https://open.bigmodel.cn/api/paas/v4",
            [LlmProviderType.Moonshot] = "https://api.moonshot.cn/v1",
            [LlmProviderType.Volcengine] = "https://ark.cn-beijing.volces.com/api/v3",
            [LlmProviderType.Minimax] = "https://api.minimax.chat/v1",
            [LlmProviderType.SiliconFlow] = "https://api.siliconflow.cn/v1",
            [LlmProviderType.Cohere] = "https://api.cohere.com/compatibility/v1",
            [LlmProviderType.SambaNova] = "https://api.sambanova.ai/v1",
            [LlmProviderType.Ai21] = "https://api.ai21.com/studio/v1",
            [LlmProviderType.Replicate] = "https://api.replicate.com/v1",
        }.ToFrozenDictionary();

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