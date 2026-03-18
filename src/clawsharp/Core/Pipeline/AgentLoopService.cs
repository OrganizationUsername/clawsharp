using Clawsharp.Channels;
using Microsoft.Extensions.Logging;
using Clawsharp.Core.Services;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Pipeline;

/// <summary>Wraps AgentLoop as a Generic Host BackgroundService.</summary>
public sealed partial class AgentLoopService(
    AgentLoop agentLoop,
    IReadOnlyList<IChannel> channels,
    IMessageBus bus,
    ILogger<AgentLoopService> logger)
    : LifecycleBackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogActiveChannels(logger, string.Join(", ", channels.Select(c => c.Name.Value)));

        var nonCli = channels.Where(c => c.Name != ChannelName.Cli).ToList();
        if (nonCli.Count > 1)
        {
            LogMultipleChannelsWarning(logger, string.Join(", ", nonCli.Select(c => c.Name.Value)));
        }

        try
        {
            await agentLoop.RunAsync(bus, stoppingToken);
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