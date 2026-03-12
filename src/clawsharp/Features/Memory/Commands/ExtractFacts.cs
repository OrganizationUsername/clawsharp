using Clawsharp.Core;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Clawsharp.Security;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Features.Memory.Commands;

/// <summary>
/// Extracts durable facts from conversation text via an LLM call,
/// scrubs secrets, and stores each fact in memory.
/// Encapsulates the logic previously in AgentLoop.ExtractAndStoreFactsAsync.
/// </summary>
[Handler]
public static partial class ExtractFacts
{
    private const int MaxExtractionChars = 4000;

    private const int MaxFactMaxTokens = 800;

    private const float ExtractionTemperature = 0.2f;

    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Command(string ConversationText);

    public sealed record Result(int FactsStored);

    private static async ValueTask<Result> HandleAsync(
        Command command,
        IProvider provider,
        IMemory memory,
        IOptions<AgentDefaults> defaultsOptions,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        const string extractionPrompt =
            "Extract key facts from this conversation that are worth remembering long-term. " +
            "Include preferences, decisions, technical details, names, and important context. " +
            "Return each fact on a separate line, prefixed with 'FACT: '. " +
            "Only include factual information. If nothing important, output only: (nothing to save)";

        // Truncate conversation text to prevent excessive token usage.
        var text = command.ConversationText.Length > MaxExtractionChars
            ? command.ConversationText[..MaxExtractionChars]
            : command.ConversationText;

        var request = new ChatRequest(
            Model: defaultsOptions.Value.Model,
            Messages:
            [
                new ChatMessage(MessageRole.System, extractionPrompt),
                new ChatMessage(MessageRole.User, text)
            ],
            Temperature: ExtractionTemperature,
            MaxTokens: MaxFactMaxTokens
        );

        var response = await provider.ChatAsync(request, ct).ConfigureAwait(false);
        if (response.Content is not { Length: > 0 } content ||
            content.Contains("(nothing to save)", StringComparison.OrdinalIgnoreCase))
        {
            return new Result(0);
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var factsStored = 0;

        foreach (var line in lines)
        {
            if (!line.StartsWith("FACT:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fact = line["FACT:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(fact) || fact.Length < 5)
            {
                continue;
            }

            // Scrub secrets before storage.
            var scrubResult = LeakDetector.Scan(fact, 0.5);
            if (!scrubResult.IsClean)
            {
                logger.LogWarning("Scrubbed {Count} secret pattern(s) from extracted fact", scrubResult.Patterns.Count);
                fact = scrubResult.Redacted;
            }

            await memory.AppendFactAsync(fact, ct).ConfigureAwait(false);
            factsStored++;
        }

        if (factsStored > 0)
        {
            logger.LogInformation("Extracted and stored {Count} fact(s) from conversation", factsStored);
        }

        return new Result(factsStored);
    }
}