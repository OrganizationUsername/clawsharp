using System.Net;
using System.Net.Sockets;
using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class SsrfGuardEdgeCaseTests
{
    // ── HIGH-1: Decimal/octal/hex IP encoding bypass ──────────────────

    [Test]
    public async Task CheckAsync_DecimalEncodedLoopback_BlocksPrivateAddress()
    {
        // 2130706433 = 127.0.0.1 in decimal notation.
        // .NET's Uri class normalizes http://2130706433/ to http://127.0.0.1/
        // so CheckAsync correctly blocks it via DNS resolution to loopback.
        var uri = new Uri("http://2130706433/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Decimal-encoded 127.0.0.1 (2130706433) should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task CheckAsync_HexEncodedLoopback_BlocksPrivateAddress()
    {
        // 0x7f000001 = 127.0.0.1 in hex notation.
        // .NET's Uri class normalizes http://0x7f000001/ to http://127.0.0.1/
        var uri = new Uri("http://0x7f000001/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Hex-encoded 127.0.0.1 (0x7f000001) should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task CheckAsync_OctalEncodedLoopback_BlocksPrivateAddress()
    {
        // 0177.0.0.1 = 127.0.0.1 in octal notation.
        // .NET's Uri class normalizes http://0177.0.0.1/ to http://127.0.0.1/
        var uri = new Uri("http://0177.0.0.1/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Octal-encoded 127.0.0.1 (0177.0.0.1) should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task CheckAsync_DecimalEncodedMetadata_BlocksPrivateAddress()
    {
        // 2852039166 = 169.254.169.254 in decimal notation (AWS/GCP metadata).
        var uri = new Uri("http://2852039166/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Decimal-encoded 169.254.169.254 (2852039166) should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task CheckAsync_HexEncodedRFC1918_BlocksPrivateAddress()
    {
        // 0x0a000001 = 10.0.0.1 in hex notation.
        var uri = new Uri("http://0x0a000001/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Hex-encoded 10.0.0.1 (0x0a000001) should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    // ── HIGH-9: CreateConnectCallback ─────────────────────────────────
    //
    // SocketsHttpConnectionContext has no public constructor (sealed, internal ctor).
    // We test the callback by assigning it to a real SocketsHttpHandler and making
    // HTTP requests that exercise the DNS-rebinding defense path.

    [Test]
    public void CreateConnectCallback_ReturnsNonNullCallback()
    {
        var callback = SsrfGuard.CreateConnectCallback();

        callback.ShouldNotBeNull();
    }

    [Test]
    public void CreateConnectCallback_CanBeAssignedToSocketsHttpHandler()
    {
        // Verify the callback signature is compatible with SocketsHttpHandler.
        var callback = SsrfGuard.CreateConnectCallback();
        using var handler = new SocketsHttpHandler();

        handler.ConnectCallback = callback;

        handler.ConnectCallback.ShouldNotBeNull();
    }

    [Test]
    public async Task CreateConnectCallback_PrivateIP_BlocksConnection()
    {
        // When the connect callback is active, requests to private IPs should fail
        // with an HttpRequestException containing "SSRF blocked".
        // localhost resolves to 127.0.0.1 which is private.
        var callback = SsrfGuard.CreateConnectCallback();
        using var handler = new SocketsHttpHandler { ConnectCallback = callback };
        using var client = new HttpClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync("http://localhost:1/"));

        ex.Message.ShouldContain("SSRF blocked");
    }

    [Test]
    public async Task CreateConnectCallback_LoopbackIP_BlocksConnection()
    {
        // Direct IP 127.0.0.1 should be blocked by the connect callback.
        var callback = SsrfGuard.CreateConnectCallback();
        using var handler = new SocketsHttpHandler { ConnectCallback = callback };
        using var client = new HttpClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync("http://127.0.0.1:1/"));

        ex.Message.ShouldContain("SSRF blocked");
    }

    [Test]
    public async Task CreateConnectCallback_NonExistentHost_ThrowsHttpRequestException()
    {
        // A host that cannot be resolved should throw with a DNS failure message.
        var callback = SsrfGuard.CreateConnectCallback();
        using var handler = new SocketsHttpHandler { ConnectCallback = callback };
        using var client = new HttpClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync("http://this-domain-does-not-exist-ssrf-test.invalid:1/"));

        ex.Message.ShouldContain("SSRF blocked");
    }

    [Test]
    public async Task CreateConnectCallback_RFC1918Address_BlocksConnection()
    {
        // 10.0.0.1 is RFC-1918 private and should be blocked.
        var callback = SsrfGuard.CreateConnectCallback();
        using var handler = new SocketsHttpHandler { ConnectCallback = callback };
        using var client = new HttpClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync("http://10.0.0.1:1/"));

        ex.Message.ShouldContain("SSRF blocked");
    }

    // ── MED-1: IPv6 zone ID / scoped address ─────────────────────────

    [Test]
    public void IsPrivateOrReservedAddress_LinkLocalIPv6_ReturnsTrue()
    {
        // fe80::1 is link-local (fe80::/10) and should be blocked.
        // Zone IDs (e.g., fe80::1%eth0) don't survive IPAddress.Parse in .NET —
        // .NET strips the zone or throws on invalid scope. The underlying IP
        // fe80::1 is what matters and it should be blocked.
        var ip = IPAddress.Parse("fe80::1");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fe80::1 (link-local) should be blocked");
    }

    [Test]
    public void IsPrivateOrReservedAddress_LinkLocalIPv6_HighAddress_ReturnsTrue()
    {
        // fe80::ffff:ffff:ffff:ffff is still in fe80::/10.
        var ip = IPAddress.Parse("fe80::ffff:ffff:ffff:ffff");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fe80::ffff:ffff:ffff:ffff (link-local) should be blocked");
    }

    [Test]
    public void IsPrivateOrReservedAddress_LinkLocalIPv6_WithScopeId_ReturnsTrue()
    {
        // .NET IPAddress supports scope IDs via the ScopeId property.
        // fe80::1%25 (URL-encoded zone ID) — the IP itself is still link-local.
        var ip = IPAddress.Parse("fe80::1");
        // Manually set a scope ID to simulate a scoped address.
        ip.ScopeId = 1;

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fe80::1 with scope ID should still be blocked as link-local");
    }

    [Test]
    public async Task CheckAsync_LinkLocalIPv6InUrl_ReturnsBlocked()
    {
        // http://[fe80::1]/ should be blocked because fe80::1 is link-local.
        // Zone IDs in URLs use %25-encoding: http://[fe80::1%25eth0]/
        // but .NET Uri parsing may reject the zone ID entirely.
        var uri = new Uri("http://[fe80::1]/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("Link-local IPv6 address in URL should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    // ── MED-2: Domain allowlist trailing dot ──────────────────────────

    [Test]
    public void CheckDomainAllowlist_TrailingDot_KnownLimitation_MayNotMatchWithoutDot()
    {
        // DNS FQDN form uses a trailing dot: "example.com."
        // .NET's Uri class strips the trailing dot from Uri.Host, so
        // "http://example.com./" becomes host "example.com" which matches normally.
        // This test documents that .NET's Uri normalization handles this case.
        var uri = new Uri("http://example.com./");

        // .NET Uri strips the trailing dot from the host.
        var host = uri.Host;

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com"]);

        // If .NET stripped the trailing dot, it should match.
        // If .NET preserved it, it would NOT match (known limitation).
        if (host == "example.com")
        {
            result.ShouldBeNull("Trailing dot stripped by Uri, matches allowlist normally");
        }
        else
        {
            // Document this as a known limitation: trailing-dot domain
            // does not match non-trailing-dot allowlist entry.
            result.ShouldNotBeNull("Trailing dot preserved — known limitation: does not match allowlist");
            result.ShouldContain("[Domain] Blocked");
        }
    }

    [Test]
    public void CheckDomainAllowlist_TrailingDotInAllowlist_KnownLimitation()
    {
        // What if the allowlist itself has the trailing dot?
        var uri = new Uri("http://example.com/");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com."]);

        // "example.com" does NOT equal "example.com." and does NOT end with ".example.com."
        // so this should be blocked — documenting the asymmetry.
        result.ShouldNotBeNull("Allowlist with trailing dot should not match host without trailing dot");
    }

    // ── MED-3: Userinfo edge cases ────────────────────────────────────

    [Test]
    public async Task CheckAsync_EmptyUserinfo_WithAt_BlocksOrParses()
    {
        // http://@evil.com/ — empty userinfo but @ present.
        // .NET Uri may strip the empty userinfo or preserve it.
        Uri uri;
        try
        {
            uri = new Uri("http://@evil.com/");
        }
        catch (UriFormatException)
        {
            // If .NET rejects this URL format entirely, the SSRF vector is closed.
            Assert.Pass("Uri constructor rejects 'http://@evil.com/' — SSRF vector closed at parse time");
            return;
        }

        var result = await SsrfGuard.CheckAsync(uri);

        // If Uri parsed it and preserved userinfo, it should be blocked.
        // If Uri stripped the @, it's just evil.com and proceeds to DNS check.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            result.ShouldNotBeNull("URL with @ should trigger userinfo block");
            result.ShouldContain("userinfo");
        }
        // Otherwise DNS resolution determines the result — not a userinfo concern.
    }

    [Test]
    public async Task CheckAsync_DoubleAt_UserinfoConfusion_Blocks()
    {
        // http://user@evil.com@safe.com/ — double @ URL confusion attack.
        // The intent is to make the guard think the host is safe.com
        // while the actual connection goes to evil.com.
        Uri uri;
        try
        {
            uri = new Uri("http://user@evil.com@safe.com/");
        }
        catch (UriFormatException)
        {
            // If .NET rejects the double-@ URL, the attack vector is closed.
            Assert.Pass("Uri constructor rejects double-@ URL — SSRF vector closed at parse time");
            return;
        }

        // .NET should parse the first @ as userinfo separator.
        // Whatever .NET decides the host is, verify the guard still works.
        var result = await SsrfGuard.CheckAsync(uri);

        // If userinfo is non-empty, it should be blocked.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            result.ShouldNotBeNull("Double-@ URL should trigger userinfo block");
            result.ShouldContain("userinfo");
        }
    }

    [Test]
    public async Task CheckAsync_UserinfoWithEncodedAt_Blocks()
    {
        // http://user%40attacker.com@safe.com/ — encoded @ in userinfo.
        Uri uri;
        try
        {
            uri = new Uri("http://user%40attacker.com@safe.com/");
        }
        catch (UriFormatException)
        {
            Assert.Pass("Uri constructor rejects encoded-@ URL — attack vector closed at parse time");
            return;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var result = await SsrfGuard.CheckAsync(uri);

            result.ShouldNotBeNull("URL with encoded @ in userinfo should be blocked");
            result.ShouldContain("userinfo");
        }
    }

    // ── LOW-5: fd00:ec2::254 as ULA range IP ─────────────────────────

    [Test]
    public void IsPrivateOrReservedAddress_AwsIpv6Metadata_ReturnsTrue()
    {
        // fd00:ec2::254 is in the fc00::/7 ULA range (fd00::/8 subset).
        // This is also the AWS IPv6 metadata endpoint.
        var ip = IPAddress.Parse("fd00:ec2::254");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fd00:ec2::254 is in ULA range (fc00::/7) and should be blocked");
    }

    [Test]
    public void IsPrivateOrReservedAddress_ULA_fd_AllOnes_ReturnsTrue()
    {
        // fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff — top of ULA range.
        var ip = IPAddress.Parse("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fdff::...ffff is ULA (fc00::/7) and should be blocked");
    }

    [Test]
    public void IsPrivateOrReservedAddress_ULA_fc00_ReturnsTrue()
    {
        // fc00::1 — bottom of ULA range.
        var ip = IPAddress.Parse("fc00::1");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue("fc00::1 is ULA (fc00::/7) and should be blocked");
    }

    [Test]
    public async Task CheckAsync_AwsIpv6MetadataEndpoint_Blocked()
    {
        // fd00:ec2::254 is both a ULA address and listed in CloudMetadataHosts.
        // The URL form uses brackets for IPv6: http://[fd00:ec2::254]/
        var uri = new Uri("http://[fd00:ec2::254]/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("AWS IPv6 metadata endpoint should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    // ── LOW-6: Cloud metadata subdomain non-match ─────────────────────

    [Test]
    public async Task CheckAsync_CloudMetadataSubdomain_NotBlockedAsMetadata()
    {
        // metadata.google.internal.evil.com is NOT metadata.google.internal.
        // The cloud metadata check uses exact match (FrozenSet.Contains), so
        // subdomains of metadata hosts should NOT be blocked by the metadata check.
        // They may still be blocked by DNS resolution failure or private IPs.
        var uri = new Uri("http://metadata.google.internal.evil.com/");

        var result = await SsrfGuard.CheckAsync(uri);

        // The result depends on DNS resolution of evil.com — but the key assertion
        // is that it is NOT blocked with a "cloud metadata endpoint" message.
        if (result is not null)
        {
            result.ShouldNotContain("cloud metadata endpoint");
        }
    }

    [Test]
    public async Task CheckAsync_MetadataAzureInternalSubdomain_NotBlockedAsMetadata()
    {
        // metadata.azure.internal.attacker.com should not be treated as
        // metadata.azure.internal by the cloud metadata check.
        var uri = new Uri("http://metadata.azure.internal.attacker.com/");

        var result = await SsrfGuard.CheckAsync(uri);

        if (result is not null)
        {
            result.ShouldNotContain("cloud metadata endpoint");
        }
    }

    [Test]
    public async Task CheckAsync_ExactCloudMetadataHost_IsBlocked()
    {
        // Verify that the exact metadata host IS still blocked (regression guard).
        var uri = new Uri("http://metadata.google.internal/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull();
        result.ShouldContain("cloud metadata endpoint");
    }

    // ── Additional edge cases discovered during analysis ──────────────

    [Test]
    public void IsPrivateOrReservedAddress_JustOutsideULA_NotBlocked()
    {
        // fbff::1 — just below fc00::/7 range. Should NOT be blocked as ULA.
        // fc00::/7 means first 7 bits = 1111110, so fc00-fdff are ULA.
        // fb00 = 11111011 — first 7 bits = 1111101 which is NOT 1111110.
        var ip = IPAddress.Parse("fbff::1");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("fbff::1 is outside the fc00::/7 ULA range");
    }

    [Test]
    public async Task CheckAsync_IPv6LoopbackInUrl_ReturnsBlocked()
    {
        // http://[::1]/ — IPv6 loopback in URL form.
        var uri = new Uri("http://[::1]/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull("IPv6 loopback [::1] in URL should be blocked");
        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task CheckAsync_CancellationToken_Respected()
    {
        // Passing an already-cancelled token should result in OperationCanceledException
        // (not a silent pass or SSRF bypass).
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var uri = new Uri("http://example.com/");

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await SsrfGuard.CheckAsync(uri, cts.Token));
    }
}
