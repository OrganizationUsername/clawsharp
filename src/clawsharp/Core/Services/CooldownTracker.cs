using Clawsharp.Config.Agent;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
﻿using System.Collections.Concurrent;

namespace Clawsharp.Core.Services;

/// <summary>
///     Per-provider cooldown state machine. Tracks failure counts and applies
///     exponential backoff cooldowns. Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}" />.
/// </summary>
public sealed class CooldownTracker
{
    private readonly ConcurrentDictionary<string, CooldownState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> if the provider is currently in cooldown and should be skipped.</summary>
    public bool IsInCooldown(string providerName)
    {
        if (!_states.TryGetValue(providerName, out var state))
        {
            return false;
        }

        lock (state)
        {
            var now = DateTimeOffset.UtcNow;

            // Auto-reset: 24 hours with no failure clears everything
            if (state.FailureCount > 0 && now - state.LastFailure > TimeSpan.FromHours(24))
            {
                state.Reset();
                return false;
            }

            return state.CooldownUntil.HasValue && now < state.CooldownUntil.Value;
        }
    }

    /// <summary>
    ///     Records a failure for the given provider and computes its cooldown duration.
    ///     If <paramref name="retryAfter" /> is provided (from a Retry-After header), it takes precedence.
    /// </summary>
    public void RecordFailure(string providerName, FailoverReason reason, TimeSpan? retryAfter = null)
    {
        var state = _states.GetOrAdd(providerName, _ => new CooldownState());

        lock (state)
        {
            state.FailureCount++;
            state.LastFailure = DateTimeOffset.UtcNow;
            state.LastReason = reason;

            if (retryAfter.HasValue)
            {
                // Explicit Retry-After from the provider takes precedence
                state.CooldownUntil = state.LastFailure + retryAfter.Value;
            }
            else
            {
                state.CooldownUntil = state.LastFailure + ComputeCooldown(reason, state.FailureCount);
            }
        }
    }

    /// <summary>Records a successful call. Clears all failure counters for the provider.</summary>
    public void RecordSuccess(string providerName)
    {
        if (_states.TryGetValue(providerName, out var state))
        {
            lock (state)
            {
                state.Reset();
            }
        }
    }

    /// <summary>
    ///     Computes cooldown duration based on reason and consecutive failure count.
    ///     Standard: 1 min x 5^min(n-1, 3) → 1m, 5m, 25m, capped at 1h.
    ///     Billing: 5 hours x 2^min(n-1, 10), capped at 24h.
    /// </summary>
    private static TimeSpan ComputeCooldown(FailoverReason reason, int failureCount)
    {
        if (reason == FailoverReason.Billing)
        {
            var exponent = Math.Min(failureCount - 1, 10);
            var hours = 5.0 * Math.Pow(2, exponent);
            return TimeSpan.FromHours(Math.Min(hours, 24));
        }

        // Standard backoff: 1 min * 5^min(n-1, 3)
        // n=1 → 1m, n=2 → 5m, n=3 → 25m, n=4+ → capped at 60m (1h)
        var exp = Math.Min(failureCount - 1, 3);
        var minutes = 1.0 * Math.Pow(5, exp);
        return TimeSpan.FromMinutes(Math.Min(minutes, 60));
    }

    /// <summary>Mutable state for a single provider's cooldown tracking.</summary>
    private sealed class CooldownState
    {
        public int FailureCount;

        public DateTimeOffset LastFailure;

        public FailoverReason LastReason;

        public DateTimeOffset? CooldownUntil;

        public void Reset()
        {
            FailureCount = 0;
            LastFailure = default;
            LastReason = FailoverReason.Unknown;
            CooldownUntil = null;
        }
    }
}