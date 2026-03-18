using Clawsharp.Channels;

namespace Clawsharp.Tests.Channels;

[TestFixture]
public sealed class BoundedDeduplicatorTests
{
    // ── Constructor validation ─────────────────────────────────────────

    [Test]
    public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundedDeduplicator<string>(0));
    }

    [Test]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundedDeduplicator<string>(-1));
    }

    [Test]
    public void Constructor_ValidCapacity_DoesNotThrow()
    {
        Should.NotThrow(() => new BoundedDeduplicator<string>(1));
    }

    // ── Basic deduplication contract ───────────────────────────────────

    [Test]
    public void TryAdd_NewItem_ReturnsTrue()
    {
        var dedup = new BoundedDeduplicator<string>(10);

        dedup.TryAdd("event-001").ShouldBeTrue();
    }

    [Test]
    public void TryAdd_SameItemTwice_SecondReturnsFalse()
    {
        var dedup = new BoundedDeduplicator<string>(10);

        dedup.TryAdd("event-123").ShouldBeTrue();
        dedup.TryAdd("event-123").ShouldBeFalse();
    }

    [Test]
    public void TryAdd_DifferentItems_AllReturnTrue()
    {
        var dedup = new BoundedDeduplicator<string>(10);

        dedup.TryAdd("a").ShouldBeTrue();
        dedup.TryAdd("b").ShouldBeTrue();
        dedup.TryAdd("c").ShouldBeTrue();
    }

    // ── LRU eviction ──────────────────────────────────────────────────

    [Test]
    public void TryAdd_EvictsOldestWhenAtCapacity_EvictedItemCanBeAddedAgain()
    {
        // capacity=3: add a, b, c → full. Adding d evicts a.
        // Window after d: [b, c, d]
        var dedup = new BoundedDeduplicator<string>(3);

        dedup.TryAdd("a").ShouldBeTrue();
        dedup.TryAdd("b").ShouldBeTrue();
        dedup.TryAdd("c").ShouldBeTrue();

        // Adding d evicts a (oldest); window is now [b, c, d]
        dedup.TryAdd("d").ShouldBeTrue();

        // "a" was evicted — it should be accepted again as a new item
        dedup.TryAdd("a").ShouldBeTrue("a was evicted and must be accepted again");

        // Adding a evicted b (oldest in [b,c,d]); window is now [c, d, a]
        // "c" should still be rejected (was not evicted yet)
        dedup.TryAdd("c").ShouldBeFalse("c was not evicted and must still be deduplicated");
        // "b" was evicted (second-oldest when a was re-added)
        dedup.TryAdd("b").ShouldBeTrue("b was evicted when a was re-added");
    }

    [Test]
    public void TryAdd_CapacityOne_EvictsOnEveryNewItem()
    {
        var dedup = new BoundedDeduplicator<string>(1);

        // First item: accepted
        dedup.TryAdd("a").ShouldBeTrue();
        // Second unique item: accepted, evicts a
        dedup.TryAdd("b").ShouldBeTrue();
        // a is evicted, can be added again
        dedup.TryAdd("a").ShouldBeTrue();
        // b is now evicted (a was just added), can be re-added
        dedup.TryAdd("b").ShouldBeTrue();
        // a is evicted again
        dedup.TryAdd("a").ShouldBeTrue();
    }

    [Test]
    public void TryAdd_CapacityOne_DuplicateBeforeEviction_ReturnsFalse()
    {
        var dedup = new BoundedDeduplicator<string>(1);

        dedup.TryAdd("x").ShouldBeTrue();
        dedup.TryAdd("x").ShouldBeFalse("duplicate before eviction must be rejected");
    }

    // ── Integer generic type ───────────────────────────────────────────

    [Test]
    public void TryAdd_IntType_WorksCorrectly()
    {
        var dedup = new BoundedDeduplicator<int>(5);

        dedup.TryAdd(1).ShouldBeTrue();
        dedup.TryAdd(2).ShouldBeTrue();
        dedup.TryAdd(1).ShouldBeFalse();
    }

    // ── Custom comparer ───────────────────────────────────────────────

    [Test]
    public void Constructor_CaseInsensitiveComparer_TreatsAsEqual()
    {
        var dedup = new BoundedDeduplicator<string>(10, StringComparer.OrdinalIgnoreCase);

        dedup.TryAdd("EventId-123").ShouldBeTrue();
        dedup.TryAdd("eventid-123").ShouldBeFalse("case-insensitive duplicate must be rejected");
    }

    // ── Concurrency ───────────────────────────────────────────────────

    [Test]
    public async Task TryAdd_ConcurrentUniqueCalls_AllAccepted_NoExceptions()
    {
        const int threadCount = 10;
        const int itemsPerThread = 100;
        var dedup = new BoundedDeduplicator<string>(threadCount * itemsPerThread);
        var acceptedCount = 0;

        var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < itemsPerThread; i++)
            {
                // Each thread uses its own unique items — all should be accepted
                var item = $"thread-{t}-item-{i}";
                if (dedup.TryAdd(item))
                {
                    Interlocked.Increment(ref acceptedCount);
                }
            }
        }));

        await Task.WhenAll(tasks);

        acceptedCount.ShouldBe(threadCount * itemsPerThread,
            "all unique items from all threads should be accepted");
    }

    [Test]
    public async Task TryAdd_ConcurrentDuplicateCalls_ExactlyOneAccepted()
    {
        var dedup = new BoundedDeduplicator<string>(100);
        const string sharedItem = "duplicate-event-999";
        var acceptedCount = 0;

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            if (dedup.TryAdd(sharedItem))
            {
                Interlocked.Increment(ref acceptedCount);
            }
        }));

        await Task.WhenAll(tasks);

        acceptedCount.ShouldBe(1, "exactly one thread should win the race for the duplicate item");
    }

    // ── Capacity boundary ─────────────────────────────────────────────

    [Test]
    public void TryAdd_ExactlyAtCapacity_AllAccepted()
    {
        const int capacity = 5;
        var dedup = new BoundedDeduplicator<int>(capacity);

        for (var i = 0; i < capacity; i++)
        {
            dedup.TryAdd(i).ShouldBeTrue($"item {i} should be accepted while under capacity");
        }
    }

    [Test]
    public void TryAdd_OnePastCapacity_StillAccepted_OldestEvicted()
    {
        const int capacity = 3;
        var dedup = new BoundedDeduplicator<int>(capacity);

        dedup.TryAdd(1);
        dedup.TryAdd(2);
        dedup.TryAdd(3);

        // This should not throw and should return true (evicts 1)
        dedup.TryAdd(4).ShouldBeTrue();
    }
}
