using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Services;

/// <summary>
///     Background service that periodically injects a synthetic heartbeat message into the
///     message bus. The prompt is read from a configurable file (defaulting to HEARTBEAT.md)
///     and the schedule is a standard 5-field cron expression.
/// </summary>
public sealed partial class HeartbeatService : LifecycleBackgroundService
{
    private const int PollIntervalMs = 10_000;

    private const string DefaultPrompt = "Check for any pending tasks or reminders.";

    private readonly IMessageBus _bus;

    private readonly HeartbeatConfig _heartbeatConfig;

    private readonly ChannelName _channelName;

    private readonly ILogger<HeartbeatService> _logger;

    private long _lastFiredMinuteTicks;

    public HeartbeatService(
        IMessageBus bus,
        IOptions<AppConfig> configOptions,
        ILogger<HeartbeatService> logger)
    {
        _bus = bus;
        _heartbeatConfig = configOptions.Value.Agents.Defaults.Heartbeat
                           ?? throw new InvalidOperationException("HeartbeatService registered but Heartbeat config is null.");
        if (ChannelName.TryFromValue(_heartbeatConfig.Channel, out var cn))
        {
            _channelName = cn;
        }
        else
        {
            _channelName = ChannelName.Cli;
        }
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogHeartbeatStarted(_logger, _heartbeatConfig.Schedule, _heartbeatConfig.Channel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sleep 10 seconds then check if the cron expression matches the current minute.
                // This mirrors the polling approach used by CronService.
                await Task.Delay(PollIntervalMs, stoppingToken);

                // Heartbeat cron schedule is evaluated against the machine's local time
                // (DateTimeOffset.Now), NOT UTC. This matches user expectations for schedules
                // like "0 9 * * *" meaning 9 AM local. For explicit TZ control, use CronService
                // which supports per-job TimeZoneInfo via the Tz field.
                var now = DateTimeOffset.Now;
                if (!CronService.CronMatches(_heartbeatConfig.Schedule, now))
                {
                    continue;
                }

                // Avoid firing more than once per minute by tracking last fire time.
                var truncatedTicks = TruncateToMinute(now).UtcTicks;
                if (Volatile.Read(ref _lastFiredMinuteTicks) == truncatedTicks)
                {
                    continue;
                }

                Volatile.Write(ref _lastFiredMinuteTicks, truncatedTicks);

                var prompt = await ReadPromptFileAsync(stoppingToken);
                LogHeartbeatFiring(_logger, _heartbeatConfig.Channel, prompt.Length);

                await _bus.PublishAsync(new InboundMessage(
                    Channel: _channelName,
                    SenderId: "heartbeat",
                    SenderName: "heartbeat",
                    Text: prompt,
                    ArrivedAt: DateTimeOffset.UtcNow,
                    IsHeartbeat: true
                ), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogHeartbeatError(_logger, ex);
            }
        }
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Offset);

    private async Task<string> ReadPromptFileAsync(CancellationToken ct)
    {
        var promptFile = _heartbeatConfig.PromptFile;

        // Try relative to ~/.clawsharp/ first, then current directory
        var configDir = ConfigLoader.ExpandHome("~/.clawsharp");
        var candidates = new[]
        {
            (Root: configDir, Resolved: Path.GetFullPath(Path.Combine(configDir, promptFile))),
            (Root: Directory.GetCurrentDirectory(), Resolved: Path.GetFullPath(promptFile))
        };

        foreach (var (root, resolved) in candidates)
        {
            // Guard against path traversal: resolved path must remain within the root directory.
            var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(normalizedRoot, StringComparison.Ordinal))
            {
                LogPromptFileTraversal(_logger, promptFile, resolved);
                continue;
            }

            if (!File.Exists(resolved))
            {
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(resolved, ct);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content.Trim();
                }
            }
            catch (Exception ex)
            {
                LogPromptFileReadError(_logger, ex, resolved);
            }
        }

        LogUsingDefaultPrompt(_logger, promptFile);
        return DefaultPrompt;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "HeartbeatService started [schedule={Schedule}, channel={Channel}]")]
    private static partial void LogHeartbeatStarted(ILogger logger, string schedule, string channel);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Heartbeat firing -> {Channel} ({PromptLength} chars)")]
    private static partial void LogHeartbeatFiring(ILogger logger, string channel, int promptLength);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "HeartbeatService error")]
    private static partial void LogHeartbeatError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Could not read prompt file {Path}")]
    private static partial void LogPromptFileReadError(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "Prompt file '{PromptFile}' not found, using default prompt")]
    private static partial void LogUsingDefaultPrompt(ILogger logger, string promptFile);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Heartbeat prompt file '{PromptFile}' resolves outside allowed directory ({Resolved}), skipping")]
    private static partial void LogPromptFileTraversal(ILogger logger, string promptFile, string resolved);
}