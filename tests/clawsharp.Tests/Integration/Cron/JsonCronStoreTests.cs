using Clawsharp.Cron;

namespace Clawsharp.Tests.Integration.Cron;

[TestFixture]
public sealed class JsonCronStoreTests : CronStoreContractTests
{
    private string _tempDir = null!;

    protected override Task<ICronStore> CreateStoreAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        return Task.FromResult<ICronStore>(new JsonCronStore(_tempDir));
    }

    protected override Task ResetAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        return Task.CompletedTask;
    }
}