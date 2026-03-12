using System.Net;
using Clawsharp.Core.Services;

namespace Clawsharp.Channels;

/// <summary>
/// Shared webhook HTTP listener setup for LINE and Lark channels.
/// Starts an <see cref="HttpListener"/> on the configured prefix and dispatches
/// incoming requests to the abstract <see cref="HandleRequestAsync"/> method.
/// </summary>
public abstract class WebhookListenerBase : LifecycleBackgroundService
{
    /// <summary>
    /// MED-07: Maximum request body size (1 MB). Subclasses can override for specific needs.
    /// Prevents denial-of-service via oversized payloads.
    /// </summary>
    protected virtual int MaxRequestBodyBytes => 1_048_576;

    /// <summary>
    /// Returns the HTTP prefix to listen on, e.g. <c>"http://+:9090/"</c>.
    /// Called once during <see cref="ExecuteAsync"/>.
    /// </summary>
    protected abstract string GetListenerPrefix();

    /// <summary>
    /// Returns <c>true</c> if this channel is enabled and should start listening.
    /// </summary>
    protected abstract bool IsEnabled { get; }

    /// <summary>
    /// Processes a single incoming HTTP request. Implementations must set the
    /// response status code and close the response.
    /// </summary>
    protected abstract Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct);

    /// <summary>
    /// Called when the listener fails to start (e.g. port already in use).
    /// Implementations should log the error.
    /// </summary>
    protected abstract void OnListenerStartFailed(HttpListenerException ex);

    /// <summary>
    /// Called once the listener is running. Implementations should log the port/prefix.
    /// </summary>
    protected abstract void OnListenerStarted();

    /// <summary>
    /// Called when an unhandled exception occurs while processing a request.
    /// Implementations should log the error.
    /// </summary>
    protected abstract void OnRequestError(Exception ex);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        using var listener = new HttpListener();
        var prefix = GetListenerPrefix();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            OnListenerStartFailed(ex);
            return;
        }

        OnListenerStarted();

        // Cancel the listener when the host shuts down
        stoppingToken.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                /* shutting down */
            }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            try
            {
                // MED-07: Enforce request body size limit to prevent DoS via large payloads
                if (ctx.Request.ContentLength64 > MaxRequestBodyBytes)
                {
                    ctx.Response.StatusCode = 413; // Payload Too Large
                    ctx.Response.Close();
                    continue;
                }

                await HandleRequestAsync(ctx, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnRequestError(ex);
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch
                {
                    /* response stream may already be disposed if the client disconnected */
                }
            }
        }
    }
}