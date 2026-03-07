using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Mattermost;

// ── WebSocket DTOs ───────────────────────────────────────────────────

/// <summary>
/// Event received via Mattermost WebSocket connection.
/// </summary>
internal sealed class MattermostWsEvent
{
    /// <summary>Event type (e.g. "posted", "hello", "typing").</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>Event data. For "posted" events, the "post" field is a double-encoded JSON string.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>Broadcast info (channel_id, user_id, team_id).</summary>
    [JsonPropertyName("broadcast")]
    public JsonElement? Broadcast { get; init; }

    /// <summary>Sequence number.</summary>
    [JsonPropertyName("seq")]
    public int Seq { get; init; }
}

/// <summary>
/// Authentication challenge message sent on WebSocket connect.
/// </summary>
internal sealed class MattermostAuthChallenge
{
    [JsonPropertyName("seq")]
    public int Seq { get; init; }

    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("data")]
    public MattermostAuthData Data { get; init; } = new();
}

/// <summary>Auth data containing the bot token.</summary>
internal sealed class MattermostAuthData
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";
}

// ── REST API DTOs ────────────────────────────────────────────────────

/// <summary>Request body for creating a post via <c>POST /api/v4/posts</c>.</summary>
internal sealed class MattermostCreatePostRequest
{
    [JsonPropertyName("channel_id")]
    public string ChannelId { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

/// <summary>Response from creating or fetching a post.</summary>
internal sealed class MattermostPostResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }
}

/// <summary>Request body for updating a post via <c>PUT /api/v4/posts/{id}</c>.</summary>
internal sealed class MattermostUpdatePostRequest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

/// <summary>User object from <c>GET /api/v4/users/me</c>.</summary>
internal sealed class MattermostUser
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

// ── JSON Context ─────────────────────────────────────────────────────

[JsonSerializable(typeof(MattermostWsEvent))]
[JsonSerializable(typeof(MattermostAuthChallenge))]
[JsonSerializable(typeof(MattermostCreatePostRequest))]
[JsonSerializable(typeof(MattermostPostResponse))]
[JsonSerializable(typeof(MattermostUpdatePostRequest))]
[JsonSerializable(typeof(MattermostUser))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class MattermostJsonContext : JsonSerializerContext;