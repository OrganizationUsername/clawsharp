using Clawsharp.Channels;

namespace Clawsharp.Tests.Unit.Channels;

public sealed class AllowListPolicyTests
{
    [Test]
    public void NullList_AllowsAll()
    {
        var policy = new AllowListPolicy(allowFrom: null);
        policy.IsAllowAll.ShouldBeTrue();
        policy.IsAllowed("anyone").ShouldBeTrue();
    }

    [Test]
    public void EmptyList_DeniesAll()
    {
        var policy = new AllowListPolicy(allowFrom: []);
        policy.IsAllowAll.ShouldBeFalse();
        policy.IsAllowed("anyone").ShouldBeFalse();
    }

    [Test]
    public void WildcardEntry_AllowsAll()
    {
        var policy = new AllowListPolicy(allowFrom: ["*"]);
        policy.IsAllowAll.ShouldBeTrue();
        policy.IsAllowed("anyone").ShouldBeTrue();
    }

    [Test]
    public void WildcardMixedWithOthers_AllowsAll()
    {
        var policy = new AllowListPolicy(allowFrom: ["alice", "*", "bob"]);
        policy.IsAllowAll.ShouldBeTrue();
        policy.IsAllowed("charlie").ShouldBeTrue();
    }

    [Test]
    public void SpecificIds_AllowOnlyListed()
    {
        var policy = new AllowListPolicy(allowFrom: ["alice", "bob"]);
        policy.IsAllowAll.ShouldBeFalse();
        policy.IsAllowed("alice").ShouldBeTrue();
        policy.IsAllowed("bob").ShouldBeTrue();
        policy.IsAllowed("charlie").ShouldBeFalse();
    }

    [Test]
    public void DefaultComparer_IsCaseInsensitive()
    {
        var policy = new AllowListPolicy(allowFrom: ["Alice"]);
        policy.IsAllowed("alice").ShouldBeTrue();
        policy.IsAllowed("ALICE").ShouldBeTrue();
        policy.IsAllowed("Alice").ShouldBeTrue();
    }

    [Test]
    public void CaseSensitiveComparer_RespectsCase()
    {
        var policy = new AllowListPolicy(
            allowFrom: ["Alice"],
            comparer: StringComparer.Ordinal);

        policy.IsAllowed("Alice").ShouldBeTrue();
        policy.IsAllowed("alice").ShouldBeFalse();
        policy.IsAllowed("ALICE").ShouldBeFalse();
    }

    [Test]
    public void Transform_AppliedToEntries()
    {
        var policy = new AllowListPolicy(
            allowFrom: ["@alice", "@bob"],
            transform: entry => entry.TrimStart('@'));

        policy.IsAllowed("alice").ShouldBeTrue();
        policy.IsAllowed("bob").ShouldBeTrue();
        policy.IsAllowed("@alice").ShouldBeFalse();
    }

    [Test]
    public void AllowAll_StaticInstance_AllowsEveryone()
    {
        AllowListPolicy.AllowAll.IsAllowAll.ShouldBeTrue();
        AllowListPolicy.AllowAll.IsAllowed("anyone").ShouldBeTrue();
    }
}
