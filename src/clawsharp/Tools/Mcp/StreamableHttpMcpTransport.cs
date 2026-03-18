using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// MCP transport over Streamable HTTP (2025-03-26 spec).
/// Sends JSON-RPC requests as HTTP POST and reads responses from the body,
/// which may be direct JSON or an SSE stream.
/// </summary>
internal sealed partial class StreamableHttpMcpTransport(
    HttpClient httpClient,
    Uri endpointUri,
    string serverName,
    Dictionary<string, string>? headers,
    ILogger logger)
    : IMcpTransport
{
    private readonly Dictionary<string, string> _headers = headers ?? [];

    private readonly SemaphoreSlim _lock = new(1, 1);

    private int _nextId;

    private volatile bool _connected = true;

    private string? _sessionId;

    public bool IsConnected => _connected;

    public async Task<McpResponse> SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new McpRequest
        {
            Id = id,
            Method = method,
            Params = parameters
        };

        var json = JsonSerializer.Serialize(request, McpJsonContext.Default.McpRequest);
        LogOutbound(logger, serverName, json);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        foreach (var (key, value) in _headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        // Attach session ID if established by a previous response.
        if (_sessionId is not null)
        {
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        using var httpResponse = await httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        // Capture session ID from response header.
        if (httpResponse.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            var sid = sessionIds.FirstOrDefault();
            if (sid is not null)
            {
                _sessionId = sid;
            }
        }

        var contentType = httpResponse.Content.Headers.ContentType?.MediaType ?? "";

        if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // Parse SSE stream for the response with our id
            return await ReadSseResponseAsync(id, httpResponse, ct).ConfigureAwait(false);
        }

        // Direct JSON response
        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        LogInbound(logger, serverName, responseJson);

        var response = JsonSerializer.Deserialize(responseJson, McpJsonContext.Default.McpResponse)
                       ?? throw new InvalidOperationException(
                           $"MCP StreamableHTTP server '{serverName}' returned null JSON-RPC response.");

        return response;
    }

    public async Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        var notification = new McpRequest
        {
            Id = null,
            Method = method,
            Params = parameters
        };

        var json = JsonSerializer.Serialize(notification, McpJsonContext.Default.McpRequest);
        LogOutboundNotification(logger, serverName, json);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in _headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        if (_sessionId is not null)
        {
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        using var httpResponse = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        // Notifications may return 202 Accepted or 200 OK -- either is fine.
        httpResponse.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reads an SSE-streamed response body and extracts the JSON-RPC response matching our request id.
    /// </summary>
    private async Task<McpResponse> ReadSseResponseAsync(
        int requestId, HttpResponseMessage httpResponse, CancellationToken ct)
    {
        using var stream = await httpResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? currentEvent = null;
        var dataBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break; // Stream ended
            }

            if (line.Length == 0)
            {
                if (dataBuffer.Length > 0)
                {
                    var data = dataBuffer.ToString();
                    dataBuffer.Clear();

                    // Only process "message" events or events with no type
                    if (string.IsNullOrEmpty(currentEvent)
                        || string.Equals(currentEvent, McpSseEventType.Message, StringComparison.Ordinal))
                    {
                        LogInbound(logger, serverName, data);

                        McpResponse? response;
                        try
                        {
                            response = JsonSerializer.Deserialize(data, McpJsonContext.Default.McpResponse);
                        }
                        catch (JsonException)
                        {
                            currentEvent = null;
                            continue;
                        }

                        if (response?.Id == requestId)
                        {
                            return response;
                        }
                    }

                    currentEvent = null;
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue; // Comment
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].TrimStart();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuffer.Length > 0)
                {
                    dataBuffer.Append('\n');
                }

                dataBuffer.Append(line["data:".Length..].TrimStart());
            }
        }

        throw new InvalidOperationException(
            $"MCP StreamableHTTP server '{serverName}' SSE stream ended without response for request {requestId}.");
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Debug,
        Message = "MCP HTTP [{ServerName}] --> {Json}")]
    private static partial void LogOutbound(ILogger logger, string serverName, string json);

    [LoggerMessage(EventId = 301, Level = LogLevel.Debug,
        Message = "MCP HTTP [{ServerName}] <-- {Data}")]
    private static partial void LogInbound(ILogger logger, string serverName, string data);

    [LoggerMessage(EventId = 302, Level = LogLevel.Debug,
        Message = "MCP HTTP [{ServerName}] --> (notification) {Json}")]
    private static partial void LogOutboundNotification(ILogger logger, string serverName, string json);
}