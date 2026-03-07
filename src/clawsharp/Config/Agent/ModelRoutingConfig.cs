namespace Clawsharp.Config.Agent;

/// <summary>
/// Controls intelligent model routing — auto-selects a cheaper/faster model for simple messages
/// and the primary (expensive) model for complex ones, based on a complexity score.
/// </summary>
public sealed class ModelRoutingConfig
{
    /// <summary>Enable intelligent routing (default: false).</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The cheap/fast model to use for simple messages (e.g., "gpt-4o-mini").
    /// Required when <see cref="Enabled"/> is true.
    /// </summary>
    public string? SimpleModel { get; init; }

    /// <summary>
    /// Provider for the simple model, if different from the primary provider.
    /// Defaults to the primary provider when null.
    /// </summary>
    public string? SimpleProvider { get; init; }

    /// <summary>
    /// Complexity score threshold (0-100). Messages scoring below this use <see cref="SimpleModel"/>.
    /// Default: 30.
    /// </summary>
    public int Threshold { get; init; } = 30;
}