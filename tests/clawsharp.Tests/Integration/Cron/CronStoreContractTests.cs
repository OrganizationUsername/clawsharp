using Clawsharp.Cron;

namespace Clawsharp.Tests.Integration.Cron;

/// <summary>Contract tests run against every ICronStore implementation.</summary>
[Category("Integration")]
public abstract class CronStoreContractTests
{
    protected abstract Task<ICronStore> CreateStoreAsync();

    private ICronStore _store = null!;

    [SetUp]
    public async Task BaseSetUp()
    {
        _store = await CreateStoreAsync();
        await _store.InitAsync();
    }

    protected virtual Task ResetAsync() => Task.CompletedTask;

    [TearDown]
    public virtual async Task BaseTearDown()
    {
        await ResetAsync();
    }

    private static CronJob MakeJob(string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Name = "Test Job",
        ScheduleKind = CronScheduleKind.Cron,
        ScheduleExpr = "0 9 * * *",
        Channel = "cli",
        Message = "Hello from test",
        SenderId = "cron",
        Enabled = true,
        Source = CronSource.Agent
    };

    [Test]
    public async Task LoadAllAsync_EmptyStore_ReturnsEmpty()
    {
        var jobs = await _store.LoadAllAsync();
        jobs.ShouldBeEmpty();
    }

    [Test]
    public async Task UpsertAsync_Insert_JobCanBeLoaded()
    {
        var job = MakeJob();
        await _store.UpsertAsync(job);

        var jobs = await _store.LoadAllAsync();
        jobs.Count.ShouldBe(1);
        jobs[0].Id.ShouldBe(job.Id);
        jobs[0].Name.ShouldBe(job.Name);
        jobs[0].Message.ShouldBe(job.Message);
    }

    [Test]
    public async Task UpsertAsync_Update_OverwritesExistingJob()
    {
        var job = MakeJob();
        await _store.UpsertAsync(job);

        var updated = new CronJob
        {
            Id = job.Id,
            Name = "Updated Name",
            Message = "Updated Message",
            ScheduleKind = job.ScheduleKind,
            ScheduleExpr = job.ScheduleExpr,
            Channel = job.Channel,
            SenderId = job.SenderId,
            Source = job.Source
        };
        await _store.UpsertAsync(updated);

        var jobs = await _store.LoadAllAsync();
        jobs.Count.ShouldBe(1);
        jobs[0].Name.ShouldBe("Updated Name");
        jobs[0].Message.ShouldBe("Updated Message");
    }

    [Test]
    public async Task UpsertAsync_MultipleJobs_AllPersist()
    {
        await _store.UpsertAsync(MakeJob());
        await _store.UpsertAsync(MakeJob());
        await _store.UpsertAsync(MakeJob());

        var jobs = await _store.LoadAllAsync();
        jobs.Count.ShouldBe(3);
    }

    [Test]
    public async Task DeleteAsync_ExistingJob_RemovesIt()
    {
        var job = MakeJob();
        await _store.UpsertAsync(job);

        await _store.DeleteAsync(job.Id);

        var jobs = await _store.LoadAllAsync();
        jobs.ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsync_NonExistentId_DoesNotThrow()
    {
        await Should.NotThrowAsync(() => _store.DeleteAsync("does-not-exist"));
    }

    [Test]
    public async Task DeleteAsync_OnlyDeletesTargetJob()
    {
        var job1 = MakeJob();
        var job2 = MakeJob();
        await _store.UpsertAsync(job1);
        await _store.UpsertAsync(job2);

        await _store.DeleteAsync(job1.Id);

        var jobs = await _store.LoadAllAsync();
        jobs.Count.ShouldBe(1);
        jobs[0].Id.ShouldBe(job2.Id);
    }

    [Test]
    public async Task UpdateRunStatsAsync_UpdatesLastRunAtAndRunCount()
    {
        var job = MakeJob();
        await _store.UpsertAsync(job);

        var runAt = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero);
        await _store.UpdateRunStatsAsync(job.Id, runAt, 5);

        var jobs = await _store.LoadAllAsync();
        jobs[0].LastRunAt.ShouldBe(runAt);
        jobs[0].RunCount.ShouldBe(5);
    }

    [Test]
    public async Task UpdateRunStatsAsync_NonExistentId_DoesNotThrow()
    {
        await Should.NotThrowAsync(() =>
            _store.UpdateRunStatsAsync("ghost-id", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1));
    }

    [Test]
    public async Task UpsertAsync_NullableFields_ArePreserved()
    {
        var job = new CronJob
        {
            Name = null,
            Tz = null,
            ScheduleExpr = "0 9 * * *",
            Channel = "cli",
            Message = "Hello from test",
            SenderId = "cron"
        };
        job.LastRunAt = null;
        await _store.UpsertAsync(job);

        var jobs = await _store.LoadAllAsync();
        jobs[0].Name.ShouldBeNull();
        jobs[0].Tz.ShouldBeNull();
        jobs[0].LastRunAt.ShouldBeNull();
    }

    [Test]
    public async Task UpsertAsync_AllScheduleKinds_RoundTrip()
    {
        foreach (var kind in new[] { CronScheduleKind.Cron, CronScheduleKind.Every, CronScheduleKind.At })
        {
            var job = new CronJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Test Job",
                ScheduleKind = kind,
                ScheduleExpr = "0 9 * * *",
                Channel = "cli",
                Message = "Hello from test",
                SenderId = "cron"
            };
            await _store.UpsertAsync(job);
        }

        var jobs = await _store.LoadAllAsync();
        var kindValues = jobs.Select(j => j.ScheduleKind.Value).ToHashSet();
        kindValues.ShouldContain(CronScheduleKind.Cron.Value);
        kindValues.ShouldContain(CronScheduleKind.Every.Value);
        kindValues.ShouldContain(CronScheduleKind.At.Value);
    }

    [Test]
    public async Task UpsertAsync_EnabledFalse_RoundTrips()
    {
        var job = MakeJob();
        job.Enabled = false;
        await _store.UpsertAsync(job);

        var jobs = await _store.LoadAllAsync();
        jobs[0].Enabled.ShouldBeFalse();
    }

    [Test]
    public async Task UpsertAsync_BothSources_RoundTrip()
    {
        var agentJob = MakeJob(); // Source defaults to Agent
        await _store.UpsertAsync(agentJob);

        var configJob = new CronJob
        {
            Name = "Config Job",
            ScheduleExpr = "0 9 * * *",
            Channel = "cli",
            Message = "Hello from config",
            SenderId = "cron",
            Source = CronSource.Config
        };
        await _store.UpsertAsync(configJob);

        var jobs = await _store.LoadAllAsync();
        jobs.ShouldContain(j => j.Source.Value == CronSource.Agent.Value);
        jobs.ShouldContain(j => j.Source.Value == CronSource.Config.Value);
    }
}