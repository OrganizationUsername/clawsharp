using System.Net;
using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

public sealed class SsrfGuardTests
{
    // ── IPv4 private/reserved (should be blocked) ────────────────────

    [TestCase("127.0.0.1", TestName = "Loopback")]
    [TestCase("127.0.0.2", TestName = "Loopback_Range")]
    [TestCase("10.0.0.1", TestName = "RFC1918_10_Low")]
    [TestCase("10.255.255.255", TestName = "RFC1918_10_High")]
    [TestCase("172.16.0.1", TestName = "RFC1918_172_Low")]
    [TestCase("172.31.255.255", TestName = "RFC1918_172_High")]
    [TestCase("192.168.0.1", TestName = "RFC1918_192_Low")]
    [TestCase("192.168.255.255", TestName = "RFC1918_192_High")]
    [TestCase("169.254.1.1", TestName = "LinkLocal")]
    [TestCase("0.0.0.0", TestName = "Unspecified")]
    [TestCase("255.255.255.255", TestName = "Broadcast")]
    [TestCase("224.0.0.1", TestName = "Multicast")]
    [TestCase("239.255.255.255", TestName = "Multicast_High")]
    [TestCase("100.64.0.1", TestName = "CGNAT_Low")]
    [TestCase("100.127.255.255", TestName = "CGNAT_High")]
    [TestCase("240.0.0.1", TestName = "Reserved_240")]
    [TestCase("255.0.0.0", TestName = "Reserved_255")]
    [TestCase("192.0.2.1", TestName = "TestNet1")]
    [TestCase("198.51.100.1", TestName = "TestNet2")]
    [TestCase("203.0.113.1", TestName = "TestNet3")]
    [TestCase("198.18.0.1", TestName = "Benchmarking_18")]
    [TestCase("198.19.0.1", TestName = "Benchmarking_19")]
    [TestCase("192.0.0.1", TestName = "IETF_Protocol_Assignments")]
    public void IsPrivateOrReservedAddress_IPv4Private_ReturnsTrue(string address)
    {
        var ip = IPAddress.Parse(address);

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue($"Expected {address} to be blocked as private/reserved");
    }

    // ── IPv6 private/reserved (should be blocked) ────────────────────

    [TestCase("::1", TestName = "IPv6_Loopback")]
    [TestCase("::", TestName = "IPv6_Unspecified")]
    [TestCase("fc00::1", TestName = "IPv6_ULA_fc00")]
    [TestCase("fd00::1", TestName = "IPv6_ULA_fd00")]
    [TestCase("fe80::1", TestName = "IPv6_LinkLocal")]
    [TestCase("ff02::1", TestName = "IPv6_Multicast")]
    [TestCase("ff00::1", TestName = "IPv6_Multicast_ff00")]
    [TestCase("2001:db8::1", TestName = "IPv6_Documentation")]
    [TestCase("2001::1", TestName = "IPv6_Teredo")]
    [TestCase("2002::1", TestName = "IPv6_6to4")]
    public void IsPrivateOrReservedAddress_IPv6Private_ReturnsTrue(string address)
    {
        var ip = IPAddress.Parse(address);

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue($"Expected {address} to be blocked as private/reserved");
    }

    // ── IPv4-mapped IPv6 normalization (should be blocked) ───────────

    [TestCase("::ffff:127.0.0.1", TestName = "Mapped_Loopback")]
    [TestCase("::ffff:10.0.0.1", TestName = "Mapped_RFC1918_10")]
    [TestCase("::ffff:192.168.1.1", TestName = "Mapped_RFC1918_192")]
    [TestCase("::ffff:169.254.169.254", TestName = "Mapped_CloudMetadata")]
    [TestCase("::ffff:172.16.0.1", TestName = "Mapped_RFC1918_172")]
    [TestCase("::ffff:0.0.0.0", TestName = "Mapped_Unspecified")]
    [TestCase("::ffff:240.0.0.1", TestName = "Mapped_Reserved")]
    public void IsPrivateOrReservedAddress_IPv4MappedIPv6_ReturnsTrue(string address)
    {
        var ip = IPAddress.Parse(address);

        // Verify the address is indeed IPv4-mapped IPv6 before testing.
        ip.IsIPv4MappedToIPv6.ShouldBeTrue($"Expected {address} to be IPv4-mapped IPv6");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeTrue($"Expected IPv4-mapped IPv6 {address} to be blocked after MapToIPv4()");
    }

