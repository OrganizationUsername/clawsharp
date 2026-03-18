using Intellenum;

namespace Clawsharp.Config;

/// <summary>Well-known Feishu/Lark API domain identifiers.</summary>
[Intellenum<string>]
internal partial class FeishuDomain
{
    /// <summary>International Lark domain (open.larksuite.com).</summary>
    public static readonly FeishuDomain Lark = new("lark");
}
