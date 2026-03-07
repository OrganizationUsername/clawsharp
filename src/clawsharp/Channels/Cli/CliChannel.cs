using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace Clawsharp.Channels.Cli;

public sealed partial class CliChannel(
    IOptions<AppConfig> options,
    IMessageBus bus,
    ILogger<CliChannel> logger) : LifecycleBackgroundService, IStreamingChannel
{
    private readonly AppConfig _config = options.Value;

    public ChannelName Name => ChannelName.Cli;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = _config.Channels.GetValueOrDefault(ChannelName.Cli.Value);
        var cliEnabled = cfg?.Enabled == true;
        var anyOtherEnabled = _config.Channels.Any(kv => kv.Key != ChannelName.Cli.Value && kv.Value.Enabled);

        // Skip if another channel is configured; run if explicitly enabled or as fallback
        if (!cliEnabled && anyOtherEnabled)
        {
            return;
        }

        // Console.ReadLine() is blocking, so Task.Run is still needed to avoid
        // blocking the host startup pipeline.
        // Force EOF on the blocking read when shutdown is requested.
        stoppingToken.Register(() => Console.In.Close());

        await Task.Run(async () =>
        {
            AnsiConsole.MarkupLine("[cyan]clawsharp[/] — type your message, Ctrl+C to exit\n");
            AnsiConsole.Markup("[green]> [/]");
            await RunMessageLoopAsync(stoppingToken);
        }, stoppingToken);
    }

    private async Task RunMessageLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line is null)
            {
                break; // EOF (or Console.In.Close() from shutdown)
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                AnsiConsole.Markup("[green]> [/]");
                continue;
            }

            try
            {
                await bus.PublishAsync(new InboundMessage(
                    Channel: Name,
                    SenderId: "cli-user",
                    SenderName: "User",
                    Text: line
                ), stoppingToken);
                // The next "> " prompt is printed by SendAsync/StreamAsync after the response.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogCliError(ex);
                AnsiConsole.Markup("[green]> [/]");
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Error processing CLI input")]
    private partial void LogCliError(Exception exception);

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        AnsiConsole.Markup("[blue]Assistant:[/] ");
        AnsiConsole.MarkupLine(Markup.Escape(message.Text.TrimStart()));
        AnsiConsole.Markup("\n[green]> [/]");
        return Task.CompletedTask;
    }

    public async Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        AnsiConsole.Markup("[blue]Assistant:[/] ");
        await foreach (var token in tokens.WithCancellation(ct))
        {
            AnsiConsole.Markup(Markup.Escape(token.TrimStart()));
        }

        AnsiConsole.Markup("\n\n[green]> [/]");
    }
}