    // ── Public addresses (should NOT be blocked) ─────────────────────

    [TestCase("8.8.8.8", TestName = "GoogleDns")]
    [TestCase("1.1.1.1", TestName = "CloudflareDns")]
    [TestCase("142.250.80.46", TestName = "Google")]
    [TestCase("104.16.132.229", TestName = "Cloudflare")]
    [TestCase("13.107.42.14", TestName = "Microsoft")]
    [TestCase("151.101.1.140", TestName = "Reddit")]
    public void IsPrivateOrReservedAddress_PublicIPv4_ReturnsFalse(string address)
    {
        var ip = IPAddress.Parse(address);

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse($"Expected public address {address} to be allowed");
    }

    [TestCase("2607:f8b0:4004:800::200e", TestName = "GoogleIPv6")]
    [TestCase("2606:4700::6810:84e5", TestName = "CloudflareIPv6")]
    public void IsPrivateOrReservedAddress_PublicIPv6_ReturnsFalse(string address)
    {
        var ip = IPAddress.Parse(address);

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse($"Expected public IPv6 address {address} to be allowed");
    }

    // ── IPv4 boundary validation ─────────────────────────────────────

    [Test]
    public void IsPrivateOrReservedAddress_RFC1918_172_BelowRange_NotBlocked()
    {
        // 172.15.x.x is NOT in the 172.16-31 range
        var ip = IPAddress.Parse("172.15.255.255");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("172.15.x.x is outside the RFC-1918 172.16-31 range");
    }

    [Test]
    public void IsPrivateOrReservedAddress_RFC1918_172_AboveRange_NotBlocked()
    {
        // 172.32.x.x is NOT in the 172.16-31 range
        var ip = IPAddress.Parse("172.32.0.1");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("172.32.x.x is outside the RFC-1918 172.16-31 range");
    }

    [Test]
    public void IsPrivateOrReservedAddress_CGNAT_BelowRange_NotBlocked()
    {
        // 100.63.x.x is below the CGNAT range (100.64-127)
        var ip = IPAddress.Parse("100.63.255.255");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("100.63.x.x is outside the CGNAT 100.64-127 range");
    }

    [Test]
    public void IsPrivateOrReservedAddress_CGNAT_AboveRange_NotBlocked()
    {
        // 100.128.x.x is above the CGNAT range
        var ip = IPAddress.Parse("100.128.0.1");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("100.128.x.x is outside the CGNAT 100.64-127 range");
    }

    [Test]
    public void IsPrivateOrReservedAddress_Multicast_BelowRange_NotBlocked()
    {
        // 223.x.x.x is NOT multicast (224-239)
        var ip = IPAddress.Parse("223.255.255.255");

        var result = SsrfGuard.IsPrivateOrReservedAddress(ip);

        result.ShouldBeFalse("223.x.x.x is below the multicast range");
    }

    // ── CheckAsync — scheme validation ───────────────────────────────

    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/file")]
    [TestCase("gopher://example.com")]
    [TestCase("data:text/html,<h1>hi</h1>")]
    [TestCase("dict://example.com")]
    [TestCase("sftp://example.com")]
    [TestCase("ldap://example.com")]
    [TestCase("ldaps://example.com")]
    public async Task CheckAsync_BlockedScheme_ReturnsError(string url)
    {
        var uri = new Uri(url);

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull();
        result.ShouldContain("[SSRF] Blocked");
        result.ShouldContain("scheme");
    }

