using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// MCP transport over Server-Sent Events (SSE).
/// Connects to an SSE endpoint to receive JSON-RPC responses,
/// and POSTs JSON-RPC requests to a message endpoint provided by the server.
/// </summary>
internal sealed partial class SseMcpTransport(
    HttpClient httpClient,
    Uri sseUri,
    string serverName,
    Dictionary<string, string>? headers,
    ILogger logger)
    : IMcpTransport
{
    private readonly Dictionary<string, string> _headers = headers ?? [];

    private readonly ConcurrentDictionary<int, TaskCompletionSource<McpResponse>> _pending = new();

    private readonly SemaphoreSlim _endpointReady = new(0, 1);

    private readonly CancellationTokenSource _disposeCts = new();

    private int _nextId;

    private volatile string? _messageEndpoint;

    private volatile bool _connected;

    private Task? _listenerTask;

    public bool IsConnected => _connected;

    /// <summary>
    /// Opens the SSE connection and waits for the server to send the message endpoint.
    /// Must be called before sending any requests.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        _listenerTask = RunListenerAsync(_disposeCts.Token);

        // Wait for the server to send the 'endpoint' event (up to 30s).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await _endpointReady.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MCP SSE server '{serverName}' did not send the endpoint event within 30s.");
        }

        if (_messageEndpoint is null)
        {
            throw new InvalidOperationException(
                $"MCP SSE server '{serverName}' SSE stream ended without sending an endpoint event.");
        }

        _connected = true;
        LogConnected(logger, serverName, _messageEndpoint);
    }

    public async Task<McpResponse> SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        if (_messageEndpoint is null)
        {
            throw new InvalidOperationException(
                $"MCP SSE server '{serverName}' is not connected (no message endpoint).");
        }

        var id = Interlocked.Increment(ref _nextId);
        var request = new McpRequest
        {
            Id = id,
            Method = method,
            Params = parameters
        };

        var tcs = new TaskCompletionSource<McpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, McpJsonContext.Default.McpRequest);
            LogOutbound(logger, serverName, json);

            var endpointUri = ResolveEndpointUri(_messageEndpoint);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            foreach (var (key, value) in _headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            using var httpResponse = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            // Response comes via the SSE stream, not the HTTP response body.
            // Wait for the background listener to dispatch it.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MCP SSE server '{serverName}' did not respond to '{method}' within 30s.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        if (_messageEndpoint is null)
        {
            throw new InvalidOperationException(
                $"MCP SSE server '{serverName}' is not connected (no message endpoint).");
        }

        var notification = new McpRequest
        {
            Id = null,
            Method = method,
            Params = parameters
        };

        var json = JsonSerializer.Serialize(notification, McpJsonContext.Default.McpRequest);
        LogOutboundNotification(logger, serverName, json);

        var endpointUri = ResolveEndpointUri(_messageEndpoint);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in _headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        using var httpResponse = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resolves the message endpoint URL. If the server sent an absolute URL, use it directly.
    /// If it sent a relative path, resolve against the SSE base URI.
    /// </summary>
    private Uri ResolveEndpointUri(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        return new Uri(sseUri, endpoint);
    }

    /// <summary>
    /// Background task that reads the SSE stream and dispatches responses to pending requests.
    /// </summary>
    private async Task RunListenerAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sseUri);
            request.Headers.Accept.ParseAdd("text/event-stream");

            foreach (var (key, value) in _headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }

            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            var dataBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    // Stream ended
                    break;
                }

                // Empty line = end of event
                if (line.Length == 0)
                {
                    if (dataBuffer.Length > 0)
                    {
                        ProcessSseEvent(currentEvent, dataBuffer.ToString());
                        dataBuffer.Clear();
                        currentEvent = null;
                    }

                    continue;
                }

                // Comment line
                if (line.StartsWith(':'))
                {
                    continue;
                }

                // Parse field: value
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
                else if (line.StartsWith("id:", StringComparison.Ordinal))
                {
                    // Last event ID -- not needed for MCP but part of SSE spec
                }
                else if (line.StartsWith("retry:", StringComparison.Ordinal))
                {
                    // Reconnect interval -- not used
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogListenerError(logger, serverName, ex);
        }
        finally
        {
            _connected = false;

            // Fail all pending requests
            foreach (var (id, tcs) in _pending)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"MCP SSE server '{serverName}' connection closed while waiting for response to request {id}."));
            }

            _pending.Clear();
        }
    }

    /// <summary>
    /// Processes a completed SSE event.
    /// </summary>
    private void ProcessSseEvent(string? eventType, string data)
    {
        if (string.Equals(eventType, McpSseEventType.Endpoint, StringComparison.Ordinal))
        {
            // The server sends the message endpoint URL
            _messageEndpoint = data.Trim();
            LogEndpointReceived(logger, serverName, _messageEndpoint);

            // Signal that the endpoint is ready
            try
            {
                _endpointReady.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signaled -- ignore
            }

            return;
        }

        // Default event type or "message" -- parse as JSON-RPC response
        if (string.IsNullOrEmpty(eventType)
            || string.Equals(eventType, McpSseEventType.Message, StringComparison.Ordinal))
        {
            LogInbound(logger, serverName, data);

            McpResponse? response;
            try
            {
                response = JsonSerializer.Deserialize(data, McpJsonContext.Default.McpResponse);
            }
            catch (JsonException ex)
            {
                LogParseError(logger, serverName, ex);
                return;
            }

            if (response?.Id is { } responseId && _pending.TryRemove(responseId, out var tcs))
            {
                tcs.TrySetResult(response);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _disposeCts.CancelAsync().ConfigureAwait(false);

            if (_listenerTask is not null)
            {
                try
                {
                    await _listenerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch (Exception ex)
                {
                    LogDisposeError(logger, serverName, ex);
                }
            }
        }
        finally
        {
            _disposeCts.Dispose();
            _endpointReady.Dispose();
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "MCP SSE '{ServerName}' connected, message endpoint: {Endpoint}")]
    private static partial void LogConnected(ILogger logger, string serverName, string endpoint);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug,
        Message = "MCP SSE [{ServerName}] --> {Json}")]
    private static partial void LogOutbound(ILogger logger, string serverName, string json);

    [LoggerMessage(EventId = 202, Level = LogLevel.Debug,
        Message = "MCP SSE [{ServerName}] <-- {Data}")]
    private static partial void LogInbound(ILogger logger, string serverName, string data);

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug,
        Message = "MCP SSE [{ServerName}] --> (notification) {Json}")]
    private static partial void LogOutboundNotification(ILogger logger, string serverName, string json);

    [LoggerMessage(EventId = 204, Level = LogLevel.Debug,
        Message = "MCP SSE '{ServerName}' received endpoint event: {Endpoint}")]
    private static partial void LogEndpointReceived(ILogger logger, string serverName, string endpoint);

    [LoggerMessage(EventId = 205, Level = LogLevel.Warning,
        Message = "MCP SSE '{ServerName}' listener encountered an error")]
    private static partial void LogListenerError(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning,
        Message = "MCP SSE '{ServerName}' failed to parse SSE data as JSON-RPC response")]
    private static partial void LogParseError(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning,
        Message = "Error disposing MCP SSE transport '{ServerName}'")]
    private static partial void LogDisposeError(ILogger logger, string serverName, Exception exception);
}