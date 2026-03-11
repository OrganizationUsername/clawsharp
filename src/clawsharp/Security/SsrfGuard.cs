using System.Collections.Frozen;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Clawsharp.Security;

/// <summary>
///     Centralized SSRF (Server-Side Request Forgery) protection.
///     Validates URIs before outbound HTTP connections are made.
///
///     IMPORTANT: <see cref="CheckAsync"/> alone is subject to DNS rebinding (TOCTOU).
///     Use <see cref="CreateConnectCallback"/> on any <see cref="SocketsHttpHandler"/>
///     to re-validate resolved IPs at TCP connect time, eliminating the rebinding gap.
/// </summary>
public static class SsrfGuard
{
    private static readonly FrozenSet<string> BlockedSchemes = FrozenSet.ToFrozenSet(
        ["file", "ftp", "gopher", "data", "dict", "sftp", "ldap", "ldaps"], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CloudMetadataHosts = FrozenSet.ToFrozenSet(
    [
        "169.254.169.254", // AWS/GCP/Azure/DO metadata
        "metadata.google.internal", // GCP metadata
        "metadata.azure.internal", // Azure metadata
        "169.254.170.2", // ECS task metadata
        "fd00:ec2::254", // AWS IPv6 metadata
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] LocalHostnamePatterns = ["localhost", ".localhost", ".local"];

    /// <summary>
    ///     Fully validate a URI for SSRF. Performs scheme check, userinfo rejection,
    ///     hostname check (cloud metadata, local hostnames), and DNS resolution + IP check.
    ///     Returns an error message if blocked, or null if safe to proceed.
    /// </summary>
    public static async Task<string?> CheckAsync(Uri uri, CancellationToken ct = default)
    {
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return $"[SSRF] Blocked: scheme '{uri.Scheme}' is not allowed. Only http/https are permitted.";
        }

        // Userinfo rejection (http://user@host/)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "[SSRF] Blocked: URL userinfo (@username) is not allowed.";
        }

        var host = uri.Host;

        // Cloud metadata hostname check
        if (IsCloudMetadataHost(host))
        {
            return $"[SSRF] Blocked: host '{host}' is a cloud metadata endpoint.";
        }

        // Local hostname check
        if (IsLocalHostname(host))
        {
            return $"[SSRF] Blocked: host '{host}' resolves to a local address.";
        }

        // DNS resolution + IP check (checks ALL returned addresses)
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return $"[SSRF] Blocked: host '{host}' did not resolve to any address.";
            }

