using System.Diagnostics;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Features.Behaviors;

/// <summary>
/// Cross-cutting behavior that logs handler execution with timing.
/// Applied globally via assembly-level <see cref="BehaviorsAttribute"/>.
/// </summary>
public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger
) : Behavior<TRequest, TResponse>
{
    public override async ValueTask<TResponse> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        var handlerName = HandlerType.Name;
        var requestName = typeof(TRequest).Name;

        LogHandlingHandlerWithRequest(handlerName, requestName);
        var timestamp = Stopwatch.GetTimestamp();

        var response = await Next(request, cancellationToken);

        var elapsedTime = Stopwatch.GetElapsedTime(timestamp);
        LogHandledHandlerInElapsedmsMs(handlerName, elapsedTime.Milliseconds);

        return response;
    }

    [LoggerMessage(LogLevel.Debug, "Handling {Handler} with {Request}")]
    partial void LogHandlingHandlerWithRequest(string handler, string request);

    [LoggerMessage(LogLevel.Debug, "Handled {Handler} in {ElapsedMs}ms")]
    partial void LogHandledHandlerInElapsedmsMs(string handler, int elapsedMs);
}