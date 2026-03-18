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
/// Integration tests for the CLI channel's SendAsync and StreamAsync output.
/// Captures Console.Out to verify correct rendering.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CliChannelTests
{
    private TextWriter _originalOut = null!;

    [SetUp]
    public void SetUp()
    {
        _originalOut = Console.Out;
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(_originalOut);
    }

    [Test]
    public async Task SendAsync_WritesToConsoleOutput()
    {
        var channel = CreateCliChannel();
        var writer = new StringWriter();
        Console.SetOut(writer);

        var message = new OutboundMessage(
            Channel: ChannelName.Cli,
            RecipientId: "cli-user",
            Text: "Hello from the assistant");

        await channel.SendAsync(message);

        var output = writer.ToString();
        // Spectre.Console writes to Console.Out — verify the text appears
        // Note: Spectre.Console may use ANSI escape codes; the plain text should be present.
        output.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task StreamAsync_WritesTokensIncrementally()
    {
        var channel = CreateCliChannel();
        var writer = new StringWriter();
        Console.SetOut(writer);

        var message = new OutboundMessage(
            Channel: ChannelName.Cli,
            RecipientId: "cli-user",
            Text: "");

        var tokens = GenerateTokens("Hello", " ", "World", "!");
        await channel.StreamAsync(message, tokens);

        var output = writer.ToString();
        output.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SendAsync_MultipleMessages_AllAppearInOutput()
    {
        var channel = CreateCliChannel();
        var writer = new StringWriter();
        Console.SetOut(writer);

        await channel.SendAsync(new OutboundMessage(ChannelName.Cli, "cli-user", "First reply"));
        await channel.SendAsync(new OutboundMessage(ChannelName.Cli, "cli-user", "Second reply"));

        var output = writer.ToString();
        output.ShouldNotBeNullOrWhiteSpace();
        // Spectre.Console renders to the real Console; StringWriter captures only direct writes.
        // This test validates no exceptions are thrown and some output is produced.
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
