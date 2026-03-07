using Clawsharp.Config.Channels;
namespace Clawsharp.Channels;

/// <summary>
/// Encapsulates the AllowFrom allowlist parsing and checking logic shared by all channels.
/// <list type="bullet">
///   <item><c>allowFrom == null</c> -- allow all (backward-compatible default).</item>
///   <item><c>allowFrom.Count == 0</c> -- deny all.</item>
///   <item><c>allowFrom</c> contains <c>"*"</c> -- allow all (explicit wildcard).</item>
///   <item>Otherwise -- only sender IDs in the set are allowed.</item>
/// </list>
/// </summary>
public sealed class AllowListPolicy
{
    /// <summary>Shared instance that allows all senders unconditionally.</summary>
    public static AllowListPolicy AllowAll { get; } = new(allowFrom: null);

    private readonly bool _allowAll;

    private readonly HashSet<string>? _allowed;

    /// <summary>
    /// Creates an <see cref="AllowListPolicy"/> from the raw <c>AllowFrom</c> config list.
    /// </summary>
    /// <param name="allowFrom">
    /// The raw list from <c>ChannelConfig.AllowFrom</c>. Null means allow all.
    /// </param>
    /// <param name="comparer">
    /// String comparer for the allowlist set. Defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// Pass <c>StringComparer.Ordinal</c> for case-sensitive matching (e.g. numeric Discord/Slack IDs).
    /// </param>
    /// <param name="transform">
    /// Optional transform applied to each entry before inserting into the set
    /// (e.g. <c>entry => entry.TrimStart('@').Trim()</c> for Telegram username normalisation).
    /// </param>
    public AllowListPolicy(
        List<string>? allowFrom,
        StringComparer? comparer = null,
        Func<string, string>? transform = null)
    {
        if (allowFrom is null)
        {
            _allowAll = true;
            return;
        }

        if (allowFrom.Count == 0)
        {
            _allowAll = false;
            return;
        }

        if (allowFrom.Contains("*"))
        {
            _allowAll = true;
            return;
        }

        var entries = transform is not null
            ? allowFrom.Select(transform)
            : allowFrom;

        _allowed = new HashSet<string>(entries, comparer ?? StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns <c>true</c> when the allowlist is null, empty-wildcard, or contains <c>"*"</c>.</summary>
    public bool IsAllowAll => _allowAll;

    /// <summary>
    /// Checks whether <paramref name="senderId"/> is allowed by this policy.
    /// Returns <c>true</c> if the list allows all, or if the sender is in the set.
    /// </summary>
    public bool IsAllowed(string senderId)
    {
        if (_allowAll)
        {
            return true;
        }

        return _allowed is not null && _allowed.Contains(senderId);
    }
}