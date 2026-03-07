using System.Text.RegularExpressions;
using Clawsharp.Memory.Markdown;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class MarkdownMemoryTests
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
        Regex.IsMatch(content, @"\d{4}-\d{2}-\d{2}T").ShouldBeTrue();
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
}