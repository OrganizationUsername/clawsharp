using Clawsharp.Cost;
using Clawsharp.Memory;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Analytics;

/// <summary>
/// Records complete LLM interactions for analytics and optionally stores
/// concise summaries in the memory provider for later AI analysis.
/// </summary>
public sealed partial class InteractionTracker
{
    private readonly IInteractionStore _store;
    private readonly IMemory _memory;
    private readonly ILogger<InteractionTracker> _logger;
    private readonly bool _storeInMemory;

    public InteractionTracker(
        IInteractionStore store,
        IMemory memory,
        ILogger<InteractionTracker> logger,
        bool storeInMemory = false)
    {
        _store = store;
        _memory = memory;
        _logger = logger;
        _storeInMemory = storeInMemory;
    }

    /// <summary>
    /// Records a complete interaction after an LLM request-response cycle.
    /// </summary>
    public async Task RecordAsync(
        string sessionId,
        string channel,
        string model,
        string userPrompt,
        string? thinking,
        string response,
        IReadOnlyList<ToolCallSummary>? toolCalls,
        int toolIterations,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        long durationMs,
        CancellationToken ct = default)
    {
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens);

        var record = new InteractionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Channel = channel,
            Model = model,
            UserPrompt = userPrompt,
            Thinking = thinking,
            Response = response,
            ToolCalls = toolCalls,
            ToolIterations = toolIterations,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            CostUsd = cost,
            CacheSavingsUsd = savings,
            DurationMs = durationMs,
            Timestamp = DateTimeOffset.UtcNow,
        };

        try
        {
            await _store.AppendAsync(record, ct);
            LogInteractionRecorded(sessionId, model, cost, savings);
        }
        catch (Exception ex)
        {
            LogInteractionFailed(ex.Message);
            return; // Don't fail the main flow
        }

        if (_storeInMemory)
        {
            await StoreMemoryFactAsync(record, ct);
        }
    }

    private async Task StoreMemoryFactAsync(InteractionRecord record, CancellationToken ct)
    {
        try
        {
            // Build a concise analytical fact for memory search
            var cacheRate = record.InputTokens > 0
                ? (double)record.CacheReadTokens / record.InputTokens * 100
                : 0;

            var toolInfo = record.ToolCalls is { Count: > 0 }
                ? $" Tools: {string.Join(", ", record.ToolCalls.Select(t => t.Name))}."
                : "";

            var thinkingInfo = record.Thinking is { Length: > 0 }
                ? $" Used {record.Thinking.Length:N0} chars of reasoning."
                : "";

            var fact = $"[Interaction {record.Timestamp:yyyy-MM-dd HH:mm}] " +
                       $"Session {record.SessionId} on {record.Channel} using {record.Model}: " +
                       $"{record.InputTokens:N0} in / {record.OutputTokens:N0} out tokens, " +
                       $"${record.CostUsd:F4} cost, ${record.CacheSavingsUsd:F4} cache savings ({cacheRate:F0}% cache hit).{toolInfo}{thinkingInfo}";

            await _memory.AppendFactAsync(fact, ct);
        }
        catch (Exception ex)
        {
            LogMemoryStoreFailed(ex.Message);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Interaction recorded: session={SessionId} model={Model} cost=${Cost:F4} savings=${Savings:F4}")]
    private partial void LogInteractionRecorded(string sessionId, string model, double cost, double savings);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to record interaction: {Error}")]
    private partial void LogInteractionFailed(string error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to store interaction fact in memory: {Error}")]
    private partial void LogMemoryStoreFailed(string error);
}
