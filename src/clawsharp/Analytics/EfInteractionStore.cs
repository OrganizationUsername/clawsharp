using System.Text.Json;
using Clawsharp.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Analytics;

/// <summary>
/// EF Core-backed interaction store. Works with any analytics DbContext
/// (SQLite, PostgreSQL, MS SQL). Runs migrations on first use.
/// </summary>
public sealed partial class EfInteractionStore<TContext> : IInteractionStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public EfInteractionStore(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EfInteractionStore<TContext>> logger,
        bool skipMigration = false)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _initialized = skipMigration;
    }

    public async Task AppendAsync(InteractionRecord record, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var entity = ToEntity(record);
        db.Set<InteractionEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<InteractionRecord>> ReadAllAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var entities = await db.Set<InteractionEntity>()
                               .AsNoTracking()
                               .OrderBy(e => e.Id)
                               .ToListAsync(ct);

        return entities.Select(ToRecord).ToList();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
            _initialized = true;
            LogDatabaseInitialized(typeof(TContext).Name);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static InteractionEntity ToEntity(InteractionRecord record)
    {
        string? toolCallsJson = null;
        if (record.ToolCalls is { Count: > 0 })
        {
            toolCallsJson = JsonSerializer.Serialize(
                record.ToolCalls, AnalyticsJsonContext.Default.IReadOnlyListToolCallSummary);
        }

        return new InteractionEntity
        {
            ExternalId = record.Id,
            SessionId = record.SessionId,
            Channel = record.Channel,
            Model = record.Model,
            UserPrompt = record.UserPrompt,
            Thinking = record.Thinking,
            Response = record.Response,
            ToolCallsJson = toolCallsJson,
            ToolIterations = record.ToolIterations,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            CacheReadTokens = record.CacheReadTokens,
            CacheWriteTokens = record.CacheWriteTokens,
            CostUsd = record.CostUsd,
            CacheSavingsUsd = record.CacheSavingsUsd,
            DurationMs = record.DurationMs,
            Timestamp = record.Timestamp,
        };
    }

    private static InteractionRecord ToRecord(InteractionEntity entity)
    {
        IReadOnlyList<ToolCallSummary>? toolCalls = null;
        if (entity.ToolCallsJson is not null)
        {
            try
            {
                toolCalls = JsonSerializer.Deserialize(
                    entity.ToolCallsJson, AnalyticsJsonContext.Default.IReadOnlyListToolCallSummary);
            }
            catch (JsonException)
            {
                // Skip malformed JSON — best-effort
            }
        }

        return new InteractionRecord
        {
            Id = entity.ExternalId,
            SessionId = entity.SessionId,
            Channel = entity.Channel,
            Model = entity.Model,
            UserPrompt = entity.UserPrompt,
            Thinking = entity.Thinking,
            Response = entity.Response,
            ToolCalls = toolCalls,
            ToolIterations = entity.ToolIterations,
            InputTokens = entity.InputTokens,
            OutputTokens = entity.OutputTokens,
            CacheReadTokens = entity.CacheReadTokens,
            CacheWriteTokens = entity.CacheWriteTokens,
            CostUsd = entity.CostUsd,
            CacheSavingsUsd = entity.CacheSavingsUsd,
            DurationMs = entity.DurationMs,
            Timestamp = entity.Timestamp,
        };
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Analytics database initialized: {ContextType}")]
    private partial void LogDatabaseInitialized(string contextType);
}
