using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Clawsharp.Ipc;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Channel;

/// <summary>Connects to the running gateway via named pipe and requests a new web pairing code.</summary>
[UsedImplicitly]
public sealed class ChannelPairWebCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var pipeName = GatewayIpcService.PipeName;
        const string command = "pair_web";

        // Read the IPC auth token written by the gateway on startup.
        var token = ReadIpcToken();
        if (token is null)
        {
            AnsiConsole.MarkupLine("[red]Cannot read IPC auth token.[/] Is the gateway running?");
            AnsiConsole.MarkupLine("[dim]Expected token at: " + Markup.Escape(GatewayIpcService.TokenPath) + "[/]");
            return 1;
        }

        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Gateway is not running.[/] Start it first with [bold]clawsharp agent[/].");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect to gateway:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var reqJson = JsonSerializer.Serialize(new IpcRequest(command, token), IpcJsonContext.Default.IpcRequest);
            await writer.WriteLineAsync(reqJson.AsMemory(), cancellationToken);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(TimeSpan.FromSeconds(5));
            var line = await reader.ReadLineAsync(readCts.Token);

            if (line is null)
            {
                AnsiConsole.MarkupLine("[red]Gateway closed connection without responding.[/]");
                return 1;
            }

            IpcResponse? resp;
            try
            {
                resp = JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcResponse);
            }
            catch
            {
                resp = null;
            }

            if (resp?.Error is { } err)
            {
                if (string.Equals(err, "auth_failed", StringComparison.Ordinal))
                {
                    AnsiConsole.MarkupLine("[red]Authentication failed.[/] The gateway may have restarted -- try again.");
                }
                else if (string.Equals(err, "too_many_connections", StringComparison.Ordinal))
                {
                    AnsiConsole.MarkupLine("[red]Gateway is busy.[/] Too many concurrent IPC connections -- try again shortly.");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Gateway error:[/] {Markup.Escape(err)}");
                }

                return 1;
            }

            if (resp is null)
            {
                AnsiConsole.MarkupLine("[red]Invalid response from gateway.[/]");
                return 1;
            }

            if (resp.Code is { } code)
            {
                AnsiConsole.MarkupLine($"[bold yellow]Pairing code:[/] [bold]{Markup.Escape(code)}[/]");
                AnsiConsole.MarkupLine("[dim]Enter this code in the browser to connect.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]No pairing code available -- web pairing may not be enabled.[/]");
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Timed out waiting for gateway response.[/]");
            return 1;
        }

        return 0;
    }

    /// <summary>Reads the IPC auth token from the well-known file path. Returns null if unreadable.</summary>
    private static string? ReadIpcToken()
    {
        try
        {
            var path = GatewayIpcService.TokenPath;
            if (!File.Exists(path))
            {
                return null;
            }

            var token = File.ReadAllText(path).Trim();
            return token.Length > 0 ? token : null;
        }
        catch
        {
            return null;
        }
    }
}