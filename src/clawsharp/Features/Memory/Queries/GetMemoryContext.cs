using System.Text;
using Clawsharp.Config;
using Clawsharp.Memory;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Features.Memory.Queries;

/// <summary>
/// Retrieves the primary memory context and optionally enhances it with
/// keyword-expanded secondary searches (enhanced recall).
/// Encapsulates the logic previously in AgentLoop.EnhanceRecallWithKeywordsAsync.
/// </summary>
[Handler]
public static partial class GetMemoryContext
{
    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Query(string UserText);

    private static async ValueTask<string?> HandleAsync(
        Query query,
        IMemory memory,
        IOptions<MemoryConfig> memoryConfigOptions,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        var primaryContext = await memory.GetContextAsync(ct);

        var recallConfig = memoryConfigOptions.Value.EnhancedRecall;
        if (recallConfig is not { Enabled: true })
        {
            return primaryContext;
        }

        try
        {
            var keywords = KeywordExpander.ExtractKeywords(query.UserText);
            if (keywords.Count == 0)
            {
                return primaryContext;
            }

            var maxKeywords = recallConfig.MaxKeywords;
            var resultsPerKeyword = recallConfig.ResultsPerKeyword;

            // Collect existing facts from primary context to deduplicate.
            var existingFacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (primaryContext is not null)
            {
                foreach (var line in primaryContext.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.TrimStart('-', ' ');
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        existingFacts.Add(trimmed);
                    }
                }
            }

            var additionalFacts = new List<string>();
            var keywordsToSearch = keywords.Count > maxKeywords
                ? keywords.Take(maxKeywords).ToList()
                : keywords;

            foreach (var keyword in keywordsToSearch)
            {
                var results = await memory.SearchAsync(keyword, resultsPerKeyword, ct).ConfigureAwait(false);
                foreach (var result in results)
                {
                    if (!string.IsNullOrWhiteSpace(result) && existingFacts.Add(result))
                    {
                        additionalFacts.Add(result);
                    }
                }
            }

            if (additionalFacts.Count == 0)
            {
                return primaryContext;
            }

            logger.LogDebug(
                "Enhanced recall found {Count} additional facts from {KeywordCount} keywords",
                additionalFacts.Count, keywordsToSearch.Count);

            // Append additional facts to the primary context.
            var sb = new StringBuilder();
            if (primaryContext is not null)
            {
                sb.Append(primaryContext);
            }
            else
            {
                sb.AppendLine("## Memory");
            }

            foreach (var fact in additionalFacts)
            {
                sb.AppendLine($"- {fact}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Enhanced recall failed, returning primary context");
            return primaryContext;
        }
    }
}