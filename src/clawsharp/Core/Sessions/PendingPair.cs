namespace Clawsharp.Core.Sessions;

/// <summary>A pending pairing request awaiting operator approval.</summary>
public sealed record PendingPair(
    string Code,
    string Channel,
    string SenderId,
    string SenderName,
    DateTimeOffset ExpiresAt
);