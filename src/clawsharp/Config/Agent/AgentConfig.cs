using Microsoft.Extensions.Options;

namespace Clawsharp.Config.Agent;

/// <summary>Agent configuration section.</summary>
public sealed class AgentConfig
{
    /// <summary>Default agent settings.</summary>
    [ValidateObjectMembers]
    public AgentDefaults Defaults { get; init; } = new();
}