using Clawsharp.Channels;
using Microsoft.Extensions.Logging;
using Clawsharp.Core.Services;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Pipeline;

/// <summary>Wraps AgentLoop as a Generic Host BackgroundService.</summary>
public sealed partial class AgentLoopService : LifecycleBackgroundService
{
    private readonly AgentLoop _agentLoop;

    private readonly IMessageBus _bus;

    private readonly IReadOnlyList<IChannel> _channels;

    private readonly ILogger<AgentLoopService> _logger;

    public AgentLoopService(AgentLoop agentLoop, IReadOnlyList<IChannel> channels, IMessageBus bus, ILogger<AgentLoopService> logger)
    {
        _agentLoop = agentLoop;
        _channels = channels;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogActiveChannels(_logger, string.Join(", ", _channels.Select(c => c.Name.Value)));

        var nonCli = _channels.Where(c => c.Name != ChannelName.Cli).ToList();
        if (nonCli.Count > 1)
        {
            LogMultipleChannelsWarning(_logger, string.Join(", ", nonCli.Select(c => c.Name.Value)));
        }

        try
        {
            await _agentLoop.RunAsync(_bus, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            /* clean shutdown */
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Channels: {Channels}")]
    private static partial void LogActiveChannels(ILogger logger, string channels);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Multiple messaging channels are active: {Channels}. " +
                  "Conversation context is NOT shared across channels -- the same user will have separate, " +
                  "out-of-sync histories on each platform. " +
                  "Enable only one channel at a time until multi-channel context is implemented.")]
    private static partial void LogMultipleChannelsWarning(ILogger logger, string channels);
}