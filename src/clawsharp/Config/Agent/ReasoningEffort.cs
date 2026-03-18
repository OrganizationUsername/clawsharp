using Intellenum;

namespace Clawsharp.Config.Agent;

/// <summary>OpenAI reasoning effort level for models that support extended thinking.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class ReasoningEffort
{
    public static readonly ReasoningEffort Low = new("low");
    public static readonly ReasoningEffort Medium = new("medium");
    public static readonly ReasoningEffort High = new("high");
}
