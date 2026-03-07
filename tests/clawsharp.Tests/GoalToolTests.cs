using System.Text.Json;
using Clawsharp.Goals;
using Clawsharp.Tools;
using Clawsharp.Tools.Ops;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class GoalToolTests : IDisposable
{
    private readonly string _tempDir;

    private readonly GoalStorage _storage;

    private readonly GoalTool _tool;

    public GoalToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-goaltool-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "goals.json");
        _storage = new GoalStorage(path, NullLogger<GoalStorage>.Instance);
        _tool = new GoalTool(_storage, NullLogger<GoalTool>.Instance);
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

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // --- create ---

    [Test]
    public async Task Create_WithTitle_ReturnsConfirmation()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Write tests"}"""));

        result.ShouldContain("Goal created:");
        result.ShouldContain("Write tests");

        var goals = await _storage.LoadAsync();
        goals.Count.ShouldBe(1);
        goals[0].Title.ShouldBe("Write tests");
        goals[0].Status.ShouldBe(GoalStatus.Active);
    }

    [Test]
    public async Task Create_WithDescriptionAndSteps_SavesAll()
    {
        var result = await _tool.ExecuteAsync(Parse(
            """{"action":"create","title":"Launch","description":"Ship the product","steps":["Design","Build","Test"]}"""));

        result.ShouldContain("3 steps");

        var goals = await _storage.LoadAsync();
        goals[0].Description.ShouldBe("Ship the product");
        goals[0].Steps.Count.ShouldBe(3);
        goals[0].Steps[0].Text.ShouldBe("Design");
        goals[0].Steps[1].Text.ShouldBe("Build");
        goals[0].Steps[2].Text.ShouldBe("Test");
        goals[0].Steps.ShouldAllBe(s => !s.Done);
    }

    [Test]
    public async Task Create_MissingTitle_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"create"}"""));
        result.ShouldContain("Error");
        result.ShouldContain("title is required");
    }

    [Test]
    public async Task Create_EmptyTitle_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"create","title":"  "}"""));
        result.ShouldContain("Error");
    }

    // --- list ---

    [Test]
    public async Task List_NoGoals_ReturnsNoGoalsMessage()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"list"}"""));
        result.ShouldContain("No active goals");
    }

    [Test]
    public async Task List_ActiveGoals_ReturnsFormatted()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Goal A","steps":["Step 1"]}"""));
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Goal B"}"""));

        var result = await _tool.ExecuteAsync(Parse("""{"action":"list"}"""));

        result.ShouldContain("Goal A");
        result.ShouldContain("Goal B");
        result.ShouldContain("[0/1 steps]");
    }

    [Test]
    public async Task List_WithStatusFilter_FiltersCorrectly()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Active goal"}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;
        await _tool.ExecuteAsync(Parse($$$"""{"action":"pause","id":"{{{id}}}"}"""));

        // List active: should not include paused
        var active = await _tool.ExecuteAsync(Parse("""{"action":"list","status":"active"}"""));
        active.ShouldContain("No active goals");

        // List paused: should include the paused goal
        var paused = await _tool.ExecuteAsync(Parse("""{"action":"list","status":"paused"}"""));
        paused.ShouldContain("Active goal");

        // List all: should include the paused goal
        var all = await _tool.ExecuteAsync(Parse("""{"action":"list","status":"all"}"""));
        all.ShouldContain("Active goal");
    }

    // --- update_step ---

    [Test]
    public async Task UpdateStep_MarksDone_ReturnsConfirmation()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"My goal","steps":["Step A","Step B"]}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"update_step","id":"{{{id}}}","step_index":0,"done":true}"""));

        result.ShouldContain("marked as done");
        result.ShouldContain("Step A");

        var updated = await _storage.LoadAsync();
        updated[0].Steps[0].Done.ShouldBeTrue();
        updated[0].Steps[1].Done.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateStep_MarksUndone_ReturnsConfirmation()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"My goal","steps":["Step A"]}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        await _tool.ExecuteAsync(Parse($$$"""{"action":"update_step","id":"{{{id}}}","step_index":0,"done":true}"""));
        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"update_step","id":"{{{id}}}","step_index":0,"done":false}"""));

        result.ShouldContain("not done");

        var updated = await _storage.LoadAsync();
        updated[0].Steps[0].Done.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateStep_MissingId_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"update_step","step_index":0,"done":true}"""));
        result.ShouldContain("Error");
        result.ShouldContain("id is required");
    }

    [Test]
    public async Task UpdateStep_MissingStepIndex_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"update_step","id":"abc","done":true}"""));
        result.ShouldContain("Error");
        result.ShouldContain("step_index");
    }

    [Test]
    public async Task UpdateStep_MissingDone_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"update_step","id":"abc","step_index":0}"""));
        result.ShouldContain("Error");
        result.ShouldContain("done");
    }

    [Test]
    public async Task UpdateStep_InvalidId_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"update_step","id":"nonexist","step_index":0,"done":true}"""));
        result.ShouldContain("not found");
    }

    [Test]
    public async Task UpdateStep_OutOfRange_ReturnsError()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Goal","steps":["One"]}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"update_step","id":"{{{id}}}","step_index":5,"done":true}"""));
        result.ShouldContain("out of range");
    }

    // --- complete ---

    [Test]
    public async Task Complete_SetsStatusCompleted()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Finish this"}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"complete","id":"{{{id}}}"}"""));

        result.ShouldContain("completed");

        var updated = await _storage.LoadAsync();
        updated[0].Status.ShouldBe(GoalStatus.Completed);
    }

    [Test]
    public async Task Complete_MissingId_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"complete"}"""));
        result.ShouldContain("Error");
        result.ShouldContain("id is required");
    }

    // --- pause ---

    [Test]
    public async Task Pause_SetsStatusPaused()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Pause me"}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"pause","id":"{{{id}}}"}"""));

        result.ShouldContain("paused");

        var updated = await _storage.LoadAsync();
        updated[0].Status.ShouldBe(GoalStatus.Paused);
    }

    // --- delete ---

    [Test]
    public async Task Delete_SoftDeletes()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Delete me"}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        var result = await _tool.ExecuteAsync(Parse($$$"""{"action":"delete","id":"{{{id}}}"}"""));

        result.ShouldContain("deleted");

        var updated = await _storage.LoadAsync();
        updated[0].Status.ShouldBe(GoalStatus.Deleted);
    }

    [Test]
    public async Task Delete_DeletedGoalNotShownInList()
    {
        await _tool.ExecuteAsync(Parse("""{"action":"create","title":"Delete me"}"""));
        var goals = await _storage.LoadAsync();
        var id = goals[0].Id;

        await _tool.ExecuteAsync(Parse($$$"""{"action":"delete","id":"{{{id}}}"}"""));

        var list = await _tool.ExecuteAsync(Parse("""{"action":"list"}"""));
        list.ShouldContain("No active goals");

        // But visible in "all" filter
        var all = await _tool.ExecuteAsync(Parse("""{"action":"list","status":"all"}"""));
        all.ShouldNotContain("Delete me"); // deleted goals excluded from "all" too
    }

    // --- invalid action ---

    [Test]
    public async Task InvalidAction_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{"action":"unknown"}"""));
        result.ShouldContain("Error");
        result.ShouldContain("action is required");
    }

    [Test]
    public async Task MissingAction_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(Parse("""{}"""));
        result.ShouldContain("Error");
    }

    // --- tool metadata ---

    [Test]
    public void Name_IsGoal()
    {
        _tool.Name.ShouldBe("goal");
    }

    [Test]
    public void Description_Exists()
    {
        _tool.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void ParametersSchema_IsValidJson()
    {
        Should.NotThrow(() => JsonDocument.Parse(_tool.ParametersSchemaJson));
    }
}