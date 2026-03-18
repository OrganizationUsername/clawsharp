using Clawsharp.Cron;
using Microsoft.Data.SqlClient;
using Respawn;
using Testcontainers.MsSql;

namespace Clawsharp.Tests.Integration.Cron;

[TestFixture]
public sealed class MssqlCronStoreTests : CronStoreContractTests
{
    private static MsSqlContainer _container = null!;

    private static string _connectionString = null!;

    private static Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task ContainerSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Initialize schema
        var store = new MssqlCronStore(_connectionString);
        await store.InitAsync();

        // Initialize Respawn
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer
        });
    }

    [OneTimeTearDown]
    public async Task ContainerTearDown()
    {
        await _container.DisposeAsync();
    }

    protected override Task<ICronStore> CreateStoreAsync()
    {
        return Task.FromResult<ICronStore>(new MssqlCronStore(_connectionString));
    }

    protected override async Task ResetAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }
}