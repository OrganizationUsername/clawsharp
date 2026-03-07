using Clawsharp.Cron;

namespace Clawsharp.Tests.Integration.Cron;

[TestFixture]
public sealed class SqliteCronStoreTests : CronStoreContractTests
{
    private string _dbPath = null!;

    protected override Task<ICronStore> CreateStoreAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        return Task.FromResult<ICronStore>(new SqliteCronStore(_dbPath));
    }

    protected override Task ResetAsync()
    {
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }
}