using Clawsharp.Config;

namespace Clawsharp.Tests.Unit.Config;

/// <summary>Tests for <see cref="ConfigKeyValidator"/> dot-path validation.</summary>
public sealed class ConfigKeyValidatorTests
{
    // ── Valid fixed leaf paths ────────────────────────────────────────────────

    // ── Legacy aliases (pre-TimeSpan migration) ─────────────────────────────

    [Test]
    [TestCase("agents.defaults.rateLimitWindowSeconds")]
    [TestCase("secrets.onePassword.timeoutSeconds")]
    [TestCase("secrets.bitwarden.timeoutSeconds")]
    public void IsValidKey_LegacySecondsAlias_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    // ── Valid fixed leaf paths ────────────────────────────────────────────────

    [Test]
    [TestCase("agents.defaults.provider")]
    [TestCase("agents.defaults.model")]
    [TestCase("agents.defaults.temperature")]
    [TestCase("agents.defaults.maxToolIterations")]
    [TestCase("agents.defaults.maxContextMessages")]
    [TestCase("agents.defaults.consolidateEvery")]
    [TestCase("agents.defaults.caching.enabled")]
    [TestCase("agents.defaults.caching.cacheToolDefinitions")]
    [TestCase("agents.defaults.compaction.enabled")]
    [TestCase("agents.defaults.compaction.keepRecent")]
    [TestCase("agents.defaults.contextWindow.enabled")]
    [TestCase("agents.defaults.contextWindow.contextWindowTokens")]
    [TestCase("agents.defaults.heartbeat.enabled")]
    [TestCase("agents.defaults.heartbeat.schedule")]
    [TestCase("agents.defaults.sessionPruning.maxMessages")]
    [TestCase("agents.defaults.sessionPruning.maxAgeDays")]
    [TestCase("memory.backend")]
    [TestCase("memory.dir")]
    [TestCase("memory.connectionString")]
    [TestCase("memory.embedding.provider")]
    [TestCase("memory.embedding.apiKey")]
    [TestCase("memory.decay.enabled")]
    [TestCase("memory.decay.ttlDays")]
    [TestCase("tools.workspace")]
    [TestCase("tools.brave.apiKey")]
    [TestCase("tools.searxng.baseUrl")]
    [TestCase("tools.requireShellApproval")]
    [TestCase("cost.enabled")]
    [TestCase("cost.dailyLimitUsd")]
    [TestCase("audit.enabled")]
    [TestCase("audit.logPath")]
    [TestCase("security.ssrf.enabled")]
    [TestCase("security.promptGuard.mode")]
    [TestCase("security.leakDetector.sensitivity")]
    [TestCase("secrets.encrypt")]
    [TestCase("secrets.onePassword.tokenEnvVar")]
    [TestCase("secrets.bitwarden.cliBinary")]
    [TestCase("transcription.enabled")]
    [TestCase("transcription.provider")]
    [TestCase("transcription.apiKey")]
    [TestCase("transcription.region")]
    [TestCase("transcription.awsSecretKey")]
    public void IsValidKey_KnownFixedPath_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    // ── Case insensitivity ───────────────────────────────────────────────────

    [Test]
    [TestCase("Agents.Defaults.Model")]
    [TestCase("MEMORY.BACKEND")]
    [TestCase("Cost.Enabled")]
    [TestCase("Transcription.Provider")]
    public void IsValidKey_CaseInsensitive_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    // ── Dynamic sections: providers, channels, mcpServers ────────────────────

    [Test]
    [TestCase("providers.openai.apiKey")]
    [TestCase("providers.anthropic.model")]
    [TestCase("providers.ollama.baseUrl")]
    [TestCase("channels.telegram.botToken")]
    [TestCase("channels.discord.guildId")]
    [TestCase("channels.slack.appToken")]
    [TestCase("mcpServers.brave.command")]
    [TestCase("mcpServers.playwright.args")]
    public void IsValidKey_DynamicSection_ThreeOrMoreSegments_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    [Test]
    [TestCase("providers.openai.nested.deep.path")]
    [TestCase("channels.discord.nested.value")]
    [TestCase("mcpServers.something.a.b.c")]
    public void IsValidKey_DynamicSection_DeeplyNested_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    [Test]
    [TestCase("providers.openai")]
    [TestCase("channels.telegram")]
    [TestCase("mcpServers.brave")]
    public void IsValidKey_DynamicSection_OnlyTwoSegments_ReturnsFalse(string key)
    {
        // Two segments means setting an object node, not a leaf.
        ConfigKeyValidator.IsValidKey(key).ShouldBeFalse();
    }

    // ── Dynamic section: cost.prices ─────────────────────────────────────────

    [Test]
    [TestCase("cost.prices.gpt-4o.input")]
    [TestCase("cost.prices.claude-3-5-sonnet.output")]
    [TestCase("cost.prices.llama-3.1-70b.input")]
    public void IsValidKey_CostPrices_FourOrMoreSegments_ReturnsTrue(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeTrue();
    }

    [Test]
    public void IsValidKey_CostPrices_ThreeSegments_ReturnsFalse()
    {
        // cost.prices.gpt-4o is the model object, not a leaf.
        ConfigKeyValidator.IsValidKey("cost.prices.gpt-4o").ShouldBeFalse();
    }

    // ── Invalid / typo paths ─────────────────────────────────────────────────

    [Test]
    [TestCase("agents.defualts.model")]
    [TestCase("agents.defaults.modle")]
    [TestCase("memmory.backend")]
    [TestCase("tools.bravee.apiKey")]
    [TestCase("cost.enable")]
    [TestCase("auditt.enabled")]
    [TestCase("security.ssrf.enabled.extra")]
    [TestCase("transcription.providers")]
    [TestCase("nonexistent.section.key")]
    public void IsValidKey_Typo_ReturnsFalse(string key)
    {
        ConfigKeyValidator.IsValidKey(key).ShouldBeFalse();
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Test]
    public void IsValidKey_EmptyString_ReturnsFalse()
    {
        ConfigKeyValidator.IsValidKey("").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_Whitespace_ReturnsFalse()
    {
        ConfigKeyValidator.IsValidKey("  ").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_SingleSegment_ReturnsFalse()
    {
        ConfigKeyValidator.IsValidKey("agents").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_TopLevelOnly_ReturnsFalse()
    {
        // "memory" alone is not a leaf -- it's a section.
        ConfigKeyValidator.IsValidKey("memory").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_IntermediateNode_ReturnsFalse()
    {
        // "agents.defaults" is a node, not a leaf.
        ConfigKeyValidator.IsValidKey("agents.defaults").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_TrailingDot_ReturnsFalse()
    {
        // The split produces an empty segment at the end.
        ConfigKeyValidator.IsValidKey("agents.defaults.model.").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_DynamicSection_TrailingDot_ReturnsFalse()
    {
        // "providers.openai.apiKey." has an empty final segment.
        ConfigKeyValidator.IsValidKey("providers.openai.apiKey.").ShouldBeFalse();
    }

    [Test]
    public void IsValidKey_LeadingDot_ReturnsFalse()
    {
        ConfigKeyValidator.IsValidKey(".agents.defaults.model").ShouldBeFalse();
    }
}