using Microsoft.Extensions.Hosting;

namespace Clawsharp.Core.Services;

/// <summary>
///     Drop-in replacement for <see cref="BackgroundService"/> that implements
///     <see cref="IHostedLifecycleService"/> for fine-grained startup/shutdown hooks.
///     Subclasses override <see cref="ExecuteAsync"/> (same as BackgroundService) and
///     optionally any lifecycle hook (<see cref="StartingAsync"/>, <see cref="StartedAsync"/>,
///     <see cref="StoppingAsync"/>, <see cref="StoppedAsync"/>).
/// </summary>
public abstract class LifecycleBackgroundService : IHostedLifecycleService, IDisposable
{
    private Task? _executeTask;

    private CancellationTokenSource? _cts;

    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    public virtual Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executeTask = ExecuteAsync(_cts.Token);
        if (_executeTask.IsCompleted)
        {
            return _executeTask;
        }

        return Task.CompletedTask;
    }

    public virtual Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executeTask == null)
        {
            return;
        }

        try
        {
            await _cts!.CancelAsync();
        }
        finally
        {
            var tcs = new TaskCompletionSource<object>();
            await using var _ = cancellationToken.Register(
                static s => ((TaskCompletionSource<object>)s!).SetCanceled(), tcs);
            await Task.WhenAny(_executeTask, tcs.Task).ConfigureAwait(false);
        }
    }

    public virtual Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}