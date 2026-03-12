namespace Clawsharp.Analytics;

/// <summary>
/// Abstraction over interaction record persistence.
/// Implementations: <see cref="InteractionStorage"/> (JSONL file),
/// <see cref="EfInteractionStore{TContext}"/> (EF Core — SQLite, PostgreSQL, MS SQL).
/// </summary>
public interface IInteractionStore
{
    Task AppendAsync(InteractionRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<InteractionRecord>> ReadAllAsync(CancellationToken ct = default);
}
