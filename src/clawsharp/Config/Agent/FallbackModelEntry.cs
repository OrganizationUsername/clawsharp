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
    public string Provider { get; init; } = "";

    /// <summary>
    /// Optional model override. When set, this model is used instead of whatever
    /// is configured as the default model for the agent.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Optional API key override. When set, this key is used instead of the
    /// provider's configured API key, enabling per-fallback authentication.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// Optional base URL override. When set, this URL is used instead of the
    /// provider's configured base URL.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Optional auth header override. When set, this header name is used instead of
    /// the provider's configured auth header (e.g. "api-key" for Azure OpenAI).
    /// </summary>
    [JsonPropertyName("authHeader")]
    public string? AuthHeader { get; init; }
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
            string provider = "";
            string? model = null;
            string? apiKey = null;
            string? baseUrl = null;
            string? authHeader = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new FallbackModelEntry
                    {
                        Provider = provider,
                        Model = model,
                        ApiKey = apiKey,
                        BaseUrl = baseUrl,
                        AuthHeader = authHeader,
                    };
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
                        provider = reader.GetString() ?? "";
                        break;
                    case "model":
                        model = reader.GetString();
                        break;
                    case "apikey" or "api_key":
                        apiKey = reader.GetString();
                        break;
                    case "baseurl" or "base_url":
                        baseUrl = reader.GetString();
                        break;
                    case "authheader" or "auth_header":
                        authHeader = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new FallbackModelEntry
            {
                Provider = provider,
                Model = model,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                AuthHeader = authHeader,
            };
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