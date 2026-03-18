using Clawsharp.Channels.Cli;
using Clawsharp.Config;
using Clawsharp.Config.Channels;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Integration.E2E;

/// <summary>
/// Integration tests for the CLI channel's SendAsync and StreamAsync.
/// Spectre.Console writes to its own backend (not Console.Out), so these tests
/// verify that the methods complete without throwing. See CliStreamingTests for
/// detailed token-assembly logic tests.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CliChannelTests
{
    [Test]
    public async Task SendAsync_CompletesWithoutException()
    {
        var channel = CreateCliChannel();
        var message = new OutboundMessage(
            Channel: ChannelName.Cli,
            RecipientId: "cli-user",
            Text: "Hello from the assistant");

        await Should.NotThrowAsync(() => channel.SendAsync(message));
    }

    [Test]
    public async Task StreamAsync_CompletesWithoutException()
    {
        var channel = CreateCliChannel();
        var message = new OutboundMessage(
            Channel: ChannelName.Cli,
            RecipientId: "cli-user",
            Text: "");

        var tokens = GenerateTokens("Hello", " ", "World", "!");
        await Should.NotThrowAsync(() => channel.StreamAsync(message, tokens));
    }

    [Test]
    public async Task SendAsync_MultipleMessages_CompletesWithoutException()
    {
        var channel = CreateCliChannel();

        await Should.NotThrowAsync(async () =>
        {
            await channel.SendAsync(new OutboundMessage(ChannelName.Cli, "cli-user", "First reply"));
            await channel.SendAsync(new OutboundMessage(ChannelName.Cli, "cli-user", "Second reply"));
        });
    }

    // ── Helpers ──

    private static CliChannel CreateCliChannel()
    {
        var appConfig = new AppConfig
        {
            Channels = new Dictionary<string, ChannelConfig>
            {
                ["cli"] = new() { Enabled = true }
            }
        };

        var bus = new InMemoryMessageBus();
        return new CliChannel(
            Options.Create(appConfig),
            bus,
            NullLogger<CliChannel>.Instance);
    }

    private static async IAsyncEnumerable<string> GenerateTokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}
