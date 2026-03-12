using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Matrix;

/// <summary>Request body for the Matrix login API (POST /_matrix/client/v3/login).</summary>
internal sealed class MatrixLoginRequest : IRequest<MatrixLoginRequest, MatrixLoginResponse>
{
    /// <summary>Login type (always "m.login.password").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "m.login.password";

    /// <summary>User identifier.</summary>
    [JsonPropertyName("identifier")]
    public MatrixUserIdentifier Identifier { get; init; } = new();

    /// <summary>Password for authentication.</summary>
    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixLoginRequest> RequestTypeInfo => MatrixJsonContext.Default.MatrixLoginRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixLoginResponse> ResponseTypeInfo => MatrixJsonContext.Default.MatrixLoginResponse;
}