    [Test]
    public async Task CheckAsync_HttpScheme_NotBlockedBySchemeCheck()
    {
        // Use a known-public host to avoid DNS resolution issues.
        var uri = new Uri("http://8.8.8.8/");

        var result = await SsrfGuard.CheckAsync(uri);

        // Should not be blocked by scheme check (may still be null or pass).
        if (result is not null)
        {
            result.ShouldNotContain("scheme");
        }
    }

    [Test]
    public async Task CheckAsync_HttpsScheme_NotBlockedBySchemeCheck()
    {
        var uri = new Uri("https://8.8.8.8/");

        var result = await SsrfGuard.CheckAsync(uri);

        if (result is not null)
        {
            result.ShouldNotContain("scheme");
        }
    }

    // ── CheckAsync — userinfo rejection ──────────────────────────────

    [Test]
    public async Task CheckAsync_UrlWithUserinfo_ReturnsBlocked()
    {
        var uri = new Uri("http://user:pass@example.com/");

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull();
        result.ShouldContain("[SSRF] Blocked");
        result.ShouldContain("userinfo");
    }

    // ── CheckAsync — cloud metadata hosts ────────────────────────────

    [TestCase("http://169.254.169.254/latest/meta-data/", TestName = "AWS_GCP_Azure_Metadata")]
    [TestCase("http://metadata.google.internal/", TestName = "GCP_Metadata")]
    [TestCase("http://metadata.azure.internal/", TestName = "Azure_Metadata")]
    [TestCase("http://169.254.170.2/", TestName = "ECS_Task_Metadata")]
    public async Task CheckAsync_CloudMetadataHost_ReturnsBlocked(string url)
    {
        var uri = new Uri(url);

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull();
        result.ShouldContain("[SSRF] Blocked");
    }

    // ── CheckAsync — local hostname check ────────────────────────────

    [TestCase("http://localhost/", TestName = "Localhost")]
    [TestCase("http://something.localhost/", TestName = "SubdomainOfLocalhost")]
    [TestCase("http://myhost.local/", TestName = "DotLocal")]
    public async Task CheckAsync_LocalHostname_ReturnsBlocked(string url)
    {
        var uri = new Uri(url);

        var result = await SsrfGuard.CheckAsync(uri);

        result.ShouldNotBeNull();
        result.ShouldContain("[SSRF] Blocked");
    }

    // ── CheckDomainAllowlist ─────────────────────────────────────────

    [Test]
    public void CheckDomainAllowlist_NullList_AllowsAll()
    {
        var uri = new Uri("https://anything.example.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, null);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckDomainAllowlist_EmptyList_DeniesAll()
    {
        var uri = new Uri("https://example.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, []);

        result.ShouldNotBeNull();
        result.ShouldContain("[Domain] Blocked");
    }

    [Test]
    public void CheckDomainAllowlist_WildcardList_AllowsAll()
    {
        var uri = new Uri("https://anything.example.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["*"]);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckDomainAllowlist_ExactMatch_Allows()
    {
        var uri = new Uri("https://example.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com"]);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckDomainAllowlist_SubdomainMatch_Allows()
    {
        var uri = new Uri("https://api.example.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com"]);

        result.ShouldBeNull();
    }

    [Test]
    public void CheckDomainAllowlist_NonMatchingDomain_Blocks()
    {
        var uri = new Uri("https://evil.com/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com", "trusted.org"]);

        result.ShouldNotBeNull();
        result.ShouldContain("[Domain] Blocked");
        result.ShouldContain("evil.com");
    }

    [Test]
    public void CheckDomainAllowlist_CaseInsensitive_Allows()
    {
        var uri = new Uri("https://API.EXAMPLE.COM/path");

        var result = SsrfGuard.CheckDomainAllowlist(uri, ["example.com"]);

        result.ShouldBeNull();
    }
}
