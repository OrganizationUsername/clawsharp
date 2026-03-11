using System.Text;
using Clawsharp.Cost;
using Clawsharp.Providers;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Services;

/// <summary>
///     Compacts conversation history by summarizing older messages while keeping recent ones verbatim.
///     Uses the LLM to generate a concise summary of the conversation prefix, then replaces it with
///     a single summary message.
/// </summary>
public sealed partial class CompactionService
{
    private const string SummaryPrompt =
        "Summarize the key points, decisions, and context from this conversation. Be concise.";

    private readonly CostTracker _costTracker;
    private readonly AuditLogger? _auditLogger;

    private readonly ILogger<CompactionService> _logger;

    public CompactionService(CostTracker costTracker, AuditLogger? auditLogger, ILogger<CompactionService> logger)
    {
        _costTracker = costTracker;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    /// <summary>
    ///     Compact the message history by keeping recent messages and summarizing older ones.
    ///     The system prompt (first System message) is always preserved. Messages between the system
    ///     prompt and the <paramref name="keepRecent" /> most recent messages are summarized via the LLM.
    /// </summary>
    /// <param name="messages">The full message list to compact.</param>
    /// <param name="provider">The LLM provider to use for summarization.</param>
    /// <param name="model">The model ID for the summarization request.</param>
    /// <param name="keepRecent">Number of recent messages to keep verbatim.</param>
    /// <param name="maxSummaryChars">Maximum character length of the generated summary.</param>
    /// <param name="maxSourceChars">Maximum total characters of source messages fed to the summarizer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A compacted message list: [system prompt, summary message, ...recent messages].</returns>
    public async Task<List<ChatMessage>> CompactAsync(
        List<ChatMessage> messages,
        IProvider provider,
        string model,
        int keepRecent = 20,
        int maxSummaryChars = 2000,
        int maxSourceChars = 12000,
        CancellationToken ct = default)
    {
        if (messages.Count <= keepRecent + 1)
        {
            // Nothing to compact — not enough messages beyond system prompt + recent
            return messages;
        }

        // Extract system prompt (first message if it's a System role)
        ChatMessage? systemPrompt = null;
        var startIndex = 0;
        if (messages.Count > 0 && messages[0].Role == MessageRole.System)
        {
            systemPrompt = messages[0];
            startIndex = 1;
        }

        // Determine the split point: keep the last `keepRecent` messages
        var recentStart = Math.Max(startIndex, messages.Count - keepRecent);
        var olderMessages = messages.GetRange(startIndex, recentStart - startIndex);
        var recentMessages = messages.GetRange(recentStart, messages.Count - recentStart);

        if (olderMessages.Count == 0)
        {
            // Nothing to summarize
            return messages;
        }

        LogCompactionStarted(olderMessages.Count, recentMessages.Count);

        try
        {
            var summary = await SummarizeMessagesAsync(
                olderMessages, provider, model, maxSummaryChars, maxSourceChars, ct).ConfigureAwait(false);

            // Security: scan the LLM-generated summary for prompt injection patterns
            // before reinserting into conversation history. An attacker could inject
            // content that survives compaction and influences future LLM behavior.
            var injAction = PromptGuard.ScanAndApply(
                ref summary, "compaction summary", PromptGuardModes.Sanitize, _auditLogger, ct: ct);
            if (injAction != InjectionAction.None)
            {
                LogCompactionInjectionDetected();
            }

            // Strip metadata sentinels that could confuse role assignment
            summary = PromptGuard.StripMetadataSentinels(summary);

            var result = new List<ChatMessage>(recentMessages.Count + 2);
            if (systemPrompt is not null)
            {
                result.Add(systemPrompt);
            }

            result.Add(new ChatMessage(MessageRole.Assistant, $"[Compaction summary]\n{summary}"));
            result.AddRange(recentMessages);

            // Merge any consecutive same-role messages created by compaction
            // (e.g. summary assistant message adjacent to a recent assistant message).
            result = AgentLoop.MergeConsecutiveRoles(result);

            LogCompactionCompleted(messages.Count, result.Count, ContextWindowGuard.EstimateTokens(summary));
            return result;
        }
        catch (Exception ex)
        {
            // Degraded: LLM summarization failed, falling back to force-compression.
            // This discards older messages without summarization. Operators should monitor
            // for this warning — repeated occurrences indicate provider instability.
            LogCompactionDegraded(ex, messages.Count);
            return ForceCompress(messages);
        }
    }

    /// <summary>
    ///     Emergency compression when context is fully exhausted. No LLM call is made.
    ///     Keeps the system prompt (first message if System role) and the last 4 messages.
    /// </summary>
    /// <param name="messages">The full message list to compress.</param>
    /// <returns>A drastically reduced message list: [system prompt, ...last 4 messages].</returns>
    public static List<ChatMessage> ForceCompress(List<ChatMessage> messages)
    {
        const int keepLast = 4;

        var result = new List<ChatMessage>(keepLast + 1);

        // Preserve system prompt if present
        if (messages.Count > 0 && messages[0].Role == MessageRole.System)
        {
            result.Add(messages[0]);
        }

        // Keep last N messages
        var start = Math.Max(result.Count, messages.Count - keepLast);
        for (var i = start; i < messages.Count; i++)
        {
            result.Add(messages[i]);
        }

        return result;
    }

    /// <summary>
    ///     Summarizes a list of older messages by building a condensed source text,
    ///     optionally splitting into two halves for large message sets, then sending to the LLM.
    /// </summary>
    private async Task<string> SummarizeMessagesAsync(
        List<ChatMessage> olderMessages,
        IProvider provider,
        string model,
        int maxSummaryChars,
        int maxSourceChars,
        CancellationToken ct)
    {
        // For very large message sets, split into two halves and summarize each
        if (olderMessages.Count > 10)
        {
            var mid = olderMessages.Count / 2;
            var firstHalf = olderMessages.GetRange(0, mid);
            var secondHalf = olderMessages.GetRange(mid, olderMessages.Count - mid);

            var results = await Task.WhenAll(
                SummarizeBatchAsync(firstHalf, provider, model, maxSummaryChars / 2, maxSourceChars / 2, ct),
                SummarizeBatchAsync(secondHalf, provider, model, maxSummaryChars / 2, maxSourceChars / 2, ct)).ConfigureAwait(false);
            var summary1 = results[0];
            var summary2 = results[1];

            return $"{summary1}\n\n{summary2}";
        }

        return await SummarizeBatchAsync(olderMessages, provider, model, maxSummaryChars, maxSourceChars, ct).ConfigureAwait(false);
    }

    /// <summary>Summarizes a single batch of messages via the LLM.</summary>
    private async Task<string> SummarizeBatchAsync(
        List<ChatMessage> batch,
        IProvider provider,
        string model,
        int maxSummaryChars,
        int maxSourceChars,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var charBudget = maxSourceChars;
        const int maxPerMessage = 500;

        foreach (var msg in batch)
        {
            if (charBudget <= 0)
            {
                break;
            }

            var content = msg.Content ?? "";
            if (content.Length > maxPerMessage)
            {
                content = content[..maxPerMessage];
            }

            var line = $"{msg.Role.Value}: {content}";
            if (line.Length > charBudget)
            {
                line = line[..charBudget];
            }

            sb.AppendLine(line);
            charBudget -= line.Length;
        }

        // Check budget before making a compaction LLM call. Pass 0 for estimated cost
        // because precise token counts are unavailable here; this still rejects when
        // the budget is already exceeded.
        var budgetCheck = await _costTracker.CheckBudgetAsync(0, ct).ConfigureAwait(false);
        if (budgetCheck.Status == BudgetStatus.Exceeded)
        {
            LogCompactionBudgetSkipped();
            return "(compaction skipped — budget exceeded)";
        }

        var request = new ChatRequest(
            Model: model,
            Messages:
            [
                new ChatMessage(MessageRole.System, SummaryPrompt),
                new ChatMessage(MessageRole.User, sb.ToString())
            ],
            Temperature: 0.3f,
            MaxTokens: (maxSummaryChars + 3) / 4 // rough chars-to-tokens conversion
        );

        var response = await provider.ChatAsync(request, ct).ConfigureAwait(false);
        var summary = response.Content ?? "(compaction failed)";

        // Enforce max summary length
        if (summary.Length > maxSummaryChars)
        {
            summary = summary[..maxSummaryChars];
        }

        return summary;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "Compaction started: summarizing {OlderCount} older messages, keeping {RecentCount} recent")]
    private partial void LogCompactionStarted(int olderCount, int recentCount);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information,
        Message = "Compaction completed: {OriginalCount} messages -> {CompactedCount} messages (summary ~{SummaryTokens} tokens)")]
    private partial void LogCompactionCompleted(int originalCount, int compactedCount, int summaryTokens);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message =
            "Compaction DEGRADED: LLM summarization failed, fell back to force-compression ({OriginalCount} messages truncated without summary). Check provider health.")]
    private partial void LogCompactionDegraded(Exception exception, int originalCount);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning,
        Message = "Compaction LLM call skipped — cost budget already exceeded")]
    private partial void LogCompactionBudgetSkipped();

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning,
        Message = "Compaction summary contained prompt injection pattern — sanitized before reinsertion")]
    private partial void LogCompactionInjectionDetected();
}