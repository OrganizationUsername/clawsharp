using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Config;

/// <summary>
/// Reads a <see cref="TimeSpan"/> from either a JSON number (interpreted as seconds) or a JSON
/// string (parsed as a TimeSpan via <see cref="TimeSpan.Parse(string)"/>). Writes as a TimeSpan
/// string.
/// <para>
/// This provides backward compatibility for config properties that were migrated from
/// <c>int</c>/<c>double</c> seconds (e.g. <c>"rateLimitWindowSeconds": 60</c>) to
/// <see cref="TimeSpan"/> (e.g. <c>"rateLimitWindow": "00:01:00"</c>). Users with old
/// configs using numeric values will still deserialize correctly.
/// </para>
/// </summary>
internal sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc />
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => TimeSpan.FromSeconds(reader.GetDouble()),
            JsonTokenType.String => TimeSpan.Parse(reader.GetString()!),
            _ => throw new JsonException(
                $"Cannot convert JSON token {reader.TokenType} to TimeSpan. " +
                "Expected a number (seconds) or a string (TimeSpan format like \"00:01:00\").")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
