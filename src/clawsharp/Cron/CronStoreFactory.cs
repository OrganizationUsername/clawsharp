using Clawsharp.Config;
using Clawsharp.Config.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Cron;

public static class CronStoreFactory
{
    public static ICronStore Create(AppConfig config)
    {
        if (!MemoryBackend.TryFromValue(config.Memory.Backend, out var backend))
        {
            return new JsonCronStore(ConfigLoader.ExpandHome("~/.clawsharp"), NullLogger.Instance);
        }

        if (backend == MemoryBackend.Sqlite)
        {
            return new SqliteCronStore(Path.Combine(ConfigLoader.ExpandHome(config.Memory.Dir), "memory.db"));
        }

        if (backend == MemoryBackend.Postgres)
        {
            return new PostgresCronStore(
                config.Memory.ConnectionString
                ?? throw new InvalidOperationException("memory.connectionString is required for the 'postgres' backend."));
        }

        if (backend == MemoryBackend.MsSql)
        {
            return new MssqlCronStore(
                config.Memory.ConnectionString
                ?? throw new InvalidOperationException("memory.connectionString is required for the 'mssql' backend."));
        }

        return new JsonCronStore(ConfigLoader.ExpandHome("~/.clawsharp"), NullLogger.Instance);
    }
}