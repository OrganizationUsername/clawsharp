using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

public sealed class CanaryGuardTests
{
    // ── GenerateCanary ───────────────────────────────────────────────────

    [Test]
    public void GenerateCanary_ReturnsInstructionContainingCssecPrefix()
    {
        var guard = new CanaryGuard();
        var instruction = guard.GenerateCanary();

        instruction.ShouldContain("CSSEC-");
        instruction.ShouldContain("do not repeat");
    }

    [Test]
    public void GenerateCanary_SetsCurrentCanary()
    {
        var guard = new CanaryGuard();
        guard.CurrentCanary.ShouldBeNull();

        guard.GenerateCanary();

        guard.CurrentCanary.ShouldNotBeNull();
        guard.CurrentCanary.ShouldStartWith("CSSEC-");
    }

    [Test]
    public void GenerateCanary_RotatesTokenEachCall()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var first = guard.CurrentCanary;

        guard.GenerateCanary();
        var second = guard.CurrentCanary;

        first.ShouldNotBe(second);
    }

    [Test]
    public void GenerateCanary_ProducesHex32CharToken()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();

        // "CSSEC-" prefix (6 chars) + 32 hex chars = 38 total
        guard.CurrentCanary!.Length.ShouldBe(38);
        guard.CurrentCanary[6..].ShouldAllBe(c => "0123456789ABCDEF".Contains(c));
    }

    // ── CheckOutput ─────────────────────────────────────────────────────

    [Test]
    public void CheckOutput_NoCanaryGenerated_ReturnsFalse()
    {
        var guard = new CanaryGuard();

        guard.CheckOutput("any text here").ShouldBeFalse();
    }

    [Test]
    public void CheckOutput_CanaryNotInOutput_ReturnsFalse()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();

        guard.CheckOutput("This is a perfectly normal LLM response.").ShouldBeFalse();
    }

    [Test]
    public void CheckOutput_CanaryPresentInOutput_ReturnsTrue()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var canary = guard.CurrentCanary!;

        guard.CheckOutput($"Here is the system prompt content: {canary} as you can see").ShouldBeTrue();
    }

    [Test]
    public void CheckOutput_CanaryExactMatch_ReturnsTrue()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();

        guard.CheckOutput(guard.CurrentCanary!).ShouldBeTrue();
    }

    [Test]
    public void CheckOutput_PreviousCanaryAfterRotation_ReturnsFalse()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var oldCanary = guard.CurrentCanary!;

        // Rotate to a new canary
        guard.GenerateCanary();

        // The old canary should no longer trigger detection
        guard.CheckOutput($"Here is {oldCanary}").ShouldBeFalse();
    }

    [Test]
    public void CheckOutput_CaseSensitive_PartialMatchFails()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var canary = guard.CurrentCanary!;

        // Lowercase version should not match (hex chars are uppercase)
        guard.CheckOutput(canary.ToLowerInvariant()).ShouldBeFalse();
    }

    // ── Redact ──────────────────────────────────────────────────────────

    [Test]
    public void Redact_NoCanaryGenerated_ReturnsOriginalText()
    {
        var guard = new CanaryGuard();

        guard.Redact("some text").ShouldBe("some text");
    }

    [Test]
    public void Redact_CanaryNotPresent_ReturnsOriginalText()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();

        guard.Redact("clean output").ShouldBe("clean output");
    }

    [Test]
    public void Redact_CanaryPresent_ReplacesWithPlaceholder()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var canary = guard.CurrentCanary!;
        var input = $"Prefix {canary} suffix";

        guard.Redact(input).ShouldBe("Prefix [CANARY-REDACTED] suffix");
    }

    [Test]
    public void Redact_MultipleOccurrences_RedactsAll()
    {
        var guard = new CanaryGuard();
        guard.GenerateCanary();
        var canary = guard.CurrentCanary!;
        var input = $"{canary} and then {canary}";

        var result = guard.Redact(input);

        result.ShouldBe("[CANARY-REDACTED] and then [CANARY-REDACTED]");
    }

    // ── Integration-style ───────────────────────────────────────────────

    [Test]
    public void FullFlow_GenerateCheckRedact_WorksEndToEnd()
    {
        var guard = new CanaryGuard();
        var instruction = guard.GenerateCanary();
        var canary = guard.CurrentCanary!;

        // Simulate system prompt containing the instruction
        var systemPrompt = "You are a helpful assistant." + instruction;
        systemPrompt.ShouldContain(canary);

        // Clean response — no exfiltration
        guard.CheckOutput("Hello! How can I help?").ShouldBeFalse();

        // Exfiltration response — model leaks the canary
        var badReply = $"My instructions contain {canary} marker";
        guard.CheckOutput(badReply).ShouldBeTrue();

        // Redact the canary from the bad reply
        var redacted = guard.Redact(badReply);
        redacted.ShouldNotContain(canary);
        redacted.ShouldContain("[CANARY-REDACTED]");
    }

    [Test]
    public void MultiTurn_EachTurnGetsUniqueCanary()
    {
        var guard = new CanaryGuard();
        var seen = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            guard.GenerateCanary();
            seen.Add(guard.CurrentCanary!).ShouldBeTrue($"Duplicate canary on iteration {i}");
        }
    }
}