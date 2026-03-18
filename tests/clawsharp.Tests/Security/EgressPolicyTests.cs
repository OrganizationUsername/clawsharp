using Clawsharp.Config.Security;
using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

public sealed class EgressPolicyTests
{
    [TearDown]
    public void ResetEgressConfig()
    {
        // Reset to null (open mode) after each test to avoid cross-test contamination.
        SsrfGuard.Configure(null);
    }

    // ── Null config (no egress section) → everything allowed ─────────

    [Test]
    public void CheckEgressPolicy_NullConfig_ReturnsNull()
    {
        SsrfGuard.Configure(null);

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldBeNull();
    }

    // ── Open mode → everything allowed ──────────────────────────────

    [Test]
    public void CheckEgressPolicy_OpenMode_ReturnsNull()
    {
        SsrfGuard.Configure(new EgressConfig { Mode = EgressMode.Open });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://anything.example.com/path"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_OpenModeWithRules_StillAllowsAll()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Open,
            Rules = [new EgressRule { Host = "only-this.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://other.com/"));

        result.ShouldBeNull();
    }

    // ── Allowlist mode — exact match ─────────────────────────────────

    [Test]
    public void CheckEgressPolicy_Allowlist_ExactMatch_Allows()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/v1/chat"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_ExactMatch_CaseInsensitive()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "API.EXAMPLE.COM" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_ExactMatch_MismatchBlocks()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://evil.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── Allowlist mode — wildcard matching ───────────────────────────

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_MatchesSubdomain()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_MatchesDeepSubdomain()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://deep.sub.example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_MatchesBareDomain()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_DoesNotMatchDifferentDomain()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://notexample.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_DoesNotMatchPartialSuffix()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com" }],
        });

        // "badexample.com" ends with "example.com" but is NOT a subdomain.
        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://badexample.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_Wildcard_CaseInsensitive()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.EXAMPLE.COM" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldBeNull();
    }

    // ── Port restriction ─────────────────────────────────────────────

    [Test]
    public void CheckEgressPolicy_Allowlist_PortMatch_Allows()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = 443 }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_PortMismatch_Blocks()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = 8080 }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_NullPort_AnyPortAllowed()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = null }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("http://api.example.com:9999/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_ZeroPort_AnyPortAllowed()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = 0 }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("http://api.example.com:8080/"));

        result.ShouldBeNull();
    }

    // ── Empty rules in allowlist mode → blocks everything ────────────

    [Test]
    public void CheckEgressPolicy_Allowlist_EmptyRules_BlocksEverything()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
        result.ShouldContain("no rules");
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_NullRules_BlocksEverything()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = null,
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
        result.ShouldContain("no rules");
    }

    // ── Multiple rules — first match wins ────────────────────────────

    [Test]
    public void CheckEgressPolicy_Allowlist_MultipleRules_MatchesSecond()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules =
            [
                new EgressRule { Host = "first.com" },
                new EgressRule { Host = "second.com" },
            ],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://second.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Allowlist_MultipleRules_NoneMatch_Blocks()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules =
            [
                new EgressRule { Host = "first.com" },
                new EgressRule { Host = "second.com" },
            ],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://third.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── Host+port overload (used by CreateConnectCallback) ───────────

    [Test]
    public void CheckEgressPolicy_HostPort_Overload_Allows()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = 443 }],
        });

        var result = SsrfGuard.CheckEgressPolicy("api.example.com", 443);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_HostPort_Overload_Blocks()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "api.example.com", Port = 443 }],
        });

        var result = SsrfGuard.CheckEgressPolicy("evil.com", 443);

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── CheckAsync integration ───────────────────────────────────────

    [Test]
    public async Task CheckAsync_AllowlistMode_BlockedHost_ReturnsEgressError()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "only-this.example.com" }],
        });

        // Use a public IP to ensure SSRF checks pass but egress blocks.
        var result = await SsrfGuard.CheckAsync(new Uri("https://8.8.8.8/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    [Test]
    public async Task CheckAsync_AllowlistMode_AllowedHost_ReturnsNull()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "8.8.8.8" }],
        });

        var result = await SsrfGuard.CheckAsync(new Uri("https://8.8.8.8/"));

        result.ShouldBeNull();
    }

    [Test]
    public async Task CheckAsync_NullConfig_StillAllowsPublicHosts()
    {
        SsrfGuard.Configure(null);

        var result = await SsrfGuard.CheckAsync(new Uri("https://8.8.8.8/"));

        result.ShouldBeNull();
    }

    // ── Wildcard with port combination ───────────────────────────────

    [Test]
    public void CheckEgressPolicy_Wildcard_WithPort_MatchesSubdomainAndPort()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com", Port = 443 }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://api.example.com/"));

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_Wildcard_WithPort_WrongPort_Blocks()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*.example.com", Port = 443 }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("http://api.example.com:8080/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── Error message content ────────────────────────────────────────

    [Test]
    public void CheckEgressPolicy_BlockedMessage_ContainsHostName()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "allowed.com" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://blocked-host.example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("blocked-host.example.com");
    }

    // ── Whitespace in host creates dead rule ──────────────────────────

    [Test]
    public void CheckEgressPolicy_WhitespaceHost_DoesNotMatch()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = " example.com " }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── Bare wildcard "*" host matches nothing ────────────────────────

    [Test]
    public void CheckEgressPolicy_BareWildcard_DoesNotMatch()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "*" }],
        });

        var result = SsrfGuard.CheckEgressPolicy(new Uri("https://example.com/"));

        result.ShouldNotBeNull();
        result.ShouldContain("[Egress] Blocked");
    }

    // ── Port boundary values ──────────────────────────────────────────

    [Test]
    public void CheckEgressPolicy_Port65535_Matches()
    {
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "example.com", Port = 65535 }],
        });

        var result = SsrfGuard.CheckEgressPolicy("example.com", 65535);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckEgressPolicy_NegativePort_TreatedAsAnyPort()
    {
        // Negative port is invalid config (caught by validator) but at runtime
        // it fails the `> 0` check and acts as "any port" — permissive fail-open
        SsrfGuard.Configure(new EgressConfig
        {
            Mode = EgressMode.Allowlist,
            Rules = [new EgressRule { Host = "example.com", Port = -1 }],
        });

        var result = SsrfGuard.CheckEgressPolicy("example.com", 443);

        result.ShouldBeNull(); // matches because -1 is not > 0, so port check is skipped
    }
}
