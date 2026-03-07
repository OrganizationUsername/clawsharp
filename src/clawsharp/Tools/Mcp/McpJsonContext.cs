using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

[JsonSerializable(typeof(McpRequest))]
[JsonSerializable(typeof(McpResponse))]
[JsonSerializable(typeof(McpToolSchema))]
[JsonSerializable(typeof(McpToolsListResult))]
[JsonSerializable(typeof(McpCallToolResult))]
[JsonSerializable(typeof(McpContentBlock))]
[JsonSerializable(typeof(McpError))]
[JsonSerializable(typeof(List<McpToolSchema>))]
[JsonSerializable(typeof(List<McpContentBlock>))]
[JsonSerializable(typeof(McpInitializeParams))]
[JsonSerializable(typeof(McpClientInfo))]
[JsonSerializable(typeof(McpCapabilities))]
[JsonSerializable(typeof(McpCallToolParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class McpJsonContext : JsonSerializerContext;