using Clawsharp.Core;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Config;

/// <summary>
/// Pure unit tests for CachingConfig defaults and the AgentLoop-equivalent logic
/// that maps config to ChatRequest caching flags. No I/O.
/// </summary>
public sealed class CachingConfigTests
{
    // ── Default values ────────────────────────────────────────────────────────

    [Test]
    public void CachingConfig_DefaultValues_AllEnabled()
    {
        var cfg = new CachingConfig();
        cfg.Enabled.ShouldBeTrue();
        cfg.CacheToolDefinitions.ShouldBeTrue();
    }

    [Test]
    public void CachingConfig_NullInstance_TreatedAsAllEnabled()
    {
        // Simulate the null-coalescing logic from AgentLoop / SpawnTool.
        CachingConfig? caching = null;

        var cachingEnabled = caching?.Enabled ?? true;
        var cacheToolDefs = cachingEnabled && (caching?.CacheToolDefinitions ?? true);

        cachingEnabled.ShouldBeTrue();
        cacheToolDefs.ShouldBeTrue();
    }

    // ── Enabled = false kills both flags ─────────────────────────────────────

    [Test]
    public void CachingConfig_EnabledFalse_BothFlagsOff()
    {
        var caching = new CachingConfig { Enabled = false };

        var cachingEnabled = caching.Enabled;
        var cacheToolDefs = cachingEnabled && caching.CacheToolDefinitions;

        cachingEnabled.ShouldBeFalse();
        cacheToolDefs.ShouldBeFalse();
    }

    [Test]
    public void CachingConfig_EnabledFalse_StaticPartSuppressed()
    {
        // When Enabled=false, AgentLoop passes null for both split parts.
        var caching = new CachingConfig { Enabled = false };
        var cachingEnabled = caching.Enabled;

        const string staticPrompt = "You are clawsharp...";
        const string dynamicPrompt = "Current date: 2026-01-01";

        var request = new ChatRequest(
            Model: "gpt-4o",
            Messages: [],
            SystemStaticPart: cachingEnabled ? staticPrompt : null,
            SystemDynamicPart: cachingEnabled ? dynamicPrompt : null,
            CacheToolDefinitions: cachingEnabled && caching.CacheToolDefinitions
        );

        request.SystemStaticPart.ShouldBeNull();
        request.SystemDynamicPart.ShouldBeNull();
        request.CacheToolDefinitions.ShouldBeFalse();
    }

    // ── Enabled = true, CacheToolDefinitions = false ─────────────────────────

    [Test]
    public void CachingConfig_SystemCachingOn_ToolCachingOff()
    {
        var caching = new CachingConfig { Enabled = true, CacheToolDefinitions = false };

        var cachingEnabled = caching.Enabled;
        var cacheToolDefs = cachingEnabled && caching.CacheToolDefinitions;

        cachingEnabled.ShouldBeTrue();
        cacheToolDefs.ShouldBeFalse();
    }

    [Test]
    public void CachingConfig_ToolCachingFalse_StaticPartPreserved()
    {
        var caching = new CachingConfig { Enabled = true, CacheToolDefinitions = false };
        var cachingEnabled = caching.Enabled;
        var cacheToolDefs = cachingEnabled && caching.CacheToolDefinitions;

        const string staticPrompt = "You are clawsharp...";
        const string dynamicPrompt = "Current date: 2026-01-01";

        var request = new ChatRequest(
            Model: "claude-sonnet-4-6",
            Messages: [],
            SystemStaticPart: cachingEnabled ? staticPrompt : null,
            SystemDynamicPart: cachingEnabled ? dynamicPrompt : null,
            CacheToolDefinitions: cacheToolDefs
        );

        // System split is preserved
        request.SystemStaticPart.ShouldBe(staticPrompt);
        request.SystemDynamicPart.ShouldBe(dynamicPrompt);

        // But tool caching is off
        request.CacheToolDefinitions.ShouldBeFalse();
    }

    // ── Default ChatRequest values ────────────────────────────────────────────

    [Test]
    public void ChatRequest_DefaultConstructor_CacheToolDefinitionsTrue()
    {
        // Without explicit caching config, ChatRequest defaults CacheToolDefinitions to true.
        var request = new ChatRequest(
            Model: "gpt-4o",
            Messages: []
        );

        request.CacheToolDefinitions.ShouldBeTrue();
    }

    // ── AgentDefaults integration ─────────────────────────────────────────────

    [Test]
    public void AgentDefaults_CachingNull_EquivalentToEnabled()
    {
        var defaults = new AgentDefaults();
        defaults.Caching.ShouldBeNull();

        // Verify the null-coalescing logic yields enabled behavior.
        var cachingEnabled = defaults.Caching?.Enabled ?? true;
        var cacheToolDefs = cachingEnabled && (defaults.Caching?.CacheToolDefinitions ?? true);

        cachingEnabled.ShouldBeTrue();
        cacheToolDefs.ShouldBeTrue();
    }

    [Test]
    public void AgentDefaults_CachingDisabled_YieldsDisabled()
    {
        var defaults = new AgentDefaults { Caching = new CachingConfig { Enabled = false } };

        var cachingEnabled = defaults.Caching?.Enabled ?? true;
        var cacheToolDefs = cachingEnabled && (defaults.Caching?.CacheToolDefinitions ?? true);

        cachingEnabled.ShouldBeFalse();
        cacheToolDefs.ShouldBeFalse();
    }
}