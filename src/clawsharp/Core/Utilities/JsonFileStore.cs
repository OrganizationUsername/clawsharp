using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Utilities;

/// <summary>
/// Thread-safe JSON file persistence with atomic write (write-to-tmp + File.Move).
/// Designed for small configuration/state files (pairing codes, approved senders, etc.).
///
/// Thread safety: all operations acquire a <see cref="SemaphoreSlim"/> before touching the file.
/// Atomic writes: data is written to a .tmp file first, then moved into place via <see cref="File.Move"/>.
/// </summary>
/// <typeparam name="T">The type to serialize/deserialize. Must have a source-generated <see cref="JsonTypeInfo{T}"/>.</typeparam>
public sealed class JsonFileStore<T> : IDisposable where T : class, new()
{
    private readonly string _filePath;

    private readonly JsonTypeInfo<T> _jsonTypeInfo;

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new <see cref="JsonFileStore{T}"/> backed by the specified file path.
    /// The parent directory is created automatically on first write.
    /// </summary>
    /// <param name="filePath">Absolute path to the JSON file.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON type info for AOT-safe serialization.</param>
    public JsonFileStore(string filePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        _filePath = filePath;
        _jsonTypeInfo = jsonTypeInfo;
    }

    /// <summary>
    /// Loads the value from disk. Returns a new <typeparamref name="T"/> if the file
    /// does not exist or cannot be deserialized.
    /// </summary>
    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Atomically persists <paramref name="value"/> to disk (write-to-tmp + File.Move).
    /// Creates the parent directory if it does not exist.
    /// </summary>
    public async Task SaveAsync(T value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(value, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Executes <paramref name="mutator"/> under the lock, loading the current value,
    /// applying the mutation, and saving if the mutator returns <c>true</c>.
    /// This avoids TOCTOU races between load and save.
    /// </summary>
    /// <param name="mutator">
    /// Receives the current value. Return <c>true</c> to persist changes, <c>false</c> to skip the save.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The (possibly mutated) value.</returns>
    public async Task<T> MutateAsync(Func<T, bool> mutator, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var value = await LoadInternalAsync(ct).ConfigureAwait(false);
            if (mutator(value))
            {
                await SaveInternalAsync(value, ct).ConfigureAwait(false);
            }

            return value;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    // ── Internal helpers (caller must hold _lock) ─────────────────────────

    private async Task<T> LoadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return new T();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var result = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, _jsonTypeInfo, ct)
                                     .ConfigureAwait(false);
            return result ?? new T();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            // Corrupt or unreadable file — return a fresh default.
            // Callers should wrap in their own logging if they need visibility.
            return new T();
        }
    }

    private async Task SaveInternalAsync(T value, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = _filePath + ".tmp";
        await using (var stream = File.Create(tmpPath))
        {
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, value, _jsonTypeInfo, ct)
                        .ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(tmpPath, _filePath, overwrite: true);
    }
}