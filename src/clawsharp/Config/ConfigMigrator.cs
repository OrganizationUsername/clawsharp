using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Config;

/// <summary>
/// Migrates legacy config JSON property names to their current equivalents before deserialization.
/// This allows users with old config files (e.g. <c>"rateLimitWindowSeconds": 60</c>) to continue
/// working without manual changes after property renames.
/// </summary>
internal static partial class ConfigMigrator
{
    /// <summary>
    /// Property renames applied during the int/double-seconds to TimeSpan migration.
    /// Each entry maps a dot-path (parent.oldName) to the new property name.
    /// The dot-path segments identify the JSON object path to the parent, and the final segment
    /// is the old property name to rename. The value is the new property name.
    /// </summary>
    private static readonly (string[] ParentPath, string OldName, string NewName, bool ConvertSecondsToTimeSpan)[] Renames =
    [
        // agents.defaults.rateLimitWindowSeconds → rateLimitWindow
        (["agents", "defaults"], "rateLimitWindowSeconds", "rateLimitWindow", true),

        // agents.defaults.healthCheck.intervalSeconds → interval
        (["agents", "defaults", "healthCheck"], "intervalSeconds", "interval", true),

        // agents.defaults.resilience.requestTimeoutSeconds → requestTimeout
        (["agents", "defaults", "resilience"], "requestTimeoutSeconds", "requestTimeout", true),

        // agents.defaults.resilience.retry.delaySeconds → delay
        (["agents", "defaults", "resilience", "retry"], "delaySeconds", "delay", true),

        // agents.defaults.resilience.retry.maxDelaySeconds → maxDelay
        (["agents", "defaults", "resilience", "retry"], "maxDelaySeconds", "maxDelay", true),

        // agents.defaults.resilience.circuitBreaker.breakDurationSeconds → breakDuration
        (["agents", "defaults", "resilience", "circuitBreaker"], "breakDurationSeconds", "breakDuration", true),

        // agents.defaults.resilience.circuitBreaker.samplingDurationSeconds → samplingDuration
        (["agents", "defaults", "resilience", "circuitBreaker"], "samplingDurationSeconds", "samplingDuration", true),

        // secrets.onePassword.timeoutSeconds → timeout
        (["secrets", "onePassword"], "timeoutSeconds", "timeout", true),

        // secrets.bitwarden.timeoutSeconds → timeout
        (["secrets", "bitwarden"], "timeoutSeconds", "timeout", true),
    ];

    /// <summary>
    /// Applies all known property renames to the raw JSON string. Returns the (possibly modified)
    /// JSON string ready for deserialization. Logs a warning for each renamed property so users
    /// know to update their config file.
    /// </summary>
    internal static string MigrateLegacyKeys(string json, ILogger logger)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch
        {
            // If parsing fails, return the original — the deserializer will report the real error.
            return json;
        }

        if (root is not JsonObject rootObj)
        {
            return json;
        }

        var migrated = false;

        foreach (var (parentPath, oldName, newName, convertSecondsToTimeSpan) in Renames)
        {
            var parent = NavigateTo(rootObj, parentPath);
            if (parent is null)
            {
                continue;
            }

            if (!parent.TryGetPropertyValue(oldName, out var value))
            {
                continue;
            }

            // Only rename if the new name doesn't already exist (user may have both).
            if (parent.TryGetPropertyValue(newName, out _))
            {
                continue;
            }

            parent.Remove(oldName);

            // When the old property held a numeric value (seconds), convert it to a
            // TimeSpan string so that IConfiguration.Bind() (which uses TimeSpan.Parse)
            // interprets it correctly. Without this, "60" would become 60 DAYS instead
            // of 60 seconds, because TimeSpan.Parse("60") parses as days.
            if (convertSecondsToTimeSpan && value is JsonValue jv && jv.TryGetValue<double>(out var seconds))
            {
                parent[newName] = TimeSpan.FromSeconds(seconds).ToString();
            }
            else
            {
                parent[newName] = value?.DeepClone();
            }

            migrated = true;

            var fullPath = string.Join(".", parentPath) + "." + oldName;
            var fullNewPath = string.Join(".", parentPath) + "." + newName;
            LogLegacyKeyRenamed(logger, fullPath, fullNewPath);
        }

        return migrated ? rootObj.ToJsonString() : json;
    }

    /// <summary>
    /// Navigates through nested <see cref="JsonObject"/>s following the given path segments.
    /// Returns <c>null</c> if any segment is missing or not an object.
    /// </summary>
    private static JsonObject? NavigateTo(JsonObject root, string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetPropertyValue(segment, out var child) || child is not JsonObject childObj)
            {
                return null;
            }

            current = childObj;
        }

        return current;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning,
        Message = "Config key '{OldPath}' is deprecated — renamed to '{NewPath}'. Please update your config.json")]
    private static partial void LogLegacyKeyRenamed(ILogger logger, string oldPath, string newPath);
}
