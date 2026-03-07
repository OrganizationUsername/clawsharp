using Microsoft.Extensions.Logging;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Utilities;

internal static partial class TaskExtensions
{
    /// <summary>
    /// Attaches a continuation that logs any unhandled exception so fire-and-forget
    /// tasks don't silently swallow faults.
    /// </summary>
    internal static Task LogExceptions(this Task task, ILogger logger, string? tag = null)
    {
        return task.ContinueWith(
            t =>
            {
                if (t.Exception is { } ex)
                {
                    LogFireAndForgetError(logger, ex, tag is not null ? $" [{tag}]" : "");
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled fire-and-forget exception{Tag}")]
    private static partial void LogFireAndForgetError(ILogger logger, Exception exception, string tag);
}