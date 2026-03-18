using Clawsharp.Config.Security;
using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class PasswordManagerResolverTests
{
    private static SecretsConfig DefaultConfig() => new();

    // ── IsReference ───────────────────────────────────────────────────

    [Test]
    public void IsReference_Null_ReturnsFalse()
    {
        PasswordManagerResolver.IsReference(null).ShouldBeFalse();
    }

    [Test]
    public void IsReference_EmptyString_ReturnsFalse()
    {
        PasswordManagerResolver.IsReference("").ShouldBeFalse();
    }

    [Test]
    public void IsReference_PlainApiKey_ReturnsFalse()
    {
        PasswordManagerResolver.IsReference("sk-abc123xyz").ShouldBeFalse();
    }

    [Test]
    public void IsReference_OpPrefix_ReturnsTrue()
    {
        PasswordManagerResolver.IsReference("op://vault/item/field").ShouldBeTrue();
    }

    [Test]
    public void IsReference_BwsPrefix_ReturnsTrue()
    {
        PasswordManagerResolver.IsReference("bws:be8e0ad8-d545-4017-a55a-b02f014d4158").ShouldBeTrue();
    }

    [Test]
    public void IsReference_PartialOpPrefix_ReturnsFalse()
    {
        PasswordManagerResolver.IsReference("op:/vault/item").ShouldBeFalse();
    }

    // ── Resolve passthrough ───────────────────────────────────────────

    [Test]
    public void Resolve_PlainValue_ReturnedUnchanged()
    {
        // Non-reference values must be returned as-is without touching the PM CLI
        var result = PasswordManagerResolver.Resolve("sk-plain-api-key-12345", DefaultConfig());

        result.ShouldBe("sk-plain-api-key-12345");
    }

    [Test]
    public void Resolve_EmptyString_ReturnedUnchanged()
    {
        var result = PasswordManagerResolver.Resolve("", DefaultConfig());
        result.ShouldBe("");
    }

    // ── RunProcess binary validation ──────────────────────────────────

    [Test]
    public void Resolve_OpReference_FakeTokenMissing_ThrowsInvalidOperation()
    {
        // With no OP_SERVICE_ACCOUNT_TOKEN env var set, Resolve must throw
        // before attempting to spawn the process.
        var savedToken = Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", null);

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve("op://vault/item/field", DefaultConfig()));

            // Error must reference the missing env var
            ex.Message.ShouldContain("OP_SERVICE_ACCOUNT_TOKEN");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", savedToken);
        }
    }

    [Test]
    public void Resolve_BwsReference_TokenMissing_ThrowsInvalidOperation()
    {
        var savedToken = Environment.GetEnvironmentVariable("BWS_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("BWS_ACCESS_TOKEN", null);

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve(
                    "bws:be8e0ad8-d545-4017-a55a-b02f014d4158", DefaultConfig()));

            ex.Message.ShouldContain("BWS_ACCESS_TOKEN");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BWS_ACCESS_TOKEN", savedToken);
        }
    }

    [Test]
    public void Resolve_DisallowedBinaryPath_ThrowsInvalidOperation()
    {
        // /tmp/op is not in the allowed directories list
        var savedToken = Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", "fake-token");

            var config = new SecretsConfig
            {
                OnePassword = new OnePasswordConfig { CliBinary = "/tmp/op" }
            };

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve("op://vault/item/field", config));

            // Disallowed binary directory must produce a clear error
            ex.Message.ShouldContain("allowed directory");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", savedToken);
        }
    }

    [Test]
    public void Resolve_PathTraversalInBinary_ThrowsInvalidOperation()
    {
        // /usr/bin/../bin/op contains .. — must be rejected
        var savedToken = Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", "fake-token");

            var config = new SecretsConfig
            {
                OnePassword = new OnePasswordConfig { CliBinary = "/usr/bin/../bin/op" }
            };

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve("op://vault/item/field", config));

            // Path traversal in binary path must produce a clear error
            ex.Message.ShouldContain("path traversal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", savedToken);
        }
    }

    [Test]
    public void Resolve_RelativeBinaryPath_ThrowsInvalidOperation()
    {
        // A relative path like "../op" or "bin/op" must be rejected (not bare name, not absolute)
        var savedToken = Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", "fake-token");

            var config = new SecretsConfig
            {
                OnePassword = new OnePasswordConfig { CliBinary = "./bin/op" }
            };

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve("op://vault/item/field", config));

            ex.Message.ShouldContain("absolute path");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", savedToken);
        }
    }

    [Test]
    public void Resolve_DisallowedBinaryName_ThrowsInvalidOperation()
    {
        // "malicious" is not in AllowedBinaries ("op", "bws")
        var savedToken = Environment.GetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", "fake-token");

            var config = new SecretsConfig
            {
                OnePassword = new OnePasswordConfig { CliBinary = "malicious" }
            };

            var ex = Should.Throw<InvalidOperationException>(() =>
                PasswordManagerResolver.Resolve("op://vault/item/field", config));

            ex.Message.ShouldContain("malicious");
            ex.Message.ShouldContain("allowed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OP_SERVICE_ACCOUNT_TOKEN", savedToken);
        }
    }
}
