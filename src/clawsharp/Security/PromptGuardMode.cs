using Intellenum;

namespace Clawsharp.Security;

/// <summary>Prompt injection guard action modes.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class PromptGuardMode
{
    /// <summary>Reject the message with an error reply.</summary>
    public static readonly PromptGuardMode Block = new("block");

    /// <summary>Log a warning and allow the message through.</summary>
    public static readonly PromptGuardMode Warn = new("warn");

    /// <summary>Replace matched injection text with [FILTERED] and forward.</summary>
    public static readonly PromptGuardMode Sanitize = new("sanitize");
}
