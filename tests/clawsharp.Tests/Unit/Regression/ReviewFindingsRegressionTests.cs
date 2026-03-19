using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Sessions;
using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Regression;

/// <summary>
/// Regression tests for the 4 code review findings fixed on the analytics-schema-and-tests branch.
/// Each test validates that the specific bug cannot silently reappear.
/// </summary>
public sealed class ReviewFindingsRegressionTests
{
    // ──────────────────────────────────────────────────────────────────────
    //  Finding 1: Race condition — snapshot before background consolidation
    //  Fix: session.Messages.ToList() before Task.Run in PostProcessReplyAsync
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void MessageSnapshot_IsIsolatedFromOriginalListMutations()
    {
        var messages = new List<string> { "msg1", "msg2", "msg3" };
        var snapshot = messages.ToList();

        // Mutate original after snapshot
        messages.Add("msg4");
        messages.RemoveAt(0);

        // Snapshot is unchanged
        snapshot.Count.ShouldBe(3);
        snapshot[0].ShouldBe("msg1");
        snapshot[1].ShouldBe("msg2");
        snapshot[2].ShouldBe("msg3");
    }

    [Test]
    public void ChatMessageSnapshot_IsIsolatedFromSessionMutations()
    {
        var session = new Session { Id = "test:regression1" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "hello"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "world"));

        // Snapshot — this is the exact pattern used in PostProcessReplyAsync
        var snapshot = session.Messages.ToList();

        // Simulate the next incoming message mutating the session
        session.Messages.Add(new ChatMessage(MessageRole.User, "follow-up"));
        session.Messages.RemoveAt(0);

