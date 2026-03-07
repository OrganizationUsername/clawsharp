namespace Clawsharp.Config;

/// <summary>
/// Validates dot-path config keys against the known <see cref="AppConfig"/> schema.
/// AOT-safe — uses a static set of valid paths with no reflection.
/// </summary>
internal static class ConfigKeyValidator
{
    /// <summary>
    /// Top-level prefixes whose sub-paths are dynamic (user-defined names).
    /// <c>providers.{name}.{field}</c>, <c>channels.{name}.{field}</c>,
    /// <c>mcpServers.{name}.{field}</c> all require at least 3 segments.
    /// </summary>
    private static readonly IReadOnlySet<string> DynamicPrefixes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "providers", "channels", "mcpServers" };

    /// <summary>All valid fixed (non-dynamic) leaf paths in config.json.</summary>
    private static readonly IReadOnlySet<string> ValidFixedPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // agents.defaults — core
            "agents.defaults.provider",
            "agents.defaults.model",
            "agents.defaults.temperature",
            "agents.defaults.maxToolIterations",
            "agents.defaults.maxContextMessages",
            "agents.defaults.consolidateEvery",
            "agents.defaults.rateLimitRequests",
            "agents.defaults.rateLimitWindowSeconds",
            "agents.defaults.allowSubagents",
            "agents.defaults.promptInjectionGuard",
            "agents.defaults.enableProviderFallback",
            "agents.defaults.fallbackModels",

            // agents.defaults.sessionPruning
            "agents.defaults.sessionPruning.maxMessages",
            "agents.defaults.sessionPruning.maxAgeDays",

            // agents.defaults.heartbeat
            "agents.defaults.heartbeat.enabled",
            "agents.defaults.heartbeat.schedule",
            "agents.defaults.heartbeat.promptFile",
            "agents.defaults.heartbeat.channel",

            // agents.defaults.contextWindow
            "agents.defaults.contextWindow.enabled",
            "agents.defaults.contextWindow.contextWindowTokens",
            "agents.defaults.contextWindow.compactionTriggerPercent",
            "agents.defaults.contextWindow.emergencyTrimPercent",

            // agents.defaults.compaction
            "agents.defaults.compaction.enabled",
            "agents.defaults.compaction.keepRecent",
            "agents.defaults.compaction.maxSummaryChars",
            "agents.defaults.compaction.maxSourceChars",
            "agents.defaults.compaction.preCompactionMemoryFlush",

            // agents.defaults.caching
            "agents.defaults.caching.enabled",
            "agents.defaults.caching.cacheToolDefinitions",

            // agents.defaults.thinking
            "agents.defaults.thinking.budgetTokens",
            "agents.defaults.thinking.reasoningEffort",
            "agents.defaults.thinking.geminiBudgetTokens",

            // agents.defaults.modelRouting
            "agents.defaults.modelRouting.enabled",
            "agents.defaults.modelRouting.simpleModel",
            "agents.defaults.modelRouting.simpleProvider",
            "agents.defaults.modelRouting.threshold",

            // memory
            "memory.backend",
            "memory.dir",
            "memory.connectionString",

            // memory.embedding
            "memory.embedding.provider",
            "memory.embedding.apiKey",
            "memory.embedding.model",
            "memory.embedding.baseUrl",

            // memory — ANN search
            "memory.embeddingDimension",

            // memory.decay
            "memory.decay.enabled",
            "memory.decay.ttlDays",
            "memory.decay.halfLifeDays",
            "memory.decay.pruneIntervalHours",

            // memory.enhancedRecall
            "memory.enhancedRecall.enabled",
            "memory.enhancedRecall.maxKeywords",
            "memory.enhancedRecall.resultsPerKeyword",

            // memory.factExtraction
            "memory.factExtraction.enabled",
            "memory.factExtraction.minTurns",
            "memory.factExtraction.minChars",

            // tools — core
            "tools.workspace",
            "tools.requireShellApproval",
            "tools.enableShellDenyPatterns",
            "tools.customShellDenyPatterns",
            "tools.sandbox",
            "tools.dockerImage",
            "tools.maxToolOutputChars",

            // tools — browser automation
            "tools.browser.enabled",
            "tools.browser.evaluateEnabled",
            "tools.browser.allowedDomains",
            "tools.browser.sessionsDir",
            "tools.browser.headless",

            // tools — PinchTab browser server
            "tools.pinchtab.enabled",
            "tools.pinchtab.baseUrl",
            "tools.pinchtab.token",
            "tools.pinchtab.evaluateEnabled",
            "tools.pinchtab.allowedDomains",

            // tools — search providers
            "tools.brave.apiKey",
            "tools.searxng.baseUrl",
            "tools.exa.apiKey",
            "tools.tavily.apiKey",
            "tools.jina.apiKey",
            "tools.firecrawl.apiKey",
            "tools.firecrawl.baseUrl",
            "tools.perplexity.apiKey",
            "tools.perplexity.model",
            "tools.glm.apiKey",
            "tools.glm.model",

            // cost
            "cost.enabled",
            "cost.dailyLimitUsd",
            "cost.monthlyLimitUsd",
            "cost.warnAtPercent",

            // audit
            "audit.enabled",
            "audit.logPath",
            "audit.maxSizeMb",
            "audit.retentionDays",

            // security
            "security.ssrf.enabled",

            // security — Landlock LSM sandbox (Linux ≥5.13)
            "security.landlock.enabled",
            "security.landlock.additionalReadPaths",
            "security.landlock.additionalReadWritePaths",
            "security.promptGuard.mode",
            "security.promptGuard.customPatterns",
            "security.leakDetector.sensitivity",
            "security.canaryGuard.enabled",
            "security.requireApprovalPatterns",
            "security.autoApprovePatterns",

            // secrets
            "secrets.encrypt",
            "secrets.onePassword.tokenEnvVar",
            "secrets.onePassword.cliBinary",
            "secrets.onePassword.timeoutSeconds",
            "secrets.bitwarden.tokenEnvVar",
            "secrets.bitwarden.cliBinary",
            "secrets.bitwarden.timeoutSeconds",

            // httpRequest
            "httpRequest.proxy",

            // transcription
            "transcription.enabled",
            "transcription.provider",
            "transcription.apiKey",
            "transcription.region",
            "transcription.language",
            "transcription.maxSpeakers",
            "transcription.model",
            "transcription.awsSecretKey",
        };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="dotPath"/> matches a known config leaf path.
    /// Dynamic sections (<c>providers.*</c>, <c>channels.*</c>, <c>mcpServers.*</c>) accept
    /// any sub-path with at least 3 total segments. <c>cost.prices.*</c> requires at least 4.
    /// </summary>
    internal static bool IsValidKey(string dotPath)
    {
        if (string.IsNullOrWhiteSpace(dotPath))
        {
            return false;
        }

        var segments = dotPath.Split('.');

        if (segments.Length < 2 || Array.Exists(segments, static s => s.Length == 0))
        {
            return false;
        }

        // Dynamic top-level sections: providers.{name}.{field}, channels.{name}.{field}, mcpServers.{name}.{field}
        if (DynamicPrefixes.Contains(segments[0]))
        {
            return segments.Length >= 3;
        }

        // Dynamic nested section: cost.prices.{model}.{field}
        if (segments.Length >= 4
            && string.Equals(segments[0], "cost", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "prices", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Dynamic nested section: tools.filterGroups.{groupName}.{field}
        if (segments.Length >= 4
            && string.Equals(segments[0], "tools", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "filterGroups", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Everything else must be a known fixed leaf path.
        return ValidFixedPaths.Contains(dotPath);
    }
}