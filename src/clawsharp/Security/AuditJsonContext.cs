using System.Text.Json.Serialization;

namespace Clawsharp.Security;

[JsonSerializable(typeof(AuditEvent))]
[JsonSerializable(typeof(AuditActor))]
[JsonSerializable(typeof(AuditAction))]
[JsonSerializable(typeof(AuditResult))]
[JsonSerializable(typeof(AuditSecurity))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class AuditJsonContext : JsonSerializerContext;