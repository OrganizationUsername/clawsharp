using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;

namespace Clawsharp.Channels.Discord;

/// <summary>Captures the bot's own user ID when the READY event fires.</summary>
public sealed partial class DiscordReadyResponder : IResponder<IReady>
{
    private readonly ILogger<DiscordReadyResponder> _logger;

    private readonly DiscordBotState _state;

    public DiscordReadyResponder(DiscordBotState state, ILogger<DiscordReadyResponder> logger)
    {
        _state = state;
        _logger = logger;
    }

    public Task<Result> RespondAsync(IReady gatewayEvent, CancellationToken ct = default)
    {
        _state.SelfId = gatewayEvent.User.ID;
        LogBotReady(_logger, gatewayEvent.User.ID, gatewayEvent.User.Username);
        return Task.FromResult(Result.FromSuccess());
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Ready. Bot ID: {BotId} (@{Username})")]
    private static partial void LogBotReady(ILogger logger, Snowflake botId, string username);
}