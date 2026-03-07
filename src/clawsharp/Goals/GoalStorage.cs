using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Goals;

public sealed partial class GoalStorage
{
    private static readonly string GoalsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clawsharp", "goals.json");

    private readonly ILogger<GoalStorage> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Cached goals list, loaded lazily on first read.</summary>
    private List<Goal>? _cached;

    public GoalStorage(ILogger<GoalStorage> logger)
    {
        _logger = logger;
    }

    /// <summary>Constructor for testing — allows injecting a custom file path.</summary>
    internal GoalStorage(string path, ILogger<GoalStorage> logger)
    {
        GoalsPathOverride = path;
        _logger = logger;
    }

    /// <summary>Override used in tests to avoid touching the real user directory.</summary>
    internal string? GoalsPathOverride { get; }

    private string EffectivePath => GoalsPathOverride ?? GoalsPath;

    public async Task<List<Goal>> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var path = EffectivePath;
            if (!File.Exists(path))
            {
                _cached = [];
                return _cached;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            _cached = JsonSerializer.Deserialize(bytes, GoalJsonContext.Default.ListGoal) ?? [];
            return _cached;
        }
        catch (Exception ex)
        {
            LogGoalLoadFailed(_logger, ex, EffectivePath);
            _cached = [];
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(List<Goal> goals, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _cached = goals;

            var path = EffectivePath;
            var dir = Path.GetDirectoryName(path);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(goals, GoalJsonContext.Default.ListGoal);
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to load goals from {Path}")]
    private static partial void LogGoalLoadFailed(ILogger logger, Exception exception, string path);
}