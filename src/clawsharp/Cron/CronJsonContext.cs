using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Cron;

/// <summary>Source-generated JSON serialization context for cron job models.</summary>
[JsonSerializable(typeof(CronJob))]
[JsonSerializable(typeof(List<CronJob>))]
[JsonSerializable(typeof(CronScheduleKind))]
[JsonSerializable(typeof(CronSource))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CronJsonContext : JsonSerializerContext
{
    private static readonly Lazy<CronJsonContext> LazyWithConverters = new(() =>
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new CronScheduleKind.CronScheduleKindSystemTextJsonConverter());
        options.Converters.Add(new CronSource.CronSourceSystemTextJsonConverter());
        return new CronJsonContext(options);
    });

    /// <summary>
    /// Instance with Intellenum converters properly registered.
    /// Use this instead of Default to ensure Intellenum types serialize as string values.
    /// </summary>
    public static CronJsonContext WithConverters => LazyWithConverters.Value;
}