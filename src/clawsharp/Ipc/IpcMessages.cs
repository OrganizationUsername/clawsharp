using System.Text.Json.Serialization;

namespace Clawsharp.Ipc;

internal sealed record IpcRequest(string Command, string? Token = null);

internal sealed record IpcResponse(string? Code, string? Error, bool Cleared);

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJsonContext : JsonSerializerContext;