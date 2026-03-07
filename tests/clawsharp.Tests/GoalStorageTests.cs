using Clawsharp.Goals;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class GoalStorageTests : IDisposable
{
    private readonly string _tempDir;

    private readonly string _goalsPath;

    private readonly GoalStorage _storage;

    public GoalStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _goalsPath = Path.Combine(_tempDir, "goals.json");
        _storage = new GoalStorage(_goalsPath, NullLogger<GoalStorage>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    [Test]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        var goals = await _storage.LoadAsync();
        goals.ShouldNotBeNull();
        goals.Count.ShouldBe(0);
    }

    [Test]
    public async Task SaveAndLoad_Roundtrip_PreservesGoals()
    {
        var original = new List<Goal>
        {
            new()
            {
                Id = "abc123",
                Title = "Write blog post",
                Description = "A post about goals",
                Status = GoalStatus.Active,
                Steps =
                [
                    new GoalStep { Text = "Draft outline", Done = true },
                    new GoalStep { Text = "Write content", Done = false }
                ]
            }
        };

        await _storage.SaveAsync(original);
        var loaded = await _storage.LoadAsync();

        loaded.Count.ShouldBe(1);
        loaded[0].Id.ShouldBe("abc123");
        loaded[0].Title.ShouldBe("Write blog post");
        loaded[0].Description.ShouldBe("A post about goals");
        loaded[0].Status.ShouldBe(GoalStatus.Active);
        loaded[0].Steps.Count.ShouldBe(2);
        loaded[0].Steps[0].Text.ShouldBe("Draft outline");
        loaded[0].Steps[0].Done.ShouldBeTrue();
        loaded[0].Steps[1].Text.ShouldBe("Write content");
        loaded[0].Steps[1].Done.ShouldBeFalse();
    }

    [Test]
    public async Task SaveAsync_AtomicWrite_NoTmpFileLeftBehind()
    {
        await _storage.SaveAsync([new Goal { Title = "Test" }]);

        File.Exists(_goalsPath).ShouldBeTrue();
        File.Exists(_goalsPath + ".tmp").ShouldBeFalse();
    }

    [Test]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        await _storage.SaveAsync([new Goal { Id = "first", Title = "First" }]);
        await _storage.SaveAsync([new Goal { Id = "second", Title = "Second" }]);

        var loaded = await _storage.LoadAsync();
        loaded.Count.ShouldBe(1);
        loaded[0].Id.ShouldBe("second");
    }

    [Test]
    public async Task SaveAndLoad_AllStatuses_PreservedCorrectly()
    {
        var goals = new List<Goal>
        {
            new() { Id = "a", Title = "Active", Status = GoalStatus.Active },
            new() { Id = "b", Title = "Paused", Status = GoalStatus.Paused },
            new() { Id = "c", Title = "Completed", Status = GoalStatus.Completed },
            new() { Id = "d", Title = "Deleted", Status = GoalStatus.Deleted }
        };

        await _storage.SaveAsync(goals);
        var loaded = await _storage.LoadAsync();

        loaded.Count.ShouldBe(4);
        loaded[0].Status.ShouldBe(GoalStatus.Active);
        loaded[1].Status.ShouldBe(GoalStatus.Paused);
        loaded[2].Status.ShouldBe(GoalStatus.Completed);
        loaded[3].Status.ShouldBe(GoalStatus.Deleted);
    }

    [Test]
    public async Task SaveAndLoad_EmptyList_PreservesEmptyFile()
    {
        await _storage.SaveAsync([]);
        var loaded = await _storage.LoadAsync();
        loaded.Count.ShouldBe(0);
    }

    [Test]
    public async Task SaveAsync_CreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "dir", "goals.json");
        var nestedStorage = new GoalStorage(nested, NullLogger<GoalStorage>.Instance);

        await nestedStorage.SaveAsync([new Goal { Title = "Nested" }]);

        File.Exists(nested).ShouldBeTrue();
        var loaded = await nestedStorage.LoadAsync();
        loaded.Count.ShouldBe(1);
    }

    [Test]
    public async Task LoadAsync_CorruptedFile_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(_goalsPath, "not valid json {{{");

        var loaded = await _storage.LoadAsync();
        loaded.Count.ShouldBe(0);
    }
}