using System.Text.RegularExpressions;
using Clawsharp.Memory.Markdown;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed partial class MarkdownMemoryTests
{
    private string _dir = null!;

    private MarkdownMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _memory = new MarkdownMemory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Test]
    public async Task GetContextAsync_NoFile_ReturnsNull()
    {
        var result = await _memory.GetContextAsync();
        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_WritesFactToMemoryFile()
    {
        await _memory.AppendFactAsync("user prefers dark mode");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "MEMORY.md"));
        content.ShouldContain("user prefers dark mode");
    }

    [Test]
    public async Task GetContextAsync_AfterAppendFact_ReturnsContent()
    {
        await _memory.AppendFactAsync("user is named Alice");

        var result = await _memory.GetContextAsync();

        result.ShouldNotBeNull();
        result!.ShouldContain("user is named Alice");
    }

    [Test]
    public async Task AppendFactAsync_MultipleFactsPersist()
    {
        await _memory.AppendFactAsync("fact one");
        await _memory.AppendFactAsync("fact two");

        var result = await _memory.GetContextAsync();

        result.ShouldNotBeNull();
        result!.ShouldContain("fact one");
        result!.ShouldContain("fact two");
    }

    [Test]
    public async Task AppendHistoryAsync_WritesToHistoryFile()
    {
        await _memory.AppendHistoryAsync("User asked about weather");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "HISTORY.md"));
        content.ShouldContain("User asked about weather");
    }

    [Test]
    public async Task AppendHistoryAsync_IncludesTimestamp()
    {
        await _memory.AppendHistoryAsync("summary text");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "HISTORY.md"));
        // Timestamp format: 2026-03-03T...Z
        MyRegex().IsMatch(content).ShouldBeTrue();
    }

    [Test]
    public async Task SearchAsync_NoFile_ReturnsEmpty()
    {
        var results = await _memory.SearchAsync("anything");
        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_MatchingFact_ReturnsIt()
    {
        await _memory.AppendFactAsync("user likes pizza");
        await _memory.AppendFactAsync("user dislikes broccoli");

        var results = await _memory.SearchAsync("pizza");

        results.Count.ShouldBe(1);
        results[0].ShouldContain("pizza");
    }

    [Test]
    public async Task SearchAsync_CaseInsensitive_Matches()
    {
        await _memory.AppendFactAsync("User Prefers DARK MODE");

        var results = await _memory.SearchAsync("dark mode");

        results.ShouldNotBeEmpty();
    }

    [Test]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        await _memory.AppendFactAsync("user likes pizza");

        var results = await _memory.SearchAsync("burgers");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_RespectsNLimit()
    {
        for (var i = 0; i < 10; i++)
        {
            await _memory.AppendFactAsync($"fact about cats number {i}");
        }

        var results = await _memory.SearchAsync("cats", n: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task AppendFactAsync_NewlineInFact_IsCollapsedToSpace()
    {
        await _memory.AppendFactAsync("line one\nline two");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "MEMORY.md"));
        content.ShouldNotContain("\nline two"); // newline should be replaced
        content.ShouldContain("line one line two");
    }

    [Test]
    public async Task GetContextAsync_EmptyFile_ReturnsNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "MEMORY.md"), "   ");

        var result = await _memory.GetContextAsync();

        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_CreatesMemoryFile()
    {
        var memoryPath = Path.Combine(_dir, "MEMORY.md");
        File.Exists(memoryPath).ShouldBeFalse();

        await _memory.AppendFactAsync("first fact ever");

        File.Exists(memoryPath).ShouldBeTrue();
    }

    [Test]
    public async Task AppendFactAsync_FactWrittenToFile_WithBulletPrefix()
    {
        await _memory.AppendFactAsync("user speaks French");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "MEMORY.md"));
        content.ShouldContain("- user speaks French");
    }

    [Test]
    public async Task AppendHistoryAsync_HistoryWrittenToSeparateFile()
    {
        var historyPath = Path.Combine(_dir, "HISTORY.md");

        await _memory.AppendHistoryAsync("Discussed project architecture");

        File.Exists(historyPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(historyPath);
        content.ShouldContain("Discussed project architecture");
        // Should have a markdown heading with timestamp
        content.ShouldContain("## ");
    }

    [Test]
    public async Task GetContextAsync_ReturnsRawFileContent()
    {
        await _memory.AppendFactAsync("fact alpha");
        await _memory.AppendFactAsync("fact beta");

        var result = await _memory.GetContextAsync();

        // GetContextAsync returns raw file content for markdown backend
        result.ShouldNotBeNull();
        result!.ShouldContain("- fact alpha");
        result!.ShouldContain("- fact beta");
    }

    [Test]
    public async Task ClearAsync_DeletesMemoryFile()
    {
        var memoryPath = Path.Combine(_dir, "MEMORY.md");
        await _memory.AppendFactAsync("something");
        File.Exists(memoryPath).ShouldBeTrue();

        await _memory.ClearAsync();

        File.Exists(memoryPath).ShouldBeFalse();
    }

    [Test]
    public async Task ClearAsync_PreservesHistoryFile()
    {
        var historyPath = Path.Combine(_dir, "HISTORY.md");
        await _memory.AppendFactAsync("fact to clear");
        await _memory.AppendHistoryAsync("history to preserve");

        await _memory.ClearAsync();

        // MEMORY.md is deleted
        File.Exists(Path.Combine(_dir, "MEMORY.md")).ShouldBeFalse();
        // HISTORY.md is WORM — preserved across clears
        File.Exists(historyPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(historyPath);
        content.ShouldContain("history to preserve");
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllPersistedInFile()
    {
        await _memory.AppendFactAsync("fact one");
        await _memory.AppendFactAsync("fact two");
        await _memory.AppendFactAsync("fact three");

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "MEMORY.md"));
        content.ShouldContain("- fact one");
        content.ShouldContain("- fact two");
        content.ShouldContain("- fact three");
    }

    [Test]
    public async Task ListFactsAsync_ReturnsAllFacts()
    {
        await _memory.AppendFactAsync("alpha");
        await _memory.AppendFactAsync("beta");
        await _memory.AppendFactAsync("gamma");

        var facts = await _memory.ListFactsAsync();

        facts.Count.ShouldBe(3);
        facts.ShouldContain(f => f.Content == "alpha");
        facts.ShouldContain(f => f.Content == "beta");
        facts.ShouldContain(f => f.Content == "gamma");
        // Each fact should have a sequential Id
        facts.Select(f => f.Id).Distinct().Count().ShouldBe(3);
    }

    [Test]
    public async Task FileRoundTrip_SurvivesRestart()
    {
        // Write facts with original instance
        await _memory.AppendFactAsync("persistent fact one");
        await _memory.AppendFactAsync("persistent fact two");

        // Create a NEW MarkdownMemory instance pointing to the same directory
        using var newMemory = new MarkdownMemory(_dir);

        var result = await newMemory.GetContextAsync();
        result.ShouldNotBeNull();
        result!.ShouldContain("persistent fact one");
        result!.ShouldContain("persistent fact two");

        var facts = await newMemory.ListFactsAsync();
        facts.Count.ShouldBe(2);

        var searchResults = await newMemory.SearchAsync("persistent");
        searchResults.Count.ShouldBe(2);
    }

    [Test]
    public async Task SearchHybridAsync_FallsBackToStringContains()
    {
        await _memory.AppendFactAsync("user enjoys hiking in mountains");
        await _memory.AppendFactAsync("user likes reading books");

        var facts = await _memory.SearchHybridAsync("hiking");

        facts.Count.ShouldBe(1);
        facts[0].Content.ShouldContain("hiking");
    }

    [Test]
    public async Task PruneExpiredFactsAsync_IsNoOp()
    {
        await _memory.AppendFactAsync("some fact");

        var pruned = await _memory.PruneExpiredFactsAsync(TimeSpan.FromSeconds(1));

        pruned.ShouldBe(0);
        // Fact should still be there
        var facts = await _memory.ListFactsAsync();
        facts.Count.ShouldBe(1);
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T")]
    private static partial Regex MyRegex();
}