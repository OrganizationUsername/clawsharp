using System.Net.Http.Headers;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Agent;

namespace Clawsharp.Cli.Models;

/// <summary>Lists available models for each configured provider.</summary>
[UsedImplicitly]
public sealed class ModelsListCommand : AsyncCommand
{
    /// <summary>Known Anthropic models (no list API available).</summary>
    private static readonly string[] AnthropicModels =
    [
        "claude-opus-4-6",
        "claude-opus-4-5-20251101",
        "claude-opus-4-1-20250805",
        "claude-opus-4-20250514",
        "claude-sonnet-4-6",
        "claude-sonnet-4-5-20250929",
        "claude-sonnet-4-20250514",
        "claude-haiku-4-5-20251001",
        "claude-3-haiku-20240307",
    ];

    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();

        AnsiConsole.MarkupLine("[bold]clawsharp models list[/]");
        AnsiConsole.WriteLine();

        if (config.Providers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No providers configured.[/]");
            return 0;
        }

        using var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        foreach (var (name, providerCfg) in config.Providers)
        {
            if (!LlmProviderType.TryFromValue(providerCfg.Type.ToLowerInvariant(), out var providerType))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Provider '{Markup.Escape(name)}': unknown type '{Markup.Escape(providerCfg.Type)}', skipping.[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            if (providerType == LlmProviderType.Anthropic)
            {
                PrintAnthropicModels(name);
                continue;
            }

            if (providerType == LlmProviderType.Gemini)
            {
                await FetchGeminiModelsAsync(http, name, providerCfg, cancellationToken);
                continue;
            }

            // All remaining types are OpenAI-compatible
            await FetchOpenAiModelsAsync(http, name, providerCfg, providerType, cancellationToken);
        }

        return 0;
    }

    private static void PrintAnthropicModels(string name)
    {
        AnsiConsole.MarkupLine($"[bold]Provider: {Markup.Escape(name)}[/] (anthropic, static list)");

        var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Model ID");

        foreach (var model in AnthropicModels)
        {
            table.AddRow(model);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static async Task FetchGeminiModelsAsync(
        HttpClient http, string name, ProviderConfig providerCfg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(providerCfg.ApiKey))
        {
            AnsiConsole.MarkupLine($"[yellow]Provider '{Markup.Escape(name)}': no API key, skipping.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var url = $"{ClawsharpConstants.GeminiDefaultBaseUrl}/models?key={providerCfg.ApiKey}";

        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync(
                stream, ModelsJsonContext.Default.GeminiModelsResponse, ct);

            var models = result?.Models;
            if (models is null || models.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Provider '{Markup.Escape(name)}': no models returned.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine(
                $"[bold]Provider: {Markup.Escape(name)}[/] (gemini, https://generativelanguage.googleapis.com)");

            var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn("Model ID")
                        .AddColumn("Display Name");

            foreach (var m in models.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                table.AddRow(
                    Markup.Escape(m.Name ?? ""),
                    Markup.Escape(m.DisplayName ?? ""));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Provider '{Markup.Escape(name)}': failed to fetch models: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private static async Task FetchOpenAiModelsAsync(
        HttpClient http, string name, ProviderConfig providerCfg,
        LlmProviderType providerType, CancellationToken ct)
    {
        var baseUrl = providerCfg.BaseUrl;
        if (baseUrl is null)
        {
            ClawsharpConstants.DefaultProviderBaseUrls.TryGetValue(providerType, out baseUrl);
        }

        if (baseUrl is null)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Provider '{Markup.Escape(name)}': no base URL configured, skipping.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var url = $"{baseUrl.TrimEnd('/')}/models";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrEmpty(providerCfg.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerCfg.ApiKey);
            }

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync(
                stream, ModelsJsonContext.Default.OpenAiModelsResponse, ct);

            var models = result?.Data;
            if (models is null || models.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Provider '{Markup.Escape(name)}': no models returned.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine(
                $"[bold]Provider: {Markup.Escape(name)}[/] ({Markup.Escape(providerType.Value)}, {Markup.Escape(baseUrl)})");

            var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn("Model ID");

            foreach (var m in models.OrderBy(m => m.Id, StringComparer.Ordinal))
            {
                table.AddRow(Markup.Escape(m.Id ?? ""));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Provider '{Markup.Escape(name)}': failed to fetch models: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine();
        }
    }
}