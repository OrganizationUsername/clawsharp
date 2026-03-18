namespace Clawsharp.Memory;

/// <summary>
///     Applies time-based exponential decay to memory relevance scores.
///     Score halves every <c>halfLifeDays</c>, following the formula:
///     <c>score * 2^(-ageDays / halfLifeDays)</c>.
/// </summary>
internal static class MemoryDecayScoring
{
    /// <summary>
    ///     Applies exponential decay to a relevance score based on fact age.
    /// </summary>
    /// <param name="score">The original relevance score.</param>
    /// <param name="createdAt">When the fact was created.</param>
    /// <param name="halfLifeDays">Days after which the score halves. Zero or negative disables decay.</param>
    /// <returns>The decayed score.</returns>
    public static float ApplyDecay(float score, DateTimeOffset createdAt, double halfLifeDays)
    {
        if (halfLifeDays <= 0 || createdAt == default)
        {
            return score;
        }

        var ageDays = (DateTimeOffset.UtcNow - createdAt).TotalDays;
        if (ageDays <= 0)
        {
            return score;
        }

        return score * (float)Math.Pow(2.0, -ageDays / halfLifeDays);
    }

    /// <summary>
    ///     Applies exponential time-decay plus a usage-frequency boost.
    /// </summary>
    /// <param name="score">Base relevance score.</param>
    /// <param name="createdAt">Fact creation timestamp.</param>
    /// <param name="halfLifeDays">Days after which the score halves. Zero or negative disables decay.</param>
    /// <param name="accessCount">Number of times this fact has been accessed by search.</param>
    /// <param name="lastAccessedAt">When the fact was last accessed (null = never).</param>
    /// <param name="usageBoostWeight">Weight for the usage boost (0.0–1.0, default 0.2).</param>
    public static float ApplyDecayWithUsage(
        float score,
        DateTimeOffset createdAt,
        double halfLifeDays,
        int accessCount = 0,
        DateTimeOffset? lastAccessedAt = null,
        double usageBoostWeight = 0.2)
    {
        var decayed = ApplyDecay(score, createdAt, halfLifeDays);

        // Usage boost: log(1 + accessCount) * weight * recencyFactor, capped at 1.0
        // Recency factor: 1.0 if accessed today, decays toward 0 with half-life of halfLifeDays
        if (accessCount > 0 && usageBoostWeight > 0)
        {
            double daysSinceAccess;
            if (lastAccessedAt.HasValue)
            {
                daysSinceAccess = Math.Max(0, (DateTimeOffset.UtcNow - lastAccessedAt.Value).TotalDays);
            }
            else
            {
                daysSinceAccess = (DateTimeOffset.UtcNow - createdAt).TotalDays;
            }
            var recencyFactor = Math.Pow(0.5, daysSinceAccess / Math.Max(halfLifeDays, 1));
            var boost = (float)(Math.Log(1.0 + accessCount) * usageBoostWeight * recencyFactor);
            decayed = Math.Min(1.0f, decayed + boost);
        }

        return decayed;
    }
}