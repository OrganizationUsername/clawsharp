using Clawsharp.Memory;

namespace Clawsharp.Tests.Unit.Memory;

[TestFixture]
public sealed class KeywordExpanderTests
{
    // ── Text too short ────────────────────────────────────────────────

    [Test]
    public void ExtractKeywords_TextShorterThan30Chars_ReturnsEmpty()
    {
        var result = KeywordExpander.ExtractKeywords("Short text");
        result.ShouldBeEmpty();
    }

    [Test]
    public void ExtractKeywords_Exactly29Chars_ReturnsEmpty()
    {
        // 29 chars — just below the 30-char threshold
        var result = KeywordExpander.ExtractKeywords("a b c d e f g h i j k l m n o");
        result.Count.ShouldBe(0);
    }

    [Test]
    public void ExtractKeywords_EmptyString_ReturnsEmpty()
    {
        var result = KeywordExpander.ExtractKeywords("");
        result.ShouldBeEmpty();
    }

    // ── Stop words ────────────────────────────────────────────────────

    [Test]
    public void ExtractKeywords_AllStopWords_ReturnsEmpty()
    {
        // All words >= 4 chars but all are stop words
        const string allStops =
            "this that with from have been were will would could should what when where which about";

        allStops.Length.ShouldBeGreaterThanOrEqualTo(30);

        var result = KeywordExpander.ExtractKeywords(allStops);
        result.ShouldBeEmpty();
    }

    // ── Keyword extraction ────────────────────────────────────────────

    [Test]
    public void ExtractKeywords_NormalText_ExtractsSignificantWords()
    {
        const string text = "The user is asking about machine learning algorithms in Python";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldContain("machine");
        result.ShouldContain("learning");
        result.ShouldContain("algorithms");
        result.ShouldContain("python");
    }

    [Test]
    public void ExtractKeywords_ShortWordsFiltered()
    {
        // Words shorter than MinKeywordLength (4) should be excluded
        const string text = "The big red dog ran away from the old farm house slowly";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldNotContain("the");
        result.ShouldNotContain("big");
        result.ShouldNotContain("red");
        result.ShouldNotContain("ran");
        result.ShouldNotContain("dog");
    }

    [Test]
    public void ExtractKeywords_ResultsAreLowercase()
    {
        const string text = "Machine Learning PYTHON AlgoRithms NeuralNetworks";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldAllBe(k => k == k.ToLowerInvariant(),
            "all extracted keywords must be lowercase");
    }

    [Test]
    public void ExtractKeywords_DuplicateWords_DeduplicatedInResult()
    {
        const string text = "Python Python Python machine learning machine learning machine";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldContain("python");
        result.ShouldContain("machine");
        result.ShouldContain("learning");

        // HashSet ensures no duplicates
        var pyCount = result.Count(k => k == "python");
        pyCount.ShouldBe(1, "duplicate keywords must be deduplicated");
    }

    [Test]
    public void ExtractKeywords_PunctuationDelimiters_WordsExtractedCorrectly()
    {
        // Comma, period, exclamation, parentheses are all word delimiters.
        // Note: hyphen is NOT a delimiter — "machine-learning" stays as one token.
        const string text = "Hello, world! This is a machine.learning test. Python (version 3.11)?";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldContain("hello");
        result.ShouldContain("world");
        result.ShouldContain("machine");
        result.ShouldContain("learning");
        result.ShouldContain("python");
        result.ShouldContain("version");
    }

    [Test]
    public void ExtractKeywords_HyphenatedCompound_TreatedAsSingleToken()
    {
        // Hyphen is intentionally NOT in the delimiter list, so "machine-learning"
        // is kept as a single token rather than split into "machine" and "learning".
        const string text = "The user is asking about machine-learning and neural networks";

        var result = KeywordExpander.ExtractKeywords(text);

        // The whole hyphenated compound survives as one keyword
        result.ShouldContain("machine-learning",
            "hyphen is not a delimiter — hyphenated compounds are preserved as single tokens");
        // Split halves do NOT appear separately
        result.ShouldNotContain("machine");
        result.ShouldNotContain("learning");
    }

    // ── Unicode keywords ──────────────────────────────────────────────

    [Test]
    public void ExtractKeywords_NonAsciiWord_IncludedInResult()
    {
        // Chinese word "人工智能" (AI) — no ASCII delimiters, passes as a single token
        const string text = "The user is asking about 人工智能 and machine learning systems";

        var result = KeywordExpander.ExtractKeywords(text);

        // Document current behavior: non-ASCII keywords pass through since stop-word list is ASCII-only
        result.ShouldContain("人工智能",
            "non-ASCII keywords should be included (stop-word list is English-only)");
    }

    // ── Minimum keyword length boundary ──────────────────────────────

    [Test]
    public void ExtractKeywords_Word3Chars_Excluded()
    {
        const string text = "The cat sat mat hat rat bat fat pat vat kat rat sat pat";
        // All 3-char words: below MinKeywordLength=4

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldAllBe(k => k.Length >= 4,
            "all returned keywords must meet the minimum length requirement");
    }

    [Test]
    public void ExtractKeywords_Word4Chars_IncludedIfNotStopWord()
    {
        const string text = "The user sent data file path from work code base test unit";

        var result = KeywordExpander.ExtractKeywords(text);

        result.ShouldContain("data");
        result.ShouldContain("file");
        result.ShouldContain("path");
        result.ShouldContain("work");
        result.ShouldContain("code");
        result.ShouldContain("base");
        result.ShouldContain("test");
        result.ShouldContain("unit");
    }
}
