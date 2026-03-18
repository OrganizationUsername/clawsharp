using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>
/// Union type for message content: either a plain string or a list of content parts.
/// The custom converter writes a JSON string for text-only content and a JSON array for
/// multimodal content (text + images), matching the OpenAI chat completions API format.
/// </summary>
[JsonConverter(typeof(MessageContentConverter))]
internal sealed class MessageContent
{
    /// <summary>Plain-text content (used when no images are present).</summary>
    public string? Text { get; init; }

    /// <summary>Multimodal content parts (used when images are present).</summary>
    public IReadOnlyList<ContentPart>? Parts { get; init; }

    /// <summary>Implicit conversion from string for convenience.</summary>
    public static implicit operator MessageContent(string text) => new() { Text = text };
}

/// <summary>Serialises MessageContent as either a plain JSON string or a JSON array of parts.</summary>
internal sealed class MessageContentConverter : JsonConverter<MessageContent>
{
    /// <inheritdoc />
    public override MessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new MessageContent { Text = null };
        }

        // For responses, content is typically a plain string.
        if (reader.TokenType == JsonTokenType.String)
        {
            return new MessageContent { Text = reader.GetString() ?? "" };
        }

        // MED-59: Handle array-type content (multimodal responses from OpenAI-compatible providers).
        // Parse each content part, extract text, and store original parts for round-tripping.
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var parts = JsonSerializer.Deserialize(ref reader, OpenAiJsonContext.Default.ListContentPart);
            if (parts is { Count: > 0 })
            {
                var texts = new List<string>(parts.Count);
                foreach (var part in parts)
                {
                    if (part is { Type: "text", Text.Length: > 0 })
                    {
                        texts.Add(part.Text);
                    }
                }

                var joinedText = "";
                if (texts.Count > 0)
                {
                    joinedText = string.Join("\n", texts);
                }

                return new MessageContent
                {
                    Text = joinedText,
                    Parts = parts
                };
            }

            return new MessageContent { Text = "" };
        }

        // Unknown token type — skip gracefully.
        reader.Skip();
        return new MessageContent { Text = "" };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MessageContent value, JsonSerializerOptions options)
    {
        if (value.Parts is null)
        {
            writer.WriteStringValue(value.Text ?? "");
            return;
        }

        writer.WriteStartArray();
        foreach (var part in value.Parts)
        {
            JsonSerializer.Serialize(writer, part, OpenAiJsonContext.Default.ContentPart);
        }

        writer.WriteEndArray();
    }
}