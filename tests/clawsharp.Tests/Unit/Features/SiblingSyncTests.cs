using Clawsharp.Config.Agent;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Sessions;
using Clawsharp.Features.Chat.Queries;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Tests.Unit.Features;

/// <summary>
/// Tests for the sibling sync features: /model slash command, ExtraHeaders,
/// ApiKeys round-robin rotation, and SpawnTimeout configuration.
/// </summary>
public sealed class SiblingSyncTests
{
    // ──────────────────────────────────────────────────────────────────────
    //  Feature 1: /model slash command parsing
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void SlashCommand_Model_NoArgs_ReturnsSetModel()
    {
        var result = SlashCommandRouter.TryHandle("/model", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_Model_WithName_ReturnsSetModelWithArg()
    {
        var result = SlashCommandRouter.TryHandle("/model gpt-4o", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("gpt-4o");
    }

    [Test]
    public void SlashCommand_Model_Reset_ReturnsSetModelWithResetArg()
    {
        var result = SlashCommandRouter.TryHandle("/model reset", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("reset");
    }

    [Test]
    public void SlashCommand_Model_WithComplexName_PreservesModelName()
    {
        var result = SlashCommandRouter.TryHandle("/model claude-sonnet-4.5-20250514", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("claude-sonnet-4.5-20250514");
    }

    [Test]
    public void SlashCommand_Model_CaseInsensitive()
    {
        var result = SlashCommandRouter.TryHandle("/MODEL gpt-4o", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("gpt-4o");
    }

    [Test]
    public void SlashCommand_Model_MixedCase()
    {
        var result = SlashCommandRouter.TryHandle("/Model GPT-4o", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("GPT-4o");
    }

    [Test]
    public void SlashCommand_ExistingCommands_StillWork()
    {
        SlashCommandRouter.TryHandle("/clear").ShouldBe(SlashCommandResult.ClearSession);
        SlashCommandRouter.TryHandle("/status").ShouldBe(SlashCommandResult.SendStatus);
        SlashCommandRouter.TryHandle("/usage").ShouldBe(SlashCommandResult.ShowUsage);
        SlashCommandRouter.TryHandle("/compact").ShouldBe(SlashCommandResult.TriggerCompaction);
    }

    [Test]
    public void SlashCommand_NonSlashCommand_DoesNotSetArgument()
    {
        var result = SlashCommandRouter.TryHandle("hello world", out _, out var arg);
        result.ShouldBeNull();
        arg.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_UnknownCommand_ReturnsNull()
    {
        var result = SlashCommandRouter.TryHandle("/unknown", out _, out var arg);
        result.ShouldBeNull();
        arg.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_TwoArgOverload_StillWorks()
    {
        var result = SlashCommandRouter.TryHandle("/model gpt-4o", out var error);
        result.ShouldBe(SlashCommandResult.SetModel);
        error.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_SingleArgOverload_StillWorks()
    {
        var result = SlashCommandRouter.TryHandle("/model gpt-4o");
        result.ShouldBe(SlashCommandResult.SetModel);
    }

    [Test]
    public void SlashCommand_ThinkOn_PassesArgument()
    {
        var result = SlashCommandRouter.TryHandle("/think on", out _, out var arg);
        result.ShouldBe(SlashCommandResult.ThinkOn);
        arg.ShouldBe("on");
    }

    [Test]
    public void SlashCommand_Goals_PassesNullArgument()
    {
        var result = SlashCommandRouter.TryHandle("/goals", out _, out var arg);
        result.ShouldBe(SlashCommandResult.ShowGoals);
        arg.ShouldBeNull();
    }

    [Test]
    public void Session_ModelOverride_DefaultsToNull()
    {
        var session = new Session { Id = "test:user1" };
        session.ModelOverride.ShouldBeNull();
    }

    [Test]
    public void Session_ModelOverride_CanBeSetAndCleared()
    {
        var session = new Session { Id = "test:user1" };
        session.ModelOverride = "gpt-4o";
        session.ModelOverride.ShouldBe("gpt-4o");

        session.ModelOverride = null;
        session.ModelOverride.ShouldBeNull();
    }

    [Test]
    public void Session_ModelOverride_PersistsAcrossAccesses()
    {
        var session = new Session { Id = "test:user1" };
        session.ModelOverride = "claude-opus-4-0520";
        session.ModelOverride.ShouldBe("claude-opus-4-0520");
        session.ModelOverride.ShouldBe("claude-opus-4-0520"); // re-read
    }

    [Test]
    public void RouteModel_Query_SessionModelOverride_DefaultsToNull()
    {
        var query = new RouteModel.Query("hello", 0, 0, null);
        query.SessionModelOverride.ShouldBeNull();
    }

    [Test]
    public void RouteModel_Query_SessionModelOverride_CanBeSet()
    {
        var query = new RouteModel.Query("hello", 0, 0, null, "claude-sonnet-4.5-20250514");
        query.SessionModelOverride.ShouldBe("claude-sonnet-4.5-20250514");
    }

    [Test]
    public void RouteModel_Query_BothOverrides_CanBeSet()
    {
        var query = new RouteModel.Query("hello", 0, 0, "cron-model", "session-model");
        query.ModelOverride.ShouldBe("cron-model");
        query.SessionModelOverride.ShouldBe("session-model");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Feature 1b: /models slash command parsing
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void SlashCommand_Models_NoArgs_ReturnsListModels()
    {
        var result = SlashCommandRouter.TryHandle("/models", out _, out var arg);
        result.ShouldBe(SlashCommandResult.ListModels);
        arg.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_Models_WithFilter_ReturnsListModelsWithArg()
    {
        var result = SlashCommandRouter.TryHandle("/models claude", out _, out var arg);
        result.ShouldBe(SlashCommandResult.ListModels);
        arg.ShouldBe("claude");
    }

    [Test]
    public void SlashCommand_Models_CaseInsensitive()
    {
        var result = SlashCommandRouter.TryHandle("/MODELS gpt", out _, out var arg);
        result.ShouldBe(SlashCommandResult.ListModels);
        arg.ShouldBe("gpt");
    }

    [Test]
    public void SlashCommand_Model_And_Models_AreDistinct()
    {
        SlashCommandRouter.TryHandle("/model").ShouldBe(SlashCommandResult.SetModel);
        SlashCommandRouter.TryHandle("/models").ShouldBe(SlashCommandResult.ListModels);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Feature 2: ExtraHeaders
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void ProviderConfig_ExtraHeaders_DefaultsToNull()
    {
        var config = new ProviderConfig();
        config.ExtraHeaders.ShouldBeNull();
    }

    [Test]
    public void ProviderConfig_ExtraHeaders_CanBeSet()
    {
        var config = new ProviderConfig
        {
            ExtraHeaders = new Dictionary<string, string>
            {
                ["X-Custom-Header"] = "value1",
                ["X-Org-Id"] = "org-123"
            }
        };
        config.ExtraHeaders.ShouldNotBeNull();
        config.ExtraHeaders.Count.ShouldBe(2);
        config.ExtraHeaders["X-Custom-Header"].ShouldBe("value1");
        config.ExtraHeaders["X-Org-Id"].ShouldBe("org-123");
    }

    [Test]
    public void ExtraHeaders_AppliedToOpenAiProvider_AcceptedInConstructor()
    {
        var extraHeaders = new Dictionary<string, string>
        {
            ["X-Test"] = "test-value",
            ["X-Org"] = "org-456"
        };

        // Verify constructor accepts extraHeaders without throwing
        var provider = new OpenAiProvider(
            new StubHttpClientFactory(),
            "https://api.openai.com/v1",
            "sk-test",
            "openai",
            authHeader: null,
            extraHeaders: extraHeaders);

        provider.Name.ShouldBe("openai");
    }

    [Test]
    public void ExtraHeaders_EmptyDictionary_AcceptedInConstructor()
    {
        var provider = new OpenAiProvider(
            new StubHttpClientFactory(),
            "https://api.openai.com/v1",
            "sk-test",
            "openai",
            authHeader: null,
            extraHeaders: new Dictionary<string, string>());

        provider.Name.ShouldBe("openai");
    }

    [Test]
    public void ExtraHeaders_NullDictionary_AcceptedInConstructor()
    {
        var provider = new OpenAiProvider(
            new StubHttpClientFactory(),
            "https://api.openai.com/v1",
            "sk-test",
            "openai",
            authHeader: null,
            extraHeaders: null);

        provider.Name.ShouldBe("openai");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Feature 3: ApiKeys round-robin rotation
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void ProviderConfig_ApiKeys_DefaultsToNull()
    {
        var config = new ProviderConfig();
        config.ApiKeys.ShouldBeNull();
    }

    [Test]
    public void ProviderConfig_ApiKeys_CanBeSet()
    {
        var config = new ProviderConfig
        {
            ApiKeys = ["key-1", "key-2", "key-3"]
        };
        config.ApiKeys.ShouldNotBeNull();
        config.ApiKeys.Count.ShouldBe(3);
    }

    [Test]
    public void OpenAiProvider_ApiKeys_AcceptsListInConstructor()
    {
        var keys = new List<string> { "key-1", "key-2", "key-3" };

        var provider = new OpenAiProvider(
            new StubHttpClientFactory(),
            "https://api.openai.com/v1",
            "primary-key",
            "openai",
            authHeader: null,
            extraHeaders: null,
            apiKeys: keys);

        provider.Name.ShouldBe("openai");
    }

    [Test]
    public void ProviderConfig_ApiKeys_NullByDefault_SingleKeyMode()
    {
        var config = new ProviderConfig
        {
            ApiKey = "primary-key",
            ApiKeys = null
        };
        config.ApiKeys.ShouldBeNull();
    }

    [Test]
    public void ProviderConfig_BothApiKeyAndApiKeys_CanCoexist()
    {
        var config = new ProviderConfig
        {
            ApiKey = "primary-key",
            ApiKeys = ["extra-key-1", "extra-key-2"]
        };

        config.ApiKey.ShouldBe("primary-key");
        config.ApiKeys.ShouldNotBeNull();
        config.ApiKeys.Count.ShouldBe(2);
    }

    [Test]
    public void ApiKeys_RoundRobin_MathematicalCorrectness()
    {
        var keyCount = 3;

        // Simulate the round-robin index calculation
        for (var i = 0; i < keyCount * 3; i++)
        {
            var incrementedValue = i + 1; // simulates Interlocked.Increment result
            var effectiveIndex = (incrementedValue & int.MaxValue) % keyCount;
            effectiveIndex.ShouldBeGreaterThanOrEqualTo(0);
            effectiveIndex.ShouldBeLessThan(keyCount);
        }
    }

    [Test]
    public void ApiKeys_RoundRobin_CyclesCorrectly()
    {
        var keyCount = 3;
        var indices = new List<int>();

        for (var i = 0; i < 9; i++)
        {
            var incrementedValue = i + 1;
            var effectiveIndex = (incrementedValue & int.MaxValue) % keyCount;
            indices.Add(effectiveIndex);
        }

        // Should cycle: 1, 2, 0, 1, 2, 0, 1, 2, 0
        indices[0].ShouldBe(1);
        indices[1].ShouldBe(2);
        indices[2].ShouldBe(0);
        indices[3].ShouldBe(1);
        indices[4].ShouldBe(2);
        indices[5].ShouldBe(0);
    }

    [Test]
    public void ApiKeys_RoundRobin_HandlesIntOverflow()
    {
        // After int.MaxValue, Interlocked.Increment wraps to int.MinValue
        var overflowed = unchecked(int.MaxValue + 1); // = int.MinValue
        var result = (overflowed & int.MaxValue) % 3;
        result.ShouldBe(0); // int.MinValue & int.MaxValue == 0
        result.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void ApiKeys_RoundRobin_SingleKey_AlwaysReturnsZero()
    {
        var keyCount = 1;

        for (var i = 0; i < 10; i++)
        {
            var incrementedValue = i + 1;
            var effectiveIndex = (incrementedValue & int.MaxValue) % keyCount;
            effectiveIndex.ShouldBe(0);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Feature 4: Configurable SpawnTimeout
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void AgentDefaults_SpawnTimeout_DefaultIs60Seconds()
    {
        var defaults = new AgentDefaults();
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_CustomValue_Preserved()
    {
        var defaults = new AgentDefaults { SpawnTimeout = TimeSpan.FromSeconds(120) };
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_ShortValue_Preserved()
    {
        var defaults = new AgentDefaults { SpawnTimeout = TimeSpan.FromSeconds(10) };
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_LongValue_Preserved()
    {
        var defaults = new AgentDefaults { SpawnTimeout = TimeSpan.FromMinutes(5) };
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_Zero_Preserved()
    {
        var defaults = new AgentDefaults { SpawnTimeout = TimeSpan.Zero };
        defaults.SpawnTimeout.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_SubSecond_Preserved()
    {
        var defaults = new AgentDefaults { SpawnTimeout = TimeSpan.FromMilliseconds(500) };
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Minimal IHttpClientFactory stub for constructor tests.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
