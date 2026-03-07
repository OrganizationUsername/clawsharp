using System.Text.Json.Serialization;

namespace Clawsharp.Auth;

[JsonSerializable(typeof(OAuthToken))]
[JsonSerializable(typeof(GitHubDeviceCodeResponse))]
[JsonSerializable(typeof(GitHubAccessTokenResponse))]
[JsonSerializable(typeof(CopilotTokenResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class AuthJsonContext : JsonSerializerContext;