using System.Text.Json.Nodes;
using Clawsharp.Cli.Migrate;

namespace Clawsharp.Tests.Unit.Cli;

/// <summary>Tests for <see cref="MigrateCommand"/> pure helper functions: TOML parsing, GetNode, SetNode.</summary>
public sealed class MigrateCommandTests
{
    // ── ParseToml ────────────────────────────────────────────────────────────

    [Test]
    public void ParseToml_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = MigrateCommand.ParseToml("");

        result.ShouldBeEmpty();
    }

    [Test]
    public void ParseToml_CommentsOnly_ReturnsEmptyDictionary()
    {
        var toml = """
                   # This is a comment
                   # Another comment
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result.ShouldBeEmpty();
    }

    [Test]
    public void ParseToml_TopLevelKeyValue_ParsesCorrectly()
    {
        var toml = """
                   name = "clawsharp"
                   version = "1.0"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result.ShouldContainKey("name");
        result["name"].ShouldBe("clawsharp");
        result["version"].ShouldBe("1.0");
    }

    [Test]
    public void ParseToml_SectionedKeys_PrefixesWithSection()
    {
        var toml = """
                   [agent]
                   model = "claude-sonnet-4-20250514"
                   provider = "anthropic"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result.ShouldContainKey("agent.model");
        result["agent.model"].ShouldBe("claude-sonnet-4-20250514");
        result["agent.provider"].ShouldBe("anthropic");
    }

    [Test]
    public void ParseToml_NestedSections_HandlesSubsections()
    {
        var toml = """
                   [providers.anthropic]
                   api_key = "sk-ant-123"

                   [providers.openai]
                   api_key = "sk-openai-456"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["providers.anthropic.api_key"].ShouldBe("sk-ant-123");
        result["providers.openai.api_key"].ShouldBe("sk-openai-456");
    }

    [Test]
    public void ParseToml_UnquotedValues_StripsInlineComments()
    {
        var toml = """
                   [agent]
                   max_tool_iterations = 10 # maximum iterations
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["agent.max_tool_iterations"].ShouldBe("10");
    }

    [Test]
    public void ParseToml_QuotedValueWithHash_PreservesHashInside()
    {
        var toml = """
                   [agent]
                   system_prompt = "You are #1 assistant"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["agent.system_prompt"].ShouldBe("You are #1 assistant");
    }

    [Test]
    public void ParseToml_SingleQuotedValues_ParsesCorrectly()
    {
        var toml = """
                   [agent]
                   model = 'gpt-4o'
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["agent.model"].ShouldBe("gpt-4o");
    }

    [Test]
    public void ParseToml_BlankLines_SkipsGracefully()
    {
        var toml = """
                   [agent]

                   model = "test"

                   [tools]

                   workspace = "/home/user"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result.Count.ShouldBe(2);
        result["agent.model"].ShouldBe("test");
        result["tools.workspace"].ShouldBe("/home/user");
    }

    [Test]
    public void ParseToml_LinesWithoutEquals_SkipsGracefully()
    {
        var toml = """
                   [agent]
                   model = "test"
                   this line has no equals sign
                   provider = "openai"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result.Count.ShouldBe(2);
        result.ShouldContainKey("agent.model");
        result.ShouldContainKey("agent.provider");
    }

    [Test]
    public void ParseToml_CaseInsensitiveLookup()
    {
        var toml = """
                   [Agent]
                   Model = "gpt-4o"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        // The dictionary uses OrdinalIgnoreCase, so lookups are case-insensitive.
        result.ShouldContainKey("agent.model");
        result.ShouldContainKey("Agent.Model");
    }

    [Test]
    public void ParseToml_BooleanValues_ParsedAsStrings()
    {
        var toml = """
                   [tools]
                   require_shell_approval = true
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["tools.require_shell_approval"].ShouldBe("true");
    }

    [Test]
    public void ParseToml_ArrayValue_PreservedAsRawString()
    {
        var toml = """
                   [channels.telegram]
                   allow_from = ["123", "456"]
                   """;

        var result = MigrateCommand.ParseToml(toml);

        // The value is stored as a raw string (TOML array literal).
        result["channels.telegram.allow_from"].ShouldContain("123");
        result["channels.telegram.allow_from"].ShouldContain("456");
    }

    // ── ParseTomlStringArray ────────────────────────────────────────────────

    [Test]
    public void ParseTomlStringArray_StandardArray_ParsesItems()
    {
        var result = MigrateCommand.ParseTomlStringArray("""["alice", "bob", "charlie"]""");

        result.Count.ShouldBe(3);
        result[0].ShouldBe("alice");
        result[1].ShouldBe("bob");
        result[2].ShouldBe("charlie");
    }

    [Test]
    public void ParseTomlStringArray_SingleQuoted_ParsesItems()
    {
        var result = MigrateCommand.ParseTomlStringArray("['a', 'b']");

        result.Count.ShouldBe(2);
        result[0].ShouldBe("a");
        result[1].ShouldBe("b");
    }

    [Test]
    public void ParseTomlStringArray_EmptyArray_ReturnsEmpty()
    {
        var result = MigrateCommand.ParseTomlStringArray("[]");

        result.ShouldBeEmpty();
    }

    [Test]
    public void ParseTomlStringArray_ExtraWhitespace_TrimsCorrectly()
    {
        var result = MigrateCommand.ParseTomlStringArray("""[  "abc" , "def"  ]""");

        result.Count.ShouldBe(2);
        result[0].ShouldBe("abc");
        result[1].ShouldBe("def");
    }

    [Test]
    public void ParseTomlStringArray_NumericIds_ParsesAsStrings()
    {
        // Telegram user IDs are often numeric strings in TOML arrays.
        var result = MigrateCommand.ParseTomlStringArray("""["123456789", "987654321"]""");

        result.Count.ShouldBe(2);
        result[0].ShouldBe("123456789");
        result[1].ShouldBe("987654321");
    }

    // ── GetNode ─────────────────────────────────────────────────────────────

    [Test]
    public void GetNode_TopLevelKey_ReturnsValue()
    {
        var root = JsonNode.Parse("""{"name": "test"}""")!;

        var result = MigrateCommand.GetNode(root, "name");

        result.ShouldNotBeNull();
        result.GetValue<string>().ShouldBe("test");
    }

    [Test]
    public void GetNode_NestedKey_ReturnsValue()
    {
        var root = JsonNode.Parse("""{"agents": {"defaults": {"model": "gpt-4o"}}}""")!;

        var result = MigrateCommand.GetNode(root, "agents.defaults.model");

        result.ShouldNotBeNull();
        result.GetValue<string>().ShouldBe("gpt-4o");
    }

    [Test]
    public void GetNode_MissingKey_ReturnsNull()
    {
        var root = JsonNode.Parse("""{"name": "test"}""")!;

        var result = MigrateCommand.GetNode(root, "nonexistent");

        result.ShouldBeNull();
    }

    [Test]
    public void GetNode_MissingNestedKey_ReturnsNull()
    {
        var root = JsonNode.Parse("""{"agents": {"defaults": {}}}""")!;

        var result = MigrateCommand.GetNode(root, "agents.defaults.model");

        result.ShouldBeNull();
    }

    [Test]
    public void GetNode_PathThroughNonObject_ReturnsNull()
    {
        var root = JsonNode.Parse("""{"name": "test"}""")!;

        // "name" is a string, not an object, so navigating further should return null.
        var result = MigrateCommand.GetNode(root, "name.sub");

        result.ShouldBeNull();
    }

    // ── SetNode ─────────────────────────────────────────────────────────────

    [Test]
    public void SetNode_TopLevelKey_SetsValue()
    {
        var root = new JsonObject();

        MigrateCommand.SetNode(root, "name", JsonValue.Create("test")!);

        root["name"]!.GetValue<string>().ShouldBe("test");
    }

    [Test]
    public void SetNode_NestedKey_CreatesIntermediateObjects()
    {
        var root = new JsonObject();

        MigrateCommand.SetNode(root, "agents.defaults.model", JsonValue.Create("gpt-4o")!);

        var model = MigrateCommand.GetNode(root, "agents.defaults.model");
        model.ShouldNotBeNull();
        model.GetValue<string>().ShouldBe("gpt-4o");
    }

    [Test]
    public void SetNode_OverwritesExistingValue()
    {
        var root = JsonNode.Parse("""{"agents": {"defaults": {"model": "old"}}}""") as JsonObject;

        MigrateCommand.SetNode(root!, "agents.defaults.model", JsonValue.Create("new")!);

        MigrateCommand.GetNode(root!, "agents.defaults.model")!.GetValue<string>().ShouldBe("new");
    }

    [Test]
    public void SetNode_DeeplyNested_CreatesAllLevels()
    {
        var root = new JsonObject();

        MigrateCommand.SetNode(root, "a.b.c.d.e", JsonValue.Create(42)!);

        MigrateCommand.GetNode(root, "a.b.c.d.e")!.GetValue<int>().ShouldBe(42);
    }

    // ── Full TOML parsing scenario (zeroclaw-style config) ──────────────────

    [Test]
    public void ParseToml_FullZeroClawConfig_ParsesCorrectly()
    {
        var toml = """
                   # zeroclaw configuration

                   [agent]
                   provider = "claude"
                   model = "claude-sonnet-4-20250514"
                   system_prompt = "You are a helpful assistant."
                   max_tool_iterations = 15

                   [providers.anthropic]
                   api_key = "sk-ant-api03-xxxx"

                   [providers.openai]
                   api_key = "sk-proj-yyyy"

                   [channels.telegram]
                   token = "12345:ABCdefGHI"
                   allow_from = ["111222333", "444555666"]

                   [channels.discord]
                   bot_token = "MTIzNDU2.xxx"
                   guild_id = "123456789"

                   [tools]
                   workspace = "~/workspace"
                   brave_api_key = "BSA_zzz"
                   """;

        var result = MigrateCommand.ParseToml(toml);

        result["agent.provider"].ShouldBe("claude");
        result["agent.model"].ShouldBe("claude-sonnet-4-20250514");
        result["agent.system_prompt"].ShouldBe("You are a helpful assistant.");
        result["agent.max_tool_iterations"].ShouldBe("15");
        result["providers.anthropic.api_key"].ShouldBe("sk-ant-api03-xxxx");
        result["providers.openai.api_key"].ShouldBe("sk-proj-yyyy");
        result["channels.telegram.token"].ShouldBe("12345:ABCdefGHI");
        result["channels.discord.bot_token"].ShouldBe("MTIzNDU2.xxx");
        result["channels.discord.guild_id"].ShouldBe("123456789");
        result["tools.workspace"].ShouldBe("~/workspace");
        result["tools.brave_api_key"].ShouldBe("BSA_zzz");

        // Allow from is stored as raw string; parse it separately.
        var allowFrom = MigrateCommand.ParseTomlStringArray(result["channels.telegram.allow_from"]);
        allowFrom.Count.ShouldBe(2);
        allowFrom[0].ShouldBe("111222333");
        allowFrom[1].ShouldBe("444555666");
    }

    [Test]
    public void ParseToml_WindowsLineEndings_ParsesCorrectly()
    {
        // ParseToml splits on \n, so \r\n should still work (line.Trim() handles \r).
        var toml = "[agent]\r\nmodel = \"gpt-4o\"\r\nprovider = \"openai\"\r\n";

        var result = MigrateCommand.ParseToml(toml);

        result["agent.model"].ShouldBe("gpt-4o");
        result["agent.provider"].ShouldBe("openai");
    }
}