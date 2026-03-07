using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Web;

public sealed partial class WebChannel : IHostedLifecycleService, IStreamingChannel, IAsyncDisposable
{
    private readonly IMessageBus _bus;

    private readonly bool _enabled;

    private readonly string _host = "localhost";

    /// <summary>CORS allowed origins — null means fail-closed (no CORS headers).</summary>
    private readonly string? _allowedOrigins;

    private readonly ILogger<WebChannel> _logger;

    private readonly WebPairingService _pairingService;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    private readonly int _port = 3000;

    // Exactly one of these is active:
    //   _staticToken  — operator set a fixed token in config (legacy/explicit mode)
    //   _pairingService — dynamic pairing guard via shared singleton (default when no token is configured)
    private readonly string? _staticToken;

    private readonly bool _tls;

    private const int MaxWebSocketMessageBytes = 1 * 1024 * 1024; // 1 MB

    private readonly ConcurrentDictionary<string, WebSocket> _wsClients = new();

    /// <summary>Inner Kestrel host — built in <see cref="StartAsync"/> and stopped in <see cref="StopAsync"/>.</summary>
    private WebApplication? _app;

    public WebChannel(IOptions<AppConfig> options, IMessageBus bus, WebPairingService pairingService, ILogger<WebChannel> logger)
    {
        _bus = bus;
        _pairingService = pairingService;
        _logger = logger;

        var config = options.Value;
        var cfg = config.Channels.GetValueOrDefault(ChannelName.Web.Value);
        if (cfg is not { Enabled: true })
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _host = cfg.WebHost;
        _port = cfg.WebPort;
        _tls = cfg.Tls;

        // S3: CORS fail-closed — null default, explicit config required.
        // Only set CORS headers when AllowedOrigins is explicitly configured.
        _allowedOrigins = cfg.AllowedOrigins;

        if (cfg.PairingToken is { Length: > 0 } tok)
        {
            _staticToken = tok;
        }
    }

