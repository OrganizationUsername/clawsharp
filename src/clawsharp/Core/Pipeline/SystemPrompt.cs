using System.Text;

namespace Clawsharp.Core.Pipeline;

public static class SystemPromptBuilder
{
    /// <summary>
    /// Builds the full system prompt as a single string.
    /// Kept for backward compatibility with tests and non-caching code paths.
    /// </summary>
    public static string Build(
        string? memoryContext = null,
        string? workspaceContext = null,
        string? channelName = null,
        IReadOnlyList<string>? enabledTools = null,
        string? activeGoalsContext = null)
    {
        var (staticPart, dynamicPart) = BuildSplit(memoryContext, workspaceContext, channelName, enabledTools, activeGoalsContext);
        return string.IsNullOrEmpty(dynamicPart)
            ? staticPart
            : staticPart + "\n\n" + dynamicPart;
    }

    /// <summary>
    /// Returns the system prompt split into two parts:
    /// <list type="bullet">
    ///   <item><term>StaticPart</term><description>Stable across all requests — safe to cache (identity, instructions, tools, memory).</description></item>
    ///   <item><term>DynamicPart</term><description>Changes every request — current datetime and channel name. Not cached.</description></item>
    /// </list>
    /// Providers that support prompt caching (Anthropic <c>cache_control</c>, OpenAI automatic prefix caching)
    /// should use the static part as the stable cacheable prefix.
    /// </summary>
    // PERF: StaticPart could be cached per-session if the inputs (memoryContext, workspaceContext,
    // enabledTools, activeGoalsContext) are guaranteed immutable within a session. Currently they
    // can change between messages (e.g., memory updates, goal changes) so caching requires either
    // input-hashing or explicit invalidation. Provider-level prompt caching (Anthropic/OpenAI)
    // already handles the heavy lifting at the API layer, so the benefit here is marginal.
    public static (string StaticPart, string DynamicPart) BuildSplit(
        string? memoryContext = null,
        string? workspaceContext = null,
        string? channelName = null,
        IReadOnlyList<string>? enabledTools = null,
        string? activeGoalsContext = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(workspaceContext))
        {
            sb.AppendLine(workspaceContext);
            sb.AppendLine();
        }

        sb.AppendLine("You are clawsharp, a helpful AI assistant running on the user's own hardware.");
        sb.AppendLine("Be concise, accurate, and helpful. When using tools, prefer the minimum necessary.");

        if (enabledTools is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"Available tools: {string.Join(", ", enabledTools)}");
        }

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            sb.AppendLine();
            sb.AppendLine("## Memory Context");
            sb.AppendLine(memoryContext);
        }

        if (!string.IsNullOrWhiteSpace(activeGoalsContext))
        {
            sb.AppendLine();
            sb.AppendLine("## Active Goals");
            sb.AppendLine(activeGoalsContext);
        }

        sb.AppendLine();
        sb.Append(GetPlatformContext());

        var staticPart = sb.ToString().TrimEnd();

        var dynSb = new StringBuilder();
        dynSb.AppendLine($"Current date/time (UTC): {DateTimeOffset.UtcNow:O}");
        if (channelName is not null)
        {
            dynSb.AppendLine($"Active channel: {channelName}");
        }

        var dynamicPart = dynSb.ToString().TrimEnd();

        return (staticPart, dynamicPart);
    }

    /// <summary>
    /// Returns platform-specific shell instructions so the LLM generates
    /// correct commands for the host operating system.
    /// </summary>
    internal static string GetPlatformContext()
    {
        if (OperatingSystem.IsWindows())
        {
            return """
                   ## Platform
                   Running on Windows. Use PowerShell or cmd.exe syntax for shell commands.
                   - Use `dir` instead of `ls`, `type` instead of `cat`
                   - Path separator is `\` (backslash)
                   - Use `$env:VAR` for environment variables in PowerShell
                   """;
        }

        if (OperatingSystem.IsMacOS())
        {
            return """
                   ## Platform
                   Running on macOS. Use bash/zsh POSIX shell syntax.
                   - Default shell is zsh
                   - Use `brew` for package management
                   - Path separator is `/`
                   """;
        }

        // Linux or other POSIX
        return """
               ## Platform
               Running on Linux. Use bash/POSIX shell syntax.
               - Path separator is `/`
               - Common package managers: apt, yum, dnf, pacman
               """;
    }
}