using Intellenum;

namespace Clawsharp.Config.Agent;

/// <summary>Supported LLM provider type identifiers.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class LlmProviderType
{
    /// <summary>OpenAI-compatible API (api.openai.com or custom base URL).</summary>
    public static readonly LlmProviderType OpenAi = new("openai");

    /// <summary>Anthropic Messages API.</summary>
    public static readonly LlmProviderType Anthropic = new("anthropic");

    /// <summary>Google Gemini (Generative Language API).</summary>
    public static readonly LlmProviderType Gemini = new("gemini");

    /// <summary>Ollama (local, OpenAI-compatible).</summary>
    public static readonly LlmProviderType Ollama = new("ollama");

    /// <summary>LM Studio (local, OpenAI-compatible).</summary>
    public static readonly LlmProviderType LmStudio = new("lmstudio");

    /// <summary>OpenRouter — single key for all major models (openrouter.ai).</summary>
    public static readonly LlmProviderType OpenRouter = new("openrouter");

    /// <summary>Groq — ultra-fast inference (api.groq.com).</summary>
    public static readonly LlmProviderType Groq = new("groq");

    /// <summary>DeepSeek — reasoning models (api.deepseek.com).</summary>
    public static readonly LlmProviderType DeepSeek = new("deepseek");

    /// <summary>Mistral AI (api.mistral.ai).</summary>
    public static readonly LlmProviderType Mistral = new("mistral");

    /// <summary>Perplexity AI (api.perplexity.ai).</summary>
    public static readonly LlmProviderType Perplexity = new("perplexity");

    /// <summary>xAI / Grok (api.x.ai).</summary>
    public static readonly LlmProviderType XAi = new("xai");

    /// <summary>GitHub Copilot (OAuth device flow, api.githubcopilot.com).</summary>
    public static readonly LlmProviderType Copilot = new("copilot");

    /// <summary>AWS Bedrock (Converse API with SigV4 signing).</summary>
    public static readonly LlmProviderType Bedrock = new("bedrock");

    /// <summary>vLLM (local, OpenAI-compatible).</summary>
    public static readonly LlmProviderType VLlm = new("vllm");

    /// <summary>llama.cpp server (local, OpenAI-compatible).</summary>
    public static readonly LlmProviderType LlamaCpp = new("llamacpp");

    /// <summary>Together AI — inference cloud (api.together.xyz).</summary>
    public static readonly LlmProviderType TogetherAi = new("togetherai");

    /// <summary>Fireworks AI — fast inference (api.fireworks.ai).</summary>
    public static readonly LlmProviderType Fireworks = new("fireworks");

    /// <summary>Cerebras — fast inference (api.cerebras.ai).</summary>
    public static readonly LlmProviderType Cerebras = new("cerebras");

    /// <summary>Novita AI — inference API (api.novita.ai).</summary>
    public static readonly LlmProviderType Novita = new("novita");

    /// <summary>Hugging Face Inference API (api-inference.huggingface.co).</summary>
    public static readonly LlmProviderType HuggingFace = new("huggingface");

    /// <summary>Alibaba DashScope — Qwen models (dashscope.aliyuncs.com).</summary>
    public static readonly LlmProviderType DashScope = new("dashscope");

    /// <summary>Zhipu AI — GLM models (open.bigmodel.cn).</summary>
    public static readonly LlmProviderType Zhipu = new("zhipu");

    /// <summary>Moonshot AI — Kimi models (api.moonshot.cn).</summary>
    public static readonly LlmProviderType Moonshot = new("moonshot");

    /// <summary>Volcengine — ByteDance Doubao models (ark.cn-beijing.volces.com).</summary>
    public static readonly LlmProviderType Volcengine = new("volcengine");

    /// <summary>Minimax — MiniMax models (api.minimax.chat).</summary>
    public static readonly LlmProviderType Minimax = new("minimax");

    /// <summary>SiliconFlow — Chinese inference platform hosting open-source and commercial models (api.siliconflow.cn).</summary>
    public static readonly LlmProviderType SiliconFlow = new("siliconflow");

    /// <summary>Cohere — Command models via OpenAI-compatible endpoint (api.cohere.com).</summary>
    public static readonly LlmProviderType Cohere = new("cohere");

    /// <summary>SambaNova — fast inference (api.sambanova.ai).</summary>
    public static readonly LlmProviderType SambaNova = new("sambanova");

    /// <summary>AI21 Labs — Jamba models (api.ai21.com).</summary>
    public static readonly LlmProviderType Ai21 = new("ai21");

    /// <summary>Replicate — inference platform for open-source models (api.replicate.com).</summary>
    public static readonly LlmProviderType Replicate = new("replicate");

    /// <summary>Google Vertex AI — OpenAI-compatible endpoint (requires project-specific baseUrl).</summary>
    public static readonly LlmProviderType VertexAi = new("vertexai");
}