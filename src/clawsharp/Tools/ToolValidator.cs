using System.Text.Json;

namespace Clawsharp.Tools;

/// <summary>
/// Lightweight JSON Schema validator for tool call arguments.
/// Checks required properties and basic type constraints without a full schema engine.
/// </summary>
internal static class ToolValidator
{
    /// <summary>
    /// Validates tool call arguments against the tool's input schema.
    /// Returns null if valid, or an error message string if invalid.
    /// </summary>
    public static string? Validate(JsonElement schema, JsonElement arguments)
    {
        // 1. Arguments must be a JSON object.
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return $"Tool arguments must be a JSON object, got {arguments.ValueKind}.";
        }

        // 2. Check required properties.
        if (schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            var missing = new List<string>();
            foreach (var req in required.EnumerateArray())
            {
                var propName = req.GetString();
                if (propName is not null && !arguments.TryGetProperty(propName, out _))
                {
                    missing.Add($"missing required property '{propName}'");
                }
            }

            if (missing.Count > 0)
            {
                return $"Validation failed: {string.Join("; ", missing)}.";
            }
        }

        // 3. Check declared property types for properties that are present.
        if (schema.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object)
        {
            var typeErrors = new List<string>();
            foreach (var prop in props.EnumerateObject())
            {
                if (!arguments.TryGetProperty(prop.Name, out var argVal))
                {
                    continue;
                }

                // Null is acceptable for any type (the LLM may explicitly pass null for optional fields).
                if (argVal.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (!prop.Value.TryGetProperty("type", out var typeElem))
                {
                    continue;
                }

                var error = CheckType(prop.Name, argVal, typeElem.GetString());
                if (error is not null)
                {
                    typeErrors.Add(error);
                }
            }

            if (typeErrors.Count > 0)
            {
                return $"Validation failed: {string.Join("; ", typeErrors)}.";
            }
        }

        return null; // valid
    }

    private static string? CheckType(string name, JsonElement value, string? expectedType)
    {
        return expectedType switch
        {
            "string" when value.ValueKind != JsonValueKind.String =>
                $"property '{name}' must be string, got {value.ValueKind}",
            "number" when value.ValueKind != JsonValueKind.Number =>
                $"property '{name}' must be number, got {value.ValueKind}",
            "integer" when value.ValueKind != JsonValueKind.Number =>
                $"property '{name}' must be integer, got {value.ValueKind}",
            "boolean" when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) =>
                $"property '{name}' must be boolean, got {value.ValueKind}",
            "array" when value.ValueKind != JsonValueKind.Array =>
                $"property '{name}' must be array, got {value.ValueKind}",
            "object" when value.ValueKind != JsonValueKind.Object =>
                $"property '{name}' must be object, got {value.ValueKind}",
            _ => null
        };
    }
}