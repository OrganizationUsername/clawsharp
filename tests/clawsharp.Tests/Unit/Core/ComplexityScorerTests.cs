using Clawsharp.Core.Pipeline;

namespace Clawsharp.Tests.Unit.Core;

public sealed class ComplexityScorerTests
{
    [Test]
    public void Score_EmptyString_ReturnsZero()
    {
        ComplexityScorer.Score("", 0, 0).ShouldBe(0);
    }

    [Test]
    public void Score_ShortPlainText_ScoresLow()
    {
        var score = ComplexityScorer.Score("Hello, how are you?", 0, 0);
        score.ShouldBeLessThanOrEqualTo(10);
    }

    [Test]
    public void Score_LongText_ScoresHigherTokenContribution()
    {
        // >500 tokens (~2000+ chars) → 25 points
        var longText = new string('a', 2500);
        var score = ComplexityScorer.Score(longText, 0, 0);
        score.ShouldBeGreaterThanOrEqualTo(25);
    }

    [Test]
    public void Score_MediumText_ScoresMediumTokenContribution()
    {
        // >200 tokens (~800+ chars) → 15 points
        var mediumText = new string('a', 900);
        var score = ComplexityScorer.Score(mediumText, 0, 0);
        score.ShouldBeGreaterThanOrEqualTo(15);
    }

    [Test]
    public void Score_FencedCodeBlock_AddsCodeBlockScore()
    {
        var message = "Here is code:\n```csharp\nvar x = 1;\n```";
        var score = ComplexityScorer.Score(message, 0, 0);
        // Should include FullCodeBlockScore (20) + InlineCodeScore (5)
        score.ShouldBeGreaterThanOrEqualTo(25);
    }

    [Test]
    public void Score_PartialCodeBlock_AddsPartialScore()
    {
        var message = "Here is some code:\n```\nvar x = 1;";
        var score = ComplexityScorer.Score(message, 0, 0);
        // Should include PartialCodeBlockScore (10) + InlineCodeScore (5)
        score.ShouldBeGreaterThanOrEqualTo(15);
    }

    [Test]
    public void Score_InlineCode_AddsInlineScore()
    {
        var message = "Use the `var` keyword";
        var score = ComplexityScorer.Score(message, 0, 0);
        score.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Test]
    public void Score_CjkContent_AddsCjkScore()
    {
        var message = "这是一个测试消息";
        var score = ComplexityScorer.Score(message, 0, 0);
        score.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Test]
    public void Score_JapaneseContent_AddsCjkScore()
    {
        var message = "テストメッセージ";
        var score = ComplexityScorer.Score(message, 0, 0);
        score.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Test]
    public void Score_HighToolCallCount_AddsHighToolScore()
    {
        var score = ComplexityScorer.Score("hello", recentToolCallCount: 4, conversationDepth: 0);
        score.ShouldBeGreaterThanOrEqualTo(25);
    }

    [Test]
    public void Score_LowToolCallCount_AddsLowToolScore()
    {
        var score = ComplexityScorer.Score("hello", recentToolCallCount: 1, conversationDepth: 0);
        score.ShouldBeGreaterThanOrEqualTo(15);
    }

    [Test]
    public void Score_ZeroToolCalls_NoToolScore()
    {
        var score = ComplexityScorer.Score("hello", recentToolCallCount: 0, conversationDepth: 0);
        score.ShouldBeLessThan(15);
    }

    [Test]
    public void Score_DeepConversation_AddsDepthScore()
    {
        var score = ComplexityScorer.Score("hello", 0, conversationDepth: 15);
        score.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Test]
    public void Score_MediumConversation_AddsMediumDepthScore()
    {
        var score = ComplexityScorer.Score("hello", 0, conversationDepth: 7);
        score.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Test]
    public void Score_IsCappedAt100()
    {
        // Long CJK text with code blocks, high tool calls, deep conversation
        // Max possible: HighToken(25) + FullCode(20) + InlineCode(5) + CJK(10) + HighTool(25) + DeepConv(10) = 95
        var extreme = new string('\u4E00', 3000) + "\n```\ncode\n```\n`inline`";
        var score = ComplexityScorer.Score(extreme, recentToolCallCount: 10, conversationDepth: 50);
        score.ShouldBeLessThanOrEqualTo(100);
        score.ShouldBe(95);
    }

    [Test]
    public void Score_AllFactorsCombine()
    {
        // Medium text + code block + tool calls + deep conversation
        var message = new string('a', 900) + "\n```\ncode\n```";
        var score = ComplexityScorer.Score(message, recentToolCallCount: 2, conversationDepth: 8);
        // MediumToken(15) + FullCode(20) + InlineCode(5) + LowTool(15) + MediumDepth(5) = 60
        score.ShouldBeGreaterThanOrEqualTo(55);
    }
}
