using Clawsharp.Memory;

namespace Clawsharp.Tests.Unit.Memory;

[TestFixture]
public sealed class EmbeddingMathTests
{
    // ── CosineSimilarity ──────────────────────────────────────────────

    [Test]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var v = new float[] { 1f, 2f, 3f };
        var result = EmbeddingMath.CosineSimilarity(v, v);
        result.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { -1f, 0f };
        var result = EmbeddingMath.CosineSimilarity(a, b);
        result.ShouldBe(-1f, 0.001f);
    }

    [Test]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        var result = EmbeddingMath.CosineSimilarity(a, b);
        result.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void CosineSimilarity_BothZeroVectors_ReturnsZeroNotNaN()
    {
        // TensorPrimitives.CosineSimilarity returns NaN for zero vectors.
        // EmbeddingMath must convert NaN to 0f.
        var zero = new float[] { 0f, 0f, 0f };
        var result = EmbeddingMath.CosineSimilarity(zero, zero);

        float.IsNaN(result).ShouldBeFalse("NaN must be converted to 0f");
        result.ShouldBe(0f);
    }

    [Test]
    public void CosineSimilarity_OneZeroVector_ReturnsZeroNotNaN()
    {
        var a = new float[] { 1f, 2f, 3f };
        var zero = new float[] { 0f, 0f, 0f };
        var result = EmbeddingMath.CosineSimilarity(a, zero);

        float.IsNaN(result).ShouldBeFalse("NaN must be converted to 0f");
        result.ShouldBe(0f);
    }

    [Test]
    public void CosineSimilarity_MismatchedLengths_ThrowsArgumentException()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 1f, 2f };

        var ex = Should.Throw<ArgumentException>(() =>
            EmbeddingMath.CosineSimilarity(a, b));

        ex.Message.ShouldContain("3");
        ex.Message.ShouldContain("2");
    }

    [Test]
    public void CosineSimilarity_SingleElementVectors_Works()
    {
        var a = new float[] { 5f };
        var b = new float[] { 3f };
        var result = EmbeddingMath.CosineSimilarity(a, b);
        // Both positive, same direction — similarity is 1
        result.ShouldBe(1f, 0.001f);
    }

    // ── Deserialize ───────────────────────────────────────────────────

    [Test]
    public void Deserialize_Null_ReturnsEmptyArray()
    {
        var result = EmbeddingMath.Deserialize(null);

        result.ShouldNotBeNull();
        result.Length.ShouldBe(0);
    }

    [Test]
    public void Deserialize_ValidJsonArray_ReturnsFloats()
    {
        var result = EmbeddingMath.Deserialize("[1.0, 2.5, -0.5]");

        result.Length.ShouldBe(3);
        result[0].ShouldBe(1.0f, 0.001f);
        result[1].ShouldBe(2.5f, 0.001f);
        result[2].ShouldBe(-0.5f, 0.001f);
    }

    [Test]
    public void Deserialize_EmptyArray_ReturnsEmpty()
    {
        var result = EmbeddingMath.Deserialize("[]");

        result.Length.ShouldBe(0);
    }

    // ── Serialize ─────────────────────────────────────────────────────

    [Test]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var original = new float[] { 0.1f, -0.5f, 0.9f, 1000f };
        var json = EmbeddingMath.Serialize(original);
        var roundTripped = EmbeddingMath.Deserialize(json);

        roundTripped.Length.ShouldBe(original.Length);
        for (var i = 0; i < original.Length; i++)
        {
            roundTripped[i].ShouldBe(original[i], 0.001f);
        }
    }

    [Test]
    public void Serialize_EmptyArray_ReturnsEmptyJsonArray()
    {
        var json = EmbeddingMath.Serialize([]);
        json.ShouldBe("[]");
    }
}