    public ChannelName Name => ChannelName.Web;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return;
        }

        if (_pairingService.IsEnabled && _pairingService.PairingCode is { } code)
        {
            LogPairingCode(_logger, code);
        }

        if (_tls)
        {
            LogTlsAdvisory(_logger, _port);
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{_host}:{_port}");

        // Suppress the inner host's logging — the outer host already has logging configured.
        builder.Logging.ClearProviders();

        // WebSocket support
        builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = MaxWebSocketMessageBytes; });

        _app = builder.Build();

        var wsOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };
        _app.UseWebSockets(wsOptions);

        // Security headers + CORS middleware (runs before all routes)
        _app.Use(async (context, next) =>
        {
            ApplySecurityHeaders(context.Response);
            ApplyCorsHeaders(context);

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next(context);
        });

        // WebSocket middleware — upgrade on /ws path
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
            {
                var ws = await context.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocketAsync(ws, context.RequestAborted);
                return;
            }

            await next(context);
        });

        // Unauthenticated routes
        _app.MapGet("/", (HttpContext ctx) => ServeIndexHtmlAsync(ctx, ctx.RequestAborted));
        _app.MapGet("/index.html", (HttpContext ctx) => ServeIndexHtmlAsync(ctx, ctx.RequestAborted));

        // Pairing endpoint (unauthenticated — it IS the auth flow)
        _app.MapPost("/pair", (HttpContext ctx) => HandlePairAsync(ctx, ctx.RequestAborted));

        // Authenticated routes
        _app.MapPost("/chat", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx.Request))
            {
                await WriteJsonAsync(ctx, StatusCodes.Status401Unauthorized,
                    new WebPairResponse { Error = "unauthorized" },
                    WebJsonContext.Default.WebPairResponse, ctx.RequestAborted);
                return;
            }

            await HandleHttpChatAsync(ctx, ctx.RequestAborted);
        });

        _app.MapGet("/health", (HttpContext ctx) =>
        {
            // Health check — requires localhost-only access to prevent info leakage
            var remoteIp = ctx.Connection.RemoteIpAddress;
            if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsync("{\"status\":\"ok\"}", ctx.RequestAborted);
        });

        var prefix = $"http://{_host}:{_port}/";
        LogListening(_logger, prefix);

        try
        {
            await _app.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogListenerStartFailed(_logger, _port, ex);
            await _app.DisposeAsync();
            _app = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var sessionId = message.RecipientId;

        if (_pending.TryRemove(sessionId, out var tcs))
        {
            tcs.TrySetResult(message.Text);
            return;
        }

        if (_wsClients.TryGetValue(sessionId, out var ws) && ws.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message.Text);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
    }

    public async Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var sessionId = message.RecipientId;

        if (_wsClients.TryGetValue(sessionId, out var ws) && ws.State == WebSocketState.Open)
        {
            // WebSocket path: send each token as a {"delta":"..."} frame immediately.
            await foreach (var token in tokens.WithCancellation(ct))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(
                    new WebStreamDelta { Delta = token },
                    WebJsonContext.Default.WebStreamDelta);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            // Signal end-of-stream so the client can finalize the message bubble.
            var doneBytes = "[DONE]"u8.ToArray();
            await ws.SendAsync(doneBytes, WebSocketMessageType.Text, true, ct);
        }
        else if (_pending.TryRemove(sessionId, out var tcs))
        {
            // HTTP polling fallback: accumulate all tokens and resolve the TCS with the full text.
            var sb = new StringBuilder();
            await foreach (var token in tokens.WithCancellation(ct))
            {
                sb.Append(token);
            }

            tcs.TrySetResult(sb.ToString());
        }
    }

    /// <summary>Apply security headers that are always set on every response.</summary>
    private static void ApplySecurityHeaders(HttpResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        // MED-05: Fallback for older browsers that don't support CSP frame-ancestors
        response.Headers["X-Frame-Options"] = "DENY";
    }

    /// <summary>Apply CORS headers — fail-closed when AllowedOrigins is not configured.</summary>
    private void ApplyCorsHeaders(HttpContext context)
    {
        // No CORS headers if AllowedOrigins is not explicitly configured (fail-closed)
        if (_allowedOrigins is null)
        {
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return;
        }

        // Check if origin is allowed
        if (_allowedOrigins == "*" || IsOriginAllowed(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] =
                "Content-Type, Authorization, X-Pairing-Code, X-Pairing-Token";
        }
    }

    /// <summary>Check if an origin is in the comma-separated allowed origins list.</summary>
    private bool IsOriginAllowed(string origin)
    {
        if (_allowedOrigins is null)
        {
            return false;
        }

        // Localhost exemption: allow loopback-to-loopback
        if (IsLoopbackOrigin(origin) && IsListeningOnLoopback())
        {
            return true;
        }

        foreach (var allowed in _allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoopbackOrigin(string origin)
    {
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return uri.Host is "localhost" or "127.0.0.1" or "::1";
        }

        return false;
    }

    private bool IsListeningOnLoopback() =>
        _host is "localhost" or "127.0.0.1" or "::1";

    private async Task HandlePairAsync(HttpContext context, CancellationToken ct)
    {
        if (!_pairingService.IsEnabled)
        {
            // Static-token mode — pairing endpoint not applicable
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest,
                new WebPairResponse { Error = "pairing_not_enabled" },
                WebJsonContext.Default.WebPairResponse, ct);
            return;
        }

        var code = context.Request.Headers[ClawsharpConstants.HttpHeaders.PairingCode].ToString();
        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest,
                new WebPairResponse { Error = "missing_pairing_code" },
                WebJsonContext.Default.WebPairResponse, ct);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var token = _pairingService.TryPair(clientIp, code);

        if (token is null)
        {
            var error = _pairingService.PairingCode is null && !_pairingService.HasPairedClients
                ? "no_active_pairing_code"
                : "invalid_code_or_locked_out";

            await WriteJsonAsync(context, StatusCodes.Status403Forbidden,
                new WebPairResponse { Error = error },
                WebJsonContext.Default.WebPairResponse, ct);
            return;
        }

        LogNewClientPaired(_logger, clientIp);
        await WriteJsonAsync(context, StatusCodes.Status200OK,
            new WebPairResponse
            {
                Paired = true,
                Token = token,
                Message = "Save this token — it cannot be recovered. Include it as: Authorization: Bearer <token>"
            },
            WebJsonContext.Default.WebPairResponse, ct);
    }

    /// <summary>Validate auth for HTTP (non-WebSocket) requests only.</summary>
    private bool IsAuthorized(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();

        if (_staticToken is not null)
        {
            // Static token: constant-time comparison
            if (!authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return false;
            }

            var provided = Encoding.UTF8.GetBytes(authHeader["Bearer ".Length..]);
            var expected = Encoding.UTF8.GetBytes(_staticToken);
            return CryptographicOperations.FixedTimeEquals(provided, expected);
        }

        if (_pairingService.IsEnabled)
        {
            if (!authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return false;
            }

            return _pairingService.IsAuthenticated(authHeader["Bearer ".Length..]);
        }

        // Neither configured — should not happen given constructor logic, but fail-closed.
        return false;
    }

    /// <summary>Validate a token (from first-frame auth or URL fallback) for WebSocket connections.</summary>
    private bool ValidateToken(string token)
    {
        if (_staticToken is not null)
        {
            var provided = Encoding.UTF8.GetBytes(token);
            var expected = Encoding.UTF8.GetBytes(_staticToken);
            return CryptographicOperations.FixedTimeEquals(provided, expected);
        }

        return _pairingService.IsAuthenticated(token);
    }

    /// <summary>
    /// Derive a deterministic session ID from the Bearer token on the request.
    /// Uses SHA-256 of the raw token to bind sessions to the authenticated identity,
    /// preventing session ID injection while maintaining conversation continuity.
    /// </summary>
    private static string DeriveSessionIdFromToken(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            var token = authHeader["Bearer ".Length..];
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return $"web:{Convert.ToHexStringLower(hash[..16])}";
        }

        // Fallback — should not be reachable since IsAuthorized() already validated the token.
        return $"web:{Guid.NewGuid():N}";
    }

    private async Task HandleHttpChatAsync(HttpContext context, CancellationToken ct)
    {
        WebChatRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync(
                context.Request.Body, WebJsonContext.Default.WebChatRequest, ct);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Message))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // MED-05: Derive session ID from the authenticated token — never trust client-supplied values.
        // This prevents session ID injection (accessing another user's session) while maintaining
        // conversation continuity across requests from the same authenticated client.
        var sessionId = DeriveSessionIdFromToken(context.Request);
        var tcs = new TaskCompletionSource<string>();
        _pending[sessionId] = tcs;

        try
        {
            await _bus.PublishAsync(new InboundMessage(
                Channel: Name,
                SenderId: sessionId,
                SenderName: "WebUser",
                Text: req.Message
            ), ct);

            var reply = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);
            var response = new WebChatResponse { Reply = reply, SessionId = sessionId };
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body, response, WebJsonContext.Default.WebChatResponse, ct);
        }
        finally
        {
            _pending.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    ///     S4: WebSocket handler with first-frame auth protocol.
    ///     1. Accept WS upgrade without auth
    ///     2. Wait up to 10s for auth frame: { "type": "auth", "token": "..." }
    ///     3. Validate token; send auth_ok or auth_error
    ///     4. Only then process user messages
    /// </summary>
    private async Task HandleWebSocketAsync(WebSocket ws, CancellationToken ct)
    {
        var (authenticated, sessionId) = await AuthenticateWebSocketAsync(ws, ct);
        if (!authenticated || sessionId is null)
        {
            return;
        }

        await RunWebSocketMessageLoopAsync(ws, sessionId, ct);
    }

    /// <summary>
    /// Performs the Phase 1 auth handshake with a 10-second timeout.
    /// Waits for an auth frame, validates the token, and sends auth_ok or auth_error.
    /// Returns (true, sessionId) if authentication succeeded, (false, null) if it failed.
    /// HIGH-02: The session ID is derived from the token via SHA-256, binding it to the
    /// authenticated identity (same as the HTTP path's DeriveSessionIdFromToken).
    /// </summary>
    private async Task<(bool Authenticated, string? SessionId)> AuthenticateWebSocketAsync(WebSocket ws, CancellationToken ct)
    {
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        authCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var authMsg = await ReceiveTextAsync(ws, authCts.Token);
            if (authMsg is null)
            {
                await CloseWithMessageAsync(ws, "auth_error", "No auth message received", ct);
                return (false, null);
            }

            // Try first-frame auth: { "type": "auth", "token": "..." }
            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(authMsg);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "auth" &&
                    root.TryGetProperty("token", out var tokenProp))
                {
                    token = tokenProp.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Not JSON — not a valid auth frame
            }

            if (token is null || !ValidateToken(token))
            {
                await CloseWithMessageAsync(ws, "auth_error", "Invalid token", ct);
                return (false, null);
            }

            // HIGH-02: Derive session ID from the authenticated token via SHA-256,
            // binding the WebSocket session to the same identity as the HTTP path.
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var sessionId = $"web:{Convert.ToHexStringLower(hash[..16])}";

            // Auth succeeded — send confirmation
            var okFrame = JsonSerializer.Serialize(
                new WebAuthResponse { Type = "auth_ok" },
                WebJsonContext.Default.WebAuthResponse);
            var okBytes = Encoding.UTF8.GetBytes(okFrame);
            await ws.SendAsync(okBytes, WebSocketMessageType.Text, true, ct);
            return (true, sessionId);
        }
        catch (OperationCanceledException) when (authCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await CloseWithMessageAsync(ws, "auth_error", "Auth timeout", ct);
            return (false, null);
        }
    }

    /// <summary>
    /// Runs the Phase 2 authenticated message loop. Generates a session ID, tracks the
    /// WebSocket client, receives messages, and publishes them to the message bus.
    /// </summary>
    private async Task RunWebSocketMessageLoopAsync(WebSocket ws, string sessionId, CancellationToken ct)
    {
        _wsClients[sessionId] = ws;
        var buffer = new byte[16384];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > MaxWebSocketMessageBytes)
                    {
                        throw new InvalidOperationException($"WebSocket message exceeded {MaxWebSocketMessageBytes} byte limit");
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                await _bus.PublishAsync(new InboundMessage(
                    Channel: Name,
                    SenderId: sessionId,
                    SenderName: "WebUser",
                    Text: text
                ), ct);
            }
        }
        catch (Exception ex)
        {
            LogWebSocketError(_logger, ex);
        }
        finally
        {
            _wsClients.TryRemove(sessionId, out _);
        }
    }

    private const int MaxAuthFrameBytes = 8192; // 8 KB limit for pre-auth WebSocket frames

    /// <summary>Read a single complete text message from the WebSocket with an 8 KB size limit.</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxAuthFrameBytes)
            {
                await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Auth frame too large", ct);
                return null;
            }
        } while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        return Encoding.UTF8.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <summary>Send an auth response frame and close the WebSocket.</summary>
    private static async Task CloseWithMessageAsync(WebSocket ws, string type, string message, CancellationToken ct)
    {
        try
        {
            var frame = JsonSerializer.Serialize(
                new WebAuthResponse { Type = type, Message = message },
                WebJsonContext.Default.WebAuthResponse);
            var bytes = Encoding.UTF8.GetBytes(frame);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, message, ct);
        }
        catch
        {
            // Best-effort close
        }
    }

    private static async Task ServeIndexHtmlAsync(HttpContext context, CancellationToken ct)
    {
        // Generate a cryptographic nonce for script-src CSP
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(nonceBytes);

        // S3: Nonce-based CSP — eliminates 'unsafe-inline' for script-src.
        // Style-src still uses 'unsafe-inline' because the bundled Svelte/Vite output
        // injects runtime styles that cannot be nonce-tagged without a build pipeline change.
        context.Response.Headers["Content-Security-Policy"] =
            $"default-src 'self'; frame-ancestors 'none'; script-src 'self' 'nonce-{nonce}'; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com; " +
            "connect-src 'self'; img-src 'self' data:";

        var asm = typeof(WebChannel).Assembly;
        var resource = asm.GetManifestResourceStream("clawsharp.Channels.Web.index.html");
        string html;
        if (resource is not null)
        {
            using var ms = new MemoryStream();
            await resource.CopyToAsync(ms, ct).ConfigureAwait(false);
            html = Encoding.UTF8.GetString(ms.ToArray());
        }
        else
        {
            // Fallback: read from disk relative to application base directory
            // (Assembly.Location returns "" in single-file/AOT apps; AppContext.BaseDirectory is always valid)
            var dir = AppContext.BaseDirectory;
            var path = Path.Combine(dir, "index.html");
            html = File.Exists(path)
                ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)
                : "<h1>UI not found</h1>";
        }

        // Inject nonce into all <script> tags so they pass the CSP check.
        // The bundled HTML has <script type="module" crossorigin> — we add nonce="...".
        html = html.Replace("<script ", $"<script nonce=\"{nonce}\" ", StringComparison.Ordinal);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, ct).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(
        HttpContext context, int statusCode, T payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, typeInfo, ct);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Pairing code: {PairingCode} -- POST /pair with X-Pairing-Code header to authenticate")]
    private static partial void LogPairingCode(ILogger logger, string pairingCode);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Listening on {Prefix}")]
    private static partial void LogListening(ILogger logger, string prefix);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Listener error")]
    private static partial void LogListenerError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "New client paired from {ClientIp}")]
    private static partial void LogNewClientPaired(ILogger logger, string clientIp);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "WebSocket error")]
    private static partial void LogWebSocketError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "TLS is enabled in config but Kestrel is not configured for TLS directly. " +
                  "Configure a reverse proxy (nginx, Caddy, Traefik) to handle TLS termination on port {Port}.")]
    private static partial void LogTlsAdvisory(ILogger logger, int port);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error,
        Message = "Failed to start Web channel listener on port {Port} — check if another process is using this port")]
    private static partial void LogListenerStartFailed(ILogger logger, int port, Exception exception);
}