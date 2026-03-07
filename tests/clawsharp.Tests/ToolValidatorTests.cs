using System.Text.Json;
using Clawsharp.Tools;

namespace Clawsharp.Tests;

[TestFixture]
public sealed class ToolValidatorTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static readonly string FileReadSchema = """
                                                    {
                                                      "type": "object",
                                                      "properties": {
                                                        "path": { "type": "string", "description": "File path" }
                                                      },
                                                      "required": ["path"]
                                                    }
                                                    """;

    private static readonly string MultiParamSchema = """
                                                      {
                                                        "type": "object",
                                                        "properties": {
                                                          "path":    { "type": "string" },
                                                          "line":    { "type": "integer" },
                                                          "verbose": { "type": "boolean" },
                                                          "tags":    { "type": "array" },
                                                          "options": { "type": "object" },
                                                          "score":   { "type": "number" }
                                                        },
                                                        "required": ["path", "line"]
                                                      }
                                                      """;

    [Test]
    public void Validate_ValidArguments_ReturnsNull()
    {
        var schema = Parse(FileReadSchema);
        var args = Parse("""{"path": "foo.txt"}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldBeNull();
    }

    [Test]
    public void Validate_MissingRequiredProperty_ReturnsError()
    {
        var schema = Parse(FileReadSchema);
        var args = Parse("{}");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("missing required property 'path'");
    }

    [Test]
    public void Validate_MultipleMissingRequiredProperties_ListsAll()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("{}");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'path'");
        result.ShouldContain("'line'");
    }

    [Test]
    public void Validate_NonObjectArguments_ReturnsError()
    {
        var schema = Parse(FileReadSchema);
        var args = Parse("""["not", "an", "object"]""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("must be a JSON object");
    }

    [Test]
    public void Validate_WrongTypeStringExpected_ReturnsError()
    {
        var schema = Parse(FileReadSchema);
        var args = Parse("""{"path": 123}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'path' must be string");
    }

    [Test]
    public void Validate_WrongTypeIntegerExpected_ReturnsError()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": "not-a-number"}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'line' must be integer");
    }

    [Test]
    public void Validate_WrongTypeBooleanExpected_ReturnsError()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": 1, "verbose": "yes"}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'verbose' must be boolean");
    }

    [Test]
    public void Validate_WrongTypeArrayExpected_ReturnsError()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": 1, "tags": "not-array"}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'tags' must be array");
    }

    [Test]
    public void Validate_WrongTypeObjectExpected_ReturnsError()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": 1, "options": 42}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'options' must be object");
    }

    [Test]
    public void Validate_WrongTypeNumberExpected_ReturnsError()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": 1, "score": true}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldNotBeNull();
        result.ShouldContain("'score' must be number");
    }

    [Test]
    public void Validate_NullValueForOptionalProperty_ReturnsNull()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""{"path": "foo.txt", "line": 1, "verbose": null}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldBeNull();
    }

    [Test]
    public void Validate_ExtraPropertiesNotInSchema_ReturnsNull()
    {
        var schema = Parse(FileReadSchema);
        var args = Parse("""{"path": "foo.txt", "extra": 42}""");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldBeNull();
    }

    [Test]
    public void Validate_SchemaWithoutRequiredField_SkipsRequiredCheck()
    {
        var schema = Parse("""
                           {
                             "type": "object",
                             "properties": {
                               "query": { "type": "string" }
                             }
                           }
                           """);
        var args = Parse("{}");

        var result = ToolValidator.Validate(schema, args);

        result.ShouldBeNull();
    }

    [Test]
    public void Validate_SchemaWithoutPropertiesField_SkipsTypeCheck()
    {
        var schema = Parse("""
                           {
                             "type": "object",
                             "required": ["path"]
                           }
                           """);
        var args = Parse("""{"path": 42}""");

        var result = ToolValidator.Validate(schema, args);

        // Required check passes (path is present), no type check since no properties defined.
        result.ShouldBeNull();
    }

    [Test]
    public void Validate_AllCorrectTypes_ReturnsNull()
    {
        var schema = Parse(MultiParamSchema);
        var args = Parse("""
                         {
                           "path": "foo.txt",
                           "line": 10,
                           "verbose": true,
                           "tags": ["a", "b"],
                           "options": {"key": "val"},
                           "score": 3.14
                         }
                         """);

        var result = ToolValidator.Validate(schema, args);

        result.ShouldBeNull();
    }
}