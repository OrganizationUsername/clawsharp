using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Config.Channels;

/// <summary>
/// Reads JSON arrays that may contain either strings ("@alice", "12345") or bare numbers (12345),
/// and normalises all elements to strings. This allows backward-compatible migration from the
/// old List&lt;long&gt; allowFrom format.
/// </summary>
internal sealed class AllowListConverter : JsonConverter<List<string>?>
{
    /// <inheritdoc />
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected array for allowFrom.");
        }

        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(reader.GetString()!);
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                list.Add(reader.GetInt64().ToString());
            }
            else
            {
                throw new JsonException($"Unexpected token {reader.TokenType} in allowFrom array.");
            }
        }

        return list;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var s in value)
        {
            writer.WriteStringValue(s);
        }

        writer.WriteEndArray();
    }
}