using Clawsharp.Config.Agent;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
﻿namespace Clawsharp.Core.Utilities;

/// <summary>Classified reason for a provider failure.</summary>
public enum FailoverReason
{
    /// <summary>Provider returned 429 or rate-limit error text.</summary>
    RateLimit,

    /// <summary>Provider returned 402 or billing-related error text.</summary>
    Billing,

    /// <summary>Request timed out or server returned 408/5xx.</summary>
    Timeout,

    /// <summary>Authentication or authorization failure (401/403).</summary>
    Auth,

    /// <summary>Request format is invalid (400). Non-retriable.</summary>
    Format,

    /// <summary>Provider is overloaded (mapped to RateLimit behavior).</summary>
    Overloaded,

    /// <summary>Unclassified error.</summary>
    Unknown
}

/// <summary>Thrown when all providers in the fallback chain have been exhausted.</summary>
public sealed class FallbackExhaustedException : Exception
{
    public FallbackExhaustedException(List<(string Provider, FailoverReason Reason, string Message)> attempts)
        : base($"All {attempts.Count} provider(s) exhausted: {string.Join(", ", attempts.Select(a => $"{a.Provider}={a.Reason}"))}")
    {
        Attempts = attempts;
    }

    /// <summary>Details of each provider attempt that failed.</summary>
    public IReadOnlyList<(string Provider, FailoverReason Reason, string Message)> Attempts { get; }
}