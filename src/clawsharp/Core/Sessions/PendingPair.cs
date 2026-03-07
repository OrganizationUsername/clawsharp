using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Sessions;

/// <summary>A pending pairing request awaiting operator approval.</summary>
public sealed record PendingPair(
    string Code,
    string Channel,
    string SenderId,
    string SenderName,
    DateTimeOffset ExpiresAt
);