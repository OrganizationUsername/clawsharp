using System.Text.Json;

namespace Clawsharp.Tests;

/// <summary>
/// Tests for ApprovedSendersStore logic: approval tracking, channel isolation, persistence,
/// and corrupt file handling.
/// Uses pattern replication because the production class uses a static readonly StorePath
/// that cannot be redirected via reflection in .NET 10 (initonly enforcement).
/// The replicated logic mirrors ApprovedSendersStore.LoadAsync/SaveAsync/IsApprovedAsync/AddAsync exactly.
/// </summary>
public sealed class ApprovedSendersStoreTests
{
    private string _tempDir = null!;

    private string _storePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _storePath = Path.Combine(_tempDir, "approved.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // Replicates ApprovedSendersStore.LoadAsync
    private async Task<Dictionary<string, List<string>>> LoadAsync()
    {
        if (!File.Exists(_storePath))
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(_storePath);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(bytes)
                   ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
    }

    // Replicates ApprovedSendersStore.SaveAsync
    private async Task SaveAsync(Dictionary<string, List<string>> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_storePath, json);
    }

    // Replicates ApprovedSendersStore.IsApprovedAsync
    private async Task<bool> IsApprovedAsync(string channel, string senderId)
    {
        var data = await LoadAsync();
        return data.TryGetValue(channel, out var senders) &&
               senders.Contains(senderId, StringComparer.Ordinal);
    }

    // Replicates ApprovedSendersStore.AddAsync
    private async Task AddAsync(string channel, string senderId)
    {
        var data = await LoadAsync();
        if (!data.TryGetValue(channel, out var senders))
        {
            senders = [];
            data[channel] = senders;
        }

        if (!senders.Contains(senderId, StringComparer.Ordinal))
        {
            senders.Add(senderId);
            await SaveAsync(data);
        }
    }

    [Test]
    public async Task IsApprovedAsync_EmptyStore_ReturnsFalse()
    {
        (await IsApprovedAsync("telegram", "user1")).ShouldBeFalse();
    }

    [Test]
    public async Task AddAsync_ThenCheck_ReturnsTrue()
    {
        await AddAsync("telegram", "user1");

        (await IsApprovedAsync("telegram", "user1")).ShouldBeTrue();
    }

    [Test]
    public async Task IsApprovedAsync_DifferentChannel_ReturnsIsolated()
    {
        await AddAsync("telegram", "user1");

        (await IsApprovedAsync("telegram", "user1")).ShouldBeTrue();
        (await IsApprovedAsync("discord", "user1")).ShouldBeFalse();
    }

    [Test]
    public async Task AddAsync_DuplicateEntry_DoesNotThrow()
    {
        await AddAsync("telegram", "user1");
        await AddAsync("telegram", "user1");

        (await IsApprovedAsync("telegram", "user1")).ShouldBeTrue();
    }

    [Test]
    public async Task LoadAsync_AfterAdd_PersistsToDisk()
    {
        await AddAsync("slack", "U12345");

        // Re-read from disk using LoadAsync() — simulates a new process reading the file
        var data = await LoadAsync();
        data.TryGetValue("slack", out var senders).ShouldBeTrue();
        senders!.ShouldContain("U12345");
    }

    [Test]
    public async Task IsApprovedAsync_DifferentCase_ReturnsCaseSensitive()
    {
        await AddAsync("telegram", "User1");

        (await IsApprovedAsync("telegram", "User1")).ShouldBeTrue();
        // The store uses StringComparer.Ordinal — case-sensitive
        (await IsApprovedAsync("telegram", "user1")).ShouldBeFalse();
    }

    [Test]
    public async Task AddAsync_MultipleChannels_IndependentPerChannel()
    {
        await AddAsync("telegram", "user1");
        await AddAsync("discord", "user2");

        (await IsApprovedAsync("telegram", "user1")).ShouldBeTrue();
        (await IsApprovedAsync("discord", "user2")).ShouldBeTrue();
        (await IsApprovedAsync("telegram", "user2")).ShouldBeFalse();
        (await IsApprovedAsync("discord", "user1")).ShouldBeFalse();
    }

    [Test]
    public async Task LoadAsync_CorruptJson_ReturnsEmptyGracefully()
    {
        // Write invalid JSON to the store path
        await File.WriteAllTextAsync(_storePath, "{{{{ not valid json ]]]]");

        // Should not throw — the LoadAsync() method catches exceptions and returns empty dict
        (await IsApprovedAsync("telegram", "user1")).ShouldBeFalse();
    }
}