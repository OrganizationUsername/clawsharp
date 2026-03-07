using System.Security.Cryptography;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Sessions;

/// <summary>
/// Stores pending pairing requests (6-digit code to sender info) in ~/.clawsharp/pending-pairs.json.
/// Thread-safe via <see cref="JsonFileStore{T}"/>.
/// </summary>
public sealed partial class PairingStore : IDisposable
{
    private static readonly string StorePath = Path.Combine(
        ConfigLoader.ExpandHome("~/.clawsharp"), "pending-pairs.json");

    private readonly JsonFileStore<List<PendingPair>> _store;

    private readonly ILogger<PairingStore> _logger;

    public PairingStore(ILogger<PairingStore> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<List<PendingPair>>(StorePath, PairingJsonContext.Default.ListPendingPair);
    }

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    /// <summary>
    /// Returns an existing pending code for the sender, or creates a new 6-digit code with 24h expiry.
    /// </summary>
    public async Task<string> GetOrCreateCodeAsync(string channel, string senderId, string senderName, CancellationToken ct = default)
    {
        string? resultCode = null;

        await _store.MutateAsync(pairs =>
        {
            CleanExpired(pairs);

            // Check if this sender already has a pending code
            var existing = pairs.Find(p =>
                string.Equals(p.Channel, channel, StringComparison.Ordinal) &&
                string.Equals(p.SenderId, senderId, StringComparison.Ordinal));

            if (existing is not null)
            {
                resultCode = existing.Code;
                return false; // no save needed
            }

            // Generate a new unique 6-digit code (bounded to prevent infinite loop if cleanup fails)
            string? code = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var candidate = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                if (!pairs.Exists(p => string.Equals(p.Code, candidate, StringComparison.Ordinal)))
                {
                    code = candidate;
                    break;
                }
            }

            if (code is null)
            {
                throw new InvalidOperationException(
                    "Failed to generate a unique pairing code after 100 attempts. This may indicate a cleanup failure.");
            }

            var pair = new PendingPair(
                Code: code,
                Channel: channel,
                SenderId: senderId,
                SenderName: senderName,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(24));

            pairs.Add(pair);
            resultCode = code;

            LogPairingCodeCreated(_logger, code, channel, senderId, senderName);

            return true; // save
        }, ct).ConfigureAwait(false);

        return resultCode!;
    }

    /// <summary>Returns all non-expired pending pairs.</summary>
    public async Task<List<PendingPair>> GetPendingAsync(CancellationToken ct = default)
    {
        return await _store.MutateAsync(pairs =>
        {
            return CleanExpired(pairs); // save only if expired entries were removed
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Approves a pending pair by code: removes it from the store and returns the pair.
    /// Returns null if the code is not found or has expired.
    /// </summary>
    public async Task<PendingPair?> ApproveAsync(string code, CancellationToken ct = default)
    {
        PendingPair? approved = null;

        await _store.MutateAsync(pairs =>
        {
            CleanExpired(pairs);

            var index = pairs.FindIndex(p => string.Equals(p.Code, code, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            approved = pairs[index];
            pairs.RemoveAt(index);

            LogPairingCodeApproved(_logger, approved.Code, approved.Channel, approved.SenderId, approved.SenderName);

            return true; // save
        }, ct).ConfigureAwait(false);

        return approved;
    }

    private static bool CleanExpired(List<PendingPair> pairs)
    {
        return pairs.RemoveAll(p => p.ExpiresAt <= DateTimeOffset.UtcNow) > 0;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Created pairing code {Code} for {Channel}:{SenderId} ({SenderName})")]
    private static partial void LogPairingCodeCreated(ILogger logger, string code, string channel, string senderId, string senderName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Approved pairing code {Code} for {Channel}:{SenderId} ({SenderName})")]
    private static partial void LogPairingCodeApproved(ILogger logger, string code, string channel, string senderId, string senderName);
}