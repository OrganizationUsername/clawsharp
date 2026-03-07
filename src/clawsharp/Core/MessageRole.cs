using Intellenum;

namespace Clawsharp.Core;

/// <summary>Strongly-typed message role in a chat conversation.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class MessageRole
{
    /// <summary>System prompt role.</summary>
    public static readonly MessageRole System = new("system");

    /// <summary>User (human) role.</summary>
    public static readonly MessageRole User = new("user");

    /// <summary>Assistant (LLM) role.</summary>
    public static readonly MessageRole Assistant = new("assistant");

    /// <summary>Tool result role.</summary>
    public static readonly MessageRole Tool = new("tool");
}