            foreach (var ip in addresses)
            {
                if (IsPrivateOrReservedAddress(ip))
                {
                    return $"[SSRF] Blocked: host '{host}' resolves to private/reserved address {ip}.";
                }
            }
        }
        catch (SocketException)
        {
            return $"[SSRF] Blocked: host '{host}' could not be resolved.";
        }

        return null; // safe
    }

    /// <summary>
    ///     Creates a <see cref="SocketsHttpHandler.ConnectCallback"/> that re-validates
    ///     resolved IP addresses against the private/reserved blocklist at TCP connect time.
    ///     This eliminates the DNS rebinding (TOCTOU) gap between <see cref="CheckAsync"/>
    ///     and the actual HTTP connection.
    /// </summary>
    /// <returns>
    ///     A callback suitable for <see cref="SocketsHttpHandler.ConnectCallback"/>
    ///     that throws <see cref="HttpRequestException"/> if the resolved IP is blocked.
    /// </returns>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback()
    {
        return static async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            // Resolve DNS ourselves so we can inspect each IP before connecting.
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"SSRF blocked: DNS resolution failed for '{host}'.", ex);
            }

            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"SSRF blocked: host '{host}' did not resolve to any address.");
            }

            // Try each resolved address until one connects (mirrors default .NET behavior)
            // but reject any private/reserved IP before attempting the connection.
            Socket? socket = null;
            Exception? lastException = null;

            foreach (var ip in addresses)
            {
                if (IsPrivateOrReservedAddress(ip))
                {
                    throw new HttpRequestException($"SSRF blocked: connection to private address {ip} refused.");
                }

                socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, port), ct).ConfigureAwait(false);
                    // Connected successfully — return the stream.
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    lastException = ex;
                }
            }

            // All addresses failed to connect.
            throw new HttpRequestException(
                $"SSRF blocked or connection failed: could not connect to any address for '{host}'.",
                lastException);
        };
    }

    /// <summary>Check if an IP address is private, reserved, or non-routable.</summary>
    public static bool IsPrivateOrReservedAddress(IPAddress ip)
    {
        // Normalize IPv4-mapped IPv6 (::ffff:10.0.0.1 -> 10.0.0.1)
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrReservedV4(ip.MapToIPv4());
            }

            return IsPrivateOrReservedV6(ip);
        }

        return IsPrivateOrReservedV4(ip);
    }

    private static bool IsPrivateOrReservedV4(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        int a = bytes[0], b = bytes[1], c = bytes[2];

        return ip.Equals(IPAddress.Loopback) // 127.0.0.1
               || a == 127 // 127.0.0.0/8 loopback
               || (a == 10) // 10.0.0.0/8 RFC-1918
               || (a == 172 && b >= 16 && b <= 31) // 172.16.0.0/12 RFC-1918
               || (a == 192 && b == 168) // 192.168.0.0/16 RFC-1918
               || (a == 169 && b == 254) // 169.254.0.0/16 link-local
               || a == 0 // 0.0.0.0/8 unspecified
               || ip.Equals(IPAddress.Broadcast) // 255.255.255.255
               || (a >= 224 && a <= 239) // 224.0.0.0/4 multicast
               || (a == 100 && b >= 64 && b <= 127) // 100.64.0.0/10 CGNAT
               || a >= 240 // 240.0.0.0/4 reserved
               || (a == 192 && b == 0 && (c == 0 || c == 2)) // 192.0.0.0/24, 192.0.2.0/24
               || (a == 198 && b == 51 && c == 100) // 198.51.100.0/24 TEST-NET-2
               || (a == 203 && b == 0 && c == 113) // 203.0.113.0/24 TEST-NET-3
               || (a == 198 && (b == 18 || b == 19)); // 198.18.0.0/15 benchmarking
    }

    private static bool IsPrivateOrReservedV6(IPAddress ip)
    {
        if (ip.Equals(IPAddress.IPv6Loopback))
        {
            return true; // ::1
        }

        if (ip.Equals(IPAddress.IPv6Any))
        {
            return true; // ::
        }

        var bytes = ip.GetAddressBytes();
        int seg0 = (bytes[0] << 8) | bytes[1];
        int seg1 = (bytes[2] << 8) | bytes[3];

        return (seg0 & 0xff00) == 0xff00 // ff00::/8 multicast
               || (seg0 & 0xfe00) == 0xfc00 // fc00::/7 ULA
               || (seg0 & 0xffc0) == 0xfe80 // fe80::/10 link-local
               || (seg0 == 0x2001 && seg1 == 0x0db8) // 2001:db8::/32 documentation
               || (seg0 == 0x2001 && seg1 == 0x0000) // 2001::/32 Teredo tunneling
               || seg0 == 0x2002; // 2002::/16 6to4 tunneling
    }

    private static bool IsCloudMetadataHost(string host) => CloudMetadataHosts.Contains(host);

    private static bool IsLocalHostname(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var suffix in LocalHostnamePatterns)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Checks if a host is in the allowed external domains list.
    ///     Returns an error message if the domain is not allowed, null if it is.
    ///     When <paramref name="allowedDomains"/> is null, all domains are allowed.
    ///     When it's empty, all domains are blocked.
    ///     Exact matches and subdomain matches are supported (e.g. "example.com" allows "sub.example.com").
    /// </summary>
    public static string? CheckDomainAllowlist(Uri uri, IReadOnlyList<string>? allowedDomains)
    {
        // null = allow all (no restriction)
        if (allowedDomains is null) return null;

        // empty list = deny all
        if (allowedDomains.Count == 0)
            return $"[Domain] Blocked: no external domains are allowed. Host '{uri.Host}' is not permitted.";

        // Wildcard
        if (allowedDomains.Count == 1 && allowedDomains[0] == "*")
            return null;

        var host = uri.Host;
        foreach (var domain in allowedDomains)
        {
            // Exact match
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase))
                return null;

            // Subdomain match (host ends with ".domain")
            if (host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return $"[Domain] Blocked: host '{host}' is not in the allowed domains list. " +
               "Add it to security.allowedExternalDomains in config or set to null to allow all.";
    }
}