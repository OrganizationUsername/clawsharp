namespace Clawsharp.Core.Pipeline;

/// <summary>Intercepts slash commands before they reach the LLM.</summary>
public static class SlashCommandRouter
{
    /// <summary>Maximum allowed length for the argument portion of a slash command.</summary>
    internal const int MaxArgumentLength = 10_000;

    /// <summary>
    ///     Attempts to parse a slash command. Returns a <see cref="SlashCommandResult" />,
    ///     or <c>null</c> if the text is not a slash command and should be forwarded to the LLM.
    /// </summary>
    public static SlashCommandResult? TryHandle(string? text)
    {
        return TryHandle(text, out _, out _);
    }

    /// <summary>
    ///     Attempts to parse a slash command. Returns a <see cref="SlashCommandResult" />,
    ///     or <c>null</c> if the text is not a slash command and should be forwarded to the LLM.
    ///     When the argument exceeds <see cref="MaxArgumentLength" />, <paramref name="errorMessage" />
    ///     contains the rejection reason.
    /// </summary>
    public static SlashCommandResult? TryHandle(string? text, out string? errorMessage)
    {
        return TryHandle(text, out errorMessage, out _);
    }

    /// <summary>
    ///     Attempts to parse a slash command. Returns a <see cref="SlashCommandResult" />,
    ///     or <c>null</c> if the text is not a slash command and should be forwarded to the LLM.
    ///     <paramref name="argument" /> contains the parsed argument for commands that carry data
    ///     (e.g. <c>/model gpt-4o</c>).
    /// </summary>
    public static SlashCommandResult? TryHandle(string? text, out string? errorMessage, out string? argument)
    {
        errorMessage = null;
        argument = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return null;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();
        string? arg = null;
        if (parts.Length > 1)
        {
            arg = parts[1];
        }

        if (arg is not null && arg.Length > MaxArgumentLength)
        {
            errorMessage = $"Error: command argument is too long (max {MaxArgumentLength:N0} characters).";
            return null;
        }

        var result = cmd switch
        {
            "/new" or "/clear" => SlashCommandResult.ClearSession,
            "/compact" => SlashCommandResult.TriggerCompaction,
            "/status" => SlashCommandResult.SendStatus,
            "/usage" or "/cost" => SlashCommandResult.ShowUsage,
            "/think" or "/verbose" => arg?.ToLowerInvariant() switch
            {
                "on" => SlashCommandResult.ThinkOn,
                "off" => SlashCommandResult.ThinkOff,
                _ => SlashCommandResult.ThinkToggle,
            },
            "/goals" => arg?.ToLowerInvariant() switch
            {
                "clear" => SlashCommandResult.ClearGoals,
                _ => SlashCommandResult.ShowGoals,
            },
            "/model" => SlashCommandResult.SetModel,
            "/models" => SlashCommandResult.ListModels,
            _ => (SlashCommandResult?)null, // unknown slash command -- let the LLM handle it
        };

        argument = arg;

        return result;
    }
}

/// <summary>Result of slash command interception.</summary>
public enum SlashCommandResult
{
    ClearSession,

    TriggerCompaction,

    SendStatus,

    ShowUsage,

    ThinkOn,

    ThinkOff,

    ThinkToggle,

    ShowGoals,

    ClearGoals,

    SetModel,

    ListModels,
}
