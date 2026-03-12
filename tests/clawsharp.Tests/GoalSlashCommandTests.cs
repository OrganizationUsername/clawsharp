using Clawsharp.Core.Pipeline;

namespace Clawsharp.Tests;

public sealed class GoalSlashCommandTests
{
    [Test]
    public void Goals_ReturnsShowGoals()
    {
        SlashCommandRouter.TryHandle("/goals").ShouldBe(SlashCommandResult.ShowGoals);
    }

    [Test]
    public void GoalsClear_ReturnsClearGoals()
    {
        SlashCommandRouter.TryHandle("/goals clear").ShouldBe(SlashCommandResult.ClearGoals);
    }

    [Test]
    public void GoalsUnknownArg_ReturnsShowGoals()
    {
        SlashCommandRouter.TryHandle("/goals something").ShouldBe(SlashCommandResult.ShowGoals);
    }
}

public sealed class GoalSystemPromptTests
{
    [Test]
    public void BuildSplit_WithGoalsContext_IncludesActiveGoalsSection()
    {
        var (staticPart, _) = SystemPromptBuilder.BuildSplit(
            activeGoalsContext: "- [abc] Write blog post (1/3 steps done)");

        staticPart.ShouldContain("## Active Goals");
        staticPart.ShouldContain("Write blog post");
    }

    [Test]
    public void BuildSplit_WithNullGoals_OmitsSection()
    {
        var (staticPart, _) = SystemPromptBuilder.BuildSplit(activeGoalsContext: null);

        staticPart.ShouldNotContain("Active Goals");
    }

    [Test]
    public void BuildSplit_WithEmptyGoals_OmitsSection()
    {
        var (staticPart, _) = SystemPromptBuilder.BuildSplit(activeGoalsContext: "  ");

        staticPart.ShouldNotContain("Active Goals");
    }

    [Test]
    public void Build_WithGoalsContext_IncludesActiveGoals()
    {
        var prompt = SystemPromptBuilder.Build(
            activeGoalsContext: "- [xyz] Ship feature (0/2 steps done)");

        prompt.ShouldContain("## Active Goals");
        prompt.ShouldContain("Ship feature");
    }
}