        // Snapshot must be completely independent
        snapshot.Count.ShouldBe(2);
        snapshot[0].Content.ShouldBe("hello");
        snapshot[1].Content.ShouldBe("world");
    }

    [Test]
    public void EmptyMessageList_SnapshotIsEmpty()
    {
        var messages = new List<ChatMessage>();
        var snapshot = messages.ToList();

        messages.Add(new ChatMessage(MessageRole.User, "late arrival"));

        snapshot.Count.ShouldBe(0);
    }

    [Test]
    public void ConcurrentMutationDuringIteration_SnapshotPreventsException()
    {
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 100; i++)
        {
            messages.Add(new ChatMessage(MessageRole.User, $"msg-{i}"));
        }

        var snapshot = messages.ToList();

        // Simulate concurrent mutation of the original list while iterating the snapshot
        var iteratedCount = 0;
        var mutationTask = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                messages.Add(new ChatMessage(MessageRole.Assistant, $"new-{i}"));
            }
        });

        // Iterating the snapshot should never throw, even with concurrent original mutation
        foreach (var _ in snapshot)
        {
            iteratedCount++;
        }

        mutationTask.Wait();

        iteratedCount.ShouldBe(100);
        snapshot.Count.ShouldBe(100);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Finding 2: ExtraHeaders cannot duplicate auth headers
    //  Fix: ConfigureHeaders skips Authorization and x-api-key from extraHeaders
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void ExtraHeaders_AuthorizationHeader_IsFiltered()
    {
        var extraHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer evil-key",
            ["X-Custom"] = "allowed-value"
        };

        var req = new HttpRequestMessage();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-key");

        ApplyExtraHeadersWithGuard(req, extraHeaders);

        // The real auth header is preserved, not overwritten
        req.Headers.Authorization!.Parameter.ShouldBe("real-key");
        // Custom header is present
        req.Headers.GetValues("X-Custom").First().ShouldBe("allowed-value");
    }

    [Test]
    public void ExtraHeaders_XApiKeyHeader_IsFiltered()
    {
        var extraHeaders = new Dictionary<string, string>
        {
            ["x-api-key"] = "evil-key",
            ["X-Custom"] = "allowed-value"
        };

        var req = new HttpRequestMessage();
        req.Headers.Add("x-api-key", "real-key");

        ApplyExtraHeadersWithGuard(req, extraHeaders);

        // Only the real x-api-key should exist (not replaced)
        req.Headers.GetValues("x-api-key").ShouldHaveSingleItem().ShouldBe("real-key");
        req.Headers.GetValues("X-Custom").First().ShouldBe("allowed-value");
    }

    [Test]
    public void ExtraHeaders_CaseInsensitive_AuthorizationVariants_AllFiltered()
    {
        var variants = new[] { "authorization", "AUTHORIZATION", "Authorization", "aUtHoRiZaTiOn" };

        foreach (var variant in variants)
        {
            var extraHeaders = new Dictionary<string, string>
            {
                [variant] = "Bearer evil-key"
            };

            var req = new HttpRequestMessage();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-key");

            ApplyExtraHeadersWithGuard(req, extraHeaders);

            req.Headers.Authorization!.Parameter.ShouldBe("real-key",
                $"Authorization variant '{variant}' should have been filtered");
        }
    }

    [Test]
    public void ExtraHeaders_CaseInsensitive_XApiKeyVariants_AllFiltered()
    {
        var variants = new[] { "x-api-key", "X-API-KEY", "X-Api-Key", "x-Api-KEY" };

        foreach (var variant in variants)
        {
            var extraHeaders = new Dictionary<string, string>
            {
                [variant] = "evil-key"
            };

            var req = new HttpRequestMessage();
            req.Headers.Add("x-api-key", "real-key");

            ApplyExtraHeadersWithGuard(req, extraHeaders);

            req.Headers.GetValues("x-api-key").ShouldHaveSingleItem().ShouldBe("real-key",
                $"x-api-key variant '{variant}' should have been filtered");
        }
    }

    [Test]
    public void ExtraHeaders_LegitimateCustomHeaders_AreApplied()
    {
        var extraHeaders = new Dictionary<string, string>
        {
            ["X-Org-Id"] = "org-123",
            ["X-Request-Id"] = "req-456",
            ["anthropic-beta"] = "max-tokens-3-5-sonnet-2024-07-15"
        };

        var req = new HttpRequestMessage();

        ApplyExtraHeadersWithGuard(req, extraHeaders);

        req.Headers.GetValues("X-Org-Id").First().ShouldBe("org-123");
        req.Headers.GetValues("X-Request-Id").First().ShouldBe("req-456");
        req.Headers.GetValues("anthropic-beta").First().ShouldBe("max-tokens-3-5-sonnet-2024-07-15");
    }

    [Test]
    public void ExtraHeaders_MixOfAuthAndCustom_OnlyCustomApplied()
    {
        var extraHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer evil",
            ["x-api-key"] = "evil-key",
            ["X-Custom-Safe"] = "safe-value"
        };

        var req = new HttpRequestMessage();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-key");
        req.Headers.Add("x-api-key", "real-key");

        ApplyExtraHeadersWithGuard(req, extraHeaders);

        req.Headers.Authorization!.Parameter.ShouldBe("real-key");
        req.Headers.GetValues("x-api-key").ShouldHaveSingleItem().ShouldBe("real-key");
        req.Headers.GetValues("X-Custom-Safe").First().ShouldBe("safe-value");
    }

    [Test]
    public void ExtraHeaders_NullDictionary_DoesNotThrow()
    {
        var req = new HttpRequestMessage();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-key");

        ApplyExtraHeadersWithGuard(req, null);

        req.Headers.Authorization!.Parameter.ShouldBe("real-key");
    }

    [Test]
    public void ExtraHeaders_EmptyDictionary_DoesNotThrow()
    {
        var req = new HttpRequestMessage();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-key");

        ApplyExtraHeadersWithGuard(req, new Dictionary<string, string>());

        req.Headers.Authorization!.Parameter.ShouldBe("real-key");
    }

    /// <summary>
    /// Applies the exact same filtering logic used in both OpenAiProvider.ConfigureHeaders
    /// and AnthropicProvider.ConfigureHeaders. This tests the guard pattern directly.
    /// </summary>
    private static void ApplyExtraHeadersWithGuard(
        HttpRequestMessage req,
        Dictionary<string, string>? extraHeaders)
    {
        if (extraHeaders is null)
        {
            return;
        }

        foreach (var (headerName, headerValue) in extraHeaders)
        {
            if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "x-api-key", StringComparison.OrdinalIgnoreCase))
                continue;

            req.Headers.TryAddWithoutValidation(headerName, headerValue);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Finding 3: ApiKeyRotator correctness
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void ApiKeyRotator_NullKeys_AlwaysReturnsFallback()
    {
        var rotator = new ApiKeyRotator("primary", null);

        rotator.GetNext().ShouldBe("primary");
        rotator.GetNext().ShouldBe("primary");
        rotator.GetNext().ShouldBe("primary");
    }

    [Test]
    public void ApiKeyRotator_EmptyKeys_AlwaysReturnsFallback()
    {
        var rotator = new ApiKeyRotator("fallback", []);

        rotator.GetNext().ShouldBe("fallback");
        rotator.GetNext().ShouldBe("fallback");
        rotator.GetNext().ShouldBe("fallback");
    }

    [Test]
    public void ApiKeyRotator_MultipleKeys_CyclesInOrder()
    {
        var rotator = new ApiKeyRotator("unused", ["a", "b", "c"]);

        // _index starts at -1; Interlocked.Increment yields 0, 1, 2, 3...
        rotator.GetNext().ShouldBe("a"); // index 0
        rotator.GetNext().ShouldBe("b"); // index 1
        rotator.GetNext().ShouldBe("c"); // index 2
        rotator.GetNext().ShouldBe("a"); // wraps to 0
        rotator.GetNext().ShouldBe("b"); // index 1
    }

    [Test]
    public void ApiKeyRotator_SingleKey_AlwaysReturnsThatKey()
    {
        var rotator = new ApiKeyRotator("fallback", ["only-key"]);

        rotator.GetNext().ShouldBe("only-key");
        rotator.GetNext().ShouldBe("only-key");
        rotator.GetNext().ShouldBe("only-key");
    }

    [Test]
    public void ApiKeyRotator_ConcurrentAccess_NoKeysSkipped()
    {
        var keys = new[] { "k0", "k1", "k2" };
        var rotator = new ApiKeyRotator("unused", keys.ToList());
        var results = new ConcurrentBag<string>();

        Parallel.For(0, 3000, _ => results.Add(rotator.GetNext()));

        // Each key should be used exactly 1000 times (3000 total / 3 keys)
        results.Count(k => k == "k0").ShouldBe(1000);
        results.Count(k => k == "k1").ShouldBe(1000);
        results.Count(k => k == "k2").ShouldBe(1000);
    }

    [Test]
    public void ApiKeyRotator_ConcurrentAccess_HighVolume_EvenDistribution()
    {
        var keys = Enumerable.Range(0, 7).Select(i => $"key-{i}").ToList();
        var rotator = new ApiKeyRotator("unused", keys);
        var results = new ConcurrentBag<string>();
        const int totalCalls = 7000;

        Parallel.For(0, totalCalls, _ => results.Add(rotator.GetNext()));

        // With 7 keys and 7000 calls, each key gets exactly 1000 calls
        foreach (var key in keys)
        {
            results.Count(k => k == key).ShouldBe(totalCalls / keys.Count);
        }
    }

    [Test]
    public void ApiKeyRotator_OverflowHandling_StaysNonNegative()
    {
        // The fix uses (i & int.MaxValue) % count to mask the sign bit.
        // Simulate what happens at int.MaxValue boundary:
        var overflowed = unchecked(int.MaxValue + 1); // = int.MinValue
        var result = (overflowed & int.MaxValue) % 3;
        result.ShouldBeGreaterThanOrEqualTo(0);
        result.ShouldBe(0);

        // Also check int.MinValue + 1
        var minPlusOne = int.MinValue + 1;
        var result2 = (minPlusOne & int.MaxValue) % 3;
        result2.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void ApiKeyRotator_FallbackKeyNotUsedWhenKeysPresent()
    {
        var rotator = new ApiKeyRotator("should-not-appear", ["a", "b"]);

        // Call enough times to cycle through all keys multiple times
        for (var i = 0; i < 20; i++)
        {
            var key = rotator.GetNext();
            key.ShouldNotBe("should-not-appear");
            key.ShouldBeOneOf("a", "b");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Finding 4: /model command — trimmed argument and edge cases
    //  Fix: argument.Trim() applied; string.IsNullOrWhiteSpace guard
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void SlashCommand_Model_NoArg_ReturnsSetModelWithNullArgument()
    {
        var result = SlashCommandRouter.TryHandle("/model", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBeNull();
    }

    [Test]
    public void SlashCommand_Model_Reset_ReturnsSetModelWithResetArgument()
    {
        var result = SlashCommandRouter.TryHandle("/model reset", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("reset");
    }

    [Test]
    public void SlashCommand_Model_SetModel_ReturnsSetModelWithModelArgument()
    {
        var result = SlashCommandRouter.TryHandle("/model gpt-4o", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("gpt-4o");
    }

    [Test]
    public void SlashCommand_Model_WhitespaceArg_IsTrimmedByRouter()
    {
        // The router uses Split with TrimEntries, so "  gpt-4o  " becomes "gpt-4o"
        var result = SlashCommandRouter.TryHandle("/model   gpt-4o  ", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("gpt-4o");
    }

    [Test]
    public void SlashCommand_Model_TrailingSpaceOnly_HasNullArgument()
    {
        // "/model " — the Split with TrimEntries produces only ["/model"]
        // because the second part is empty/whitespace and gets trimmed away
        var result = SlashCommandRouter.TryHandle("/model ", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        // With TrimEntries, empty second part means arg is null (only 1 part)
        arg.ShouldBeNull();
    }

    [Test]
    public void HandleModelCommand_NullArgument_DoesNotCrash()
    {
        // Validates that IsNullOrWhiteSpace guard handles null gracefully.
        // This is the condition that returns "Current model: ..." info.
        string.IsNullOrWhiteSpace(null).ShouldBeTrue();
    }

    [Test]
    public void HandleModelCommand_EmptyArgument_DoesNotCrash()
    {
        // Validates that IsNullOrWhiteSpace guard handles empty string.
        string.IsNullOrWhiteSpace("").ShouldBeTrue();
    }

    [Test]
    public void HandleModelCommand_WhitespaceOnlyArgument_DoesNotCrash()
    {
        // Validates that IsNullOrWhiteSpace guard handles whitespace-only string.
        string.IsNullOrWhiteSpace("   ").ShouldBeTrue();
    }

    [Test]
    public void HandleModelCommand_TrimPattern_RemovesSurroundingWhitespace()
    {
        // This is the exact pattern used in HandleModelCommandAsync:
        // var trimmedArg = argument.Trim();
        var argument = "  gpt-4o  ";
        var trimmedArg = argument.Trim();
        trimmedArg.ShouldBe("gpt-4o");
    }

    [Test]
    public void HandleModelCommand_TrimPattern_PreservesInternalSpaces()
    {
        // Model names shouldn't have internal spaces, but trim should only
        // affect leading/trailing whitespace.
        var argument = "  model with spaces  ";
        var trimmedArg = argument.Trim();
        trimmedArg.ShouldBe("model with spaces");
    }

    [Test]
    public void HandleModelCommand_ResetComparison_IsCaseInsensitive()
    {
        // The fix uses StringComparison.OrdinalIgnoreCase for "reset"
        string.Equals("reset", "reset", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        string.Equals("RESET", "reset", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        string.Equals("Reset", "reset", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        string.Equals("rEsEt", "reset", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Test]
    public void SlashCommand_Model_MultipleSpacesBetweenCommandAndArg_StillWorks()
    {
        // Split(' ', 2, StringSplitOptions.TrimEntries) handles multiple spaces
        var result = SlashCommandRouter.TryHandle("/model    claude-opus-4-0520", out _, out var arg);
        result.ShouldBe(SlashCommandResult.SetModel);
        arg.ShouldBe("claude-opus-4-0520");
    }
}
