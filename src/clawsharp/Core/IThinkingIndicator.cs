namespace Clawsharp.Core;

/// <summary>
/// Implemented by channels that support sending a "thinking/typing" indicator to the user
/// while the agent is processing, and clearing it when done.
/// All methods are best-effort and must never throw.
/// </summary>
public interface IThinkingIndicator
{
    /// <summary>Starts the thinking indicator for the given recipient. Best-effort — never throws.</summary>
    Task StartThinkingAsync(string recipientId, CancellationToken ct = default);

    /// <summary>Stops the thinking indicator. Best-effort — never throws.</summary>
    Task StopThinkingAsync(string recipientId, CancellationToken ct = default);
}