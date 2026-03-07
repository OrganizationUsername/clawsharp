using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Config.Agent;

/// <summary>
/// A fallback model entry that can be specified as either a plain string (provider name)
/// or an object with explicit overrides for API key, model, base URL, and auth header.
/// </summary>
[JsonConverter(typeof(FallbackModelEntryConverter))]
public sealed class FallbackModelEntry
{
    /// <summary>
    /// Provider name — must be a key in the <c>providers</c> config section.
    /// When specified as a plain string, this is the only field populated.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    /// <summary>
    /// Optional model override. When set, this model is used instead of whatever
    /// is configured as the default model for the agent.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Optional API key override. When set, this key is used instead of the
    /// provider's configured API key, enabling per-fallback authentication.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional base URL override. When set, this URL is used instead of the
    /// provider's configured base URL.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional auth header override. When set, this header name is used instead of
    /// the provider's configured auth header (e.g. "api-key" for Azure OpenAI).
    /// </summary>
    [JsonPropertyName("authHeader")]
    public string? AuthHeader { get; set; }
}

/// <summary>
/// Converts a fallback model entry from either a plain JSON string (provider name)
/// or a JSON object with explicit fields. Supports backward compatibility with the
/// original <c>List&lt;string&gt;</c> format.
/// </summary>
public sealed class FallbackModelEntryConverter : JsonConverter<FallbackModelEntry>
{
    public override FallbackModelEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new FallbackModelEntry { Provider = reader.GetString() ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var entry = new FallbackModelEntry();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return entry;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var propertyName = reader.GetString() ?? "";
                reader.Read();

                switch (propertyName.ToLowerInvariant())
                {
                    case "provider":
                        entry.Provider = reader.GetString() ?? "";
                        break;
                    case "model":
                        entry.Model = reader.GetString();
                        break;
                    case "apikey" or "api_key":
                        entry.ApiKey = reader.GetString();
                        break;
                    case "baseurl" or "base_url":
                        entry.BaseUrl = reader.GetString();
                        break;
                    case "authheader" or "auth_header":
                        entry.AuthHeader = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return entry;
        }

        throw new JsonException($"Expected string or object for FallbackModelEntry, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, FallbackModelEntry value, JsonSerializerOptions options)
    {
        // If only provider is set, write as plain string for compact output
        if (value.ApiKey is null && value.Model is null && value.BaseUrl is null && value.AuthHeader is null)
        {
            writer.WriteStringValue(value.Provider);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("provider", value.Provider);

        if (value.Model is not null)
        {
            writer.WriteString("model", value.Model);
        }

        if (value.ApiKey is not null)
        {
            writer.WriteString("apiKey", value.ApiKey);
        }

        if (value.BaseUrl is not null)
        {
            writer.WriteString("baseUrl", value.BaseUrl);
        }

        if (value.AuthHeader is not null)
        {
            writer.WriteString("authHeader", value.AuthHeader);
        }

        writer.WriteEndObject();
    }
}