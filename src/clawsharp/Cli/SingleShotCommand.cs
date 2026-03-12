using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Providers;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Clawsharp.Cli;

/// <summary>Sends a single message to the LLM and prints the response. No session, no tools.</summary>
public static class SingleShotCommand
{
    public static async Task RunAsync(string message, CancellationToken ct = default)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var providerName = config.Agents.Defaults.Provider;

        // Build a minimal service provider just to get IHttpClientFactory for the provider
        var services = new ServiceCollection();
        services.AddHttpClient("llm", client => client.Timeout = TimeSpan.FromSeconds(120));
        using var sp = services.BuildServiceProvider();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        IProvider provider;
        try
        {
            provider = ProviderFactory.Create(providerName, config.Providers, httpFactory);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red][[agent]][/] Provider error: {Markup.Escape(ex.Message)}");
            return;
        }

        var d = config.Agents.Defaults;
        var request = new ChatRequest(
            Model: d.Model,
            Messages: [new ChatMessage(MessageRole.User, message)],
            Temperature: d.Temperature
        );

        try
        {
            if (provider is IStreamingProvider streamingProvider)
            {
                await foreach (var chunk in streamingProvider.StreamAsync(request, ct))
                {
                    if (chunk is TextDeltaChunk td)
                    {
                        AnsiConsole.Markup(Markup.Escape(td.Delta));
                    }
                }

                AnsiConsole.WriteLine();
            }
            else
            {
                var response = await provider.ChatAsync(request, ct);
                AnsiConsole.MarkupLine(Markup.Escape(response.Content ?? "(no response)"));
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red][[agent]][/] Error: {Markup.Escape(ex.Message)}");
        }
    }
}