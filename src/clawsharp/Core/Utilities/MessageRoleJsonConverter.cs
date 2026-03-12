using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Core.Utilities;

/// <summary>
///     Serializes <see cref="MessageRole"/> as its underlying string value.
///     Needed because the Intellenum-generated converter is not picked up
///     by source-generated <see cref="JsonSerializerContext"/> contexts.
/// </summary>
internal sealed class MessageRoleJsonConverter : JsonConverter<MessageRole>
{
    public override MessageRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString()!;
            return MessageRole.TryFromValue(value, out var role) ? role : MessageRole.User;
        }

        // Skip over the broken `{}` tokens from legacy session files.
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            reader.Skip();
            return MessageRole.User;
        }

        return MessageRole.User;
    }

    public override void Write(Utf8JsonWriter writer, MessageRole value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}